[CmdletBinding()]
param(
    [switch]$SkipPayloads,
    [switch]$SkipTests,
    [switch]$FrameworkDependent,
    [switch]$Zip,
    [switch]$SingleExe,
    [string]$ChineseParentsGameDirectory,
    [string]$StardewGameDirectory,
    [string]$ProjectHospitalGameDirectory,
    [string]$Version = '0.3.55',
    [string]$UpdateFeedUrl,
    [string]$UpdateFallbackFeedUrl,
    [string[]]$UpdateFeedUrls = @(),
    [string]$UpdatePublicKeyPath,
    [string]$ReleaseSourceCommit
)
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Version must be major.minor.patch without leading zeroes, such as 0.2.0.' }
$hcParsedVersion = [Version]::Parse($Version)
if ($hcParsedVersion.Major -gt 65534 -or $hcParsedVersion.Minor -gt 65534 -or $hcParsedVersion.Build -gt 65534) { throw 'Version components cannot exceed 65534 (assembly version limit).' }
if ([bool]$UpdateFeedUrl -ne [bool]$UpdatePublicKeyPath) { throw 'Supply both UpdateFeedUrl and UpdatePublicKeyPath to configure server updates.' }
if ($UpdateFallbackFeedUrl -and !$UpdateFeedUrl) { throw 'UpdateFallbackFeedUrl requires the primary feed and public key.' }
if ($UpdateFeedUrls.Count -and !$UpdateFeedUrl) { throw 'UpdateFeedUrls requires the compatibility primary feed and public key.' }
if ($ReleaseSourceCommit -and $ReleaseSourceCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'ReleaseSourceCommit must be a full Git commit SHA.' }
if ($SingleExe -and $FrameworkDependent) { throw 'SingleExe requires a self-contained release; omit FrameworkDependent.' }
if ($UpdatePublicKeyPath -and !(Test-Path -LiteralPath $UpdatePublicKeyPath -PathType Leaf)) { throw 'UpdatePublicKeyPath must point to the public verification PEM file.' }
if (!$SkipPayloads) {
    if ($ChineseParentsGameDirectory) { & (Join-Path $PSScriptRoot 'Build-ChineseParents.ps1') -GameDirectory $ChineseParentsGameDirectory }
    else { & (Join-Path $PSScriptRoot 'Build-ChineseParents.ps1') }
    & (Join-Path $PSScriptRoot 'Build-Onimusha.ps1')
    & (Join-Path $PSScriptRoot 'Build-Laundry.ps1')
    & (Join-Path $PSScriptRoot 'Build-Astroneer.ps1')
    & (Join-Path $PSScriptRoot 'Build-Dawnwalker.ps1')
    & (Join-Path $PSScriptRoot 'Build-CrimsonDesert.ps1')
    & (Join-Path $PSScriptRoot 'Build-Wukong.ps1')
    & (Join-Path $PSScriptRoot 'Build-PhantomBlade.ps1')
    & (Join-Path $PSScriptRoot 'Build-Rdr2.ps1')
    & (Join-Path $PSScriptRoot 'Build-GtaV.ps1')
    & (Join-Path $PSScriptRoot 'Build-HotelArchitect.ps1')
    & (Join-Path $PSScriptRoot 'Build-CivilizationVI.ps1')
    & (Join-Path $PSScriptRoot 'Build-CivilizationVII.ps1')
    & (Join-Path $PSScriptRoot 'Build-Witcher3.ps1')
    & (Join-Path $PSScriptRoot 'Build-EldenRing.ps1')
    if ($ProjectHospitalGameDirectory) { & (Join-Path $PSScriptRoot 'Build-ProjectHospital.ps1') -GameDirectory $ProjectHospitalGameDirectory }
    else { & (Join-Path $PSScriptRoot 'Build-ProjectHospital.ps1') }
    if ($StardewGameDirectory) { & (Join-Path $PSScriptRoot 'Build-Stardew.ps1') -GameDirectory $StardewGameDirectory }
    else { & (Join-Path $PSScriptRoot 'Build-Stardew.ps1') }
}
& (Join-Path $PSScriptRoot 'Prepare-SmapiBundle.ps1') -ValidateOnly
if (!$SkipTests) {
    & (Join-Path $PSScriptRoot 'Test.ps1')
}
$hcDist = Join-Path $hcRoot 'dist\HappyCheats'
$hcStaging = [IO.Path]::GetFullPath((Join-Path $hcRoot ('artifacts\package-stage\' + [Guid]::NewGuid().ToString('N') + '\HappyCheats')))
$hcPrevious = [IO.Path]::GetFullPath((Join-Path $hcRoot ('artifacts\package-backups\' + [Guid]::NewGuid().ToString('N') + '\HappyCheats')))
$hcStageRoot = [IO.Path]::GetFullPath((Join-Path $hcRoot 'artifacts\package-stage')) + [IO.Path]::DirectorySeparatorChar
$hcBackupRoot = [IO.Path]::GetFullPath((Join-Path $hcRoot 'artifacts\package-backups')) + [IO.Path]::DirectorySeparatorChar
if (!$hcStaging.StartsWith($hcStageRoot, [StringComparison]::OrdinalIgnoreCase) -or !$hcPrevious.StartsWith($hcBackupRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFullPath($hcDist) -ne (Join-Path $hcRoot 'dist\HappyCheats')) { throw 'Invalid package output location.' }
$hcContained = if ($FrameworkDependent) { 'false' } else { 'true' }
& dotnet publish (Join-Path $hcRoot 'src\HappyCheats.App') -c Release -r win-x64 --self-contained $hcContained "-p:Version=$Version" -o $hcStaging --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
& dotnet publish (Join-Path $hcRoot 'src\HappyCheats.Updater') -c Release -r win-x64 --self-contained true "-p:Version=$Version" -o (Join-Path $hcStaging 'updater') --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed.' }
if ($UpdateFeedUrl) {
    $hcConfigureArgs = @('configure','--feed-url',$UpdateFeedUrl,'--public-key',$UpdatePublicKeyPath,'--output',(Join-Path $hcStaging 'update-settings.json'))
    if ($UpdateFallbackFeedUrl) { $hcConfigureArgs += @('--fallback-feed-url',$UpdateFallbackFeedUrl) }
    foreach ($hcFeedSource in $UpdateFeedUrls) { $hcConfigureArgs += @('--feed-source',$hcFeedSource) }
    & dotnet run --project (Join-Path $hcRoot 'tools\HappyCheats.Release') -c Release -- @hcConfigureArgs
    if ($LASTEXITCODE -ne 0) { throw 'Update configuration failed.' }
    # Defaults permit migration of official endpoints only when retained trust matches.
    Copy-Item -LiteralPath (Join-Path $hcStaging 'update-settings.json') -Destination (Join-Path $hcStaging 'update-defaults.json')
}
if ($ReleaseSourceCommit) {
    if ((& git -C $hcRoot rev-parse HEAD) -cne $ReleaseSourceCommit -or $LASTEXITCODE -ne 0 -or
        (& git -C $hcRoot status --porcelain --untracked-files=all)) { throw 'Release source changed during the build. Commit and review it before publishing.' }
    [ordered]@{ version=$Version; commit=$ReleaseSourceCommit } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hcStaging 'release-source.json') -Encoding utf8NoBOM
}
foreach ($hcGame in @('chinese-parents','onimusha','laundry','astroneer','stardew','dawnwalker','crimson-desert','black-myth-wukong','phantom-blade-zero','red-dead-redemption-2','gta-v-enhanced','project-hospital','hotel-architect','civilization-vi','civilization-vii','witcher-3','elden-ring')) {
    $hcPayload = Join-Path $hcRoot ('payload\' + $hcGame)
    if (!(Test-Path -LiteralPath $hcPayload)) { throw "Missing payload: $hcGame" }
    New-Item -ItemType Directory -Force -Path (Join-Path $hcStaging 'payload') | Out-Null
    Copy-Item -LiteralPath $hcPayload -Destination (Join-Path $hcStaging 'payload') -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $hcRoot 'README.md') -Destination $hcStaging -Force
Copy-Item -LiteralPath (Join-Path $hcRoot 'README.zh-CN.md') -Destination $hcStaging -Force
if (Test-Path -LiteralPath (Join-Path $hcRoot 'docs')) { Copy-Item -LiteralPath (Join-Path $hcRoot 'docs') -Destination $hcStaging -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Split-Path $hcDist -Parent) | Out-Null
if (Test-Path -LiteralPath $hcDist) {
    if ((Get-Item -LiteralPath $hcDist).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to replace a linked distribution directory.' }
    New-Item -ItemType Directory -Force -Path (Split-Path $hcPrevious -Parent) | Out-Null
    Move-Item -LiteralPath $hcDist -Destination $hcPrevious
}
try { Move-Item -LiteralPath $hcStaging -Destination $hcDist }
catch {
    if (!(Test-Path -LiteralPath $hcDist) -and (Test-Path -LiteralPath $hcPrevious)) { Move-Item -LiteralPath $hcPrevious -Destination $hcDist }
    throw
}
# Preserve the previous build in artifacts/package-backups for recovery.
if ($Zip) {
    $hcArchive = Join-Path $hcRoot ("dist\HappyCheats-$Version-win-x64.zip")
    Compress-Archive -LiteralPath $hcDist -DestinationPath $hcArchive -Force
    (Get-FileHash -LiteralPath $hcArchive -Algorithm SHA256).Hash | Set-Content -LiteralPath ($hcArchive + '.sha256')
}
Write-Host "Built launcher: $hcDist\HappyCheats.exe"
if ($SingleExe) {
    & (Join-Path $PSScriptRoot 'Build-SingleExe.ps1') -PackageDirectory $hcDist -SkipTests:$SkipTests -Zip:$Zip
}
