[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcNative = Join-Path $hcRoot 'modules/gta-v-enhanced/native'
$hcBuild = Join-Path $hcRoot 'artifacts/gta-build'
$hcAsi = Join-Path $hcBuild 'HappyCheats.GtaV.asi'
& (Join-Path $hcNative 'Compile.ps1') -OutputPath $hcAsi
& (Join-Path $PSScriptRoot 'Test-GtaV.ps1') -AsiPath $hcAsi
$hcPayload = Join-Path $hcRoot 'payload/gta-v-enhanced'
$hcSupport = Join-Path $hcPayload 'HappyCheatsGtaV'
New-Item -ItemType Directory -Force -Path $hcSupport | Out-Null
Copy-Item -LiteralPath $hcAsi -Destination (Join-Path $hcPayload 'HappyCheats.GtaV.asi') -Force
$hcNotices = @(
    'HappyCheats GTA V Enhanced Story Mode trainer 0.2.0',
    'Experimental Story Mode targets: 1.0.1158.16 (included source backend) and 1.0.1158.13 (external ScriptHookV).',
    'Native startup and gameplay have not been verified. GTA Online, including invite-only sessions, is disabled.',
    'This native module is GPL-2.0. Corresponding source, build scripts and licenses accompany it in Source.zip.',
    'Enhanced interface/hash subset derived from YimMenuV2 enhanced commit 39a0f227782304d7fb3a5f7d3cdaec36a4d348f3.',
    'https://github.com/YimMenu/YimMenuV2/tree/39a0f227782304d7fb3a5f7d3cdaec36a4d348f3',
    'Setup acquires the required author loader or ScriptHookV runtime directly; proprietary files are not bundled.',
    'Obtain ScriptHookV from its author: https://www.dev-c.com/gtav/scripthookv/',
    'No ScriptHookV SDK, runtime, game executable, game assets or third-party trainer binaries are redistributed.',
    '',
    'MinHook v1.3.4 (BSD-2-Clause with retained HDE notices): https://github.com/TsudaKageyu/minhook/tree/v1.3.4',
    (Get-Content -LiteralPath (Join-Path $hcNative 'vendor/minhook/LICENSE.txt') -Raw),
    '',
    'JSON for Modern C++ v3.12.0: https://github.com/nlohmann/json/tree/v3.12.0',
    (Get-Content -LiteralPath (Join-Path $hcNative 'vendor/nlohmann/LICENSE.MIT') -Raw)
) -join "`r`n"
$hcNotices | Set-Content -LiteralPath (Join-Path $hcSupport 'ThirdPartyNotices.txt') -Encoding utf8NoBOM
# Only native source is packaged, never research downloads, game files, binaries
# or SDK artifacts. Keep a complete corresponding source/build tree for GPL-2.0.
$hcSourceZip = Join-Path $hcSupport 'Source.zip'
$hcSourceEntries = Get-ChildItem -LiteralPath $hcNative -File -Recurse
if ($hcSourceEntries | Where-Object { $_.Extension -in '.dll','.asi','.exe','.o','.obj','.a' }) { throw 'A binary artifact was found in the GTA source tree.' }
Compress-Archive -Path (Join-Path $hcNative '*') -DestinationPath $hcSourceZip -Force
$hcFiles = @('HappyCheats.GtaV.asi', 'HappyCheatsGtaV/ThirdPartyNotices.txt', 'HappyCheatsGtaV/Source.zip') | ForEach-Object {
    @{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $hcPayload $_) -Algorithm SHA256).Hash }
}
@{ schemaVersion = 1; game = 'gta-v-enhanced'; version = '0.2.0'; files = $hcFiles } | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $hcPayload 'package.json') -Encoding utf8NoBOM
Write-Host "Built and fixture-tested GTA V Enhanced payload: $hcPayload"
