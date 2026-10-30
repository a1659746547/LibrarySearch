param([string]$DotNet='dotnet')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
foreach($runtime in @('netfx48','net6windows','net8windows')){
 & (Join-Path $root 'plugin/Build.Shared.ps1') -ManagedRuntime $runtime -CoreBindingVersion '1.1.0-beta.2' -DotNet $DotNet
}
