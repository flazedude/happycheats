[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$cpRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$cpCache = Join-Path $cpRoot '.tools\bepinex\5.4.23.5'
$cpArchive = Join-Path $cpCache 'BepInEx_win_x86_5.4.23.5.zip'
$cpExpected = '37651c79e40d6f909572a4f461ac25350bb3ef8fe7fbd29f1aa8791a33b84c82'
$cpUrl = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x86_5.4.23.5.zip'
New-Item -ItemType Directory -Path $cpCache -Force | Out-Null
if (!(Test-Path -LiteralPath $cpArchive)) {
    Invoke-WebRequest -Uri $cpUrl -OutFile $cpArchive
}
$cpActual = (Get-FileHash -LiteralPath $cpArchive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($cpActual -ne $cpExpected) { throw 'Pinned BepInEx archive hash mismatch. The cached file must be inspected before using it.' }
Expand-Archive -LiteralPath $cpArchive -DestinationPath (Join-Path $cpCache 'extracted') -Force
$cpLicense = Join-Path $cpCache 'LICENSE.txt'
if (!(Test-Path -LiteralPath $cpLicense)) {
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/BepInEx/BepInEx/v5.4.23.5/LICENSE' -OutFile $cpLicense
}
if ((Get-FileHash -LiteralPath $cpLicense -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'e6e534ef6f4347b6449407ee046a3d09cb0174c6f688c996ad0bed94b74b3933') {
    throw 'Pinned BepInEx license hash mismatch.'
}
$cpProvenance = [ordered]@{
    name = 'BepInEx'; version = '5.4.23.5'; architecture = 'win-x86'
    source = $cpUrl; sha256 = $cpExpected
    upstream_release = 'https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5'
    verification = 'SHA-256 pinned to the official GitHub release asset digest, inspected 2026-09-13'
}
$cpProvenance | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $cpCache 'provenance.json') -Encoding utf8
Write-Output (Join-Path $cpCache 'extracted')
