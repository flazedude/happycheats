[CmdletBinding()]
param([string]$AsiPath, [string]$BuildDirectory = (Join-Path $PSScriptRoot 'build-tests'))
$ErrorActionPreference = 'Stop'
$hcNative = $PSScriptRoot
$hcBuild = [IO.Path]::GetFullPath($BuildDirectory)
New-Item -ItemType Directory -Force -Path $hcBuild | Out-Null
$hcGpp = (Get-Command g++ -ErrorAction Stop).Source
$hcBin = Split-Path $hcGpp
$hcLogic = Join-Path $hcBuild 'engine-tests.exe'
& $hcGpp -std=c++20 -O2 -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor') (Join-Path $hcNative 'tests/engine_tests.cpp') -o $hcLogic
if ($LASTEXITCODE -ne 0) { throw 'GTA V protocol/game dispatch tests did not compile.' }
& $hcLogic
if ($LASTEXITCODE -ne 0) { throw 'GTA V protocol/game dispatch tests failed.' }
if ([string]::IsNullOrWhiteSpace($AsiPath)) {
    $AsiPath = Join-Path $hcBuild 'HappyCheats.GtaV.asi'
    & (Join-Path $hcNative 'Compile.ps1') -OutputPath $AsiPath
}
$hcAsi = [IO.Path]::GetFullPath($AsiPath)
if (!(Test-Path -LiteralPath $hcAsi)) { throw 'GTA V ASI missing for packaged-host tests.' }
$hcMock = Join-Path $hcBuild 'ScriptHookV.dll'
& $hcGpp -std=c++20 -O2 -shared -static -static-libgcc -static-libstdc++ (Join-Path $hcNative 'tests/mock_script_hook.cpp') (Join-Path $hcNative 'tests/mock_script_hook.def') -o $hcMock '-Wl,--no-insert-timestamp'
if ($LASTEXITCODE -ne 0) { throw 'Inert ScriptHook dependency fixture did not compile.' }
$hcCases = @(
    @{ name = 'unsupported-host'; exe = 'fixture.exe'; version = ''; error = 'Unsupported host' },
    @{ name = 'unsupported-version'; exe = 'GTA5_Enhanced.exe'; version = '1,0,1158,17'; error = 'Unsupported GTA V Enhanced build 1.0.1158.17' },
    @{ name = 'missing-exports'; exe = 'GTA5_Enhanced.exe'; version = '1,0,1158,13'; error = 'required public export' }
)
foreach ($hcCase in $hcCases) {
    $hcDirectory = Join-Path $hcBuild $hcCase.name
    New-Item -ItemType Directory -Force -Path $hcDirectory | Out-Null
    $hcObjects = @()
    if ($hcCase.version) {
        $hcResource = Join-Path $hcDirectory 'fixture.rc'
        @"
#include <windows.h>
1 VERSIONINFO
FILEVERSION $($hcCase.version)
PRODUCTVERSION $($hcCase.version)
FILEOS VOS_NT_WINDOWS32
FILETYPE VFT_APP
BEGIN
BLOCK "VarFileInfo"
BEGIN
VALUE "Translation", 0x409, 1200
END
END
"@ | Set-Content -LiteralPath $hcResource -Encoding ascii
        $hcObject = Join-Path $hcDirectory 'fixture-resource.o'
        & (Join-Path $hcBin 'windres.exe') $hcResource -O coff -o $hcObject
        if ($LASTEXITCODE -ne 0) { throw 'GTA V version fixture did not compile.' }
        $hcObjects += $hcObject
    }
    $hcHost = Join-Path $hcDirectory $hcCase.exe
    & $hcGpp -std=c++20 -O2 -municode -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor') (Join-Path $hcNative 'tests/host.cpp') @hcObjects -o $hcHost
    if ($LASTEXITCODE -ne 0) { throw 'GTA V fixture host did not compile.' }
    Copy-Item -LiteralPath $hcMock -Destination (Join-Path $hcDirectory 'ScriptHookV.dll') -Force
    $hcFixtureAsi = Join-Path $hcDirectory 'HappyCheats.GtaV.asi'
    Copy-Item -LiteralPath $hcAsi -Destination $hcFixtureAsi -Force
    & $hcHost $hcFixtureAsi $hcCase.error
    if ($LASTEXITCODE -ne 0) { throw "GTA V fixture '$($hcCase.name)' failed with code $LASTEXITCODE." }
}
$hcAccepted = Join-Path $hcBuild 'accepted-host'
New-Item -ItemType Directory -Force -Path $hcAccepted | Out-Null
$hcFullMock = Join-Path $hcAccepted 'ScriptHookV.dll'
& $hcGpp -std=c++20 -O2 -shared -static -static-libgcc -static-libstdc++ (Join-Path $hcNative 'tests/full_mock_script_hook.cpp') (Join-Path $hcNative 'tests/full_mock_script_hook.def') -o $hcFullMock '-Wl,--no-insert-timestamp'
if ($LASTEXITCODE -ne 0) { throw 'Full inert ScriptHook ABI fixture did not compile.' }
$hcAcceptedHost = Join-Path $hcAccepted 'GTA5_Enhanced.exe'
$hcVersionObject = Join-Path $hcBuild 'missing-exports/fixture-resource.o'
& $hcGpp -std=c++20 -O2 -municode -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor') (Join-Path $hcNative 'tests/accepted_host.cpp') $hcVersionObject -o $hcAcceptedHost
if ($LASTEXITCODE -ne 0) { throw 'Accepted-host ABI fixture did not compile.' }
$hcAcceptedAsi = Join-Path $hcAccepted 'HappyCheats.GtaV.asi'
Copy-Item -LiteralPath $hcAsi -Destination $hcAcceptedAsi -Force
# Bound the fixture process itself as well as its internal mailbox deadlines.
$hcProcess = Start-Process -FilePath $hcAcceptedHost -ArgumentList ('"' + $hcAcceptedAsi + '"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $hcAccepted 'stdout.txt') -RedirectStandardError (Join-Path $hcAccepted 'stderr.txt')
if (!$hcProcess.WaitForExit(15000)) { $hcProcess.Kill(); throw 'Accepted-host ASI fixture exceeded its 15 second deadline.' }
Get-Content -LiteralPath (Join-Path $hcAccepted 'stdout.txt')
if ($hcProcess.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $hcAccepted 'stderr.txt')
    throw "Accepted-host ASI fixture failed with code $($hcProcess.ExitCode)."
}
Write-Host 'GTA V Enhanced offline checks passed. No game launched; gameplay compatibility is unverified.'

