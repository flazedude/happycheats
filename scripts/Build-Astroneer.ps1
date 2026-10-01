[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcArchive = Join-Path $hcRoot 'artifacts\astroneer\UE4SS_v3.0.1-1133-gb4cefa18.zip'
$hcArchiveHash = '89B7EED47C37D6FF6EAA144A41311A75098279A3454777F4EDD2446CEA1EA7A8'
$hcUrl = 'https://github.com/UE4SS-RE/RE-UE4SS/releases/download/experimental-latest/UE4SS_v3.0.1-1133-gb4cefa18.zip'
$hcExtract = Join-Path $hcRoot 'artifacts\astroneer\ue4ss'
$hcPackage = Join-Path $hcRoot 'payload\astroneer'
$hcBinary = Join-Path $hcPackage 'Astro\Binaries\Win64'
$hcMod = Join-Path $hcBinary 'ue4ss\Mods\HappyCheatsAstroneer'
New-Item -ItemType Directory -Force -Path (Split-Path $hcArchive),$hcExtract,$hcPackage,$hcBinary,(Join-Path $hcMod 'Scripts'),(Join-Path $hcMod 'bridge'),(Join-Path $hcPackage 'licenses') | Out-Null
if (!(Test-Path -LiteralPath $hcArchive)) { Invoke-WebRequest -Uri $hcUrl -OutFile $hcArchive }
if ((Get-FileHash -LiteralPath $hcArchive -Algorithm SHA256).Hash -ne $hcArchiveHash) { throw 'Astroneer UE4SS dependency checksum mismatch.' }
Expand-Archive -LiteralPath $hcArchive -DestinationPath $hcExtract -Force
Copy-Item -LiteralPath (Join-Path $hcExtract 'dwmapi.dll') -Destination $hcBinary -Force
Copy-Item -LiteralPath (Join-Path $hcExtract 'ue4ss\UE4SS.dll') -Destination (Join-Path $hcBinary 'ue4ss') -Force
$hcSettings = Get-Content -LiteralPath (Join-Path $hcExtract 'ue4ss\UE4SS-settings.ini') -Raw
$hcSettings = $hcSettings -replace '(?m)^MajorVersion\s*=.*$', 'MajorVersion = 4' -replace '(?m)^MinorVersion\s*=.*$', 'MinorVersion = 27'
[IO.File]::WriteAllText((Join-Path $hcBinary 'ue4ss\UE4SS-settings.ini'), $hcSettings)
[IO.File]::WriteAllText((Join-Path $hcBinary 'ue4ss\Mods\mods.txt'), "HappyCheatsAstroneer : 1`n")
foreach ($hcScript in @('main','bindings','collection','protocol')) {
    Copy-Item -LiteralPath (Join-Path $hcRoot ('modules\astroneer\Scripts\' + $hcScript + '.lua')) -Destination (Join-Path $hcMod 'Scripts') -Force
}
[IO.File]::WriteAllText((Join-Path $hcMod 'bridge\README.txt'), "HappyCheats runtime mailbox. Commands expire after eight seconds and are bound to the current locally hosted game session.`n")
Copy-Item -LiteralPath (Join-Path $hcExtract 'ue4ss\LICENSE') -Destination (Join-Path $hcPackage 'licenses\UE4SS-MIT.txt') -Force
$hcProvenance = [ordered]@{ dependency='UE4SS'; version='3.0.1-1133-gb4cefa18'; source_commit='b4cefa18'; source='https://github.com/UE4SS-RE/RE-UE4SS/tree/b4cefa18'; archive_url=$hcUrl; archive_sha256=$hcArchiveHash; license='MIT'; retrieved_utc='2026-09-13'; api_reference='https://docs.ue4ss.com/'; game_bindings_reference='https://github.com/AstroTechies/ModdingKit' }
$hcProvenance | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $hcPackage 'licenses\provenance.json') -Encoding utf8NoBOM
$hcFiles = @(Get-ChildItem -LiteralPath (Join-Path $hcPackage 'Astro') -File -Recurse | Sort-Object FullName | ForEach-Object { [ordered]@{ path=[IO.Path]::GetRelativePath($hcPackage,$_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
[ordered]@{ version='0.1.3'; app_id='361420'; game_version='1.43.2.0'; steam_build='24409322'; executable_sha256='E1764F108E51BB172148F3A4AC71AE38AC031D9ECEB41AD213E139A9CBA61F56'; files=$hcFiles } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $hcPackage 'package.json') -Encoding utf8NoBOM
Write-Host 'Built Astroneer payload with pinned UE4SS and verified file hashes.'
