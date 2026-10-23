# Shared publishing helpers. Credentials remain in memory and never enter arguments or logs.
function New-HcGitHubClient {
    $hcToken = $null
    $hcCredentialProcess = $null
    try {
        if (Get-Command gh -ErrorAction SilentlyContinue) {
            $hcToken = (& gh auth token --hostname github.com 2>$null | Out-String).Trim()
            if ($LASTEXITCODE -ne 0) { $hcToken = $null }
        }
        if (!$hcToken) {
            $hcStart = [Diagnostics.ProcessStartInfo]::new('git')
            $hcStart.ArgumentList.Add('credential'); $hcStart.ArgumentList.Add('fill')
            $hcStart.UseShellExecute = $false; $hcStart.CreateNoWindow = $true
            $hcStart.RedirectStandardInput = $true; $hcStart.RedirectStandardOutput = $true; $hcStart.RedirectStandardError = $true
            $hcStart.Environment['GIT_TERMINAL_PROMPT'] = '0'; $hcStart.Environment['GCM_INTERACTIVE'] = 'never'
            $hcCredentialProcess = [Diagnostics.Process]::Start($hcStart)
            $hcCredentialProcess.StandardInput.Write("protocol=https`nhost=github.com`n`n")
            $hcCredentialProcess.StandardInput.Close()
            $hcOutputTask = $hcCredentialProcess.StandardOutput.ReadToEndAsync()
            $hcErrorTask = $hcCredentialProcess.StandardError.ReadToEndAsync()
            if (!$hcCredentialProcess.WaitForExit(30000)) { $hcCredentialProcess.Kill(); throw 'GitHub credential lookup timed out.' }
            $hcCredentialText = $hcOutputTask.GetAwaiter().GetResult()
            $null = $hcErrorTask.GetAwaiter().GetResult()
            if ($hcCredentialProcess.ExitCode -eq 0) {
                foreach ($hcLine in ($hcCredentialText -split "`n")) {
                    if ($hcLine.StartsWith('password=')) { $hcToken = $hcLine.Substring(9).TrimEnd("`r") }
                }
            }
            $hcCredentialText = $null
        }
        if (!$hcToken) { throw 'GitHub authentication unavailable. Sign in with gh auth login or the existing Git credential manager.' }
        $hcClient = [Net.Http.HttpClient]::new()
        $hcClient.Timeout = [TimeSpan]::FromMinutes(30)
        $hcClient.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $hcToken)
        $hcClient.DefaultRequestHeaders.UserAgent.ParseAdd('HappyCheats-Release/1.0')
        $hcClient.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
        $hcClient.DefaultRequestHeaders.Add('X-GitHub-Api-Version', '2022-11-28')
        return $hcClient
    } finally {
        $hcToken = $null
        if ($hcCredentialProcess) { $hcCredentialProcess.Dispose() }
    }
}

function Invoke-HcGitHub {
    param([Net.Http.HttpClient]$Client, [string]$Method = 'GET', [Parameter(Mandatory)][string]$Path, $Body, [switch]$AllowNotFound)
    if (!$Path.StartsWith('/repos/')) { throw 'Unexpected GitHub API path.' }
    $hcRequest = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), 'https://api.github.com' + $Path)
    $hcResponse = $null
    try {
        if ($null -ne $Body) { $hcRequest.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 12 -Compress), [Text.Encoding]::UTF8, 'application/json') }
        $hcResponse = $Client.SendAsync($hcRequest).GetAwaiter().GetResult()
        if ($AllowNotFound -and [int]$hcResponse.StatusCode -eq 404) { return $null }
        if (!$hcResponse.IsSuccessStatusCode) { throw "GitHub $Method failed (HTTP $([int]$hcResponse.StatusCode)); no credentials were logged." }
        $hcText = $hcResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ($hcText) { return ($hcText | ConvertFrom-Json) }
    } finally { if ($hcResponse) { $hcResponse.Dispose() }; $hcRequest.Dispose() }
}

