param([Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$Live)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$release=Import-PowerShellDataFile -LiteralPath (Join-Path $root 'Release.psd1')
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$core=Join-Path $root ('shared\LibrarySearch.Core-'+$release.Version+'.dll')
Copy-Item -LiteralPath $core -Destination $output -Force
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /platform:x64 ('/out:'+(Join-Path $output 'UpdatesTests.exe')) ('/reference:'+ $core) /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'UpdatesTests.cs')
if($LASTEXITCODE -ne 0){throw 'Update test compilation failed'}
$arguments=@($output);if($Live){$arguments+='--live'}
& (Join-Path $output 'UpdatesTests.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'Update tests failed'}
