[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$lcRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lcNative=Join-Path $lcRoot 'modules/livin-cream/native'
$lcVendor=Join-Path $lcRoot 'modules/crimson-desert/native/vendor'
$lcBuild=Join-Path $lcRoot 'artifacts/livin-cream-build'
New-Item -ItemType Directory -Path $lcBuild -Force | Out-Null
$lcObjects=@()
foreach($lcSource in @('buffer.c','hook.c','trampoline.c','hde/hde64.c')) {
    $lcObject=Join-Path $lcBuild ('test-'+[IO.Path]::GetFileNameWithoutExtension($lcSource)+'.o')
    & gcc -c -O2 -DNDEBUG -D_WIN64 -I (Join-Path $lcVendor 'minhook/include') (Join-Path $lcVendor ('minhook/src/'+$lcSource)) -o $lcObject
    if($LASTEXITCODE -ne 0){throw 'Livin Cream test hook compilation failed.'}
    $lcObjects+=$lcObject
}
$lcTest=Join-Path $lcBuild 'runtime-tests.exe'
& g++ -std=c++20 -O2 -Wall -Wextra -static -static-libgcc -static-libstdc++ -I $lcVendor -I $lcNative -I (Join-Path $lcVendor 'minhook/include') (Join-Path $lcNative 'tests.cpp') @lcObjects -o $lcTest
if($LASTEXITCODE -ne 0){throw 'Livin Cream native fixture compilation failed.'}
& $lcTest
if($LASTEXITCODE -ne 0){throw 'Livin Cream native fixture checks failed.'}
& dotnet run --project (Join-Path $lcRoot 'tests/HappyCheats.Tests') -c Release --no-launch-profile -- --livin-cream
if($LASTEXITCODE -ne 0){throw 'Livin Cream module/installer checks failed.'}
$lcPayload=Join-Path $lcRoot 'payload/livin-cream'
$lcManifest=Get-Content (Join-Path $lcPayload 'package.json') -Raw | ConvertFrom-Json
if($lcManifest.game -cne 'livin-cream' -or $lcManifest.version -cne '1.0.3' -or $lcManifest.steamBuild -cne '26525450' -or @($lcManifest.files).Count -ne 2){throw 'Invalid native Livin Cream payload identity.'}
foreach($lcFile in $lcManifest.files){if((Get-FileHash -LiteralPath (Join-Path $lcPayload $lcFile.path)).Hash -ine $lcFile.sha256){throw 'Livin Cream packaged file hash mismatch.'}}
foreach($lcSource in $lcManifest.sources){if((Get-FileHash -LiteralPath (Join-Path $lcNative $lcSource.path)).Hash -ine $lcSource.sha256){throw 'Stale compiled Livin Cream source.'}}
if(Get-ChildItem -LiteralPath $lcPayload -Recurse -File | Where-Object Name -in @('Assembly-CSharp.dll','GameAssembly.dll','UnityEngine.dll','Newtonsoft.Json.dll','winhttp.dll')){throw 'Legacy loader or game binaries must not be shipped in the native payload.'}
Write-Host 'Livin Cream native runtime, payload/source hashes and installer checks passed. Actual gameplay and physical controller operation remain unverified.'
