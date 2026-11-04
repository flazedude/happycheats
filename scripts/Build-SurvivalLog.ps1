[CmdletBinding()]
param([string]$GameDirectory='G:\Games\SteamLib\steamapps\common\Survival Log')
$ErrorActionPreference='Stop'
$slRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$slNative=Join-Path $slRoot 'modules/survival-log/native'
# Reuse the repository's pinned, locally reviewed MinHook and JSON source, including licenses.
$slVendor=Join-Path $slRoot 'modules/crimson-desert/native/vendor'
$slBuild=Join-Path $slRoot 'artifacts/survival-log-build'
$slOutput=Join-Path $slRoot 'payload/survival-log/HappyCheatsSurvivalLog'
New-Item -ItemType Directory -Force -Path $slBuild,$slOutput | Out-Null
& (Join-Path $slRoot 'tools/InspectSurvivalLog/Validate-Bindings.ps1') -GameDirectory $GameDirectory
$slObjects=@()
foreach($slSource in @('buffer.c','hook.c','trampoline.c','hde/hde64.c')) {
    $slObject=Join-Path $slBuild (([IO.Path]::GetFileNameWithoutExtension($slSource))+'.o')
    & gcc -c -O2 -DNDEBUG -D_WIN64 -I (Join-Path $slVendor 'minhook/include') (Join-Path $slVendor ('minhook/src/'+$slSource)) -o $slObject
    if($LASTEXITCODE -ne 0){throw "Survival Log hook compilation failed: $slSource"}
    $slObjects+=$slObject
}
$slDll=Join-Path $slBuild 'HappyCheats.SurvivalLog.dll'
& g++ -std=c++20 -O2 -Wall -Wextra -DNDEBUG -DUNICODE -D_UNICODE -D_WIN64 -shared -static -static-libgcc -static-libstdc++ -I $slVendor -I (Join-Path $slVendor 'minhook/include') (Join-Path $slNative 'trainer.cpp') @slObjects -o $slDll -lbcrypt '-Wl,--no-insert-timestamp'
if($LASTEXITCODE -ne 0){throw 'Survival Log trainer compilation failed.'}
& (Join-Path $PSScriptRoot 'Test-SurvivalLog.ps1')
$slHost=Join-Path $slBuild 'unsupported-host.exe'
& g++ -std=c++20 -O2 -municode -static -static-libgcc -static-libstdc++ -I $slVendor (Join-Path $slNative 'tests/host.cpp') -o $slHost
if($LASTEXITCODE -ne 0){throw 'Survival Log DLL smoke host compilation failed.'}
& $slHost $slDll
if($LASTEXITCODE -ne 0){throw 'Survival Log packaged DLL startup smoke check failed.'}
Copy-Item -LiteralPath $slDll -Destination $slOutput -Force
@(
    'HappyCheats Survival Log trainer 0.1.0. Original bindings; game code/assets are not redistributed.',
    'MinHook v1.3.4 with repository reviewed x64 allocation fixes: https://github.com/TsudaKageyu/minhook/tree/v1.3.4',
    (Get-Content (Join-Path $slVendor 'minhook/LICENSE.txt') -Raw),
    'JSON for Modern C++ v3.12.0: https://github.com/nlohmann/json/tree/v3.12.0',
    (Get-Content (Join-Path $slVendor 'nlohmann/LICENSE.MIT') -Raw)
) -join "`r`n" | Set-Content (Join-Path $slOutput 'ThirdPartyNotices.txt') -Encoding utf8NoBOM
$slPayload=Split-Path $slOutput -Parent
$slFiles=@('HappyCheatsSurvivalLog/HappyCheats.SurvivalLog.dll','HappyCheatsSurvivalLog/ThirdPartyNotices.txt') | ForEach-Object {
    @{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $slPayload $_) -Algorithm SHA256).Hash}
}
@{schemaVersion=1;game='survival-log';version='0.1.0';steamBuild='26294912';files=@($slFiles)} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $slPayload 'package.json') -Encoding utf8NoBOM
Write-Host "Built Survival Log payload: $slOutput"
