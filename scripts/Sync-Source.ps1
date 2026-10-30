#Requires -Version 7.0
<#
.SYNOPSIS
Receives or publishes committed HappyCheats source through origin.
.DESCRIPTION
The default receives only fast-forward changes for the current main/codex branch.
-Publish explicitly shares committed changes on that same branch. -Status fetches
and reports without modifying the checkout. No files are staged or committed.
See docs/source-sync.md for cross-PC handoff and deliberate main integration.
#>
[CmdletBinding(DefaultParameterSetName = 'Receive')]
param(
    [string]$RepositoryPath = (Split-Path -Parent $PSScriptRoot),
    [Parameter(ParameterSetName = 'Publish')][switch]$Publish,
    [Parameter(ParameterSetName = 'Status')][switch]$Status
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$hcRoot = (Resolve-Path -LiteralPath $RepositoryPath).Path

function Invoke-HcSourceGit {
    param([string[]]$GitArguments)
    $hcOutput = @(& git -C $hcRoot @GitArguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Source sync: git $($GitArguments[0]) failed. $($hcOutput -join [Environment]::NewLine)"
    }
    return $hcOutput | ForEach-Object { [string]$_ }
}

function Get-HcSourceBranch {
    return [string](Invoke-HcSourceGit @('branch', '--show-current'))
}

function Get-HcSourceChanges {
    return @(Invoke-HcSourceGit @('status', '--porcelain=v1', '--untracked-files=all', '--ignore-submodules=none'))
}

function Get-HcSourceOperations {
    foreach ($hcMarker in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-merge', 'rebase-apply', 'sequencer', 'BISECT_LOG')) {
        $hcPath = [string](Invoke-HcSourceGit @('rev-parse', '--path-format=absolute', '--git-path', $hcMarker))
        if (Test-Path -LiteralPath $hcPath) { $hcMarker }
    }
}

function Assert-HcSourceUnchanged {
    param([string]$Branch, [string]$Commit)
    if ((Get-HcSourceBranch) -cne $Branch -or
        [string](Invoke-HcSourceGit @('rev-parse', 'HEAD')) -cne $Commit) {
        throw 'The checkout changed during source sync. Retry from its current state; no history was overwritten.'
    }
    if (@(Get-HcSourceOperations).Count -gt 0) {
        throw 'A merge, rebase, cherry-pick, revert or bisect is in progress. Complete it before syncing source.'
    }
    if (@(Get-HcSourceChanges).Count -gt 0) {
        throw 'Unfinished tracked, staged or untracked files were preserved. Review and commit the intended work, or use a separate clean worktree. Use -Status to inspect incoming source.'
    }
}

function Get-HcSourceState {
    param([string]$Branch)
    $hcLocal = [string](Invoke-HcSourceGit @('rev-parse', 'HEAD'))
    $hcRemoteRef = "refs/remotes/origin/$Branch"
    $hcRemoteCommit = ''
    if ($Branch) {
        $hcResolved = @(& git -C $hcRoot rev-parse --verify --quiet "$hcRemoteRef^{commit}" 2>&1)
        if ($LASTEXITCODE -eq 0) { $hcRemoteCommit = [string]$hcResolved[0] }
        elseif ($LASTEXITCODE -ne 1) { throw "Could not inspect origin/$Branch. Source sync stopped." }
    }
    $hcAhead = 0
    $hcBehind = 0
    if ($hcRemoteCommit) {
        $hcCounts = ([string](Invoke-HcSourceGit @('rev-list', '--left-right', '--count', "HEAD...$hcRemoteRef"))) -split '\s+'
        $hcAhead = [int]$hcCounts[0]
        $hcBehind = [int]$hcCounts[1]
    }
    $hcRelation = if (!$Branch) { 'Detached' }
        elseif (!$hcRemoteCommit) { 'Unpublished' }
        elseif ($hcAhead -gt 0 -and $hcBehind -gt 0) { 'Diverged' }
        elseif ($hcAhead -gt 0) { 'LocalAhead' }
        elseif ($hcBehind -gt 0) { 'RemoteAhead' }
        else { 'Current' }
    [pscustomobject]@{
        Computer = [Environment]::MachineName
        Repository = $hcRoot
        Branch = $Branch
        State = $hcRelation
        LocalCommit = $hcLocal
        RemoteCommit = $hcRemoteCommit
        Ahead = $hcAhead
        Behind = $hcBehind
        ChangedFiles = @(Get-HcSourceChanges).Count
        Operations = @(Get-HcSourceOperations)
    }
}

$hcRoot = [string](Invoke-HcSourceGit @('rev-parse', '--show-toplevel'))
$hcBranch = Get-HcSourceBranch
$hcStartingCommit = [string](Invoke-HcSourceGit @('rev-parse', 'HEAD'))
if (!$Status) {
    if (!$hcBranch) { throw 'Detached HEAD: switch deliberately to main or a codex/ task branch before syncing source.' }
    if ($hcBranch -cne 'main' -and !$hcBranch.StartsWith('codex/', [StringComparison]::Ordinal)) {
        throw 'Source sync supports main and codex/ task branches. The current branch was preserved.'
    }
    $hcUpstream = [string](Invoke-HcSourceGit @('for-each-ref', '--format=%(upstream)', "refs/heads/$hcBranch"))
    if ($hcUpstream -and $hcUpstream -cne "refs/remotes/origin/$hcBranch") {
        throw "Branch '$hcBranch' tracks a different branch. Inspect its upstream and deliberately correct it before syncing; source sync never redirects task work to main."
    }
    Assert-HcSourceUnchanged -Branch $hcBranch -Commit $hcStartingCommit
}

# Fetch only remote-tracking branches, leaving release tags and the publish lock alone.
$null = Invoke-HcSourceGit @('-c', 'fetch.pruneTags=false', '-c', 'remote.origin.pruneTags=false',
    'fetch', '--prune', '--no-tags', 'origin', '+refs/heads/*:refs/remotes/origin/*')
$hcState = Get-HcSourceState -Branch $hcBranch
if ($Status) { return $hcState }
Assert-HcSourceUnchanged -Branch $hcBranch -Commit $hcStartingCommit
if ($hcState.State -eq 'Diverged') {
    throw "Local '$hcBranch' and origin/$hcBranch have diverged ($($hcState.Ahead) local, $($hcState.Behind) remote commits). Merge deliberately, resolve conflicts and rerun affected checks before retrying. Nothing was reset, rebased or force-pushed."
}
if ($hcState.Behind -gt 0) {
    # Prevent merge.autostash from changing unrelated local state.
    $null = Invoke-HcSourceGit @('-c', 'merge.autostash=false', 'merge', '--ff-only', '--no-autostash', "refs/remotes/origin/$hcBranch")
    Write-Host "Received $($hcState.Behind) commit(s) on '$hcBranch'. Re-read AGENTS.md before continuing work."
    $hcState = Get-HcSourceState -Branch $hcBranch
}
if ($Publish) {
    Assert-HcSourceUnchanged -Branch $hcBranch -Commit $hcState.LocalCommit
    # Pin the reviewed snapshot and destination explicitly. Ignore broad default
    # push refspecs, mirror mode and automatic tag pushes in the user's Git config.
    $null = Invoke-HcSourceGit @('-c', 'push.followTags=false', '-c', 'remote.origin.mirror=false',
        'push', '--no-force', 'origin', "$($hcState.LocalCommit):refs/heads/$hcBranch")
    $null = Invoke-HcSourceGit @('-c', 'fetch.pruneTags=false', '-c', 'remote.origin.pruneTags=false',
        'fetch', '--prune', '--no-tags', 'origin', '+refs/heads/*:refs/remotes/origin/*')
    $hcRemoteCommit = [string](Invoke-HcSourceGit @('rev-parse', "refs/remotes/origin/$hcBranch"))
    $null = Invoke-HcSourceGit @('merge-base', '--is-ancestor', $hcState.LocalCommit, $hcRemoteCommit)
    $null = Invoke-HcSourceGit @('branch', "--set-upstream-to=origin/$hcBranch", $hcBranch)
    Write-Host "Committed source is available on origin/$hcBranch. The other PC receives it on its next source sync."
} elseif ($hcState.State -eq 'Unpublished') {
    Write-Host "No origin/$hcBranch exists yet. Use -Publish after reviewing and committing this branch."
} elseif ($hcState.Ahead -gt 0) {
    Write-Host "'$hcBranch' has $($hcState.Ahead) unpublished commit(s). Use -Publish to share reviewed commits."
} else {
    Write-Host "'$hcBranch' is up to date with origin/$hcBranch."
}
Get-HcSourceState -Branch $hcBranch
