#requires -Version 7.0
<#
.SYNOPSIS
Receives or sends committed HappyCheats source through the shared GitHub repository.
.DESCRIPTION
Pull (default) fast-forwards the current clean main/codex branch. -Publish sends
reviewed commits on that branch. -Status fetches and reports without changing files.
The equivalent -Mode Pull/Push/Status forms are also supported.
Never stages, commits, stashes, resets, rebases, switches branches or force-pushes.
#>
[CmdletBinding(DefaultParameterSetName = 'Mode')]
param(
    [Parameter(ParameterSetName = 'Mode')][ValidateSet('Pull', 'Push', 'Status')][string]$Mode = 'Pull',
    [Parameter(ParameterSetName = 'Publish')][switch]$Publish,
    [Parameter(ParameterSetName = 'Status')][switch]$Status,
    [string]$RepositoryPath = (Join-Path $PSScriptRoot '..')
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($Publish) { $Mode = 'Push' }
if ($Status) { $Mode = 'Status' }

function Invoke-HcSourceGit([string[]]$GitArguments, [switch]$AllowFailure) {
    $hcOutput = @(& git -C $RepositoryPath @GitArguments 2>&1 | ForEach-Object { "$_" })
    $hcExit = $LASTEXITCODE
    if ($hcExit -ne 0 -and !$AllowFailure) {
        throw "Git $($GitArguments[0]) failed (exit $hcExit). $($hcOutput -join [Environment]::NewLine)"
    }
    [pscustomobject]@{ Code = $hcExit; Lines = $hcOutput; Text = ($hcOutput -join "`n").Trim() }
}

function Get-HcSourceHead {
    (Invoke-HcSourceGit @('rev-parse', '--verify', 'HEAD')).Text
}

function Get-HcSourceOperations {
    foreach ($hcMarker in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-merge', 'rebase-apply', 'sequencer', 'BISECT_START', 'BISECT_LOG', 'index.lock')) {
        $hcMarkerPath = (Invoke-HcSourceGit @('rev-parse', '--path-format=absolute', '--git-path', $hcMarker)).Text
        if (Test-Path -LiteralPath $hcMarkerPath) { $hcMarker }
    }
}

function Get-HcSourceState {
    $hcLocalCommit = Get-HcSourceHead
    $hcCurrentBranch = Invoke-HcSourceGit @('symbolic-ref', '--quiet', '--short', 'HEAD') -AllowFailure
    $hcBranchName = if ($hcCurrentBranch.Code -eq 0) { $hcCurrentBranch.Text } else { '' }
    $hcRemoteCommit = ''
    $hcAheadCount = 0
    $hcBehindCount = 0
    if ($hcBranchName) {
        $hcResolved = Invoke-HcSourceGit @('rev-parse', '--verify', '--quiet', "refs/remotes/origin/${hcBranchName}^{commit}") -AllowFailure
        if ($hcResolved.Code -eq 0) {
            $hcRemoteCommit = $hcResolved.Text
            $hcCounts = (Invoke-HcSourceGit @('rev-list', '--left-right', '--count', "$hcLocalCommit...$hcRemoteCommit")).Text -split '\s+'
            $hcAheadCount = [int]$hcCounts[0]
            $hcBehindCount = [int]$hcCounts[1]
        } elseif ($hcResolved.Code -ne 1) { throw 'Could not inspect the remote-tracking source branch.' }
    }
    $hcRelation = if (!$hcBranchName) { 'Detached' }
        elseif (!$hcRemoteCommit) { 'Unpublished' }
        elseif ($hcAheadCount -gt 0 -and $hcBehindCount -gt 0) { 'Diverged' }
        elseif ($hcAheadCount -gt 0) { 'LocalAhead' }
        elseif ($hcBehindCount -gt 0) { 'RemoteAhead' }
        else { 'Current' }
    [pscustomobject]@{
        Computer = [Environment]::MachineName; Repository = $RepositoryPath; Branch = $hcBranchName
        State = $hcRelation; LocalCommit = $hcLocalCommit; RemoteCommit = $hcRemoteCommit
        Ahead = $hcAheadCount; Behind = $hcBehindCount
        ChangedFiles = (Invoke-HcSourceGit @('status', '--porcelain=v1', '--untracked-files=all', '--ignore-submodules=none')).Lines.Count
        Operations = @(Get-HcSourceOperations)
    }
}

function Assert-HcSourceClean([string]$ExpectedHead, [string]$ExpectedBranch) {
    $hcCurrentBranch = Invoke-HcSourceGit @('symbolic-ref', '--quiet', '--short', 'HEAD') -AllowFailure
    if ($hcCurrentBranch.Code -ne 0 -or $hcCurrentBranch.Text -cne $ExpectedBranch -or
        (Get-HcSourceHead) -cne $ExpectedHead) {
        throw 'The checked-out branch or commit changed during synchronization. Retry after the other work finishes.'
    }
    $hcOperations = @(Get-HcSourceOperations)
    if ($hcOperations.Count -gt 0) {
        throw "An unfinished Git operation ($($hcOperations -join ', ')) exists. Finish or abort it deliberately before syncing; it was preserved."
    }
    if ((Invoke-HcSourceGit @('status', '--porcelain=v1', '--untracked-files=all', '--ignore-submodules=none')).Text) {
        throw 'The checkout has unfinished changes or untracked files. Review and commit or save them deliberately before syncing; nothing was staged or discarded.'
    }
}

$hcRepositoryItem = Get-Item -LiteralPath $RepositoryPath -Force
if (!$hcRepositoryItem.PSIsContainer -or $hcRepositoryItem.PSProvider.Name -ne 'FileSystem') {
    throw 'RepositoryPath must name an existing filesystem directory.'
}
$RepositoryPath = $hcRepositoryItem.FullName
if ((Invoke-HcSourceGit @('rev-parse', '--is-inside-work-tree')).Text -cne 'true') {
    throw 'Select an existing HappyCheats working checkout, not a bare repository.'
}
$RepositoryPath = (Invoke-HcSourceGit @('rev-parse', '--show-toplevel')).Text
# Validate the saved identities, including explicit push destinations. Never print credentials.
$hcOfficialOrigin = '^(https://github\.com/|git@github\.com:|ssh://git@github\.com/)flazedude/happycheats(\.git)?/?$'
$hcFetchUrls = Invoke-HcSourceGit @('config', '--get-all', 'remote.origin.url') -AllowFailure
$hcPushUrls = Invoke-HcSourceGit @('config', '--get-all', 'remote.origin.pushurl') -AllowFailure
if ($hcFetchUrls.Code -ne 0 -or $hcFetchUrls.Lines.Count -ne 1 -or $hcFetchUrls.Text -notmatch $hcOfficialOrigin -or
    ($hcPushUrls.Code -eq 0 -and ($hcPushUrls.Lines.Count -ne 1 -or $hcPushUrls.Text -notmatch $hcOfficialOrigin))) {
    throw 'origin must fetch and push only flazedude/happycheats on GitHub. Inspect the repository configuration locally; no source was sent.'
}
$hcBranchResult = Invoke-HcSourceGit @('symbolic-ref', '--quiet', '--short', 'HEAD') -AllowFailure
$hcBranch = if ($hcBranchResult.Code -eq 0) { $hcBranchResult.Text } else { '' }
if ($Mode -ne 'Status' -and !$hcBranch) { throw 'Detached HEAD: select the intended development branch before syncing.' }
if ($Mode -ne 'Status' -and $hcBranch -cne 'main' -and !$hcBranch.StartsWith('codex/', [StringComparison]::Ordinal)) {
    throw 'Source sync supports main and codex/ task branches. The current branch was preserved.'
}
$hcHead = Get-HcSourceHead
$hcUpstreamRemote = Invoke-HcSourceGit @('config', '--get', "branch.$hcBranch.remote") -AllowFailure
$hcUpstreamBranch = Invoke-HcSourceGit @('config', '--get-all', "branch.$hcBranch.merge") -AllowFailure
if ($Mode -ne 'Status' -and ($hcUpstreamRemote.Code -eq 0 -or $hcUpstreamBranch.Code -eq 0) -and
    ($hcUpstreamRemote.Text -cne 'origin' -or $hcUpstreamBranch.Text -cne "refs/heads/$hcBranch")) {
    throw "Branch '$hcBranch' tracks a different branch or remote. Choose its intended upstream explicitly before syncing."
}

$hcLockPath = (Invoke-HcSourceGit @('rev-parse', '--path-format=absolute', '--git-path', 'happycheats-source-sync.lock')).Text
try { $hcLock = [IO.File]::Open($hcLockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
catch { throw 'Another source sync is using this checkout, or its Git directory is not writable. Retry after it finishes.' }
try {
    if ($Mode -ne 'Status') { Assert-HcSourceClean $hcHead $hcBranch }
    Write-Host "Fetching shared source for $hcBranch..."
    $null = Invoke-HcSourceGit @('-c', 'fetch.pruneTags=false', '-c', 'remote.origin.pruneTags=false',
        'fetch', '--prune', '--no-tags', 'origin', '+refs/heads/*:refs/remotes/origin/*')
    $hcState = Get-HcSourceState
    $hcRemoteExists = !!$hcState.RemoteCommit
    $hcAhead = $hcState.Ahead
    $hcBehind = $hcState.Behind
    Write-Host "Checkout: $RepositoryPath"
    Write-Host "Branch: $hcBranch | local: $hcHead | remote present: $hcRemoteExists | ahead: $hcAhead | behind: $hcBehind"
    if ($Mode -eq 'Status') {
        return $hcState
    }
    Assert-HcSourceClean $hcHead $hcBranch
    if ($hcAhead -gt 0 -and $hcBehind -gt 0) {
        throw 'Local and remote commits have diverged. Review and merge the histories deliberately, run affected checks, then retry. Neither history was overwritten.'
    }
    if ($Mode -eq 'Pull') {
        if (!$hcRemoteExists) {
            Write-Host "No origin/$hcBranch exists yet. Use -Publish after reviewing and committing this branch."
            return $hcState
        }
        if ($hcBehind -gt 0) {
            # Use the inspected commit, not a tracking ref another worktree could change.
            $null = Invoke-HcSourceGit @('-c', 'merge.autostash=false', 'merge', '--ff-only', '--no-autostash', '--no-overwrite-ignore', $hcState.RemoteCommit)
            if ((Get-HcSourceHead) -cne $hcState.RemoteCommit) { throw 'Source changed during the fast-forward. Inspect git status before continuing.' }
            Write-Host 'Received committed source. Re-read AGENTS.md before starting work.'
        } elseif ($hcAhead -gt 0) {
            Write-Host "No incoming changes. $hcAhead local commit(s) remain to send after review with -Mode Push."
        } else {
            Write-Host 'Source is already up to date.'
        }
        return Get-HcSourceState
    }
    if ($hcBehind -gt 0) { throw 'The remote branch is ahead. Run the default Pull first, then review before pushing.' }
    if (!$hcRemoteExists -and $hcBranch -notlike 'codex/*') {
        throw 'New shared task branches must use the codex/ prefix. An existing remote branch can be synchronized normally.'
    }
    # Pin the inspected commit so concurrent local commits cannot be published by this run.
    # Exact refspec avoids push.default and configured push refspec surprises; no tags or force.
    $hcPush = Invoke-HcSourceGit @('-c', 'push.followTags=false', '-c', 'remote.origin.mirror=false',
        'push', '--no-force', '--no-follow-tags', '--recurse-submodules=no',
        'origin', "${hcHead}:refs/heads/$hcBranch") -AllowFailure
    if ($hcPush.Code -ne 0) {
        throw "Source push failed. Fetch and inspect incoming changes or authentication before retrying; never force-push. $($hcPush.Text)"
    }
    # Refresh the exact tracking ref even when saved fetch mappings exclude it.
    $null = Invoke-HcSourceGit @('-c', 'fetch.pruneTags=false', '-c', 'remote.origin.pruneTags=false',
        'fetch', '--no-tags', 'origin', "+refs/heads/${hcBranch}:refs/remotes/origin/$hcBranch")
    $hcPublished = (Invoke-HcSourceGit @('ls-remote', '--exit-code', '--heads', 'origin', "refs/heads/$hcBranch")).Text
    if (($hcPublished -split '\s+')[0] -cne $hcHead) {
        throw 'The remote branch changed during push verification. Fetch and inspect its history before reporting synchronization complete.'
    }
    Assert-HcSourceClean $hcHead $hcBranch
    # Save a same-name upstream only after verifying publication and the checkout.
    # Set these explicitly because a pinned SHA is not a branch for push --set-upstream.
    $null = Invoke-HcSourceGit @('config', "branch.$hcBranch.remote", 'origin')
    $null = Invoke-HcSourceGit @('config', "branch.$hcBranch.merge", "refs/heads/$hcBranch")
    Write-Host "Verified origin/$hcBranch at $hcHead. The other PC can receive it with a source sync."
    Get-HcSourceState
} finally {
    $hcLock.Dispose()
}
