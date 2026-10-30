[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcNative = Join-Path $hcRoot 'modules/crimson-desert/native'
$hcBuild = Join-Path $hcRoot 'artifacts/crimson-build'
$hcPayload = Join-Path $hcRoot 'payload/crimson-desert'
$hcOutput = Join-Path $hcPayload 'bin64/HappyCheatsCrimsonDesert'
$hcGcc = (Get-Command gcc -ErrorAction Stop).Source
$hcGpp = (Get-Command g++ -ErrorAction Stop).Source
New-Item -ItemType Directory -Force -Path $hcBuild | Out-Null
$hcObjects = @()
foreach ($hcSource in @('buffer.c','hook.c','trampoline.c','hde/hde64.c')) {
    $hcObject = Join-Path $hcBuild (([IO.Path]::GetFileNameWithoutExtension($hcSource)) + '.o')
    & $hcGcc -c -O2 -DNDEBUG -D_WIN64 -I (Join-Path $hcNative 'vendor/minhook/include') (Join-Path $hcNative ('vendor/minhook/src/' + $hcSource)) -o $hcObject
    if ($LASTEXITCODE -ne 0) { throw "MinHook compilation failed: $hcSource" }
    $hcObjects += $hcObject
}
$hcDll = Join-Path $hcBuild 'HappyCheats.CrimsonDesert.dll'
& $hcGpp -std=c++20 -O2 -DNDEBUG -DUNICODE -D_UNICODE -D_WIN64 -shared -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor') -I (Join-Path $hcNative 'vendor/minhook/include') (Join-Path $hcNative 'trainer.cpp') @hcObjects -o $hcDll -lbcrypt -lpsapi -lversion '-Wl,--no-insert-timestamp'
if ($LASTEXITCODE -ne 0) { throw 'Crimson Desert trainer compilation failed.' }
& (Join-Path $PSScriptRoot 'Test-CrimsonDesert.ps1')
$hcHostTest = Join-Path $hcBuild 'unsupported-host-test.exe'
& $hcGpp -std=c++20 -O2 -municode -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor') (Join-Path $hcNative 'tests/host.cpp') -o $hcHostTest
if ($LASTEXITCODE -ne 0) { throw 'Crimson Desert fixture host compilation failed.' }
& $hcHostTest $hcDll
if ($LASTEXITCODE -ne 0) { throw 'Crimson Desert native DLL startup/compatibility smoke check failed.' }
New-Item -ItemType Directory -Force -Path $hcOutput | Out-Null
Copy-Item -LiteralPath $hcDll -Destination $hcOutput -Force
$hcNotice = @(
    'HappyCheats Crimson Desert trainer 0.2.6',
    'Original implementation for HappyCheats with interface research adapted from Trinity.',
    'Trinity source: https://github.com/gugi97/Trinity/tree/e5b3303854e7383cfeb3489e7830995772c52ea9',
    'Game executable and game assets are not redistributed.',
    '',
    (Get-Content -LiteralPath (Join-Path $hcNative 'TRINITY-LICENSE.txt') -Raw),
    'MinHook v1.3.4: https://github.com/TsudaKageyu/minhook/tree/v1.3.4',
    'HappyCheats local changes: near-first extended x64 allocation and checked RIP-relative/relay displacements.',
    (Get-Content -LiteralPath (Join-Path $hcNative 'vendor/minhook/LICENSE.txt') -Raw),
    'JSON for Modern C++ v3.12.0: https://github.com/nlohmann/json/tree/v3.12.0',
    (Get-Content -LiteralPath (Join-Path $hcNative 'vendor/nlohmann/LICENSE.MIT') -Raw)
) -join "`r`n"
$hcNotice | Set-Content -LiteralPath (Join-Path $hcOutput 'ThirdPartyNotices.txt') -Encoding utf8NoBOM
$hcFiles = @('bin64/HappyCheatsCrimsonDesert/HappyCheats.CrimsonDesert.dll','bin64/HappyCheatsCrimsonDesert/ThirdPartyNotices.txt') | ForEach-Object {
    @{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $hcPayload $_) -Algorithm SHA256).Hash }
}
@{ schemaVersion=1; game='crimson-desert'; version='0.2.6'; files=$hcFiles } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $hcPayload 'package.json') -Encoding utf8NoBOM
Write-Host "Built Crimson Desert trainer: $hcDll"
