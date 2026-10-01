[CmdletBinding()]
param(
    [string]$PackageDirectory,
    [string]$OutputPath,
    [switch]$Zip,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$PackageDirectory) { $PackageDirectory = Join-Path $hcRoot 'dist\HappyCheats' }
if (!$OutputPath) { $OutputPath = Join-Path $hcRoot 'dist\HappyCheats.exe' }
$hcPackage = [IO.Path]::GetFullPath($PackageDirectory)
$hcOutput = [IO.Path]::GetFullPath($OutputPath)
if ($hcOutput.StartsWith(($hcPackage.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'The single EXE must be outside its source package folder.' }
foreach ($hcRequired in @('HappyCheats.exe','HappyCheats.dll','HappyCheats.runtimeconfig.json','updater\HappyCheats.Updater.exe','updater\HappyCheats.Updater.dll','updater\HappyCheats.Updater.runtimeconfig.json')) {
    $hcFile = Join-Path $hcPackage $hcRequired
    if (!(Test-Path -LiteralPath $hcFile -PathType Leaf) -or (Get-Item -LiteralPath $hcFile).Length -eq 0) { throw "Build a complete release first. Missing: $hcRequired" }
}
if (!(Test-Path -LiteralPath (Join-Path $hcPackage 'payload') -PathType Container)) { throw 'The release is missing its trainer payloads.' }
$hcRuntime = (Get-Content -LiteralPath (Join-Path $hcPackage 'HappyCheats.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions
if ($hcRuntime.framework -or $hcRuntime.frameworks) {
    throw 'Single-EXE distribution requires a self-contained client. Rebuild without -FrameworkDependent.'
}
$hcVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $hcPackage 'HappyCheats.dll')).Version
$hcVersionText = '{0}.{1}.{2}' -f $hcVersion.Major,$hcVersion.Minor,$hcVersion.Build
if (!$SkipTests) {
    & dotnet run --project (Join-Path $hcRoot 'tests\HappyCheats.Bootstrap.Tests') -c Release --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw 'Single-EXE extraction tests failed.' }
}
$hcStage = Join-Path $hcRoot ('artifacts\single-exe\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $hcStage | Out-Null
$hcArchive = Join-Path $hcStage 'HappyCheats.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Build ZIP paths explicitly, independent of the source folder's display name.
$hcZip = [IO.Compression.ZipFile]::Open($hcArchive, [IO.Compression.ZipArchiveMode]::Create)
try {
    $hcQueue = [Collections.Generic.Queue[string]]::new()
    $hcQueue.Enqueue($hcPackage)
    while ($hcQueue.Count -gt 0) {
        $hcDirectory = Get-Item -LiteralPath $hcQueue.Dequeue()
        if ($hcDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked package directories are unsupported.' }
        foreach ($hcItem in Get-ChildItem -LiteralPath $hcDirectory.FullName -Force) {
            if ($hcItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked package files are unsupported.' }
            if ($hcItem.PSIsContainer) { $hcQueue.Enqueue($hcItem.FullName); continue }
            $hcRelative = $hcItem.FullName.Substring($hcPackage.TrimEnd('\').Length + 1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($hcZip,$hcItem.FullName,('HappyCheats/' + $hcRelative),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
} finally { $hcZip.Dispose() }
$hcHash = (Get-FileHash -LiteralPath $hcArchive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($hcArchive + '.sha256'), $hcHash, [Text.UTF8Encoding]::new($false))
$hcPublished = Join-Path $hcStage 'publish'
& dotnet publish (Join-Path $hcRoot 'src\HappyCheats.Bootstrap') -c Release -r win-x64 --self-contained true "-p:BundleArchive=$hcArchive" "-p:Version=$hcVersionText" -o $hcPublished --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Single-EXE launcher publish failed.' }
$hcFiles = @(Get-ChildItem -LiteralPath $hcPublished -File)
if ($hcFiles.Count -ne 1 -or $hcFiles[0].Name -ne 'HappyCheats.Bootstrap.exe') { throw 'The bootstrap publish did not produce exactly one EXE.' }
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($hcOutput)) | Out-Null
# Atomic replacement preserves a previously working EXE if the build fails.
$hcTemporary = $hcOutput + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
Copy-Item -LiteralPath $hcFiles[0].FullName -Destination $hcTemporary
if (Test-Path -LiteralPath $hcOutput) { [IO.File]::Replace($hcTemporary,$hcOutput,(Join-Path $hcStage 'previous.exe')) }
else { [IO.File]::Move($hcTemporary,$hcOutput) }
Write-Host "Single EXE ready: $hcOutput ($([Math]::Round((Get-Item -LiteralPath $hcOutput).Length / 1MB,1)) MiB)"
Write-Host 'Only this EXE is needed for distribution. Working files are extracted into the user AppData folder on first launch.'
if ($Zip) {
    $hcZipOutput = Join-Path ([IO.Path]::GetDirectoryName($hcOutput)) ("HappyCheats-$hcVersionText-win-x64-single-exe.zip")
    $hcZipStage = Join-Path $hcStage 'single-exe.zip'
    $hcSingleArchive = [IO.Compression.ZipFile]::Open($hcZipStage, [IO.Compression.ZipArchiveMode]::Create)
    try {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($hcSingleArchive, $hcOutput, 'HappyCheats.exe', [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    } finally { $hcSingleArchive.Dispose() }
    if (Test-Path -LiteralPath $hcZipOutput) { [IO.File]::Replace($hcZipStage, $hcZipOutput, (Join-Path $hcStage 'previous-single-exe.zip')) }
    else { [IO.File]::Move($hcZipStage, $hcZipOutput) }
    (Get-FileHash -LiteralPath $hcZipOutput -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath ($hcZipOutput + '.sha256') -Encoding ascii
    Write-Host "Versioned single-EXE ZIP: $hcZipOutput"
}
