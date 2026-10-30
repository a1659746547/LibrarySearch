param([Parameter(Mandatory=$true)][string]$Config)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent;$project=Join-Path $root 'plugin'
& (Join-Path $PSScriptRoot 'Test-NativeBuild.ps1') -ProjectRoot $root
. (Join-Path $project 'Installer.Common.ps1')
$configPath=[IO.Path]::GetFullPath($Config);$configRoot=Split-Path $configPath -Parent
$data=Get-Content -Raw -Encoding UTF8 -LiteralPath $configPath|ConvertFrom-Json
if($data.SchemaVersion -ne 1 -or @($data.Targets).Count -eq 0){throw 'Supply at least one real installed AD System in the target configuration'}
$targets=New-Object 'System.Collections.Generic.List[object]';$seen=@{}
foreach($entry in $data.Targets){
 if([string]$entry.AdMajor -notmatch '^\d{1,3}$' -or [int]$entry.AdMajor -lt 18){throw 'This matrix accepts Windows x64 AD18+ only; provide a real SDK target'}
 $system=[string]$entry.System;if(![IO.Path]::IsPathRooted($system)){$system=Join-Path $configRoot $system}
 $info=Get-LibrarySearchAdInfo $system -AllowSdkSnapshot
 if($info.AdMajor -ne [int]$entry.AdMajor){throw ('Configured AD'+$entry.AdMajor+' does not match actual X2.exe '+$info.AdVersion)}
 if($seen.ContainsKey($info.AdMajor)){throw ('More than one SDK configured for AD'+$info.AdMajor+'. Choose a deliberate baseline for that major.')}
 $seen[$info.AdMajor]=$true;$targets.Add($info)
}
# Build in a new isolated source snapshot. Failed targets never enter a distributable matrix.
$run=Join-Path $root ('.artifacts/matrix-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Force -Path $run|Out-Null
foreach($name in @('src','licenses','online','docs','host-source')){if(Test-Path -LiteralPath (Join-Path $project $name)){Copy-Item -LiteralPath (Join-Path $project $name) -Destination $run -Recurse}}
foreach($name in @('Build.Shared.ps1','Build.Host.ps1','Build.Modern.ps1','Installer.Common.ps1','Release.psd1','LibrarySearch.ins.template','Install.ps1','Install.cmd','Uninstall.ps1','Uninstall.cmd','README.md','THIRD-PARTY-NOTICES.md')){Copy-Item -LiteralPath (Join-Path $project $name) -Destination $run}
& (Join-Path $run 'Build.Shared.ps1')
foreach($runtime in @($targets.ManagedRuntime|Sort-Object -Unique|Where-Object {$_ -ne 'netfx48'})){& (Join-Path $run 'Build.Shared.ps1') -ManagedRuntime $runtime}
foreach($target in $targets){& (Join-Path $run 'Build.Host.ps1') -AltiumSystem $target.System -SkipShared -AllowSdkSnapshot}
$manifest=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $run 'hosts/targets.json')|ConvertFrom-Json
if(@($manifest.Targets).Count -ne $targets.Count){throw 'Built host matrix is incomplete'}
$manifest|Add-Member -NotePropertyName MatrixCompleted -NotePropertyValue $true
$manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $run 'hosts/targets.json') -Encoding UTF8
[ordered]@{SchemaVersion=1;PluginVersion=$manifest.PluginVersion;BuildDirectory=$run;AdMajors=@($targets|ForEach-Object {$_.AdMajor});Acceptance='compiled-only-not-AD-tested'}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $run 'matrix-result.json') -Encoding UTF8
Write-Host ('Completed matrix: '+(@($targets|ForEach-Object {'AD'+$_.AdMajor}) -join ', '))
Write-Host ('Build directory: '+$run)
Write-Host 'No AD installation was changed. Compilation is not runtime acceptance.'
