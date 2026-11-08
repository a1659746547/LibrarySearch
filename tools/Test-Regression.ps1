param([Parameter(Mandatory=$true)][string]$BuildDirectory,[ValidateSet('netfx48','net6windows','net8windows')][string]$ManagedRuntime='netfx48',[string]$DotNet=$env:LIBRARYSEARCH_DOTNET,[string]$RuntimeVersion,[switch]$Headless=$true)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent;$build=[IO.Path]::GetFullPath($BuildDirectory)
$tests=Join-Path $root 'plugin/tests';$output=Join-Path $root ('.artifacts/regression-'+$ManagedRuntime+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6));New-Item -ItemType Directory -Force -Path $output|Out-Null
$metadata=if($ManagedRuntime -eq 'netfx48'){'build-info.json'}else{'build-info-'+$ManagedRuntime+'.json'}
$coreInfo=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $build ('shared/'+$metadata))|ConvertFrom-Json
$core=Join-Path $build $coreInfo.CorePath
$testNames=@('MultiVersionTests','UpdatesTests','JsonCompatibilityTests','OnlineSearchCacheTests','CustomerFlowTests','CompatiblePayloadTests','PanelLifecycleTests','LocalSearchTests','SourceSwitchTests','ResultScrollBarTests','PanelRenderingTests');$largeFixturePath=$env:LIBRARYSEARCH_LARGE_FIXTURE;if($largeFixturePath){$testNames+='LargeLocalIndexTests'}
$unifiedFixture=$env:LIBRARYSEARCH_UNIFIED_FIXTURE;if($unifiedFixture){$testNames+='UnifiedLibraryTests'}
$testNames+='NativeRefreshTests'
$testNames+='Default3dTests'
$recoveryFixture=$env:LIBRARYSEARCH_RECOVERY_FIXTURE;if($recoveryFixture){$testNames+='UnifiedRecoveryTests'}
if($Headless){$testNames=@($testNames|Where-Object {$_ -in @('MultiVersionTests','UpdatesTests','JsonCompatibilityTests','OnlineSearchCacheTests','CompatiblePayloadTests','Default3dTests','UnifiedRecoveryTests')});Write-Host 'Headless checks only: no forms are shown.'}
foreach($name in $testNames){
 $source=Join-Path $tests ($name+'.cs');$arguments=@()
 if($name -eq 'JsonCompatibilityTests'){$arguments=@((Join-Path $output 'json'))}
 if($name -eq 'UpdatesTests'){$arguments=@((Join-Path $output 'checksums'))}
 if($name -in @('CustomerFlowTests','PanelLifecycleTests','LocalSearchTests','SourceSwitchTests','ResultScrollBarTests','PanelRenderingTests')){$arguments=@((Join-Path $output $name),$build)}
 if($name -eq 'LargeLocalIndexTests'){$arguments=@((Join-Path $output $name),$build,$largeFixturePath)}
 if($name -eq 'UnifiedLibraryTests'){$arguments=@((Join-Path $output $name),$build,$unifiedFixture)}
 if($name -eq 'Default3dTests'){$arguments=@((Join-Path $output $name),$build)}
 if($name -eq 'NativeRefreshTests'){
  $stubFolder=Join-Path $output 'sdk-stub';New-Item -ItemType Directory -Force -Path $stubFolder|Out-Null;$stub=Join-Path $stubFolder 'Altium.Dxp.Classes.dll'
  & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library ('/out:'+$stub) (Join-Path $tests 'NativeRefreshSdkStub.cs')
  if($LASTEXITCODE -ne 0){throw 'SDK stub compilation failed'}
  $arguments=@((Join-Path $output $name),$build,$stub)
 }
 if($name -eq 'UnifiedRecoveryTests'){$arguments=@((Join-Path $output $name),$build,$recoveryFixture)}
 if($name -eq 'CompatiblePayloadTests'){$arguments=@((Join-Path $output 'compatible-payload'))}
 if($ManagedRuntime -eq 'netfx48'){
  Copy-Item -LiteralPath $core -Destination $output -Force;$exe=Join-Path $output ($name+'.exe')
  & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /platform:x64 ('/out:'+$exe) ('/reference:'+$core) /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $source
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
 if($name -eq 'CustomerFlowTests'){
  $marker=Join-Path $arguments[0] 'completed.txt'
  if(!(Test-Path -LiteralPath $marker)){throw 'Customer flow tests did not reach completion'}
 }
 if($LASTEXITCODE -ne 0){throw ('Regression failed: '+$name+' / '+$ManagedRuntime)}
}
Write-Host ('Regression completed: '+$ManagedRuntime+'. No AD was launched.')
