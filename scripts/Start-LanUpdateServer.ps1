[CmdletBinding()]
param([string]$Address, [ValidateRange(0,65535)][int]$Port = 0, [switch]$OpenFirewall)
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcStateRoot = Join-Path $hcRoot 'artifacts\lan-updates'
$hcConfigPath = Join-Path $hcStateRoot 'server.json'
New-Item -ItemType Directory -Force -Path $hcStateRoot | Out-Null
if (Test-Path -LiteralPath $hcConfigPath) {
    $hcConfig = Get-Content -LiteralPath $hcConfigPath -Raw | ConvertFrom-Json
    if (($Address -and $Address -ne $hcConfig.Address) -or ($Port -and $Port -ne $hcConfig.Port)) {
        throw 'The saved LAN address differs. Keep its DHCP reservation, or follow docs/lan-updates.md to migrate the feed.'
    }
    $Address = $hcConfig.Address
    $Port = [int]$hcConfig.Port
} else {
    if (!$Address) {
        $hcPhysical = @(Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | Select-Object -ExpandProperty ifIndex)
        $hcCandidates = @(Get-NetIPConfiguration | Where-Object { $_.InterfaceIndex -in $hcPhysical -and $_.IPv4DefaultGateway } | ForEach-Object { $_.IPv4Address.IPAddress })
        if ($hcCandidates.Count -ne 1) { throw 'Choose this PC''s LAN IPv4 address with -Address (multiple or no physical network connections found).' }
        $Address = $hcCandidates[0]
    }
    if (!$Port) { $Port = 17845 }
    $hcIp = [Net.IPAddress]::Parse($Address)
    $hcBytes = $hcIp.GetAddressBytes()
    if ($hcBytes.Length -ne 4 -or !($hcBytes[0] -eq 10 -or ($hcBytes[0] -eq 172 -and $hcBytes[1] -ge 16 -and $hcBytes[1] -le 31) -or ($hcBytes[0] -eq 192 -and $hcBytes[1] -eq 168))) { throw 'Choose a private LAN IPv4 address.' }
    if ($Port -lt 1024) { throw 'Choose a port between 1024 and 65535.' }
    if (!(Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq $Address)) { throw 'This PC does not own that IPv4 address.' }
    $hcKeys = Join-Path $env:LOCALAPPDATA 'HappyCheatsPublisher\LAN'
    $hcPrivate = Join-Path $hcKeys 'release-private.pem'
    $hcPublic = Join-Path $hcKeys 'release-public.pem'
    if (!(Test-Path -LiteralPath $hcPrivate) -and !(Test-Path -LiteralPath $hcPublic)) {
        & dotnet run --project (Join-Path $hcRoot 'tools\HappyCheats.Release') -c Release -- keygen --private-key $hcPrivate --public-key $hcPublic
        if ($LASTEXITCODE -ne 0) { throw 'LAN signing key generation failed.' }
    }
    if (!(Test-Path -LiteralPath $hcPrivate) -or !(Test-Path -LiteralPath $hcPublic)) { throw 'The LAN signing key pair is incomplete. Restore it; do not replace an existing client signing key.' }
    $hcConfig = [pscustomobject]@{ Address = $Address; Port = $Port; BaseUrl = "http://${Address}:$Port/happycheats"; PublicDirectory = (Join-Path $hcStateRoot 'public'); PrivateKeyPath = $hcPrivate; PublicKeyPath = $hcPublic }
    $hcConfig | ConvertTo-Json | Set-Content -LiteralPath $hcConfigPath -Encoding UTF8
}
$hcServerDirectory = Join-Path $hcRoot 'artifacts\lan-update-server'
$hcServerExe = Join-Path $hcServerDirectory 'HappyCheats.UpdateServer.exe'
$hcPidPath = Join-Path $hcStateRoot 'process.json'
$hcExisting = $null
if (Test-Path -LiteralPath $hcPidPath) {
    $hcSaved = Get-Content -LiteralPath $hcPidPath -Raw | ConvertFrom-Json
    $hcExisting = Get-Process -Id $hcSaved.Id -ErrorAction SilentlyContinue
    if ($hcExisting -and ($hcExisting.Path -ne $hcServerExe -or $hcExisting.StartTime.ToUniversalTime().Ticks -ne $hcSaved.StartTicks)) { $hcExisting = $null }
}
if (!$hcExisting) {
    $hcStartupTask = Get-ScheduledTask -TaskName 'HappyCheats LAN Update Server' -ErrorAction SilentlyContinue
    if ($hcStartupTask) {
        if ($hcStartupTask.State -eq 'Disabled') { throw 'The LAN startup task is disabled. Enable it in Task Scheduler or remove it to use manual startup.' }
        if ($hcStartupTask.State -ne 'Running') { Start-ScheduledTask -TaskName $hcStartupTask.TaskName }
        $hcDeadline = [DateTime]::UtcNow.AddSeconds(45)
        do {
            Start-Sleep -Milliseconds 250
            if (Test-Path -LiteralPath $hcPidPath) {
                $hcSaved = Get-Content -LiteralPath $hcPidPath -Raw | ConvertFrom-Json
                $hcCandidate = Get-Process -Id $hcSaved.Id -ErrorAction SilentlyContinue
                if ($hcCandidate -and $hcCandidate.Path -eq $hcServerExe -and $hcCandidate.StartTime.ToUniversalTime().Ticks -eq $hcSaved.StartTicks) { $hcExisting = $hcCandidate }
            }
        } while (!$hcExisting -and [DateTime]::UtcNow -lt $hcDeadline)
        if (!$hcExisting) { throw "Startup task is still waiting or failed. Check Task Scheduler and $hcStateRoot\startup-error.log" }
    } else {
    & dotnet publish (Join-Path $hcRoot 'tools\HappyCheats.UpdateServer') -c Release -r win-x64 --self-contained true -o $hcServerDirectory --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'LAN server build failed.' }
    New-Item -ItemType Directory -Force -Path $hcConfig.PublicDirectory | Out-Null
    $hcServerArgs = @('--address', $Address, '--port', $Port.ToString(), '--release-root', ('"' + $hcConfig.PublicDirectory + '"'))
    $hcExisting = Start-Process -FilePath $hcServerExe -ArgumentList $hcServerArgs -WorkingDirectory $hcServerDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $hcStateRoot 'server.log') -RedirectStandardError (Join-Path $hcStateRoot 'server-error.log')
    @{ Id = $hcExisting.Id; StartTicks = $hcExisting.StartTime.ToUniversalTime().Ticks } | ConvertTo-Json | Set-Content -LiteralPath $hcPidPath
    }
}
$hcReady = $false
for ($hcAttempt = 0; $hcAttempt -lt 30; $hcAttempt++) {
    if ($hcExisting.HasExited) { throw "LAN server exited. Check $hcStateRoot\server-error.log" }
    try {
        $hcResponse = Invoke-WebRequest -Uri "http://${Address}:$Port/health" -NoProxy -TimeoutSec 2
        if ($hcResponse.Content -eq 'HappyCheats LAN update server') { $hcReady = $true; break }
    } catch { Start-Sleep -Milliseconds 200 }
}
if (!$hcReady) { throw "LAN server did not respond. Check $hcStateRoot\server.log" }
if ($OpenFirewall) {
    $hcIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $hcPrincipal = [Security.Principal.WindowsPrincipal]::new($hcIdentity)
    if (!$hcPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Warning 'To allow other PCs through Windows Firewall, run this script once in an administrator PowerShell with -OpenFirewall.'
    } elseif (!(Get-NetFirewallRule -Name 'HappyCheats-LAN-Updates' -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -Name 'HappyCheats-LAN-Updates' -DisplayName 'HappyCheats LAN updates' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -LocalAddress $Address -RemoteAddress LocalSubnet -Profile Any -Program $hcServerExe | Out-Null
    }
}
Write-Host "LAN server running (PID $($hcExisting.Id)): $($hcConfig.BaseUrl)"
Write-Host "Client download after publishing: $($hcConfig.BaseUrl)/HappyCheats.exe"
Write-Host "Feed after publishing: $($hcConfig.BaseUrl)/latest.json"
