param([string[]]$SearchRoots=@(),[string]$Output)
$ErrorActionPreference='Stop'
$candidates=New-Object 'System.Collections.Generic.List[string]'
foreach($base in @($env:ProgramFiles,${env:ProgramFiles(x86)},'D:\Program Files')+$SearchRoots){
 if(!$base -or !(Test-Path -LiteralPath $base -PathType Container)){continue}
 if(Test-Path -LiteralPath (Join-Path $base 'X2.exe')){$candidates.Add((Join-Path $base 'System'))}
 if(Test-Path -LiteralPath (Join-Path $base 'Altium.Dxp.Interfaces.dll')){$candidates.Add($base)}
 $altium=if((Split-Path $base -Leaf) -eq 'Altium'){$base}else{Join-Path $base 'Altium'}
 if(Test-Path -LiteralPath $altium -PathType Container){Get-ChildItem -LiteralPath $altium -Directory|ForEach-Object {$candidates.Add((Join-Path $_.FullName 'System'))}}
 foreach($child in (Get-ChildItem -LiteralPath $base -Directory -ErrorAction SilentlyContinue|Where-Object {$_.Name -match '^AD\d|Altium'})){$candidates.Add((Join-Path $child.FullName 'System'))}
}
foreach($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')){
 Get-ItemProperty -Path $key -ErrorAction SilentlyContinue|Where-Object {$_.DisplayName -match 'Altium Designer' -and $_.InstallLocation}|ForEach-Object {$candidates.Add((Join-Path $_.InstallLocation 'System'))}
}
$project=Join-Path (Split-Path $PSScriptRoot -Parent) 'plugin'
. (Join-Path $project 'Installer.Common.ps1')
$targets=@(foreach($system in ($candidates|Sort-Object -Unique)){try{$info=Get-LibrarySearchAdInfo $system;[ordered]@{AdMajor=$info.AdMajor;AdVersion=$info.AdVersion;System=$info.System;Architecture=$info.Architecture;State='discovered-not-tested'}}catch{}})
$config=[ordered]@{SchemaVersion=1;Targets=$targets}
if($Output){$config|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $Output -Encoding UTF8}
$targets|ForEach-Object {[pscustomobject]$_}|Format-Table -AutoSize
Write-Host 'Discovery does not establish compatibility and does not install or launch AD.'
