# Requires PowerShell 7; no SDK, GitHub authentication or private key is used.
[CmdletBinding()]
param([string]$ConfigPath, [string]$GitHubRepository = 'flazedude/happycheats')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LanMirror.Common.ps1')
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcStateRoot = Join-Path $hcRoot 'artifacts/lan-updates'
if (!$ConfigPath) { $ConfigPath = Join-Path $hcStateRoot 'server.json' }
$hcConfig = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$hcFeedName = Get-HcLanFeedAsset $hcConfig.BaseUrl
$null = Get-HcOfficialFeedUrls $GitHubRepository
$hcLock = $null; $hcKey = $null; $hcClient = $null
function Receive-HcMirrorAsset([string]$Url, [string]$Destination, [long]$MaximumBytes, [long]$ExpectedBytes = 0, [string]$ExpectedSha256) {
    Assert-HcMirrorSafePath $Destination
    $hcResponse = $null; $hcInput = $null; $hcOutput = $null
    $hcTemporary = $Destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $hcAddress = [Uri]$Url
        for ($hcRedirect = 0; ; $hcRedirect++) {
            if ($hcAddress.Scheme -cne 'https' -or $hcAddress.UserInfo -or ($hcAddress.Host -cne 'github.com' -and $hcAddress.Host -cne 'release-assets.githubusercontent.com' -and $hcAddress.Host -cne 'objects.githubusercontent.com')) { throw 'Mirror downloads must remain on the pinned GitHub HTTPS hosts.' }
            $hcResponse = $hcClient.GetAsync($hcAddress, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            if ([int]$hcResponse.StatusCode -notin @(301,302,303,307,308)) { break }
            if ($hcRedirect -ge 5 -or !$hcResponse.Headers.Location) { throw 'Too many mirror download redirects.' }
            $hcAddress = [Uri]::new($hcAddress, $hcResponse.Headers.Location)
            $hcResponse.Dispose(); $hcResponse = $null
        }
        if (!$hcResponse.IsSuccessStatusCode) { throw "GitHub mirror download failed (HTTP $([int]$hcResponse.StatusCode))." }
        if ($hcResponse.Content.Headers.ContentLength -gt $MaximumBytes -or ($ExpectedBytes -and $hcResponse.Content.Headers.ContentLength -and $hcResponse.Content.Headers.ContentLength -ne $ExpectedBytes)) { throw 'Mirror download length differs from its signed metadata.' }
        $hcInput = $hcResponse.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $hcOutput = [IO.File]::Open($hcTemporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $hcBuffer = [byte[]]::new(65536); $hcTotal = 0L
        while ($true) {
            $hcTimeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(60))
            try { $hcRead = $hcInput.ReadAsync($hcBuffer, 0, $hcBuffer.Length, $hcTimeout.Token).GetAwaiter().GetResult() }
            finally { $hcTimeout.Dispose() }
            if (!$hcRead) { break }; $hcTotal += $hcRead
            if ($hcTotal -gt $MaximumBytes) { throw 'Mirror download exceeded its signed size limit.' }
            $hcOutput.Write($hcBuffer, 0, $hcRead)
        }
        if ($ExpectedBytes -and $hcTotal -ne $ExpectedBytes) { throw 'Mirror download was truncated.' }
        $hcOutput.Flush($true); $hcOutput.Dispose(); $hcOutput = $null
        if ($ExpectedSha256 -and (Get-FileHash -LiteralPath $hcTemporary -Algorithm SHA256).Hash -ine $ExpectedSha256) { throw 'Mirror download SHA-256 differs from its signed metadata.' }
        [IO.File]::Move($hcTemporary, $Destination, $false)
    } finally {
        if ($hcOutput) { $hcOutput.Dispose() }; if ($hcInput) { $hcInput.Dispose() }; if ($hcResponse) { $hcResponse.Dispose() }
        if (Test-Path -LiteralPath $hcTemporary) { Remove-Item -LiteralPath $hcTemporary -Force }
    }
}
try {
    Assert-HcMirrorSafePath $hcStateRoot
    New-Item -ItemType Directory -Path $hcStateRoot -Force | Out-Null
    Assert-HcMirrorSafePath (Join-Path $hcStateRoot 'publish.lock')
    $hcLock = [IO.File]::Open((Join-Path $hcStateRoot 'publish.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $hcKey = Read-HcMirrorPublicKey $hcConfig.PublicKeyPath
    $hcHandler = [Net.Http.HttpClientHandler]::new(); $hcHandler.AllowAutoRedirect = $false
    $hcClient = [Net.Http.HttpClient]::new($hcHandler); $hcClient.Timeout = [TimeSpan]::FromSeconds(60)
    $hcClient.DefaultRequestHeaders.UserAgent.ParseAdd('HappyCheats-LanMirror/1.0')
    $hcCacheRoot = Join-Path $hcStateRoot 'mirror-cache'
    Assert-HcMirrorSafePath $hcCacheRoot
    New-Item -ItemType Directory -Path $hcCacheRoot -Force | Out-Null
    $hcDiscovery = Join-Path $hcCacheRoot ('mirror-' + [Guid]::NewGuid().ToString('N') + '.json')
    Receive-HcMirrorAsset -Url "https://github.com/$GitHubRepository/releases/latest/download/mirror.json" -Destination $hcDiscovery -MaximumBytes 131072
    $hcManifest = Read-HcMirrorManifest -Path $hcDiscovery -Key $hcKey -Repository $GitHubRepository
    $hcVersionDirectory = Join-Path $hcCacheRoot $hcManifest.version
    Assert-HcMirrorSafePath $hcVersionDirectory
    New-Item -ItemType Directory -Path $hcVersionDirectory -Force | Out-Null
    $hcSavedManifest = Join-Path $hcVersionDirectory 'mirror.json'
    Assert-HcMirrorSafePath $hcSavedManifest
    if (Test-Path -LiteralPath $hcSavedManifest) {
        if ((Get-FileHash -LiteralPath $hcDiscovery).Hash -cne (Get-FileHash -LiteralPath $hcSavedManifest).Hash) { throw 'An existing signed mirror version has different bytes; preserve the cache.' }
        Remove-Item -LiteralPath $hcDiscovery -Force
    } else { [IO.File]::Move($hcDiscovery, $hcSavedManifest, $false) }
    foreach ($hcName in @($hcFeedName, "HappyCheats-$($hcManifest.version)-win-x64.zip", 'HappyCheats.exe')) {
        $hcAsset = @($hcManifest.assets | Where-Object name -CEQ $hcName)[0]
        $hcDestination = Join-Path $hcVersionDirectory $hcName
        if (!(Test-Path -LiteralPath $hcDestination)) {
            $hcPublicName = if ($hcName -ceq $hcFeedName) { 'latest.json' } else { $hcName }
            $hcCopied = Copy-HcVerifiedMirrorCacheAsset -Source (Join-Path $hcConfig.PublicDirectory $hcPublicName) -Destination $hcDestination -Asset $hcAsset
            if (!$hcCopied) { Receive-HcMirrorAsset -Url "https://github.com/$GitHubRepository/releases/download/v$($hcManifest.version)/$hcName" -Destination $hcDestination -MaximumBytes $hcAsset.size -ExpectedBytes $hcAsset.size -ExpectedSha256 $hcAsset.sha256 }
        }
        Assert-HcMirrorAsset -Path $hcDestination -Asset $hcAsset
    }
    $hcVersion = Install-HcLanMirror -Config $hcConfig -Directory $hcVersionDirectory -Repository $GitHubRepository -BackupDirectory (Join-Path $hcStateRoot 'mirror-backups')
    Write-Host "LAN mirror verified and current: $($hcConfig.BaseUrl), version $hcVersion."
} finally { if ($hcClient) { $hcClient.Dispose() }; if ($hcKey) { $hcKey.Dispose() }; if ($hcLock) { $hcLock.Dispose() } }
