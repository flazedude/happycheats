[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'LanMirror.Common.ps1')
$hcFixture=Join-Path ([IO.Path]::GetTempPath()) ('HappyCheats-Gpd-Isolation-'+[Guid]::NewGuid().ToString('N'))
$hcRepo=Join-Path $hcFixture 'development'
$hcSnapshot=Join-Path $hcFixture 'deployment'
function Assert-HcIsolation([bool]$Value,[string]$Message) { if(!$Value){throw $Message} }
try {
    New-Item -ItemType Directory -Path $hcRepo -Force|Out-Null
    & git init -q -b main $hcRepo
    if($LASTEXITCODE -ne 0){throw 'Fixture init failed'}
    [IO.File]::WriteAllText((Join-Path $hcRepo 'work.txt'),'committed source')
    & git -C $hcRepo add work.txt
    & git -C $hcRepo -c user.name=Fixture -c user.email=fixture@example.invalid -c commit.gpgsign=false -c core.hooksPath= commit -qm baseline
    if($LASTEXITCODE -ne 0){throw 'Fixture commit failed'}
    $hcCommit=& git -C $hcRepo rev-parse HEAD
    & git -C $hcRepo switch -qc codex/unfinished-game
    [IO.File]::WriteAllText((Join-Path $hcRepo 'work.txt'),'unfinished trainer changes')
    [IO.File]::WriteAllText((Join-Path $hcRepo 'new-work.txt'),'untracked work')
    [IO.File]::WriteAllText((Join-Path $hcRepo 'staged-work.txt'),'staged unfinished work')
    & git -C $hcRepo add staged-work.txt
    $hcBeforeStatus=(& git -C $hcRepo status --porcelain --untracked-files=all)|Out-String
    $hcBeforeHash=(Get-FileHash (Join-Path $hcRepo 'work.txt')).Hash
    & git -C $hcRepo worktree add --detach -q $hcSnapshot $hcCommit
    if($LASTEXITCODE -ne 0){throw 'Isolated worktree creation failed'}
    Assert-HcIsolation (((& git -C $hcRepo status --porcelain --untracked-files=all)|Out-String) -ceq $hcBeforeStatus) 'Development status changed'
    Assert-HcIsolation ((Get-FileHash (Join-Path $hcRepo 'work.txt')).Hash -ceq $hcBeforeHash) 'Unfinished file changed'
    Assert-HcIsolation ((& git -C $hcRepo branch --show-current) -ceq 'codex/unfinished-game') 'Development branch changed'
    Assert-HcIsolation ([IO.File]::ReadAllText((Join-Path $hcSnapshot 'work.txt')) -ceq 'committed source') 'Snapshot used unfinished code'
    Assert-HcIsolation (!(& git -C $hcSnapshot status --porcelain --untracked-files=all)) 'Snapshot is not clean'
    $hcBundle=Join-Path $hcFixture 'source.bundle'
    & git -C $hcRepo bundle create $hcBundle refs/heads/main
    if($LASTEXITCODE -ne 0){throw 'Fixture bundle creation failed'}
    & git -C $hcRepo fetch --no-tags $hcBundle ('refs/heads/main:refs/happycheats/deployments/'+$hcCommit)
    if($LASTEXITCODE -ne 0){throw 'Offline bundle import failed'}
    Assert-HcIsolation (((& git -C $hcRepo status --porcelain --untracked-files=all)|Out-String) -ceq $hcBeforeStatus) 'Offline bundle import changed unfinished work'
    Assert-HcIsolation ((& git -C $hcRepo rev-parse ('refs/happycheats/deployments/'+$hcCommit)) -ceq $hcCommit) 'Offline deployment pin differs'
    $hcHostConfig=Join-Path $hcRepo 'artifacts/lan-updates/server.json'
    $hcPaths=Resolve-HcMirrorLocations (Join-Path $hcSnapshot 'scripts') $hcHostConfig
    Assert-HcIsolation ($hcPaths.StateRoot -ceq [IO.Path]::GetDirectoryName($hcHostConfig)) 'Explicit host state ignored'
    Assert-HcIsolation ($hcPaths.ConfigPath -ceq $hcHostConfig) 'Explicit host config ignored'
    $hcDefaults=Resolve-HcMirrorLocations (Join-Path $hcRepo 'scripts') ''
    Assert-HcIsolation ($hcDefaults.ConfigPath -ceq $hcHostConfig) 'Default host layout changed'
    Write-Host 'PASS isolated deployment: dirty tracked/untracked work and branch preserved; clean pinned source; explicit host locks/cache/config and legacy defaults verified.'
} finally {
    $hcResolved=[IO.Path]::GetFullPath($hcFixture)
    if(!$hcResolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($hcResolved).StartsWith('HappyCheats-Gpd-Isolation-')){throw 'Unsafe fixture cleanup'}
    if(Test-Path -LiteralPath $hcSnapshot){& git -C $hcRepo worktree remove $hcSnapshot}
    if(Test-Path -LiteralPath $hcResolved){Remove-Item -LiteralPath $hcResolved -Recurse -Force}
}
