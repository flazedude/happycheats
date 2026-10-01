[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Use PowerShell 7 to build Elden Ring payload metadata.' }
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcSource = Join-Path $hcRoot 'modules/elden-ring'
$hcPayload = Join-Path $hcRoot 'payload/elden-ring'
$hcProfile = Get-Content -Raw -LiteralPath (Join-Path $hcSource 'profile.json') | ConvertFrom-Json
if ($hcProfile.id -ne 'elden-ring-ww-2.7.1.0-resources-v1' -or $hcProfile.fileVersion -ne '2.7.1.0' -or $hcProfile.gameplayVerified) { throw 'Unsupported Elden Ring source profile.' }
New-Item -ItemType Directory -Force -Path $hcPayload | Out-Null
$hcFiles = foreach ($hcName in @('profile.json', 'ThirdPartyNotices.txt')) {
    Copy-Item -LiteralPath (Join-Path $hcSource $hcName) -Destination (Join-Path $hcPayload $hcName) -Force
    [ordered]@{ path=$hcName; sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $hcPayload $hcName)).Hash }
}
[ordered]@{ schemaVersion=1; game='elden-ring'; version='0.1.0'; files=@($hcFiles) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $hcPayload 'package.json') -Encoding utf8NoBOM
Write-Host "Built Elden Ring managed-backend profile: $hcPayload"
