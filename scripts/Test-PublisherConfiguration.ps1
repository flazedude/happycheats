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
function Test-HcPublicationRecovery {
    # Execute the real coordinator in an isolated checkout. Only its external
    # build/source/network boundaries are replaced; no credentials are requested.
    $hcFixture = Join-Path $hcTestRoot 'publication-checkout'
    $hcScripts = Join-Path $hcFixture 'scripts'
    $hcStagePath = Join-Path $hcFixture 'artifacts/releases/1.2.3'
    $hcRetiredPublic = Join-Path $hcFixture 'retired-public'
    foreach ($hcPath in @($hcScripts, $hcRetiredPublic, (Join-Path $hcFixture 'src/HappyCheats.App'))) { New-Item -ItemType Directory -Path $hcPath -Force | Out-Null }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Publish-Update.ps1') -Destination $hcScripts
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LanMirror.Common.ps1') -Destination $hcScripts
    [IO.File]::WriteAllText((Join-Path $hcFixture 'src/HappyCheats.App/HappyCheats.App.csproj'), '<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $hcRetiredPublic 'latest.json'), '{ malformed retired feed')
    [IO.File]::WriteAllText((Join-Path $hcRetiredPublic 'HappyCheats-1.2.3-win-x64.zip'), 'old LAN archive')
    foreach ($hcName in @('notes.txt','private-placeholder','public-placeholder')) { [IO.File]::WriteAllText((Join-Path $hcFixture $hcName), 'fixture') }
    [IO.File]::WriteAllText((Join-Path $hcScripts 'GitHubRelease.Common.ps1'), @'
. (Join-Path $PSScriptRoot 'LanMirror.Common.ps1')
function Get-HcPublisherConfiguration {
    param($Root, $Repository, [switch]$RequireLan)
    return [pscustomobject]@{ PrivateKeyPath=(Join-Path $Root 'private-placeholder'); PublicKeyPath=(Join-Path $Root 'public-placeholder'); PublicDirectory=(Join-Path $Root 'retired-public'); BaseUrl='http://192.168.2.76:17845/happycheats' }
}
function New-HcGitHubClient { return [IO.MemoryStream]::new() }
function git { return ('a'*40) }
function Assert-HcGitHubPreflight { param($Client, $Repository, $Version, $SourceCommit, [switch]$AllowExistingVersion, [switch]$AllowPublishedResume) }
function Assert-HcReleaseSource { param($Root, $Repository, $ExpectedCommit, [switch]$Push); return ('a'*40) }
function Get-HcReleaseStage { param($Directory, $Version, $Repository); return (Get-Content -LiteralPath (Join-Path $Directory 'release.json') -Raw | ConvertFrom-Json) }
function Test-HcReleaseIncludesLan { param($Stage); return $Stage.publishLan }
function Assert-HcReleaseStageTrust {
    param($Directory, $Stage, $Config)
    foreach ($item in $Stage.files) {
        if ((Get-FileHash -LiteralPath (Join-Path $Directory $item.path)).Hash -cne $item.sha256) { throw 'Fixture stage bytes changed.' }
    }
    [IO.File]::WriteAllText((Join-Path $Directory 'verified.fixture'), 'verified')
}
'@)
    [IO.File]::WriteAllText((Join-Path $hcScripts 'Build.ps1'), @'
param($Version, [switch]$Zip, [switch]$SingleExe, [switch]$SkipPayloads, [switch]$SkipTests, $UpdateFeedUrl, $UpdateFallbackFeedUrl, [string[]]$UpdateFeedUrls, $UpdatePublicKeyPath, $ReleaseSourceCommit)
if (($UpdateFeedUrls -join '|') -cne ((Get-HcOfficialFeedUrls) -join '|') -or $UpdateFeedUrl -cne $UpdateFeedUrls[0] -or $UpdateFallbackFeedUrl -cne $UpdateFeedUrls[2]) { throw 'Fixture build configuration was not the official three-source chain.' }
throw 'Fixture reached build with all three sources.'
'@)
    [IO.File]::WriteAllText((Join-Path $hcScripts 'Publish-GitHubUpdate.ps1'), @'
param($Version, $GitHubRepository, $ReleaseDirectory, [switch]$PrepareOnly)
if (!(Test-Path -LiteralPath (Join-Path $ReleaseDirectory 'verified.fixture'))) { throw 'Coordinator must verify the stage before publication.' }
'@)
    $hcArguments = @{ Version='1.2.3'; NotesEnglish=(Join-Path $hcFixture 'notes.txt'); NotesChinese=(Join-Path $hcFixture 'notes.txt') }
    Assert-HcConfigRejected { & (Join-Path $hcScripts 'Publish-Update.ps1') @hcArguments } 'new three-source publication requires a valid local LAN feed'
    Remove-Item -LiteralPath (Join-Path $hcRetiredPublic 'latest.json'),(Join-Path $hcRetiredPublic 'HappyCheats-1.2.3-win-x64.zip')
    $hcReachedBuild = $false
    try { & (Join-Path $hcScripts 'Publish-Update.ps1') @hcArguments }
    catch { $hcReachedBuild = $_.Exception.Message -ceq 'Fixture reached build with all three sources.' }
    Assert-HcConfigTest $hcReachedBuild 'new release builds the official three-source settings without a peer host connection'
    $hcFiles = @()
    foreach ($hcRelative in @('assets/HappyCheats.exe','assets/HappyCheats-1.2.3.exe','assets/HappyCheats-1.2.3-win-x64-single-exe.zip',
        'assets/HappyCheats-1.2.3-win-x64-single-exe.zip.sha256','assets/HappyCheats-1.2.3-win-x64.zip.sha256','github/HappyCheats-1.2.3-win-x64.zip')) {
        $hcPath = Join-Path $hcStagePath $hcRelative
        New-Item -ItemType Directory -Path (Split-Path $hcPath) -Force | Out-Null
        [IO.File]::WriteAllText($hcPath, 'immutable fixture bytes: ' + $hcRelative)
        $hcFiles += [pscustomobject]@{ path=$hcRelative; sha256=(Get-FileHash -LiteralPath $hcPath).Hash }
    }
    @{ commit=('a'*40); publishLan=$false; files=$hcFiles } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $hcStagePath 'release.json') -Encoding utf8NoBOM
    [IO.File]::WriteAllText((Join-Path $hcRetiredPublic 'latest.json'), '{ malformed unrelated LAN feed')
    $hcDist = Join-Path $hcFixture 'dist'
    Assert-HcConfigTest (!(Test-Path -LiteralPath $hcDist)) 'recovery fixture starts without dist'
    & (Join-Path $hcScripts 'Publish-Update.ps1') @hcArguments 6>$null | Out-Null
    foreach ($hcFile in $hcFiles) {
        $hcRestored = Join-Path $hcDist ([IO.Path]::GetFileName($hcFile.path))
        Assert-HcConfigTest ((Get-FileHash -LiteralPath $hcRestored).Hash -ceq $hcFile.sha256) 'staged recovery restores each required EXE ZIP and checksum byte-for-byte'
    }
    [IO.File]::WriteAllText((Join-Path $hcDist 'HappyCheats.exe'), 'a later local build')
    & (Join-Path $hcScripts 'Publish-Update.ps1') @hcArguments 6>$null | Out-Null
    Assert-HcConfigTest ((Get-FileHash -LiteralPath (Join-Path $hcDist 'HappyCheats.exe')).Hash -ceq $hcFiles[0].sha256) 'repeated recovery restores the same staged bytes without rebuilding'
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
    Assert-HcConfigRejected { Get-HcSigningPublicIdentity -PrivateKeyPath $hcPrivate -PublicKeyPath $hcPrivate } 'matching private PEM cannot be embedded as the public verification key'
    $hcMixedPublic = Join-Path $hcTestRoot 'mixed-public.pem'
    [IO.File]::WriteAllText($hcMixedPublic, $hcKey.ExportSubjectPublicKeyInfoPem() + "`n" + $hcKey.ExportRSAPrivateKeyPem())
    Assert-HcConfigRejected { Get-HcSigningPublicIdentity -PrivateKeyPath $hcPrivate -PublicKeyPath $hcMixedPublic } 'public PEM with appended RSA private material rejected'
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
    $hcLegacy.PublicKeyPath = $hcOtherPublic
    $hcLegacy | ConvertTo-Json | Set-Content -LiteralPath $hcLegacyPath -Encoding utf8NoBOM
    Assert-HcConfigRejected { Get-HcPublisherConfiguration -Root $hcRoot -PublisherDirectory $hcLocal -RequireLan } 'local LAN mirror must preserve the same verification key as the publisher'
    [IO.File]::WriteAllText($hcLegacyPath, '{ malformed retired LAN configuration')
    $hcWithoutLan = Get-HcPublisherConfiguration -Root $hcRoot -PublisherDirectory $hcLocal
    Assert-HcConfigTest ($hcWithoutLan.PrivateKeyPath -ceq $hcConfig.PrivateKeyPath -and !$hcWithoutLan.PublicDirectory -and !$hcWithoutLan.BaseUrl) 'normal GitHub publisher ignores malformed LAN config and exposes no retired target'
    Assert-HcConfigRejected { Get-HcPublisherConfiguration -Root $hcRoot -PublisherDirectory $hcLocal -RequireLan } 'explicit LAN publication still validates the saved host configuration'
    Assert-HcConfigRejected { Get-HcPublisherConfiguration -Root $hcRoot -PublisherDirectory (Join-Path $hcTestRoot 'unconfigured-publisher') } 'legacy key discovery requires valid legacy configuration when no local publisher exists'
    $hcLocalSettings = $hcLocalJson | ConvertFrom-Json
    $hcLocalSettings.PublicKeyPath = $hcLocalSettings.PrivateKeyPath
    $hcLocalSettings | ConvertTo-Json | Set-Content -LiteralPath $hcInstalled.ConfigurationPath -Encoding utf8NoBOM
    Assert-HcConfigRejected { Get-HcPublisherConfiguration -Root $hcRoot -PublisherDirectory $hcLocal } 'publisher rejects a private key path in its public key setting before a build'
    [IO.File]::WriteAllText($hcInstalled.ConfigurationPath, $hcLocalJson)
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
    Test-HcPublicationRecovery
    Write-Host "Publisher configuration offline checks passed: $hcCount. No live keys, credentials, network or release publishing used."
} finally {
    $hcKey.Dispose(); $hcOtherKey.Dispose()
    $hcResolved = [IO.Path]::GetFullPath($hcTestRoot)
    $hcTempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if ($hcResolved.StartsWith($hcTempPrefix, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($hcResolved).StartsWith('HappyCheats-PublisherConfig-Tests-') -and (Test-Path -LiteralPath $hcResolved)) { Remove-Item -LiteralPath $hcResolved -Recurse -Force }
}
