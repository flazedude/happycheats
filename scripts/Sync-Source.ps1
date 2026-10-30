#requires -Version 7.0
<#
.SYNOPSIS
Receives or sends committed HappyCheats source through the shared GitHub repository.
.DESCRIPTION
Pull (default) fast-forwards the current clean branch. Push sends reviewed commits
on that branch. Status fetches and reports without changing the working tree.
Never stages, commits, stashes, resets, rebases, switches branches or force-pushes.
#>
[CmdletBinding()]
param(
    [ValidateSet('Pull', 'Push', 'Status')][string]$Mode = 'Pull',
    [string]$RepositoryPath = (Join-Path $PSScriptRoot '..')
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

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

function Assert-HcSourceClean([string]$ExpectedHead, [string]$ExpectedBranch) {
    $hcCurrentBranch = Invoke-HcSourceGit @('symbolic-ref', '--quiet', '--short', 'HEAD') -AllowFailure
    if ($hcCurrentBranch.Code -ne 0 -or $hcCurrentBranch.Text -cne $ExpectedBranch -or
        (Get-HcSourceHead) -cne $ExpectedHead) {
        throw 'The checked-out branch or commit changed during synchronization. Retry after the other work finishes.'
    }
    foreach ($hcMarker in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-merge', 'rebase-apply', 'sequencer', 'BISECT_START', 'index.lock')) {
        $hcMarkerPath = (Invoke-HcSourceGit @('rev-parse', '--path-format=absolute', '--git-path', $hcMarker)).Text
        if (Test-Path -LiteralPath $hcMarkerPath) {
            throw "An unfinished Git operation ($hcMarker) exists. Finish or abort it deliberately before syncing; it was preserved."
        }
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
if ((Invoke-HcSourceGit @('config', '--bool', '--get', 'remote.origin.mirror') -AllowFailure).Text -eq 'true') {
    throw 'A mirror remote is not supported for development source synchronization.'
}
$hcBranchResult = Invoke-HcSourceGit @('symbolic-ref', '--quiet', '--short', 'HEAD') -AllowFailure
if ($hcBranchResult.Code -ne 0) { throw 'Detached HEAD: select the intended development branch before syncing.' }
$hcBranch = $hcBranchResult.Text
$hcHead = Get-HcSourceHead
$hcUpstreamRemote = Invoke-HcSourceGit @('config', '--get', "branch.$hcBranch.remote") -AllowFailure
$hcUpstreamBranch = Invoke-HcSourceGit @('config', '--get-all', "branch.$hcBranch.merge") -AllowFailure
if (($hcUpstreamRemote.Code -eq 0 -or $hcUpstreamBranch.Code -eq 0) -and
    ($hcUpstreamRemote.Text -cne 'origin' -or $hcUpstreamBranch.Text -cne "refs/heads/$hcBranch")) {
    throw "Branch '$hcBranch' tracks a different branch or remote. Choose its intended upstream explicitly before syncing."
}

$hcLockPath = (Invoke-HcSourceGit @('rev-parse', '--path-format=absolute', '--git-path', 'happycheats-source-sync.lock')).Text
try { $hcLock = [IO.File]::Open($hcLockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
catch { throw 'Another source sync is using this checkout, or its Git directory is not writable. Retry after it finishes.' }
try {
    if ($Mode -ne 'Status') { Assert-HcSourceClean $hcHead $hcBranch }
    Write-Host "Fetching shared source for $hcBranch..."
    $null = Invoke-HcSourceGit @('fetch', '--prune', '--no-tags', 'origin', '+refs/heads/*:refs/remotes/origin/*')
    $hcRemoteRef = "refs/remotes/origin/$hcBranch"
    $hcRemoteResult = Invoke-HcSourceGit @('rev-parse', '--verify', $hcRemoteRef) -AllowFailure
    $hcRemoteExists = $hcRemoteResult.Code -eq 0
    $hcAhead = 0
    $hcBehind = 0
    if ($hcRemoteExists) {
        $hcCounts = (Invoke-HcSourceGit @('rev-list', '--left-right', '--count', "$hcHead...$($hcRemoteResult.Text)")).Text -split '\s+'
        $hcAhead = [int]$hcCounts[0]
        $hcBehind = [int]$hcCounts[1]
    }
    Write-Host "Checkout: $RepositoryPath"
    Write-Host "Branch: $hcBranch | local: $hcHead | remote present: $hcRemoteExists | ahead: $hcAhead | behind: $hcBehind"
    if ($Mode -eq 'Status') {
        $hcStatus = (Invoke-HcSourceGit @('status', '--short', '--branch', '--untracked-files=all')).Text
        Write-Host $hcStatus
        return
    }
    Assert-HcSourceClean $hcHead $hcBranch
    if ($hcAhead -gt 0 -and $hcBehind -gt 0) {
        throw 'Local and remote commits have diverged. Review and merge the histories deliberately, run affected checks, then retry. Neither history was overwritten.'
    }
    if ($Mode -eq 'Pull') {
        if (!$hcRemoteExists) { throw "origin/$hcBranch does not exist. To send this reviewed task branch, use -Mode Push." }
        if ($hcBehind -gt 0) {
            # Use the inspected commit, not a tracking ref another worktree could change.
            $null = Invoke-HcSourceGit @('-c', 'merge.autostash=false', 'merge', '--ff-only', '--no-autostash', '--no-overwrite-ignore', $hcRemoteResult.Text)
            if ((Get-HcSourceHead) -cne $hcRemoteResult.Text) { throw 'Source changed during the fast-forward. Inspect git status before continuing.' }
            Write-Host 'Received committed source. Re-read AGENTS.md before starting work.'
        } elseif ($hcAhead -gt 0) {
            Write-Host "No incoming changes. $hcAhead local commit(s) remain to send after review with -Mode Push."
        } else {
            Write-Host 'Source is already up to date.'
        }
        return
    }
    if ($hcBehind -gt 0) { throw 'The remote branch is ahead. Run the default Pull first, then review before pushing.' }
    if (!$hcRemoteExists -and $hcBranch -notlike 'codex/*') {
        throw 'New shared task branches must use the codex/ prefix. An existing remote branch can be synchronized normally.'
    }
    # Pin the inspected commit so concurrent local commits cannot be published by this run.
    # Exact refspec avoids push.default and configured push refspec surprises; no tags or force.
    $hcPush = Invoke-HcSourceGit @('-c', 'push.followTags=false', 'push', '--no-follow-tags', '--recurse-submodules=no',
        'origin', "${hcHead}:refs/heads/$hcBranch") -AllowFailure
    if ($hcPush.Code -ne 0) {
        throw "Source push failed. Fetch and inspect incoming changes or authentication before retrying; never force-push. $($hcPush.Text)"
    }
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
} finally {
    $hcLock.Dispose()
}
