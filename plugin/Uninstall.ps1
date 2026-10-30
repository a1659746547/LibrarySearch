param([string]$AltiumSystem,[switch]$CheckOnly)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Installer.Common.ps1')
$consoleContext=Initialize-LibrarySearchConsole
try{
 Write-Host '正在检查 LibrarySearch 安装记录，请稍候…'
 $release=Get-LibrarySearchRelease
 $system=Get-LibrarySearchSystem $AltiumSystem -NoDialog:$CheckOnly
 $info=Get-LibrarySearchAdInfo $system
 $stateFile=Join-Path $system 'LibrarySearchBeta\install-state.json'
 if(!(Test-Path -LiteralPath $stateFile)){throw '未找到此目录的安装记录。'}
 $currentStateBytes=[IO.File]::ReadAllBytes($stateFile)
 $state=Get-Content -Raw -Encoding UTF8 -LiteralPath $stateFile | ConvertFrom-Json
 if($state.SchemaVersion -notin @(2,3) -or $state.Version -ne $release.Version -or $state.AdMajor -ne $info.AdMajor -or ![string]::Equals($state.System,$system,[StringComparison]::OrdinalIgnoreCase)){throw '安装记录与当前插件版本、AD 版本或目录不符。'}
 $registration=Join-Path $system 'LibrarySearch.ins'
 if(!(Test-Path -LiteralPath $registration) -or (Get-FileHash -LiteralPath $registration).Hash -ne $state.RegistrationHash){throw '插件注册文件已变化，请检查备份；本次不覆盖。'}
 $currentRegistration=[IO.File]::ReadAllBytes($registration)
 $previousRegistration=if($state.PreviousExists){[Convert]::FromBase64String($state.PreviousRegistration)}else{$null}
 $previousState=if($state.PreviousState){[Convert]::FromBase64String($state.PreviousState)}else{$null}
 if($previousState){
  $previousInfo=[Text.Encoding]::UTF8.GetString($previousState).TrimStart([char]0xFEFF) | ConvertFrom-Json
  if(![string]::Equals($previousInfo.System,$system,[StringComparison]::OrdinalIgnoreCase)){throw '旧安装记录目录不匹配，停止回退。'}
 }
 $restore=@()
 if($state.SchemaVersion -eq 3 -and @($state.PreviousBinaries).Count -gt 0){
  $boundary=[IO.Path]::GetFullPath((Join-Path $system 'LibrarySearchBackups')).TrimEnd('\')+'\'
  $backup=[IO.Path]::GetFullPath($state.Backup)
  if(!$backup.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw '旧程序备份路径不在本 AD 目录内。'}
  foreach($entry in $state.PreviousBinaries){
   if($entry.Name -notin @($state.HostDll,$state.CoreDll) -or [IO.Path]::GetFileName($entry.Name) -cne $entry.Name){throw '旧程序备份名称无效。'}
   $saved=Join-Path $backup $entry.Name;$current=Join-Path $system $entry.Name
   if(!(Test-Path -LiteralPath $saved) -or (Get-FileHash -LiteralPath $saved).Hash -ne $entry.SHA256){throw '旧程序备份校验失败，停止回退。'}
   $restore+=@{Path=$current;Previous=[IO.File]::ReadAllBytes($saved);Current=if(Test-Path -LiteralPath $current){[IO.File]::ReadAllBytes($current)}else{$null}}
  }
 }
 if($CheckOnly){Write-Host ('AD'+$info.AdMajor+' 回退条件检查通过。');exit 0}
 if(Get-Process X2 -ErrorAction SilentlyContinue){throw '请先保存工程并完全关闭所有 AD 实例。'}
 if(!(Test-LibrarySearchAdmin)){exit (Start-LibrarySearchElevated $PSCommandPath $system)}
 try{
  foreach($item in $restore){[IO.File]::WriteAllBytes($item.Path,$item.Previous)}
  if($state.PreviousExists){[IO.File]::WriteAllBytes($registration,$previousRegistration)}else{Remove-Item -LiteralPath $registration}
  if($previousState){[IO.File]::WriteAllBytes($stateFile,$previousState)}else{Remove-Item -LiteralPath $stateFile}
 }catch{foreach($item in $restore){if($null -ne $item.Current){[IO.File]::WriteAllBytes($item.Path,$item.Current)}};[IO.File]::WriteAllBytes($registration,$currentRegistration);[IO.File]::WriteAllBytes($stateFile,$currentStateBytes);throw}
 Write-Host '已恢复安装前的插件注册和安装记录。重新打开 AD 后生效。'
 Write-Host '各 AD 安装目录独立处理；用户工程、元件库、日志及程序备份均保留。'
}catch{Write-Host ('未完成回退：'+$_.Exception.Message) -ForegroundColor Red;exit 1}
finally{Restore-LibrarySearchConsole $consoleContext}
