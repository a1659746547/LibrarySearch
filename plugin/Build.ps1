param([Parameter(Mandatory=$true)][string]$AltiumSystem,[switch]$SkipShared)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Build.Host.ps1') -AltiumSystem $AltiumSystem -SkipShared:$SkipShared
