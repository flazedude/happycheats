[CmdletBinding()]
param([switch]$EnableMirror, [ValidateRange(60,86400)][int]$MirrorIntervalSeconds = 300)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LanMirrorSupervisor.Common.ps1')
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (!([Security.Principal.WindowsPrincipal]::new($hcIdentity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this setup script once in administrator PowerShell.'
}
$hcConfigPath = Join-Path $hcRoot 'artifacts\lan-updates\server.json'
$hcServerExe = Join-Path $hcRoot 'artifacts\lan-update-server\HappyCheats.UpdateServer.exe'
if (!(Test-Path -LiteralPath $hcConfigPath) -or !(Test-Path -LiteralPath $hcServerExe)) {
    throw 'Run Start-LanUpdateServer.ps1 first to configure and publish the LAN server.'
}
if ($EnableMirror) {
    if ($PSVersionTable.PSVersion.Major -lt 7 -or !(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Sync-LanUpdate.ps1'))) { throw 'Enable mirror setup from PowerShell 7 with the current sync helper present.' }
}
$hcTaskName = 'HappyCheats LAN Update Server'
$hcEntry = Join-Path $PSScriptRoot 'Run-LanUpdateServer.ps1'
$hcPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$hcArguments = '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $hcEntry + '"'
$hcOld = Get-ScheduledTask -TaskName $hcTaskName -ErrorAction SilentlyContinue
if ($hcOld -and ($hcOld.Actions.Count -ne 1 -or $hcOld.Actions[0].Arguments -ne $hcArguments)) {
    throw 'A different task already uses the HappyCheats startup name. Inspect it before replacing it.'
}
# S4U uses this account's local file permissions without storing a password or requiring sign-in.
$hcPrincipal = New-ScheduledTaskPrincipal -UserId $hcIdentity.Name -LogonType S4U -RunLevel Limited
$hcTrigger = New-ScheduledTaskTrigger -AtStartup
$hcTrigger.Delay = 'PT30S'
$hcAction = New-ScheduledTaskAction -Execute $hcPowerShell -Argument $hcArguments -WorkingDirectory $hcRoot
$hcSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1)
# Stop only the verified existing HappyCheats server before handing control to the task.
& (Join-Path $PSScriptRoot 'Stop-LanUpdateServer.ps1')
if ($EnableMirror) {
    Enable-HcLanMirrorConfiguration -ConfigPath $hcConfigPath -PowerShellPath (Get-Process -Id $PID).Path -IntervalSeconds $MirrorIntervalSeconds
}
Register-ScheduledTask -TaskName $hcTaskName -Description 'Starts the HappyCheats LAN update server at Windows boot and restarts it after failure.' -Action $hcAction -Trigger $hcTrigger -Principal $hcPrincipal -Settings $hcSettings -Force | Out-Null
Start-ScheduledTask -TaskName $hcTaskName
& (Join-Path $PSScriptRoot 'Start-LanUpdateServer.ps1')
Write-Host "Installed startup task: $hcTaskName (Windows boot, no interactive sign-in required)."
