#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$hcSyncScript = Join-Path $PSScriptRoot 'Sync-Source.ps1'
$hcTestRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('HappyCheats-SourceSync-' + [Guid]::NewGuid().ToString('N'))))
$hcOrigin = Join-Path $hcTestRoot 'origin.git'
$hcJack = Join-Path $hcTestRoot 'JACK-PC'
$hcGpd = Join-Path $hcTestRoot 'GPD-PC'
$hcOfficialUrl = 'https://github.com/flazedude/happycheats.git'
$hcSavedEnvironment = @{}
$hcCount = 0

function Invoke-HcTestGit {
    param([string]$Directory, [string[]]$Arguments, [int]$ExpectedExit = 0)
    $hcOutput = @(& git -C $Directory -c commit.gpgsign=false -c tag.gpgsign=false -c core.hooksPath="$hcTestRoot/empty-hooks" @Arguments 2>&1)
    if ($LASTEXITCODE -ne $ExpectedExit) {
        throw "Fixture git $($Arguments -join ' ') exited $LASTEXITCODE (expected $ExpectedExit): $($hcOutput -join [Environment]::NewLine)"
    }
    return ($hcOutput -join [Environment]::NewLine)
}

function Assert-HcSourceSync {
    param([bool]$Condition, [string]$Message)
    if (!$Condition) { throw "Source synchronization regression failed: $Message" }
    $script:hcCount++
}

function Assert-HcSourceSyncRejected {
    param([scriptblock]$Action, [string]$Message)
    $hcRejected = $false
    try { & $Action | Out-Null } catch { $hcRejected = $true }
    Assert-HcSourceSync $hcRejected $Message
}

function Get-HcTestHead([string]$Directory) {
    return (Invoke-HcTestGit $Directory @('rev-parse', 'HEAD'))
}

function Add-HcTestCommit {
    param([string]$Directory, [string]$Name, [string]$Content)
    [IO.File]::WriteAllText((Join-Path $Directory $Name), $Content)
    $null = Invoke-HcTestGit $Directory @('add', '--', $Name)
    $null = Invoke-HcTestGit $Directory @('commit', '-m', "Fixture: $Name")
    return (Get-HcTestHead $Directory)
}

