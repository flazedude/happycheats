[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcCiv7Root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcCiv7Package = Join-Path $hcCiv7Root 'payload/civilization-vii'
$hcCiv7Source = Join-Path $hcCiv7Root 'modules/civilization-vii/Scripts/runtime.js'
if (!(Test-Path -LiteralPath $hcCiv7Source -PathType Leaf)) { throw 'Civilization VII JavaScript source is missing.' }
& node (Join-Path $hcCiv7Root 'modules/civilization-vii/tests/run.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Civilization VII JavaScript validation failed.' }
New-Item -ItemType Directory -Force -Path $hcCiv7Package | Out-Null
Copy-Item -LiteralPath $hcCiv7Source -Destination (Join-Path $hcCiv7Package 'runtime.js') -Force
Copy-Item -LiteralPath (Join-Path $hcCiv7Root 'docs/civilization-vii.md') -Destination (Join-Path $hcCiv7Package 'README.md') -Force
[ordered]@{
    version='0.1.0'; app_id='1295660'; backend='JavaScript FireTuner localhost:4318'; validation='offline-only';
    files=@([ordered]@{path='runtime.js'; sha256=(Get-FileHash -LiteralPath (Join-Path $hcCiv7Package 'runtime.js') -Algorithm SHA256).Hash})
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $hcCiv7Package 'package.json') -Encoding utf8NoBOM
Write-Host 'Built Civilization VII 0.1.0 JavaScript payload. Gameplay is unverified.'
