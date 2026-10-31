[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& dotnet build (Join-Path $hcRoot 'tools\HappyCheats.UpdateServer') -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Server build failed.' }
$hcFixture = Join-Path ([IO.Path]::GetTempPath()) ('HappyCheats-server-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $hcFixture | Out-Null
$hcListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$hcListener.Start()
$hcPort = $hcListener.LocalEndpoint.Port
$hcListener.Stop()
$hcProcess = $null
try {
    [IO.File]::WriteAllText((Join-Path $hcFixture 'latest.json'), '{"test":true}')
    [IO.File]::WriteAllBytes((Join-Path $hcFixture 'HappyCheats-1.2.3-win-x64.zip'), [byte[]](0..255))
    [IO.File]::WriteAllText((Join-Path $hcFixture 'secret.pem'), 'not public')
    [IO.File]::WriteAllText((Join-Path $hcFixture 'HappyCheats.exe'), 'bootstrap fixture')
    $pkFixture = Join-Path $hcFixture 'proxyking'
    New-Item -ItemType Directory -Path $pkFixture | Out-Null
    [IO.File]::WriteAllText((Join-Path $pkFixture 'latest.json'), '{"version":"1.2.3"}')
    [IO.File]::WriteAllBytes((Join-Path $pkFixture 'ProxyKing_1.2.3_x64-setup.exe'), [byte[]](0..255))
    [IO.File]::WriteAllText((Join-Path $pkFixture 'secret.pem'), 'not public')
    $hcDll = Join-Path $hcRoot 'tools\HappyCheats.UpdateServer\bin\Release\net8.0\HappyCheats.UpdateServer.dll'
    $hcArgs = @(('"' + $hcDll + '"'),'--address','127.0.0.1','--port',$hcPort.ToString(),'--release-root',('"' + $hcFixture + '"'))
    $hcProcess = Start-Process -FilePath 'dotnet' -ArgumentList $hcArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $hcFixture 'stdout.log') -RedirectStandardError (Join-Path $hcFixture 'stderr.log')
    $hcBase = "http://127.0.0.1:$hcPort"
    $hcReady = $false
    for ($hcAttempt = 0; $hcAttempt -lt 30; $hcAttempt++) {
        try { $null = Invoke-WebRequest "$hcBase/health" -NoProxy -TimeoutSec 1; $hcReady = $true; break } catch { Start-Sleep -Milliseconds 100 }
    }
    if (!$hcReady) { throw 'Test server did not start.' }
    $hcFeed = Invoke-WebRequest "$hcBase/happycheats/latest.json" -NoProxy
    if ($hcFeed.Content -ne '{"test":true}' -or $hcFeed.Headers['Cache-Control'] -notmatch 'no-cache') { throw 'Feed response/cache failed.' }
    $hcHead = Invoke-WebRequest "$hcBase/happycheats/HappyCheats-1.2.3-win-x64.zip" -Method Head -NoProxy
    if ($hcHead.Headers['Content-Length'] -ne '256' -or $hcHead.Headers['Cache-Control'] -notmatch 'immutable') { throw 'Archive HEAD/cache failed.' }
    $hcRange = Invoke-WebRequest "$hcBase/happycheats/HappyCheats-1.2.3-win-x64.zip" -Headers @{Range='bytes=10-19'} -NoProxy
    if ($hcRange.StatusCode -ne 206 -or $hcRange.RawContentLength -ne 10) { throw 'Archive range download failed.' }
    $hcBootstrap = Invoke-WebRequest "$hcBase/happycheats/HappyCheats.exe" -NoProxy
    if ($hcBootstrap.Headers['Cache-Control'] -notmatch 'no-cache') { throw 'Bootstrap cache failed.' }
    foreach ($hcPath in @('/happycheats/secret.pem','/happycheats/stdout.log','/happycheats/latest.json.tmp','/happycheats/','/happycheats/%2e%2e%2fsecret.pem')) {
        $hcResult = Invoke-WebRequest ($hcBase + $hcPath) -NoProxy -SkipHttpErrorCheck
        if ($hcResult.StatusCode -ne 404) { throw "Unexpected exposure at $hcPath" }
    }
    $hcPost = Invoke-WebRequest "$hcBase/happycheats/latest.json" -Method Post -NoProxy -SkipHttpErrorCheck
    if ($hcPost.StatusCode -ne 405) { throw 'Server accepted a write request.' }
    $pkFeed = Invoke-WebRequest "$hcBase/proxyking/latest.json" -NoProxy
    if ($pkFeed.Content -ne '{"version":"1.2.3"}' -or $pkFeed.Headers['Cache-Control'] -notmatch 'no-cache') { throw 'ProxyKing feed/isolation failed.' }
    $pkRange = Invoke-WebRequest "$hcBase/proxyking/ProxyKing_1.2.3_x64-setup.exe" -Headers @{Range='bytes=10-19'} -NoProxy
    if ($pkRange.StatusCode -ne 206 -or $pkRange.RawContentLength -ne 10 -or $pkRange.Headers['Cache-Control'] -notmatch 'immutable') { throw 'ProxyKing range/cache failed.' }
    foreach ($pkPath in @('/proxyking/secret.pem','/proxyking/HappyCheats.exe','/happycheats/ProxyKing_1.2.3_x64-setup.exe','/proxyking/%2e%2e%2flatest.json','/proxyking/latest.json.tmp','/proxyking/')) {
        if ((Invoke-WebRequest ($hcBase + $pkPath) -NoProxy -SkipHttpErrorCheck).StatusCode -ne 404) { throw "Unexpected exposure at $pkPath" }
    }
    if ((Invoke-WebRequest "$hcBase/proxyking/latest.json" -Method Post -NoProxy -SkipHttpErrorCheck).StatusCode -ne 405) { throw 'ProxyKing accepted writes.' }
    Write-Host 'PASS LAN server: feed, cache policies, HEAD, range downloads, bootstrap, path isolation and read-only access.'
} finally {
    if ($hcProcess -and !$hcProcess.HasExited) { Stop-Process -Id $hcProcess.Id; $hcProcess.WaitForExit() }
    $hcResolved = [IO.Path]::GetFullPath($hcFixture)
    if (!$hcResolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($hcResolved).StartsWith('HappyCheats-server-tests-')) { throw 'Invalid test cleanup path.' }
    Remove-Item -LiteralPath $hcResolved -Recurse -Force
}
