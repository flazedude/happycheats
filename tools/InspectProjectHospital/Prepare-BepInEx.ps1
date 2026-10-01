[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$phRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$phCache = Join-Path $phRoot '.tools\bepinex\5.4.23.5-x64'
$phArchive = Join-Path $phCache 'BepInEx_win_x64_5.4.23.5.zip'
$phExpected = '82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'
$phUrl = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip'
New-Item -ItemType Directory -Path $phCache -Force | Out-Null
if (!(Test-Path -LiteralPath $phArchive)) { Invoke-WebRequest -Uri $phUrl -OutFile $phArchive }
if ((Get-FileHash -LiteralPath $phArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $phExpected) { throw 'Pinned x64 BepInEx archive hash mismatch.' }
Expand-Archive -LiteralPath $phArchive -DestinationPath (Join-Path $phCache 'extracted') -Force
$phLicense = Join-Path $phCache 'LICENSE.txt'
if (!(Test-Path -LiteralPath $phLicense)) { Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/BepInEx/BepInEx/v5.4.23.5/LICENSE' -OutFile $phLicense }
if ((Get-FileHash -LiteralPath $phLicense -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'e6e534ef6f4347b6449407ee046a3d09cb0174c6f688c996ad0bed94b74b3933') { throw 'Pinned BepInEx license hash mismatch.' }
[ordered]@{
    name = 'BepInEx'; version = '5.4.23.5'; architecture = 'win-x64'
    source = $phUrl; sha256 = $phExpected
    upstream_release = 'https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5'
    verification = 'SHA-256 pinned to official GitHub release asset digest, inspected 2026-09-27'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $phCache 'provenance.json') -Encoding utf8
Write-Output (Join-Path $phCache 'extracted')
