[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcCivRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcCivPython = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
if (!(Test-Path -LiteralPath $hcCivPython)) { $hcCivPython = 'python' }
& $hcCivPython (Join-Path $hcCivRoot 'modules/civilization-vi/tests/run.py')
if ($LASTEXITCODE -ne 0) { throw 'Civilization VI Lua validation failed.' }
& dotnet run --project (Join-Path $hcCivRoot 'modules/civilization-vi/tests/FireTuner.Tests') -c Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw 'Civilization VI FireTuner protocol tests failed.' }
$hcCivPackage = Join-Path $hcCivRoot 'payload/civilization-vi'
$hcCivManifest = Get-Content -LiteralPath (Join-Path $hcCivPackage 'package.json') -Raw | ConvertFrom-Json
if ($hcCivManifest.version -ne '0.1.1' -or $hcCivManifest.app_id -ne '289070' -or @($hcCivManifest.files).Count -ne 1 -or $hcCivManifest.files[0].path -cne 'runtime.lua') { throw 'Invalid Civilization VI manifest.' }
$hcCivHash = (Get-FileHash -LiteralPath (Join-Path $hcCivPackage 'runtime.lua') -Algorithm SHA256).Hash
if ($hcCivHash -ne $hcCivManifest.files[0].sha256 -or $hcCivHash -ne (Get-FileHash -LiteralPath (Join-Path $hcCivRoot 'modules/civilization-vi/Scripts/runtime.lua')).Hash) { throw 'Stale or damaged Civilization VI payload.' }
Write-Host 'Civilization VI runtime and payload verified offline; no game or saves were opened.'
