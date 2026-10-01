[CmdletBinding()]
param([string]$AsiPath,[switch]$SkipPayloadValidation)
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcNative = Join-Path $hcRoot 'modules/rdr2/native'
$hcBuild = Join-Path $hcRoot 'artifacts/rdr2-build'
$hcFixture = Join-Path $hcBuild 'fixtures'
$hcPackage = Join-Path $hcRoot 'payload/red-dead-redemption-2'
$hcCompiler = (Get-Command g++ -ErrorAction Stop).Source
New-Item -ItemType Directory -Force -Path $hcBuild,$hcFixture | Out-Null
foreach ($hcTest in @('logic','natives')) {
    $hcTestExe = Join-Path $hcBuild "$hcTest-tests.exe"
    & $hcCompiler -std=c++20 -O2 -Wall -Wextra -Werror -Wno-cast-function-type -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor') (Join-Path $hcNative "tests/$hcTest.cpp") -o $hcTestExe
    if ($LASTEXITCODE -ne 0) { throw "RDR2 $hcTest fixture compilation failed." }
    & $hcTestExe
    if ($LASTEXITCODE -ne 0) { throw "RDR2 $hcTest fixture failed." }
}
if (!$AsiPath) { $AsiPath = Join-Path $hcPackage 'HappyCheats.Rdr2.asi' }
if (!(Test-Path -LiteralPath $AsiPath)) { throw 'Build the RDR2 payload before running packaged ASI tests.' }
$hcFixtureAsi = Join-Path $hcFixture 'HappyCheats.Rdr2.asi'
Copy-Item -LiteralPath $AsiPath -Destination $hcFixtureAsi -Force
& $hcCompiler -std=c++20 -O2 -shared -static -static-libgcc -static-libstdc++ (Join-Path $hcNative 'tests/fake_hook.cpp') (Join-Path $hcNative 'tests/fake_hook.def') -o (Join-Path $hcFixture 'ScriptHookRDR2.dll')
if ($LASTEXITCODE -ne 0) { throw 'RDR2 mock ScriptHook fixture compilation failed.' }
foreach ($hcHost in @(@{Source='host';Exe='unsupported-host.exe'},@{Source='registration';Exe='RDR2.exe'})) {
    $hcHostExe = Join-Path $hcFixture $hcHost.Exe
    & $hcCompiler -std=c++20 -O2 -municode -static -static-libgcc -static-libstdc++ (Join-Path $hcNative "tests/$($hcHost.Source).cpp") -o $hcHostExe
    if ($LASTEXITCODE -ne 0) { throw 'RDR2 loader fixture compilation failed.' }
    & $hcHostExe $hcFixtureAsi
    if ($LASTEXITCODE -ne 0) { throw "RDR2 $($hcHost.Source) loader fixture failed." }
}
if (!$SkipPayloadValidation) {
    $hcManifest = Get-Content -LiteralPath (Join-Path $hcPackage 'package.json') -Raw | ConvertFrom-Json
    if ($hcManifest.version -ne '0.1.0' -or $hcManifest.game_id -ne 'red-dead-redemption-2' -or $hcManifest.app_id -ne '1174180' -or $hcManifest.profile_file_version -ne '1.0.1491.50' -or $hcManifest.files.Count -ne 2) { throw 'Unexpected RDR2 payload manifest.' }
    foreach ($hcFile in $hcManifest.files) {
        $hcPath = [IO.Path]::GetFullPath((Join-Path $hcPackage $hcFile.path))
        if (!$hcPath.StartsWith($hcPackage+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'RDR2 payload path escapes its root.' }
        if ((Get-FileHash -LiteralPath $hcPath -Algorithm SHA256).Hash -ne $hcFile.sha256) { throw "RDR2 payload hash mismatch: $($hcFile.path)" }
    }
    if ($hcManifest.sources.Count -ne 6) { throw 'RDR2 source provenance is incomplete.' }
    foreach ($hcSource in $hcManifest.sources) {
        if ((Get-FileHash -LiteralPath (Join-Path $hcNative $hcSource.path) -Algorithm SHA256).Hash -ne $hcSource.sha256) { throw "Stale RDR2 compiled payload: $($hcSource.path)" }
    }
    if ((Test-Path -LiteralPath (Join-Path $hcPackage 'ScriptHookRDR2.dll')) -or (Test-Path -LiteralPath (Join-Path $hcPackage 'dinput8.dll'))) { throw 'RDR2 external runtime must not be redistributed in this payload.' }
    Write-Host 'RDR2 payload/source hashes verified; no game or live gameplay was tested.'
}
