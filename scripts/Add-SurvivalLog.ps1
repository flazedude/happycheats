#requires -Version 7.0
[CmdletBinding()]
param([string]$ClientDirectory)
$ErrorActionPreference='Stop'
$slRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$ClientDirectory) {
    $slBundles=Join-Path $env:LOCALAPPDATA 'HappyCheats/Bundles'
    $slCandidates=@()
    if(Test-Path -LiteralPath $slBundles) {
        $slCandidates=@(Get-ChildItem -LiteralPath $slBundles -Filter 'update-defaults.json' -File -Recurse | ForEach-Object {
            $slExe=Join-Path $_.DirectoryName 'HappyCheats.exe'
            if(Test-Path -LiteralPath $slExe) {
                $slVersion=(Get-Item -LiteralPath $slExe).VersionInfo.ProductVersion.Split('+')[0]
                if($slVersion -match '^\d+\.\d+\.\d+$' -and [Version]$slVersion -ge [Version]'0.3.69') {
                    [pscustomobject]@{Directory=$_.DirectoryName;Version=[Version]$slVersion;Modified=$_.LastWriteTimeUtc}
                }
            }
        } | Sort-Object -Property Version,Modified -Descending)
    }
    if(!$slCandidates.Count){throw 'No compatible extracted HappyCheats client was found. Start HappyCheats 0.3.69 or newer once, close it, then retry; or pass -ClientDirectory with its unpacked folder.'}
    $ClientDirectory=$slCandidates[0].Directory
}
$slClient=Join-Path $ClientDirectory 'HappyCheats.exe'
if (!(Test-Path -LiteralPath $slClient -PathType Leaf)) { throw 'Pass the existing unpacked HappyCheats client directory (beside update-defaults.json), not the standalone wrapper directory.' }
$slDefaults=Join-Path $ClientDirectory 'update-defaults.json'
if (!(Test-Path -LiteralPath $slDefaults -PathType Leaf)) { throw 'Choose the unpacked official client directory beside update-defaults.json.' }
$slSettings=Get-Content -LiteralPath $slDefaults -Raw | ConvertFrom-Json
$slIdentity=[Security.Cryptography.RSA]::Create()
try {
    $slIdentity.ImportFromPem($slSettings.publicKeyPem)
    $slFingerprint=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($slIdentity.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
    if ($slSettings.channel -cne 'stable' -or $slFingerprint -cne '39e6cea79d18fadabcc400754b24e77c01321be729a712a89f9304cd7e5af1eb') { throw 'This client uses different update trust. Its custom configuration was preserved; use the official client for this signed module.' }
} finally { $slIdentity.Dispose() }
$slVersion=(Get-Item -LiteralPath $slClient).VersionInfo.ProductVersion.Split('+')[0]
& dotnet run --project (Join-Path $slRoot 'tools/AddSurvivalLog') -c Release -- (Join-Path $slRoot 'assets/updates/release-public.pem') $slVersion
if ($LASTEXITCODE -ne 0) { throw 'Survival Log setup did not complete. Follow the error above and retry; signed cached packages are preserved.' }
