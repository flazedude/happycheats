[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LanMirrorSupervisor.Common.ps1')
$hcTestRoot = Join-Path ([IO.Path]::GetTempPath()) ('HappyCheats-MirrorSupervisor-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $hcTestRoot
$hcConfig = [pscustomobject]@{MirrorEnabled=$true; SyncPowerShellPath=(Get-Process -Id $PID).Path; MirrorIntervalSeconds=60}
$hcProcess = $null; $hcCount = 0
function Assert-HcMirrorSupervisor($Condition,[string]$Name) { if (!$Condition) { throw "Mirror supervisor check failed: $Name" }; $script:hcCount++ }
try {
    '{"Address":"192.168.2.59","PublicKeyPath":"public-only-fixture.pem"}' | Set-Content -LiteralPath (Join-Path $hcTestRoot 'server.json')
    Enable-HcLanMirrorConfiguration -ConfigPath (Join-Path $hcTestRoot 'server.json') -PowerShellPath $hcConfig.SyncPowerShellPath
    $hcSavedConfig=Get-Content -LiteralPath (Join-Path $hcTestRoot 'server.json') -Raw | ConvertFrom-Json
    Assert-HcMirrorSupervisor ($hcSavedConfig.MirrorEnabled -and $hcSavedConfig.MirrorIntervalSeconds -eq 300 -and $hcSavedConfig.Address -ceq '192.168.2.59' -and $hcSavedConfig.PublicKeyPath -ceq 'public-only-fixture.pem') 'atomic setup preserves host and public key configuration'
    Assert-HcMirrorSupervisor (@(Get-ChildItem -LiteralPath $hcTestRoot -Filter 'server.json.previous-*').Count -eq 1) 'atomic setup retains previous configuration'
    $hcFixture = Join-Path $hcTestRoot 'Sync-Fixture.ps1'
    'param([string]$ConfigPath); if (!(Test-Path -LiteralPath $ConfigPath)) { exit 3 }; Write-Output ''fixture started''; Start-Sleep -Seconds 20' | Set-Content -LiteralPath $hcFixture
    Assert-HcMirrorSupervisor ((Get-HcLanMirrorIntervalSeconds $hcConfig) -eq 60) 'configured interval'
    $hcConfig.MirrorIntervalSeconds=1
    Assert-HcMirrorSupervisor ((Get-HcLanMirrorIntervalSeconds $hcConfig) -eq 300) 'unsafe interval falls back to five minutes'
    $hcConfig.MirrorEnabled=$false
    Assert-HcMirrorSupervisor ($null -eq (Start-HcLanMirrorProcess $hcConfig $hcTestRoot $hcFixture)) 'disabled mirror launches nothing'
    $hcConfig.MirrorEnabled=$true
    $hcProcess=Start-HcLanMirrorProcess $hcConfig $hcTestRoot $hcFixture
    $hcRecord=Get-Content -LiteralPath (Join-Path $hcTestRoot 'mirror-process.json') -Raw | ConvertFrom-Json
    Assert-HcMirrorSupervisor ($hcRecord.Id -eq $hcProcess.Id -and $hcRecord.Path -eq $hcConfig.SyncPowerShellPath) 'owned process identity recorded'
    $hcReattached=Start-HcLanMirrorProcess $hcConfig $hcTestRoot $hcFixture
    Assert-HcMirrorSupervisor ($hcReattached.Id -eq $hcProcess.Id) 'restarted supervisor reattaches to a surviving owned helper'
    $hcReattached.Dispose()
    $hcOriginalTicks=$hcRecord.StartTicks
    $hcRecord.StartTicks=0
    $hcRecord | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hcTestRoot 'mirror-process.json')
    Stop-HcLanMirrorProcess $hcConfig $hcTestRoot
    Assert-HcMirrorSupervisor (!$hcProcess.HasExited) 'stale identity cannot stop another process'
    $hcRecord.StartTicks=$hcOriginalTicks
    $hcRecord | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hcTestRoot 'mirror-process.json')
    Stop-HcLanMirrorProcess $hcConfig $hcTestRoot
    Assert-HcMirrorSupervisor ($hcProcess.WaitForExit(5000)) 'owned helper stops promptly'
    Assert-HcMirrorSupervisor (!(Test-Path -LiteralPath (Join-Path $hcTestRoot 'mirror-process.json'))) 'owned stop clears process record'
    $hcConfig.SyncPowerShellPath='C:\missing-mirror-runtime\pwsh.exe'
    $hcRejected=$false
    try { $null=Start-HcLanMirrorProcess $hcConfig $hcTestRoot $hcFixture } catch { $hcRejected=$true }
    Assert-HcMirrorSupervisor $hcRejected 'missing runtime does not launch another executable'
    Write-Host "LAN mirror supervisor checks passed: $hcCount. Temporary child process only; no server, network, firewall or key changes."
} finally {
    if ($hcProcess) { if (!$hcProcess.HasExited) { $hcProcess.Kill() }; $hcProcess.Dispose() }
    $hcResolved=[IO.Path]::GetFullPath($hcTestRoot)
    $hcPrefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if ($hcResolved.StartsWith($hcPrefix,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($hcResolved).StartsWith('HappyCheats-MirrorSupervisor-')) { Remove-Item -LiteralPath $hcResolved -Recurse -Force }
}
