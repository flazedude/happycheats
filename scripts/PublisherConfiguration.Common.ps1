# Machine-local signing configuration. No private key material is returned or logged.
function Assert-HcPublisherRepository([string]$Repository) {
    if ($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'GitHubRepository must be owner/repository.' }
}

function Assert-HcPrivateKeyLocation([string]$Path, [string]$Root, [string]$PublicDirectory) {
    if (![IO.Path]::IsPathFullyQualified($Path)) { throw 'Publisher key paths must be absolute.' }
    $hcKeyPath = [IO.Path]::GetFullPath($Path)
    foreach ($hcForbidden in @($Root, $PublicDirectory)) {
        if (!$hcForbidden) { continue }
        $hcPrefix = [IO.Path]::GetFullPath($hcForbidden).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
        if ($hcKeyPath.StartsWith($hcPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Private signing keys must remain outside the checkout and served directory.' }
    }
}

function Get-HcSigningPublicIdentity([string]$PrivateKeyPath, [string]$PublicKeyPath) {
    $hcPrivate = [Security.Cryptography.RSA]::Create()
    $hcPublic = [Security.Cryptography.RSA]::Create()
    try {
        if (!(Test-Path -LiteralPath $PrivateKeyPath -PathType Leaf)) { throw 'Missing private key.' }
        $hcPrivate.ImportFromPem([IO.File]::ReadAllText($PrivateKeyPath))
        # Signing proves the input actually contains the private half.
        $null = $hcPrivate.SignData([byte[]](1,2,3), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $hcIdentity = [Convert]::ToBase64String($hcPrivate.ExportSubjectPublicKeyInfo())
        if ($PublicKeyPath) {
            if (!(Test-Path -LiteralPath $PublicKeyPath -PathType Leaf)) { throw 'Missing public key.' }
            $hcPublicPem = [IO.File]::ReadAllText($PublicKeyPath)
            # Build embeds this file verbatim in client settings. Never accept a
            # private PEM here, even when it has the expected public identity.
            if ($hcPublicPem -match '-----BEGIN [^-]*PRIVATE KEY-----') { throw 'Public key input contains private material.' }
            $hcPublic.ImportFromPem($hcPublicPem)
            if ($hcIdentity -cne [Convert]::ToBase64String($hcPublic.ExportSubjectPublicKeyInfo())) { throw 'Key pair differs.' }
        }
        return $hcIdentity
    } catch { throw 'Established signing key is missing, unreadable or does not match its public key. Restore the correct key; no replacement will be generated.' }
    finally { $hcPrivate.Dispose(); $hcPublic.Dispose(); $hcPublicPem = $null }
}

function Get-HcPublisherConfiguration {
    param([string]$Root, [string]$Repository = 'flazedude/happycheats', [string]$PublisherDirectory, [switch]$RequireLan)
    Assert-HcPublisherRepository $Repository
    if (!$PublisherDirectory) { $PublisherDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HappyCheatsPublisher' }
    $hcLocalPath = Join-Path $PublisherDirectory 'publisher.json'
    $hcLegacyPath = Join-Path $Root 'artifacts/lan-updates/server.json'
    $hcHasLocal = Test-Path -LiteralPath $hcLocalPath -PathType Leaf
    # A configured GitHub publisher has no dependency on a retired host's JSON
    # or paths. Read that file only for legacy key discovery or explicit LAN use.
    $hcLegacy = if (($RequireLan -or !$hcHasLocal) -and (Test-Path -LiteralPath $hcLegacyPath -PathType Leaf)) { Get-Content -LiteralPath $hcLegacyPath -Raw | ConvertFrom-Json } else { $null }
    if ($hcHasLocal) {
        $hcSaved = Get-Content -LiteralPath $hcLocalPath -Raw | ConvertFrom-Json
        if ($hcSaved.Repository -ine $Repository) { throw 'Local publisher configuration belongs to another repository. Initialize the intended repository explicitly.' }
    } elseif ($hcLegacy) {
        if ($Repository -cne 'flazedude/happycheats') { throw 'Legacy signing configuration is only trusted for flazedude/happycheats.' }
        $hcSaved = $hcLegacy
    } else { throw 'Publisher is not configured on this PC. Run Initialize-Publisher.ps1 with the existing release private key transferred securely outside the repository.' }
    Assert-HcPrivateKeyLocation -Path $hcSaved.PrivateKeyPath -Root $Root -PublicDirectory $hcLegacy.PublicDirectory
    if (![IO.Path]::IsPathFullyQualified($hcSaved.PublicKeyPath)) { throw 'Publisher key paths must be absolute.' }
    $null = Get-HcSigningPublicIdentity -PrivateKeyPath $hcSaved.PrivateKeyPath -PublicKeyPath $hcSaved.PublicKeyPath
    if ($RequireLan -and (!$hcLegacy -or !$hcLegacy.BaseUrl -or !$hcLegacy.PublicDirectory)) { throw 'Explicit legacy LAN publication requires the saved LAN server configuration.' }
    if ($RequireLan) { $null = Get-HcSigningPublicIdentity -PrivateKeyPath $hcSaved.PrivateKeyPath -PublicKeyPath $hcLegacy.PublicKeyPath }
    return [pscustomobject]@{
        Repository=$Repository; PrivateKeyPath=$hcSaved.PrivateKeyPath; PublicKeyPath=$hcSaved.PublicKeyPath
        BaseUrl=$hcLegacy.BaseUrl; PublicDirectory=$hcLegacy.PublicDirectory
    }
}

function Assert-HcPublishedFeedKey {
    param([string]$EnvelopeJson, [Security.Cryptography.RSA]$Key, [string]$Repository)
    Assert-HcPublisherRepository $Repository
    try {
        $hcEnvelope = $EnvelopeJson | ConvertFrom-Json
        $hcPayload = [Convert]::FromBase64String($hcEnvelope.payload)
        if (!$Key.VerifyData($hcPayload, [Convert]::FromBase64String($hcEnvelope.signature), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw 'Signature differs.' }
        $hcFeed = [Text.Encoding]::UTF8.GetString($hcPayload) | ConvertFrom-Json
        if ($hcFeed.schemaVersion -ne 1 -or $hcFeed.channel -cne 'stable' -or $hcFeed.version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or $hcFeed.packageSize -le 0 -or $hcFeed.sha256 -notmatch '^[0-9a-f]{64}$') { throw 'Invalid release identity.' }
        $hcExpected = "https://github.com/$Repository/releases/download/v$($hcFeed.version)/HappyCheats-$($hcFeed.version)-win-x64.zip"
        if ($hcFeed.packageUrl -cne $hcExpected) { throw 'Repository or pinned package URL differs.' }
        return $hcFeed.version
    } catch { throw 'The supplied key does not verify the current signed stable GitHub release for this repository. Publisher configuration was not installed.' }
}

function Initialize-HcPublisherConfiguration {
    param([string]$Root, [string]$Repository, [string]$PrivateKeyPath, [string]$EnvelopeJson, [string]$PublisherDirectory)
    Assert-HcPublisherRepository $Repository
    if (!$PublisherDirectory) { $PublisherDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HappyCheatsPublisher' }
    $hcLegacyPath = Join-Path $Root 'artifacts/lan-updates/server.json'
    $hcLegacy = if (Test-Path -LiteralPath $hcLegacyPath) { Get-Content -LiteralPath $hcLegacyPath -Raw | ConvertFrom-Json } else { $null }
    Assert-HcPrivateKeyLocation -Path $PrivateKeyPath -Root $Root -PublicDirectory $hcLegacy.PublicDirectory
    $hcIdentity = Get-HcSigningPublicIdentity -PrivateKeyPath $PrivateKeyPath
    $hcKey = [Security.Cryptography.RSA]::Create()
    try {
        $hcKey.ImportFromPem([IO.File]::ReadAllText($PrivateKeyPath))
        $hcVerifiedVersion = Assert-HcPublishedFeedKey -EnvelopeJson $EnvelopeJson -Key $hcKey -Repository $Repository
        $hcConfigPath = Join-Path $PublisherDirectory 'publisher.json'
        if (Test-Path -LiteralPath $hcConfigPath) {
            $hcExisting = Get-HcPublisherConfiguration -Root $Root -Repository $Repository -PublisherDirectory $PublisherDirectory
            if ((Get-HcSigningPublicIdentity -PrivateKeyPath $hcExisting.PrivateKeyPath -PublicKeyPath $hcExisting.PublicKeyPath) -cne $hcIdentity) { throw 'An existing publisher uses a different key. It will not be overwritten.' }
            return [pscustomobject]@{ ConfigurationPath=$hcConfigPath; VerifiedVersion=$hcVerifiedVersion; Reused=$true }
        }
        $hcKeyDirectory = Join-Path $PublisherDirectory ('Signing/' + $Repository.Replace('/','_'))
        $hcDestinationPrivate = Join-Path $hcKeyDirectory 'release-private.pem'
        $hcDestinationPublic = Join-Path $hcKeyDirectory 'release-public.pem'
        Assert-HcPrivateKeyLocation -Path $hcDestinationPrivate -Root $Root -PublicDirectory $hcLegacy.PublicDirectory
        if (Test-Path -LiteralPath $hcDestinationPrivate) {
            if ((Get-HcSigningPublicIdentity -PrivateKeyPath $hcDestinationPrivate) -cne $hcIdentity) { throw 'The destination contains a different private key. It will not be overwritten.' }
        }
        if (Test-Path -LiteralPath $hcDestinationPublic) {
            $null = Get-HcSigningPublicIdentity -PrivateKeyPath $PrivateKeyPath -PublicKeyPath $hcDestinationPublic
        }
        New-Item -ItemType Directory -Path $hcKeyDirectory -Force | Out-Null
        if ($IsWindows) {
            $hcAcl = [Security.AccessControl.DirectorySecurity]::new()
            $hcAcl.SetAccessRuleProtection($true, $false)
            foreach ($hcSid in @([Security.Principal.WindowsIdentity]::GetCurrent().User, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
                $hcAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($hcSid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
            }
            Set-Acl -LiteralPath $hcKeyDirectory -AclObject $hcAcl
        }
        # CreateNew/Copy(false) never replace a concurrently installed or existing key.
        if (!(Test-Path -LiteralPath $hcDestinationPrivate)) { [IO.File]::Copy($PrivateKeyPath, $hcDestinationPrivate, $false) }
        if (!(Test-Path -LiteralPath $hcDestinationPublic)) {
            $hcStream = [IO.File]::Open($hcDestinationPublic, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            $hcWriter = [IO.StreamWriter]::new($hcStream, [Text.UTF8Encoding]::new($false))
            try { $hcWriter.Write($hcKey.ExportSubjectPublicKeyInfoPem()) } finally { $hcWriter.Dispose() }
        }
        $null = Get-HcSigningPublicIdentity -PrivateKeyPath $hcDestinationPrivate -PublicKeyPath $hcDestinationPublic
        $hcConfigJson = [ordered]@{ Repository=$Repository; PrivateKeyPath=$hcDestinationPrivate; PublicKeyPath=$hcDestinationPublic } | ConvertTo-Json
        $hcConfigTemporary = $hcConfigPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        [IO.File]::WriteAllText($hcConfigTemporary, $hcConfigJson, [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($hcConfigTemporary, $hcConfigPath, $false)
        return [pscustomobject]@{ ConfigurationPath=$hcConfigPath; VerifiedVersion=$hcVerifiedVersion; Reused=$false }
    } finally { $hcKey.Dispose() }
}
