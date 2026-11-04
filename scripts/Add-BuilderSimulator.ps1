#requires -Version 7.0
[CmdletBinding()]
param([string]$ClientDirectory)
$ErrorActionPreference='Stop'
$bsRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$ClientDirectory) {
    $bsBundles=Join-Path $env:LOCALAPPDATA 'HappyCheats/Bundles'
    $bsCandidates=@()
    if(Test-Path -LiteralPath $bsBundles) {
        $bsCandidates=@(Get-ChildItem -LiteralPath $bsBundles -Filter 'update-defaults.json' -File -Recurse | ForEach-Object {
            $bsExe=Join-Path $_.DirectoryName 'HappyCheats.exe'
            if(Test-Path -LiteralPath $bsExe) {
                $bsVersion=(Get-Item -LiteralPath $bsExe).VersionInfo.ProductVersion.Split('+')[0]
                if($bsVersion -match '^\d+\.\d+\.\d+$' -and [Version]$bsVersion -ge [Version]'0.3.69') {
                    [pscustomobject]@{Directory=$_.DirectoryName;Version=[Version]$bsVersion;Modified=$_.LastWriteTimeUtc}
                }
            }
        } | Sort-Object -Property Version,Modified -Descending)
    }
    if(!$bsCandidates.Count){throw 'No compatible extracted HappyCheats client was found. Start HappyCheats 0.3.69 or newer once, close it, then retry; or pass -ClientDirectory with its unpacked folder.'}
    $ClientDirectory=$bsCandidates[0].Directory
}
$bsClient=Join-Path $ClientDirectory 'HappyCheats.exe'
if (!(Test-Path -LiteralPath $bsClient -PathType Leaf)) { throw 'Pass the existing unpacked HappyCheats client directory (beside update-defaults.json), not the standalone wrapper directory.' }
$bsDefaults=Join-Path $ClientDirectory 'update-defaults.json'
if (!(Test-Path -LiteralPath $bsDefaults -PathType Leaf)) { throw 'Choose the unpacked official client directory beside update-defaults.json.' }
$bsSettings=Get-Content -LiteralPath $bsDefaults -Raw | ConvertFrom-Json
$bsIdentity=[Security.Cryptography.RSA]::Create()
try {
    $bsIdentity.ImportFromPem($bsSettings.publicKeyPem)
    $bsFingerprint=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bsIdentity.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
    if ($bsSettings.channel -cne 'stable' -or $bsFingerprint -cne '39e6cea79d18fadabcc400754b24e77c01321be729a712a89f9304cd7e5af1eb') { throw 'This client uses different update trust. Its custom configuration was preserved; use the official client for this signed module.' }
} finally { $bsIdentity.Dispose() }
$bsVersion=(Get-Item -LiteralPath $bsClient).VersionInfo.ProductVersion.Split('+')[0]
& dotnet run --project (Join-Path $bsRoot 'tools/AddBuilderSimulator') -c Release -- (Join-Path $bsRoot 'assets/updates/release-public.pem') $bsVersion
if ($LASTEXITCODE -ne 0) { throw 'Builder Simulator setup did not complete. Follow the error above and retry; signed cached packages are preserved.' }
