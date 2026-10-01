[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcCiv7Root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& node (Join-Path $hcCiv7Root 'modules/civilization-vii/tests/run.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Civilization VII runtime tests failed.' }
& dotnet run --project (Join-Path $hcCiv7Root 'modules/civilization-vii/tests/Tuner.Tests') -c Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw 'Civilization VII Tuner transport tests failed.' }
$hcCiv7Package = Join-Path $hcCiv7Root 'payload/civilization-vii'
$hcCiv7Manifest = Get-Content -LiteralPath (Join-Path $hcCiv7Package 'package.json') -Raw | ConvertFrom-Json
if ($hcCiv7Manifest.version -ne '0.1.0' -or $hcCiv7Manifest.app_id -ne '1295660' -or $hcCiv7Manifest.files.Count -ne 1 -or $hcCiv7Manifest.files[0].path -ne 'runtime.js') { throw 'Invalid Civilization VII manifest.' }
$hcCiv7Hash = (Get-FileHash -LiteralPath (Join-Path $hcCiv7Package 'runtime.js') -Algorithm SHA256).Hash
if ($hcCiv7Hash -ne $hcCiv7Manifest.files[0].sha256 -or $hcCiv7Hash -ne (Get-FileHash -LiteralPath (Join-Path $hcCiv7Root 'modules/civilization-vii/Scripts/runtime.js')).Hash) { throw 'Stale or damaged Civilization VII payload.' }
Write-Host 'Civilization VII payload and offline transport checks passed.'
