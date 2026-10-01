[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& dotnet run --project (Join-Path $hcRoot 'modules/crimson-desert/tests/HappyCheats.CrimsonDesert.Tests') -c Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw 'Crimson Desert binary inspection tests failed.' }
$hcNativeTest = Join-Path $hcRoot 'modules/crimson-desert/native/tests/logic.cpp'
if (!(Test-Path -LiteralPath $hcNativeTest)) { throw 'Crimson Desert native regression test source is missing.' }
$hcBuild = Join-Path $hcRoot 'artifacts/crimson-build'
New-Item -ItemType Directory -Force -Path $hcBuild | Out-Null
$hcTestExe = Join-Path $hcBuild 'trainer-logic-tests.exe'
& g++ -std=c++20 -O2 -static -static-libgcc -static-libstdc++ $hcNativeTest -o $hcTestExe
if ($LASTEXITCODE -ne 0) { throw 'Crimson Desert native tests did not compile.' }
& $hcTestExe
if ($LASTEXITCODE -ne 0) { throw 'Crimson Desert native tests failed.' }
$hcNative = Join-Path $hcRoot 'modules/crimson-desert/native'
$hcObjects = @()
foreach ($hcSource in @('buffer.c','hook.c','trampoline.c','hde/hde64.c')) {
    $hcObject = Join-Path $hcBuild ('test-' + [IO.Path]::GetFileNameWithoutExtension($hcSource) + '.o')
    & gcc -c -O2 -DNDEBUG -D_WIN64 -I (Join-Path $hcNative 'vendor/minhook/include') (Join-Path $hcNative ('vendor/minhook/src/' + $hcSource)) -o $hcObject
    if ($LASTEXITCODE -ne 0) { throw 'Crimson Desert test hook library compilation failed.' }
    $hcObjects += $hcObject
}
$hcHookTest = Join-Path $hcBuild 'hook-tests.exe'
& g++ -std=c++20 -O2 -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor/minhook/include') (Join-Path $hcNative 'tests/hooks.cpp') @hcObjects -o $hcHookTest
if ($LASTEXITCODE -ne 0) { throw 'Crimson Desert hook tests did not compile.' }
& $hcHookTest
if ($LASTEXITCODE -ne 0) { throw 'Crimson Desert native hook fixture failed.' }
$hcPrologueTest = Join-Path $hcBuild 'prologue-tests.exe'
& g++ -std=c++20 -O2 -Wall -Wextra -Wpedantic -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor/minhook/include') (Join-Path $hcNative 'tests/prologues.cpp') @hcObjects -o $hcPrologueTest
if ($LASTEXITCODE -ne 0) { throw 'Crimson Desert exact-prologue fixture did not compile.' }
$hcPrologueLog = Join-Path $hcBuild 'prologue-test-results.jsonl'
& $hcPrologueTest *> $hcPrologueLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $hcPrologueLog | Write-Host
    throw 'Crimson Desert exact-prologue or nearby-space fixture failed.'
}
Write-Host 'PASS all five exact game prologues and isolated nearby-space exhaustion; no game accessed'
$hcDiagnosticsTest = Join-Path $hcBuild 'diagnostics-tests.exe'
& g++ -std=c++20 -O2 -Wall -Wextra -Wpedantic -DUNICODE -D_UNICODE -D_WIN64 -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor') -I (Join-Path $hcNative 'vendor/minhook/include') (Join-Path $hcNative 'tests/diagnostics.cpp') @hcObjects -o $hcDiagnosticsTest -lbcrypt -lpsapi -lversion
if ($LASTEXITCODE -ne 0) { throw 'Crimson Desert diagnostic fixture did not compile.' }
$hcDiagnosticsLog = Join-Path $hcBuild 'diagnostics-test-results.txt'
& $hcDiagnosticsTest *> $hcDiagnosticsLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $hcDiagnosticsLog | Write-Host
    throw 'Crimson Desert native diagnostic regression fixture failed.'
}
Write-Host 'PASS native hook outcomes, injected enable failure, read-only evidence and bounded scan diagnostics'
$hcTeleportTest = Join-Path $hcBuild 'teleport-tests.exe'
& g++ -std=c++20 -O2 -Wall -Wextra -Wpedantic -DUNICODE -D_UNICODE -D_WIN64 -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor') -I (Join-Path $hcNative 'vendor/minhook/include') (Join-Path $hcNative 'tests/teleport.cpp') @hcObjects -o $hcTeleportTest -lbcrypt -lpsapi -lversion
if ($LASTEXITCODE -ne 0) { throw 'Crimson Desert outer teleport fixture did not compile.' }
$hcTeleportLog = Join-Path $hcBuild 'teleport-test-results.txt'
& $hcTeleportTest *> $hcTeleportLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $hcTeleportLog | Write-Host
    throw 'Crimson Desert outer teleport regression fixture failed.'
}
Get-Content -LiteralPath $hcTeleportLog | Write-Host