# A real MinHook trampoline and native/TLS calls in a synthetic PE image. This
# fixture never starts, opens or injects into the installed game.
$hcObjects = @()
foreach ($hcSource in @('buffer.c','hook.c','trampoline.c','hde/hde64.c')) {
    $hcObject = Join-Path $hcBuild ('source-fixture-' + [IO.Path]::GetFileNameWithoutExtension($hcSource) + '.o')
    & (Join-Path $hcBin 'gcc.exe') -c -O2 -DNDEBUG -D_WIN64 -I (Join-Path $hcNative 'vendor/minhook/include') (Join-Path $hcNative ('vendor/minhook/src/' + $hcSource)) -o $hcObject
    if ($LASTEXITCODE -ne 0) { throw 'Source runtime fixture hook library did not compile.' }
    $hcObjects += $hcObject
}
$hcSourceFixture = Join-Path $hcBuild 'SourceRuntimeFixture.exe'
& $hcGpp -std=c++20 -O2 -DHC_SOURCE_TESTING -DUNICODE -D_UNICODE -D_WIN64 -Wall -Wextra -Wno-cast-function-type -static -static-libgcc -static-libstdc++ -I $hcNative -I (Join-Path $hcNative 'vendor/minhook/include') (Join-Path $hcNative 'source_runtime.cpp') (Join-Path $hcNative 'source_mappings.cpp') (Join-Path $hcNative 'tests/source_fixture.cpp') @hcObjects -o $hcSourceFixture '-Wl,--no-insert-timestamp'
if ($LASTEXITCODE -ne 0) { throw 'Source runtime fixture compilation failed.' }
foreach ($hcScenario in @('online','foreign-thread')) {
    $hcSourceProcess = Start-Process -FilePath $hcSourceFixture -ArgumentList ('--' + $hcScenario) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $hcBuild ('source-fixture-' + $hcScenario + '.out.txt')) -RedirectStandardError (Join-Path $hcBuild ('source-fixture-' + $hcScenario + '.err.txt'))
    if (!$hcSourceProcess.WaitForExit(10000)) { $hcSourceProcess.Kill(); throw 'Source runtime fixture exceeded 10 seconds.' }
    Get-Content -LiteralPath (Join-Path $hcBuild ('source-fixture-' + $hcScenario + '.out.txt')),(Join-Path $hcBuild ('source-fixture-' + $hcScenario + '.err.txt'))
    if ($hcSourceProcess.ExitCode -ne 0) { throw "Source runtime fixture failed: $hcScenario" }
}

