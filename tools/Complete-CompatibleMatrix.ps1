param([Parameter(Mandatory=$true)][string]$MetadataLibraries,[Parameter(Mandatory=$true)][string]$Python)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent;$plugin=Join-Path $root 'plugin'
& (Join-Path $PSScriptRoot 'Test-NativeBuild.ps1') -ProjectRoot $root
& $Python (Join-Path $PSScriptRoot 'verify_adapter_abi.py') --root $root --metadata-libs $MetadataLibraries
if($LASTEXITCODE -ne 0){throw 'Adapter ABI verification failed'}
$baseline=Join-Path $root 'compatibility/adapter-baseline.json'
if(!(Test-Path -LiteralPath $baseline)){throw 'Missing immutable original adapter matrix: compatibility/adapter-baseline.json'}
$manifest=Get-Content -Raw -Encoding UTF8 -LiteralPath $baseline|ConvertFrom-Json
if($manifest.SchemaVersion -ne 1 -or $manifest.PluginVersion -ne '1.1.0-beta.2'){throw 'Supply the original verified adapter matrix, not a renamed package'}
$release=Import-PowerShellDataFile -LiteralPath (Join-Path $plugin 'Release.psd1')
$framework=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $plugin 'shared/build-info.json')|ConvertFrom-Json
$modern=@('net6windows','net8windows')|ForEach-Object {Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $plugin ('shared/build-info-'+$_+'.json'))|ConvertFrom-Json}
foreach($core in @($framework)+$modern){if($core.PluginVersion -ne $release.Version){throw 'Stale shared core version'}}
foreach($hostTarget in $manifest.Targets){
 $core=if($hostTarget.ManagedRuntime -eq 'netfx48'){$framework}else{@($modern|Where-Object {$_.ManagedRuntime -eq $hostTarget.ManagedRuntime})[0]}
 $hostTarget|Add-Member -NotePropertyName HostComponentVersion -NotePropertyValue $manifest.PluginVersion
 $hostTarget|Add-Member -NotePropertyName AdapterBuildCoreSHA256 -NotePropertyValue $hostTarget.CoreSHA256
 $hostTarget.CoreSHA256=$core.CoreSHA256;$hostTarget.CorePath=$core.CorePath
}
$manifest.SchemaVersion=2;$manifest.PluginVersion=$release.Version;$manifest.CorePath=$framework.CorePath;$manifest.CoreSHA256=$framework.CoreSHA256;$manifest.CoreSources=$framework.CoreSources;$manifest.RuntimeCores=$modern
$manifest|Add-Member -NotePropertyName CoreBindingVersion -NotePropertyValue '1.1.0-beta.2'
$manifest|Add-Member -NotePropertyName CompatibilityProofSHA256 -NotePropertyValue (Get-FileHash -LiteralPath (Join-Path $plugin 'hosts/compatibility.json')).Hash
$manifest.MatrixCompleted=$true
$manifest|Add-Member -NotePropertyName BuildMode -NotePropertyValue 'unchanged-sdk-adapters-new-abi-compatible-cores'
$manifest|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $plugin 'hosts/targets.json') -Encoding UTF8
. (Join-Path $plugin 'Installer.Common.ps1')
$verified=Get-LibrarySearchHostManifest
$run=Join-Path $root ('.artifacts/compatible-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $run|Out-Null
foreach($dir in @('src','licenses','online','docs','hosts','shared','host-source')){if(Test-Path -LiteralPath (Join-Path $plugin $dir)){Copy-Item -LiteralPath (Join-Path $plugin $dir) -Destination $run -Recurse}}
foreach($file in @('Build.Shared.ps1','Build.Host.ps1','Build.Modern.ps1','Installer.Common.ps1','Release.psd1','LibrarySearch.ins.template','Install.ps1','Install.cmd','Uninstall.ps1','Uninstall.cmd','README.md','THIRD-PARTY-NOTICES.md')){Copy-Item -LiteralPath (Join-Path $plugin $file) -Destination $run}
[ordered]@{SchemaVersion=1;PluginVersion=$release.Version;BuildDirectory=$run;AdMajors=@($verified.Targets.AdMajor);Acceptance='adapter-ABI-verified-real-AD-acceptance-pending';BuildMode=$manifest.BuildMode}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $run 'matrix-result.json') -Encoding UTF8
Write-Host ('Completed compatible matrix: '+$run)
