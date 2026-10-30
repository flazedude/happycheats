[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LanUpdateConfiguration.Common.ps1')
$hcTestRoot = Join-Path ([IO.Path]::GetTempPath()) ('HappyCheats-LanConfig-Tests-' + [Guid]::NewGuid().ToString('N'))
$hcCheckout = Join-Path $hcTestRoot 'checkout'
$hcPublisherDirectory = Join-Path $hcTestRoot 'publisher'
$hcKey = [Security.Cryptography.RSA]::Create(2048)
$hcCount = 0
function Assert-HcLanConfig([bool]$Value, [string]$Message) {
    if (!$Value) { throw "LAN configuration regression failed: $Message" }
    $script:hcCount++
}
function Assert-HcLanConfigRejected([scriptblock]$Action, [string]$Message) {
    $hcRejected = $false
    try { $null = & $Action } catch { $hcRejected = $true }
    Assert-HcLanConfig $hcRejected $Message
}
try {
    New-Item -ItemType Directory -Path $hcCheckout -Force | Out-Null
    $hcConfigPath = Join-Path $hcCheckout 'artifacts/lan-updates/server.json'
    Assert-HcLanConfigRejected { Initialize-HcLanUpdateConfiguration -Root $hcCheckout -Address '192.168.2.77' -PublisherDirectory $hcPublisherDirectory } 'missing publisher setup cannot create a new signing identity'
    Assert-HcLanConfig (!(Test-Path -LiteralPath $hcConfigPath) -and !(Test-Path -LiteralPath $hcPublisherDirectory)) 'failed setup creates neither server config nor keys'

    New-Item -ItemType Directory -Path $hcPublisherDirectory | Out-Null
    $hcPrivatePath = Join-Path $hcPublisherDirectory 'existing-private.pem'
    $hcPublicPath = Join-Path $hcPublisherDirectory 'existing-public.pem'
    [IO.File]::WriteAllText($hcPrivatePath, $hcKey.ExportPkcs8PrivateKeyPem())
    [IO.File]::WriteAllText($hcPublicPath, $hcKey.ExportSubjectPublicKeyInfoPem())
    @{Repository='flazedude/happycheats';PrivateKeyPath=$hcPrivatePath;PublicKeyPath=$hcPublicPath} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hcPublisherDirectory 'publisher.json') -Encoding utf8NoBOM
    $hcPrivateHash = (Get-FileHash -LiteralPath $hcPrivatePath).Hash
    $hcPublicHash = (Get-FileHash -LiteralPath $hcPublicPath).Hash
    $hcConfig = Initialize-HcLanUpdateConfiguration -Root $hcCheckout -Address '192.168.2.77' -PublisherDirectory $hcPublisherDirectory
    Assert-HcLanConfig ($hcConfig.PrivateKeyPath -ceq $hcPrivatePath -and $hcConfig.PublicKeyPath -ceq $hcPublicPath) 'new LAN host reuses established publisher key paths'
    Assert-HcLanConfig ($hcConfig.BaseUrl -ceq 'http://192.168.2.77:17845/happycheats' -and $hcConfig.PublicDirectory -ceq (Join-Path $hcCheckout 'artifacts/lan-updates/public')) 'new LAN host gets its own address and public directory'
    Assert-HcLanConfig ((Get-FileHash -LiteralPath $hcPrivatePath).Hash -ceq $hcPrivateHash -and (Get-FileHash -LiteralPath $hcPublicPath).Hash -ceq $hcPublicHash) 'established key files remain byte-identical'
    Assert-HcLanConfig ((Get-ChildItem -LiteralPath $hcPublisherDirectory -File).Count -eq 3 -and !(Get-Content -LiteralPath $hcConfigPath -Raw).Contains('BEGIN ')) 'no replacement keys or private key material are written to server config'

    $hcBefore = [IO.File]::ReadAllBytes($hcConfigPath)
    $hcReused = Initialize-HcLanUpdateConfiguration -Root $hcCheckout -PublisherDirectory (Join-Path $hcTestRoot 'missing-publisher')
    Assert-HcLanConfig ($hcReused.BaseUrl -ceq $hcConfig.BaseUrl -and [Convert]::ToBase64String($hcBefore) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($hcConfigPath))) 'existing server config is reused byte-for-byte without publisher access'
    Assert-HcLanConfigRejected { Initialize-HcLanUpdateConfiguration -Root $hcCheckout -Address '192.168.2.78' -PublisherDirectory $hcPublisherDirectory } 'existing host address cannot change silently'
    Assert-HcLanConfigRejected { Initialize-HcLanUpdateConfiguration -Root $hcCheckout -Port 17846 -PublisherDirectory $hcPublisherDirectory } 'existing host port cannot change silently'
    Assert-HcLanConfig ([Convert]::ToBase64String($hcBefore) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($hcConfigPath))) 'rejected address changes preserve saved server bytes'

    $hcFreshCheckout = Join-Path $hcTestRoot 'fresh-checkout'
    Assert-HcLanConfigRejected { Initialize-HcLanUpdateConfiguration -Root $hcFreshCheckout -Address '8.8.8.8' -PublisherDirectory $hcPublisherDirectory } 'public interface is rejected'
    Assert-HcLanConfigRejected { Initialize-HcLanUpdateConfiguration -Root $hcFreshCheckout -Address '192.168.2.78' -Port 80 -PublisherDirectory $hcPublisherDirectory } 'privileged port is rejected'
    Assert-HcLanConfig (!(Test-Path -LiteralPath (Join-Path $hcFreshCheckout 'artifacts/lan-updates/server.json'))) 'invalid host settings are never persisted'
    Write-Host "LAN configuration offline checks passed: $hcCount. Temporary fixture keys only; no server, network or firewall changes."
} finally {
    $hcKey.Dispose()
    $hcResolved = [IO.Path]::GetFullPath($hcTestRoot)
    $hcTempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if ($hcResolved.StartsWith($hcTempPrefix, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($hcResolved).StartsWith('HappyCheats-LanConfig-Tests-') -and (Test-Path -LiteralPath $hcResolved)) { Remove-Item -LiteralPath $hcResolved -Recurse -Force }
}
