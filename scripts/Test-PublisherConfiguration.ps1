[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PublisherConfiguration.Common.ps1')
$hcTestRoot = Join-Path ([IO.Path]::GetTempPath()) ('HappyCheats-PublisherConfig-Tests-' + [Guid]::NewGuid().ToString('N'))
$hcRoot = Join-Path $hcTestRoot 'checkout'
$hcLocal = Join-Path $hcTestRoot 'publisher'
$hcPrivate = Join-Path $hcTestRoot 'source-private.pem'
$hcPublic = Join-Path $hcTestRoot 'source-public.pem'
$hcOtherPrivate = Join-Path $hcTestRoot 'other-private.pem'
$hcOtherPublic = Join-Path $hcTestRoot 'other-public.pem'
$hcKey = [Security.Cryptography.RSA]::Create(2048)
$hcOtherKey = [Security.Cryptography.RSA]::Create(2048)
$hcCount = 0
function Assert-HcConfigTest([bool]$Value, [string]$Message) {
    if (!$Value) { throw "Publisher configuration regression failed: $Message" }
    $script:hcCount++
}
function Assert-HcConfigRejected([scriptblock]$Action, [string]$Message) {
    $hcRejected = $false
    try { $null = & $Action } catch { $hcRejected = $true }
    Assert-HcConfigTest $hcRejected $Message
}
function New-HcConfigFeed([string]$Repository = 'flazedude/happycheats') {
    $hcManifest = @{ schemaVersion=1; version='0.3.49'; channel='stable'; packageSize=123; sha256=('a'*64); packageUrl="https://github.com/$Repository/releases/download/v0.3.49/HappyCheats-0.3.49-win-x64.zip" }
    $hcBytes = [Text.Encoding]::UTF8.GetBytes(($hcManifest | ConvertTo-Json -Compress))
    $hcSignature = $hcKey.SignData($hcBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    return (@{ payload=[Convert]::ToBase64String($hcBytes); signature=[Convert]::ToBase64String($hcSignature) } | ConvertTo-Json)
}
try {
    New-Item -ItemType Directory -Path (Join-Path $hcRoot 'artifacts/lan-updates') -Force | Out-Null
    [IO.File]::WriteAllText($hcPrivate, $hcKey.ExportPkcs8PrivateKeyPem())
    [IO.File]::WriteAllText($hcPublic, $hcKey.ExportSubjectPublicKeyInfoPem())
    [IO.File]::WriteAllText($hcOtherPrivate, $hcOtherKey.ExportPkcs8PrivateKeyPem())
    [IO.File]::WriteAllText($hcOtherPublic, $hcOtherKey.ExportSubjectPublicKeyInfoPem())
    $hcLegacyPath = Join-Path $hcRoot 'artifacts/lan-updates/server.json'
    $hcLegacy = @{ PrivateKeyPath=$hcPrivate; PublicKeyPath=$hcPublic; BaseUrl='http://192.168.2.76:17845/happycheats'; PublicDirectory=(Join-Path $hcRoot 'artifacts/lan-updates/public') }
    $hcLegacy | ConvertTo-Json | Set-Content -LiteralPath $hcLegacyPath -Encoding utf8NoBOM
    $hcConfig = Get-HcPublisherConfiguration -Root $hcRoot -PublisherDirectory $hcLocal
    Assert-HcConfigTest ($hcConfig.PrivateKeyPath -ceq $hcPrivate) 'existing PC retains its established legacy key'
    Assert-HcConfigRejected { Get-HcPublisherConfiguration -Root $hcRoot -PublisherDirectory $hcLocal -Repository 'someone/else' } 'legacy config cannot be silently reused for another repository'
    Assert-HcConfigRejected { Get-HcSigningPublicIdentity -PrivateKeyPath $hcPrivate -PublicKeyPath $hcOtherPublic } 'mismatched key pair rejected'
    Assert-HcConfigRejected { Get-HcSigningPublicIdentity -PrivateKeyPath $hcPublic } 'public-only key cannot initialize signing'
    Assert-HcConfigRejected { Assert-HcPrivateKeyLocation -Path (Join-Path $hcRoot 'bad-private.pem') -Root $hcRoot } 'private key in checkout rejected'
    Assert-HcConfigRejected { Assert-HcPrivateKeyLocation -Path (Join-Path $hcTestRoot 'served/bad-private.pem') -Root $hcRoot -PublicDirectory (Join-Path $hcTestRoot 'served') } 'private key in served directory rejected'
    $hcFeed = New-HcConfigFeed
    Assert-HcConfigTest ((Assert-HcPublishedFeedKey -EnvelopeJson $hcFeed -Key $hcKey -Repository 'flazedude/happycheats') -ceq '0.3.49') 'existing private key verifies published identity'
    Assert-HcConfigRejected { Assert-HcPublishedFeedKey -EnvelopeJson $hcFeed -Key $hcOtherKey -Repository 'flazedude/happycheats' } 'wrong key cannot verify signed release'
    Assert-HcConfigRejected { Assert-HcPublishedFeedKey -EnvelopeJson (New-HcConfigFeed 'someone/else') -Key $hcKey -Repository 'flazedude/happycheats' } 'signed wrong-repository package rejected'
    $hcWrongDirectory = Join-Path $hcTestRoot 'wrong-publisher'
    Assert-HcConfigRejected { Initialize-HcPublisherConfiguration -Root $hcRoot -Repository 'flazedude/happycheats' -PrivateKeyPath $hcOtherPrivate -EnvelopeJson $hcFeed -PublisherDirectory $hcWrongDirectory } 'wrong key fails before installation'
    Assert-HcConfigTest (!(Test-Path -LiteralPath $hcWrongDirectory)) 'failed trust verification creates no config or key copies'
    $hcOriginalHash = (Get-FileHash -LiteralPath $hcPrivate).Hash
    $hcInstalled = Initialize-HcPublisherConfiguration -Root $hcRoot -Repository 'flazedude/happycheats' -PrivateKeyPath $hcPrivate -EnvelopeJson $hcFeed -PublisherDirectory $hcLocal
    $hcConfig = Get-HcPublisherConfiguration -Root $hcRoot -PublisherDirectory $hcLocal
    Assert-HcConfigTest ($hcInstalled.VerifiedVersion -ceq '0.3.49' -and !$hcInstalled.Reused -and $hcConfig.PrivateKeyPath -cne $hcPrivate) 'initialization saves machine-local key paths after verification'
    Assert-HcConfigTest ((Get-FileHash -LiteralPath $hcConfig.PrivateKeyPath).Hash -ceq $hcOriginalHash -and (Get-FileHash -LiteralPath $hcPrivate).Hash -ceq $hcOriginalHash) 'copied key matches and original remains untouched'
    $hcLocalJson = Get-Content -LiteralPath $hcInstalled.ConfigurationPath -Raw
    Assert-HcConfigTest (!$hcLocalJson.Contains('BEGIN ') -and !$hcLocalJson.Contains('PRIVATE KEY')) 'configuration stores paths, never key material'
    $hcRepeated = Initialize-HcPublisherConfiguration -Root $hcRoot -Repository 'flazedude/happycheats' -PrivateKeyPath $hcPrivate -EnvelopeJson $hcFeed -PublisherDirectory $hcLocal
    Assert-HcConfigTest ($hcRepeated.Reused -and (Get-Content -LiteralPath $hcInstalled.ConfigurationPath -Raw) -ceq $hcLocalJson) 'same-key initialization is idempotent'
    Assert-HcConfigRejected { Get-HcPublisherConfiguration -Root $hcRoot -PublisherDirectory $hcLocal -Repository 'someone/else' } 'preferred configuration is bound to repository'
    $hcLegacy.PrivateKeyPath = Join-Path $hcTestRoot 'missing-private.pem'
    $hcLegacy | ConvertTo-Json | Set-Content -LiteralPath $hcLegacyPath -Encoding utf8NoBOM
    Assert-HcConfigTest ((Get-HcPublisherConfiguration -Root $hcRoot -PublisherDirectory $hcLocal).PrivateKeyPath -ceq $hcConfig.PrivateKeyPath) 'preferred config does not depend on obsolete LAN key path'
    $hcNewCheckout = Join-Path $hcTestRoot 'second-checkout'
    New-Item -ItemType Directory -Path $hcNewCheckout | Out-Null
    Assert-HcConfigTest ((Get-HcPublisherConfiguration -Root $hcNewCheckout -PublisherDirectory $hcLocal).Repository -ceq 'flazedude/happycheats') 'new checkout publishes with no LAN configuration'
    Assert-HcConfigRejected { Get-HcPublisherConfiguration -Root $hcNewCheckout -PublisherDirectory $hcLocal -RequireLan } 'explicit legacy publication still requires LAN configuration'
    $hcConflictDirectory = Join-Path $hcTestRoot 'conflict-publisher'
    $hcConflictKeyPath = Join-Path $hcConflictDirectory 'Signing/flazedude_happycheats/release-private.pem'
    New-Item -ItemType Directory -Path (Split-Path $hcConflictKeyPath) -Force | Out-Null
    Copy-Item -LiteralPath $hcOtherPrivate -Destination $hcConflictKeyPath
    $hcConflictHash = (Get-FileHash -LiteralPath $hcConflictKeyPath).Hash
    Assert-HcConfigRejected { Initialize-HcPublisherConfiguration -Root $hcNewCheckout -Repository 'flazedude/happycheats' -PrivateKeyPath $hcPrivate -EnvelopeJson $hcFeed -PublisherDirectory $hcConflictDirectory } 'conflicting existing private key cannot be overwritten'
    Assert-HcConfigTest ((Get-FileHash -LiteralPath $hcConflictKeyPath).Hash -ceq $hcConflictHash -and !(Test-Path -LiteralPath (Join-Path $hcConflictDirectory 'publisher.json'))) 'conflicting destination preserved without installing config'
    Write-Host "Publisher configuration offline checks passed: $hcCount. No live keys, credentials, network or release publishing used."
} finally {
    $hcKey.Dispose(); $hcOtherKey.Dispose()
    $hcResolved = [IO.Path]::GetFullPath($hcTestRoot)
    $hcTempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if ($hcResolved.StartsWith($hcTempPrefix, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($hcResolved).StartsWith('HappyCheats-PublisherConfig-Tests-') -and (Test-Path -LiteralPath $hcResolved)) { Remove-Item -LiteralPath $hcResolved -Recurse -Force }
}