function Assert-HcReleaseSource {
    param([string]$Root, [string]$Repository, [string]$ExpectedCommit, [switch]$Push)
    if ($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'GitHubRepository must be owner/repository.' }
    $hcRemote = & git -C $Root remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $hcRemote -notmatch ('^(https://github\.com/|git@github\.com:)' + [Regex]::Escape($Repository) + '(\.git)?/?$')) {
        throw 'The origin remote must be the selected GitHub repository; credential-bearing URLs are unsupported.'
    }
    $hcStatus = & git -C $Root status --porcelain --untracked-files=all
    if ($LASTEXITCODE -ne 0 -or $hcStatus) { throw 'Release source must be clean. Review and commit intended changes first; publishing never stages unrelated work.' }
    $hcCommit = & git -C $Root rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $hcCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot determine the release source commit.' }
    if ($ExpectedCommit -and $hcCommit -cne $ExpectedCommit) { throw 'Source HEAD changed after the release was prepared. Do not publish a different commit under this version.' }
    $hcBranch = & git -C $Root symbolic-ref --quiet --short HEAD
    if ($LASTEXITCODE -ne 0 -or !$hcBranch) { throw 'Publish from a named branch with the reviewed release commit.' }
    if ($Push) {
        & git -C $Root push --porcelain origin ($hcCommit + ':refs/heads/' + $hcBranch) | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Pushing the reviewed release commit failed. No release was published.' }
    }
    $hcRemoteHead = & git -C $Root ls-remote --exit-code origin ('refs/heads/' + $hcBranch)
    if ($LASTEXITCODE -ne 0 -or !$hcRemoteHead -or ($hcRemoteHead -split '\s+')[0] -cne $hcCommit) { throw 'GitHub branch HEAD must match the local release commit. Push the reviewed source first.' }
    return $hcCommit
}

