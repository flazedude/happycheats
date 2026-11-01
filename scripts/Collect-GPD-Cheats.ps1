#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryPath = 'D:\Coding\happycheats')
$ErrorActionPreference = 'Stop'
try {
    if (!(Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq '192.168.2.59')) {
        throw 'Run this log collector on GPD-PC (192.168.2.59).'
    }
    $hcState = Join-Path $RepositoryPath 'artifacts/lan-updates'
    $hcConfig = Get-Content -LiteralPath (Join-Path $hcState 'server.json') -Raw | ConvertFrom-Json
    if ($hcConfig.BaseUrl -cne 'http://192.168.2.59:17845/happycheats') { throw 'This is not GPD-PC host configuration.' }
    $hcReport = [ordered]@{ computer=$env:COMPUTERNAME; collectedAt=[DateTimeOffset]::UtcNow.ToString('o');
        mirrorEnabled=$hcConfig.MirrorEnabled; intervalSeconds=$hcConfig.MirrorIntervalSeconds;
        taskState=[string](Get-ScheduledTask -TaskName 'HappyCheats LAN Update Server').State; logs=[ordered]@{} }
    $hcDeployment = Join-Path $hcState 'deployed-components.json'
    if (Test-Path -LiteralPath $hcDeployment) { $hcReport.deployment = Get-Content -LiteralPath $hcDeployment -Raw | ConvertFrom-Json }
    foreach ($hcName in @('mirror-supervisor.log','mirror.log','mirror-error.log','startup-error.log')) {
        $hcLog = Join-Path $hcState $hcName
        if (Test-Path -LiteralPath $hcLog) { $hcReport.logs[$hcName] = @(Get-Content -LiteralPath $hcLog -Tail 80) }
    }
    $hcSetup = Join-Path $env:LOCALAPPDATA 'HappyCheatsPublisher/Setup'
    $hcResult = Get-ChildItem -Path (Join-Path $hcSetup 'GPD-*/result.json') -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($hcResult) {
        $hcReport.setupResult = Get-Content -LiteralPath $hcResult.FullName -Raw | ConvertFrom-Json
        if (Test-Path -LiteralPath ($hcResult.FullName+'.log')) {
            $hcReport.logs['setup.log'] = @(Get-Content -LiteralPath ($hcResult.FullName+'.log') -Tail 100)
        }
    }
    $hcDestination = Join-Path $PSScriptRoot ('GPD-cheat-diagnostics-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.json')
    $hcReport | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $hcDestination
    Write-Host "Logs saved: $hcDestination" -ForegroundColor Green
    Write-Host 'No deployment, server, firewall, configuration or source files were changed.'
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
