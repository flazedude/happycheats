#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryPath = 'D:\Coding\happycheats', [switch]$AdminPhase, [string]$ExpectedCommit,
    [string]$ExpectedUserSid, [string]$ResultPath, [string]$DeploymentSource)
$ErrorActionPreference = 'Stop'
$hcExpectedBase = 'http://192.168.2.59:17845/happycheats'

function Assert-HcGpdMachine {
    if (!(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object IPAddress -eq '192.168.2.59')) {
        throw 'Run this launcher on GPD-PC (192.168.2.59). This computer was not changed.'
    }
}
function Get-HcGpdConfiguration([string]$Root) {
    $hcFullRoot = [IO.Path]::GetFullPath($Root)
    if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($hcFullRoot)).DriveType -ne [IO.DriveType]::Fixed) {
        throw 'Select GPD-PC''s existing local HappyCheats checkout, not the shared V: drive.'
    }
    $hcConfig = Get-Content -LiteralPath (Join-Path $hcFullRoot 'artifacts/lan-updates/server.json') -Raw | ConvertFrom-Json
    if ($hcConfig.Address -cne '192.168.2.59' -or $hcConfig.Port -ne 17845 -or $hcConfig.BaseUrl -cne $hcExpectedBase) {
        throw 'This checkout is not configured for GPD-PC''s existing .59 LAN server. Its settings were preserved.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $hcFullRoot 'artifacts/lan-update-server/HappyCheats.UpdateServer.exe') -PathType Leaf)) {
        throw 'The existing published LAN server is missing. Ask for help on GPD-PC before continuing.'
    }
    return $hcConfig
}
function Assert-HcGpdRepository([string]$Root, [string]$Commit, [switch]$AllowUnfinished, [switch]$AllowDetached) {
    $hcTop = & git -C $Root rev-parse --show-toplevel
    if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($hcTop).TrimEnd('\','/') -ine [IO.Path]::GetFullPath($Root).TrimEnd('\','/')) {
        throw 'Select the root of GPD-PC''s existing HappyCheats checkout.'
    }
    $hcRemote = & git -C $Root remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $hcRemote -notmatch '^(https://github\.com/|git@github\.com:|ssh://git@github\.com/)flazedude/happycheats(\.git)?/?$') {
        throw 'The checkout does not use the expected HappyCheats GitHub repository.'
    }
    $hcDirty = & git -C $Root status --porcelain --untracked-files=all
    if ($LASTEXITCODE -ne 0 -or ($hcDirty -and !$AllowUnfinished)) { throw 'Deployment source is not clean. Development work was preserved.' }
    $null = & git -C $Root symbolic-ref --quiet HEAD
    if ($LASTEXITCODE -ne 0 -and !$AllowDetached) { throw 'Select the intended deployment source before setup.' }
    if ($Commit) {
        $hcHead = & git -C $Root rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $hcHead -cne $Commit) { throw 'Source changed during setup. Run the launcher again after the other work finishes.' }
    }
}
function Find-HcGpdRepository {
    $hcCandidates = [Collections.Generic.List[string]]::new()
    foreach ($hcServer in @(Get-Process -Name 'HappyCheats.UpdateServer' -ErrorAction SilentlyContinue)) {
        if ($hcServer.Path) { $hcCandidates.Add((Split-Path (Split-Path (Split-Path $hcServer.Path)))) }
    }
    $hcTask = Get-ScheduledTask -TaskName 'HappyCheats LAN Update Server' -ErrorAction SilentlyContinue
    if ($hcTask) {
        foreach ($hcAction in $hcTask.Actions) { if ($hcAction.WorkingDirectory) { $hcCandidates.Add($hcAction.WorkingDirectory) } }
    }
    $hcMatches = @($hcCandidates | Sort-Object -Unique | Where-Object {
        try { $null = Get-HcGpdConfiguration $_; $true } catch { $false }
    })
    if ($hcMatches.Count -eq 1) { return $hcMatches[0] }
    return (Read-Host 'Paste the full path to GPD-PC''s existing HappyCheats folder').Trim().Trim('"')
}

