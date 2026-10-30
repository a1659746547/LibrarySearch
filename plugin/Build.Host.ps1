param([Parameter(Mandatory=$true)][string]$AltiumSystem,[switch]$SkipShared,[switch]$AllowSdkSnapshot,[string]$DotNet=$env:LIBRARYSEARCH_DOTNET)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Installer.Common.ps1')
$release=Get-LibrarySearchRelease
$info=Get-LibrarySearchAdInfo $AltiumSystem -AllowSdkSnapshot:$AllowSdkSnapshot
if(!$SkipShared){
 & (Join-Path $PSScriptRoot 'Build.Shared.ps1') -DotNet $DotNet
 if($info.ManagedRuntime -ne 'netfx48'){& (Join-Path $PSScriptRoot 'Build.Shared.ps1') -ManagedRuntime $info.ManagedRuntime -DotNet $DotNet}
}
$metadata=if($info.ManagedRuntime -eq 'netfx48'){'build-info.json'}else{'build-info-'+$info.ManagedRuntime+'.json'}
$shared=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot ('shared\'+$metadata)) | ConvertFrom-Json
if($shared.PluginVersion -ne $release.Version){throw 'Shared build metadata is stale'}
$core=Resolve-LibrarySearchPayloadPath $shared.CorePath
if((Get-FileHash -LiteralPath $core).Hash -ne $shared.CoreSHA256){throw 'Shared DLL changed after compilation'}
foreach($entry in $shared.CoreSources){if((Get-FileHash -LiteralPath (Resolve-LibrarySearchPayloadPath $entry.Path)).Hash -ne $entry.SHA256){throw 'Shared source changed; rebuild the core'}}
$major=$info.AdMajor;$version=$release.Version
$relative='hosts\AD'+$major
$output=Join-Path $PSScriptRoot $relative
New-Item -ItemType Directory -Force -Path $output | Out-Null
$hostSource='host-source\AD'+$major+'\Plugin.cs'
if(!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $hostSource))){$hostSource='src\Plugin.cs'}
$dllName='LibrarySearch-'+$version+'-AD'+$major+'.dll'
$dll=Join-Path $output $dllName
$bootstrap=Join-Path $output 'HostRuntime.g.cs'
$sdkVersion=$info.AdVersion.Replace('\','\\').Replace('"','\"')
$bootstrapSource='namespace LibrarySearch { internal static class HostRuntime { internal const string CoreFileName = "'+[IO.Path]::GetFileName($shared.CorePath)+'"; internal const int AdMajor = '+$major+'; internal const string SdkVersion = "'+$sdkVersion+'"; } }'
[IO.File]::WriteAllText($bootstrap,$bootstrapSource,[Text.UTF8Encoding]::new($false))
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if($info.ManagedRuntime -eq 'netfx48'){
 & $compiler /nologo /codepage:65001 /target:library /platform:x64 /optimize+ "/out:$dll" "/reference:$core" "/reference:$($info.System)\Altium.Dxp.Classes.dll" "/reference:$($info.System)\Altium.Dxp.Interfaces.dll" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll (Join-Path $PSScriptRoot $hostSource) $bootstrap
 if($LASTEXITCODE -ne 0){throw ('AD'+$major+' host compilation failed; no new manifest entry will be published')}
}else{
 . (Join-Path $PSScriptRoot 'Build.Modern.ps1')
 Build-LibrarySearchModern -DotNet $DotNet -ManagedRuntime $info.ManagedRuntime -AssemblyName ([IO.Path]::GetFileNameWithoutExtension($dllName)) -Sources @((Join-Path $PSScriptRoot $hostSource),$bootstrap) -References @($core,(Join-Path $info.System 'Altium.Dxp.Classes.dll'),(Join-Path $info.System 'Altium.Dxp.Interfaces.dll')) -Destination $dll
}
$registration=(Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot 'LibrarySearch.ins.template')).Replace('__HOST_DLL__',$dllName).Replace('__PLUGIN_VERSION__',$version)
[IO.File]::WriteAllText((Join-Path $output 'LibrarySearch.ins'),$registration,[Text.UTF8Encoding]::new($false))
$target=[ordered]@{AdMajor=$major;BuildAdVersion=$info.AdVersion;Dll=($relative+'\'+$dllName);DllSHA256=(Get-FileHash -LiteralPath $dll).Hash;Registration=($relative+'\LibrarySearch.ins');RegistrationSHA256=(Get-FileHash -LiteralPath (Join-Path $output 'LibrarySearch.ins')).Hash;CoreSHA256=$shared.CoreSHA256;HostSource=$hostSource;HostSourceSHA256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $hostSource)).Hash;SdkClassesVersion=(Get-Item -LiteralPath (Join-Path $info.System 'Altium.Dxp.Classes.dll')).VersionInfo.FileVersion;SdkClassesSHA256=(Get-FileHash -LiteralPath (Join-Path $info.System 'Altium.Dxp.Classes.dll')).Hash;SdkInterfacesVersion=(Get-Item -LiteralPath (Join-Path $info.System 'Altium.Dxp.Interfaces.dll')).VersionInfo.FileVersion;SdkInterfacesSHA256=(Get-FileHash -LiteralPath (Join-Path $info.System 'Altium.Dxp.Interfaces.dll')).Hash;Acceptance='not-tested'}
$manifestPath=Join-Path $PSScriptRoot 'hosts\targets.json'
$targets=@()
if(Test-Path -LiteralPath $manifestPath){
 $previous=Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
 if($previous.PluginVersion -eq $version){$targets=@($previous.Targets | Where-Object {[int]$_.AdMajor -ne $major})}
}
$target.ApplicationSHA256=$info.ApplicationSHA256
$target.VersionSource=$info.VersionSource
$target.ManagedRuntime=$info.ManagedRuntime;$target.CorePath=$shared.CorePath
$targets+=([pscustomobject]$target)
$legacy=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot 'shared/build-info.json')|ConvertFrom-Json
$runtimeCores=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'shared') -Filter 'build-info-net*windows.json'|ForEach-Object {Get-Content -Raw -Encoding UTF8 -LiteralPath $_.FullName|ConvertFrom-Json})
[ordered]@{SchemaVersion=1;PluginVersion=$version;Architecture='x64';CorePath=$legacy.CorePath;CoreSHA256=$legacy.CoreSHA256;CoreSources=@($legacy.CoreSources);RuntimeCores=$runtimeCores;Targets=@($targets | Sort-Object AdMajor)} | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -LiteralPath $manifestPath
Write-Host ('AD'+$major+' host compiled against '+$info.AdVersion+'; not installed or tested.')
