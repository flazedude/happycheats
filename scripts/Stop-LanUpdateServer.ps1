[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LanMirrorSupervisor.Common.ps1')
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcPidPath = Join-Path $hcRoot 'artifacts\lan-updates\process.json'
$hcStartupTask = Get-ScheduledTask -TaskName 'HappyCheats LAN Update Server' -ErrorAction SilentlyContinue
if ($hcStartupTask -and $hcStartupTask.State -eq 'Running') { Stop-ScheduledTask -TaskName $hcStartupTask.TaskName }
$hcConfigPath = Join-Path $hcRoot 'artifacts\lan-updates\server.json'
if (Test-Path -LiteralPath $hcConfigPath) {
    $hcConfig = Get-Content -LiteralPath $hcConfigPath -Raw | ConvertFrom-Json
    Stop-HcLanMirrorProcess -Config $hcConfig -StateRoot (Split-Path $hcPidPath)
}
if (!(Test-Path -LiteralPath $hcPidPath)) { Write-Host 'No saved LAN server process.'; return }
$hcSaved = Get-Content -LiteralPath $hcPidPath -Raw | ConvertFrom-Json
$hcProcess = Get-Process -Id $hcSaved.Id -ErrorAction SilentlyContinue
$hcExpected = Join-Path $hcRoot 'artifacts\lan-update-server\HappyCheats.UpdateServer.exe'
if ($hcProcess -and $hcProcess.Path -eq $hcExpected -and $hcProcess.StartTime.ToUniversalTime().Ticks -eq $hcSaved.StartTicks) {
    Stop-Process -Id $hcProcess.Id
    Write-Host 'LAN update server stopped.'
} else { Write-Host 'The saved LAN server is no longer running.' }
Remove-Item -LiteralPath $hcPidPath
