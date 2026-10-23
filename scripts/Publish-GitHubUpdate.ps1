[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$GitHubRepository = 'flazedude/happycheats',
    [string]$ReleaseDirectory,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GitHubRelease.Common.ps1')
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$ReleaseDirectory) { $ReleaseDirectory = Join-Path $hcRoot "artifacts/releases/$Version" }
$hcClient = $null; $hcLock = $null
try {
    $hcStage = Get-HcReleaseStage -Directory $ReleaseDirectory -Version $Version -Repository $GitHubRepository
    $hcConfig = Get-Content -LiteralPath (Join-Path $hcRoot 'artifacts/lan-updates/server.json') -Raw | ConvertFrom-Json
    Assert-HcReleaseStageTrust -Directory $ReleaseDirectory -Stage $hcStage -Config $hcConfig
    $hcLock = [IO.File]::Open((Join-Path $hcRoot 'artifacts/lan-updates/github-publish.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $null = Assert-HcReleaseSource -Root $hcRoot -Repository $GitHubRepository -ExpectedCommit $hcStage.commit
    $hcClient = New-HcGitHubClient
    $hcRelease = Assert-HcGitHubPreflight -Client $hcClient -Repository $GitHubRepository -Version $Version -SourceCommit $hcStage.commit
    $hcUploads = @($hcStage.files | Where-Object upload)
    $hcNames = @($hcUploads | ForEach-Object { [IO.Path]::GetFileName($_.path) })
    foreach ($hcRequired in @('latest.json',"HappyCheats-$Version-win-x64.zip",'HappyCheats.exe',"HappyCheats-$Version.exe", "HappyCheats-$Version-win-x64-single-exe.zip")) {
        if (@($hcNames | Where-Object { $_ -ceq $hcRequired }).Count -ne 1) { throw "Release stage must include exactly one $hcRequired upload." }
    }
    if (!$hcRelease) {
        $hcBody = Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'release-notes.md') -Raw
        $hcRelease = Invoke-HcGitHub -Client $hcClient -Method POST -Path "/repos/$GitHubRepository/releases" -Body @{
            tag_name="v$Version"; target_commitish=$hcStage.commit; name="HappyCheats $Version"
            body=$hcBody; draft=$true; prerelease=$false; make_latest='false'
        }
    }
    # Feed upload is last. A draft stays unavailable to public clients until all assets verify.
    foreach ($hcFile in ($hcUploads | Sort-Object { [IO.Path]::GetFileName($_.path) -eq 'latest.json' })) {
        $hcPath = Join-Path $ReleaseDirectory $hcFile.path
        $hcName = [IO.Path]::GetFileName($hcPath)
        $hcExisting = @($hcRelease.assets | Where-Object name -CEQ $hcName)
        if ($hcExisting.Count -gt 1) { throw "Duplicate GitHub asset: $hcName." }
        if ($hcExisting.Count -eq 1 -and $hcExisting[0].state -eq 'starter') {
            # GitHub may retain an empty starter after a failed upload. Only this
            # uncompleted asset in the same verified draft may be removed on retry.
            $hcCurrentDraft = Assert-HcGitHubPreflight -Client $hcClient -Repository $GitHubRepository -Version $Version -SourceCommit $hcStage.commit
            if (!$hcCurrentDraft -or $hcCurrentDraft.id -ne $hcRelease.id) { throw 'Draft changed while resuming the interrupted upload.' }
            $null = Invoke-HcGitHub -Client $hcClient -Method DELETE -Path "/repos/$GitHubRepository/releases/assets/$($hcExisting[0].id)"
            $hcExisting = @()
        }
        if ($hcExisting.Count -eq 1) { $hcAsset = $hcExisting[0] }
        else {
            Write-Host "Uploading $hcName ($($hcFile.size) bytes)..."
            $hcAsset = Add-HcGitHubAsset -Client $hcClient -Repository $GitHubRepository -ReleaseId $hcRelease.id -Path $hcPath
        }
        Assert-HcGitHubAsset -Client $hcClient -Repository $GitHubRepository -Asset $hcAsset -Path $hcPath
    }
    $null = Get-HcReleaseStage -Directory $ReleaseDirectory -Version $Version -Repository $GitHubRepository
    $null = Assert-HcReleaseSource -Root $hcRoot -Repository $GitHubRepository -ExpectedCommit $hcStage.commit
    $hcRelease = Assert-HcGitHubPreflight -Client $hcClient -Repository $GitHubRepository -Version $Version -SourceCommit $hcStage.commit
    if (!$hcRelease) { throw 'Prepared GitHub draft disappeared.' }
    foreach ($hcFile in $hcUploads) {
        $hcName = [IO.Path]::GetFileName($hcFile.path)
        $hcAsset = @($hcRelease.assets | Where-Object name -CEQ $hcName)
        if ($hcAsset.Count -ne 1) { throw "Prepared draft is missing $hcName." }
        Assert-HcGitHubAsset -Client $hcClient -Repository $GitHubRepository -Asset $hcAsset[0] -Path (Join-Path $ReleaseDirectory $hcFile.path)
    }
    if ($PrepareOnly) { Write-Host "GitHub v$Version draft is complete and verified; publication is pending LAN completion."; return }
    # Direct recovery invocation still requires the identical LAN release to be ready.
    foreach ($hcPair in @(@('lan/latest.json','latest.json'), @("lan/HappyCheats-$Version-win-x64.zip","HappyCheats-$Version-win-x64.zip"), @('assets/HappyCheats.exe','HappyCheats.exe'))) {
        if ((Get-FileHash -LiteralPath (Join-Path $ReleaseDirectory $hcPair[0])).Hash -cne (Get-FileHash -LiteralPath (Join-Path $hcConfig.PublicDirectory $hcPair[1])).Hash) { throw 'The identical LAN release must be ready before making GitHub latest.' }
    }
    $hcPublished = Invoke-HcGitHub -Client $hcClient -Method PATCH -Path "/repos/$GitHubRepository/releases/$($hcRelease.id)" -Body @{ draft=$false; make_latest='true' }
    if ($hcPublished.draft) { throw 'GitHub did not publish the prepared release.' }
    Write-Host "Published GitHub release: $($hcPublished.html_url)"
} finally { if ($hcClient) { $hcClient.Dispose() }; if ($hcLock) { $hcLock.Dispose() } }