# The production ASI must route .16 hosts to its source backend without loading
# ScriptHookV, and reject this tiny fixture's missing engine interfaces.
$hcSourceHostDirectory = Join-Path $hcBuild 'source-host'
New-Item -ItemType Directory -Force -Path $hcSourceHostDirectory | Out-Null
$hcResource = Join-Path $hcSourceHostDirectory 'fixture.rc'
(Get-Content -LiteralPath (Join-Path $hcBuild 'missing-exports/fixture.rc') -Raw).Replace('1,0,1158,13','1,0,1158,16') | Set-Content -LiteralPath $hcResource -Encoding ascii
$hcSourceResource = Join-Path $hcSourceHostDirectory 'fixture-resource.o'
& (Join-Path $hcBin 'windres.exe') $hcResource -O coff -o $hcSourceResource
if ($LASTEXITCODE -ne 0) { throw 'Latest source host resource compilation failed.' }
$hcSourceHost = Join-Path $hcSourceHostDirectory 'GTA5_Enhanced.exe'
& $hcGpp -std=c++20 -O2 -municode -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor') (Join-Path $hcNative 'tests/source_host.cpp') $hcSourceResource -o $hcSourceHost
if ($LASTEXITCODE -ne 0) { throw 'Latest source host compilation failed.' }
$hcSourceAsi = Join-Path $hcSourceHostDirectory 'HappyCheats.GtaV.asi'
Copy-Item -LiteralPath $hcAsi -Destination $hcSourceAsi -Force
$hcRouting = Start-Process -FilePath $hcSourceHost -ArgumentList ('"' + $hcSourceAsi + '"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $hcSourceHostDirectory 'stdout.txt') -RedirectStandardError (Join-Path $hcSourceHostDirectory 'stderr.txt')
if (!$hcRouting.WaitForExit(10000)) { $hcRouting.Kill(); throw 'Source host routing exceeded 10 seconds.' }
Get-Content -LiteralPath (Join-Path $hcSourceHostDirectory 'stdout.txt'),(Join-Path $hcSourceHostDirectory 'stderr.txt')
if ($hcRouting.ExitCode -ne 0) { throw 'Latest source host routing failed.' }
Write-Host 'Both GTA backend fixtures passed; actual native startup and gameplay remain unverified.'

$hcLifetime = Join-Path $hcBuild 'WorkerLifetimeFixture.exe'
& $hcGpp -std=c++20 -O2 -municode -static -static-libgcc -static-libstdc++ (Join-Path $hcNative 'tests/worker_lifetime_fixture.cpp') -o $hcLifetime
if ($LASTEXITCODE -ne 0) { throw 'Worker lifetime fixture did not compile.' }
$hcLifetimeProcess = Start-Process -FilePath $hcLifetime -ArgumentList ('"' + $hcAsi + '"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $hcBuild 'lifetime.out.txt') -RedirectStandardError (Join-Path $hcBuild 'lifetime.err.txt')
if (!$hcLifetimeProcess.WaitForExit(10000)) { $hcLifetimeProcess.Kill(); throw 'Worker lifetime fixture exceeded 10 seconds.' }
Get-Content -LiteralPath (Join-Path $hcBuild 'lifetime.out.txt'),(Join-Path $hcBuild 'lifetime.err.txt')
if ($hcLifetimeProcess.ExitCode -ne 0) { throw 'Queued worker module lifetime check failed.' }
