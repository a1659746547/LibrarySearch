param([Parameter(Mandatory=$true)][string]$SourceFixture,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$fixture=[IO.Path]::GetFullPath($SourceFixture);$outputPath=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $outputPath){throw 'Choose a new scratch output directory'}
New-Item -ItemType Directory -Force -Path $outputPath|Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$sourceNames=@('LibraryStore','LibraryPanel','Diagnostics','ModelSearch','InlineSearch','SettingsPage','PanelPages','Updates')
$sources=@();$sourceDirectory=Join-Path $outputPath 'test-source';New-Item -ItemType Directory -Path $sourceDirectory|Out-Null
$profile=Join-Path $outputPath 'isolated-profile'
foreach($name in $sourceNames){
 $text=[IO.File]::ReadAllText((Join-Path $root ('plugin/src/'+$name+'.cs')))
 if($name -eq 'LibraryStore'){
  $original='Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LibrarySearch")'
  if(!$text.Contains($original)){throw 'Could not isolate profile path for the test build'}
  $text=$text.Replace($original,('@"'+$profile.Replace('"','""')+'"'))
 }
 $target=Join-Path $sourceDirectory ($name+'.cs');[IO.File]::WriteAllText($target,$text,[Text.UTF8Encoding]::new($false));$sources+=$target
}
$core=Join-Path $outputPath 'LibrarySearch.Core-1.1.0-beta.2.dll'
$references=@('/reference:System.Core.dll','/reference:System.Web.Extensions.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll')
& $compiler /nologo /codepage:65001 /target:library /platform:x64 ('/out:'+$core) $references $sources
if($LASTEXITCODE -ne 0){throw 'Isolated core compilation failed'}
$bin=Join-Path $outputPath 'distribution/online/bin';New-Item -ItemType Directory -Force -Path $bin|Out-Null
Copy-Item -LiteralPath (Join-Path $root 'plugin/online/bin/easyeda2altium.exe') -Destination $bin
$realRepair=Join-Path $root 'plugin/online/bin/footprint-repair.exe'
foreach($pair in @(@('ContractRepairStub',(Join-Path $bin 'footprint-repair.exe')),@('ContractDescriptionStub',(Join-Path $outputPath 'describe.exe')),@('ConverterContractTests',(Join-Path $outputPath 'ConverterContractTests.exe')))){
 & $compiler /nologo /codepage:65001 /target:exe /platform:x64 ('/out:'+$pair[1]) ('/reference:'+$core) $references (Join-Path $root ('plugin/tests/'+$pair[0]+'.cs'))
 if($LASTEXITCODE -ne 0){throw ('Test compilation failed: '+$pair[0])}
}
$previous=$env:LS_CONTRACT_REPAIR
try{
 $env:LS_CONTRACT_REPAIR=$realRepair
 & (Join-Path $outputPath 'ConverterContractTests.exe') (Join-Path $outputPath 'contract') (Join-Path $bin 'easyeda2altium.exe') $fixture (Join-Path $outputPath 'describe.exe')
 if($LASTEXITCODE -ne 0){throw 'Converter contract failed'}
}finally{$env:LS_CONTRACT_REPAIR=$previous}
# Existing regression suites exercise the updated shared source and panel.
foreach($name in @('MultiVersionTests','UpdatesTests','JsonCompatibilityTests','CustomerFlowTests','ModelSearchTests')){
 $exe=Join-Path $outputPath ($name+'.exe')
 & $compiler /nologo /codepage:65001 /target:exe /platform:x64 ('/out:'+$exe) ('/reference:'+$core) $references (Join-Path $root ('plugin/tests/'+$name+'.cs'))
 if($LASTEXITCODE -ne 0){throw ('Regression compilation failed: '+$name)}
 $arguments=@();if($name -ne 'MultiVersionTests' -and $name -ne 'ModelSearchTests'){$arguments=@((Join-Path $outputPath $name))};if($name -eq 'CustomerFlowTests'){$arguments+=$root}
 & $exe @arguments
 if($LASTEXITCODE -ne 0){throw ('Regression failed: '+$name)}
}
Write-Host 'Offline converter contract and managed regression completed. No AD or real user cache was used.'
