[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcNative = Join-Path $hcRoot 'modules/crimson-desert/native'
$hcFixture = Join-Path $hcRoot ('artifacts/crimson-build/injection-fixture-' + [Guid]::NewGuid().ToString('N'))
$hcBin = Join-Path $hcFixture 'bin64'
$hcTrainer = Join-Path $hcBin 'HappyCheatsCrimsonDesert'
New-Item -ItemType Directory -Force -Path $hcTrainer | Out-Null
$hcExe = Join-Path $hcBin 'CrimsonDesert.exe'
& g++ -std=c++20 -O2 -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor') (Join-Path $hcNative 'tests/inject_target.cpp') -o $hcExe
if ($LASTEXITCODE -ne 0) { throw 'Fixture target compilation failed.' }
Copy-Item -LiteralPath (Join-Path $hcRoot 'payload/crimson-desert/bin64/HappyCheatsCrimsonDesert/HappyCheats.CrimsonDesert.dll') -Destination $hcTrainer
$hcAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $hcRoot 'src/HappyCheats.Core/bin/Release/net8.0/HappyCheats.Core.dll'))
$hcInjector = $hcAssembly.GetType('HappyCheats.Core.CrimsonDesertInjector', $true)
$hcAttach = $hcInjector.GetMethod('Attach', [Reflection.BindingFlags]'Static,Public')
$hcProcess = Start-Process -FilePath $hcExe -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Milliseconds 200
    $hcArguments = [object[]]::new(1)
    $hcArguments[0] = [string]$hcFixture
    $hcAttach.Invoke($null, $hcArguments) | Out-Null
    if (!$hcProcess.WaitForExit(10000)) { throw 'Fixture did not confirm native startup.' }
    if ($hcProcess.ExitCode -ne 0) { throw "Fixture failed: $($hcProcess.ExitCode)" }
    $hcStatus=Get-Content -LiteralPath (Join-Path $hcTrainer 'status.json') -Raw | ConvertFrom-Json
    if ($hcStatus.process_id -ne $hcProcess.Id -or $hcStatus.ready -or $hcStatus.player_ready) { throw 'Fixture compatibility gate did not reject the unsupported host.' }
    Write-Host 'PASS real remote loader and native rejection in an isolated compiled fixture; no game opened.'
} finally {
    if (!$hcProcess.HasExited) { Stop-Process -Id $hcProcess.Id -Force }
    $hcProcess.Dispose()
}
