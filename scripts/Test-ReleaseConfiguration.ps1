[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& dotnet build (Join-Path $hcRoot 'tools/HappyCheats.Release') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Release configuration tool build failed.' }
$hcDll = Join-Path $hcRoot 'tools/HappyCheats.Release/bin/Release/net8.0/HappyCheats.Release.dll'
$hcFixture = Join-Path ([IO.Path]::GetTempPath()) ('HappyCheats-ReleaseConfig-Tests-' + [Guid]::NewGuid().ToString('N'))
$hcKey = [Security.Cryptography.RSA]::Create(2048)
$hcCount = 0
function Assert-HcReleaseConfig([bool]$Value, [string]$Message) { if (!$Value) { throw "Release configuration regression: $Message" }; $script:hcCount++ }
function Invoke-HcConfigure([string[]]$Options) {
    & dotnet $hcDll configure @Options *> (Join-Path $hcFixture 'command.log')
    return $LASTEXITCODE
}
try {
    New-Item -ItemType Directory -Path $hcFixture | Out-Null
    $hcPublicPath = Join-Path $hcFixture 'public.pem'; $hcPrivatePath = Join-Path $hcFixture 'private.pem'; $hcOutput = Join-Path $hcFixture 'settings.json'
    [IO.File]::WriteAllText($hcPublicPath, $hcKey.ExportSubjectPublicKeyInfoPem()); [IO.File]::WriteAllText($hcPrivatePath, $hcKey.ExportPkcs8PrivateKeyPem())
    $hcPrimary = 'http://192.168.2.76:17845/happycheats/latest.json'
    $hcPeer = 'http://192.168.2.59:17845/happycheats/latest.json'
    $hcGitHub = 'https://github.com/flazedude/happycheats/releases/latest/download/latest.json'
    $hcBase = @('--feed-url',$hcPrimary,'--fallback-feed-url',$hcGitHub,'--public-key',$hcPublicPath,'--output',$hcOutput)
    Assert-HcReleaseConfig ((Invoke-HcConfigure ($hcBase + @('--feed-source',$hcPrimary,'--feed-source',$hcPeer,'--feed-source',$hcGitHub))) -eq 0) 'repeatable feed-source configures all three sources'
    $hcSettings = Get-Content -LiteralPath $hcOutput -Raw | ConvertFrom-Json
    Assert-HcReleaseConfig (($hcSettings.feedUrls -join '|') -ceq (@($hcPrimary,$hcPeer,$hcGitHub) -join '|') -and $hcSettings.feedUrl -ceq $hcPrimary -and $hcSettings.fallbackFeedUrl -ceq $hcGitHub) 'ordered chain keeps old client compatibility fields'
    Assert-HcReleaseConfig ($hcSettings.channel -ceq 'stable' -and $hcSettings.publicKeyPem -ceq $hcKey.ExportSubjectPublicKeyInfoPem() -and !(Get-Content -LiteralPath $hcOutput -Raw).Contains('PRIVATE KEY')) 'configuration retains only established public trust'
    $hcBefore = (Get-FileHash -LiteralPath $hcOutput).Hash
    $hcBadLists = @(
        ,@($hcPeer,$hcPrimary,$hcGitHub),
        ,@($hcPrimary,$hcGitHub,$hcPeer),
        ,@($hcPrimary,$hcPeer,$hcPeer,$hcGitHub),
        ,@($hcPrimary,'http://public.example/latest.json',$hcGitHub),
        ,@($hcPrimary,'https://user:secret@example.com/latest.json',$hcGitHub),
        ,@($hcPrimary,'https://example.com/latest.json#fragment',$hcGitHub),
        ,@($hcPrimary,'https://example.com/1','https://example.com/2','https://example.com/3','https://example.com/4','https://example.com/5','https://example.com/6','https://example.com/7',$hcGitHub)
    )
    foreach ($hcList in $hcBadLists) {
        $hcArguments = @($hcBase)
        foreach ($hcUrl in $hcList) { $hcArguments += @('--feed-source',$hcUrl) }
        Assert-HcReleaseConfig ((Invoke-HcConfigure $hcArguments) -ne 0 -and (Get-FileHash -LiteralPath $hcOutput).Hash -ceq $hcBefore) 'invalid source chain fails before replacing valid settings'
    }
    Assert-HcReleaseConfig ((Invoke-HcConfigure @('--feed-url',$hcPrimary,'--public-key',$hcPrivatePath,'--output',$hcOutput)) -ne 0 -and (Get-FileHash -LiteralPath $hcOutput).Hash -ceq $hcBefore) 'private key material cannot be embedded'
    Assert-HcReleaseConfig ((Invoke-HcConfigure $hcBase) -eq 0) 'legacy two-source configuration remains supported'
    Assert-HcReleaseConfig (!(Get-Content -LiteralPath $hcOutput -Raw).Contains('feedUrls')) 'legacy config omits absent ordered sources'
    Write-Host "Release CLI configuration offline checks passed: $hcCount. Temporary fixture keys only."
} finally {
    $hcKey.Dispose()
    $hcResolved = [IO.Path]::GetFullPath($hcFixture)
    $hcPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (!$hcResolved.StartsWith($hcPrefix,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($hcResolved).StartsWith('HappyCheats-ReleaseConfig-Tests-')) { throw 'Invalid fixture cleanup path.' }
    if (Test-Path -LiteralPath $hcResolved) { Remove-Item -LiteralPath $hcResolved -Recurse -Force }
}
