[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$NotesEnglish,
    [Parameter(Mandatory)][string]$NotesChinese,
    [string[]]$NewGame = @(),
    [string]$GitHubRepository = 'flazedude/happycheats',
    [switch]$PublishLan,
    [switch]$SkipPayloads,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GitHubRelease.Common.ps1')
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcConfig = Get-HcPublisherConfiguration -Root $hcRoot -Repository $GitHubRepository -RequireLan:$PublishLan
if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Use a major.minor.patch version.' }
foreach ($hcNotes in @($NotesEnglish, $NotesChinese)) { if (!(Test-Path -LiteralPath $hcNotes -PathType Leaf)) { throw "Missing changelog: $hcNotes" } }
foreach ($hcKeyPath in @($hcConfig.PublicKeyPath, $hcConfig.PrivateKeyPath)) { if (!(Test-Path -LiteralPath $hcKeyPath -PathType Leaf)) { throw 'Established signing key is missing. Restore it; no replacement key will be generated.' } }
$hcStageDirectory = Join-Path $hcRoot "artifacts/releases/$Version"
$hcZipName = "HappyCheats-$Version-win-x64.zip"
$hcGitHubBase = "https://github.com/$GitHubRepository/releases/download/v$Version"
$hcGitHubFeed = "https://github.com/$GitHubRepository/releases/latest/download/latest.json"
$hcClient = $null; $hcLock = $null
try {
    # Reuse the existing lock so legacy and GitHub-only publishers cannot share outputs.
    $hcLockDirectory = Join-Path $hcRoot 'artifacts/lan-updates'
    New-Item -ItemType Directory -Path $hcLockDirectory -Force | Out-Null
    $hcLock = [IO.File]::Open((Join-Path $hcLockDirectory 'publish.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $hcClient = New-HcGitHubClient
    $hcExpectedCommit = & git -C $hcRoot rev-parse HEAD
    $null = Assert-HcGitHubPreflight -Client $hcClient -Repository $GitHubRepository -Version $Version -SourceCommit $hcExpectedCommit
    $hcSourceCommit = Assert-HcReleaseSource -Root $hcRoot -Repository $GitHubRepository -ExpectedCommit $hcExpectedCommit -Push
    $hcStageExists = Test-Path -LiteralPath (Join-Path $hcStageDirectory 'release.json')
    $hcFeed = if ($hcConfig.PublicDirectory) { Join-Path $hcConfig.PublicDirectory 'latest.json' } else { $null }
    if ($hcFeed -and (Test-Path -LiteralPath $hcFeed)) {
        $hcEnvelope = Get-Content -LiteralPath $hcFeed -Raw | ConvertFrom-Json
        $hcLast = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($hcEnvelope.payload)) | ConvertFrom-Json
        if ([Version]$Version -lt [Version]$hcLast.version -or ([Version]$Version -eq [Version]$hcLast.version -and !$hcStageExists)) { throw "Version must be newer than $($hcLast.version). An interrupted staged release may resume unchanged." }
    }
    if (!$hcStageExists) {
        if ((Test-Path -LiteralPath $hcStageDirectory) -or ($hcConfig.PublicDirectory -and (Test-Path -LiteralPath (Join-Path $hcConfig.PublicDirectory $hcZipName)))) { throw 'This version already has release artifacts. Preserve them and choose a higher version.' }
        $hcAppProject = [xml](Get-Content -LiteralPath (Join-Path $hcRoot 'src/HappyCheats.App/HappyCheats.App.csproj') -Raw)
        if ($hcAppProject.Project.PropertyGroup.Version -cne $Version) { throw 'App project version must match the requested release.' }
        $hcBuildFeed = if ($PublishLan) { $hcConfig.BaseUrl + '/latest.json' } else { $hcGitHubFeed }
        $hcBuildFallback = if ($PublishLan) { $hcGitHubFeed } else { $null }
        & (Join-Path $PSScriptRoot 'Build.ps1') -Version $Version -Zip -SingleExe -SkipPayloads:$SkipPayloads -SkipTests:$SkipTests -UpdateFeedUrl $hcBuildFeed -UpdateFallbackFeedUrl $hcBuildFallback -UpdatePublicKeyPath $hcConfig.PublicKeyPath -ReleaseSourceCommit $hcSourceCommit
        $null = Assert-HcReleaseSource -Root $hcRoot -Repository $GitHubRepository -ExpectedCommit $hcSourceCommit
        $hcStageTemporary = Join-Path $hcRoot ('artifacts/releases/.' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $hcStageTemporary 'assets') -Force | Out-Null
        $hcTargets = ,@('github',$hcGitHubBase)
        if ($PublishLan) { $hcTargets += ,@('lan',$hcConfig.BaseUrl) }
        foreach ($hcTarget in $hcTargets) {
            $hcPublishArgs = @('publish','--package',(Join-Path $hcRoot "dist/$hcZipName"),'--version',$Version,'--base-url',$hcTarget[1],'--private-key',$hcConfig.PrivateKeyPath,'--output',(Join-Path $hcStageTemporary $hcTarget[0]),'--notes-en',[IO.Path]::GetFullPath($NotesEnglish),'--notes-zh',[IO.Path]::GetFullPath($NotesChinese))
            foreach ($hcGame in $NewGame) { $hcPublishArgs += @('--new-game',$hcGame) }
            & dotnet run --project (Join-Path $hcRoot 'tools/HappyCheats.Release') -c Release -- @hcPublishArgs
            if ($LASTEXITCODE -ne 0) { throw 'Signing staged release failed. Nothing has been published.' }
        }
        Copy-Item -LiteralPath (Join-Path $hcRoot 'dist/HappyCheats.exe') -Destination (Join-Path $hcStageTemporary 'assets/HappyCheats.exe')
        Copy-Item -LiteralPath (Join-Path $hcRoot 'dist/HappyCheats.exe') -Destination (Join-Path $hcStageTemporary "assets/HappyCheats-$Version.exe")
        foreach ($hcName in @("HappyCheats-$Version-win-x64-single-exe.zip", "HappyCheats-$Version-win-x64-single-exe.zip.sha256", "$hcZipName.sha256")) {
            Copy-Item -LiteralPath (Join-Path $hcRoot "dist/$hcName") -Destination (Join-Path $hcStageTemporary "assets/$hcName")
        }
        $hcReleaseNotes = (Get-Content -LiteralPath $NotesEnglish -Raw).Trim() + "`n`n---`n`n" + (Get-Content -LiteralPath $NotesChinese -Raw).Trim() + "`n`nSource commit: $hcSourceCommit`n"
        [IO.File]::WriteAllText((Join-Path $hcStageTemporary 'release-notes.md'), $hcReleaseNotes, [Text.UTF8Encoding]::new($false))
        $hcFiles = @(Get-ChildItem -LiteralPath $hcStageTemporary -File -Recurse | Sort-Object FullName | ForEach-Object {
            $hcRelative = [IO.Path]::GetRelativePath($hcStageTemporary, $_.FullName).Replace('\','/')
            [ordered]@{ path=$hcRelative; size=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName).Hash; upload=($hcRelative.StartsWith('github/') -or $hcRelative.StartsWith('assets/')) }
        })
        [ordered]@{ version=$Version; repository=$GitHubRepository; commit=$hcSourceCommit; publishLan=[bool]$PublishLan; lanBaseUrl=$(if ($PublishLan) { $hcConfig.BaseUrl } else { $null }); files=$hcFiles } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $hcStageTemporary 'release.json') -Encoding utf8NoBOM
        # Both directory paths are fixed children of artifacts/releases, including retries.
        $hcStageParent = [IO.Path]::GetFullPath((Join-Path $hcRoot 'artifacts/releases')).TrimEnd('\') + '\'
        if (![IO.Path]::GetFullPath($hcStageTemporary).StartsWith($hcStageParent) -or ![IO.Path]::GetFullPath($hcStageDirectory).StartsWith($hcStageParent)) { throw 'Invalid release stage path.' }
        [IO.Directory]::Move($hcStageTemporary, $hcStageDirectory)
    }
    $hcStage = Get-HcReleaseStage -Directory $hcStageDirectory -Version $Version -Repository $GitHubRepository
    if ($hcStage.commit -cne $hcSourceCommit -or (Test-HcReleaseIncludesLan $hcStage) -ne [bool]$PublishLan -or ($PublishLan -and $hcStage.lanBaseUrl -cne $hcConfig.BaseUrl)) { throw 'Staged source or publication targets changed. Resume using the original targets, or preserve the release stage and choose a new version.' }
    Assert-HcReleaseStageTrust -Directory $hcStageDirectory -Stage $hcStage -Config $hcConfig
    # Complete standalone copies are restored from the immutable stage during a retry.
    foreach ($hcName in @('HappyCheats.exe',"HappyCheats-$Version.exe")) {
        Copy-Item -LiteralPath (Join-Path $hcStageDirectory "assets/$hcName") -Destination (Join-Path $hcRoot "dist/$hcName") -Force
    }
    & (Join-Path $PSScriptRoot 'Publish-GitHubUpdate.ps1') -Version $Version -GitHubRepository $GitHubRepository -ReleaseDirectory $hcStageDirectory -PrepareOnly
    $null = Assert-HcReleaseSource -Root $hcRoot -Repository $GitHubRepository -ExpectedCommit $hcSourceCommit
    $null = Get-HcReleaseStage -Directory $hcStageDirectory -Version $Version -Repository $GitHubRepository
    if ($PublishLan) {
        New-Item -ItemType Directory -Force -Path $hcConfig.PublicDirectory | Out-Null
        $hcPublishedZip = Join-Path $hcConfig.PublicDirectory $hcZipName
        $hcStagedZip = Join-Path $hcStageDirectory "lan/$hcZipName"
        if (Test-Path -LiteralPath $hcPublishedZip) {
            if ((Get-FileHash -LiteralPath $hcPublishedZip).Hash -cne (Get-FileHash -LiteralPath $hcStagedZip).Hash) { throw 'Existing LAN archive differs. Published versions are immutable.' }
        } else {
            $hcZipTemporary = $hcPublishedZip + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
            Copy-Item -LiteralPath $hcStagedZip -Destination $hcZipTemporary
            [IO.File]::Move($hcZipTemporary, $hcPublishedZip, $false)
        }
        foreach ($hcPair in @(@('assets/HappyCheats.exe','HappyCheats.exe'), @('lan/latest.json','latest.json'))) {
            $hcDestination = Join-Path $hcConfig.PublicDirectory $hcPair[1]
            $hcTemporary = $hcDestination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
            Copy-Item -LiteralPath (Join-Path $hcStageDirectory $hcPair[0]) -Destination $hcTemporary
            if (Test-Path -LiteralPath $hcDestination) {
                $hcBackup = Join-Path $hcStageDirectory ($hcPair[1] + '.previous-' + [Guid]::NewGuid().ToString('N'))
                [IO.File]::Replace($hcTemporary, $hcDestination, $hcBackup)
            } else { [IO.File]::Move($hcTemporary, $hcDestination) }
        }
    }
    & (Join-Path $PSScriptRoot 'Publish-GitHubUpdate.ps1') -Version $Version -GitHubRepository $GitHubRepository -ReleaseDirectory $hcStageDirectory
    Write-Host "Published $Version to GitHub from $hcSourceCommit."
    if ($PublishLan) { Write-Host "Legacy LAN download: $($hcConfig.BaseUrl)/HappyCheats.exe" }
    Write-Host "GitHub download: $hcGitHubBase/HappyCheats.exe"
    Write-Host "Versioned standalone EXE: $(Join-Path $hcRoot "dist/HappyCheats-$Version.exe")"
} finally { if ($hcClient) { $hcClient.Dispose() }; if ($hcLock) { $hcLock.Dispose() } }
