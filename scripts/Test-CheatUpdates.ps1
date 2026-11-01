[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CheatUpdates.Common.ps1')
$hcRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcFixture=Join-Path ([IO.Path]::GetTempPath()) ('HappyCheats-cheat-mirror-tests-'+[Guid]::NewGuid().ToString('N'))
$hcKey=[Security.Cryptography.RSA]::Create(2048);$hcChecks=0
function Test-HcCheat([bool]$Condition,[string]$Message) { if (!$Condition) { throw "Cheat mirror: $Message" }; $script:hcChecks++ }
function Reject-HcCheat([scriptblock]$Action) { $hcRejected=$false;try{& $Action | Out-Null}catch{$hcRejected=$true};Test-HcCheat $hcRejected 'expected rejection' }
function Write-HcCheatProof([string]$Path,$Catalog) {
    $hcPayload=[Text.Encoding]::UTF8.GetBytes(($Catalog|ConvertTo-Json -Depth 10 -Compress))
    @{payload=[Convert]::ToBase64String($hcPayload);signature=[Convert]::ToBase64String($hcKey.SignData($hcPayload,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1))}|ConvertTo-Json|Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}
try {
    New-Item -ItemType Directory -Force -Path $hcFixture,(Join-Path $hcFixture 'cache') | Out-Null
    $hcPublicKey=Join-Path $hcFixture 'public.pem'; [IO.File]::WriteAllText($hcPublicKey,$hcKey.ExportSubjectPublicKeyInfoPem())
    $hcBytes=[byte[]](1,2,3,4);$hcName='HappyCheats-cheat-future-game-1.0.0.zip'
    $hcGame=[ordered]@{id='future-game';name='Future Game';version='1.0.0';minimumClientVersion='0.3.65';contractVersion=1;moduleType='HappyCheats.Core.FutureGameModule';assembly='lib/HappyCheats.Core.dll';sourceCommit=('a'*40);packageUrl="https://github.com/flazedude/happycheats/releases/download/cheat-future-game-1.0.0/$hcName";packageSize=4;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($hcBytes)).ToLowerInvariant();notesEnglish='Synthetic fixture';notesChinese='合成示例'}
    $hcCatalog=[ordered]@{schemaVersion=1;channel='stable';revision=1L;games=@($hcGame)}
    $hcFeed=Join-Path $hcFixture 'cheats.json';Write-HcCheatProof $hcFeed $hcCatalog
    Test-HcCheat ((Read-HcCheatCatalog $hcFeed $hcKey).games.Count -eq 1) 'signed catalog accepted'
    & dotnet run --project (Join-Path $hcRoot 'tools/HappyCheats.CheatRelease') -c Release -- verify --catalog $hcFeed --public-key $hcPublicKey
    Test-HcCheat ($LASTEXITCODE -eq 0) 'PowerShell proof verifies in client implementation'
    $hcCache=Join-Path $hcFixture 'cache';[IO.File]::WriteAllBytes((Join-Path $hcCache $hcName),$hcBytes)
    $hcConfig=[pscustomobject]@{PublicKeyPath=$hcPublicKey;PublicDirectory=(Join-Path $hcFixture 'public');BaseUrl='http://127.0.0.1:17845/happycheats'}
    Install-HcCheatMirror $hcFeed $hcConfig $hcCache
    Test-HcCheat ((Get-FileHash -LiteralPath (Join-Path $hcConfig.PublicDirectory $hcName)).Hash -ieq $hcGame.sha256) 'mirror exposes exact package bytes'
    $hcBefore=(Get-FileHash -LiteralPath (Join-Path $hcConfig.PublicDirectory 'cheats.json')).Hash
    $hcGame.sha256='b'*64;Write-HcCheatProof $hcFeed $hcCatalog
    Reject-HcCheat { Install-HcCheatMirror $hcFeed $hcConfig $hcCache }
    Test-HcCheat ((Get-FileHash -LiteralPath (Join-Path $hcConfig.PublicDirectory 'cheats.json')).Hash -ceq $hcBefore) 'failed integrity preserves active mirror'
    $hcGame.sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($hcBytes)).ToLowerInvariant()
    $hcCatalog.revision=2L;Write-HcCheatProof $hcFeed $hcCatalog;Install-HcCheatMirror $hcFeed $hcConfig $hcCache
    $hcCatalog.revision=1L;Write-HcCheatProof $hcFeed $hcCatalog;Reject-HcCheat { Install-HcCheatMirror $hcFeed $hcConfig $hcCache }
    $hcCatalog.channel='beta';Write-HcCheatProof $hcFeed $hcCatalog;Reject-HcCheat { Read-HcCheatCatalog $hcFeed $hcKey }
    $hcCatalog.channel='stable';$hcGame.packageUrl='https://example.invalid/package.zip';Write-HcCheatProof $hcFeed $hcCatalog;Reject-HcCheat { Read-HcCheatCatalog $hcFeed $hcKey }
    foreach($hcFile in @('Publish-CheatUpdate.ps1','Sync-CheatUpdate.ps1','CheatUpdates.Common.ps1')) {
        $hcTokens=$null;$hcErrors=$null;$null=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $hcFile),[ref]$hcTokens,[ref]$hcErrors)
        Test-HcCheat (!$hcErrors) "valid PowerShell syntax: $hcFile"
    }
    Write-Host "Independent cheat mirror checks: $hcChecks passed. Synthetic local fixtures; no live server, credentials or publication."
} finally {
    $hcKey.Dispose();$hcResolved=[IO.Path]::GetFullPath($hcFixture)
    if (!$hcResolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($hcResolved).StartsWith('HappyCheats-cheat-mirror-tests-')) { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $hcResolved) { Remove-Item -LiteralPath $hcResolved -Recurse -Force }
}
