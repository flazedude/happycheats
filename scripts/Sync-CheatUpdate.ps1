# No SDK, publisher credentials, or private key required.
[CmdletBinding()]
param([string]$ConfigPath, [switch]$LockHeld)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CheatUpdates.Common.ps1')
$hcLocations=Resolve-HcMirrorLocations $PSScriptRoot $ConfigPath
$ConfigPath=$hcLocations.ConfigPath
$hcState=$hcLocations.StateRoot
$hcConfig=Get-HcLanMirrorVerificationConfiguration (Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json)
$null=Get-HcLanFeedAsset $hcConfig.BaseUrl
$hcLock=$null; $hcHttp=[Net.Http.HttpClient]::new(); $hcHttp.Timeout=[TimeSpan]::FromSeconds(60)
try {
    if (!$LockHeld) { Assert-HcMirrorSafePath (Join-Path $hcState 'publish.lock'); $hcLock=[IO.File]::Open((Join-Path $hcState 'publish.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::Write,[IO.FileShare]::None) }
    $hcCache=Join-Path $hcState 'cheat-cache'; Assert-HcMirrorSafePath $hcCache
    New-Item -ItemType Directory -Force -Path $hcCache | Out-Null
    $hcDiscovery=Join-Path $hcCache ('cheats-'+[Guid]::NewGuid().ToString('N')+'.json')
    Receive-HcCheatFile $hcHttp 'https://raw.githubusercontent.com/flazedude/happycheats/cheat-feed/cheats.json' $hcDiscovery 524288
    Install-HcCheatMirror $hcDiscovery $hcConfig $hcCache
} finally { $hcHttp.Dispose(); if ($hcLock) { $hcLock.Dispose() } }
