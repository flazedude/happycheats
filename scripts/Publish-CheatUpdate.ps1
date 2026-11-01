# A cheat release changes only the game's package version and never releases/latest or the client EXE.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameId,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$NotesEnglish,
    [Parameter(Mandatory)][string]$NotesChinese,
    [string]$MinimumClientVersion='0.3.65',
    [switch]$SkipPayloads
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'GitHubRelease.Common.ps1')
. (Join-Path $PSScriptRoot 'GitHubPublishLock.ps1')
. (Join-Path $PSScriptRoot 'CheatUpdates.Common.ps1')
$hcRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcRepository='flazedude/happycheats'
if ($GameId -cnotmatch '^[a-z0-9]+(-[a-z0-9]+)*$' -or $GameId.Length -gt 80 -or $Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Use a game ID and independent major.minor.patch version.' }
$hcBuilders=@{'chinese-parents'='ChineseParents';'project-hospital'='ProjectHospital';'hotel-architect'='HotelArchitect';'civilization-vi'='CivilizationVI';'civilization-vii'='CivilizationVII';'elden-ring'='EldenRing';'onimusha'='Onimusha';'laundry'='Laundry';'astroneer'='Astroneer';'stardew'='Stardew';'dawnwalker'='Dawnwalker';'crimson-desert'='CrimsonDesert';'black-myth-wukong'='Wukong';'phantom-blade-zero'='PhantomBlade';'red-dead-redemption-2'='Rdr2';'gta-v-enhanced'='GtaV';'witcher-3'='Witcher3'}
if (!$hcBuilders.ContainsKey($GameId)) { throw 'Register the new game payload builder in Publish-CheatUpdate.ps1 before publishing.' }
foreach ($hcNote in @($NotesEnglish,$NotesChinese)) { if (!(Test-Path -LiteralPath $hcNote -PathType Leaf)) { throw "Missing release notes: $hcNote" } }
$hcConfig=Get-HcPublisherConfiguration -Root $hcRoot -RequireLan
$null=Get-HcLanFeedAsset $hcConfig.BaseUrl
$hcStage=Join-Path $hcRoot "artifacts/cheat-releases/$GameId/$Version"
$hcTool=Join-Path $hcRoot 'tools/HappyCheats.CheatRelease'
$hcClient=$null; $hcLock=$null; $hcRemoteLock=$null; $hcKey=$null
try {
    $hcState=Join-Path $hcRoot 'artifacts/lan-updates'; New-Item -ItemType Directory -Force -Path $hcState | Out-Null
    Assert-HcMirrorSafePath (Join-Path $hcState 'publish.lock')
    $hcLock=[IO.File]::Open((Join-Path $hcState 'publish.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::Write,[IO.FileShare]::None)
    $hcSource=Assert-HcReleaseSource -Root $hcRoot -Repository $hcRepository
    $hcClient=New-HcGitHubClient
    $hcRemoteLock=Enter-HcGitHubPublishLock -Client $hcClient -Repository $hcRepository -Version $Version -SourceCommit $hcSource
    $hcTag="cheat-$GameId-$Version"; $hcName="HappyCheats-cheat-$GameId-$Version.zip"
    $hcExisting=Invoke-HcGitHub -Client $hcClient -Path "/repos/$hcRepository/releases/tags/$hcTag" -AllowNotFound
    for ($hcPage=1;;$hcPage++) {
        $hcReleases=@(Invoke-HcGitHub -Client $hcClient -Path "/repos/$hcRepository/releases?per_page=100&page=$hcPage")
        foreach ($hcOther in $hcReleases) {
            if ($hcOther.tag_name -cmatch ('^cheat-'+[Regex]::Escape($GameId)+'-(\d+\.\d+\.\d+)$')) {
                if ([Version]$Matches[1] -gt [Version]$Version) { throw 'A higher cheat version is already published or reserved, including drafts.' }
                if ($hcOther.tag_name -ceq $hcTag) { if ($hcExisting -and $hcExisting.id -ne $hcOther.id) { throw 'Duplicate cheat release drafts.' }; $hcExisting=$hcOther }
            }
        }
        if ($hcReleases.Count -lt 100) { break }
    }
    foreach ($hcReserved in @(Invoke-HcGitHub -Client $hcClient -Path "/repos/$hcRepository/git/matching-refs/tags/cheat-$GameId-")) {
        if ($hcReserved.ref -cmatch ('^refs/tags/cheat-'+[Regex]::Escape($GameId)+'-(\d+\.\d+\.\d+)$') -and [Version]$Matches[1] -gt [Version]$Version) { throw 'A higher cheat tag reserves this game version.' }
        if ($hcReserved.ref -ceq "refs/tags/$hcTag" -and ($hcReserved.object.type -cne 'commit' -or $hcReserved.object.sha -cne $hcSource)) { throw 'Cheat tag belongs to another source commit.' }
    }
    $hcParent=Split-Path $hcStage -Parent
    if (Test-Path -LiteralPath $hcParent) {
        foreach ($hcReserved in Get-ChildItem -LiteralPath $hcParent -Directory) {
            if ($hcReserved.Name -match '^\d+\.\d+\.\d+$' -and [Version]$hcReserved.Name -gt [Version]$Version) { throw 'A higher local cheat version is staged. Preserve it and select an unused higher version.' }
        }
    }
    $hcStageMetadata=Join-Path $hcStage 'release.json'
    if (!(Test-Path -LiteralPath $hcStageMetadata)) {
        if ($hcExisting -or (Test-Path -LiteralPath $hcStage)) { throw 'This cheat version is reserved or has interrupted artifacts. Restore its complete original stage or select a higher version.' }
        if (!$SkipPayloads) { & (Join-Path $PSScriptRoot ("Build-$($hcBuilders[$GameId]).ps1")) }
        & (Join-Path $PSScriptRoot 'Test.ps1')
        $null=Assert-HcReleaseSource -Root $hcRoot -Repository $hcRepository -ExpectedCommit $hcSource
        & dotnet run --project $hcTool -c Release -- pack --root $hcRoot --game $GameId --version $Version --source $hcSource --minimum-client $MinimumClientVersion --notes-en ([IO.Path]::GetFullPath($NotesEnglish)) --notes-zh ([IO.Path]::GetFullPath($NotesChinese)) --output $hcStage
        if ($LASTEXITCODE -ne 0) { throw 'Cheat package preparation failed.' }
    }
    $hcRelease=Get-Content -LiteralPath $hcStageMetadata -Raw | ConvertFrom-Json
    if ($hcRelease.id -cne $GameId -or $hcRelease.version -cne $Version -or $hcRelease.sourceCommit -cne $hcSource -or $hcRelease.minimumClientVersion -cne $MinimumClientVersion) { throw 'Staged cheat identity/source changed. Resume its original stage without rebuilding.' }
    Assert-HcMirrorAsset (Join-Path $hcStage $hcName) @{name=$hcName;size=$hcRelease.packageSize;sha256=$hcRelease.sha256}
    $hcKey=Read-HcMirrorPublicKey $hcConfig.PublicKeyPath
    $hcRemoteIndex=Invoke-HcGitHub -Client $hcClient -Path "/repos/$hcRepository/contents/cheats.json?ref=cheat-feed" -AllowNotFound
    $hcCatalogArgs=@('catalog','--release',$hcStageMetadata,'--private-key',$hcConfig.PrivateKeyPath,'--public-key',$hcConfig.PublicKeyPath)
    if ($hcRemoteIndex) {
        $hcPrevious=Join-Path $hcStage ('previous-'+[Guid]::NewGuid().ToString('N')+'.json')
        [IO.File]::WriteAllBytes($hcPrevious,[Convert]::FromBase64String($hcRemoteIndex.content.Replace("`n",'')))
        $null=Read-HcCheatCatalog $hcPrevious $hcKey
        $hcCatalogArgs+=@('--previous',$hcPrevious)
    }
    $hcCatalogPath=Join-Path $hcStage ('cheats-'+[Guid]::NewGuid().ToString('N')+'.json')
    $hcCatalogArgs+=@('--output',$hcCatalogPath)
    & dotnet run --project $hcTool -c Release -- @hcCatalogArgs
    if ($LASTEXITCODE -ne 0) { throw 'Cheat catalog signing failed.' }
    $hcNext=Read-HcCheatCatalog $hcCatalogPath $hcKey
    foreach ($hcLanBase in @('http://192.168.2.76:17845/happycheats','http://192.168.2.59:17845/happycheats')) {
        try { $hcPeer=Invoke-WebRequest "$hcLanBase/cheats.json" -NoProxy -TimeoutSec 4 -SkipHttpErrorCheck }
        catch [Net.Http.HttpRequestException] { Write-Host "Cheat peer unavailable: $hcLanBase"; continue }
        catch [Threading.Tasks.TaskCanceledException] { Write-Host "Cheat peer timed out: $hcLanBase"; continue }
        if ($hcPeer.StatusCode -ne 200) { Write-Host "Cheat peer feed unavailable (HTTP $($hcPeer.StatusCode)): $hcLanBase"; continue }
        if ($hcPeer.RawContentLength -gt 524288) { throw 'LAN cheat catalog exceeds its limit.' }
        $hcPeerPath=Join-Path $hcStage ('peer-'+[Guid]::NewGuid().ToString('N')+'.json')
        [IO.File]::WriteAllText($hcPeerPath,[string]$hcPeer.Content)
        $hcPeerCatalog=Read-HcCheatCatalog $hcPeerPath $hcKey
        Assert-HcCheatCatalogAdvance $hcPeerCatalog $hcNext
        Write-Host "Verified cheat peer revision $($hcPeerCatalog.revision): $hcLanBase"
    }
    $hcHostedFeed=Join-Path $hcConfig.PublicDirectory 'cheats.json'
    if (Test-Path -LiteralPath $hcHostedFeed) { Assert-HcCheatCatalogAdvance (Read-HcCheatCatalog $hcHostedFeed $hcKey) $hcNext }
    # The first per-package proof is immutable, while the separate feed branch can advance atomically.
    $hcProof=Join-Path $hcStage 'cheat.json'
    if (!(Test-Path -LiteralPath $hcProof)) { Copy-Item -LiteralPath $hcCatalogPath -Destination $hcProof }
    $hcProofCatalog=Read-HcCheatCatalog $hcProof $hcKey
    $hcProofEntry=@($hcProofCatalog.games | Where-Object id -CEQ $GameId)
    if ($hcProofEntry.Count -ne 1 -or $hcProofEntry[0].sha256 -cne $hcRelease.sha256 -or $hcProofEntry[0].version -cne $Version) { throw 'Immutable cheat proof does not match the staged package.' }
    Assert-HcGitHubPublishLock -Client $hcClient -Lock $hcRemoteLock
    if (!$hcExisting) {
        $hcExisting=Invoke-HcGitHub -Client $hcClient -Method POST -Path "/repos/$hcRepository/releases" -Body @{
            tag_name=$hcTag;target_commitish=$hcSource;name="$($hcRelease.name) cheat $Version";draft=$true;prerelease=$true;make_latest='false'
            body=($hcRelease.notesEnglish+"`n`n---`n`n"+$hcRelease.notesChinese+"`n`nSource: $hcSource`nClient version unchanged.")
        }
    }
    foreach ($hcAssetPath in @((Join-Path $hcStage $hcName),$hcProof)) {
        $hcAssetName=[IO.Path]::GetFileName($hcAssetPath)
        $hcAssets=@($hcExisting.assets | Where-Object name -CEQ $hcAssetName)
        if ($hcAssets.Count -gt 1) { throw 'Duplicate cheat release asset.' }
        if (!$hcAssets.Count) {
            if (!$hcExisting.draft) { throw 'Published cheat release lacks an immutable asset; do not modify it.' }
            $hcAsset=Add-HcGitHubAsset -Client $hcClient -Repository $hcRepository -ReleaseId $hcExisting.id -Path $hcAssetPath
        } else { $hcAsset=$hcAssets[0] }
        Assert-HcGitHubAsset -Client $hcClient -Repository $hcRepository -Asset $hcAsset -Path $hcAssetPath
    }
    $null=Assert-HcReleaseSource -Root $hcRoot -Repository $hcRepository -ExpectedCommit $hcSource
    Assert-HcGitHubPublishLock -Client $hcClient -Lock $hcRemoteLock
    if ($hcExisting.draft) { $hcExisting=Invoke-HcGitHub -Client $hcClient -Method PATCH -Path "/repos/$hcRepository/releases/$($hcExisting.id)" -Body @{draft=$false;prerelease=$true;make_latest='false'} }
    if ($hcExisting.draft -or !$hcExisting.prerelease) { throw 'Independent cheat release did not retain its separate release track.' }
    $hcFeedBranch=Invoke-HcGitHub -Client $hcClient -Path "/repos/$hcRepository/git/ref/heads/cheat-feed" -AllowNotFound
    if (!$hcFeedBranch) { $null=Invoke-HcGitHub -Client $hcClient -Method POST -Path "/repos/$hcRepository/git/refs" -Body @{ref='refs/heads/cheat-feed';sha=$hcSource} }
    $hcIndexBody=@{message="Publish $GameId cheat $Version catalog";branch='cheat-feed';content=[Convert]::ToBase64String([IO.File]::ReadAllBytes($hcCatalogPath))}
    if ($hcRemoteIndex) { $hcIndexBody.sha=$hcRemoteIndex.sha }
    $null=Invoke-HcGitHub -Client $hcClient -Method PUT -Path "/repos/$hcRepository/contents/cheats.json" -Body $hcIndexBody
    $hcConfirmed=Invoke-HcGitHub -Client $hcClient -Path "/repos/$hcRepository/contents/cheats.json?ref=cheat-feed"
    if ([Convert]::ToBase64String([Convert]::FromBase64String($hcConfirmed.content.Replace("`n",''))) -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes($hcCatalogPath))) { throw 'GitHub cheat catalog verification failed.' }
    # Reuse the exact staged package locally; a peer downloads those same GitHub bytes.
    $hcCache=Join-Path $hcState 'cheat-cache'; New-Item -ItemType Directory -Force -Path $hcCache | Out-Null
    $hcCachePackage=Join-Path $hcCache $hcName
    if (!(Test-Path -LiteralPath $hcCachePackage)) { Copy-Item -LiteralPath (Join-Path $hcStage $hcName) -Destination $hcCachePackage }
    Install-HcCheatMirror $hcCatalogPath $hcConfig $hcCache
    Write-Host "Published independent cheat $GameId ${Version}: https://github.com/$hcRepository/releases/tag/$hcTag"
    Write-Host 'HappyCheats client EXE and client version were not rebuilt or replaced.'
} finally {
    try { if ($hcRemoteLock) { Exit-HcGitHubPublishLock -Client $hcClient -Lock $hcRemoteLock } }
    finally { if ($hcKey) { $hcKey.Dispose() }; if ($hcClient) { $hcClient.Dispose() }; if ($hcLock) { $hcLock.Dispose() } }
}
