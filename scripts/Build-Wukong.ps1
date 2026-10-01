[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcArchive = Join-Path $hcRoot 'artifacts\astroneer\UE4SS_v3.0.1-1133-gb4cefa18.zip'
$hcArchiveHash = '89B7EED47C37D6FF6EAA144A41311A75098279A3454777F4EDD2446CEA1EA7A8'
$hcUrl = 'https://github.com/UE4SS-RE/RE-UE4SS/releases/download/experimental-latest/UE4SS_v3.0.1-1133-gb4cefa18.zip'
$hcExtract = Join-Path $hcRoot 'artifacts\wukong\ue4ss'
$hcPackage = Join-Path $hcRoot 'payload\black-myth-wukong'
$hcBinary = Join-Path $hcPackage 'b1\Binaries\Win64'
$hcMod = Join-Path $hcBinary 'ue4ss\Mods\HappyCheatsWukong'
New-Item -ItemType Directory -Force -Path (Split-Path $hcArchive),$hcExtract,$hcBinary,(Join-Path $hcMod 'Scripts'),(Join-Path $hcMod 'bridge'),(Join-Path $hcPackage 'licenses') | Out-Null
if (!(Test-Path -LiteralPath $hcArchive)) { Invoke-WebRequest -Uri $hcUrl -OutFile $hcArchive }
if ((Get-FileHash -LiteralPath $hcArchive -Algorithm SHA256).Hash -ne $hcArchiveHash) { throw 'Wukong UE4SS dependency checksum mismatch.' }
Expand-Archive -LiteralPath $hcArchive -DestinationPath $hcExtract -Force
Copy-Item -LiteralPath (Join-Path $hcExtract 'dwmapi.dll') -Destination $hcBinary -Force
Copy-Item -LiteralPath (Join-Path $hcExtract 'ue4ss\UE4SS.dll') -Destination (Join-Path $hcBinary 'ue4ss') -Force
Copy-Item -LiteralPath (Join-Path $hcRoot 'modules\wukong\VTableLayout.ini') -Destination (Join-Path $hcBinary 'ue4ss\VTableLayout.ini') -Force
$hcSettings = Get-Content -LiteralPath (Join-Path $hcExtract 'ue4ss\UE4SS-settings.ini') -Raw
# Existing game crash metadata identifies the UE5.0 family; loader startup was
# observed on the pinned installed build. Only native tick/event dispatch is
# needed. No optional script hooks, console unlocking, or mod hot reload.
$hcSettings = $hcSettings -replace '(?m)^MajorVersion\s*=.*$', 'MajorVersion = 5' -replace '(?m)^MinorVersion\s*=.*$', 'MinorVersion = 0'
$hcSettings = $hcSettings -replace '(?m)^(Hook\w+)\s*=.*$', '$1 = 0'
$hcSettings = $hcSettings -replace '(?m)^HookEngineTick\s*=.*$', 'HookEngineTick = 1' -replace '(?m)^HookUObjectProcessEvent\s*=.*$', 'HookUObjectProcessEvent = 1'
[IO.File]::WriteAllText((Join-Path $hcBinary 'ue4ss\UE4SS-settings.ini'), $hcSettings)
[IO.File]::WriteAllText((Join-Path $hcBinary 'ue4ss\Mods\mods.txt'), "HappyCheatsWukong : 1`n")
foreach ($hcScript in @('main','bindings','protocol')) {
    Copy-Item -LiteralPath (Join-Path $hcRoot "modules\wukong\Scripts\$hcScript.lua") -Destination (Join-Path $hcMod 'Scripts') -Force
}
[IO.File]::WriteAllText((Join-Path $hcMod 'bridge\README.txt'), "Session-bound HappyCheats mailbox. Commands expire after eight seconds; progression is dispatched once with no automatic retry.`n")
Copy-Item -LiteralPath (Join-Path $hcExtract 'ue4ss\LICENSE') -Destination (Join-Path $hcPackage 'licenses\UE4SS-MIT.txt') -Force
[ordered]@{dependency='UE4SS';version='3.0.1-1133-gb4cefa18';archive_url=$hcUrl;archive_sha256=$hcArchiveHash;license='MIT';api_reference='https://docs.ue4ss.com/';game_bindings_reference='docs/wukong-bindings.md; external reference metadata and local installed executable hash; loader startup observed; gameplay effects unverified'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hcPackage 'licenses\provenance.json') -Encoding utf8NoBOM
$hcRuntimeFiles = @('dwmapi.dll','ue4ss/UE4SS.dll','ue4ss/VTableLayout.ini','ue4ss/UE4SS-settings.ini','ue4ss/Mods/mods.txt','ue4ss/Mods/HappyCheatsWukong/Scripts/main.lua','ue4ss/Mods/HappyCheatsWukong/Scripts/bindings.lua','ue4ss/Mods/HappyCheatsWukong/Scripts/protocol.lua','ue4ss/Mods/HappyCheatsWukong/bridge/README.txt')
$hcFiles = @($hcRuntimeFiles | ForEach-Object { $hcPath = Join-Path $hcBinary $_; [ordered]@{path=[IO.Path]::GetRelativePath($hcPackage,$hcPath).Replace('\','/');sha256=(Get-FileHash -LiteralPath $hcPath -Algorithm SHA256).Hash} })
[ordered]@{version='0.1.1';app_id='2358720';steam_build='21393610';executable_sha256='CBA6127DF85FE6AEDF5A71B398F268C8A963C7B19582C3CAC087462518358E9A';files=$hcFiles} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $hcPackage 'package.json') -Encoding utf8NoBOM
Write-Host 'Built Wukong 0.1.1 payload with pinned UE4SS and verified file hashes.'
