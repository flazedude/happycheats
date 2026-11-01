# Signed independent game packages. This helper contains no credentials or private-key discovery.
. (Join-Path $PSScriptRoot 'LanMirror.Common.ps1')
function Write-HcCheatWebContent([string]$Path, [object]$Content) {
    Assert-HcMirrorSafePath $Path
    $hcBytes = if ($Content -is [byte[]]) { $Content } else { [Text.Encoding]::UTF8.GetBytes([string]$Content) }
    if ($hcBytes.Length -gt 524288) { throw 'Cheat catalog is too large.' }
    [IO.File]::WriteAllBytes($Path, [byte[]]$hcBytes)
}
function Read-HcCheatCatalog([string]$Path, [Security.Cryptography.RSA]$Key) {
    Assert-HcMirrorSafePath $Path
    if ((Get-Item -LiteralPath $Path).Length -gt 524288) { throw 'Cheat catalog is too large.' }
    $hcEnvelope = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $hcPayload = [Convert]::FromBase64String($hcEnvelope.payload)
    if ($hcPayload.Length -gt 350000 -or !$Key.VerifyData($hcPayload, [Convert]::FromBase64String($hcEnvelope.signature), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw 'Cheat catalog signature is invalid.' }
    $hcCatalog = [Text.Encoding]::UTF8.GetString($hcPayload) | ConvertFrom-Json
    if ($hcCatalog.schemaVersion -ne 1 -or $hcCatalog.channel -cne 'stable' -or $hcCatalog.revision -le 0 -or $null -eq $hcCatalog.games -or @($hcCatalog.games).Count -gt 200) { throw 'Invalid cheat catalog identity.' }
    $hcIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($hcGame in $hcCatalog.games) {
        if ($hcGame.id -cnotmatch '^[a-z0-9]+(-[a-z0-9]+)*$' -or $hcGame.id.Length -gt 80 -or !$hcIds.Add($hcGame.id) -or
            $hcGame.version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
            $hcGame.minimumClientVersion -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
            $hcGame.contractVersion -lt 1 -or $hcGame.assembly -cne 'lib/HappyCheats.Core.dll' -or
            $hcGame.moduleType -cnotmatch '^HappyCheats\.Core\.[A-Za-z0-9_]+Module$' -or $hcGame.sourceCommit -cnotmatch '^[a-f0-9]{40}$' -or
            $hcGame.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $hcGame.packageSize -le 0 -or $hcGame.packageSize -gt 2GB) { throw 'Invalid cheat package metadata.' }
        $hcName = "HappyCheats-cheat-$($hcGame.id)-$($hcGame.version).zip"
        if ($hcGame.packageUrl -cne "https://github.com/flazedude/happycheats/releases/download/cheat-$($hcGame.id)-$($hcGame.version)/$hcName") { throw 'Cheat package URL is not its immutable official asset.' }
    }
    return $hcCatalog
}
function Assert-HcCheatCatalogAdvance($Previous, $Next) {
    if ($Previous.revision -gt $Next.revision) { throw 'Refusing to downgrade the cheat catalog.' }
    foreach ($hcOld in $Previous.games) {
        $hcNew = @($Next.games | Where-Object id -CEQ $hcOld.id)
        if ($hcNew.Count -ne 1 -or [Version]$hcNew[0].version -lt [Version]$hcOld.version) { throw 'Cheat catalog removes or downgrades a game.' }
        if ($hcOld.version -ceq $hcNew[0].version) {
            foreach ($hcField in @('packageUrl','packageSize','sha256','moduleType','assembly','sourceCommit','minimumClientVersion','contractVersion')) {
                if ($hcOld.$hcField -cne $hcNew[0].$hcField) { throw 'Conflicting immutable cheat package.' }
            }
        }
    }
}
function Receive-HcCheatFile([Net.Http.HttpClient]$Client, [string]$Url, [string]$Destination, [long]$Limit, [long]$ExpectedSize = 0, [string]$ExpectedHash) {
    Assert-HcMirrorSafePath $Destination
    if (!$Url.StartsWith('https://')) { throw 'Cheat mirror receives only HTTPS assets.' }
    $hcTemporary = $Destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $hcResponse=$null; $hcInput=$null; $hcOutput=$null
    try {
        $hcResponse = $Client.GetAsync($Url,[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $hcResponse.EnsureSuccessStatusCode() | Out-Null
        if ($hcResponse.RequestMessage.RequestUri.Scheme -cne 'https' -or $hcResponse.Content.Headers.ContentLength -gt $Limit -or ($ExpectedSize -and $hcResponse.Content.Headers.ContentLength -and $hcResponse.Content.Headers.ContentLength -ne $ExpectedSize)) { throw 'Cheat download size or redirect is invalid.' }
        $hcInput=$hcResponse.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $hcOutput=[IO.File]::Open($hcTemporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        $hcBuffer=[byte[]]::new(65536); [long]$hcReceived=0
        while (($hcCount=$hcInput.ReadAsync($hcBuffer,0,$hcBuffer.Length).WaitAsync([TimeSpan]::FromSeconds(60)).GetAwaiter().GetResult()) -gt 0) {
            $hcReceived += $hcCount
            if ($hcReceived -gt $Limit) { throw 'Cheat download exceeds its limit.' }
            $hcOutput.Write($hcBuffer,0,$hcCount)
        }
        $hcOutput.Flush($true); $hcOutput.Dispose(); $hcOutput=$null
        if (($ExpectedSize -and $hcReceived -ne $ExpectedSize) -or ($ExpectedHash -and (Get-FileHash -LiteralPath $hcTemporary).Hash -ine $ExpectedHash)) { throw 'Cheat download failed signed size/SHA-256 verification.' }
        [IO.File]::Move($hcTemporary,$Destination,$false)
    } finally {
        if ($hcOutput) { $hcOutput.Dispose() }; if ($hcInput) { $hcInput.Dispose() }; if ($hcResponse) { $hcResponse.Dispose() }
        if (Test-Path -LiteralPath $hcTemporary) { Remove-Item -LiteralPath $hcTemporary -Force }
    }
}
function Install-HcCheatMirror([string]$CatalogPath, $Config, [string]$CacheDirectory) {
    $hcKey=Read-HcMirrorPublicKey $Config.PublicKeyPath; $hcHttp=[Net.Http.HttpClient]::new()
    $hcHttp.Timeout=[TimeSpan]::FromMinutes(10); $hcHttp.DefaultRequestHeaders.UserAgent.ParseAdd('HappyCheats-CheatMirror/1.0')
    try {
        $hcNext=Read-HcCheatCatalog $CatalogPath $hcKey
        $hcFeed=Join-Path $Config.PublicDirectory 'cheats.json'
        Assert-HcMirrorSafePath $hcFeed; Assert-HcMirrorSafePath $CacheDirectory
        New-Item -ItemType Directory -Force -Path $CacheDirectory,$Config.PublicDirectory | Out-Null
        if (Test-Path -LiteralPath $hcFeed) {
            $hcOld=Read-HcCheatCatalog $hcFeed $hcKey
            Assert-HcCheatCatalogAdvance $hcOld $hcNext
            if ($hcOld.revision -eq $hcNext.revision -and (Get-FileHash -LiteralPath $hcFeed).Hash -cne (Get-FileHash -LiteralPath $CatalogPath).Hash) { throw 'Same-revision cheat catalogs have conflicting bytes.' }
        }
        foreach ($hcGame in $hcNext.games) {
            $hcName="HappyCheats-cheat-$($hcGame.id)-$($hcGame.version).zip"
            $hcAsset=@{name=$hcName;size=$hcGame.packageSize;sha256=$hcGame.sha256}
            $hcCache=Join-Path $CacheDirectory $hcName; $hcPublic=Join-Path $Config.PublicDirectory $hcName
            Assert-HcMirrorSafePath $hcCache; Assert-HcMirrorSafePath $hcPublic
            if (!(Test-Path -LiteralPath $hcCache)) {
                if (!(Copy-HcVerifiedMirrorCacheAsset $hcPublic $hcCache $hcAsset)) { Receive-HcCheatFile $hcHttp $hcGame.packageUrl $hcCache $hcGame.packageSize $hcGame.packageSize $hcGame.sha256 }
            }
            Assert-HcMirrorAsset $hcCache $hcAsset
            if (Test-Path -LiteralPath $hcPublic) { Assert-HcMirrorAsset $hcPublic $hcAsset }
            else { $null=Copy-HcVerifiedMirrorCacheAsset $hcCache $hcPublic $hcAsset }
        }
        Copy-HcMirrorAtomic $CatalogPath $hcFeed (Join-Path $CacheDirectory 'backups')
        Write-Host "Independent cheat mirror verified: $($Config.BaseUrl)/cheats.json, revision $($hcNext.revision)."
    } finally { $hcKey.Dispose(); $hcHttp.Dispose() }
}
