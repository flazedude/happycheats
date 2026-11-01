#requires -Version 7.0
[CmdletBinding()]
param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
# This is a local GPD operation, never a remote key/configuration transfer.
if (!(Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq '192.168.2.59')) { throw 'Run this helper on GPD-PC (192.168.2.59). No server was changed.' }
$hcIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (!([Security.Principal.WindowsPrincipal]::new($hcIdentity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this helper in Administrator PowerShell 7 on GPD-PC.' }
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'CheatUpdates.Common.ps1')
. (Join-Path $PSScriptRoot 'LanMirrorSupervisor.Common.ps1')
$hcState = Join-Path $hcRoot 'artifacts/lan-updates'
$hcConfigPath = Join-Path $hcState 'server.json'
$hcConfig = Get-Content -LiteralPath $hcConfigPath -Raw | ConvertFrom-Json
if ($hcConfig.Address -cne '192.168.2.59' -or $hcConfig.Port -ne 17845 -or $hcConfig.BaseUrl -cne 'http://192.168.2.59:17845/happycheats') { throw 'This checkout is not the existing GPD .59 host. Its configuration was preserved.' }
$hcRemote = & git -C $hcRoot remote get-url origin
if ($LASTEXITCODE -ne 0 -or $hcRemote -notmatch '^(https://github\.com/|git@github\.com:)flazedude/happycheats(\.git)?/?$') { throw 'Use the official HappyCheats checkout.' }
$hcBranch = & git -C $hcRoot branch --show-current
if ($LASTEXITCODE -ne 0 -or $hcBranch -cne 'main') { throw 'Deploy from clean integrated main. Preserve task-branch work and integrate it first.' }
$hcSource = & git -C $hcRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or (& git -C $hcRoot status --porcelain --untracked-files=all)) { throw 'Source has unfinished work. It was preserved; no server was changed.' }
$hcServerDirectory = Join-Path $hcRoot 'artifacts/lan-update-server'
$hcServerExe = Join-Path $hcServerDirectory 'HappyCheats.UpdateServer.exe'
foreach ($hcPath in @($hcConfigPath, $hcServerExe, $hcConfig.PublicDirectory)) { Assert-HcMirrorSafePath $hcPath }
if (!(Test-Path -LiteralPath $hcServerExe -PathType Leaf)) { throw 'The existing GPD server binary is missing.' }
$hcTask = Get-ScheduledTask -TaskName 'HappyCheats LAN Update Server' -ErrorAction Stop
$hcExpectedArguments = '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + (Join-Path $PSScriptRoot 'Run-LanUpdateServer.ps1') + '"'
if ($hcTask.Actions.Count -ne 1 -or $hcTask.Actions[0].Arguments -ine $hcExpectedArguments -or
    [IO.Path]::GetFileName($hcTask.Actions[0].Execute) -ine 'powershell.exe' -or $hcTask.State -eq 'Disabled') {
    throw 'The startup task points to another checkout, uses another launcher, or is disabled. It was preserved. Report its action before repairing it.'
}
$hcRule = Get-NetFirewallRule -Name 'HappyCheats-LAN-Updates' -ErrorAction Stop
if (($hcRule | Get-NetFirewallApplicationFilter).Program -ine $hcServerExe) { throw 'The firewall rule still points to another server executable. Correct that specific path before deploying.' }
$hcOwners = @(Get-NetTCPConnection -State Listen -LocalPort 17845 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
if ($hcOwners.Count -ne 1) { throw 'Expected one existing update server on port 17845; no server was changed.' }
$hcExisting = Get-Process -Id $hcOwners[0] -ErrorAction Stop
if ($hcExisting.Path -ine $hcServerExe) { throw 'Port 17845 belongs to a different program. It was preserved.' }
$hcExistingId = $hcExisting.Id
$hcExistingStart = $hcExisting.StartTime.ToUniversalTime().Ticks
# Verify the protected local public identity before any service interruption.
$hcVerification = Get-HcLanMirrorVerificationConfiguration $hcConfig
$hcKey = Read-HcMirrorPublicKey $hcVerification.PublicKeyPath
try {
    $hcPreviousFeed = Read-HcSignedMirrorPayload (Join-Path $hcConfig.PublicDirectory 'latest.json') $hcKey
    if ($hcPreviousFeed.channel -cne 'stable' -or !$hcPreviousFeed.packageUrl.StartsWith($hcConfig.BaseUrl + '/', [StringComparison]::Ordinal)) { throw 'The existing signed feed is not GPD stable. It was preserved.' }
} finally { $hcKey.Dispose() }
$hcStage = Join-Path $hcRoot ('artifacts/gpd-component-stage/' + [Guid]::NewGuid().ToString('N'))
$hcStageServer = Join-Path $hcStage 'server'
Assert-HcMirrorSafePath $hcStage
Write-Host 'Building new server in isolation; the current server stays running...'
& dotnet publish (Join-Path $hcRoot 'tools/HappyCheats.UpdateServer') -c Release -r win-x64 --self-contained true --artifacts-path (Join-Path $hcStage 'build') -o $hcStageServer --nologo -v minimal
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath (Join-Path $hcStageServer 'HappyCheats.UpdateServer.exe'))) { throw 'Server staging failed. The running server was not changed.' }
if ((& git -C $hcRoot rev-parse HEAD) -cne $hcSource -or (& git -C $hcRoot status --porcelain --untracked-files=all)) { throw 'Source changed during staging. No server was changed; retry after reviewing it.' }
if ($PrepareOnly) { Write-Host "Staged server: $hcStageServer. No task, firewall, configuration or active binary was changed."; return }
$hcBackup = Join-Path $hcState ('component-backups/' + [Guid]::NewGuid().ToString('N'))
Assert-HcMirrorSafePath $hcBackup
$hcLock = $null; $hcStopped = $false; $hcReplaced = $false; $hcFailure = $null
try {
    try { $hcLock = [IO.File]::Open((Join-Path $hcState 'publish.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::Write, [IO.FileShare]::None) }
    catch { throw 'A local publisher or mirror refresh is active. No server was changed. Wait for it to finish, then retry.' }
    New-Item -ItemType Directory -Force -Path $hcBackup | Out-Null
    Copy-Item -LiteralPath $hcServerDirectory -Destination $hcBackup -Recurse
    Copy-Item -LiteralPath $hcConfigPath -Destination (Join-Path $hcBackup 'server.json')
    Export-ScheduledTask -TaskName $hcTask.TaskName | Set-Content -LiteralPath (Join-Path $hcBackup 'startup-task.xml') -Encoding utf8NoBOM
    $hcStopped = $true
    & (Join-Path $PSScriptRoot 'Stop-LanUpdateServer.ps1')
    # Also handle a manually started copy, but only the exact preflight-verified process.
    $hcRemaining = Get-Process -Id $hcExistingId -ErrorAction SilentlyContinue
    if ($hcRemaining) {
        if ($hcRemaining.Path -ine $hcServerExe -or $hcRemaining.StartTime.ToUniversalTime().Ticks -ne $hcExistingStart) { throw 'The server process identity changed. No unrelated process was stopped.' }
        Stop-Process -Id $hcExistingId -ErrorAction Stop
        if (!$hcRemaining.WaitForExit(10000)) { throw 'The old server did not stop; its backup is retained.' }
    }
    $hcReplaced = $true
    Copy-Item -Path (Join-Path $hcStageServer '*') -Destination $hcServerDirectory -Recurse -Force
    $hcRuntime = $hcConfig.SyncPowerShellPath
    if (!$hcRuntime -or !(Test-Path -LiteralPath $hcRuntime -PathType Leaf)) { $hcRuntime = (Get-Process -Id $PID).Path }
    Enable-HcLanMirrorConfiguration -ConfigPath $hcConfigPath -PowerShellPath $hcRuntime -IntervalSeconds (Get-HcLanMirrorIntervalSeconds $hcConfig)
} catch {
    $hcFailure = $_
    if ($hcReplaced) {
        Copy-Item -Path (Join-Path $hcBackup 'lan-update-server/*') -Destination $hcServerDirectory -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $hcBackup 'server.json') -Destination $hcConfigPath -Force
        Write-Warning "Restored the previous server and configuration. Backup: $hcBackup"
    }
} finally {
    if ($hcLock) { $hcLock.Dispose() }
}
if ($hcStopped) {
    try { & (Join-Path $PSScriptRoot 'Start-LanUpdateServer.ps1') }
    catch {
        $hcStartupFailure = $_
        if (!$hcFailure -and $hcReplaced) {
            Write-Warning 'The new server did not start. Restoring the previous host before reporting the failure.'
            & (Join-Path $PSScriptRoot 'Stop-LanUpdateServer.ps1')
            $hcRestoreLock = [IO.File]::Open((Join-Path $hcState 'publish.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                Copy-Item -Path (Join-Path $hcBackup 'lan-update-server/*') -Destination $hcServerDirectory -Recurse -Force
                Copy-Item -LiteralPath (Join-Path $hcBackup 'server.json') -Destination $hcConfigPath -Force
            } finally { $hcRestoreLock.Dispose() }
            & (Join-Path $PSScriptRoot 'Start-LanUpdateServer.ps1')
        }
        throw "Server startup needs attention. Backup: $hcBackup. $($hcStartupFailure.Exception.Message)"
    }
}
if ($hcFailure) { throw $hcFailure }
# The unchanged startup task launches the updated sync helper automatically.
$hcReady = $false; $hcDeadline = [DateTime]::UtcNow.AddSeconds(45)
do {
    try {
        $hcResponse = Invoke-WebRequest ($hcConfig.BaseUrl + '/cheats.json') -NoProxy -TimeoutSec 3 -SkipHttpErrorCheck
        if ($hcResponse.StatusCode -eq 200) {
            $hcProof = Join-Path $hcStage 'hosted-cheats.json'
            Write-HcCheatWebContent $hcProof $hcResponse.Content
            $hcKey = Read-HcMirrorPublicKey (Get-HcLanMirrorVerificationConfiguration (Get-Content -LiteralPath $hcConfigPath -Raw | ConvertFrom-Json)).PublicKeyPath
            try { $hcCatalog = Read-HcCheatCatalog $hcProof $hcKey } finally { $hcKey.Dispose() }
            $hcReady = $true
        }
    } catch [Net.Http.HttpRequestException] { }
    catch [Threading.Tasks.TaskCanceledException] { }
    if (!$hcReady) { Start-Sleep -Seconds 1 }
} while (!$hcReady -and [DateTime]::UtcNow -lt $hcDeadline)
[ordered]@{serverDeployed=$true;cheatFeedReady=$hcReady;sourceCommit=$hcSource;backup=$hcBackup;taskState=[string](Get-ScheduledTask -TaskName $hcTask.TaskName).State;mirrorLog=(Join-Path $hcState 'mirror.log');mirrorErrorLog=(Join-Path $hcState 'mirror-error.log')} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $hcStage 'result.json')
if (!$hcReady) { Write-Host 'Server is updated and automatic mirroring is enabled. Initial cheat refresh is still pending; send the result/log paths for follow-up. Do not rebuild or re-sign packages.' }
