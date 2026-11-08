[CmdletBinding()]
param([string]$GameDirectory="G:\Games\SteamLib\steamapps\common\Livin' the Cream Ice Cream Simulator")
$ErrorActionPreference='Stop'
$lcRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lcNative=Join-Path $lcRoot 'modules/livin-cream/native'
# Reuse the repository's pinned, locally reviewed MinHook and JSON source, including licenses.
$lcVendor=Join-Path $lcRoot 'modules/crimson-desert/native/vendor'
$lcBuild=Join-Path $lcRoot 'artifacts/livin-cream-build'
$lcOutput=Join-Path $lcRoot 'payload/livin-cream/HappyCheatsLivinCream'
New-Item -ItemType Directory -Force -Path $lcBuild,$lcOutput | Out-Null
& dotnet run --project (Join-Path $lcRoot 'tools/InspectLivinCream') -c Release -- $GameDirectory
if($LASTEXITCODE -ne 0){throw 'Livin Cream native binding verification failed.'}
$lcObjects=@()
foreach($lcSource in @('buffer.c','hook.c','trampoline.c','hde/hde64.c')) {
    $lcObject=Join-Path $lcBuild (([IO.Path]::GetFileNameWithoutExtension($lcSource))+'.o')
    & gcc -c -O2 -DNDEBUG -D_WIN64 -I (Join-Path $lcVendor 'minhook/include') (Join-Path $lcVendor ('minhook/src/'+$lcSource)) -o $lcObject
    if($LASTEXITCODE -ne 0){throw "Livin Cream hook compilation failed: $lcSource"}
    $lcObjects+=$lcObject
}
$lcDll=Join-Path $lcBuild 'HappyCheats.LivinCream.dll'
& g++ -std=c++20 -O2 -Wall -Wextra -DNDEBUG -DUNICODE -D_UNICODE -D_WIN64 -shared -static -static-libgcc -static-libstdc++ -I $lcVendor -I (Join-Path $lcVendor 'minhook/include') (Join-Path $lcNative 'trainer.cpp') @lcObjects -o $lcDll -lbcrypt '-Wl,--no-insert-timestamp'
if($LASTEXITCODE -ne 0){throw 'Livin Cream trainer compilation failed.'}
$lcHost=Join-Path $lcBuild 'unsupported-host.exe'
& g++ -std=c++20 -O2 -municode -static -static-libgcc -static-libstdc++ -I $lcVendor (Join-Path $lcNative 'host.cpp') -o $lcHost
if($LASTEXITCODE -ne 0){throw 'Livin Cream DLL smoke host compilation failed.'}
& $lcHost $lcDll
if($LASTEXITCODE -ne 0){throw 'Livin Cream packaged DLL startup smoke check failed.'}
Copy-Item -LiteralPath $lcDll -Destination $lcOutput -Force
@(
    'HappyCheats Livin Cream trainer 1.0.1. Original bindings; game code/assets are not redistributed.',
    'MinHook v1.3.4 with repository reviewed x64 allocation fixes: https://github.com/TsudaKageyu/minhook/tree/v1.3.4',
    (Get-Content (Join-Path $lcVendor 'minhook/LICENSE.txt') -Raw),
    'JSON for Modern C++ v3.12.0: https://github.com/nlohmann/json/tree/v3.12.0',
    (Get-Content (Join-Path $lcVendor 'nlohmann/LICENSE.MIT') -Raw)
) -join "`r`n" | Set-Content (Join-Path $lcOutput 'ThirdPartyNotices.txt') -Encoding utf8NoBOM
$lcPayload=Split-Path $lcOutput -Parent
$lcFiles=@('HappyCheatsLivinCream/HappyCheats.LivinCream.dll','HappyCheatsLivinCream/ThirdPartyNotices.txt') | ForEach-Object {
    @{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $lcPayload $_) -Algorithm SHA256).Hash}
}
@{schemaVersion=1;game='livin-cream';version='1.0.1';steamBuild='26511230';files=@($lcFiles);sources=@(@('trainer.cpp','game_api.h','runtime.h','bindings.h') | ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $lcNative $_)).Hash}})} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $lcPayload 'package.json') -Encoding utf8NoBOM
Write-Host "Built Livin Cream payload: $lcOutput"

& (Join-Path $PSScriptRoot 'Test-LivinCream.ps1')