try {
    $null = New-Item -ItemType Directory -Path $hcTestRoot
    $hcEmptyConfig = Join-Path $hcTestRoot 'empty-git-config'
    Set-Content -LiteralPath $hcEmptyConfig -Value ''
    foreach ($hcName in @('GIT_CONFIG_GLOBAL', 'GIT_CONFIG_NOSYSTEM', 'GIT_TERMINAL_PROMPT', 'GIT_CONFIG_COUNT',
        'GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_OBJECT_DIRECTORY', 'GIT_ALTERNATE_OBJECT_DIRECTORIES', 'GIT_COMMON_DIR')) {
        $hcSavedEnvironment[$hcName] = [Environment]::GetEnvironmentVariable($hcName, 'Process')
    }
    $env:GIT_CONFIG_GLOBAL = $hcEmptyConfig
    $env:GIT_CONFIG_NOSYSTEM = '1'
    $env:GIT_TERMINAL_PROMPT = '0'
    $env:GIT_CONFIG_COUNT = '0'
    foreach ($hcName in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_OBJECT_DIRECTORY', 'GIT_ALTERNATE_OBJECT_DIRECTORIES', 'GIT_COMMON_DIR')) {
        Remove-Item -LiteralPath "Env:$hcName" -ErrorAction SilentlyContinue
    }
    $null = New-Item -ItemType Directory -Path (Join-Path $hcTestRoot 'empty-hooks')
    $null = Invoke-HcTestGit $hcTestRoot @('init', '--bare', '--initial-branch=main', $hcOrigin)
    $null = Invoke-HcTestGit $hcTestRoot @('clone', $hcOrigin, $hcJack)
    foreach ($hcEntry in @(@('user.name', 'Source Sync Fixture'), @('user.email', 'source-sync@example.invalid'))) {
        $null = Invoke-HcTestGit $hcJack @('config', '--local', $hcEntry[0], $hcEntry[1])
    }
    $null = Add-HcTestCommit $hcJack '.gitignore' "artifacts/`ndist/`n"
    $hcSeed = Add-HcTestCommit $hcJack 'shared.txt' 'Initial source'
    $null = Invoke-HcTestGit $hcJack @('push', '-u', 'origin', 'main')
    $null = Invoke-HcTestGit $hcTestRoot @('clone', $hcOrigin, $hcGpd)
    foreach ($hcEntry in @(@('user.name', 'Source Sync Fixture'), @('user.email', 'source-sync@example.invalid'))) {
        $null = Invoke-HcTestGit $hcGpd @('config', '--local', $hcEntry[0], $hcEntry[1])
    }
    foreach ($hcCheckout in @($hcJack, $hcGpd)) {
        # Keep the production origin identity while redirecting only these local
        # fixtures; a missing rewrite cannot accidentally contact the real repo.
        $hcFileUrl = ([Uri]([IO.Path]::GetFullPath($hcOrigin))).AbsoluteUri
        $null = Invoke-HcTestGit $hcCheckout @('config', '--local', "url.$hcFileUrl.insteadOf", $hcOfficialUrl)
        $null = Invoke-HcTestGit $hcCheckout @('config', '--local', 'protocol.allow', 'never')
        $null = Invoke-HcTestGit $hcCheckout @('config', '--local', 'protocol.file.allow', 'always')
        $null = Invoke-HcTestGit $hcCheckout @('remote', 'set-url', 'origin', $hcOfficialUrl)
        $null = Invoke-HcTestGit $hcCheckout @('config', '--local', 'core.hooksPath', (Join-Path $hcTestRoot 'empty-hooks'))
        $null = New-Item -ItemType Directory -Path (Join-Path $hcCheckout 'artifacts/lan-updates'), (Join-Path $hcCheckout 'dist')
        [IO.File]::WriteAllText((Join-Path $hcCheckout 'artifacts/lan-updates/server.json'), "Local host fixture: $hcCheckout")
        [IO.File]::WriteAllText((Join-Path $hcCheckout 'dist/HappyCheats.exe'), "Local package fixture: $hcCheckout")
    }
    $null = Invoke-HcTestGit $hcJack @('tag', '-a', 'local-recovery-fixture', '-m', 'Preserve local recovery tag')
    $hcRecoveryTag = Invoke-HcTestGit $hcJack @('rev-parse', 'refs/tags/local-recovery-fixture')
    $null = Invoke-HcTestGit $hcJack @('branch', 'codex/unrelated-fixture')
    foreach ($hcSetting in @('fetch.pruneTags', 'remote.origin.pruneTags', 'push.followTags', 'remote.origin.mirror')) {
        $null = Invoke-HcTestGit $hcJack @('config', '--local', $hcSetting, 'true')
    }

    # A completed change can travel in either direction through the shared main.
    $hcFromJack = Add-HcTestCommit $hcJack 'jack.txt' 'Completed on JACK-PC'
    & $hcSyncScript -RepositoryPath $hcJack -Publish | Out-Null
    Assert-HcSourceSync ((Invoke-HcTestGit $hcOrigin @('rev-parse', 'refs/heads/main')) -ceq $hcFromJack) 'publish uploads JACK-PC committed main'
    Assert-HcSourceSync ((Invoke-HcTestGit $hcJack @('rev-parse', 'refs/tags/local-recovery-fixture')) -ceq $hcRecoveryTag) 'source fetch preserves local recovery tags despite configured tag pruning'
    Assert-HcSourceSync ([string]::IsNullOrWhiteSpace((Invoke-HcTestGit $hcOrigin @('for-each-ref', '--format=%(refname)', 'refs/tags')))) 'source publish does not follow annotated tags despite push.followTags'
    Assert-HcSourceSync ([string]::IsNullOrWhiteSpace((Invoke-HcTestGit $hcOrigin @('for-each-ref', '--format=%(refname)', 'refs/heads/codex/unrelated-fixture')))) 'source publish does not mirror unrelated branches despite remote mirror configuration'
    & $hcSyncScript -RepositoryPath $hcGpd | Out-Null
    Assert-HcSourceSync ((Get-HcTestHead $hcGpd) -ceq $hcFromJack) 'GPD-PC receives JACK-PC source by fast-forward'
    $hcFromGpd = Add-HcTestCommit $hcGpd 'gpd.txt' 'Completed on GPD-PC'
    & $hcSyncScript -RepositoryPath $hcGpd -Publish | Out-Null
    & $hcSyncScript -RepositoryPath $hcJack | Out-Null
    Assert-HcSourceSync ((Get-HcTestHead $hcJack) -ceq $hcFromGpd) 'JACK-PC receives GPD-PC source by fast-forward'

    # Merely receiving must never publish local commits.
    $hcLocalOnly = Add-HcTestCommit $hcJack 'local-only.txt' 'Awaiting explicit publication'
    & $hcSyncScript -RepositoryPath $hcJack | Out-Null
    Assert-HcSourceSync ((Invoke-HcTestGit $hcOrigin @('rev-parse', 'refs/heads/main')) -ceq $hcFromGpd) 'default receive leaves unpublished commits local'
    Assert-HcSourceSync ((Get-HcTestHead $hcJack) -ceq $hcLocalOnly) 'default receive preserves local ahead commit'
    $null = Invoke-HcTestGit $hcJack @('remote', 'remove', 'origin')
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcJack } 'missing origin is reported as a failure'
    Assert-HcSourceSync ((Get-HcTestHead $hcJack) -ceq $hcLocalOnly) 'missing origin preserves committed source'
    $null = Invoke-HcTestGit $hcJack @('remote', 'add', 'origin', $hcOfficialUrl)
    $hcUnavailableUrl = ([Uri](Join-Path $hcTestRoot 'unavailable-origin.git')).AbsoluteUri
    $hcPushIdentity = 'https://github.com/flazedude/happycheats'
    $null = Invoke-HcTestGit $hcJack @('config', '--local', "url.$hcUnavailableUrl.insteadOf", $hcPushIdentity)
    $null = Invoke-HcTestGit $hcJack @('remote', 'set-url', '--push', 'origin', $hcPushIdentity)
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcJack -Publish } 'push transport failure is reported instead of claiming publication'
    Assert-HcSourceSync ((Get-HcTestHead $hcJack) -ceq $hcLocalOnly) 'failed push preserves local commit'
    Assert-HcSourceSync ((Invoke-HcTestGit $hcOrigin @('rev-parse', 'refs/heads/main')) -ceq $hcFromGpd) 'failed push leaves peer source unchanged'
    $null = Invoke-HcTestGit $hcJack @('config', '--local', '--unset-all', 'remote.origin.pushurl')
    $null = Invoke-HcTestGit $hcJack @('config', '--local', '--unset-all', "url.$hcUnavailableUrl.insteadOf")
    & $hcSyncScript -RepositoryPath $hcJack -Publish | Out-Null

    # Status may fetch while dirty, but must preserve the current files and HEAD.
    [IO.File]::WriteAllText((Join-Path $hcGpd 'shared.txt'), 'Unfinished peer edit')
    $hcBeforeStatus = Get-HcTestHead $hcGpd
    & $hcSyncScript -RepositoryPath $hcGpd -Status | Out-Null
    Assert-HcSourceSync ((Get-HcTestHead $hcGpd) -ceq $hcBeforeStatus) 'status does not fast-forward a checkout'
    Assert-HcSourceSync (([IO.File]::ReadAllText((Join-Path $hcGpd 'shared.txt'))) -ceq 'Unfinished peer edit') 'status preserves unfinished source'
    $hcDirtyStatus = Invoke-HcTestGit $hcGpd @('status', '--porcelain=v1', '--untracked-files=all')
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcGpd } 'receive rejects unstaged changes'
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcGpd -Publish } 'publish rejects unstaged changes'
    Assert-HcSourceSync ((Invoke-HcTestGit $hcGpd @('status', '--porcelain=v1', '--untracked-files=all')) -ceq $hcDirtyStatus) 'rejected synchronization preserves dirty index and worktree'
    $null = Invoke-HcTestGit $hcGpd @('restore', '--', 'shared.txt')

    [IO.File]::WriteAllText((Join-Path $hcGpd 'untracked.txt'), 'Unfinished untracked source')
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcGpd } 'receive rejects untracked source'
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcGpd -Publish } 'publish rejects untracked source'
    Assert-HcSourceSync (([IO.File]::ReadAllText((Join-Path $hcGpd 'untracked.txt'))) -ceq 'Unfinished untracked source') 'untracked source remains intact'
    Remove-Item -LiteralPath (Join-Path $hcGpd 'untracked.txt')

    [IO.File]::WriteAllText((Join-Path $hcGpd 'shared.txt'), 'Staged unfinished source')
    $null = Invoke-HcTestGit $hcGpd @('add', '--', 'shared.txt')
    $hcIndexBefore = Invoke-HcTestGit $hcGpd @('write-tree')
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcGpd -Publish } 'publish rejects staged changes'
    Assert-HcSourceSync ((Invoke-HcTestGit $hcGpd @('write-tree')) -ceq $hcIndexBefore) 'rejection preserves staged content'
    Assert-HcSourceSync ((Get-HcTestHead $hcGpd) -ceq $hcBeforeStatus) 'dirty rejections never create automatic commits'
    $null = Invoke-HcTestGit $hcGpd @('restore', '--staged', '--worktree', '--', 'shared.txt')
    & $hcSyncScript -RepositoryPath $hcGpd | Out-Null

    # A new task branch is published under its own name and tracked on the peer.
    $hcTaskBranch = 'codex/source-sync-fixture-jack'
    $null = Invoke-HcTestGit $hcJack @('switch', '-c', $hcTaskBranch)
    $hcTaskCommit = Add-HcTestCommit $hcJack 'task.txt' 'Task branch change'
    try { & $hcSyncScript -RepositoryPath $hcJack | Out-Null } catch { }
    Assert-HcSourceSync ((Get-HcTestHead $hcJack) -ceq $hcTaskCommit) 'new unpublished branch receive never substitutes main'
    & $hcSyncScript -RepositoryPath $hcJack -Publish | Out-Null
    Assert-HcSourceSync ((Invoke-HcTestGit $hcOrigin @('rev-parse', "refs/heads/$hcTaskBranch")) -ceq $hcTaskCommit) 'first publish creates the same-named remote task branch'
    Assert-HcSourceSync ((Invoke-HcTestGit $hcJack @('rev-parse', '--abbrev-ref', '@{upstream}')) -ceq "origin/$hcTaskBranch") 'first publish establishes the correct task upstream'
    Assert-HcSourceSync ((Invoke-HcTestGit $hcOrigin @('rev-parse', 'refs/heads/main')) -ceq $hcLocalOnly) 'task publication leaves shared main unchanged'
    $null = Invoke-HcTestGit $hcGpd @('fetch', 'origin')
    $null = Invoke-HcTestGit $hcGpd @('switch', '--track', "origin/$hcTaskBranch")
    $hcTaskReturn = Add-HcTestCommit $hcGpd 'task-return.txt' 'Peer continues task'
    & $hcSyncScript -RepositoryPath $hcGpd -Publish | Out-Null
    & $hcSyncScript -RepositoryPath $hcJack | Out-Null
    Assert-HcSourceSync ((Get-HcTestHead $hcJack) -ceq $hcTaskReturn) 'shared task branch accepts peer continuation without rewriting history'

    $null = Invoke-HcTestGit $hcJack @('branch', '--set-upstream-to=origin/main', $hcTaskBranch)
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcJack } 'mismatched upstream cannot receive main into a task branch'
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcJack -Publish } 'mismatched upstream cannot publish a task branch to main'
    Assert-HcSourceSync ((Invoke-HcTestGit $hcOrigin @('rev-parse', 'refs/heads/main')) -ceq $hcLocalOnly) 'mismatched upstream rejection preserves remote main'
    $null = Invoke-HcTestGit $hcJack @('branch', "--set-upstream-to=origin/$hcTaskBranch", $hcTaskBranch)

    $null = Invoke-HcTestGit $hcJack @('switch', '--detach')
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcJack -Publish } 'detached HEAD cannot be published'
    Assert-HcSourceSync ((Get-HcTestHead $hcJack) -ceq $hcTaskReturn) 'detached HEAD is preserved'
    $null = Invoke-HcTestGit $hcJack @('switch', 'main')
    $null = Invoke-HcTestGit $hcGpd @('switch', 'main')

    # Simultaneous commits require deliberate integration, not a reset or force push.
    $hcDivergedJack = Add-HcTestCommit $hcJack 'diverged-jack.txt' 'JACK-PC concurrent commit'
    $hcDivergedGpd = Add-HcTestCommit $hcGpd 'diverged-gpd.txt' 'GPD-PC concurrent commit'
    & $hcSyncScript -RepositoryPath $hcGpd -Publish | Out-Null
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcJack } 'divergent source cannot be received automatically'
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcJack -Publish } 'divergent source cannot be force-published'
    Assert-HcSourceSync ((Get-HcTestHead $hcJack) -ceq $hcDivergedJack) 'divergence preserves local commits'
    Assert-HcSourceSync ((Invoke-HcTestGit $hcOrigin @('rev-parse', 'refs/heads/main')) -ceq $hcDivergedGpd) 'divergence preserves peer commits'
    Assert-HcSourceSync (!(Test-Path -LiteralPath (Join-Path $hcJack '.git/MERGE_HEAD'))) 'rejected divergence does not leave an implicit merge'
    $null = Invoke-HcTestGit $hcJack @('merge', '--no-edit', 'origin/main')
    $hcIntegrated = Get-HcTestHead $hcJack
    & $hcSyncScript -RepositoryPath $hcJack -Publish | Out-Null
    & $hcSyncScript -RepositoryPath $hcGpd | Out-Null
    Assert-HcSourceSync ((Get-HcTestHead $hcGpd) -ceq $hcIntegrated) 'manually integrated history synchronizes normally'
    $null = Invoke-HcTestGit $hcJack @('merge-base', '--is-ancestor', $hcDivergedJack, $hcIntegrated)
    $null = Invoke-HcTestGit $hcJack @('merge-base', '--is-ancestor', $hcDivergedGpd, $hcIntegrated)
    Assert-HcSourceSync $true 'deliberate integration retains both concurrent commits'

    # Preserve real unresolved merge state rather than continuing an operation.
    $null = Invoke-HcTestGit $hcJack @('switch', '-c', 'codex/conflict-fixture')
    $null = Add-HcTestCommit $hcJack 'shared.txt' 'Task conflict'
    $null = Invoke-HcTestGit $hcJack @('switch', 'main')
    $null = Add-HcTestCommit $hcJack 'shared.txt' 'Main conflict'
    $null = Invoke-HcTestGit $hcJack @('merge', 'codex/conflict-fixture') -ExpectedExit 1
    $hcConflictStatus = Invoke-HcTestGit $hcJack @('status', '--porcelain=v1')
    $hcConflictBytes = [IO.File]::ReadAllText((Join-Path $hcJack 'shared.txt'))
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcJack } 'active conflicted merge is rejected'
    Assert-HcSourceSyncRejected { & $hcSyncScript -RepositoryPath $hcJack -Publish } 'active merge cannot be published'
    Assert-HcSourceSync ((Invoke-HcTestGit $hcJack @('status', '--porcelain=v1')) -ceq $hcConflictStatus) 'active merge index state is preserved'
    Assert-HcSourceSync (([IO.File]::ReadAllText((Join-Path $hcJack 'shared.txt'))) -ceq $hcConflictBytes) 'conflict content is preserved'
    Assert-HcSourceSync (Test-Path -LiteralPath (Join-Path $hcJack '.git/MERGE_HEAD')) 'active merge remains available for deliberate resolution'

    foreach ($hcCheckout in @($hcJack, $hcGpd)) {
        Assert-HcSourceSync (([IO.File]::ReadAllText((Join-Path $hcCheckout 'artifacts/lan-updates/server.json'))) -ceq "Local host fixture: $hcCheckout") 'machine-local LAN settings remain local and unchanged'
        Assert-HcSourceSync (([IO.File]::ReadAllText((Join-Path $hcCheckout 'dist/HappyCheats.exe'))) -ceq "Local package fixture: $hcCheckout") 'source synchronization does not install or replace client artifacts'
        Assert-HcSourceSync ([string]::IsNullOrWhiteSpace((Invoke-HcTestGit $hcCheckout @('stash', 'list')))) 'source synchronization never creates an automatic stash'
    }

    Write-Host "Source synchronization regression checks passed: $hcCount (two disposable clones and a local bare origin; no physical peer or GitHub network validation)."
} finally {
    foreach ($hcName in $hcSavedEnvironment.Keys) {
        if ($null -eq $hcSavedEnvironment[$hcName]) {
            Remove-Item -LiteralPath "Env:$hcName" -ErrorAction SilentlyContinue
        } else {
            [Environment]::SetEnvironmentVariable($hcName, $hcSavedEnvironment[$hcName], 'Process')
        }
    }
    # Delete only this test's resolved, uniquely named directory under TEMP.
    $hcTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $hcResolvedTest = [IO.Path]::GetFullPath($hcTestRoot)
    if ($hcResolvedTest.StartsWith($hcTempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($hcResolvedTest) -match '^HappyCheats-SourceSync-[0-9a-f]{32}$' -and
        (Test-Path -LiteralPath $hcResolvedTest)) {
        Remove-Item -LiteralPath $hcResolvedTest -Recurse -Force
    }
}
