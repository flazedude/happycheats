[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcCivRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcCivPackage = Join-Path $hcCivRoot 'payload/civilization-vi'
$hcCivSource = Join-Path $hcCivRoot 'modules/civilization-vi/Scripts/runtime.lua'
if (!(Test-Path -LiteralPath $hcCivSource -PathType Leaf)) { throw 'Civilization VI Lua source is missing.' }
# Validate the actual runtime before copying it into either distribution format.
$hcCivPython = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
if (!(Test-Path -LiteralPath $hcCivPython)) { $hcCivPython = 'python' }
& $hcCivPython (Join-Path $hcCivRoot 'modules/civilization-vi/tests/run.py')
if ($LASTEXITCODE -ne 0) { throw 'Civilization VI Lua validation failed.' }
New-Item -ItemType Directory -Force -Path $hcCivPackage | Out-Null
Copy-Item -LiteralPath $hcCivSource -Destination (Join-Path $hcCivPackage 'runtime.lua') -Force
Copy-Item -LiteralPath (Join-Path $hcCivRoot 'docs/civilization-vi.md') -Destination (Join-Path $hcCivPackage 'README.md') -Force
[ordered]@{
    version='0.1.1'; app_id='289070'; backend='FireTuner localhost:4318'; validation='offline-only';
    files=@([ordered]@{path='runtime.lua'; sha256=(Get-FileHash -LiteralPath (Join-Path $hcCivPackage 'runtime.lua') -Algorithm SHA256).Hash})
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $hcCivPackage 'package.json') -Encoding utf8NoBOM
Write-Host 'Built Civilization VI 0.1.1 Lua payload. Gameplay is unverified.'