if ($AdminPhase) {
    $hcInstallerStarted = $false
    $hcRecovery = $null
    try {
        Assert-HcGpdMachine
        $hcIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
        if ($hcIdentity.User.Value -cne $ExpectedUserSid -or
            !([Security.Principal.WindowsPrincipal]::new($hcIdentity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Approve administrator access for the same Windows account that owns the GPD-PC checkout.'
        }
        $hcConfig = Get-HcGpdConfiguration $RepositoryPath
        Assert-HcGpdRepository $RepositoryPath -AllowUnfinished -AllowDetached
        Assert-HcGpdRepository $DeploymentSource $ExpectedCommit -AllowDetached
        . (Join-Path $DeploymentSource 'scripts/LanMirror.Common.ps1')
        $hcKey = Read-HcMirrorPublicKey $hcConfig.PublicKeyPath
        $hcKey.Dispose()
        $hcStartedAt = [DateTimeOffset]::UtcNow
        $hcInstallerStarted = $true
        & (Join-Path $DeploymentSource 'scripts/Deploy-GPD-Components.ps1') -HostRoot $RepositoryPath -ExpectedCommit $ExpectedCommit *> ($ResultPath + '.log')
        $hcConfig = Get-HcGpdConfiguration $RepositoryPath
        $hcTask = Get-ScheduledTask -TaskName 'HappyCheats LAN Update Server'
        $hcHealth = Invoke-WebRequest -Uri 'http://192.168.2.59:17845/health' -NoProxy -TimeoutSec 5
        if (!$hcConfig.MirrorEnabled -or ($hcConfig.MirrorIntervalSeconds -lt 60 -or $hcConfig.MirrorIntervalSeconds -gt 86400) -or
            $hcTask.State -ne 'Running' -or $hcHealth.Content -cne 'HappyCheats LAN update server') {
            throw 'The mirror task or LAN health check did not pass. Inspect the local setup log.'
        }
        $hcRefresh = 'Pending: task is enabled; the first download may still be running.'
        $hcLogPath = Join-Path $RepositoryPath 'artifacts/lan-updates/mirror-supervisor.log'
        $hcDeadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
        do {
            $hcFreshResult = @(Get-Content -LiteralPath $hcLogPath -Tail 12 -ErrorAction SilentlyContinue | Where-Object {
                if ([string]$_ -match '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}Z) Mirror finished with exit code (\d+)\.$') {
                    [DateTimeOffset]::Parse($Matches[1]) -ge $hcStartedAt
                }
            })
            if ($hcFreshResult.Count) {
                $hcRefresh = if ([string]$hcFreshResult[-1] -match 'exit code 0\.$') { 'Verified: an automatic refresh completed successfully.' }
                    else { 'Pending: the first refresh failed; the task will retry after five minutes. Inspect mirror-error.log.' }
                break
            }
            Start-Sleep -Seconds 1
        } while ([DateTimeOffset]::UtcNow -lt $hcDeadline)
        $hcTask = Get-ScheduledTask -TaskName 'HappyCheats LAN Update Server'
        if ($hcTask.State -ne 'Running') { throw 'The startup task stopped during verification. Inspect the local setup log.' }
        [ordered]@{ success=$true; computer=$env:COMPUTERNAME; host='192.168.2.59'; mirrorEnabled=$true;
            intervalSeconds=$hcConfig.MirrorIntervalSeconds; taskState=[string]$hcTask.State; refreshStatus=$hcRefresh; sourceCommit=$ExpectedCommit;
            repository=$RepositoryPath; deploymentSource=$DeploymentSource; completedAt=[DateTimeOffset]::UtcNow.ToString('o') } |
            ConvertTo-Json | Set-Content -LiteralPath $ResultPath
        exit 0
    } catch {
        $hcFailure = $_.Exception.Message
        if ($hcInstallerStarted) {
            try {
                & (Join-Path $DeploymentSource 'scripts/Start-LanUpdateServer.ps1') -HostRoot $RepositoryPath *>> ($ResultPath + '.log')
                $hcRecovery = 'Existing LAN server restarted or remained available.'
            } catch { $hcRecovery = 'Server restart needs attention: ' + $_.Exception.Message }
        }
        [ordered]@{ success=$false; error=$hcFailure; serverRecovery=$hcRecovery; completedAt=[DateTimeOffset]::UtcNow.ToString('o') } |
            ConvertTo-Json | Set-Content -LiteralPath $ResultPath
        exit 1
    }
}

try {
    Assert-HcGpdMachine
    if (!$RepositoryPath) { $RepositoryPath = Find-HcGpdRepository }
    $RepositoryPath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepositoryPath))
    $null = Get-HcGpdConfiguration $RepositoryPath
    Assert-HcGpdRepository $RepositoryPath -AllowUnfinished -AllowDetached
    $hcBundleManifest = Join-Path $PSScriptRoot 'Deploy-GPD-Cheats-source.json'
    if (Test-Path -LiteralPath $hcBundleManifest) {
        $hcBundle = Get-Content -LiteralPath $hcBundleManifest -Raw | ConvertFrom-Json
        if ($hcBundle.commit -cnotmatch '^[0-9a-f]{40}$' -or $hcBundle.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
            $hcBundle.file -cne ('HappyCheats-GPD-source-' + $hcBundle.commit + '.bundle')) { throw 'Invalid deployment bundle identity.' }
        $hcBundlePath = Join-Path $PSScriptRoot $hcBundle.file
        if ((Get-FileHash -LiteralPath $hcBundlePath).Hash -ine $hcBundle.sha256) { throw 'Deployment bundle failed SHA-256 verification.' }
        Write-Host 'Using the verified shared-drive source bundle; no GitHub fetch is needed...'
        & git -C $RepositoryPath bundle verify $hcBundlePath
        if ($LASTEXITCODE -ne 0) { throw 'Git rejected the deployment source bundle. Development files were not changed.' }
        $ExpectedCommit = $hcBundle.commit
        & git -C $RepositoryPath fetch --no-tags $hcBundlePath ('refs/heads/main:refs/happycheats/deployments/' + $ExpectedCommit)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot import the verified source bundle. Development files were not changed.' }
    } else {
        Write-Host 'Fetching a clean deployment snapshot; the current branch and unfinished files stay unchanged...'
        & git -C $RepositoryPath -c http.version=HTTP/1.1 -c http.sslBackend=openssl fetch --no-tags origin 'refs/heads/main:refs/remotes/origin/main'
        if ($LASTEXITCODE -ne 0) { throw 'GitHub source fetch failed. The server and development files were not changed.' }
        $ExpectedCommit = & git -C $RepositoryPath rev-parse refs/remotes/origin/main
        if ($LASTEXITCODE -eq 0 -and $ExpectedCommit -cmatch '^[0-9a-f]{40}$') {
            & git -C $RepositoryPath update-ref ('refs/happycheats/deployments/' + $ExpectedCommit) $ExpectedCommit
            if ($LASTEXITCODE -ne 0) { throw 'Could not pin the fetched deployment source.' }
        }
    }
    if ($LASTEXITCODE -ne 0 -or $ExpectedCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot pin the received source commit.' }
    $hcSourceParent = Join-Path $env:LOCALAPPDATA 'HappyCheatsPublisher/HostDeployments'
    New-Item -ItemType Directory -Force -Path $hcSourceParent | Out-Null
    $DeploymentSource = Join-Path $hcSourceParent ('GPD-' + [Guid]::NewGuid().ToString('N'))
    & git -C $RepositoryPath worktree add --detach $DeploymentSource $ExpectedCommit
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the isolated deployment worktree. Development files and server were not changed.' }
    Assert-HcGpdRepository $DeploymentSource $ExpectedCommit -AllowDetached
    foreach ($hcName in @('Deploy-GPD-Components.ps1','Sync-LanUpdate.ps1','Sync-CheatUpdate.ps1','CheatUpdates.Common.ps1','LanMirror.Common.ps1','LanMirrorSupervisor.Common.ps1')) {
        if (!(Test-Path -LiteralPath (Join-Path $DeploymentSource ('scripts/' + $hcName)) -PathType Leaf)) { throw "Required setup script is missing: $hcName" }
    }
    # Stage locally before elevation: mapped V: and share credentials may not
    # be available to the elevated token. No key or server.json is copied.
    $hcLocalDirectory = Join-Path $env:LOCALAPPDATA ('HappyCheatsPublisher/Setup/GPD-' + [Guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $hcLocalDirectory
    $hcLocalHelper = Join-Path $hcLocalDirectory 'Deploy-GPD-Cheats.ps1'
    Copy-Item -LiteralPath $PSCommandPath -Destination $hcLocalHelper
    $ResultPath = Join-Path $hcLocalDirectory 'result.json'
    $hcSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    Write-Host 'Approve the Windows administrator prompt. The server is staged first, then the existing host is briefly restarted. A result and log will be saved beside this launcher.'
    $hcProcess = Start-Process -FilePath (Get-Process -Id $PID).Path -Verb RunAs -WindowStyle Hidden -PassThru -Wait -ArgumentList @(
        '-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$hcLocalHelper+'"'),'-AdminPhase',
        '-RepositoryPath',('"'+$RepositoryPath+'"'),'-ExpectedCommit',$ExpectedCommit,'-ExpectedUserSid',$hcSid,
        '-ResultPath',('"'+$ResultPath+'"'),'-DeploymentSource',('"'+$DeploymentSource+'"'))
    if (!(Test-Path -LiteralPath $ResultPath)) { throw "Setup did not produce a result. Local diagnostics: $hcLocalDirectory" }
    $hcResult = Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json
    $hcResultName = 'GPD-cheat-deployment-result-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '.json'
    try {
        Copy-Item -LiteralPath $ResultPath -Destination (Join-Path $PSScriptRoot $hcResultName)
        if (Test-Path -LiteralPath ($ResultPath + '.log')) { Copy-Item -LiteralPath ($ResultPath + '.log') -Destination (Join-Path $PSScriptRoot ($hcResultName + '.log')) }
        Write-Host "Setup report saved beside the launcher: $hcResultName"
    } catch { Write-Host "Share report could not be saved; the local result is $ResultPath" }
    if (!$hcResult.success -or $hcProcess.ExitCode -ne 0) {
        throw ($hcResult.error + ' ' + $hcResult.serverRecovery + " Local log: $ResultPath.log")
    }
    Write-Host 'GPD server components are deployed and automatic signed mirroring is enabled.' -ForegroundColor Green
    Write-Host $hcResult.refreshStatus
    Write-Host "Local logs: $RepositoryPath\artifacts\lan-updates"
} catch {
    $hcFailureReport = Join-Path $PSScriptRoot ('GPD-cheat-deployment-error-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '.txt')
    try { [IO.File]::WriteAllText($hcFailureReport, $_.Exception.ToString()); Write-Host "Error report: $hcFailureReport" } catch { }
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
