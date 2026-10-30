#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryPath, [switch]$AdminPhase, [string]$ExpectedCommit,
    [string]$ExpectedUserSid, [string]$ResultPath)
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
function Assert-HcGpdRepository([string]$Root, [string]$Commit) {
    $hcTop = & git -C $Root rev-parse --show-toplevel
    if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($hcTop).TrimEnd('\','/') -ine [IO.Path]::GetFullPath($Root).TrimEnd('\','/')) {
        throw 'Select the root of GPD-PC''s existing HappyCheats checkout.'
    }
    $hcRemote = & git -C $Root remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $hcRemote -notmatch '^(https://github\.com/|git@github\.com:|ssh://git@github\.com/)flazedude/happycheats(\.git)?/?$') {
        throw 'The checkout does not use the expected HappyCheats GitHub repository.'
    }
    $hcDirty = & git -C $Root status --porcelain --untracked-files=all
    if ($LASTEXITCODE -ne 0 -or $hcDirty) { throw 'The checkout has unfinished work. It was preserved; finish or integrate it before running setup again.' }
    $null = & git -C $Root symbolic-ref --quiet HEAD
    if ($LASTEXITCODE -ne 0) { throw 'The checkout has a detached HEAD. Select its normal development branch before setup.' }
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
        Assert-HcGpdRepository $RepositoryPath $ExpectedCommit
        . (Join-Path $RepositoryPath 'scripts/LanMirror.Common.ps1')
        $hcKey = Read-HcMirrorPublicKey $hcConfig.PublicKeyPath
        $hcKey.Dispose()
        $hcStartedAt = [DateTimeOffset]::UtcNow
        $hcInstallerStarted = $true
        & (Join-Path $RepositoryPath 'scripts/Install-LanUpdateStartup.ps1') -EnableMirror *> ($ResultPath + '.log')
        $hcConfig = Get-HcGpdConfiguration $RepositoryPath
        $hcTask = Get-ScheduledTask -TaskName 'HappyCheats LAN Update Server'
        $hcHealth = Invoke-WebRequest -Uri 'http://192.168.2.59:17845/health' -NoProxy -TimeoutSec 5
        if (!$hcConfig.MirrorEnabled -or $hcConfig.MirrorIntervalSeconds -ne 300 -or
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
            intervalSeconds=300; taskState=[string]$hcTask.State; refreshStatus=$hcRefresh; sourceCommit=$ExpectedCommit;
            repository=$RepositoryPath; completedAt=[DateTimeOffset]::UtcNow.ToString('o') } |
            ConvertTo-Json | Set-Content -LiteralPath $ResultPath
        exit 0
    } catch {
        $hcFailure = $_.Exception.Message
        if ($hcInstallerStarted) {
            try {
                & (Join-Path $RepositoryPath 'scripts/Start-LanUpdateServer.ps1') *>> ($ResultPath + '.log')
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
    Assert-HcGpdRepository $RepositoryPath
    Write-Host 'Updating GPD-PC''s existing HappyCheats source from GitHub...'
    & git -C $RepositoryPath fetch origin
    if ($LASTEXITCODE -ne 0) { throw 'GitHub source download failed. The startup task was not changed.' }
    $ExpectedCommit = & git -C $RepositoryPath rev-parse origin/main
    if ($LASTEXITCODE -ne 0 -or $ExpectedCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'The shared main branch was not found.' }
    & git -C $RepositoryPath merge --ff-only origin/main
    if ($LASTEXITCODE -ne 0) { throw 'Source needs a deliberate merge. Existing work was preserved; ask for help on GPD-PC.' }
    Assert-HcGpdRepository $RepositoryPath $ExpectedCommit
    foreach ($hcName in @('Install-LanUpdateStartup.ps1','Sync-LanUpdate.ps1','LanMirror.Common.ps1','LanMirrorSupervisor.Common.ps1')) {
        if (!(Test-Path -LiteralPath (Join-Path $RepositoryPath ('scripts/' + $hcName)) -PathType Leaf)) { throw "Required setup script is missing: $hcName" }
    }
    # Stage locally before elevation: mapped V: and share credentials may not
    # be available to the elevated token. No key or server.json is copied.
    $hcLocalDirectory = Join-Path $env:LOCALAPPDATA ('HappyCheatsPublisher/Setup/GPD-' + [Guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $hcLocalDirectory
    $hcLocalHelper = Join-Path $hcLocalDirectory 'Enable-GPD-Mirror.ps1'
    Copy-Item -LiteralPath $PSCommandPath -Destination $hcLocalHelper
    $ResultPath = Join-Path $hcLocalDirectory 'result.json'
    $hcSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    Write-Host 'Approve the Windows administrator prompt. Setup briefly restarts the existing LAN server.'
    $hcProcess = Start-Process -FilePath (Get-Process -Id $PID).Path -Verb RunAs -WindowStyle Hidden -PassThru -Wait -ArgumentList @(
        '-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$hcLocalHelper+'"'),'-AdminPhase',
        '-RepositoryPath',('"'+$RepositoryPath+'"'),'-ExpectedCommit',$ExpectedCommit,'-ExpectedUserSid',$hcSid,
        '-ResultPath',('"'+$ResultPath+'"'))
    if (!(Test-Path -LiteralPath $ResultPath)) { throw "Setup did not produce a result. Local diagnostics: $hcLocalDirectory" }
    $hcResult = Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json
    $hcResultName = 'GPD-setup-result-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '.json'
    try {
        Copy-Item -LiteralPath $ResultPath -Destination (Join-Path $PSScriptRoot $hcResultName)
        Write-Host "Setup report saved beside the launcher: $hcResultName"
    } catch { Write-Host "Share report could not be saved; the local result is $ResultPath" }
    if (!$hcResult.success -or $hcProcess.ExitCode -ne 0) {
        throw ($hcResult.error + ' ' + $hcResult.serverRecovery + " Local log: $ResultPath.log")
    }
    Write-Host 'Automatic GitHub mirroring is enabled on GPD-PC.' -ForegroundColor Green
    Write-Host $hcResult.refreshStatus
    Write-Host "Local logs: $RepositoryPath\artifacts\lan-updates"
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
