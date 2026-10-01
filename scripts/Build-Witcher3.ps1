[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcSource = Join-Path $hcRoot 'modules/witcher-3/happycheats.ws'
$hcPayload = Join-Path $hcRoot 'payload/witcher-3'
New-Item -ItemType Directory -Force -Path $hcPayload | Out-Null
Copy-Item -LiteralPath $hcSource -Destination (Join-Path $hcPayload 'happycheats.ws') -Force
@{ schemaVersion=1; game='witcher-3'; version='0.1.1'; files=@(@{path='happycheats.ws'; sha256=(Get-FileHash -LiteralPath $hcSource -Algorithm SHA256).Hash}) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $hcPayload 'package.json') -Encoding utf8NoBOM
& (Join-Path $PSScriptRoot 'Test-Witcher3.ps1')
Write-Host 'Built Witcher 3 source payload 0.1.1. The game compiles WitcherScript at startup; runtime compilation and gameplay remain unverified.'
