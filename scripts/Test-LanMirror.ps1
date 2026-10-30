[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GitHubRelease.Common.ps1')
$hcTestRoot = Join-Path ([IO.Path]::GetTempPath()) ('HappyCheats-Mirror-Tests-' + [Guid]::NewGuid().ToString('N'))
$hcStageDirectory = Join-Path $hcTestRoot 'stage'
$hcVersion = '1.2.3'; $hcRepository = 'flazedude/happycheats'; $hcCommit = 'a'*40
$hcZipName = "HappyCheats-$hcVersion-win-x64.zip"
$hcKey = [Security.Cryptography.RSA]::Create(2048)
$hcCount = 0
function Assert-HcMirrorTest([bool]$Condition, [string]$Message) { if (!$Condition) { throw "LAN mirror regression failed: $Message" }; $script:hcCount++ }
function Assert-HcMirrorRejected([scriptblock]$Action, [string]$Message) { $hcFailed = $false; try { $null = & $Action } catch { $hcFailed = $true }; Assert-HcMirrorTest $hcFailed $Message }
function Write-HcMirrorTestSigned([string]$Path, $Payload) {
    $hcBytes = [Text.Encoding]::UTF8.GetBytes(($Payload | ConvertTo-Json -Depth 8 -Compress))
    $hcSignature = $hcKey.SignData($hcBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    @{ payload=[Convert]::ToBase64String($hcBytes); signature=[Convert]::ToBase64String($hcSignature) } | ConvertTo-Json | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}
function New-HcMirrorTestFeed([string]$BaseUrl) {
    return [pscustomobject]@{ schemaVersion=1; channel='stable'; version=$hcVersion; publishedAt='2026-10-30T00:00:00Z'; packageUrl="$BaseUrl/$hcZipName"; packageSize=(Get-Item -LiteralPath (Join-Path $hcStageDirectory "github/$hcZipName")).Length; sha256=(Get-FileHash -LiteralPath (Join-Path $hcStageDirectory "github/$hcZipName")).Hash.ToLowerInvariant(); changelogEnglish='Fixture'; changelogChinese='测试'; newGames=@() }
}
try {
    foreach ($hcName in @('github','assets','lan-76','lan-59','package/HappyCheats')) { New-Item -ItemType Directory -Path (Join-Path $hcStageDirectory $hcName) -Force | Out-Null }
    $hcPublicPath = Join-Path $hcTestRoot 'public.pem'; $hcPrivatePath = Join-Path $hcTestRoot 'private.pem'
    [IO.File]::WriteAllText($hcPublicPath, $hcKey.ExportSubjectPublicKeyInfoPem()); [IO.File]::WriteAllText($hcPrivatePath, $hcKey.ExportPkcs8PrivateKeyPem())
    $hcFeedUrls = @(Get-HcOfficialFeedUrls $hcRepository)
    $hcSettings = @{ feedUrl=$hcFeedUrls[0]; fallbackFeedUrl=$hcFeedUrls[2]; feedUrls=$hcFeedUrls; publicKeyPem=$hcKey.ExportSubjectPublicKeyInfoPem(); channel='stable' } | ConvertTo-Json
    foreach ($hcName in @('update-settings.json','update-defaults.json')) { [IO.File]::WriteAllText((Join-Path $hcStageDirectory "package/HappyCheats/$hcName"), $hcSettings) }
    @{version=$hcVersion;commit=$hcCommit} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hcStageDirectory 'package/HappyCheats/release-source.json') -Encoding utf8NoBOM
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $hcStageDirectory 'package'), (Join-Path $hcStageDirectory "github/$hcZipName"))
    foreach ($hcHostName in @('76','59')) {
        Copy-Item -LiteralPath (Join-Path $hcStageDirectory "github/$hcZipName") -Destination (Join-Path $hcStageDirectory "lan-$hcHostName/$hcZipName")
        Write-HcMirrorTestSigned -Path (Join-Path $hcStageDirectory "lan-$hcHostName/latest.json") -Payload (New-HcMirrorTestFeed "http://192.168.2.${hcHostName}:17845/happycheats")
        Copy-Item -LiteralPath (Join-Path $hcStageDirectory "lan-$hcHostName/latest.json") -Destination (Join-Path $hcStageDirectory "assets/latest-lan-$hcHostName.json")
    }
    Write-HcMirrorTestSigned -Path (Join-Path $hcStageDirectory 'github/latest.json') -Payload (New-HcMirrorTestFeed "https://github.com/$hcRepository/releases/download/v$hcVersion")
    [IO.File]::WriteAllBytes((Join-Path $hcStageDirectory 'assets/HappyCheats.exe'), [byte[]](1,2,3,4,5,6))
    Copy-Item -LiteralPath (Join-Path $hcStageDirectory 'assets/HappyCheats.exe') -Destination (Join-Path $hcStageDirectory "assets/HappyCheats-$hcVersion.exe")
    $hcSingle = [IO.Compression.ZipFile]::Open((Join-Path $hcStageDirectory "assets/HappyCheats-$hcVersion-win-x64-single-exe.zip"), [IO.Compression.ZipArchiveMode]::Create)
    try { $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($hcSingle, (Join-Path $hcStageDirectory 'assets/HappyCheats.exe'), 'HappyCheats.exe') } finally { $hcSingle.Dispose() }
    foreach ($hcName in @($hcZipName,"HappyCheats-$hcVersion-win-x64-single-exe.zip")) { [IO.File]::WriteAllText((Join-Path $hcStageDirectory "assets/$hcName.sha256"), 'fixture sidecar') }
    Write-HcSignedMirrorManifest -Directory $hcStageDirectory -Version $hcVersion -Repository $hcRepository -Commit $hcCommit -PrivateKeyPath $hcPrivatePath
    $hcManifestPath = Join-Path $hcStageDirectory 'assets/mirror.json'
    $hcOriginalManifestBytes = [IO.File]::ReadAllBytes($hcManifestPath)
    $hcManifest = Read-HcMirrorManifest -Path $hcManifestPath -Key $hcKey -Repository $hcRepository
    Assert-HcMirrorTest ($hcManifest.assets.Count -eq 9 -and $hcManifest.commit -ceq $hcCommit) 'signed metadata binds both feeds all distribution assets and source'
    $hcConfig = [pscustomobject]@{ BaseUrl='http://192.168.2.59:17845/happycheats'; PublicDirectory=(Join-Path $hcTestRoot 'public-59'); PublicKeyPath=$hcPublicPath }
    $hcStage = [pscustomobject]@{ schemaVersion=3; version=$hcVersion;repository=$hcRepository;commit=$hcCommit;publishLan=$true;lanBaseUrls=@('http://192.168.2.76:17845/happycheats','http://192.168.2.59:17845/happycheats');feedUrls=$hcFeedUrls }
    $hcUploadFiles = @(foreach ($hcName in (@(Get-HcMirrorAssetNames $hcVersion) + @('mirror.json'))) { [pscustomobject]@{path=('assets/' + $hcName);upload=$true} })
    $hcStage | Add-Member -NotePropertyName files -NotePropertyValue $hcUploadFiles
    Assert-HcMirrorTest ((Get-HcReleaseUploads $hcStage).Count -eq 10) 'every signed mirror asset is required in GitHub uploads'
    foreach ($hcName in @('mirror.json','latest-lan-76.json','latest-lan-59.json')) {
        $hcUpload = @($hcStage.files | Where-Object path -CEQ ('assets/' + $hcName))[0]
        $hcUpload.upload = $false
        Assert-HcMirrorRejected { Get-HcReleaseUploads $hcStage } 'missing mirror upload flag cannot publish an incomplete GitHub release'
        $hcUpload.upload = $true
    }
    Assert-HcReleaseStageTrust -Directory $hcStageDirectory -Stage $hcStage -Config $hcConfig
    Assert-HcMirrorTest $true 'full three-source stage validates on the second host'
    $hcInstall = @{ Config=$hcConfig; Directory=(Join-Path $hcStageDirectory 'assets'); PackageDirectory=(Join-Path $hcStageDirectory 'github'); Repository=$hcRepository; BackupDirectory=(Join-Path $hcTestRoot 'backups') }
    [IO.File]::Move($hcPrivatePath, $hcPrivatePath + '.offline')
    try { Assert-HcMirrorTest ((Install-HcLanMirror @hcInstall) -ceq $hcVersion) 'public-only local mirror install succeeds with the signing key unavailable' }
    finally { [IO.File]::Move($hcPrivatePath + '.offline', $hcPrivatePath) }
    $hcPublicFeed = Join-Path $hcConfig.PublicDirectory 'latest.json'
    $hcFeedHash = (Get-FileHash -LiteralPath $hcPublicFeed).Hash
    $hcArchiveHash = (Get-FileHash -LiteralPath (Join-Path $hcConfig.PublicDirectory $hcZipName)).Hash
    Assert-HcMirrorTest ($hcArchiveHash -ceq (Get-FileHash -LiteralPath (Join-Path $hcStageDirectory "github/$hcZipName")).Hash) 'LAN archive is byte-identical to GitHub'
    $hcCachedCopy = Join-Path $hcTestRoot 'verified-cache.zip'
    $hcArchiveAsset = @($hcManifest.assets | Where-Object name -CEQ $hcZipName)[0]
    Assert-HcMirrorTest (Copy-HcVerifiedMirrorCacheAsset -Source (Join-Path $hcConfig.PublicDirectory $hcZipName) -Destination $hcCachedCopy -Asset $hcArchiveAsset) 'already published local bytes seed the download cache without network'
    Assert-HcMirrorTest ((Get-FileHash -LiteralPath $hcCachedCopy).Hash -ceq $hcArchiveHash) 'cache seed is verified after copying'
    Assert-HcMirrorTest (!(Copy-HcVerifiedMirrorCacheAsset -Source (Join-Path $hcConfig.PublicDirectory 'HappyCheats.exe') -Destination (Join-Path $hcTestRoot 'wrong-cache.zip') -Asset $hcArchiveAsset)) 'unmatched local file falls back to a download instead of poisoning cache'
    $null = Install-HcLanMirror @hcInstall
    Assert-HcMirrorTest (!(Test-Path -LiteralPath $hcInstall.BackupDirectory)) 'identical retry makes no replacements or backup churn'
    $hcConfig76 = [pscustomobject]@{ BaseUrl='http://192.168.2.76:17845/happycheats'; PublicDirectory=(Join-Path $hcTestRoot 'public-76'); PublicKeyPath=$hcPublicPath }
    $null = Install-HcLanMirror -Config $hcConfig76 -Directory $hcInstall.Directory -PackageDirectory $hcInstall.PackageDirectory -Repository $hcRepository -BackupDirectory $hcInstall.BackupDirectory
    Assert-HcMirrorTest ((Get-FileHash -LiteralPath (Join-Path $hcConfig76.PublicDirectory $hcZipName)).Hash -ceq $hcArchiveHash -and (Get-FileHash -LiteralPath (Join-Path $hcConfig76.PublicDirectory 'HappyCheats.exe')).Hash -ceq (Get-FileHash -LiteralPath (Join-Path $hcConfig.PublicDirectory 'HappyCheats.exe')).Hash) 'offline peer catch-up installs identical ZIP and complete EXE'
    Assert-HcMirrorRejected { Get-HcLanFeedAsset 'http://192.168.2.60:17845/happycheats' } 'unknown server endpoint cannot select signed peer feeds'
    $hcOtherKey = [Security.Cryptography.RSA]::Create(2048)
    try { Assert-HcMirrorRejected { Read-HcMirrorManifest -Path $hcManifestPath -Key $hcOtherKey -Repository $hcRepository } 'wrong signing identity rejected' } finally { $hcOtherKey.Dispose() }
    Assert-HcMirrorRejected { Read-HcMirrorPublicKey $hcPrivatePath } 'sync cannot use a private key as public input'
    foreach ($hcMutation in @('path','duplicate','oversize','repository','channel','commit')) {
        $hcModified = Read-HcMirrorManifest -Path $hcManifestPath -Key $hcKey -Repository $hcRepository
        switch ($hcMutation) {
            'path' { $hcModified.assets[0].name='../private.pem' }
            'duplicate' { $hcModified.assets += $hcModified.assets[0] }
            'oversize' { $hcModified.assets[0].size=3GB }
            'repository' { $hcModified.repository='another/repo' }
            'channel' { $hcModified.channel='preview' }
            'commit' { $hcModified.commit='not-a-source-commit' }
        }
        Write-HcMirrorTestSigned -Path $hcManifestPath -Payload $hcModified
        Assert-HcMirrorRejected { Read-HcMirrorManifest -Path $hcManifestPath -Key $hcKey -Repository $hcRepository } "signed but invalid mirror $hcMutation rejected"
        [IO.File]::WriteAllBytes($hcManifestPath, $hcOriginalManifestBytes)
    }
    $hcModified = Read-HcMirrorManifest -Path $hcManifestPath -Key $hcKey -Repository $hcRepository
    $hcModified.commit = 'b'*40; Write-HcMirrorTestSigned -Path $hcManifestPath -Payload $hcModified
    Assert-HcMirrorRejected { Install-HcLanMirror @hcInstall } 'signed source mismatch rejected before activation'
    [IO.File]::WriteAllBytes($hcManifestPath, $hcOriginalManifestBytes)
    $hcExePath = Join-Path $hcInstall.Directory 'HappyCheats.exe'; $hcExeBytes = [IO.File]::ReadAllBytes($hcExePath)
    [IO.File]::WriteAllBytes($hcExePath, [byte[]](6,5,4,3,2,1))
    Assert-HcMirrorRejected { Install-HcLanMirror @hcInstall } 'same-size tampered standalone executable rejected'
    Assert-HcMirrorTest ((Get-FileHash -LiteralPath $hcPublicFeed).Hash -ceq $hcFeedHash) 'failed verification keeps active feed unchanged'
    [IO.File]::WriteAllBytes($hcExePath, $hcExeBytes)
    $hcNewer = New-HcMirrorTestFeed $hcConfig.BaseUrl; $hcNewer.version='1.2.4'
    Write-HcMirrorTestSigned -Path $hcPublicFeed -Payload $hcNewer
    Assert-HcMirrorRejected { Install-HcLanMirror @hcInstall } 'stale canonical mirror cannot downgrade a newer local signed release'
    $hcConflicting = New-HcMirrorTestFeed $hcConfig.BaseUrl; $hcConflicting.changelogEnglish='different bytes'
    Write-HcMirrorTestSigned -Path $hcPublicFeed -Payload $hcConflicting
    Assert-HcMirrorRejected { Install-HcLanMirror @hcInstall } 'same-version signed feed conflict is preserved'
    Copy-Item -LiteralPath (Join-Path $hcInstall.Directory 'latest-lan-59.json') -Destination $hcPublicFeed -Force
    $hcPublishedArchive = Join-Path $hcConfig.PublicDirectory $hcZipName
    [IO.File]::WriteAllBytes($hcPublishedArchive, [byte[]](0,1,2))
    Assert-HcMirrorRejected { Install-HcLanMirror @hcInstall } 'existing immutable ZIP conflict cannot be overwritten'
    Assert-HcMirrorTest ((Get-Item -LiteralPath $hcPublishedArchive).Length -eq 3 -and (Get-FileHash -LiteralPath $hcPublicFeed).Hash -ceq $hcFeedHash) 'archive conflict and current feed are preserved'
    Copy-Item -LiteralPath (Join-Path $hcStageDirectory "github/$hcZipName") -Destination $hcPublishedArchive -Force
    $hcOlder = New-HcMirrorTestFeed $hcConfig.BaseUrl; $hcOlder.version='1.2.2'
    Write-HcMirrorTestSigned -Path $hcPublicFeed -Payload $hcOlder
    $hcOlderHash = (Get-FileHash -LiteralPath $hcPublicFeed).Hash
    [IO.File]::WriteAllText((Join-Path $hcConfig.PublicDirectory 'HappyCheats.exe'), 'old standalone')
    $hcRealAtomicCopy = (Get-Command Copy-HcMirrorAtomic).ScriptBlock
    try {
        function Copy-HcMirrorAtomic([string]$Source,[string]$Destination,[string]$BackupDirectory) {
            if ([IO.Path]::GetFileName($Destination) -ceq 'latest.json') { throw 'Fixture interruption just before feed activation.' }
            & $hcRealAtomicCopy -Source $Source -Destination $Destination -BackupDirectory $BackupDirectory
        }
        Assert-HcMirrorRejected { Install-HcLanMirror @hcInstall } 'feed activation interruption is reported'
        Assert-HcMirrorTest ((Get-FileHash -LiteralPath $hcPublicFeed).Hash -ceq $hcOlderHash -and (Get-FileHash -LiteralPath $hcPublishedArchive).Hash -ceq $hcArchiveHash) 'activation interruption preserves old feed and completed immutable archive'
    } finally { Set-Item -Path Function:Copy-HcMirrorAtomic -Value $hcRealAtomicCopy }
    $null = Install-HcLanMirror @hcInstall
    Assert-HcMirrorTest ((Get-FileHash -LiteralPath $hcPublicFeed).Hash -ceq $hcFeedHash) 'retry completes from identical verified bytes after activation interruption'
    $hcLockPath = Join-Path $hcTestRoot 'publish.lock'
    $hcLock = [IO.File]::Open($hcLockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { Assert-HcMirrorRejected { $hcOther = [IO.File]::Open($hcLockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::Write,[IO.FileShare]::None); $hcOther.Dispose() } 'publisher and sync exclusive lock excludes simultaneous local activation' } finally { $hcLock.Dispose() }
    # Load only the owned download function, not the sync entry point. A fake
    # HTTP client exercises real streamed size/hash handling without any network.
    $hcTokens = $null; $hcErrors = $null
    $hcSyncAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Sync-LanUpdate.ps1'),[ref]$hcTokens,[ref]$hcErrors)
    $hcDownloadDefinition = $hcSyncAst.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Receive-HcMirrorAsset'},$true)
    . ([scriptblock]::Create($hcDownloadDefinition.Extent.Text))
    $hcClient = [pscustomobject]@{ Bytes=[byte[]](4,3,2,1); ResponseLocation=$null }
    $hcClient | Add-Member -MemberType ScriptMethod -Name GetAsync -Value {
        param($Address,$Completion)
        $hcResponse = [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::OK)
        $hcResponse.Content = [Net.Http.ByteArrayContent]::new([byte[]]$this.Bytes)
        if ($this.ResponseLocation) { $hcResponse.StatusCode=[Net.HttpStatusCode]::Found; $hcResponse.Headers.Location=[Uri]$this.ResponseLocation }
        return [Threading.Tasks.Task]::FromResult($hcResponse)
    }
    $hcDownloadPath = Join-Path $hcTestRoot 'downloaded.zip'
    $hcExpectedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]](1,2,3,4))).ToLowerInvariant()
    $hcDownloadArgs = @{ Url='https://github.com/flazedude/happycheats/releases/download/v1.2.3/HappyCheats-1.2.3-win-x64.zip';Destination=$hcDownloadPath;MaximumBytes=4;ExpectedBytes=4;ExpectedSha256=$hcExpectedHash }
    Assert-HcMirrorRejected { Receive-HcMirrorAsset @hcDownloadArgs } 'same-size corrupted transfer is rejected before committing the cache name'
    Assert-HcMirrorTest (!(Test-Path -LiteralPath $hcDownloadPath) -and @(Get-ChildItem -LiteralPath $hcTestRoot -Filter 'downloaded.zip.*.tmp').Count -eq 0) 'failed hash leaves no poisoned immutable cache or partial file'
    $hcClient.Bytes=[byte[]](1,2,3,4)
    Receive-HcMirrorAsset @hcDownloadArgs
    Assert-HcMirrorTest ((Get-FileHash -LiteralPath $hcDownloadPath).Hash -ieq $hcExpectedHash) 'a later correct transfer retries successfully'
    $hcDownloadArgs.Destination = Join-Path $hcTestRoot 'redirect.zip'; $hcClient.ResponseLocation='http://192.168.2.1/asset'
    Assert-HcMirrorRejected { Receive-HcMirrorAsset @hcDownloadArgs } 'HTTPS to LAN redirect is rejected'
    $hcClient.ResponseLocation='https://unrelated.example/asset'
    Assert-HcMirrorRejected { Receive-HcMirrorAsset @hcDownloadArgs } 'unrelated HTTPS download host is rejected'
    Write-Host "LAN mirror offline checks passed: $hcCount. No network, live keys or publication used."
} finally {
    $hcKey.Dispose()
    $hcAbsolute = [IO.Path]::GetFullPath($hcTestRoot)
    $hcPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (!$hcAbsolute.StartsWith($hcPrefix,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($hcAbsolute).StartsWith('HappyCheats-Mirror-Tests-')) { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $hcAbsolute) { Remove-Item -LiteralPath $hcAbsolute -Recurse -Force }
}
