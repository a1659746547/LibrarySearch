$ErrorActionPreference='Stop'
$script:LibrarySearchPackageRoot=$PSScriptRoot
function Initialize-LibrarySearchConsole {
 # Console input modes belong to this console, not to the user's registry defaults.
 # Preserve every input flag except QuickEdit, and support redirected/headless runs.
 try{
  if(!('LibrarySearch.InstallerConsole' -as [type])){
   Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace LibrarySearch {
 public static class InstallerConsole {
  [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr GetStdHandle(int handle);
  [DllImport("kernel32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetConsoleMode(IntPtr handle, out uint mode);
  [DllImport("kernel32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetConsoleMode(IntPtr handle, uint mode);
 }
}
'@
  }
  $inputHandle=[LibrarySearch.InstallerConsole]::GetStdHandle(-10);[uint32]$originalMode=0
  if(![LibrarySearch.InstallerConsole]::GetConsoleMode($inputHandle,[ref]$originalMode)){return $null}
  [uint32]$installationMode=($originalMode -bor 0x80) -band (-bnot 0x40)
  if(![LibrarySearch.InstallerConsole]::SetConsoleMode($inputHandle,$installationMode)){return $null}
  return [pscustomobject]@{Handle=$inputHandle;Mode=$originalMode}
 }catch{Write-Verbose ('Console input setup unavailable: '+$_.Exception.Message);return $null}
}
function Restore-LibrarySearchConsole($Context){
 if($null -ne $Context){try{[void][LibrarySearch.InstallerConsole]::SetConsoleMode($Context.Handle,[uint32]$Context.Mode)}catch{Write-Verbose 'Console input mode could not be restored.'}}
}
function Get-LibrarySearchRelease {
 # A nested Windows PowerShell process can inherit a customized module path.
 # Load only the built-in Utility manifest if automatic discovery omitted it.
 if(!(Get-Command Import-PowerShellDataFile -ErrorAction SilentlyContinue)){
  Import-Module -Name (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -Global -Force -ErrorAction Stop
 }
 $release=Import-PowerShellDataFile -LiteralPath (Join-Path $script:LibrarySearchPackageRoot 'Release.psd1')
 if($release.Version -notmatch '^\d+\.\d+\.\d+(?:-[a-z0-9.]+)?$' -or $release.Architecture -ne 'x64'){throw 'Invalid LibrarySearch release metadata'}
 return $release
}
function Resolve-LibrarySearchPayloadPath([string]$Relative){
 $root=[IO.Path]::GetFullPath($script:LibrarySearchPackageRoot).TrimEnd('\')+'\'
 if([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative)){throw 'Payload path must be relative'}
 $path=[IO.Path]::GetFullPath((Join-Path $root $Relative))
 if(!$path.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){throw 'Payload path leaves package root'}
 return $path
}
function Get-LibrarySearchSdkRuntime([string]$Classes){
 if(!$script:SdkRuntimeCache){$script:SdkRuntimeCache=@{}}
 $hash=(Get-FileHash -LiteralPath $Classes).Hash
 if($script:SdkRuntimeCache.ContainsKey($hash)){return $script:SdkRuntimeCache[$hash]}
 # Inspect metadata in a fresh CLR process so same-name SDK assemblies cannot bind to another target.
 $quoted=$Classes.Replace("'","''")
 $probe="`$ErrorActionPreference='Stop'; try { `$refs=[Reflection.Assembly]::ReflectionOnlyLoadFrom('$quoted').GetReferencedAssemblies(); `$modern=@(`$refs|Where-Object {`$_.Name -eq 'System.Runtime'}); if(`$modern.Count -eq 1 -and `$modern[0].Version.Major -in @(6,8)){Write-Output ('net'+`$modern[0].Version.Major+'windows')}elseif(@(`$refs|Where-Object {`$_.Name -eq 'mscorlib' -and `$_.Version.Major -eq 4}).Count -eq 1){Write-Output 'netfx48'}else{throw 'Unsupported SDK CLR'} }catch{Write-Error `$_;exit 1}"
 $probe="`$ProgressPreference='SilentlyContinue';"+$probe
 $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($probe))
 $shell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
 $runtime=(& $shell -NoProfile -EncodedCommand $encoded|Out-String).Trim()
 if($LASTEXITCODE -ne 0 -or $runtime -notin @('netfx48','net6windows','net8windows')){throw '无法识别 AD 接口运行环境，已停止安装。'}
 $script:SdkRuntimeCache[$hash]=$runtime;return $runtime
}
function Get-LibrarySearchAdInfo([string]$System,[switch]$AllowSdkSnapshot){
 $systemPath=[IO.Path]::GetFullPath($System).TrimEnd('\')
 $exe=Join-Path (Split-Path $systemPath -Parent) 'X2.exe'
 foreach($file in @($exe,(Join-Path $systemPath 'Altium.Dxp.Interfaces.dll'),(Join-Path $systemPath 'Altium.Dxp.Classes.dll'))){if(!(Test-Path -LiteralPath $file -PathType Leaf)){throw ('不是有效的 AD System 目录，缺少：'+$file)}}
 $stream=[IO.File]::OpenRead($exe);$reader=New-Object IO.BinaryReader($stream)
 try{
  if($reader.ReadUInt16() -ne 0x5A4D){throw 'X2.exe is not a Windows executable'}
  $stream.Position=0x3C;$header=$reader.ReadInt32()
  if($header -lt 0 -or $header -gt $stream.Length-6){throw 'Invalid X2.exe PE header'}
  $stream.Position=$header
  if($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664){throw '此插件仅支持 Windows x64 AD，不支持 32 位或其他架构。'}
 }finally{$reader.Dispose()}
 $version=(Get-Item -LiteralPath $exe).VersionInfo
 $major=[int]$version.FileMajorPart;$fullVersion=$version.FileVersion;$versionSource='executable-resource'
 if($major -lt 1){
  # Signed AD18-21 launchers can lack VERSIONINFO. Never infer a version from folder names.
  $fingerprint=(Get-FileHash -LiteralPath $exe).Hash
  $manifestPath=Join-Path $script:LibrarySearchPackageRoot 'hosts\targets.json'
  if(Test-Path -LiteralPath $manifestPath){
   $known=Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath|ConvertFrom-Json
   $fingerprintMatches=@($known.Targets|Where-Object {$_.ApplicationSHA256 -and $_.ApplicationSHA256 -eq $fingerprint})
   if($fingerprintMatches.Count -eq 1){$major=[int]$fingerprintMatches[0].AdMajor;$fullVersion=[string]$fingerprintMatches[0].BuildAdVersion;$versionSource='known-executable-sha256'}
  }
  if($major -lt 1){
   $parent=[IO.Path]::GetFullPath((Split-Path $systemPath -Parent)).TrimEnd('\')
   foreach($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')){
    foreach($entry in (Get-ItemProperty -Path $key -ErrorAction SilentlyContinue|Where-Object {$_.DisplayName -match 'Altium Designer' -and $_.InstallLocation -and $_.DisplayVersion})){
     try{$location=[IO.Path]::GetFullPath($entry.InstallLocation).TrimEnd('\')}catch{continue}
     if([string]::Equals($location,$parent,[StringComparison]::OrdinalIgnoreCase) -and $entry.DisplayVersion -match '^(\d+)\.\d+(?:\.\d+){0,2}$'){
      $registeredMajor=[int]$Matches[1]
      if($major -gt 0 -and $major -ne $registeredMajor){throw 'Conflicting registered AD versions'}
      $major=$registeredMajor;$fullVersion=$entry.DisplayVersion;$versionSource='registered-install-location'
     }
    }
   }
  }
  if($major -lt 1 -and $AllowSdkSnapshot){
   $originPath=Join-Path (Split-Path $systemPath -Parent) 'sdk-origin.json'
   if(Test-Path -LiteralPath $originPath){
    $origin=Get-Content -Raw -Encoding UTF8 -LiteralPath $originPath|ConvertFrom-Json
    if($origin.AdVersion -notmatch '^(\d+)\.\d+\.\d+\.\d+$' -or [int]$origin.AdMajor -ne [int]$Matches[1]){throw 'SDK snapshot version is invalid'}
    foreach($required in @('X2.exe','System/Altium.Dxp.Classes.dll','System/Altium.Dxp.Interfaces.dll')){
     $proof=@($origin.files|Where-Object {$_.file -ieq $required})
     if($proof.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path (Split-Path $systemPath -Parent) $required)).Hash -ne $proof[0].sha256){throw 'SDK snapshot fingerprint mismatch'}
    }
    $major=[int]$origin.AdMajor;$fullVersion=$origin.AdVersion;$versionSource='verified-build-reference-snapshot'
   }
  }
 }
 if($major -lt 18){throw '无法可靠识别可用的 64 位 AD18+ 版本。请指定已安装 AD 的 System 目录。'}
 $runtime=Get-LibrarySearchSdkRuntime (Join-Path $systemPath 'Altium.Dxp.Classes.dll')
 return [pscustomobject]@{System=$systemPath;Application=$exe;AdMajor=$major;AdVersion=$fullVersion;Architecture='x64';ManagedRuntime=$runtime;VersionSource=$versionSource;ApplicationSHA256=(Get-FileHash -LiteralPath $exe).Hash}
}
function Get-LibrarySearchHostManifest {
 $release=Get-LibrarySearchRelease
 $path=Resolve-LibrarySearchPayloadPath 'hosts\targets.json'
 if(!(Test-Path -LiteralPath $path)){throw '未找到已构建的 AD 宿主清单，请先使用目标 AD 的 SDK 构建。'}
 $manifest=Get-Content -Raw -Encoding UTF8 -LiteralPath $path | ConvertFrom-Json
 if($manifest.SchemaVersion -notin @(1,2) -or $manifest.PluginVersion -ne $release.Version -or $manifest.Architecture -ne 'x64'){throw 'Host manifest does not match this release'}
 $binding=$release.Version
 if($manifest.SchemaVersion -eq 2){
  if($manifest.CoreBindingVersion -notmatch '^\d+\.\d+\.\d+(?:-[a-z0-9]+(?:\.[a-z0-9]+)*)?$'){throw 'Invalid adapter ABI binding version'}
  $binding=$manifest.CoreBindingVersion
  $proofPath=Resolve-LibrarySearchPayloadPath 'hosts\compatibility.json'
  if(!(Test-Path -LiteralPath $proofPath) -or (Get-FileHash -LiteralPath $proofPath).Hash -ne $manifest.CompatibilityProofSHA256){throw 'Missing verified adapter ABI report'}
  $proof=Get-Content -Raw -Encoding UTF8 -LiteralPath $proofPath|ConvertFrom-Json
  if($proof.SchemaVersion -ne 1 -or $proof.PluginVersion -ne $release.Version -or $proof.CoreBindingVersion -ne $binding -or $proof.BuildMode -ne 'unchanged-sdk-adapters-new-abi-compatible-cores'){throw 'Invalid adapter ABI report'}
 }
 $expectedCore='shared\LibrarySearch.Core-'+$binding+'.dll'
 if($manifest.CorePath -cne $expectedCore){throw 'Invalid shared assembly path'}
 $core=Resolve-LibrarySearchPayloadPath $manifest.CorePath
 if(!(Test-Path -LiteralPath $core) -or (Get-FileHash -LiteralPath $core).Hash -ne $manifest.CoreSHA256){throw 'Shared core is missing or changed; rebuild all hosts'}
 $coreSources=@($manifest.CoreSources)
 $expectedCoreSources=@('src\LibraryStore.cs','src\LibraryPanel.cs','src\ResultScrollBar.cs','src\Diagnostics.cs','src\ModelSearch.cs','src\InlineSearch.cs','src\SettingsPage.cs','src\PanelPages.cs','src\Updates.cs')
 if($coreSources.Count -ne $expectedCoreSources.Count -or @($coreSources.Path | Sort-Object -Unique).Count -ne $expectedCoreSources.Count){throw 'Shared source metadata is incomplete'}
 foreach($entry in $coreSources){
  if($entry.Path -notin $expectedCoreSources){throw 'Invalid shared source path'}
  $file=Resolve-LibrarySearchPayloadPath $entry.Path
  if(!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -ne $entry.SHA256){throw ('Shared source changed since compilation: '+$entry.Path)}
 }
 $targets=@($manifest.Targets)
 if($targets.Count -eq 0){throw 'No compiled AD hosts are available'}
 $seen=@{}
 foreach($target in $targets){
  if([string]$target.AdMajor -notmatch '^\d{1,3}$' -or [int]$target.AdMajor -lt 1){throw 'Invalid AD major in host manifest'}
  $major=[int]$target.AdMajor
  if($seen.ContainsKey($major)){throw 'Duplicate AD major in host manifest'};$seen[$major]=$true
  $hostVersion=$release.Version
  if($manifest.SchemaVersion -eq 2){if($target.HostComponentVersion -cne $binding){throw 'Unexpected frozen adapter version'};$hostVersion=$target.HostComponentVersion}
  $expectedDll='hosts\AD'+$major+'\LibrarySearch-'+$hostVersion+'-AD'+$major+'.dll'
  $expectedIns='hosts\AD'+$major+'\LibrarySearch.ins'
  if($target.Dll -cne $expectedDll -or $target.Registration -cne $expectedIns){throw 'Host payload paths do not match AD major'}
  $runtime=if($target.ManagedRuntime){$target.ManagedRuntime}else{'netfx48'}
  $selectedCore=$manifest
  if($runtime -ne 'netfx48'){
   $matches=@($manifest.RuntimeCores|Where-Object {$_.ManagedRuntime -eq $runtime})
   if($matches.Count -ne 1){throw 'Missing runtime core'};$selectedCore=$matches[0]
   $runtimeNumber=if($runtime -eq 'net6windows'){6}elseif($runtime -eq 'net8windows'){8}else{throw 'Unsupported runtime core'}
   if($selectedCore.CorePath -cne ('shared\LibrarySearch.Core-'+$binding+'-net'+$runtimeNumber+'.dll')){throw 'Invalid runtime core path'}
   $runtimeFile=Resolve-LibrarySearchPayloadPath $selectedCore.CorePath
   if(!(Test-Path -LiteralPath $runtimeFile) -or (Get-FileHash -LiteralPath $runtimeFile).Hash -ne $selectedCore.CoreSHA256){throw 'Runtime core is missing or changed'}
   if(($selectedCore.CoreSources|ConvertTo-Json -Compress) -cne ($manifest.CoreSources|ConvertTo-Json -Compress)){throw 'Runtime core sources do not match'}
  }
  if($target.CoreSHA256 -ne $selectedCore.CoreSHA256 -or ($target.CorePath -and $target.CorePath -cne $selectedCore.CorePath)){throw ('AD'+$major+' host was built with another runtime core; rebuild the matrix')}
  if($manifest.SchemaVersion -eq 2){
   $matches=@($proof.Targets|Where-Object {$_.AdMajor -eq $major})
   if($matches.Count -ne 1 -or $matches[0].HostPath -cne $target.Dll -or $matches[0].HostSHA256 -ne $target.DllSHA256 -or $matches[0].CorePath -cne $selectedCore.CorePath -or $matches[0].CoreSHA256 -ne $selectedCore.CoreSHA256 -or $matches[0].ManagedRuntime -ne $runtime -or @($matches[0].CheckedMembers).Count -lt 15){throw 'Adapter and core do not match their ABI verification'}
  }
  foreach($pair in @(@($target.Dll,$target.DllSHA256),@($target.Registration,$target.RegistrationSHA256))){
   $file=Resolve-LibrarySearchPayloadPath $pair[0]
   if(!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -ne $pair[1]){throw ('Host payload is missing or changed: '+$pair[0])}
  }
  if($target.HostSource -notin @('src\Plugin.cs',('host-source\AD'+$major+'\Plugin.cs'))){throw 'Invalid host source path'}
  $hostSource=Resolve-LibrarySearchPayloadPath $target.HostSource
  if(!(Test-Path -LiteralPath $hostSource) -or (Get-FileHash -LiteralPath $hostSource).Hash -ne $target.HostSourceSHA256){throw ('AD'+$major+' source changed since compilation')}
  $registration=Get-Content -Raw -Encoding UTF8 -LiteralPath (Resolve-LibrarySearchPayloadPath $target.Registration)
  $dllName=[IO.Path]::GetFileName($target.Dll)
  if($registration -notmatch "(?m)^\s*EditorName\s*=\s*'LibrarySearch'\s*$" -or $registration -notmatch ("(?m)^\s*EditorExePath\s*=\s*'"+[regex]::Escape($dllName)+"'\s*$")){throw 'Registration does not identify the selected host DLL'}
 }
 return $manifest
}
function Select-LibrarySearchHost($Info,$Manifest){
 $hostMatches=@($Manifest.Targets | Where-Object {[int]$_.AdMajor -eq $Info.AdMajor})
 if($hostMatches.Count -eq 1){$runtime=if($hostMatches[0].ManagedRuntime){$hostMatches[0].ManagedRuntime}else{'netfx48'};if($runtime -eq $Info.ManagedRuntime){return $hostMatches[0]}}
 $candidates=@($Manifest.Targets|Where-Object {$(if($_.ManagedRuntime){$_.ManagedRuntime}else{'netfx48'}) -eq $Info.ManagedRuntime}|Sort-Object @{Expression={[Math]::Abs([int]$_.AdMajor-[int]$Info.AdMajor)}},AdMajor)
 if($candidates.Count -eq 0){throw '没有运行环境匹配的插件，暂时无法安装此 AD 版本。'}
 return $candidates[0]
}
function Get-LibrarySearchSystem([string]$Requested,[switch]$NoDialog,[switch]$RequirePayload){
 $manifest=$null
 if($RequirePayload){$manifest=Get-LibrarySearchHostManifest}
 if($Requested){$info=Get-LibrarySearchAdInfo $Requested;if($RequirePayload){$null=Select-LibrarySearchHost $info $manifest};return $info.System}
 $candidates=New-Object 'System.Collections.Generic.List[string]'
 foreach($base in @($env:ProgramFiles,${env:ProgramFiles(x86)},'D:\Program Files')){
  if($base){$folder=Join-Path $base 'Altium';if(Test-Path -LiteralPath $folder){Get-ChildItem -LiteralPath $folder -Directory | ForEach-Object {$candidates.Add((Join-Path $_.FullName 'System'))}}}
 }
 foreach($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')){
  Get-ItemProperty -Path $key -ErrorAction SilentlyContinue | Where-Object {$_.DisplayName -match 'Altium Designer' -and $_.InstallLocation} | ForEach-Object {$candidates.Add((Join-Path $_.InstallLocation 'System'))}
 }
 $choices=@(foreach($candidate in ($candidates | Sort-Object -Unique)){
  try{$info=Get-LibrarySearchAdInfo $candidate;if($RequirePayload){$null=Select-LibrarySearchHost $info $manifest};$info.System}catch{}
 })
 if($choices.Count -eq 1){return $choices[0]}
 if($NoDialog){throw '未唯一定位目标 AD，请使用 -AltiumSystem 指定其 System 目录。'}
 Add-Type -AssemblyName System.Windows.Forms
 $dialog=New-Object System.Windows.Forms.OpenFileDialog
 $dialog.Title='请选择要安装或卸载 LibrarySearch 的 Altium Designer X2.exe';$dialog.Filter='Altium Designer (X2.exe)|X2.exe'
 try{
  if($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK){throw '已取消 AD 目录选择。'}
  $info=Get-LibrarySearchAdInfo (Join-Path (Split-Path $dialog.FileName -Parent) 'System')
  if($RequirePayload){$null=Select-LibrarySearchHost $info $manifest}
  return $info.System
 }finally{$dialog.Dispose()}
}
function Assert-LibrarySearchPayload {
 $release=Get-LibrarySearchRelease
 $nativeSystem=if([Environment]::Is64BitProcess){Join-Path $env:WINDIR 'System32'}else{Join-Path $env:WINDIR 'Sysnative'}
 if(!(Test-Path -LiteralPath (Join-Path $nativeSystem 'vcruntime140.dll'))){throw '缺少 Microsoft Visual C++ x64 运行库。请从微软官网下载并安装后重试：https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist/'}
 $manifestPath=Resolve-LibrarySearchPayloadPath 'SHA256SUMS.json'
 if(!(Test-Path -LiteralPath $manifestPath)){
  if(Test-Path -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'tools/Build-Matrix.ps1')){throw '当前是开发源码目录，不能直接安装。请使用 dist 或 releases 中完整的安装 ZIP，解压后运行其中的 Install.cmd。'}
  throw '安装包缺少 SHA256SUMS.json，请重新解压完整 ZIP。'
 }
 $entries=Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
 $entries=@($entries)
 if($entries.Count -lt 5){throw '安装包校验清单不完整。'}
 $seen=@{}
 foreach($entry in $entries){
  $file=Resolve-LibrarySearchPayloadPath $entry.Path
  if($seen.ContainsKey($file)){throw 'Duplicate payload checksum path'};$seen[$file]=$true
  if(!(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file).Hash -ne $entry.SHA256){throw ('安装包文件缺失或校验失败：'+$entry.Path)}
 }
 $hosts=Get-LibrarySearchHostManifest
 $required=@('Release.psd1','hosts\targets.json','online\bin\easyeda2altium.exe','online\bin\footprint-repair.exe','docs\使用指南.html',$hosts.CorePath)
 foreach($hostTarget in $hosts.Targets){$required+=$hostTarget.Dll;$required+=$hostTarget.Registration;$required+=$hostTarget.HostSource}
 foreach($runtimeCore in $hosts.RuntimeCores){$required+=$runtimeCore.CorePath}
 foreach($entry in $hosts.CoreSources){$required+=$entry.Path}
 foreach($path in $required){if(!$seen.ContainsKey((Resolve-LibrarySearchPayloadPath $path))){throw ('校验清单缺少必要文件：'+$path)}}
 return $entries
}
function Assert-LibrarySearchRuntime($Info){
 if($Info.ManagedRuntime -eq 'netfx48'){
  $release=Get-LibrarySearchRelease
  $runtime=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue
  if(!$runtime -or $runtime.Release -lt $release.RequiredFrameworkRelease){throw '此 AD 对应的 LibrarySearch 需要 Microsoft .NET Framework 4.8。'}
 }
 # Modern Altium hosts provide their own .NET runtime. SDKs are build tools only.
}
function Test-LibrarySearchAdmin {
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent();$principal=New-Object Security.Principal.WindowsPrincipal($identity)
 return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Initialize-LibrarySearchInstalledBinary([string]$Path,[string]$ExpectedSHA256){
 # Trust only a deployed binary whose bytes match the validated release manifest.
 # Never change AD/.NET security settings or unblock an entire directory.
 if($ExpectedSHA256 -notmatch '^[A-Fa-f0-9]{64}$' -or !(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-FileHash -LiteralPath $Path).Hash -ne $ExpectedSHA256){throw ('安装文件校验失败，未解除下载标记：'+$Path)}
 Unblock-File -LiteralPath $Path -ErrorAction Stop
 if(Get-Item -LiteralPath $Path -Stream Zone.Identifier -ErrorAction SilentlyContinue){throw ('无法清除插件文件的下载标记：'+$Path)}
 if((Get-FileHash -LiteralPath $Path).Hash -ne $ExpectedSHA256){throw ('安装文件内容发生变化：'+$Path)}
}
function Start-LibrarySearchElevated([string]$Script,[string]$System){
 $arguments=@('-NoProfile','-STA','-ExecutionPolicy','Bypass','-File',('"'+$Script+'"'),'-AltiumSystem',('"'+$System+'"'))
 $process=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
 return $process.ExitCode
}
