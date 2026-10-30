param([Parameter(Mandatory=$true)][string]$Common,[Parameter(Mandatory=$true)][string]$Report,[switch]$Redirected,[string]$InstallScript,[string]$AltiumSystem)
$ErrorActionPreference='Stop'
. $Common
$checks=@();$initialContext=$null
function Check([bool]$Condition,[string]$Label){if(!$Condition){throw $Label};$script:checks+=$Label}
try{
 $initialContext=Initialize-LibrarySearchConsole
 if($Redirected){Check ($null -eq $initialContext) 'redirected input is accepted without a console';Restore-LibrarySearchConsole $null}
 else{
  Check ($null -ne $initialContext) 'native hidden console input handle available'
  $handle=$initialContext.Handle
  Restore-LibrarySearchConsole $initialContext
  [uint32]$quickMode=([uint32]$initialContext.Mode -bor 0x80 -bor 0x40)
  Check ([LibrarySearch.InstallerConsole]::SetConsoleMode($handle,$quickMode)) 'enable QuickEdit in isolated test console'
  $context=Initialize-LibrarySearchConsole
  [uint32]$actual=0
  Check ([LibrarySearch.InstallerConsole]::GetConsoleMode($handle,[ref]$actual)) 'read configured input mode'
  Check (($actual -band 0x40) -eq 0) 'QuickEdit disabled during installation'
  Check (($actual -band 0x80) -ne 0) 'extended flags enabled as required by Windows'
  Check (($actual -band (-bnot 0xC0)) -eq ($quickMode -band (-bnot 0xC0))) 'unrelated input flags preserved'
  Restore-LibrarySearchConsole $context
  Check ([LibrarySearch.InstallerConsole]::GetConsoleMode($handle,[ref]$actual) -and $actual -eq $quickMode) 'input mode restored after completion'
  for($i=0;$i -lt 3;$i++){$context=Initialize-LibrarySearchConsole;Restore-LibrarySearchConsole $context}
  Check ($true) 'repeated initialization does not redefine the native type'
  if($InstallScript){
   $ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
   & $ps -NoProfile -STA -ExecutionPolicy Bypass -File $InstallScript -AltiumSystem 'Z:\LibrarySearch-Isolated-Invalid-Target' -CheckOnly
   Check ($LASTEXITCODE -eq 1) 'installer failure retains a nonzero exit code'
   Check ([LibrarySearch.InstallerConsole]::GetConsoleMode($handle,[ref]$actual) -and $actual -eq $quickMode) 'installer failure restores the previous console mode'
   $output=& $ps -NoProfile -STA -ExecutionPolicy Bypass -File $InstallScript -AltiumSystem $AltiumSystem -CheckOnly 2>&1
   $precheckExit=$LASTEXITCODE
   $output|Out-String|Set-Content -LiteralPath ($Report+'.precheck.log') -Encoding UTF8
   Check ($precheckExit -eq 0) 'read-only successful precheck retains zero exit code'
   Check ([LibrarySearch.InstallerConsole]::GetConsoleMode($handle,[ref]$actual) -and $actual -eq $quickMode) 'successful precheck restores the previous console mode'
  }
 }
 [ordered]@{passed=$true;checks=$checks}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $Report -Encoding UTF8
}catch{[ordered]@{passed=$false;checks=$checks;error=$_.Exception.ToString()}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $Report -Encoding UTF8;exit 1}
finally{Restore-LibrarySearchConsole $initialContext}
