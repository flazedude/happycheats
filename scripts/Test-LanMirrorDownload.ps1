[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LanMirror.Common.ps1')
$hcTokens=$null; $hcErrors=$null
$hcAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Sync-LanUpdate.ps1'),[ref]$hcTokens,[ref]$hcErrors)
if ($hcErrors.Count) { throw 'Mirror download script has parse errors.' }
$hcDefinition=$hcAst.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Receive-HcMirrorAsset'},$true)
. ([scriptblock]::Create($hcDefinition.Extent.Text))
if (!('HappyCheats.MirrorDownloadTests.InterruptedStream' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace HappyCheats.MirrorDownloadTests {
    public sealed class UnseekableStream : MemoryStream {
        public UnseekableStream(byte[] bytes) : base(bytes, false) { }
        public override bool CanSeek => false;
    }
    public sealed class InterruptedStream : MemoryStream {
        readonly long after;
        readonly bool stall;
        public InterruptedStream(byte[] bytes, long after, bool stall) : base(bytes, false) { this.after=after; this.stall=stall; }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            if (Position >= after) {
                if (stall) await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new IOException("Fixture interrupted transport.");
            }
            return await base.ReadAsync(buffer, offset, (int)Math.Min(count, after-Position), cancellationToken);
        }
    }
}
'@
}
$hcTestRoot=Join-Path ([IO.Path]::GetTempPath()) ('HappyCheats-MirrorDownload-' + [Guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $hcTestRoot
$hcCount=0
$hcBytes=[byte[]](1,2,3,4,5,6,7,8)
$hcHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($hcBytes)).ToLowerInvariant()
$hcClient=[pscustomobject]@{Responses=[Collections.Generic.Queue[Net.Http.HttpResponseMessage]]::new();Ranges=[Collections.Generic.List[string]]::new()}
$hcClient | Add-Member -MemberType ScriptMethod -Name SendAsync -Value {
    param($Request,$Completion,$CancellationToken)
    $CancellationToken.ThrowIfCancellationRequested()
    $this.Ranges.Add([string]$Request.Headers.Range)
    if (!$this.Responses.Count) { throw 'Unexpected fixture HTTP request.' }
    return [Threading.Tasks.Task]::FromResult($this.Responses.Dequeue())
}
function Assert-HcDownload([bool]$Condition,[string]$Message) { if (!$Condition) { throw "Mirror download regression failed: $Message" }; $script:hcCount++ }
function Assert-HcDownloadRejected([scriptblock]$Action,[string]$Message) { $hcFailed=$false; try { & $Action } catch { $hcFailed=$true }; Assert-HcDownload $hcFailed $Message }
function New-HcDownloadResponse {
    param([byte[]]$Bytes=$hcBytes,[int]$Status=200,[long]$Start=-1,[long]$End=7,[long]$Total=8,[long]$Length=-1,[long]$InterruptAfter=-1,[switch]$Stall,[switch]$NoLength)
    $hcResponse=[Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]$Status)
    if ($InterruptAfter -ge 0) {
        $hcStream=[HappyCheats.MirrorDownloadTests.InterruptedStream]::new($Bytes,$InterruptAfter,$Stall)
        $hcResponse.Content=[Net.Http.StreamContent]::new($hcStream)
    } elseif ($NoLength) { $hcResponse.Content=[Net.Http.StreamContent]::new([HappyCheats.MirrorDownloadTests.UnseekableStream]::new($Bytes)) }
    else { $hcResponse.Content=[Net.Http.ByteArrayContent]::new($Bytes) }
    if ($Length -ge 0) { $hcResponse.Content.Headers.ContentLength=$Length }
    if ($Start -ge 0) { $hcResponse.Content.Headers.ContentRange=[Net.Http.Headers.ContentRangeHeaderValue]::new($Start,$End,$Total) }
    return $hcResponse
}
function New-HcDownloadArguments([string]$Name) {
    return @{Url='https://github.com/flazedude/happycheats/releases/download/v1.2.3/HappyCheats-1.2.3-win-x64.zip';Destination=(Join-Path $hcTestRoot $Name);MaximumBytes=8;ExpectedBytes=8;ExpectedSha256=$hcHash;Resume=$true}
}
function Get-HcPartialPath($Arguments) { return $Arguments.Destination + '.' + $Arguments.ExpectedSha256 + '.partial' }
try {
    $hcArgs=New-HcDownloadArguments 'resume.zip'; $hcPartial=Get-HcPartialPath $hcArgs
    $hcClient.Responses.Enqueue((New-HcDownloadResponse -InterruptAfter 3))
    Assert-HcDownloadRejected { Receive-HcMirrorAsset @hcArgs } 'transport interruption is reported'
    Assert-HcDownload ((Get-Item -LiteralPath $hcPartial).Length -eq 3 -and !(Test-Path -LiteralPath $hcArgs.Destination)) 'interruption preserves only the bounded partial outside the complete cache'
    $hcClient.Responses.Enqueue((New-HcDownloadResponse -Bytes $hcBytes[3..7] -Status 206 -Start 3))
    Receive-HcMirrorAsset @hcArgs
    Assert-HcDownload ($hcClient.Ranges[-1] -ceq 'bytes=3-') 'retry requests the exact saved offset'
    Assert-HcDownload ((Get-FileHash -LiteralPath $hcArgs.Destination).Hash -ieq $hcHash -and !(Test-Path -LiteralPath $hcPartial)) 'resumed bytes pass signed hash before complete cache activation'
    $hcRequests=$hcClient.Ranges.Count
    Receive-HcMirrorAsset @hcArgs
    Assert-HcDownload ($hcClient.Ranges.Count -eq $hcRequests) 'verified complete cache requires no network'

    $hcArgs=New-HcDownloadArguments 'complete-partial.zip'; $hcPartial=Get-HcPartialPath $hcArgs
    [IO.File]::WriteAllBytes($hcPartial,$hcBytes)
    Receive-HcMirrorAsset @hcArgs
    Assert-HcDownload ((Test-Path -LiteralPath $hcArgs.Destination) -and $hcClient.Ranges.Count -eq $hcRequests) 'complete partial surviving termination verifies and commits without network'
    [IO.File]::WriteAllBytes($hcArgs.Destination,[byte[]](8,7,6,5,4,3,2,1))
    Assert-HcDownloadRejected { Receive-HcMirrorAsset @hcArgs } 'corrupt complete cache is not silently replaced'

    $hcArgs=New-HcDownloadArguments 'corrupt-complete-partial.zip'; $hcPartial=Get-HcPartialPath $hcArgs
    [IO.File]::WriteAllBytes($hcPartial,[byte[]](8,7,6,5,4,3,2,1))
    Assert-HcDownloadRejected { Receive-HcMirrorAsset @hcArgs } 'complete but corrupt partial is rejected without a network request'
    Assert-HcDownload (!(Test-Path -LiteralPath $hcPartial) -and $hcClient.Ranges.Count -eq $hcRequests) 'complete corrupt partial is discarded so the next attempt can restart'
    $hcClient.Responses.Enqueue((New-HcDownloadResponse))
    Receive-HcMirrorAsset @hcArgs
    Assert-HcDownload ((Get-FileHash -LiteralPath $hcArgs.Destination).Hash -ieq $hcHash) 'clean retry after complete partial corruption succeeds'

    $hcArgs=New-HcDownloadArguments 'short-body.zip'; $hcPartial=Get-HcPartialPath $hcArgs
    $hcClient.Responses.Enqueue((New-HcDownloadResponse -Bytes $hcBytes[0..2] -Length 8))
    Assert-HcDownloadRejected { Receive-HcMirrorAsset @hcArgs } 'early clean EOF is detected despite claimed full content length'
    Assert-HcDownload ((Get-Item -LiteralPath $hcPartial).Length -eq 3) 'early EOF retains bounded progress for a later range retry'
    $hcResponse=New-HcDownloadResponse -Bytes $hcBytes[3..7] -Status 206 -Start 3 -NoLength
    Assert-HcDownload ($null -eq $hcResponse.Content.Headers.ContentLength) 'fixture range response omits Content-Length'
    $hcClient.Responses.Enqueue($hcResponse)
    Receive-HcMirrorAsset @hcArgs
    Assert-HcDownload ((Get-FileHash -LiteralPath $hcArgs.Destination).Hash -ieq $hcHash) 'clean EOF resumes successfully with Content-Range and no Content-Length'

    $hcArgs=New-HcDownloadArguments 'ignored-range.zip'; $hcPartial=Get-HcPartialPath $hcArgs
    [IO.File]::WriteAllBytes($hcPartial,$hcBytes[0..2])
    $hcClient.Responses.Enqueue((New-HcDownloadResponse))
    Receive-HcMirrorAsset @hcArgs
    Assert-HcDownload ((Get-FileHash -LiteralPath $hcArgs.Destination).Hash -ieq $hcHash) '200 ignoring Range restarts from zero instead of appending duplicate bytes'

    foreach ($hcCase in @('start','end','total','unit','length','missing')) {
        $hcArgs=New-HcDownloadArguments ("bad-range-$hcCase.zip"); $hcPartial=Get-HcPartialPath $hcArgs
        [IO.File]::WriteAllBytes($hcPartial,$hcBytes[0..2])
        $hcResponse=New-HcDownloadResponse -Bytes $hcBytes[3..7] -Status 206 -Start 3
        switch ($hcCase) {
            'start' { $hcResponse.Content.Headers.ContentRange=[Net.Http.Headers.ContentRangeHeaderValue]::new(2,7,8) }
            'end' { $hcResponse.Content.Headers.ContentRange=[Net.Http.Headers.ContentRangeHeaderValue]::new(3,6,8) }
            'total' { $hcResponse.Content.Headers.ContentRange=[Net.Http.Headers.ContentRangeHeaderValue]::new(3,7,9) }
            'unit' { $hcResponse.Content.Headers.ContentRange.Unit='items' }
            'length' { $hcResponse.Content.Headers.ContentLength=4 }
            'missing' { $hcResponse.Content.Headers.ContentRange=$null }
        }
        $hcClient.Responses.Enqueue($hcResponse)
        Assert-HcDownloadRejected { Receive-HcMirrorAsset @hcArgs } "invalid Content-Range/$hcCase is rejected"
        Assert-HcDownload ((Get-Item -LiteralPath $hcPartial).Length -eq 3 -and !(Test-Path -LiteralPath $hcArgs.Destination)) "invalid $hcCase cannot alter the retained prefix or complete cache"
    }
    $hcArgs=New-HcDownloadArguments 'bad-restart.zip'; $hcPartial=Get-HcPartialPath $hcArgs
    [IO.File]::WriteAllBytes($hcPartial,$hcBytes[0..2])
    $hcClient.Responses.Enqueue((New-HcDownloadResponse -Length 9))
    Assert-HcDownloadRejected { Receive-HcMirrorAsset @hcArgs } 'invalid full-response size is rejected before truncating the existing prefix'
    Assert-HcDownload ((Get-Item -LiteralPath $hcPartial).Length -eq 3) 'rejected restart preserves the existing prefix'

    $hcArgs=New-HcDownloadArguments 'bad-hash.zip'; $hcPartial=Get-HcPartialPath $hcArgs
    [IO.File]::WriteAllBytes($hcPartial,[byte[]](0,0,0))
    $hcClient.Responses.Enqueue((New-HcDownloadResponse -Bytes $hcBytes[3..7] -Status 206 -Start 3))
    Assert-HcDownloadRejected { Receive-HcMirrorAsset @hcArgs } 'corrupt resumed prefix fails final signed hash'
    Assert-HcDownload (!(Test-Path -LiteralPath $hcPartial) -and !(Test-Path -LiteralPath $hcArgs.Destination)) 'hash mismatch discards the unusable partial'
    $hcClient.Responses.Enqueue((New-HcDownloadResponse))
    Receive-HcMirrorAsset @hcArgs
    Assert-HcDownload ((Get-FileHash -LiteralPath $hcArgs.Destination).Hash -ieq $hcHash) 'next clean retry succeeds after hash failure'

    $hcArgs=New-HcDownloadArguments 'oversize.zip'; $hcPartial=Get-HcPartialPath $hcArgs
    [IO.File]::WriteAllBytes($hcPartial,[byte[]](1,2,3,4,5,6,7,8,9))
    Assert-HcDownloadRejected { Receive-HcMirrorAsset @hcArgs } 'oversized preexisting partial is rejected'
    Assert-HcDownload (!(Test-Path -LiteralPath $hcPartial)) 'oversized partial is discarded'

    $hcArgs=New-HcDownloadArguments 'oversize-body.zip'; $hcPartial=Get-HcPartialPath $hcArgs
    $hcClient.Responses.Enqueue((New-HcDownloadResponse -Bytes ([byte[]](1,2,3,4,5,6,7,8,9)) -NoLength))
    Assert-HcDownloadRejected { Receive-HcMirrorAsset @hcArgs } 'body cannot grow past signed length when Content-Length is absent'
    Assert-HcDownload (!(Test-Path -LiteralPath $hcPartial) -and !(Test-Path -LiteralPath $hcArgs.Destination)) 'oversized transfer cannot poison a complete or partial cache'

    $hcArgs=New-HcDownloadArguments 'unsigned.zip'; $hcArgs.ExpectedSha256=''
    Assert-HcDownloadRejected { Receive-HcMirrorAsset @hcArgs } 'resuming without signed hash is forbidden'
    Assert-HcDownload (@(Get-ChildItem -LiteralPath $hcTestRoot -Filter 'unsigned.zip*').Count -eq 0) 'missing signed identity cannot create a partial'

    $hcArgs=New-HcDownloadArguments 'redirect.zip'; $hcPartial=Get-HcPartialPath $hcArgs
    [IO.File]::WriteAllBytes($hcPartial,$hcBytes[0..2])
    $hcResponse=New-HcDownloadResponse -Status 302
    $hcResponse.Headers.Location=[Uri]'https://release-assets.githubusercontent.com/fixture'
    $hcClient.Responses.Enqueue($hcResponse)
    $hcClient.Responses.Enqueue((New-HcDownloadResponse -Bytes $hcBytes[3..7] -Status 206 -Start 3))
    Receive-HcMirrorAsset @hcArgs
    Assert-HcDownload ($hcClient.Ranges[-1] -ceq 'bytes=3-' -and $hcClient.Ranges[-2] -ceq 'bytes=3-') 'Range survives allowed GitHub redirects'

    foreach ($hcMode in @('cancel','read-timeout')) {
        $hcArgs=New-HcDownloadArguments ("$hcMode.zip"); $hcPartial=Get-HcPartialPath $hcArgs
        $hcClient.Responses.Enqueue((New-HcDownloadResponse -InterruptAfter 3 -Stall))
        $hcCancellation=[Threading.CancellationTokenSource]::new()
        try {
            if ($hcMode -ceq 'cancel') { $hcCancellation.CancelAfter(200); $hcArgs.CancellationToken=$hcCancellation.Token }
            else { $hcArgs.ReadTimeoutSeconds=1 }
            $hcTimer=[Diagnostics.Stopwatch]::StartNew()
            Assert-HcDownloadRejected { Receive-HcMirrorAsset @hcArgs } "$hcMode interrupts a stalled read"
            $hcTimer.Stop()
            Assert-HcDownload ($hcTimer.ElapsedMilliseconds -lt 5000 -and (Get-Item -LiteralPath $hcPartial).Length -eq 3) "$hcMode is bounded and retains verified-size-limited progress"
        } finally { $hcCancellation.Dispose() }
    }
    $hcArgs=New-HcDownloadArguments 'metadata.json'; $hcArgs.Resume=$false
    $hcClient.Responses.Enqueue((New-HcDownloadResponse -InterruptAfter 3))
    Assert-HcDownloadRejected { Receive-HcMirrorAsset @hcArgs } 'metadata interruption is reported'
    Assert-HcDownload (@(Get-ChildItem -LiteralPath $hcTestRoot -Filter 'metadata.json*').Count -eq 0) 'metadata remains nonresumable and leaves no partial file'
    Assert-HcDownload ($hcClient.Responses.Count -eq 0) 'all scripted transport responses were consumed'
    Write-Host "LAN mirror download checks passed: $hcCount. Mocked transport only; no network, live keys or server changes."
} finally {
    while ($hcClient.Responses.Count) { $hcClient.Responses.Dequeue().Dispose() }
    $hcResolved=[IO.Path]::GetFullPath($hcTestRoot)
    $hcPrefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if (!$hcResolved.StartsWith($hcPrefix,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($hcResolved).StartsWith('HappyCheats-MirrorDownload-')) { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $hcResolved) { Remove-Item -LiteralPath $hcResolved -Recurse -Force }
}
