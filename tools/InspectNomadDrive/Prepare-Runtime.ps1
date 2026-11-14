# The game ships a stripped Windows Mono framework. Keep unstripped BCLs isolated
# under Doorstop's search override; never replace the game's Managed directory.
[CmdletBinding()]
param([string]$RuntimeDirectory)
$ErrorActionPreference = 'Stop'
$ndRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ndResources = Get-Content (Join-Path $PSScriptRoot 'runtime-resources.json') -Raw | ConvertFrom-Json
if (!$RuntimeDirectory) { $RuntimeDirectory = Join-Path $ndRoot '.tools/nomad-runtime/6000.0.73-win32' }
$RuntimeDirectory = [IO.Path]::GetFullPath($RuntimeDirectory)
New-Item -ItemType Directory -Force $RuntimeDirectory | Out-Null
if (@($ndResources.files | Where-Object { !(Test-Path -LiteralPath (Join-Path $RuntimeDirectory $_.name)) }).Count) {
    # Reuse immutable, individually hash-pinned framework bytes after the first
    # publication. The initial build seeds this cache from the verified editor.
    $ndArchive = Join-Path $RuntimeDirectory 'published-runtime.zip'
    if (!(Test-Path -LiteralPath $ndArchive)) { Invoke-WebRequest $ndResources.cache_package_url -OutFile $ndArchive }
    $ndZip = [IO.Compression.ZipFile]::OpenRead($ndArchive)
    try {
        foreach ($ndFile in $ndResources.files) {
            $ndEntryName = 'payload/nomad-drive/BepInEx/nomad-runtime/' + $ndFile.name
            $ndEntries = @($ndZip.Entries | Where-Object FullName -CEQ $ndEntryName)
            if ($ndEntries.Count -ne 1 -or $ndEntries[0].Length -gt 20000000) { throw "Missing or invalid Windows framework entry: $ndEntryName" }
            $ndTarget = Join-Path $RuntimeDirectory $ndFile.name
            if (!(Test-Path -LiteralPath $ndTarget)) { [IO.Compression.ZipFileExtensions]::ExtractToFile($ndEntries[0], $ndTarget, $false) }
        }
    } finally { $ndZip.Dispose() }
}
foreach ($ndFile in $ndResources.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $RuntimeDirectory $ndFile.name)).Hash -ine $ndFile.sha256) {
        throw "Windows Mono framework checksum mismatch: $($ndFile.name). Preserve the cache and supply the verified unityjit-win32 files in a separate RuntimeDirectory."
    }
}
return $RuntimeDirectory
