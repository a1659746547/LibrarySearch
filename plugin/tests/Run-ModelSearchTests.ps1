param([Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$Fixtures,[switch]$Live)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$release=Import-PowerShellDataFile -LiteralPath (Join-Path $root 'Release.psd1')
$core=Join-Path $root ('shared\LibrarySearch.Core-'+$release.Version+'.dll')
Copy-Item -LiteralPath $core -Destination $output -Force
& $compiler /nologo /codepage:65001 /target:exe /platform:x64 ('/out:'+(Join-Path $output 'ModelSearchTests.exe')) ('/reference:'+ $core) /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'ModelSearchTests.cs')
if($LASTEXITCODE -ne 0){throw 'Test build failed'}
$arguments=@();if($Fixtures){$arguments+=([IO.Path]::GetFullPath($Fixtures))};if($Live){if(!$Fixtures){throw '-Live requires -Fixtures'};$arguments+='--live'}
& (Join-Path $output 'ModelSearchTests.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'Model search tests failed'}
