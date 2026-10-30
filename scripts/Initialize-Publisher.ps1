[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [string]$GitHubRepository = 'flazedude/happycheats'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PublisherConfiguration.Common.ps1')
Assert-HcPublisherRepository $GitHubRepository
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcInputKeyPath = (Resolve-Path -LiteralPath $PrivateKeyPath -ErrorAction Stop).Path
$hcFeedUrl = "https://github.com/$GitHubRepository/releases/latest/download/latest.json"
$hcClient = [Net.Http.HttpClient]::new()
try {
    $hcClient.Timeout = [TimeSpan]::FromSeconds(60)
    $hcClient.DefaultRequestHeaders.UserAgent.ParseAdd('HappyCheats-PublisherSetup/1.0')
    try { $hcEnvelopeJson = $hcClient.GetStringAsync($hcFeedUrl).GetAwaiter().GetResult() }
    catch { throw 'The public GitHub signed feed could not be downloaded. Setup requires an existing release and working HTTPS access; no publisher configuration was installed.' }
    $hcResult = Initialize-HcPublisherConfiguration -Root $hcRoot -Repository $GitHubRepository -PrivateKeyPath $hcInputKeyPath -EnvelopeJson $hcEnvelopeJson
    Write-Host "Publisher configured for $GitHubRepository; existing key verified against signed release $($hcResult.VerifiedVersion)."
    Write-Host "Machine-local configuration: $($hcResult.ConfigurationPath)"
    Write-Host 'The original private key was preserved. Sign in to GitHub separately on this PC before publishing.'
} finally { $hcClient.Dispose() }
