param([switch]$Check,[switch]$Offline,[string]$Cargo='cargo',[string]$Toolchain='1.89.0-x86_64-pc-windows-msvc',[string]$NativeTools,[string]$TargetDir)
$ErrorActionPreference='Stop'
$repository=Split-Path $PSScriptRoot -Parent
$project=Join-Path $repository 'converter\easyeda2altium-ad21515ef0a7dd65cf0462458dc1ba8e8f47ac6d'
$cargoCommand=Get-Command $Cargo -ErrorAction Stop
$cargoExecutable=$cargoCommand.Source
$rustup=Join-Path (Split-Path $cargoExecutable -Parent) 'rustup.exe'
if(!(Test-Path -LiteralPath $rustup)){$rustup=(Get-Command rustup -ErrorAction Stop).Source}
$savedPath=$env:PATH
$savedFlags=$env:RUSTFLAGS
$savedLinker=$env:CARGO_TARGET_X86_64_PC_WINDOWS_GNU_LINKER
$savedTarget=$env:CARGO_TARGET_DIR
try{
 if($NativeTools){$nativePath=[IO.Path]::GetFullPath($NativeTools);if(!(Test-Path -LiteralPath $nativePath -PathType Container)){throw 'NativeTools must point to the MinGW-w64 / w64devkit bin directory'};$env:PATH=$nativePath+';'+$env:PATH}
 $sysroot=(& $rustup run $Toolchain rustc --print sysroot | Out-String).Trim()
 if($LASTEXITCODE -ne 0){throw 'Rust toolchain unavailable; see README.md'}
 $hostInfo=(& $rustup run $Toolchain rustc --version --verbose | Out-String)
 if($LASTEXITCODE -ne 0){throw 'Cannot query the selected Rust toolchain'}
 if($hostInfo -match 'host: x86_64-pc-windows-gnu'){
  $linker=Join-Path $sysroot 'lib\rustlib\x86_64-pc-windows-gnu\bin\self-contained\x86_64-w64-mingw32-gcc.exe'
  if(!(Test-Path -LiteralPath $linker)){throw 'Bundled GNU linker not found in the selected toolchain'}
  $env:CARGO_TARGET_X86_64_PC_WINDOWS_GNU_LINKER=$linker
  $env:RUSTFLAGS='-C link-self-contained=yes'
 }elseif($hostInfo -match 'host: x86_64-pc-windows-msvc'){
  # Requires the installed Visual Studio C++ Build Tools and Windows SDK.
  $env:RUSTFLAGS=''
 }else{throw 'Select a Windows x64 GNU or MSVC toolchain'}
 if($TargetDir){$env:CARGO_TARGET_DIR=[IO.Path]::GetFullPath($TargetDir)}
 Push-Location $project
 try{
  $arguments=@(('+'+$Toolchain),$(if($Check){'test'}else{'build'}),'--release','--locked')
  if($Offline){$arguments+='--offline'}
  & $cargoExecutable @arguments
  if($LASTEXITCODE -ne 0){throw 'Converter build or test failed'}
  if(!$Check){
   $target=if($env:CARGO_TARGET_DIR){[IO.Path]::GetFullPath($env:CARGO_TARGET_DIR)}else{Join-Path $project 'target'}
   $destination=Join-Path $repository 'plugin\online\bin'
   New-Item -ItemType Directory -Path $destination -Force | Out-Null
   foreach($binary in @('easyeda2altium.exe','footprint-repair.exe')){
    Copy-Item -LiteralPath (Join-Path $target ('release\'+$binary)) -Destination (Join-Path $destination $binary) -Force
   }
   $nativeRoot=Join-Path $repository 'converter'
   $hashes=@(Get-ChildItem -LiteralPath $nativeRoot -Recurse -File|Where-Object {($_.Extension -eq '.rs' -or $_.Name -in @('Cargo.toml','Cargo.lock')) -and $_.FullName -notmatch '[\\/](target|\.artifacts|\.git)[\\/]'}|Sort-Object FullName|ForEach-Object {[ordered]@{Path=$_.FullName.Substring($repository.Length+1).Replace('\','/');SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
   [ordered]@{SchemaVersion=1;Protocol='pinned-cad-1';Compiler=((& $rustup run $Toolchain rustc --version|Out-String).Trim());Sources=$hashes;Binaries=@('easyeda2altium.exe','footprint-repair.exe')|ForEach-Object {[ordered]@{Path=$_;SHA256=(Get-FileHash -LiteralPath (Join-Path $destination $_)).Hash}}}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $destination 'native-build-info.json') -Encoding UTF8
   Write-Host ('Converter copied to '+$destination)
  }
 }finally{Pop-Location}
}finally{$env:PATH=$savedPath;$env:RUSTFLAGS=$savedFlags;$env:CARGO_TARGET_X86_64_PC_WINDOWS_GNU_LINKER=$savedLinker;$env:CARGO_TARGET_DIR=$savedTarget}
