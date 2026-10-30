param([Parameter(Mandatory=$true)][string]$BuildDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent;$build=[IO.Path]::GetFullPath($BuildDirectory)
& (Join-Path $PSScriptRoot 'Test-NativeBuild.ps1') -ProjectRoot $root -BinaryDirectory (Join-Path $build 'online/bin')
$result=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $build 'matrix-result.json')|ConvertFrom-Json
$manifest=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $build 'hosts/targets.json')|ConvertFrom-Json
if(!$manifest.MatrixCompleted -or @($manifest.Targets).Count -ne @($result.AdMajors).Count){throw 'Only a completed matrix can be packaged'}
$version=$manifest.PluginVersion;$name='LibrarySearch-'+$version;$dist=Join-Path $root 'dist';$payload=Join-Path $dist $name;$zip=Join-Path $dist ($name+'.zip')
if((Test-Path -LiteralPath $payload) -or (Test-Path -LiteralPath $zip)){throw 'Output already exists; do not overwrite a released package'}
New-Item -ItemType Directory -Force -Path $payload|Out-Null
foreach($dir in @('src','licenses','online','docs','hosts','shared','host-source')){if(Test-Path -LiteralPath (Join-Path $build $dir)){Copy-Item -LiteralPath (Join-Path $build $dir) -Destination $payload -Recurse}}
foreach($file in @('Install.cmd','Install.ps1','Installer.Common.ps1','Uninstall.cmd','Uninstall.ps1','Release.psd1','README.md','THIRD-PARTY-NOTICES.md')){Copy-Item -LiteralPath (Join-Path $build $file) -Destination $payload}
$majors=@($manifest.Targets|ForEach-Object {[int]$_.AdMajor})
$readme="LibrarySearch "+$version+"`r`n`r`n多版本安装包 / Windows x64`r`n已包含对应 SDK 编译的宿主："+(($majors|ForEach-Object {'AD'+$_}) -join '、')+"。`r`n正式版。其他 AD 版本可尝试兼容安装并自行验证。`r`n`r`n1. 保存工程并完全关闭全部 AD 实例。`r`n2. 完整解压，运行 Install.cmd，选择需要安装的 AD X2.exe。`r`n3. 打开 AD，通过 Panels → LibrarySearch 验证面板。`r`n4. 多套 AD 需分别选择安装；卸载只处理所选目录。`r`n`r`n需要 .NET Framework 4.8+ 和 Microsoft Visual C++ x64 运行库。旧工程和下载的元件库保留。`r`n请核对符号、封装、引脚和工程连接；当前程序未签名，不保证安全软件不拦截。`r`n"
$readme=[regex]::Replace($readme,'已包含对应 SDK 编译的宿主：[^\r\n]*','兼容多版本 AD，安装器自动选择对应插件；其他版本可尝试兼容安装并自行验证。')
$readme=$readme.Replace('需要 .NET Framework 4.8+ 和 Microsoft Visual C++ x64 运行库。','旧接口需要 .NET Framework 4.8+；现代接口使用 AD 自带的 .NET 6/8。均需 Microsoft Visual C++ x64 运行库，客户无需 SDK。')
[IO.File]::WriteAllText((Join-Path $payload 'README.md'),$readme,[Text.UTF8Encoding]::new($false))
$guide=Join-Path $payload 'docs/使用指南.html'
$guideText=(Get-Content -Raw -Encoding UTF8 -LiteralPath $guide).Replace('1.0.1',$version).Replace('Altium Designer 24','Altium Designer（以安装包宿主清单为准）').Replace('AD24 安装目录','目标 AD 安装目录')
$guideText=[regex]::Replace($guideText,'LibrarySearch\s+\d+\.\d+\.\d+(?:-[a-z0-9]+(?:\.[a-z0-9]+)*)?',('LibrarySearch '+$version))
$guideText=[regex]::Replace($guideText,'本包包含 AD18[^<]*对应插件。','兼容多版本 AD，安装器自动选择对应插件。')
[IO.File]::WriteAllText($guide,$guideText,[Text.UTF8Encoding]::new($false))
$entries=@(Get-ChildItem -LiteralPath $payload -Recurse -File|Sort-Object FullName|ForEach-Object {[ordered]@{Path=$_.FullName.Substring($payload.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
$entries|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $payload 'SHA256SUMS.json') -Encoding UTF8
foreach($major in $result.AdMajors){
 $configuration=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $build 'hosts/targets.json')|ConvertFrom-Json
 if(@($configuration.Targets|Where-Object {$_.AdMajor -eq $major}).Count -ne 1){throw 'Missing compiled host'}
}
. (Join-Path $PSScriptRoot 'Archive.Common.ps1')
Write-LibrarySearchArchive $payload $zip ($name+'/')
$sha=(Get-FileHash -LiteralPath $zip).Hash
[IO.File]::WriteAllText(($zip+'.sha256'),$sha+'  '+[IO.Path]::GetFileName($zip)+[Environment]::NewLine,[Text.Encoding]::ASCII)
Write-Host ('Installer package: '+$zip)
Write-Host ('Contains compiled hosts: '+(@($result.AdMajors) -join ', '))
Write-Host 'Package created. This command did not install or publish anything.'
