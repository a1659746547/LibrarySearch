param([ValidateSet('netfx48','net6windows','net8windows')][string]$ManagedRuntime='netfx48',[string]$DotNet=$env:LIBRARYSEARCH_DOTNET,[string]$CoreBindingVersion)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Installer.Common.ps1')
$release=Get-LibrarySearchRelease
$version=$release.Version
if(!$CoreBindingVersion){$CoreBindingVersion=$version}
if($CoreBindingVersion -notmatch '^\d+\.\d+\.\d+(?:-[a-z0-9]+(?:\.[a-z0-9]+)*)?$'){throw 'Invalid core binding version'}
$sources=@('src\LibraryStore.cs','src\LibraryPanel.cs','src\ResultScrollBar.cs','src\Diagnostics.cs','src\ModelSearch.cs','src\InlineSearch.cs','src\SettingsPage.cs','src\PanelPages.cs','src\Updates.cs')
if((Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot 'src\LibraryStore.cs')) -notmatch ('Version="'+[regex]::Escape($version)+'"')){throw 'Release.psd1 and ReleaseInfo.Version must match'}
$output=Join-Path $PSScriptRoot 'shared'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$coreName='LibrarySearch.Core-'+$CoreBindingVersion+'.dll'
if($ManagedRuntime -ne 'netfx48'){$coreName='LibrarySearch.Core-'+$CoreBindingVersion+'-net'+$ManagedRuntime.Substring(3,1)+'.dll'}
$core=Join-Path $output $coreName
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$absoluteSources=@($sources | ForEach-Object {Join-Path $PSScriptRoot $_})
if($ManagedRuntime -eq 'netfx48'){
 & $compiler /nologo /codepage:65001 /target:library /platform:x64 /optimize+ "/out:$core" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $absoluteSources
 if($LASTEXITCODE -ne 0){throw 'Shared core compilation failed'}
}else{
 . (Join-Path $PSScriptRoot 'Build.Modern.ps1')
 Build-LibrarySearchModern -DotNet $DotNet -ManagedRuntime $ManagedRuntime -AssemblyName ([IO.Path]::GetFileNameWithoutExtension($coreName)) -Sources $absoluteSources -References @() -Destination $core
}
$sourceHashes=@($sources | ForEach-Object {[ordered]@{Path=$_;SHA256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_)).Hash}})
$metadata=if($ManagedRuntime -eq 'netfx48'){'build-info.json'}else{'build-info-'+$ManagedRuntime+'.json'}
[ordered]@{PluginVersion=$version;Architecture='x64';ManagedRuntime=$ManagedRuntime;CorePath=('shared\'+$coreName);CoreSHA256=(Get-FileHash -LiteralPath $core).Hash;CoreSources=$sourceHashes} | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $output $metadata)
Write-Host ('Shared core compiled: '+$coreName)
