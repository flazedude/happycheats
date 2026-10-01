[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$sdRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sdTools = Join-Path $sdRoot '.tools\smapi\4.5.2'
$sdZip = Join-Path $sdTools 'SMAPI-4.5.2-installer.zip'
$sdExpected = 'dd01ddca7b566bfe0d3b3d2d03833496abc56c53da976241f2ab443f5484acc4'
$sdUrl = 'https://github.com/Pathoschild/SMAPI/releases/download/4.5.2/SMAPI-4.5.2-installer.zip'
New-Item -ItemType Directory -Path $sdTools -Force | Out-Null
if (!(Test-Path -LiteralPath $sdZip)) { Invoke-WebRequest -Uri $sdUrl -OutFile $sdZip }
if ((Get-FileHash -LiteralPath $sdZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sdExpected) { throw 'SMAPI reference archive checksum does not match its official release digest.' }
Expand-Archive -LiteralPath $sdZip -DestinationPath $sdTools -Force
$sdReferences = Join-Path $sdTools 'reference'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sdArchive = [IO.Compression.ZipFile]::OpenRead((Join-Path $sdTools 'SMAPI 4.5.2 installer\internal\windows\install.dat'))
try {
    foreach ($sdEntry in $sdArchive.Entries) {
        $sdTarget = [IO.Path]::GetFullPath((Join-Path $sdReferences $sdEntry.FullName))
        if (!$sdTarget.StartsWith(([IO.Path]::GetFullPath($sdReferences) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) { throw 'SMAPI archive contains an unsafe path.' }
        if (!$sdEntry.Name) { New-Item -ItemType Directory -Path $sdTarget -Force | Out-Null; continue }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($sdTarget)) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($sdEntry, $sdTarget, $true)
    }
} finally { $sdArchive.Dispose() }
# Build references only. Prepare-SmapiBundle.ps1 packages the official installer separately.
Write-Output $sdReferences
