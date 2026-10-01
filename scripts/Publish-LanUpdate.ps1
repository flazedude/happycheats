[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$NotesEnglish,
    [string]$NotesChinese,
    [string[]]$NewGame = @(),
    [switch]$SkipPayloads,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcConfigPath = Join-Path $hcRoot 'artifacts\lan-updates\server.json'
if (!(Test-Path -LiteralPath $hcConfigPath)) { throw 'Run Start-LanUpdateServer.ps1 first to set up the feed and signing key.' }
$hcConfig = Get-Content -LiteralPath $hcConfigPath -Raw | ConvertFrom-Json
if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Use a major.minor.patch version.' }
foreach ($hcNotes in @($NotesEnglish, $NotesChinese)) { if ($hcNotes -and !(Test-Path -LiteralPath $hcNotes -PathType Leaf)) { throw "Missing changelog: $hcNotes" } }
New-Item -ItemType Directory -Force -Path $hcConfig.PublicDirectory | Out-Null
$hcLock = $null
try {
    $hcLock = [IO.File]::Open((Join-Path (Split-Path $hcConfigPath) 'publish.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::Write, [IO.FileShare]::None)
    if (Test-Path -LiteralPath (Join-Path $hcConfig.PublicDirectory "HappyCheats-$Version-win-x64.zip")) { throw 'This version is already published. Choose a higher version.' }
    $hcFeed = Join-Path $hcConfig.PublicDirectory 'latest.json'
    if (Test-Path -LiteralPath $hcFeed) {
        $hcEnvelope = Get-Content -LiteralPath $hcFeed -Raw | ConvertFrom-Json
        $hcLast = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($hcEnvelope.payload)) | ConvertFrom-Json
        if ([Version]$Version -le [Version]$hcLast.version) { throw "Version must be newer than $($hcLast.version)." }
    }
    & (Join-Path $PSScriptRoot 'Build.ps1') -Version $Version -Zip -SingleExe -SkipPayloads:$SkipPayloads -SkipTests:$SkipTests -UpdateFeedUrl ($hcConfig.BaseUrl + '/latest.json') -UpdatePublicKeyPath $hcConfig.PublicKeyPath
    # Keep a directly usable, versioned standalone EXE as well as the latest EXE and ZIPs.
    # This is the complete bootstrap bundle, not dist/HappyCheats/HappyCheats.exe.
    $hcVersionedExe = Join-Path $hcRoot "dist\HappyCheats-$Version.exe"
    $hcVersionedTemporary = $hcVersionedExe + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        Copy-Item -LiteralPath (Join-Path $hcRoot 'dist\HappyCheats.exe') -Destination $hcVersionedTemporary
        [IO.File]::Move($hcVersionedTemporary, $hcVersionedExe, $true)
    } finally { if (Test-Path -LiteralPath $hcVersionedTemporary) { Remove-Item -LiteralPath $hcVersionedTemporary } }
    $hcPublishArgs = @('publish','--package',(Join-Path $hcRoot "dist\HappyCheats-$Version-win-x64.zip"),'--version',$Version,'--base-url',$hcConfig.BaseUrl,'--private-key',$hcConfig.PrivateKeyPath,'--output',$hcConfig.PublicDirectory,'--notes-en',[IO.Path]::GetFullPath($NotesEnglish))
    if ($NotesChinese) { $hcPublishArgs += @('--notes-zh',[IO.Path]::GetFullPath($NotesChinese)) }
    foreach ($hcGame in $NewGame) { $hcPublishArgs += @('--new-game',$hcGame) }
    & dotnet run --project (Join-Path $hcRoot 'tools\HappyCheats.Release') -c Release -- @hcPublishArgs
    if ($LASTEXITCODE -ne 0) { throw 'Signing/publishing the LAN release failed.' }
    $hcBootstrap = Join-Path $hcConfig.PublicDirectory 'HappyCheats.exe'
    $hcTemporary = $hcBootstrap + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    Copy-Item -LiteralPath (Join-Path $hcRoot 'dist\HappyCheats.exe') -Destination $hcTemporary
    [IO.File]::Move($hcTemporary, $hcBootstrap, $true)
    Write-Host "Published $Version. New PCs: $($hcConfig.BaseUrl)/HappyCheats.exe"
    Write-Host "Versioned standalone EXE: $hcVersionedExe"
    Write-Host 'Existing LAN clients: open Updates, download, then Install & restart.'
} finally { if ($hcLock) { $hcLock.Dispose() } }
