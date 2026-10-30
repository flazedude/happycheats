[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GitHubRelease.Common.ps1')
$hcTestRoot = Join-Path ([IO.Path]::GetTempPath()) ('HappyCheats-Publish-Tests-' + [Guid]::NewGuid().ToString('N'))
$hcStagePath = Join-Path $hcTestRoot 'stage'
$hcTestCount = 0
$hcTestKey = [Security.Cryptography.RSA]::Create(2048)
function Assert-HcTest([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "Publishing regression failed: $Message" }
    $script:hcTestCount++
}
function Assert-HcRejected([scriptblock]$Action, [string]$Message) {
    $hcRejected = $false
    try { $null = & $Action } catch { $hcRejected = $true }
    Assert-HcTest $hcRejected $Message
}
function Write-HcTestFeed([string]$Target, [string]$HashOverride) {
    $hcPackagePath = Join-Path $hcStagePath 'github/HappyCheats-0.0.1-win-x64.zip'
    $hcBase = if ($Target -eq 'lan') { 'http://192.168.2.76:17845/happycheats' } else { 'https://github.com/example/happycheats/releases/download/v0.0.1' }
    $hcDigest = if ($HashOverride) { $HashOverride } else { (Get-FileHash -LiteralPath $hcPackagePath).Hash }
    $hcManifest = [ordered]@{ schemaVersion=1; version='0.0.1'; channel='stable'; publishedAt='2026-10-24T00:00:00Z'; packageUrl=($hcBase+'/HappyCheats-0.0.1-win-x64.zip'); packageSize=(Get-Item $hcPackagePath).Length; sha256=$hcDigest; changelogEnglish='Fixture'; changelogChinese='测试'; newGames=@() }
    $hcBytes = [Text.Encoding]::UTF8.GetBytes(($hcManifest | ConvertTo-Json -Compress))
    $hcSignature = $hcTestKey.SignData($hcBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    @{ payload=[Convert]::ToBase64String($hcBytes); signature=[Convert]::ToBase64String($hcSignature) } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hcStagePath "$Target/latest.json") -Encoding utf8NoBOM
}
function Save-HcTestStage([switch]$GitHubOnly) {
    $hcFiles = @(Get-ChildItem -LiteralPath $hcStagePath -File -Recurse | Where-Object { $_.Name -ne 'release.json' -and (!$GitHubOnly -or $_.Directory.Name -ne 'lan') } | ForEach-Object {
        $hcRel = [IO.Path]::GetRelativePath($hcStagePath, $_.FullName).Replace('\','/')
        [ordered]@{ path=$hcRel; size=$_.Length; sha256=(Get-FileHash $_.FullName).Hash; upload=(!$hcRel.StartsWith('lan/')) }
    })
    $hcMetadata = [ordered]@{ version='0.0.1'; repository='example/happycheats'; commit=('a'*40); lanBaseUrl='http://192.168.2.76:17845/happycheats'; files=$hcFiles }
    if ($GitHubOnly) { $hcMetadata.publishLan = $false; $hcMetadata.lanBaseUrl = $null }
    $hcMetadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $hcStagePath 'release.json') -Encoding utf8NoBOM
}
function Set-HcTestPackageSettings($Settings) {
    $hcPackage = [IO.Compression.ZipFile]::Open((Join-Path $hcStagePath 'github/HappyCheats-0.0.1-win-x64.zip'), [IO.Compression.ZipArchiveMode]::Update)
    try {
        foreach ($hcName in @('update-settings.json','update-defaults.json')) {
            $hcPackage.GetEntry('HappyCheats/' + $hcName).Delete()
            $hcWriter = [IO.StreamWriter]::new($hcPackage.CreateEntry('HappyCheats/' + $hcName).Open())
            try { $hcWriter.Write(($Settings | ConvertTo-Json)) } finally { $hcWriter.Dispose() }
        }
    } finally { $hcPackage.Dispose() }
    Write-HcTestFeed 'github'
}
try {
    New-Item -ItemType Directory -Path (Join-Path $hcStagePath 'github'),(Join-Path $hcStagePath 'lan'),(Join-Path $hcStagePath 'assets'),(Join-Path $hcTestRoot 'package/HappyCheats') -Force | Out-Null
    $hcPublicPath = Join-Path $hcTestRoot 'public.pem'
    [IO.File]::WriteAllText($hcPublicPath, $hcTestKey.ExportSubjectPublicKeyInfoPem())
    $hcConfig = [pscustomobject]@{ BaseUrl='http://192.168.2.76:17845/happycheats'; PublicKeyPath=$hcPublicPath }
    $hcSettings = @{ feedUrl=($hcConfig.BaseUrl+'/latest.json'); fallbackFeedUrl='https://github.com/example/happycheats/releases/latest/download/latest.json'; publicKeyPem=$hcTestKey.ExportSubjectPublicKeyInfoPem(); channel='stable' } | ConvertTo-Json
    foreach ($hcName in @('update-settings.json','update-defaults.json')) { [IO.File]::WriteAllText((Join-Path $hcTestRoot "package/HappyCheats/$hcName"), $hcSettings) }
    @{ version='0.0.1'; commit=('a'*40) } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hcTestRoot 'package/HappyCheats/release-source.json') -Encoding utf8NoBOM
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $hcTestRoot 'package'), (Join-Path $hcStagePath 'github/HappyCheats-0.0.1-win-x64.zip'))
    Copy-Item -LiteralPath (Join-Path $hcStagePath 'github/HappyCheats-0.0.1-win-x64.zip') -Destination (Join-Path $hcStagePath 'lan/HappyCheats-0.0.1-win-x64.zip')
    [IO.File]::WriteAllBytes((Join-Path $hcStagePath 'assets/HappyCheats.exe'), [byte[]](1,2,3,4,5))
    Copy-Item -LiteralPath (Join-Path $hcStagePath 'assets/HappyCheats.exe') -Destination (Join-Path $hcStagePath 'assets/HappyCheats-0.0.1.exe')
    $hcSingle = [IO.Compression.ZipFile]::Open((Join-Path $hcStagePath 'assets/HappyCheats-0.0.1-win-x64-single-exe.zip'), [IO.Compression.ZipArchiveMode]::Create)
    try { $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($hcSingle, (Join-Path $hcStagePath 'assets/HappyCheats.exe'), 'HappyCheats.exe') } finally { $hcSingle.Dispose() }
    Write-HcTestFeed 'lan'; Write-HcTestFeed 'github'; Save-HcTestStage
    $hcStage = Get-HcReleaseStage -Directory $hcStagePath -Version '0.0.1' -Repository 'example/happycheats'
    Assert-HcReleaseStageTrust -Directory $hcStagePath -Stage $hcStage -Config $hcConfig
    Assert-HcTest ($hcStage.files.Count -eq 7) 'valid signed dual-feed stage and complete standalone distribution'
    Assert-HcRejected { Get-HcReleaseStage -Directory $hcStagePath -Version '0.0.2' -Repository 'example/happycheats' } 'wrong staged version'
    Assert-HcRejected { Get-HcReleaseStage -Directory $hcStagePath -Version '0.0.1' -Repository 'other/repo' } 'wrong staged repository'
    $hcStage.commit = 'b'*40
    Assert-HcRejected { Assert-HcReleaseStageTrust -Directory $hcStagePath -Stage $hcStage -Config $hcConfig } 'packaged source differs from reviewed source'
    $hcStage.commit = 'a'*40
    Write-HcTestFeed 'github' ('f'*64)
    Assert-HcRejected { Assert-HcReleaseStageTrust -Directory $hcStagePath -Stage $hcStage -Config $hcConfig } 'valid signature with wrong package identity'
    Write-HcTestFeed 'github'
    $hcEnvelopePath = Join-Path $hcStagePath 'lan/latest.json'
    $hcEnvelope = Get-Content $hcEnvelopePath -Raw | ConvertFrom-Json
    $hcEnvelope.signature = [Convert]::ToBase64String([byte[]]::new(256))
    $hcEnvelope | ConvertTo-Json | Set-Content $hcEnvelopePath
    Assert-HcRejected { Assert-HcReleaseStageTrust -Directory $hcStagePath -Stage $hcStage -Config $hcConfig } 'tampered feed signature'
    Write-HcTestFeed 'lan'; Save-HcTestStage
    [IO.File]::WriteAllBytes((Join-Path $hcStagePath 'assets/HappyCheats-0.0.1.exe'), [byte[]](5,4,3,2,1))
    Assert-HcRejected { Get-HcReleaseStage -Directory $hcStagePath -Version '0.0.1' -Repository 'example/happycheats' } 'same-size modified artifact fails resume'
    Assert-HcRejected { Assert-HcReleaseStageTrust -Directory $hcStagePath -Stage $hcStage -Config $hcConfig } 'standalone copies must match'
    Copy-Item -LiteralPath (Join-Path $hcStagePath 'assets/HappyCheats.exe') -Destination (Join-Path $hcStagePath 'assets/HappyCheats-0.0.1.exe') -Force
    $hcStage.files[0].path = '../public.pem'
    $hcStage | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $hcStagePath 'release.json')
    Assert-HcRejected { Get-HcReleaseStage -Directory $hcStagePath -Version '0.0.1' -Repository 'example/happycheats' } 'stage traversal rejected'
    Save-HcTestStage
    $hcStage = Get-Content (Join-Path $hcStagePath 'release.json') -Raw | ConvertFrom-Json
    $hcStage.files += $hcStage.files[0]
    $hcStage | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $hcStagePath 'release.json')
    Assert-HcRejected { Get-HcReleaseStage -Directory $hcStagePath -Version '0.0.1' -Repository 'example/happycheats' } 'duplicate staged paths rejected'
    Save-HcTestStage

    # New releases need only the preserved signing key, without a running LAN host
    # or any LAN publication paths. Old dual-host stages above still resume safely.
    $hcGitHubSettings = $hcSettings | ConvertFrom-Json
    $hcGitHubSettings.feedUrl = $hcGitHubSettings.fallbackFeedUrl
    $hcGitHubSettings.fallbackFeedUrl = $null
    Set-HcTestPackageSettings $hcGitHubSettings
    Save-HcTestStage -GitHubOnly
    $hcStage = Get-HcReleaseStage -Directory $hcStagePath -Version '0.0.1' -Repository 'example/happycheats'
    $hcSigningOnlyConfig = [pscustomobject]@{ PublicKeyPath=$hcPublicPath }
    Assert-HcReleaseStageTrust -Directory $hcStagePath -Stage $hcStage -Config $hcSigningOnlyConfig
    Assert-HcTest ($hcStage.files.Count -eq 5 -and !(Test-HcReleaseIncludesLan $hcStage)) 'signed GitHub-only stage works without LAN address or public directory'
    $hcStage.publishLan = 'false'
    Assert-HcRejected { Test-HcReleaseIncludesLan $hcStage } 'publication target cannot be an ambiguous string'
    $hcStage.publishLan = $false
    $hcStage.lanBaseUrl = $hcConfig.BaseUrl
    Assert-HcRejected { Assert-HcReleaseStageTrust -Directory $hcStagePath -Stage $hcStage -Config $hcConfig } 'GitHub-only stage cannot retain a LAN target'
    $hcStage.lanBaseUrl = $null
    $hcGitHubSettings.fallbackFeedUrl = $hcConfig.BaseUrl + '/latest.json'
    Set-HcTestPackageSettings $hcGitHubSettings
    Assert-HcRejected { Assert-HcReleaseStageTrust -Directory $hcStagePath -Stage $hcStage -Config $hcSigningOnlyConfig } 'GitHub-only starter cannot keep LAN fallback'
    $hcGitHubSettings.fallbackFeedUrl = $null
    $hcGitHubSettings.feedUrl = $hcConfig.BaseUrl + '/latest.json'
    Set-HcTestPackageSettings $hcGitHubSettings
    Assert-HcRejected { Assert-HcReleaseStageTrust -Directory $hcStagePath -Stage $hcStage -Config $hcSigningOnlyConfig } 'GitHub-only starter cannot keep LAN primary'
    $hcGitHubSettings.feedUrl = 'https://github.com/example/happycheats/releases/latest/download/latest.json'
    Set-HcTestPackageSettings $hcGitHubSettings
    Save-HcTestStage -GitHubOnly
    $hcStage = Get-HcReleaseStage -Directory $hcStagePath -Version '0.0.1' -Repository 'example/happycheats'
    Assert-HcReleaseStageTrust -Directory $hcStagePath -Stage $hcStage -Config $hcSigningOnlyConfig
    $hcGitHubSettings.channel = 'preview'
    Set-HcTestPackageSettings $hcGitHubSettings
    Assert-HcRejected { Assert-HcReleaseStageTrust -Directory $hcStagePath -Stage $hcStage -Config $hcSigningOnlyConfig } 'GitHub-only starter still requires stable channel'

    $hcExePath = Join-Path $hcStagePath 'assets/HappyCheats.exe'
    $hcAsset = [pscustomobject]@{ name='HappyCheats.exe'; state='uploaded'; size=5; digest=('sha256:'+(Get-FileHash $hcExePath).Hash.ToLowerInvariant()) }
    Assert-HcGitHubAsset -Client $null -Repository 'example/happycheats' -Asset $hcAsset -Path $hcExePath
    Assert-HcTest $true 'matching uploaded asset can resume without upload'
    $hcAsset.digest = 'sha256:' + ('f'*64)
    Assert-HcRejected { Assert-HcGitHubAsset -Client $null -Repository 'example/happycheats' -Asset $hcAsset -Path $hcExePath } 'conflicting GitHub digest rejected'
    $hcAsset.state = 'starter'
    Assert-HcRejected { Assert-HcGitHubAsset -Client $null -Repository 'example/happycheats' -Asset $hcAsset -Path $hcExePath } 'incomplete upload cannot pass verification'

    # API responses are fully in-memory; these tests never access GitHub or credentials.
    function Invoke-HcGitHub { param($Client, $Method, $Path, $Body, [switch]$AllowNotFound) $script:hcApiCalls.Add($Path); return $script:hcApiFixtures[$Path] }
    $hcRepoPath = '/repos/example/happycheats'
    $script:hcApiFixtures = @{}
    $script:hcApiCalls = [Collections.Generic.List[string]]::new()
    $script:hcApiFixtures[$hcRepoPath] = [pscustomobject]@{ private=$false; archived=$false; permissions=@{push=$true} }
    $hcDraft = [pscustomobject]@{ draft=$true; target_commitish=('a'*40); id=123; tag_name='v0.0.1'; assets=@() }
    $script:hcApiFixtures["$hcRepoPath/releases/tags/v0.0.1"] = $hcDraft
    $hcActualDraft = Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40)
    Assert-HcTest ($hcActualDraft.id -eq 123) 'matching interrupted draft can resume'
    $hcDraft.draft = $false
    Assert-HcRejected { Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40) } 'published release overwrite rejected'
    $hcDraft.draft = $true; $hcDraft.target_commitish = 'b'*40
    Assert-HcRejected { Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40) } 'draft source drift rejected'
    $hcDraft.target_commitish = 'a'*40
    $script:hcApiFixtures[$hcRepoPath].private = $true
    Assert-HcRejected { Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40) } 'private repository rejected before build'
    $script:hcApiFixtures[$hcRepoPath].private = $false
    $script:hcApiFixtures["$hcRepoPath/releases/latest"] = [pscustomobject]@{tag_name='v0.0.2'}
    Assert-HcRejected { Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40) } 'older release cannot become latest'
    $script:hcApiFixtures.Remove("$hcRepoPath/releases/latest")
    $script:hcApiFixtures["$hcRepoPath/git/ref/tags/v0.0.1"] = [pscustomobject]@{object=@{type='commit';sha=('b'*40)}}
    Assert-HcRejected { Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40) } 'conflicting remote tag rejected'
    $script:hcApiFixtures.Remove("$hcRepoPath/git/ref/tags/v0.0.1")

    # A draft is invisible at /releases/tags but is visible in list/by-ID responses.
    $script:hcApiFixtures.Remove("$hcRepoPath/releases/tags/v0.0.1")
    $script:hcApiFixtures["$hcRepoPath/releases?per_page=100&page=1"] = @($hcDraft)
    $script:hcApiFixtures["$hcRepoPath/releases/123"] = $hcDraft
    $hcDiscovered = Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40)
    Assert-HcTest ($hcDiscovered.id -eq 123) 'tag 404 discovers existing authenticated draft for retry'
    $hcDraft.assets = @([pscustomobject]@{name='latest.json';state='uploaded'})
    $script:hcApiCalls.Clear()
    $hcRefreshed = Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40) -ReleaseId 123
    Assert-HcTest ($hcRefreshed.assets.Count -eq 1 -and !$script:hcApiCalls.Contains("$hcRepoPath/releases/tags/v0.0.1")) 'post-upload refresh uses stable ID and sees prepared assets'
    $script:hcApiFixtures["$hcRepoPath/releases?per_page=100&page=1"] = @(1..100 | ForEach-Object { [pscustomobject]@{id=(1000+$_);tag_name="v9.0.$_";draft=$false} })
    $script:hcApiFixtures["$hcRepoPath/releases?per_page=100&page=2"] = @($hcDraft)
    $hcPaginated = Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40)
    Assert-HcTest ($hcPaginated.id -eq 123) 'draft discovery follows later release pages'
    $hcDraft.tag_name = 'v0.0.2'
    Assert-HcRejected { Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40) -ReleaseId 123 } 'refreshed ID cannot silently switch release tags'
    $hcDraft.tag_name = 'v0.0.1'; $hcDraft.draft = $false
    Assert-HcRejected { Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40) -ReleaseId 123 } 'draft published concurrently is rejected on refresh'
    $hcDraft.draft = $true; $hcDraft.target_commitish = 'b'*40
    Assert-HcRejected { Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40) -ReleaseId 123 } 'refreshed draft source drift rejected'
    $script:hcApiFixtures.Remove("$hcRepoPath/releases/123")
    Assert-HcRejected { Assert-HcGitHubPreflight -Client $null -Repository 'example/happycheats' -Version '0.0.1' -SourceCommit ('a'*40) -ReleaseId 123 } 'missing prepared release ID fails explicitly'

    # Mock Git at the process boundary: push chatter must never enter the returned SHA.
    $script:hcGitDirty = $false; $script:hcGitPushFails = $false; $script:hcPushCount = 0
    $script:hcGitRemoteCommit = 'a'*40
    function git {
        $global:LASTEXITCODE = 0
        switch ($args[2]) {
            'remote' { return 'https://github.com/example/happycheats.git' }
            'status' { if ($script:hcGitDirty) { return '?? unrelated-work.txt' }; return }
            'rev-parse' { return ('a'*40) }
            'symbolic-ref' { return 'main' }
            'push' {
                $script:hcPushCount++
                if ($script:hcGitPushFails) { $global:LASTEXITCODE = 1 }
                return @('To https://github.com/example/happycheats.git','up to date','Done')
            }
            'ls-remote' { return ($script:hcGitRemoteCommit + "`trefs/heads/main") }
            default { throw 'Unexpected Git call in isolated test.' }
        }
    }
    $hcCommitResult = Assert-HcReleaseSource -Root $hcTestRoot -Repository 'example/happycheats' -ExpectedCommit ('a'*40) -Push 6>$null
    Assert-HcTest ($hcCommitResult -is [string] -and $hcCommitResult -ceq ('a'*40)) 'git push output cannot contaminate source commit'
    $script:hcGitDirty = $true
    Assert-HcRejected { Assert-HcReleaseSource -Root $hcTestRoot -Repository 'example/happycheats' -Push } 'untracked source blocks normal publication'
    Assert-HcTest ($script:hcPushCount -eq 1) 'dirty source is rejected before pushing'
    $script:hcGitDirty = $false
    Assert-HcRejected { Assert-HcReleaseSource -Root $hcTestRoot -Repository 'example/happycheats' -ExpectedCommit ('b'*40) } 'source HEAD drift blocks publication'
    $script:hcGitRemoteCommit = 'b'*40
    Assert-HcRejected { Assert-HcReleaseSource -Root $hcTestRoot -Repository 'example/happycheats' } 'remote branch must match local source'
    $script:hcGitRemoteCommit = 'a'*40; $script:hcGitPushFails = $true
    Assert-HcRejected { Assert-HcReleaseSource -Root $hcTestRoot -Repository 'example/happycheats' -Push 6>$null } 'failed push aborts before release publication'
    $global:LASTEXITCODE = 0
    Write-Host "GitHub publishing offline checks passed: $hcTestCount. No credentials, network, games or release publishing used."
} finally {
    $hcTestKey.Dispose()
    $hcResolved = [IO.Path]::GetFullPath($hcTestRoot)
    $hcTempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if ($hcResolved.StartsWith($hcTempPrefix, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($hcResolved).StartsWith('HappyCheats-Publish-Tests-') -and (Test-Path -LiteralPath $hcResolved)) { Remove-Item -LiteralPath $hcResolved -Recurse -Force }
}
