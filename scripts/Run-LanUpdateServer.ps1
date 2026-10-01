# Foreground task entry point. Compatible with the Windows-bundled PowerShell 5.1.
# Builds, signing and firewall changes deliberately stay in the interactive setup scripts.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcStateRoot = Join-Path $hcRoot 'artifacts\lan-updates'
$hcServerDirectory = Join-Path $hcRoot 'artifacts\lan-update-server'
$hcServerExe = Join-Path $hcServerDirectory 'HappyCheats.UpdateServer.exe'
$hcPidPath = Join-Path $hcStateRoot 'process.json'
$hcLock = $null
try {
    $hcConfig = Get-Content -LiteralPath (Join-Path $hcStateRoot 'server.json') -Raw | ConvertFrom-Json
    if (!(Test-Path -LiteralPath $hcServerExe -PathType Leaf)) { throw 'The published LAN server is missing. Run Start-LanUpdateServer.ps1 before installing startup.' }
    $hcLock = [IO.File]::Open((Join-Path $hcStateRoot 'server-instance.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::Write, [IO.FileShare]::None)
    while ($true) {
        try {
            # Wi-Fi and DHCP can become available after the boot trigger fires.
            $hcDeadline = [DateTime]::UtcNow.AddMinutes(3)
            while (!(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object IPAddress -eq $hcConfig.Address)) {
                if ([DateTime]::UtcNow -ge $hcDeadline) { throw "The configured LAN address $($hcConfig.Address) is not available yet." }
                Start-Sleep -Seconds 2
            }
            $hcServer = $null
            if (Test-Path -LiteralPath $hcPidPath) {
                $hcSaved = Get-Content -LiteralPath $hcPidPath -Raw | ConvertFrom-Json
                $hcServer = Get-Process -Id $hcSaved.Id -ErrorAction SilentlyContinue
                if ($hcServer -and ($hcServer.Path -ne $hcServerExe -or $hcServer.StartTime.ToUniversalTime().Ticks -ne $hcSaved.StartTicks)) { $hcServer = $null }
            }
            if (!$hcServer) {
                $hcArguments = @('--address', $hcConfig.Address, '--port', ([int]$hcConfig.Port).ToString(), '--release-root', ('"' + $hcConfig.PublicDirectory + '"'))
                $hcServer = Start-Process -FilePath $hcServerExe -ArgumentList $hcArguments -WorkingDirectory $hcServerDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $hcStateRoot 'server.log') -RedirectStandardError (Join-Path $hcStateRoot 'server-error.log')
                @{ Id = $hcServer.Id; StartTicks = $hcServer.StartTime.ToUniversalTime().Ticks } | ConvertTo-Json | Set-Content -LiteralPath $hcPidPath
            }
            $hcServer.WaitForExit()
            throw 'The LAN server process exited.'
        } catch {
            try { ('{0:u} {1} Retrying in 15 seconds.' -f [DateTime]::UtcNow, $_.Exception.Message) | Add-Content -LiteralPath (Join-Path $hcStateRoot 'startup-error.log') } catch { }
        }
        # Supervise directly: Task Scheduler does not consistently retry tasks started on demand.
        Start-Sleep -Seconds 15
    }
} catch {
    try { ('{0:u} {1}' -f [DateTime]::UtcNow, $_.Exception.Message) | Add-Content -LiteralPath (Join-Path $hcStateRoot 'startup-error.log') } catch { }
    exit 1
} finally { if ($hcLock) { $hcLock.Dispose() } }
