#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$hcSyncScript = Join-Path $PSScriptRoot 'Sync-Source.ps1'
$hcPwsh = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
$hcTempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$hcTestRoot = Join-Path $hcTempParent ('HappyCheats-SourceSync-Tests-' + [Guid]::NewGuid().ToString('N'))
$hcOfficialUrl = 'https://github.com/flazedude/happycheats.git'
$hcCount = 0
$hcSavedEnvironment = @{}

function Invoke-HcFixtureGit([string]$Repository, [string[]]$GitArguments) {
    $hcOutput = @(& git -C $Repository @GitArguments 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { throw "Fixture Git failed: $($GitArguments -join ' ')`n$($hcOutput -join "`n")" }
    return ($hcOutput -join "`n").Trim()
}
function Assert-HcSourceTest([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "Source sync regression failed: $Message" }
    $script:hcCount++
}
function Invoke-HcFixtureSync([string]$Repository, [string]$Mode = 'Pull', [switch]$Reject) {
    $hcOutput = @(& $hcPwsh -NoProfile -File $hcSyncScript -RepositoryPath $Repository -Mode $Mode 2>&1 | ForEach-Object { "$_" })
    $hcExit = $LASTEXITCODE
    Assert-HcSourceTest $(if ($Reject) { $hcExit -ne 0 } else { $hcExit -eq 0 }) "$Mode expected rejection=$Reject, exit=$hcExit`n$($hcOutput -join "`n")"
    return ($hcOutput -join "`n")
}
function Get-HcFixtureHead([string]$Repository) { Invoke-HcFixtureGit $Repository @('rev-parse', 'HEAD') }
function Add-HcFixtureCommit([string]$Repository, [string]$Name) {
    Set-Content -LiteralPath (Join-Path $Repository "$Name.txt") -Value $Name -Encoding utf8NoBOM
    $null = Invoke-HcFixtureGit $Repository @('add', '--', "$Name.txt")
    $null = Invoke-HcFixtureGit $Repository @('commit', '-m', $Name)
    return Get-HcFixtureHead $Repository
}
function New-HcSourceFixture([string]$Name) {
    $hcRoot = Join-Path $hcTestRoot $Name
    $hcRemote = Join-Path $hcRoot 'shared.git'
    $hcA = Join-Path $hcRoot 'pc-a'
    $hcB = Join-Path $hcRoot 'pc-b'
    $null = New-Item -ItemType Directory -Path $hcRoot, $hcA
    $null = Invoke-HcFixtureGit $hcRoot @('init', '--bare', '--initial-branch=main', $hcRemote)
    $null = Invoke-HcFixtureGit $hcA @('init', '--initial-branch=main')
    $null = Invoke-HcFixtureGit $hcA @('config', 'user.name', 'Source sync fixture')
    $null = Invoke-HcFixtureGit $hcA @('config', 'user.email', 'fixture@example.invalid')
    $null = Add-HcFixtureCommit $hcA 'initial'
    $null = Invoke-HcFixtureGit $hcA @('remote', 'add', 'origin', $hcRemote)
    $null = Invoke-HcFixtureGit $hcA @('push', '-u', 'origin', 'main')
    $null = Invoke-HcFixtureGit $hcRoot @('clone', $hcRemote, $hcB)
    $hcFileUrl = ([Uri]([IO.Path]::GetFullPath($hcRemote))).AbsoluteUri
    foreach ($hcCheckout in @($hcA, $hcB)) {
        $null = Invoke-HcFixtureGit $hcCheckout @('config', 'user.name', 'Source sync fixture')
        $null = Invoke-HcFixtureGit $hcCheckout @('config', 'user.email', 'fixture@example.invalid')
        $null = Invoke-HcFixtureGit $hcCheckout @('config', 'commit.gpgSign', 'false')
        $null = Invoke-HcFixtureGit $hcCheckout @('config', 'protocol.allow', 'never')
        $null = Invoke-HcFixtureGit $hcCheckout @('config', 'protocol.file.allow', 'always')
        $null = Invoke-HcFixtureGit $hcCheckout @('config', "url.$hcFileUrl.insteadOf", $hcOfficialUrl)
        $null = Invoke-HcFixtureGit $hcCheckout @('remote', 'set-url', 'origin', $hcOfficialUrl)
    }
    return [pscustomobject]@{ Root=$hcRoot; Remote=$hcRemote; A=$hcA; B=$hcB; FileUrl=$hcFileUrl }
}

try {
    $null = New-Item -ItemType Directory -Path $hcTestRoot
    $hcEmptyConfig = Join-Path $hcTestRoot 'empty-git-config'
    Set-Content -LiteralPath $hcEmptyConfig -Value ''
    # The child helper inherits isolated Git configuration. Local allowlists also
    # prohibit network transports if a fixture URL rewrite is accidentally broken.
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

    $hcFixture = New-HcSourceFixture 'handoff'
    $null = Invoke-HcFixtureSync $hcFixture.A
    $hcRelativeRunner = Join-Path $hcTestRoot 'invoke-relative.ps1'
    @'
param([string]$SyncScript, [string]$Directory)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $Directory
& $SyncScript -RepositoryPath 'pc-a' -Mode Status
'@ | Set-Content -LiteralPath $hcRelativeRunner -Encoding utf8NoBOM
    $hcRelativeOutput = @(& $hcPwsh -NoProfile -File $hcRelativeRunner -SyncScript $hcSyncScript -Directory $hcFixture.Root 2>&1)
    Assert-HcSourceTest ($LASTEXITCODE -eq 0) "relative repository path honors PowerShell's current directory: $hcRelativeOutput"
    $hcInitial = Get-HcFixtureHead $hcFixture.B
    $hcSent = Add-HcFixtureCommit $hcFixture.A 'from-a'
    $null = Invoke-HcFixtureSync $hcFixture.A 'Push'
    $null = Invoke-HcFixtureSync $hcFixture.B 'Status'
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.B) -ceq $hcInitial) 'Status fetch does not update checked-out source'
    Assert-HcSourceTest ((Invoke-HcFixtureGit $hcFixture.B @('rev-parse', 'origin/main')) -ceq $hcSent) 'Status fetch sees incoming commits'
    $null = Invoke-HcFixtureSync $hcFixture.B 'Push' -Reject
    $null = Invoke-HcFixtureSync $hcFixture.B
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.B) -ceq $hcSent) 'PC B receives PC A source by fast-forward'
    $hcReturned = Add-HcFixtureCommit $hcFixture.B 'from-b'
    $null = Invoke-HcFixtureSync $hcFixture.B 'Push'
    $null = Invoke-HcFixtureSync $hcFixture.A
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.A) -ceq $hcReturned) 'PC A receives PC B source by fast-forward'

    $hcFixture = New-HcSourceFixture 'unfinished'
    $hcBefore = Get-HcFixtureHead $hcFixture.B
    $null = Add-HcFixtureCommit $hcFixture.A 'incoming'
    $null = Invoke-HcFixtureSync $hcFixture.A 'Push'
    $hcTracked = Join-Path $hcFixture.B 'initial.txt'
    Set-Content -LiteralPath $hcTracked -Value 'unfinished local edit'
    $hcDirtyHash = (Get-FileHash -LiteralPath $hcTracked).Hash
    $null = Invoke-HcFixtureSync $hcFixture.B -Reject
    $null = Invoke-HcFixtureSync $hcFixture.B 'Push' -Reject
    $null = Invoke-HcFixtureSync $hcFixture.B 'Status'
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.B) -ceq $hcBefore -and (Get-FileHash -LiteralPath $hcTracked).Hash -ceq $hcDirtyHash) 'dirty source and its branch survive all modes'
    $null = Invoke-HcFixtureGit $hcFixture.B @('add', 'initial.txt')
    $null = Invoke-HcFixtureSync $hcFixture.B -Reject
    Assert-HcSourceTest ((Invoke-HcFixtureGit $hcFixture.B @('diff', '--cached', '--name-only')) -ceq 'initial.txt') 'staged unfinished work remains staged'
    $null = Invoke-HcFixtureGit $hcFixture.B @('restore', '--source=HEAD', '--staged', '--worktree', 'initial.txt')
    $hcUntracked = Join-Path $hcFixture.B 'untracked.txt'
    Set-Content -LiteralPath $hcUntracked -Value 'not reviewed'
    $null = Invoke-HcFixtureSync $hcFixture.B -Reject
    Assert-HcSourceTest ((Get-Content -LiteralPath $hcUntracked -Raw).Trim() -ceq 'not reviewed') 'untracked file remains untouched'
    Remove-Item -LiteralPath $hcUntracked
    foreach ($hcMarker in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-merge', 'rebase-apply', 'sequencer', 'BISECT_START', 'index.lock')) {
        $hcMarkerPath = Join-Path $hcFixture.B ".git/$hcMarker"
        Set-Content -LiteralPath $hcMarkerPath -Value $hcBefore
        $null = Invoke-HcFixtureSync $hcFixture.B -Reject
        Assert-HcSourceTest (Test-Path -LiteralPath $hcMarkerPath) "$hcMarker is preserved"
        Remove-Item -LiteralPath $hcMarkerPath
    }
    $hcSyncLock = [IO.File]::Open((Join-Path $hcFixture.B '.git/happycheats-source-sync.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    try { $null = Invoke-HcFixtureSync $hcFixture.B -Reject } finally { $hcSyncLock.Dispose() }
    $null = Invoke-HcFixtureGit $hcFixture.B @('switch', '--detach')
    $null = Invoke-HcFixtureSync $hcFixture.B -Reject
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.B) -ceq $hcBefore) 'detached checkout is preserved'
    $null = Invoke-HcFixtureGit $hcFixture.B @('switch', 'main')
    $null = Invoke-HcFixtureGit $hcFixture.B @('config', 'branch.main.merge', 'refs/heads/another-branch')
    $null = Invoke-HcFixtureSync $hcFixture.B -Reject
    $null = Invoke-HcFixtureGit $hcFixture.B @('config', 'branch.main.merge', 'refs/heads/main')
    $null = Invoke-HcFixtureGit $hcFixture.B @('config', 'remote.origin.url', 'https://example.invalid/another/repository.git')
    $null = Invoke-HcFixtureSync $hcFixture.B 'Push' -Reject
    $null = Invoke-HcFixtureGit $hcFixture.B @('config', 'remote.origin.url', $hcOfficialUrl)
    $null = Invoke-HcFixtureGit $hcFixture.B @('config', 'remote.origin.pushurl', 'https://example.invalid/another/repository.git')
    $null = Invoke-HcFixtureSync $hcFixture.B 'Push' -Reject
    $null = Invoke-HcFixtureGit $hcFixture.B @('config', '--unset', 'remote.origin.pushurl')
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.B) -ceq $hcBefore) 'operation and repository guard failures never advance source'

    $hcFixture = New-HcSourceFixture 'branches'
    $null = Invoke-HcFixtureGit $hcFixture.A @('switch', '-c', 'codex/handoff-a')
    $hcTask = Add-HcFixtureCommit $hcFixture.A 'task-source'
    $null = Invoke-HcFixtureSync $hcFixture.A
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.A) -ceq $hcTask) 'receiving an unpublished task preserves its source'
    $null = Invoke-HcFixtureGit $hcFixture.A @('branch', 'codex/keep-local')
    $null = Invoke-HcFixtureGit $hcFixture.A @('config', 'remote.origin.mirror', 'true')
    $null = Invoke-HcFixtureSync $hcFixture.A 'Push'
    Assert-HcSourceTest ((Invoke-HcFixtureGit $hcFixture.A @('config', '--bool', 'remote.origin.mirror')) -ceq 'true') 'safe publication leaves saved mirror configuration unchanged'
    Assert-HcSourceTest ([string]::IsNullOrWhiteSpace((Invoke-HcFixtureGit $hcFixture.Remote @('for-each-ref', '--format=%(refname)', 'refs/heads/codex/keep-local')))) 'safe publication never mirrors unrelated local branches'
    Assert-HcSourceTest ((Invoke-HcFixtureGit $hcFixture.A @('rev-parse', '--abbrev-ref', '@{upstream}')) -ceq 'origin/codex/handoff-a') 'first task push establishes its same-name upstream'
    $null = Invoke-HcFixtureSync $hcFixture.B 'Status'
    $null = Invoke-HcFixtureGit $hcFixture.B @('switch', '--track', 'origin/codex/handoff-a')
    $null = Invoke-HcFixtureSync $hcFixture.B
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.B) -ceq $hcTask) 'published task branch can be continued on the other PC'
    $hcUnsent = Add-HcFixtureCommit $hcFixture.B 'unsent'
    $null = Invoke-HcFixtureSync $hcFixture.B
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.B) -ceq $hcUnsent -and (Invoke-HcFixtureGit $hcFixture.Remote @('rev-parse', 'refs/heads/codex/handoff-a')) -ceq $hcTask) 'Pull keeps ahead-only commits local without publishing them'
    $null = Invoke-HcFixtureGit $hcFixture.B @('switch', '-c', 'unprefixed-task')
    $null = Invoke-HcFixtureSync $hcFixture.B 'Push' -Reject

    $hcFixture = New-HcSourceFixture 'divergence'
    $hcAHead = Add-HcFixtureCommit $hcFixture.A 'a-divergent'
    $hcBHead = Add-HcFixtureCommit $hcFixture.B 'b-divergent'
    $null = Invoke-HcFixtureSync $hcFixture.A 'Push'
    $null = Invoke-HcFixtureSync $hcFixture.B -Reject
    $null = Invoke-HcFixtureSync $hcFixture.B 'Push' -Reject
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.B) -ceq $hcBHead -and (Invoke-HcFixtureGit $hcFixture.Remote @('rev-parse', 'main')) -ceq $hcAHead) 'divergence preserves both histories without automatic merge or force push'

    $hcFixture = New-HcSourceFixture 'ignored-collision'
    $hcBefore = Get-HcFixtureHead $hcFixture.B
    foreach ($hcCheckout in @($hcFixture.A, $hcFixture.B)) {
        $null = New-Item -ItemType Directory -Path (Join-Path $hcCheckout 'local-only')
    }
    Set-Content -LiteralPath (Join-Path $hcFixture.B '.git/info/exclude') -Value 'local-only/'
    $hcLocalConfig = Join-Path $hcFixture.B 'local-only/settings.txt'
    Set-Content -LiteralPath $hcLocalConfig -Value 'machine-local settings'
    $hcConfigHash = (Get-FileHash -LiteralPath $hcLocalConfig).Hash
    Set-Content -LiteralPath (Join-Path $hcFixture.A 'local-only/settings.txt') -Value 'incoming tracked file'
    $null = Invoke-HcFixtureGit $hcFixture.A @('add', 'local-only/settings.txt')
    $null = Invoke-HcFixtureGit $hcFixture.A @('commit', '-m', 'incoming path collision')
    $null = Invoke-HcFixtureSync $hcFixture.A 'Push'
    $null = Invoke-HcFixtureSync $hcFixture.B -Reject
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.B) -ceq $hcBefore -and (Get-FileHash -LiteralPath $hcLocalConfig).Hash -ceq $hcConfigHash) 'incoming tracked files cannot overwrite ignored local configuration'

    $hcFixture = New-HcSourceFixture 'transport'
    $hcBefore = Get-HcFixtureHead $hcFixture.A
    $hcMissingUrl = ([Uri](Join-Path $hcFixture.Root 'missing.git')).AbsoluteUri
    $null = Invoke-HcFixtureGit $hcFixture.A @('config', '--unset', "url.$($hcFixture.FileUrl).insteadOf")
    $null = Invoke-HcFixtureGit $hcFixture.A @('config', "url.$hcMissingUrl.insteadOf", $hcOfficialUrl)
    $null = Invoke-HcFixtureSync $hcFixture.A -Reject
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.A) -ceq $hcBefore) 'failed fetch leaves checkout unchanged'
    $null = Invoke-HcFixtureGit $hcFixture.A @('config', '--unset', "url.$hcMissingUrl.insteadOf")
    $null = Invoke-HcFixtureGit $hcFixture.A @('config', "url.$($hcFixture.FileUrl).insteadOf", $hcOfficialUrl)
    $hcPending = Add-HcFixtureCommit $hcFixture.A 'push-rejected'
    $hcHook = Join-Path $hcFixture.A '.git/hooks/pre-push'
    [IO.File]::WriteAllText($hcHook, "#!/bin/sh`nexit 1`n", [Text.UTF8Encoding]::new($false))
    $null = Invoke-HcFixtureSync $hcFixture.A 'Push' -Reject
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.A) -ceq $hcPending -and (Invoke-HcFixtureGit $hcFixture.Remote @('rev-parse', 'main')) -ceq $hcBefore) 'rejected push keeps local commit and remote history'
    Remove-Item -LiteralPath $hcHook
    $hcRacing = Add-HcFixtureCommit $hcFixture.B 'concurrent-pc'
    $null = Invoke-HcFixtureGit $hcFixture.B @('push', 'origin', 'HEAD:refs/heads/race-reserved')
    # Simulate the other PC advancing main after push advertised its old ref.
    $hcRemoteShellPath = $hcFixture.Remote.Replace('\', '/').Replace("'", "'\''")
    [IO.File]::WriteAllText($hcHook, "#!/bin/sh`ngit --git-dir='$hcRemoteShellPath' update-ref refs/heads/main $hcRacing`n", [Text.UTF8Encoding]::new($false))
    $null = Invoke-HcFixtureSync $hcFixture.A 'Push' -Reject
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.A) -ceq $hcPending -and (Invoke-HcFixtureGit $hcFixture.Remote @('rev-parse', 'main')) -ceq $hcRacing) 'concurrent remote advancement is never overwritten'

    $hcFixture = New-HcSourceFixture 'local-commit-race'
    $hcReviewed = Add-HcFixtureCommit $hcFixture.A 'reviewed-for-push'
    $hcHook = Join-Path $hcFixture.A '.git/hooks/pre-push'
    $hcCheckoutShellPath = $hcFixture.A.Replace('\', '/').Replace("'", "'\''")
    [IO.File]::WriteAllText($hcHook, "#!/bin/sh`ngit -C '$hcCheckoutShellPath' commit --allow-empty -m 'added during push'`n", [Text.UTF8Encoding]::new($false))
    $null = Invoke-HcFixtureSync $hcFixture.A 'Push' -Reject
    Assert-HcSourceTest ((Get-HcFixtureHead $hcFixture.A) -cne $hcReviewed -and (Invoke-HcFixtureGit $hcFixture.Remote @('rev-parse', 'main')) -ceq $hcReviewed) 'push publishes only the inspected commit when the local branch advances concurrently'

    $hcFixture = New-HcSourceFixture 'custom-fetch-mapping'
    $null = Invoke-HcFixtureGit $hcFixture.A @('config', 'remote.origin.fetch', '+refs/heads/main:refs/remotes/origin/main')
    $null = Invoke-HcFixtureGit $hcFixture.A @('switch', '-c', 'codex/custom-fetch')
    $hcReviewed = Add-HcFixtureCommit $hcFixture.A 'custom-fetch-task'
    $hcPublishedState = & $hcSyncScript -RepositoryPath $hcFixture.A -Publish
    Assert-HcSourceTest ($hcPublishedState.State -ceq 'Current' -and $hcPublishedState.LocalCommit -ceq $hcReviewed -and
        $hcPublishedState.RemoteCommit -ceq $hcReviewed -and $hcPublishedState.Ahead -eq 0 -and $hcPublishedState.Behind -eq 0) 'successful publication returns current structured state despite a custom fetch mapping'
    Assert-HcSourceTest ((Invoke-HcFixtureGit $hcFixture.A @('rev-parse', 'refs/remotes/origin/codex/custom-fetch')) -ceq $hcReviewed) 'publication refreshes its exact tracking ref when saved fetch mappings exclude it'

    Write-Host "Source sync safety checks passed: $hcCount (isolated local Git fixtures; no physical second-PC or GitHub validation)."
} finally {
    foreach ($hcName in $hcSavedEnvironment.Keys) {
        if ($null -eq $hcSavedEnvironment[$hcName]) {
            Remove-Item -LiteralPath "Env:$hcName" -ErrorAction SilentlyContinue
        } else {
            [Environment]::SetEnvironmentVariable($hcName, $hcSavedEnvironment[$hcName], 'Process')
        }
    }
    $hcResolvedRoot = [IO.Path]::GetFullPath($hcTestRoot)
    $hcParentPrefix = [IO.Path]::TrimEndingDirectorySeparator($hcTempParent) + [IO.Path]::DirectorySeparatorChar
    if (!$hcResolvedRoot.StartsWith($hcParentPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($hcResolvedRoot) -notmatch '^HappyCheats-SourceSync-Tests-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the generated source-sync fixture directory.'
    }
    if (Test-Path -LiteralPath $hcResolvedRoot) { Remove-Item -LiteralPath $hcResolvedRoot -Recurse -Force }
}
