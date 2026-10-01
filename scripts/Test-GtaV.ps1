[CmdletBinding()]
param([string]$AsiPath)
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $hcRoot 'modules/gta-v-enhanced/native/Test.ps1') -AsiPath $AsiPath -BuildDirectory (Join-Path $hcRoot 'artifacts/gta-tests')
