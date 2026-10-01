[CmdletBinding()]
param([string]$GameDirectory, [string]$Python)
$ErrorActionPreference = 'Stop'
$hcWitcherRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Python) {
    $hcWitcherBundledPython = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
    $Python = if (Test-Path -LiteralPath $hcWitcherBundledPython) { $hcWitcherBundledPython } else { 'python' }
}
$hcWitcherArguments = @((Join-Path $hcWitcherRoot 'modules/witcher-3/tests/validate.py'))
if ($GameDirectory) { $hcWitcherArguments += @('--game-directory', $GameDirectory) }
& $Python @hcWitcherArguments
if ($LASTEXITCODE -ne 0) { throw 'Witcher 3 static payload validation failed.' }
Write-Host 'Witcher 3 static payload/API checks passed. WitcherScript compilation and gameplay remain unverified; no game or saves were opened.'
