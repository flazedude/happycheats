[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcArchive = Join-Path $hcRoot 'artifacts\astroneer\UE4SS_v3.0.1-1133-gb4cefa18.zip'
$hcArchiveHash = '89B7EED47C37D6FF6EAA144A41311A75098279A3454777F4EDD2446CEA1EA7A8'
$hcUrl = 'https://github.com/UE4SS-RE/RE-UE4SS/releases/download/experimental-latest/UE4SS_v3.0.1-1133-gb4cefa18.zip'
$hcExtract = Join-Path $hcRoot 'artifacts\dawnwalker\ue4ss'
$hcPackage = Join-Path $hcRoot 'payload\dawnwalker'
$hcBinary = Join-Path $hcPackage 'Dawnwalker\Binaries\Win64'
$hcMod = Join-Path $hcBinary 'ue4ss\Mods\HappyCheatsDawnwalker'
New-Item -ItemType Directory -Force -Path (Split-Path $hcArchive),$hcExtract,$hcPackage,$hcBinary,(Join-Path $hcMod 'Scripts'),(Join-Path $hcMod 'bridge'),(Join-Path $hcPackage 'licenses') | Out-Null
if (!(Test-Path -LiteralPath $hcArchive)) { Invoke-WebRequest -Uri $hcUrl -OutFile $hcArchive }
if ((Get-FileHash -LiteralPath $hcArchive -Algorithm SHA256).Hash -ne $hcArchiveHash) { throw 'Dawnwalker UE4SS dependency checksum mismatch.' }
Expand-Archive -LiteralPath $hcArchive -DestinationPath $hcExtract -Force
Copy-Item -LiteralPath (Join-Path $hcExtract 'dwmapi.dll') -Destination $hcBinary -Force
Copy-Item -LiteralPath (Join-Path $hcExtract 'ue4ss\UE4SS.dll') -Destination (Join-Path $hcBinary 'ue4ss') -Force
$hcSettings = Get-Content -LiteralPath (Join-Path $hcExtract 'ue4ss\UE4SS-settings.ini') -Raw
# This exact executable is pinned by the module/manifest and uses Unreal 5.5.4.
# Avoid startup retries with partially resolved automatic engine-version scans.
$hcSettings = $hcSettings -replace '(?m)^MajorVersion\s*=.*$', 'MajorVersion = 5' -replace '(?m)^MinorVersion\s*=.*$', 'MinorVersion = 5'
# In the previous build 25232147 the scanner mistook __security_check_cookie
# (RVA 0x3904B40) for ProcessLocalScriptFunction. Hooking it deadlocks/crashes
# startup. Use only the native event/tick hooks this trainer needs. LoadMap's
# optional callback bridge was also incompatible. Retain the minimal profile
# for patch 3; see docs/dawnwalker-patch3.md for current resolver evidence.
$hcSettings = $hcSettings -replace '(?m)^(Hook\w+)\s*=.*$', '$1 = 0'
$hcSettings = $hcSettings -replace '(?m)^HookEngineTick\s*=.*$', 'HookEngineTick = 1' -replace '(?m)^HookUObjectProcessEvent\s*=.*$', 'HookUObjectProcessEvent = 1'
[IO.File]::WriteAllText((Join-Path $hcBinary 'ue4ss\UE4SS-settings.ini'), $hcSettings)
[IO.File]::WriteAllText((Join-Path $hcBinary 'ue4ss\Mods\mods.txt'), "HappyCheatsDawnwalker : 1`n")
foreach ($hcScript in @('main','bindings','protocol','combat','movement','economy')) {
    Copy-Item -LiteralPath (Join-Path $hcRoot ('modules\dawnwalker\Scripts\' + $hcScript + '.lua')) -Destination (Join-Path $hcMod 'Scripts') -Force
}
[IO.File]::WriteAllText((Join-Path $hcMod 'bridge\README.txt'), "HappyCheats runtime mailbox. Commands expire after eight seconds and are bound to the current single-player session.`n")
Copy-Item -LiteralPath (Join-Path $hcExtract 'ue4ss\LICENSE') -Destination (Join-Path $hcPackage 'licenses\UE4SS-MIT.txt') -Force
$hcProvenance = [ordered]@{ dependency='UE4SS'; version='3.0.1-1133-gb4cefa18'; source_commit='2bfa839f'; source='https://github.com/UE4SS-RE/RE-UE4SS/tree/2bfa839f'; archive_url=$hcUrl; archive_sha256=$hcArchiveHash; license='MIT'; api_reference='https://docs.ue4ss.com/'; game_bindings_reference='Static inspection of Steam build 25915545 / CL-265636; current-build startup and gameplay validation pending' }
$hcProvenance | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $hcPackage 'licenses\provenance.json') -Encoding utf8NoBOM
# Enumerate only deliverables. Runtime mailboxes and probe output never enter a release.
$hcRuntimeFiles = @('dwmapi.dll','ue4ss/UE4SS.dll','ue4ss/UE4SS-settings.ini','ue4ss/Mods/mods.txt','ue4ss/Mods/HappyCheatsDawnwalker/Scripts/main.lua','ue4ss/Mods/HappyCheatsDawnwalker/Scripts/bindings.lua','ue4ss/Mods/HappyCheatsDawnwalker/Scripts/protocol.lua','ue4ss/Mods/HappyCheatsDawnwalker/bridge/README.txt','ue4ss/Mods/HappyCheatsDawnwalker/Scripts/combat.lua','ue4ss/Mods/HappyCheatsDawnwalker/Scripts/movement.lua','ue4ss/Mods/HappyCheatsDawnwalker/Scripts/economy.lua')
$hcFiles = @($hcRuntimeFiles | ForEach-Object { $hcPath = Join-Path $hcBinary $_; [ordered]@{ path=[IO.Path]::GetRelativePath($hcPackage,$hcPath).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $hcPath -Algorithm SHA256).Hash } })
[ordered]@{ version='0.4.0'; app_id='3751260'; steam_build='25915545'; executable_sha256='E6222B815B1C376F2C85A2189120C5A9B5CD30AC5688A34B61A2C18EFBC9B117'; files=$hcFiles } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $hcPackage 'package.json') -Encoding utf8NoBOM
Write-Host 'Built Dawnwalker payload with pinned UE4SS and verified file hashes.'
