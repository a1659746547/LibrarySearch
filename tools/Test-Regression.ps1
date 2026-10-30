param([Parameter(Mandatory=$true)][string]$BuildDirectory,[ValidateSet('netfx48','net6windows','net8windows')][string]$ManagedRuntime='netfx48',[string]$DotNet=$env:LIBRARYSEARCH_DOTNET,[string]$RuntimeVersion)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent;$build=[IO.Path]::GetFullPath($BuildDirectory)
$tests=Join-Path $root 'plugin/tests';$output=Join-Path $root ('.artifacts/regression-'+$ManagedRuntime+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6));New-Item -ItemType Directory -Force -Path $output|Out-Null
$metadata=if($ManagedRuntime -eq 'netfx48'){'build-info.json'}else{'build-info-'+$ManagedRuntime+'.json'}
$coreInfo=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $build ('shared/'+$metadata))|ConvertFrom-Json
$core=Join-Path $build $coreInfo.CorePath
$testNames=@('MultiVersionTests','UpdatesTests','JsonCompatibilityTests','CustomerFlowTests','CompatiblePayloadTests','PanelLifecycleTests','LocalSearchTests','SourceSwitchTests','ResultScrollBarTests','PanelRenderingTests');$largeFixturePath=$env:LIBRARYSEARCH_LARGE_FIXTURE;if($largeFixturePath){$testNames+='LargeLocalIndexTests'}
foreach($name in $testNames){
 $source=Join-Path $tests ($name+'.cs');$arguments=@()
 if($name -eq 'JsonCompatibilityTests'){$arguments=@((Join-Path $output 'json'))}
 if($name -eq 'UpdatesTests'){$arguments=@((Join-Path $output 'checksums'))}
 if($name -in @('CustomerFlowTests','PanelLifecycleTests','LocalSearchTests','SourceSwitchTests','ResultScrollBarTests','PanelRenderingTests')){$arguments=@((Join-Path $output $name),$build)}
 if($name -eq 'LargeLocalIndexTests'){$arguments=@((Join-Path $output $name),$build,$largeFixturePath)}
 if($name -eq 'CompatiblePayloadTests'){$arguments=@((Join-Path $output 'compatible-payload'))}
 if($ManagedRuntime -eq 'netfx48'){
  Copy-Item -LiteralPath $core -Destination $output -Force;$exe=Join-Path $output ($name+'.exe')
  & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /platform:x64 ('/out:'+$exe) ('/reference:'+$core) /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $source
  if($LASTEXITCODE -ne 0){throw 'Regression compilation failed'}
  & $exe @arguments
 }else{
  . (Join-Path $build 'Build.Modern.ps1')
  $testName=$name+'-'+$ManagedRuntime
  Build-LibrarySearchModern -DotNet $DotNet -ManagedRuntime $ManagedRuntime -AssemblyName $testName -Sources @($source) -References @($core) -Destination (Join-Path $output ($testName+'.dll')) -OutputType Exe -CopyReferences
  $number=$ManagedRuntime.Substring(3,1);$bin=Join-Path $build ('.modern-build/'+$testName+'/bin/Release/net'+$number+'.0-windows')
  Copy-Item -LiteralPath $core -Destination $bin -Force
  if($RuntimeVersion){
   if($RuntimeVersion -notmatch '^\d+\.\d+\.\d+$' -or $RuntimeVersion.Split('.')[0] -ne $number){throw 'RuntimeVersion must match the test target'}
   $configPath=Join-Path $bin ($testName+'.runtimeconfig.json');$config=Get-Content -Raw -Encoding UTF8 -LiteralPath $configPath|ConvertFrom-Json
   foreach($framework in $config.runtimeOptions.frameworks){$framework.version=$RuntimeVersion}
   $config.runtimeOptions|Add-Member -NotePropertyName rollForward -NotePropertyValue 'Disable' -Force
   [IO.File]::WriteAllText($configPath,($config|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
  }
  & $DotNet (Join-Path $bin ($testName+'.dll')) @arguments
 }
 if($LASTEXITCODE -ne 0){throw ('Regression failed: '+$name+' / '+$ManagedRuntime)}
}
Write-Host ('Regression completed: '+$ManagedRuntime+'. No AD was launched.')
