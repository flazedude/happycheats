# Compatible with the Windows PowerShell 5.1 boot supervisor. Crypto/network work
# runs in the configured PowerShell 7 helper, using only the public signing key.
function Start-HcLanMirrorProcess {
    param($Config, [string]$StateRoot, [string]$SyncScriptPath)
    if (!$Config.MirrorEnabled) { return $null }
    $hcPowerShell = [string]$Config.SyncPowerShellPath
    if (![IO.Path]::IsPathRooted($hcPowerShell) -or !(Test-Path -LiteralPath $hcPowerShell -PathType Leaf) -or
        [IO.Path]::GetFileName($hcPowerShell) -ine 'pwsh.exe' -or
        [Diagnostics.FileVersionInfo]::GetVersionInfo($hcPowerShell).FileMajorPart -lt 7) {
        throw 'The configured PowerShell 7 mirror runtime is unavailable. Repair mirror startup locally.'
    }
    if (!(Test-Path -LiteralPath $SyncScriptPath -PathType Leaf)) { throw 'The LAN mirror sync helper is missing.' }
    $hcRecordPath = Join-Path $StateRoot 'mirror-process.json'
    if (Test-Path -LiteralPath $hcRecordPath -PathType Leaf) {
        $hcSaved = Get-Content -LiteralPath $hcRecordPath -Raw | ConvertFrom-Json
        $hcExisting = Get-Process -Id $hcSaved.Id -ErrorAction SilentlyContinue
        if ($hcExisting -and $hcExisting.StartTime.ToUniversalTime().Ticks -eq $hcSaved.StartTicks) {
            if ($hcSaved.Path -ieq $hcPowerShell -and $hcExisting.Path -ieq $hcPowerShell) { return $hcExisting }
            throw 'A live mirror record uses a different runtime. Preserve it and stop the recorded helper before changing runtimes.'
        }
    }
    $hcArguments = @('-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',('"' + $SyncScriptPath + '"'),'-ConfigPath',('"' + (Join-Path $StateRoot 'server.json') + '"'))
    $hcProcess = Start-Process -FilePath $hcPowerShell -ArgumentList $hcArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $StateRoot 'mirror.log') -RedirectStandardError (Join-Path $StateRoot 'mirror-error.log')
    @{ Id=$hcProcess.Id; StartTicks=$hcProcess.StartTime.ToUniversalTime().Ticks; Path=$hcPowerShell } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $StateRoot 'mirror-process.json')
    return $hcProcess
}

function Enable-HcLanMirrorConfiguration {
    param([string]$ConfigPath, [string]$PowerShellPath, [ValidateRange(60,86400)][int]$IntervalSeconds = 300)
    if (!(Test-Path -LiteralPath $PowerShellPath -PathType Leaf) -or [IO.Path]::GetFileName($PowerShellPath) -ine 'pwsh.exe' -or
        [Diagnostics.FileVersionInfo]::GetVersionInfo($PowerShellPath).FileMajorPart -lt 7) { throw 'Mirror startup requires an existing PowerShell 7 runtime.' }
    $hcConfig = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    $hcConfig | Add-Member -NotePropertyName MirrorEnabled -NotePropertyValue $true -Force
    $hcConfig | Add-Member -NotePropertyName MirrorIntervalSeconds -NotePropertyValue $IntervalSeconds -Force
    $hcConfig | Add-Member -NotePropertyName SyncPowerShellPath -NotePropertyValue $PowerShellPath -Force
    $hcTemporary = $ConfigPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $hcBackup = $ConfigPath + '.previous-' + [Guid]::NewGuid().ToString('N')
    try {
        [IO.File]::WriteAllText($hcTemporary,($hcConfig | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
        [IO.File]::Replace($hcTemporary,$ConfigPath,$hcBackup)
    } finally { if (Test-Path -LiteralPath $hcTemporary) { Remove-Item -LiteralPath $hcTemporary } }
}

function Stop-HcLanMirrorProcess {
    param($Config, [string]$StateRoot)
    $hcRecordPath = Join-Path $StateRoot 'mirror-process.json'
    if (!(Test-Path -LiteralPath $hcRecordPath -PathType Leaf)) { return }
    $hcSaved = Get-Content -LiteralPath $hcRecordPath -Raw | ConvertFrom-Json
    $hcProcess = Get-Process -Id $hcSaved.Id -ErrorAction SilentlyContinue
    if ($hcProcess -and $hcSaved.Path -ieq $Config.SyncPowerShellPath -and
        $hcProcess.Path -ieq $Config.SyncPowerShellPath -and
        $hcProcess.StartTime.ToUniversalTime().Ticks -eq $hcSaved.StartTicks) {
        Stop-Process -Id $hcProcess.Id
    }
    Remove-Item -LiteralPath $hcRecordPath
}

function Get-HcLanMirrorIntervalSeconds {
    param($Config)
    $hcValue = 0
    if (![int]::TryParse([string]$Config.MirrorIntervalSeconds,[ref]$hcValue) -or $hcValue -lt 60 -or $hcValue -gt 86400) { return 300 }
    return $hcValue
}
