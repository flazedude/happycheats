[CmdletBinding()]
param(
    [string]$GameDirectory = 'D:\Steam\steamapps\common\Stardew Valley',
    [string]$SmapiDirectory,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$sdRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sdGame = [IO.Path]::GetFullPath($GameDirectory)
$sdAssembly = Join-Path $sdGame 'Stardew Valley.dll'
if (!(Test-Path -LiteralPath $sdAssembly -PathType Leaf)) { throw 'GameDirectory must contain Stardew Valley.dll. Finish the Steam installation first.' }
$sdVersion = [Reflection.AssemblyName]::GetAssemblyName($sdAssembly).Version
if ($sdVersion.Major -ne 1 -or $sdVersion.Minor -ne 6 -or $sdVersion.Build -lt 15) { throw "Unsupported Stardew Valley assembly version: $sdVersion" }
if (!$SmapiDirectory) {
    if (Test-Path -LiteralPath (Join-Path $sdGame 'StardewModdingAPI.dll')) { $SmapiDirectory = $sdGame }
    else { $SmapiDirectory = & (Join-Path $PSScriptRoot 'Prepare-StardewReferences.ps1') }
}
& dotnet build (Join-Path $sdRoot 'modules\stardew\HappyCheats.StardewValley') -c $Configuration --nologo -v minimal "-p:GameDirectory=$sdGame" "-p:SmapiDirectory=$SmapiDirectory"
if ($LASTEXITCODE -ne 0) { throw 'Stardew Valley mod build failed.' }
& dotnet run --project (Join-Path $sdRoot 'modules\stardew\tests\HappyCheats.StardewValley.HookChecks') -c Release "-p:GameDirectory=$sdGame" "-p:SmapiDirectory=$SmapiDirectory" "-p:ModAssembly=$(Join-Path $sdRoot "modules\stardew\HappyCheats.StardewValley\bin\$Configuration\net6.0\HappyCheats.StardewValley.dll")" -- $sdGame $SmapiDirectory
if ($LASTEXITCODE -ne 0) { throw 'Stardew Valley hook checks failed. Payload was not updated.' }
$sdOutput = Join-Path $sdRoot 'payload\stardew'
& (Join-Path $PSScriptRoot 'Prepare-SmapiBundle.ps1')
$sdMod = 'Mods/HappyCheats.StardewValley/'
$sdFiles = @(($sdMod + 'HappyCheats.StardewValley.dll'), ($sdMod + 'manifest.json'), ($sdMod + 'bridge/README.txt'))
$sdSources = @(
    (Join-Path $sdRoot "modules\stardew\HappyCheats.StardewValley\bin\$Configuration\net6.0\HappyCheats.StardewValley.dll"),
    (Join-Path $sdRoot 'modules\stardew\HappyCheats.StardewValley\manifest.json'),
    (Join-Path $sdRoot 'modules\stardew\HappyCheats.StardewValley\bridge\README.txt')
)
for ($sdIndex = 0; $sdIndex -lt $sdFiles.Count; $sdIndex++) {
    $sdTarget = Join-Path $sdOutput $sdFiles[$sdIndex]
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($sdTarget)) | Out-Null
    Copy-Item -LiteralPath $sdSources[$sdIndex] -Destination $sdTarget -Force
}
$sdManifest = [ordered]@{
    schema_version = 1; game_id = 'stardew'; steam_app_id = 413150; version = '0.1.1'
    inspected_game_version = $sdVersion.ToString(); game_assembly_sha256 = (Get-FileHash -LiteralPath $sdAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
    minimum_smapi_version = '4.5.2'; bundled_smapi_version = '4.5.2'; smapi_url = 'https://smapi.io/'; gameplay_validated = $false
    files = @($sdFiles | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $sdOutput $_) -Algorithm SHA256).Hash.ToLowerInvariant() } })
}
$sdJson = $sdManifest | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText((Join-Path $sdOutput 'package.json'), $sdJson, [Text.UTF8Encoding]::new($false))
Write-Host "Stardew Valley payload ready: $sdOutput (includes the official offline SMAPI installer)."
