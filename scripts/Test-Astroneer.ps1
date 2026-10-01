[CmdletBinding()]
param([string]$Python)
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Python) {
    $hcBundledPython = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
    $Python = if (Test-Path -LiteralPath $hcBundledPython) { $hcBundledPython } else { 'python' }
}
& $Python (Join-Path $hcRoot 'modules\astroneer\tests\run.py')
if ($LASTEXITCODE -ne 0) { throw 'Astroneer Lua test suite failed.' }
