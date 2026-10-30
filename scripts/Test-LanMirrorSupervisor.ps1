[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LanMirrorSupervisor.Common.ps1')
$hcTestRoot = Join-Path ([IO.Path]::GetTempPath()) ('HappyCheats-MirrorSupervisor-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $hcTestRoot
$hcConfig = [pscustomobject]@{MirrorEnabled=$true; SyncPowerShellPath=(Get-Process -Id $PID).Path; MirrorIntervalSeconds=60}
$hcProcess = $null; $hcCount = 0
$hcTestKey=[Security.Cryptography.RSA]::Create(2048)
function Assert-HcMirrorSupervisor($Condition,[string]$Name) { if (!$Condition) { throw "Mirror supervisor check failed: $Name" }; $script:hcCount++ }
try {
    $hcPublicSource=Join-Path $hcTestRoot 'publisher-public.pem'
    [IO.File]::WriteAllText($hcPublicSource,$hcTestKey.ExportSubjectPublicKeyInfoPem())
    $hcMissingPrivate=Join-Path $hcTestRoot 'private-must-not-be-read.pem'
    @{Address='192.168.2.59';PublicKeyPath=$hcPublicSource;PrivateKeyPath=$hcMissingPrivate;PublicDirectory=(Join-Path $hcTestRoot 'served')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hcTestRoot 'server.json')
    Enable-HcLanMirrorConfiguration -ConfigPath (Join-Path $hcTestRoot 'server.json') -PowerShellPath $hcConfig.SyncPowerShellPath
    $hcSavedConfig=Get-Content -LiteralPath (Join-Path $hcTestRoot 'server.json') -Raw | ConvertFrom-Json
    Assert-HcMirrorSupervisor ($hcSavedConfig.MirrorEnabled -and $hcSavedConfig.MirrorIntervalSeconds -eq 300 -and $hcSavedConfig.Address -ceq '192.168.2.59' -and $hcSavedConfig.PublicKeyPath -ceq $hcPublicSource -and $hcSavedConfig.PrivateKeyPath -ceq $hcMissingPrivate) 'atomic setup preserves host and original key paths without requiring the private key'
    Assert-HcMirrorSupervisor (@(Get-ChildItem -LiteralPath $hcTestRoot -Filter 'server.json.previous-*').Count -eq 1) 'atomic setup retains previous configuration'
    $hcCopyPem=[IO.File]::ReadAllText($hcSavedConfig.MirrorPublicKeyPath)
    Assert-HcMirrorSupervisor ($hcSavedConfig.MirrorPublicKeyPath -ceq (Join-Path $hcTestRoot 'mirror-public.pem') -and !$hcCopyPem.Contains('PRIVATE KEY')) 'task-readable copy contains public material outside the served directory'
    $hcCopyKey=[Security.Cryptography.RSA]::Create()
    try {
        $hcCopyKey.ImportFromPem($hcCopyPem)
        $hcExpectedIdentity=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($hcTestKey.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
        Assert-HcMirrorSupervisor ([Convert]::ToBase64String($hcCopyKey.ExportSubjectPublicKeyInfo()) -ceq [Convert]::ToBase64String($hcTestKey.ExportSubjectPublicKeyInfo()) -and $hcSavedConfig.MirrorPublicKeySha256 -ceq $hcExpectedIdentity) 'public copy retains and binds the exact established RSA identity'
    } finally { $hcCopyKey.Dispose() }
    $hcCopyHash=(Get-FileHash -LiteralPath $hcSavedConfig.MirrorPublicKeyPath).Hash
    Enable-HcLanMirrorConfiguration -ConfigPath (Join-Path $hcTestRoot 'server.json') -PowerShellPath $hcConfig.SyncPowerShellPath
    Assert-HcMirrorSupervisor ((Get-FileHash -LiteralPath $hcSavedConfig.MirrorPublicKeyPath).Hash -ceq $hcCopyHash) 'repeated setup preserves the same public copy'
    $hcWrongKey=[Security.Cryptography.RSA]::Create(2048)
    try { [IO.File]::WriteAllText($hcSavedConfig.MirrorPublicKeyPath,$hcWrongKey.ExportSubjectPublicKeyInfoPem()) } finally { $hcWrongKey.Dispose() }
    $hcConflictHash=(Get-FileHash -LiteralPath $hcSavedConfig.MirrorPublicKeyPath).Hash
    $hcConfigHash=(Get-FileHash -LiteralPath (Join-Path $hcTestRoot 'server.json')).Hash
    $hcRejected=$false
    try { Enable-HcLanMirrorConfiguration -ConfigPath (Join-Path $hcTestRoot 'server.json') -PowerShellPath $hcConfig.SyncPowerShellPath } catch { $hcRejected=$true }
    Assert-HcMirrorSupervisor ($hcRejected -and (Get-FileHash -LiteralPath $hcSavedConfig.MirrorPublicKeyPath).Hash -ceq $hcConflictHash -and (Get-FileHash -LiteralPath (Join-Path $hcTestRoot 'server.json')).Hash -ceq $hcConfigHash) 'conflicting existing public copy and saved configuration remain untouched'
    [IO.File]::WriteAllText($hcSavedConfig.MirrorPublicKeyPath,$hcCopyPem)
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
    $hcProcess.Dispose(); $hcProcess=$null
    'param([string]$ConfigPath); exit 7' | Set-Content -LiteralPath $hcFixture
    $hcProcess=Start-HcLanMirrorProcess $hcConfig $hcTestRoot $hcFixture
    $hcProcess.WaitForExit()
    Assert-HcMirrorSupervisor ($hcProcess.ExitCode -eq 7) 'cached process handle preserves the exact short-lived child exit code'
    Stop-HcLanMirrorProcess $hcConfig $hcTestRoot
    $hcConfig.SyncPowerShellPath='C:\missing-mirror-runtime\pwsh.exe'
    $hcRejected=$false
    try { $null=Start-HcLanMirrorProcess $hcConfig $hcTestRoot $hcFixture } catch { $hcRejected=$true }
    Assert-HcMirrorSupervisor $hcRejected 'missing runtime does not launch another executable'
    Write-Host "LAN mirror supervisor checks passed: $hcCount. Temporary child process only; no server, network, firewall or key changes."
} finally {
    $hcTestKey.Dispose()
    if ($hcProcess) { if (!$hcProcess.HasExited) { $hcProcess.Kill() }; $hcProcess.Dispose() }
    $hcResolved=[IO.Path]::GetFullPath($hcTestRoot)
    $hcPrefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if ($hcResolved.StartsWith($hcPrefix,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($hcResolved).StartsWith('HappyCheats-MirrorSupervisor-')) { Remove-Item -LiteralPath $hcResolved -Recurse -Force }
}
