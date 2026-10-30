param([string]$AltiumSystem,[switch]$CheckOnly)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Installer.Common.ps1')
$consoleContext=Initialize-LibrarySearchConsole
try{
 Write-Host '正在校验 LibrarySearch 安装包，请稍候…'
 $entries=@(Assert-LibrarySearchPayload)
 $release=Get-LibrarySearchRelease
 $manifest=Get-LibrarySearchHostManifest
 Write-Host '正在查找 Altium Designer…'
 $system=Get-LibrarySearchSystem $AltiumSystem -NoDialog:$CheckOnly -RequirePayload
 $info=Get-LibrarySearchAdInfo $system
 $target=Select-LibrarySearchHost $info $manifest
 Assert-LibrarySearchRuntime $info
 if([int]$target.AdMajor -ne [int]$info.AdMajor){Write-Host ('兼容安装：AD'+$info.AdMajor+' 使用 AD'+$target.AdMajor+' 适配器，请在此版本 AD 中自行验证。') -ForegroundColor Yellow}
 if($CheckOnly){Write-Host ('包校验通过。目标 AD'+$info.AdMajor+' / '+$info.AdVersion+'；宿主：'+[IO.Path]::GetFileName($target.Dll));exit 0}
 if(Get-Process X2 -ErrorAction SilentlyContinue){throw '请先保存工程并完全关闭所有 AD 实例，再安装。安装器不会关闭 AD。'}
 if(!(Test-LibrarySearchAdmin)){exit (Start-LibrarySearchElevated $PSCommandPath $system)}
 $version=$release.Version
 $registration=Join-Path $system 'LibrarySearch.ins'
 if((Test-Path -LiteralPath $registration) -and (Get-Content -Raw -LiteralPath $registration) -notmatch "EditorName\s*=\s*'LibrarySearch'"){throw '同名注册文件属于其他插件，停止覆盖。'}
 $payload=Join-Path $system ('LibrarySearchBeta\'+$version)
 $stateFile=Join-Path $system 'LibrarySearchBeta\install-state.json'
 $previousStateBytes=if(Test-Path -LiteralPath $stateFile){[IO.File]::ReadAllBytes($stateFile)}else{$null}
 $previousState=$null
 if($previousStateBytes){$previousState=[Text.Encoding]::UTF8.GetString($previousStateBytes).TrimStart([char]0xFEFF) | ConvertFrom-Json}
 $previousRegistration=if(Test-Path -LiteralPath $registration){[IO.File]::ReadAllBytes($registration)}else{$null}
 $sameInstall=$previousState -and $previousState.Version -eq $version -and $previousState.AdMajor -eq $info.AdMajor -and $previousRegistration -and (Get-FileHash -LiteralPath $registration).Hash -eq $previousState.RegistrationHash
 $sourceHost=Resolve-LibrarySearchPayloadPath $target.Dll
 $selectedCore=if($target.CorePath){$target.CorePath}else{$manifest.CorePath}
 $sourceCore=Resolve-LibrarySearchPayloadPath $selectedCore
 $sourceRegistration=Resolve-LibrarySearchPayloadPath $target.Registration
 $hostName=[IO.Path]::GetFileName($target.Dll);$coreName=[IO.Path]::GetFileName($selectedCore)
 $hostPath=Join-Path $system $hostName;$corePath=Join-Path $system $coreName
 $oldHost=if(Test-Path -LiteralPath $hostPath){[IO.File]::ReadAllBytes($hostPath)}else{$null}
 $oldCore=if(Test-Path -LiteralPath $corePath){[IO.File]::ReadAllBytes($corePath)}else{$null}
 $backup=Join-Path $system ('LibrarySearchBackups\AD'+$info.AdMajor+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
 New-Item -ItemType Directory -Force -Path $backup | Out-Null
 if($previousRegistration){[IO.File]::WriteAllBytes((Join-Path $backup 'LibrarySearch.ins'),$previousRegistration)}
 if($previousStateBytes){[IO.File]::WriteAllBytes((Join-Path $backup 'install-state.json'),$previousStateBytes)}
 if($oldHost){[IO.File]::WriteAllBytes((Join-Path $backup $hostName),$oldHost)}
 if($oldCore){[IO.File]::WriteAllBytes((Join-Path $backup $coreName),$oldCore)}
 $runtimeEntries=@($entries | Where-Object {$_.Path -match '^(online|docs|licenses)\\' -or $_.Path -in @('README.md','THIRD-PARTY-NOTICES.md','Release.psd1')})
 if(Test-Path -LiteralPath $payload){
  foreach($entry in $runtimeEntries){$file=Join-Path $payload $entry.Path;if(!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -ne $entry.SHA256){throw '同版本运行目录内容不同，已停止覆盖。请发布新版本或保留目录后处理冲突。'}}
 }else{
  $parent=Split-Path $payload -Parent;New-Item -ItemType Directory -Force -Path $parent | Out-Null
  $stage=Join-Path $parent ('.install-'+[Guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $stage | Out-Null
  foreach($directory in @('online','docs','licenses')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $directory) -Destination $stage -Recurse}
  foreach($file in @('README.md','THIRD-PARTY-NOTICES.md','Release.psd1','SHA256SUMS.json')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $stage}
  foreach($entry in $runtimeEntries){if((Get-FileHash -LiteralPath (Join-Path $stage $entry.Path)).Hash -ne $entry.SHA256){throw ('运行文件部署校验失败：'+$entry.Path)}}
  # Both paths are fixed children of the validated AD System/LibrarySearchBeta.
  $prefix=[IO.Path]::GetFullPath($parent).TrimEnd('\')+'\'
  if(![IO.Path]::GetFullPath($stage).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFullPath($payload).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid deployment paths'}
  Move-Item -LiteralPath $stage -Destination $payload
 }
 $previousBinaries=@()
 foreach($pair in @(@($hostName,$oldHost),@($coreName,$oldCore))){if($null -ne $pair[1]){$previousBinaries+=@{Name=$pair[0];SHA256=(Get-FileHash -LiteralPath (Join-Path $backup $pair[0])).Hash}}}
 $state=[ordered]@{SchemaVersion=3;Version=$version;System=$system;AdMajor=$info.AdMajor;AdVersion=$info.AdVersion;AdapterAdMajor=$target.AdMajor;CompatibilityTrial=([int]$target.AdMajor -ne [int]$info.AdMajor);HostDll=$hostName;CoreDll=$coreName;InstalledUtc=[DateTime]::UtcNow.ToString('o');RegistrationHash=$target.RegistrationSHA256;PreviousExists=[bool]($null -ne $previousRegistration);PreviousRegistration=if($null -ne $previousRegistration){[Convert]::ToBase64String($previousRegistration)}else{''};PreviousState=if($previousStateBytes){[Convert]::ToBase64String($previousStateBytes)}else{''};Backup=$backup;PreviousBinaries=$previousBinaries}
 if($sameInstall){$state.PreviousExists=$previousState.PreviousExists;$state.PreviousRegistration=$previousState.PreviousRegistration;$state.PreviousState=$previousState.PreviousState}
 if($sameInstall -and $previousState.SchemaVersion -eq 3){$state.Backup=$previousState.Backup;$state.PreviousBinaries=@($previousState.PreviousBinaries)}
 try{
  Copy-Item -LiteralPath $sourceCore -Destination $corePath -Force
  Copy-Item -LiteralPath $sourceHost -Destination $hostPath -Force
  if((Get-FileHash -LiteralPath $corePath).Hash -ne $target.CoreSHA256 -or (Get-FileHash -LiteralPath $hostPath).Hash -ne $target.DllSHA256){throw '宿主或核心 DLL 部署校验失败。'}
  Initialize-LibrarySearchInstalledBinary $corePath $target.CoreSHA256
  Initialize-LibrarySearchInstalledBinary $hostPath $target.DllSHA256
  foreach($entry in $runtimeEntries){
   if([IO.Path]::GetExtension($entry.Path) -in @('.exe','.dll')){Initialize-LibrarySearchInstalledBinary (Join-Path $payload $entry.Path) $entry.SHA256}
  }
  Copy-Item -LiteralPath $sourceRegistration -Destination $registration -Force
  if((Get-FileHash -LiteralPath $registration).Hash -ne $target.RegistrationSHA256){throw '插件注册部署校验失败。'}
  $state | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 -LiteralPath $stateFile
 }catch{
  if($null -ne $previousRegistration){[IO.File]::WriteAllBytes($registration,$previousRegistration)}elseif(Test-Path -LiteralPath $registration){Remove-Item -LiteralPath $registration}
  if($null -ne $oldCore){[IO.File]::WriteAllBytes($corePath,$oldCore)}elseif(Test-Path -LiteralPath $corePath){Remove-Item -LiteralPath $corePath}
  if($null -ne $oldHost){[IO.File]::WriteAllBytes($hostPath,$oldHost)}elseif(Test-Path -LiteralPath $hostPath){Remove-Item -LiteralPath $hostPath}
  if($previousStateBytes){[IO.File]::WriteAllBytes($stateFile,$previousStateBytes)}elseif(Test-Path -LiteralPath $stateFile){Remove-Item -LiteralPath $stateFile}
  throw
 }
 Write-Host ('安装完成：LibrarySearch '+$version+' / AD'+$info.AdMajor) -ForegroundColor Green
 Write-Host '重新打开 AD，通过右下角 Panels → LibrarySearch 打开面板。'
 Write-Host ('备份：'+$backup)
}catch{Write-Host ('安装未完成：'+$_.Exception.Message) -ForegroundColor Red;exit 1}
finally{Restore-LibrarySearchConsole $consoleContext}
