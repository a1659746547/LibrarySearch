param([Parameter(Mandatory=$true)][string]$PackageDirectory,[Parameter(Mandatory=$true)][string]$SdkConfig,[int[]]$AdMajors=@())
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent;$run=Join-Path $root ('.artifacts/installer-matrix-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $run|Out-Null
$fixture=Join-Path $run 'package';Copy-Item -LiteralPath $PackageDirectory -Destination $fixture -Recurse
$common=Join-Path $fixture 'Installer.Common.ps1'
# Fixture-only mocks: no real AD install, process, account or elevation is changed.
Add-Content -LiteralPath $common -Encoding UTF8 -Value "`nfunction Test-LibrarySearchAdmin { return `$true }`nfunction Get-Process { param([string]`$Name) if(`$Name -ne 'X2'){throw 'Unexpected lookup'} }"
$sum=Join-Path $fixture 'SHA256SUMS.json';$entries=Get-Content -Raw -Encoding UTF8 -LiteralPath $sum|ConvertFrom-Json
foreach($entry in $entries){if($entry.Path -eq 'Installer.Common.ps1'){$entry.SHA256=(Get-FileHash -LiteralPath $common).Hash}}
$entries|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $sum -Encoding UTF8
$configPath=[IO.Path]::GetFullPath($SdkConfig);$configRoot=Split-Path $configPath -Parent
$config=Get-Content -Raw -Encoding UTF8 -LiteralPath $configPath|ConvertFrom-Json
$manifest=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $fixture 'hosts/targets.json')|ConvertFrom-Json
$passed=0
foreach($sdk in $config.Targets){
 if($AdMajors.Count -gt 0 -and [int]$sdk.AdMajor -notin $AdMajors){continue}
 $major=[int]$sdk.AdMajor;$target=Join-Path $run ('AD'+$major+'/System');New-Item -ItemType Directory -Force -Path $target|Out-Null
 $source=if([IO.Path]::IsPathRooted($sdk.System)){$sdk.System}else{Join-Path $configRoot $sdk.System};$source=[IO.Path]::GetFullPath($source)
 Copy-Item -LiteralPath (Join-Path (Split-Path $source -Parent) 'X2.exe') -Destination (Split-Path $target -Parent)
 foreach($dll in @('Altium.Dxp.Classes.dll','Altium.Dxp.Interfaces.dll')){Copy-Item -LiteralPath (Join-Path $source $dll) -Destination $target}
 # Use real launcher bytes. No version override or cloned SDK-origin.json is used.
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'Install.ps1') -AltiumSystem $target -CheckOnly
 if($LASTEXITCODE -ne 0){throw ('AD'+$major+' detection/preflight failed')}
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'Install.ps1') -AltiumSystem $target
 if($LASTEXITCODE -ne 0){throw ('AD'+$major+' installation fixture failed')}
 $state=Get-Content -Raw -LiteralPath (Join-Path $target 'LibrarySearchBeta/install-state.json')|ConvertFrom-Json
 $reg=Get-Content -Raw -LiteralPath (Join-Path $target 'LibrarySearch.ins')
 if($state.AdMajor -ne $major -or $reg -notmatch ([regex]::Escape($state.HostDll)) -or $state.HostDll -notmatch ('-AD'+$major+'\.dll$')){throw 'Wrong version registered'}
 if(@(Get-ChildItem -LiteralPath $target -Filter 'LibrarySearch-*-AD*.dll').Count -ne 1){throw 'Extra host copied to the wrong AD'}
 $hostTarget=@($manifest.Targets|Where-Object {$_.AdMajor -eq $major})[0]
 $expectedCore=if($hostTarget.CorePath){$hostTarget.CorePath}else{$manifest.CorePath}
 if($state.CoreDll -cne [IO.Path]::GetFileName($expectedCore) -or (Get-FileHash -LiteralPath (Join-Path $target $state.CoreDll)).Hash -ne $hostTarget.CoreSHA256){throw 'Wrong runtime core deployed'}
 if(@(Get-ChildItem -LiteralPath $target -Filter 'LibrarySearch.Core-*.dll').Count -ne 1){throw 'Extra runtime core deployed'}
 if((Get-FileHash -LiteralPath (Join-Path $target $state.HostDll)).Hash -ne $hostTarget.DllSHA256){throw 'Host hash changed'}
 if(Get-Item -LiteralPath (Join-Path $target $state.HostDll) -Stream Zone.Identifier -ErrorAction SilentlyContinue){throw 'Download marker remains'}
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'Uninstall.ps1') -AltiumSystem $target
 if($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath (Join-Path $target 'LibrarySearch.ins'))){throw 'Uninstall fixture failed'}
 $passed++;Write-Host ('PASS: AD'+$major+' actual executable detection, matching host install, hash and uninstall')
}
Write-Host ('RESULT: '+$passed+' SDK targets passed isolated installer integration. Actual AD runtime acceptance remains pending.')
