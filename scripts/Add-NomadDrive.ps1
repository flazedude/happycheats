#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientDirectory)
$ErrorActionPreference='Stop'
$ndRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ndClient=Join-Path $ClientDirectory 'HappyCheats.exe'
if (!(Test-Path -LiteralPath $ndClient -PathType Leaf)) { throw 'Pass the existing unpacked HappyCheats client directory (beside update-defaults.json), not the standalone wrapper directory.' }
$ndDefaults=Join-Path $ClientDirectory 'update-defaults.json'
if (!(Test-Path -LiteralPath $ndDefaults -PathType Leaf)) { throw 'Choose the unpacked official client directory beside update-defaults.json.' }
$ndSettings=Get-Content -LiteralPath $ndDefaults -Raw | ConvertFrom-Json
$ndIdentity=[Security.Cryptography.RSA]::Create()
try {
    $ndIdentity.ImportFromPem($ndSettings.publicKeyPem)
    $ndFingerprint=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($ndIdentity.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
    if ($ndSettings.channel -cne 'stable' -or $ndFingerprint -cne '39e6cea79d18fadabcc400754b24e77c01321be729a712a89f9304cd7e5af1eb') { throw 'This client uses different update trust. Its custom configuration was preserved; use the official client for this signed module.' }
} finally { $ndIdentity.Dispose() }
$ndVersion=(Get-Item -LiteralPath $ndClient).VersionInfo.ProductVersion.Split('+')[0]
& dotnet run --project (Join-Path $ndRoot 'tools/AddNomadDrive') -c Release -- (Join-Path $ndRoot 'assets/updates/release-public.pem') $ndVersion
if ($LASTEXITCODE -ne 0) { throw 'Nomad Drive setup did not complete. Follow the error above and retry; signed cached packages are preserved.' }
