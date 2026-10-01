#requires -Version 5.1
[CmdletBinding()]
param([string]$SourceProject = (Join-Path $PSScriptRoot '..\..\cheats-laundry'))
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskPayload = Join-Path $taskRoot 'payload\laundry'
New-Item -ItemType Directory -Path $taskPayload -Force | Out-Null

function Copy-VerifiedDependency {
    param([string]$Source, [string]$Destination, [string]$Expected, [string]$Url)
    if (!(Test-Path -LiteralPath $Destination -PathType Leaf)) {
        if (Test-Path -LiteralPath $Source -PathType Leaf) { Copy-Item -LiteralPath $Source -Destination $Destination }
        else { Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing }
    }
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ine $Expected) {
        throw "Dependency checksum mismatch: $Destination"
    }
}

Copy-VerifiedDependency -Source (Join-Path $SourceProject '.tools\MelonLoader\v0.7.3\MelonLoader.x64.zip') -Destination (Join-Path $taskPayload 'loader.zip') -Expected '5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416' -Url 'https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip'
Copy-VerifiedDependency -Source (Join-Path $SourceProject '.tools\Prerequisites\6.0.36\windowsdesktop-runtime-6.0.36-win-x64.exe') -Destination (Join-Path $taskPayload 'windowsdesktop-runtime-6.0.36-win-x64.exe') -Expected '0D20DEBB26FC8B2BC84F25FBD9D4596A6364AF8517EBF012E8B871127B798941' -Url 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/6.0.36/windowsdesktop-runtime-6.0.36-win-x64.exe'
$taskSignature = Get-AuthenticodeSignature -LiteralPath (Join-Path $taskPayload 'windowsdesktop-runtime-6.0.36-win-x64.exe')
if ($taskSignature.Status -ne 'Valid' -or $taskSignature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw 'The bundled Microsoft runtime must have a valid Microsoft signature.'
}

$taskNotices = Join-Path $taskPayload 'notices'
New-Item -ItemType Directory -Path $taskNotices -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'modules\laundry\licenses') -File | Copy-Item -Destination $taskNotices -Force
$taskNoticeSource = Join-Path $SourceProject '.tools\Prerequisites\licenses'
if (Test-Path -LiteralPath $taskNoticeSource) {
    Get-ChildItem -LiteralPath $taskNoticeSource -File | Copy-Item -Destination $taskNotices -Force
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'modules\laundry\docs\distribution-notices.md') -Destination (Join-Path $taskNotices 'distribution-notices.md') -Force
if (!(Test-Path -LiteralPath (Join-Path $taskNotices 'GPL-3.0.txt'))) {
    throw 'Dependency license notices are missing. Supply -SourceProject pointing to the original Laundry project or restore payload/laundry/notices.'
}

& dotnet build (Join-Path $taskRoot 'modules\laundry\LaundryTrainer\LaundryTrainer.csproj') -c Release -p:RestoreLockedMode=true
if ($LASTEXITCODE -ne 0) { throw 'Laundry trainer build failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'modules\laundry\LaundryTrainer\bin\Release\net6.0\LaundryTrainer.dll') -Destination (Join-Path $taskPayload 'LaundryTrainer.dll') -Force
$taskManifest = [ordered]@{
    schemaVersion = 1
    trainerVersion = '0.3.0'
    trainerSha256 = (Get-FileHash -LiteralPath (Join-Path $taskPayload 'LaundryTrainer.dll') -Algorithm SHA256).Hash
    loaderVersion = '0.7.3'
    loaderSha256 = '5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416'
    runtimeVersion = '6.0.36'
    runtimeSha256 = '0D20DEBB26FC8B2BC84F25FBD9D4596A6364AF8517EBF012E8B871127B798941'
    steamAppId = '3150440'
    steamBuild = '22778860'
}
[IO.File]::WriteAllText((Join-Path $taskPayload 'manifest.json'), ($taskManifest | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
Write-Output "Laundry trainer and verified dependencies prepared at $taskPayload"
