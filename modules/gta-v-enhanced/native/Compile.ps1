[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$hcNative = $PSScriptRoot
$hcOutput = [IO.Path]::GetFullPath($OutputPath)
$hcBuild = Split-Path $hcOutput
New-Item -ItemType Directory -Force -Path $hcBuild | Out-Null
$hcGpp = (Get-Command g++ -ErrorAction Stop).Source
$hcBin = Split-Path $hcGpp
$hcGcc = (Get-Command gcc -ErrorAction Stop).Source
$hcObjects = @()
foreach ($hcSource in @('buffer.c','hook.c','trampoline.c','hde/hde64.c')) {
    $hcObject = Join-Path $hcBuild ('gta-minhook-' + [IO.Path]::GetFileNameWithoutExtension($hcSource) + '.o')
    & $hcGcc -c -O2 -DNDEBUG -D_WIN64 -I (Join-Path $hcNative 'vendor/minhook/include') (Join-Path $hcNative ('vendor/minhook/src/' + $hcSource)) -o $hcObject
    if ($LASTEXITCODE -ne 0) { throw 'GTA source runtime hook library did not compile.' }
    $hcObjects += $hcObject
}
$hcUsed = [regex]::Matches((Get-Content (Join-Path $hcNative 'trainer.cpp') -Raw), 'g_hook\.Native(?:<[^>]+>)?\(0x([A-Fa-f0-9]+)ULL') | ForEach-Object { $_.Groups[1].Value.ToUpperInvariant() } | Sort-Object -Unique
$hcMapped = [regex]::Matches((Get-Content (Join-Path $hcNative 'source_mappings.cpp') -Raw), '\{0x([A-Fa-f0-9]+)ULL,') | ForEach-Object { $_.Groups[1].Value.ToUpperInvariant() } | Sort-Object -Unique
if ($hcUsed.Count -ne 41 -or (Compare-Object $hcUsed $hcMapped)) { throw 'The source backend must map every native used by the trainer.' }
& $hcGpp -std=c++20 -O2 -DNDEBUG -DUNICODE -D_UNICODE -D_WIN64 -Wall -Wextra -Wno-cast-function-type -shared -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor') -I (Join-Path $hcNative 'vendor/minhook/include') (Join-Path $hcNative 'trainer.cpp') (Join-Path $hcNative 'source_runtime.cpp') (Join-Path $hcNative 'source_mappings.cpp') @hcObjects -o $hcOutput -lbcrypt -lversion '-Wl,--no-insert-timestamp'
if ($LASTEXITCODE -ne 0) { throw 'GTA V Enhanced ASI compilation failed.' }
$hcPe = (& (Join-Path $hcBin 'objdump.exe') -p $hcOutput | Out-String)
if ($LASTEXITCODE -ne 0 -or $hcPe -match 'DLL Name: ScriptHookV.dll') { throw 'The latest-build source backend must load independently of ScriptHookV.' }
if ($hcPe -notmatch 'pei-x86-64' -or $hcPe -match 'DLL Name: (libstdc\+\+|libgcc|libwinpthread)') { throw 'ASI architecture or self-contained compiler runtime check failed.' }
Write-Host "Compiled x64 GTA V Enhanced dual-backend ASI: $hcOutput"
