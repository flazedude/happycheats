# Signed, version-pinned mirror assets. This file never discovers or reads a private key.
function Get-HcOfficialFeedUrls([string]$Repository = 'flazedude/happycheats') {
    if ($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid GitHub repository.' }
    return @('http://192.168.2.76:17845/happycheats/latest.json', 'http://192.168.2.59:17845/happycheats/latest.json', "https://github.com/$Repository/releases/latest/download/latest.json")
}
function Get-HcLanFeedAsset([string]$BaseUrl) {
    switch -CaseSensitive ($BaseUrl) {
        'http://192.168.2.76:17845/happycheats' { return 'latest-lan-76.json' }
        'http://192.168.2.59:17845/happycheats' { return 'latest-lan-59.json' }
        default { throw 'The saved LAN address is not one of the two configured update mirrors.' }
    }
}
function Assert-HcMirrorSafePath([string]$Path) {
    for ($hcPart = [IO.Path]::GetFullPath($Path); $hcPart; $hcPart = [IO.Path]::GetDirectoryName($hcPart)) {
        if ((Test-Path -LiteralPath $hcPart) -and ((Get-Item -LiteralPath $hcPart -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Mirror paths must not traverse links.' }
    }
}
function Read-HcMirrorPublicKey([string]$Path) {
    $hcPem = [IO.File]::ReadAllText($Path)
    if ($hcPem -match 'PRIVATE KEY') { throw 'Mirror verification requires a public-only PEM.' }
    $hcKey = [Security.Cryptography.RSA]::Create()
    try { $hcKey.ImportFromPem($hcPem); if ($hcKey.KeySize -lt 2048) { throw 'The verification key is too small.' }; return $hcKey }
    catch { $hcKey.Dispose(); throw }
    finally { $hcPem = $null }
}
function Read-HcSignedMirrorPayload([string]$Path, [Security.Cryptography.RSA]$Key) {
    Assert-HcMirrorSafePath $Path
    if ((Get-Item -LiteralPath $Path).Length -gt 131072) { throw 'Signed mirror metadata is too large.' }
    $hcEnvelope = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $hcPayload = [Convert]::FromBase64String($hcEnvelope.payload)
    if (!$Key.VerifyData($hcPayload, [Convert]::FromBase64String($hcEnvelope.signature), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw 'Mirror metadata signature is invalid.' }
    return ([Text.Encoding]::UTF8.GetString($hcPayload) | ConvertFrom-Json)
}
function Get-HcMirrorAssetNames([string]$Version) {
    return @('latest.json','latest-lan-76.json','latest-lan-59.json',"HappyCheats-$Version-win-x64.zip",'HappyCheats.exe',"HappyCheats-$Version.exe", "HappyCheats-$Version-win-x64-single-exe.zip", "HappyCheats-$Version-win-x64.zip.sha256", "HappyCheats-$Version-win-x64-single-exe.zip.sha256")
}
function Read-HcMirrorManifest([string]$Path, [Security.Cryptography.RSA]$Key, [string]$Repository) {
    $null = Get-HcOfficialFeedUrls $Repository
    $hcManifest = Read-HcSignedMirrorPayload -Path $Path -Key $Key
    if ($hcManifest.schemaVersion -ne 1 -or $hcManifest.repository -cne $Repository -or $hcManifest.channel -cne 'stable' -or
        $hcManifest.version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or $hcManifest.commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Signed mirror identity is invalid.' }
    $hcAllowed = @(Get-HcMirrorAssetNames $hcManifest.version)
    $hcSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($hcAsset in $hcManifest.assets) {
        if ($hcAllowed -cnotcontains $hcAsset.name -or !$hcSeen.Add($hcAsset.name) -or $hcAsset.size -le 0 -or $hcAsset.size -gt 2GB -or $hcAsset.sha256 -cnotmatch '^[a-f0-9]{64}$') { throw 'Signed mirror assets are invalid or duplicated.' }
        if ($hcAsset.name.EndsWith('.json') -and $hcAsset.size -gt 131072) { throw 'Signed feed asset is too large.' }
    }
    if ($hcSeen.Count -ne $hcAllowed.Count) { throw 'The signed mirror is missing required assets.' }
    $hcExe = @($hcManifest.assets | Where-Object name -CEQ 'HappyCheats.exe')[0]
    $hcVersioned = @($hcManifest.assets | Where-Object name -CEQ "HappyCheats-$($hcManifest.version).exe")[0]
    if ($hcExe.sha256 -cne $hcVersioned.sha256 -or $hcExe.size -ne $hcVersioned.size) { throw 'Signed standalone copies differ.' }
    return $hcManifest
}
function Assert-HcMirrorAsset([string]$Path, $Asset) {
    Assert-HcMirrorSafePath $Path
    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -ne $Asset.size -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $Asset.sha256) { throw "Mirror asset failed size/SHA-256 verification: $($Asset.name)." }
}
function Copy-HcVerifiedMirrorCacheAsset([string]$Source, [string]$Destination, $Asset) {
    Assert-HcMirrorSafePath $Source
    Assert-HcMirrorSafePath $Destination
    if (!(Test-Path -LiteralPath $Source -PathType Leaf) -or (Get-Item -LiteralPath $Source).Length -ne $Asset.size -or (Get-FileHash -LiteralPath $Source).Hash -ine $Asset.sha256) { return $false }
    $hcTemporary = $Destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        Copy-Item -LiteralPath $Source -Destination $hcTemporary
        # The server's latest files may be replaced during a copy. Recheck the
        # copied bytes before treating them as an immutable download-cache asset.
        Assert-HcMirrorAsset -Path $hcTemporary -Asset $Asset
        [IO.File]::Move($hcTemporary, $Destination, $false)
        return $true
    } finally { if (Test-Path -LiteralPath $hcTemporary) { Remove-Item -LiteralPath $hcTemporary -Force } }
}
function Assert-HcMirrorPackage([string]$Path, $Manifest) {
    Assert-HcMirrorSafePath $Path
    $hcArchive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $hcEntries = @($hcArchive.Entries | Where-Object FullName -CEQ 'HappyCheats/release-source.json')
        if ($hcEntries.Count -ne 1 -or $hcEntries[0].Length -gt 4096) { throw 'Mirror package lacks a unique bounded source record.' }
        $hcReader = [IO.StreamReader]::new($hcEntries[0].Open())
        try { $hcSource = $hcReader.ReadToEnd() | ConvertFrom-Json } finally { $hcReader.Dispose() }
        if ($hcSource.version -cne $Manifest.version -or $hcSource.commit -cne $Manifest.commit) { throw 'Mirror package source does not match signed metadata.' }
    } finally { $hcArchive.Dispose() }
}
function Assert-HcMirrorFeed([string]$Path, [Security.Cryptography.RSA]$Key, $Manifest, [string]$BaseUrl) {
    $hcFeed = Read-HcSignedMirrorPayload -Path $Path -Key $Key
    $hcZipName = "HappyCheats-$($Manifest.version)-win-x64.zip"
    $hcArchive = @($Manifest.assets | Where-Object name -CEQ $hcZipName)[0]
    if ($hcFeed.schemaVersion -ne 1 -or $hcFeed.channel -cne 'stable' -or $hcFeed.version -cne $Manifest.version -or
        $hcFeed.packageUrl -cne "$BaseUrl/$hcZipName" -or $hcFeed.packageSize -ne $hcArchive.size -or $hcFeed.sha256 -ine $hcArchive.sha256) { throw 'Signed LAN feed does not identify the identical package for this host.' }
    return $hcFeed
}
function Copy-HcMirrorAtomic([string]$Source, [string]$Destination, [string]$BackupDirectory) {
    Assert-HcMirrorSafePath $Destination
    Assert-HcMirrorSafePath $BackupDirectory
    $hcTemporary = $Destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        Copy-Item -LiteralPath $Source -Destination $hcTemporary
        if (Test-Path -LiteralPath $Destination) {
            New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
            [IO.File]::Replace($hcTemporary, $Destination, (Join-Path $BackupDirectory ([IO.Path]::GetFileName($Destination) + '.' + [Guid]::NewGuid().ToString('N'))))
        } else { [IO.File]::Move($hcTemporary, $Destination, $false) }
    } finally { if (Test-Path -LiteralPath $hcTemporary) { Remove-Item -LiteralPath $hcTemporary -Force } }
}
function Install-HcLanMirror {
    # Caller holds artifacts/lan-updates/publish.lock through verification and activation.
    param($Config, [string]$Directory, [string]$Repository, [string]$BackupDirectory, [string]$PackageDirectory)
    if (!$PackageDirectory) { $PackageDirectory = $Directory }
    $hcFeedName = Get-HcLanFeedAsset $Config.BaseUrl
    Assert-HcMirrorSafePath $Config.PublicDirectory
    $hcKey = Read-HcMirrorPublicKey $Config.PublicKeyPath
    try {
        $hcManifest = Read-HcMirrorManifest -Path (Join-Path $Directory 'mirror.json') -Key $hcKey -Repository $Repository
        $hcZipName = "HappyCheats-$($hcManifest.version)-win-x64.zip"
        foreach ($hcName in @($hcFeedName, $hcZipName, 'HappyCheats.exe')) {
            $hcAsset = @($hcManifest.assets | Where-Object name -CEQ $hcName)[0]
            $hcAssetDirectory = if ($hcName -ceq $hcZipName) { $PackageDirectory } else { $Directory }
            Assert-HcMirrorAsset -Path (Join-Path $hcAssetDirectory $hcName) -Asset $hcAsset
        }
        $null = Assert-HcMirrorFeed -Path (Join-Path $Directory $hcFeedName) -Key $hcKey -Manifest $hcManifest -BaseUrl $Config.BaseUrl
        Assert-HcMirrorPackage -Path (Join-Path $PackageDirectory $hcZipName) -Manifest $hcManifest
        $hcFeedDestination = Join-Path $Config.PublicDirectory 'latest.json'
        Assert-HcMirrorSafePath $hcFeedDestination
        if (Test-Path -LiteralPath $hcFeedDestination) {
            $hcPrevious = Read-HcSignedMirrorPayload -Path $hcFeedDestination -Key $hcKey
            if ($hcPrevious.channel -cne 'stable' -or $hcPrevious.version -cnotmatch '^\d+\.\d+\.\d+$') { throw 'Existing LAN feed identity is invalid; preserve it for inspection.' }
            if ([Version]$hcPrevious.version -gt [Version]$hcManifest.version) { throw 'Refusing to downgrade the LAN mirror.' }
            if ([Version]$hcPrevious.version -eq [Version]$hcManifest.version -and (Get-FileHash -LiteralPath $hcFeedDestination).Hash -cne (Get-FileHash -LiteralPath (Join-Path $Directory $hcFeedName)).Hash) { throw 'An existing same-version LAN feed has different signed bytes.' }
        }
        $hcZipDestination = Join-Path $Config.PublicDirectory $hcZipName
        Assert-HcMirrorSafePath $hcZipDestination
        if (Test-Path -LiteralPath $hcZipDestination) {
            Assert-HcMirrorAsset -Path $hcZipDestination -Asset @($hcManifest.assets | Where-Object name -CEQ $hcZipName)[0]
        }
        New-Item -ItemType Directory -Path $Config.PublicDirectory -Force | Out-Null
        if (!(Test-Path -LiteralPath $hcZipDestination)) {
            $hcTemporary = $hcZipDestination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
            try { Copy-Item -LiteralPath (Join-Path $PackageDirectory $hcZipName) -Destination $hcTemporary; [IO.File]::Move($hcTemporary, $hcZipDestination, $false) }
            finally { if (Test-Path -LiteralPath $hcTemporary) { Remove-Item -LiteralPath $hcTemporary -Force } }
        }
        foreach ($hcPair in @(@('HappyCheats.exe','HappyCheats.exe'),@($hcFeedName,'latest.json'))) {
            $hcSource = Join-Path $Directory $hcPair[0]; $hcDestination = Join-Path $Config.PublicDirectory $hcPair[1]
            Assert-HcMirrorSafePath $hcDestination
            if (!(Test-Path -LiteralPath $hcDestination) -or (Get-FileHash -LiteralPath $hcSource).Hash -cne (Get-FileHash -LiteralPath $hcDestination).Hash) { Copy-HcMirrorAtomic -Source $hcSource -Destination $hcDestination -BackupDirectory $BackupDirectory }
        }
        return $hcManifest.version
    } finally { $hcKey.Dispose() }
}