function Assert-HcGitHubPreflight {
    param([Net.Http.HttpClient]$Client, [string]$Repository, [string]$Version, [string]$SourceCommit)
    if ($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Invalid repository or release version.' }
    $hcRepo = Invoke-HcGitHub -Client $Client -Path "/repos/$Repository"
    if ($hcRepo.private -or $hcRepo.archived -or !$hcRepo.permissions.push) { throw 'GitHub releases require a public, writable, unarchived repository. No visibility change is made by this script.' }
    $hcRelease = Invoke-HcGitHub -Client $Client -Path "/repos/$Repository/releases/tags/v$Version" -AllowNotFound
    if ($hcRelease -and !$hcRelease.draft) { throw "GitHub v$Version is already published; immutable releases cannot be overwritten." }
    if ($hcRelease -and $SourceCommit -and $hcRelease.target_commitish -cne $SourceCommit) { throw 'Existing draft belongs to a different source commit.' }
    $hcLatest = Invoke-HcGitHub -Client $Client -Path "/repos/$Repository/releases/latest" -AllowNotFound
    if ($hcLatest -and $hcLatest.tag_name -match '^v?(\d+\.\d+\.\d+)$' -and [Version]$Version -le [Version]$Matches[1]) { throw "Version must be newer than the latest GitHub release $($hcLatest.tag_name)." }
    $hcTag = Invoke-HcGitHub -Client $Client -Path "/repos/$Repository/git/ref/tags/v$Version" -AllowNotFound
    if ($hcTag) {
        $hcObject = $hcTag.object
        for ($hcDepth = 0; $hcObject.type -eq 'tag' -and $hcDepth -lt 8; $hcDepth++) {
            $hcObject = (Invoke-HcGitHub -Client $Client -Path "/repos/$Repository/git/tags/$($hcObject.sha)").object
        }
        if ($hcObject.type -ne 'commit' -or ($SourceCommit -and $hcObject.sha -cne $SourceCommit)) { throw 'Existing version tag does not match the release source commit.' }
    }
    return $hcRelease
}

function Get-HcReleaseStage {
    param([string]$Directory, [string]$Version, [string]$Repository)
    $hcStage = Get-Content -LiteralPath (Join-Path $Directory 'release.json') -Raw | ConvertFrom-Json
    if ($hcStage.version -cne $Version -or $hcStage.repository -cne $Repository -or $hcStage.commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Release stage identity does not match the requested release.' }
    $hcPrefix = [IO.Path]::GetFullPath($Directory).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (@($hcStage.files).Count -eq 0) { throw 'Release stage has no artifacts.' }
    $hcSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($hcFile in $hcStage.files) {
        $hcPath = [IO.Path]::GetFullPath((Join-Path $Directory $hcFile.path))
        if (!$hcPath.StartsWith($hcPrefix, [StringComparison]::OrdinalIgnoreCase) -or !$hcSeen.Add($hcPath) -or !(Test-Path -LiteralPath $hcPath -PathType Leaf)) { throw 'Release stage contains an invalid, duplicate or missing artifact.' }
        if ((Get-Item -LiteralPath $hcPath).Length -ne $hcFile.size -or (Get-FileHash -LiteralPath $hcPath).Hash -ine $hcFile.sha256) { throw "Release artifact changed: $($hcFile.path). Preserve the stage and choose a new release version." }
    }
    return $hcStage
}

function Assert-HcReleaseStageTrust {
    param([string]$Directory, $Stage, $Config)
    if ($Stage.lanBaseUrl -cne $Config.BaseUrl) { throw 'The staged LAN address differs from the saved server.' }
    $hcVersion = $Stage.version
    $hcZipName = "HappyCheats-$hcVersion-win-x64.zip"
    $hcZipPath = Join-Path $Directory "github/$hcZipName"
    $hcZipHash = (Get-FileHash -LiteralPath $hcZipPath).Hash
    $hcZipBytes = (Get-Item -LiteralPath $hcZipPath).Length
    $hcKey = [Security.Cryptography.RSA]::Create()
    try {
        $hcKey.ImportFromPem([IO.File]::ReadAllText($Config.PublicKeyPath))
        foreach ($hcTarget in @(@('lan',$Config.BaseUrl), @('github',"https://github.com/$($Stage.repository)/releases/download/v$hcVersion"))) {
            $hcEnvelope = Get-Content -LiteralPath (Join-Path $Directory ($hcTarget[0] + '/latest.json')) -Raw | ConvertFrom-Json
            $hcPayload = [Convert]::FromBase64String($hcEnvelope.payload)
            if (!$hcKey.VerifyData($hcPayload, [Convert]::FromBase64String($hcEnvelope.signature), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw 'Staged release signature is invalid.' }
            $hcFeed = [Text.Encoding]::UTF8.GetString($hcPayload) | ConvertFrom-Json
            if ($hcFeed.version -cne $hcVersion -or $hcFeed.channel -cne 'stable' -or $hcFeed.packageSize -ne $hcZipBytes -or $hcFeed.sha256 -ine $hcZipHash -or $hcFeed.packageUrl -cne ($hcTarget[1] + '/' + $hcZipName)) { throw 'Signed release identity or download URL does not match the staged package.' }
        }
        if ((Get-FileHash -LiteralPath (Join-Path $Directory "lan/$hcZipName")).Hash -cne $hcZipHash) { throw 'LAN and GitHub update packages must be byte-identical.' }
        $hcZip = [IO.Compression.ZipFile]::OpenRead($hcZipPath)
        try {
            function Read-HcStageZipText([string]$Name) {
                $hcEntry = $hcZip.GetEntry('HappyCheats/' + $Name)
                if (!$hcEntry) { throw "Missing packaged release file: $Name." }
                $hcReader = [IO.StreamReader]::new($hcEntry.Open())
                try { return $hcReader.ReadToEnd() } finally { $hcReader.Dispose() }
            }
            $hcSource = Read-HcStageZipText 'release-source.json' | ConvertFrom-Json
            if ($hcSource.version -cne $hcVersion -or $hcSource.commit -cne $Stage.commit) { throw 'Packaged source commit does not match the release stage.' }
            $hcSettingsText = Read-HcStageZipText 'update-settings.json'
            if ($hcSettingsText -cne (Read-HcStageZipText 'update-defaults.json')) { throw 'Bundled update defaults must match the configured starter.' }
            $hcSettings = $hcSettingsText | ConvertFrom-Json
            if ($hcSettings.feedUrl -cne ($Config.BaseUrl + '/latest.json') -or $hcSettings.fallbackFeedUrl -cne "https://github.com/$($Stage.repository)/releases/latest/download/latest.json" -or $hcSettings.channel -cne 'stable') { throw 'Packaged LAN/GitHub update configuration is incorrect.' }
            $hcEmbeddedKey = [Security.Cryptography.RSA]::Create()
            try {
                $hcEmbeddedKey.ImportFromPem($hcSettings.publicKeyPem)
                if ([Convert]::ToBase64String($hcEmbeddedKey.ExportSubjectPublicKeyInfo()) -cne [Convert]::ToBase64String($hcKey.ExportSubjectPublicKeyInfo())) { throw 'Packaged verification key differs from the established key.' }
            } finally { $hcEmbeddedKey.Dispose() }
        } finally { $hcZip.Dispose() }
        $hcExeHash = (Get-FileHash -LiteralPath (Join-Path $Directory 'assets/HappyCheats.exe')).Hash
        if ((Get-FileHash -LiteralPath (Join-Path $Directory "assets/HappyCheats-$hcVersion.exe")).Hash -cne $hcExeHash) { throw 'Versioned and latest standalone executables differ.' }
        $hcSingleZip = [IO.Compression.ZipFile]::OpenRead((Join-Path $Directory "assets/HappyCheats-$hcVersion-win-x64-single-exe.zip"))
        try {
            if ($hcSingleZip.Entries.Count -ne 1 -or $hcSingleZip.Entries[0].FullName -cne 'HappyCheats.exe') { throw 'The standalone ZIP must contain exactly HappyCheats.exe.' }
            $hcExeStream = $hcSingleZip.Entries[0].Open()
            try { $hcInsideHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($hcExeStream)) } finally { $hcExeStream.Dispose() }
            if ($hcInsideHash -cne $hcExeHash) { throw 'The standalone ZIP contains a different executable.' }
        } finally { $hcSingleZip.Dispose() }
    } finally { $hcKey.Dispose() }
}

function Assert-HcGitHubAsset {
    param([Net.Http.HttpClient]$Client, [string]$Repository, $Asset, [string]$Path)
    $hcInfo = Get-Item -LiteralPath $Path
    $hcHash = (Get-FileHash -LiteralPath $Path).Hash.ToLowerInvariant()
    if ($Asset.state -ne 'uploaded' -or $Asset.size -ne $hcInfo.Length) { throw "Incomplete or conflicting GitHub asset: $($Asset.name). Only failed starter uploads can be retried automatically; preserve published assets." }
    if ($Asset.PSObject.Properties.Name -contains 'digest' -and $Asset.digest) {
        if ($Asset.digest -cne ('sha256:' + $hcHash)) { throw "GitHub asset digest differs: $($Asset.name). Existing assets are never overwritten." }
        return
    }
    # Older GitHub responses may omit digest; verify the actual bytes before resuming.
    $hcRequest = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, "https://api.github.com/repos/$Repository/releases/assets/$($Asset.id)")
    $hcRequest.Headers.Accept.Clear(); $hcRequest.Headers.Accept.ParseAdd('application/octet-stream')
    $hcResponse = $null; $hcStream = $null
    try {
        $hcResponse = $Client.SendAsync($hcRequest, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        if (!$hcResponse.IsSuccessStatusCode) { throw 'Could not verify existing GitHub asset bytes.' }
        $hcStream = $hcResponse.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $hcActual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($hcStream))
        if ($hcActual -ine $hcHash) { throw "GitHub asset checksum differs: $($Asset.name)." }
    } finally { if ($hcStream) { $hcStream.Dispose() }; if ($hcResponse) { $hcResponse.Dispose() }; $hcRequest.Dispose() }
}

function Add-HcGitHubAsset {
    param([Net.Http.HttpClient]$Client, [string]$Repository, [long]$ReleaseId, [string]$Path)
    $hcName = [IO.Path]::GetFileName($Path)
    $hcRequest = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "https://uploads.github.com/repos/$Repository/releases/$ReleaseId/assets?name=$([Uri]::EscapeDataString($hcName))")
    $hcResponse = $null
    try {
        $hcRequest.Content = [Net.Http.StreamContent]::new([IO.File]::OpenRead($Path))
        $hcRequest.Content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')
        $hcResponse = $Client.SendAsync($hcRequest).GetAwaiter().GetResult()
        if (!$hcResponse.IsSuccessStatusCode) { throw "GitHub upload failed for $hcName (HTTP $([int]$hcResponse.StatusCode)). Rerun to verify and resume the draft." }
        return ($hcResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json)
    } finally { if ($hcResponse) { $hcResponse.Dispose() }; $hcRequest.Dispose() }
}
