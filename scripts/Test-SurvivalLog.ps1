[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$slRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$slBuild=Join-Path $slRoot 'artifacts/survival-log-build'
$slVendor=Join-Path $slRoot 'modules/crimson-desert/native/vendor'
New-Item -ItemType Directory -Force -Path $slBuild | Out-Null
$slObjects=@()
foreach($slSource in @('buffer.c','hook.c','trampoline.c','hde/hde64.c')) {
    $slObject=Join-Path $slBuild ('test-'+[IO.Path]::GetFileNameWithoutExtension($slSource)+'.o')
    & gcc -c -O2 -DNDEBUG -D_WIN64 -I (Join-Path $slVendor 'minhook/include') (Join-Path $slVendor ('minhook/src/'+$slSource)) -o $slObject
    if($LASTEXITCODE -ne 0){throw 'Survival Log fixture hook compilation failed.'}
    $slObjects+=$slObject
}
$slTest=Join-Path $slBuild 'native-tests.exe'
& g++ -std=c++20 -O2 -Wall -Wextra -DUNICODE -D_UNICODE -D_WIN64 -static -static-libgcc -static-libstdc++ -I $slVendor -I (Join-Path $slVendor 'minhook/include') (Join-Path $slRoot 'modules/survival-log/native/tests/tests.cpp') @slObjects -o $slTest -lbcrypt
if($LASTEXITCODE -ne 0){throw 'Survival Log native fixture compilation failed.'}
& $slTest
if($LASTEXITCODE -ne 0){throw 'Survival Log native fixture failed.'}
