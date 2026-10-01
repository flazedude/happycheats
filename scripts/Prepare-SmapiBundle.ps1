[CmdletBinding()]
param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$sdRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sdVersion = '4.5.2'
$sdArchiveName = "SMAPI-$sdVersion-installer.zip"
$sdExpected = 'dd01ddca7b566bfe0d3b3d2d03833496abc56c53da976241f2ab443f5484acc4'
$sdUrl = "https://github.com/Pathoschild/SMAPI/releases/download/$sdVersion/$sdArchiveName"
$sdOutput = Join-Path $sdRoot 'payload\stardew\smapi'
$sdLicenses = if ($ValidateOnly) { Join-Path $sdOutput 'licenses' } else { Join-Path $sdRoot 'modules\stardew\licenses' }
$sdArchive = if ($ValidateOnly) { Join-Path $sdOutput $sdArchiveName } else { Join-Path $sdRoot ".tools\smapi\$sdVersion\$sdArchiveName" }

# Source and notices travel with the binaries, including in the single-EXE release.
$sdProvenance = Join-Path $sdLicenses 'provenance.json'
if (!(Test-Path -LiteralPath $sdProvenance -PathType Leaf)) { throw 'The bundled SMAPI license/source manifest is missing.' }
$sdFiles = @((Get-Content -LiteralPath $sdProvenance -Raw | ConvertFrom-Json).files)
$sdSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($sdFile in $sdFiles) {
    if (!$sdFile.path -or [IO.Path]::GetFileName($sdFile.path) -cne $sdFile.path -or !$sdSeen.Add($sdFile.path) -or $sdFile.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid bundled SMAPI license/source manifest entry.' }
    $sdLicenseFile = Join-Path $sdLicenses $sdFile.path
    if (!(Test-Path -LiteralPath $sdLicenseFile -PathType Leaf) -or (Get-FileHash -LiteralPath $sdLicenseFile -Algorithm SHA256).Hash -ine $sdFile.sha256) { throw "SMAPI license/source checksum mismatch: $($sdFile.path)" }
}
foreach ($sdRequired in @("SMAPI-$sdVersion-source.zip", 'SMAPI-LGPL-3.0.txt', 'GPL-3.0.txt', 'THIRD-PARTY-NOTICES.txt')) {
    if (!$sdSeen.Contains($sdRequired)) { throw "The SMAPI distribution is missing required source or notices: $sdRequired" }
}
if (!(Test-Path -LiteralPath $sdArchive -PathType Leaf)) {
    if ($ValidateOnly) { throw 'The offline SMAPI installer is missing. Run scripts/Prepare-SmapiBundle.ps1 before packaging.' }
    New-Item -ItemType Directory -Force -Path (Split-Path $sdArchive -Parent) | Out-Null
    # Network access is only needed by maintainers preparing a build, never by the launcher.
    Invoke-WebRequest -Uri $sdUrl -OutFile $sdArchive
}
if ((Get-FileHash -LiteralPath $sdArchive -Algorithm SHA256).Hash -ine $sdExpected) { throw 'SMAPI installer checksum does not match the pinned official release.' }
if (!$ValidateOnly) {
    New-Item -ItemType Directory -Force -Path $sdOutput | Out-Null
    Copy-Item -LiteralPath $sdArchive -Destination (Join-Path $sdOutput $sdArchiveName) -Force
    $sdNoticesOutput = Join-Path $sdOutput 'licenses'
    New-Item -ItemType Directory -Force -Path $sdNoticesOutput | Out-Null
    foreach ($sdFile in $sdFiles) { Copy-Item -LiteralPath (Join-Path $sdLicenses $sdFile.path) -Destination $sdNoticesOutput -Force }
    Copy-Item -LiteralPath $sdProvenance -Destination $sdNoticesOutput -Force
    $sdMetadata = [ordered]@{
        schema_version = 1; version = $sdVersion; project_url = 'https://smapi.io/'
        installer = $sdArchiveName; installer_sha256 = $sdExpected; source_url = $sdUrl
        license = 'LGPL-3.0'; notices = 'licenses/THIRD-PARTY-NOTICES.txt'; source = "licenses/SMAPI-$sdVersion-source.zip"
    }
    [IO.File]::WriteAllText((Join-Path $sdOutput 'bundle.json'), ($sdMetadata | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}
Write-Host "Verified offline SMAPI $sdVersion bundle, corresponding source and notices."
