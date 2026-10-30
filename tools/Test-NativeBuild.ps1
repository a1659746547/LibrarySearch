param([string]$ProjectRoot=(Split-Path $PSScriptRoot -Parent),[string]$BinaryDirectory)
$ErrorActionPreference='Stop'
$ProjectRoot=[IO.Path]::GetFullPath($ProjectRoot)
if(!$BinaryDirectory){$BinaryDirectory=Join-Path $ProjectRoot 'plugin/online/bin'}
$manifest=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $BinaryDirectory 'native-build-info.json')|ConvertFrom-Json
if($manifest.SchemaVersion -ne 1 -or $manifest.Protocol -ne 'pinned-cad-1' -or @($manifest.Sources).Count -lt 10){throw 'Build the full converter pair with tools/Build-Converter.ps1 first'}
foreach($item in $manifest.Sources){
 if($item.Path -notmatch '^converter/' -or $item.Path -match '(^|/)\.\.(/|$)'){throw 'Invalid converter source manifest path'}
 if((Get-FileHash -LiteralPath (Join-Path $ProjectRoot $item.Path)).Hash -ne $item.SHA256){throw ('Native source changed after build: '+$item.Path)}
}
if((@($manifest.Binaries.Path|Sort-Object) -join ',') -ne 'easyeda2altium.exe,footprint-repair.exe'){throw 'Both native executables must be built together'}
foreach($item in $manifest.Binaries){if((Get-FileHash -LiteralPath (Join-Path $BinaryDirectory $item.Path)).Hash -ne $item.SHA256){throw ('Native binary does not match source build: '+$item.Path)}}
Write-Host 'Verified complete converter source and both native executables.'
