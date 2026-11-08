param([Parameter(Mandatory=$true)][string]$Core,[Parameter(Mandatory=$true)][string]$DotNet,[ValidateSet('6.0.30','8.0.15','8.0.33')][string]$RuntimeVersion='6.0.30')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=Join-Path $root ('.artifacts/runtime-dependency-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output|Out-Null
Copy-Item -LiteralPath $Core -Destination $output
$exe=Join-Path $output 'RuntimeDependencyTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /platform:x64 ('/out:'+$exe) ('/reference:'+$Core) /reference:System.Core.dll (Join-Path $root 'plugin/tests/RuntimeDependencyTests.cs')
if($LASTEXITCODE -ne 0){throw 'Dependency test compilation failed'}
$config=Join-Path $output 'RuntimeDependencyTests.runtimeconfig.json'
@{runtimeOptions=@{tfm=('net'+$RuntimeVersion.Split('.')[0]+'.0');rollForward='Disable';frameworks=@(@{name='Microsoft.NETCore.App';version=$RuntimeVersion},@{name='Microsoft.WindowsDesktop.App';version=$RuntimeVersion})}}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $config -Encoding UTF8
& $DotNet exec --runtimeconfig $config $exe (Join-Path $output 'data')
if($LASTEXITCODE -ne 0){throw 'Runtime dependency check failed'}
