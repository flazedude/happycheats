[CmdletBinding()]
param(
    [string]$GameDirectory = 'G:\Games\SteamLib\steamapps\common\Nomad Drive',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$ndRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ndGame = [IO.Path]::GetFullPath($GameDirectory)
$ndAssembly = Join-Path $ndGame 'Nomad Drive_Data\Managed\Assembly-CSharp.dll'
$ndExpectedGameHash = '6957f9a1180d811ca2ae1bb44748157d794fed5befd845290fd15e41fb54ffaf'
if (!(Test-Path -LiteralPath $ndAssembly)) { throw 'GameDirectory must contain Nomad Drive_Data/Managed/Assembly-CSharp.dll.' }
if ((Get-FileHash -LiteralPath $ndAssembly -Algorithm SHA256).Hash.ToLowerInvariant() -ne $ndExpectedGameHash) { throw 'This Nomad Drive assembly has not been inspected. Review bindings before updating support.' }
& dotnet run --project (Join-Path $ndRoot "tools/InspectNomadDrive") -c Release -- $ndAssembly
if ($LASTEXITCODE -ne 0) { throw "Nomad Drive binding validation failed." }
$ndLoader = & (Join-Path $ndRoot 'tools\InspectProjectHospital\Prepare-BepInEx.ps1')
& dotnet build (Join-Path $ndRoot 'modules\nomad-drive\NomadDrive.Trainer.csproj') -c $Configuration --nologo --verbosity minimal "-p:GameDirectory=$ndGame"
if ($LASTEXITCODE -ne 0) { throw 'Nomad Drive trainer build failed.' }
$ndOutput = Join-Path $ndRoot 'payload\nomad-drive'
New-Item -ItemType Directory -Path $ndOutput -Force | Out-Null
$ndLicensePath = Join-Path $ndOutput 'licenses'
New-Item -ItemType Directory -Path $ndLicensePath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $ndRoot '.tools\bepinex\5.4.23.5-x64\LICENSE.txt') -Destination (Join-Path $ndLicensePath 'BepInEx-LICENSE.txt') -Force
$ndResourcesPath = Join-Path $ndRoot 'tools\InspectChineseParents\third-party-resources.json'
$ndResources = Get-Content -LiteralPath $ndResourcesPath -Raw | ConvertFrom-Json
foreach ($ndResource in $ndResources) {
    $ndDestination = Join-Path $ndLicensePath $ndResource.file
    if (!(Test-Path -LiteralPath $ndDestination)) {
        $ndCached = Join-Path $ndRoot ('payload\chinese-parents\licenses\' + $ndResource.file)
        if (Test-Path -LiteralPath $ndCached) { Copy-Item -LiteralPath $ndCached -Destination $ndDestination }
        else { Invoke-WebRequest -Uri $ndResource.url -OutFile $ndDestination }
    }
    if ((Get-FileHash -LiteralPath $ndDestination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $ndResource.sha256) { throw "Third-party resource hash mismatch: $($ndResource.file)" }
}
Copy-Item -LiteralPath $ndResourcesPath -Destination (Join-Path $ndLicensePath 'third-party-resources.json') -Force
Copy-Item -LiteralPath (Join-Path $ndRoot 'tools\InspectNomadDrive\THIRD-PARTY-NOTICES.md') -Destination (Join-Path $ndLicensePath 'THIRD-PARTY-NOTICES.md') -Force
$ndPaths = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')
$ndPaths += Get-ChildItem -LiteralPath (Join-Path $ndLoader 'BepInEx\core') -File -Filter '*.dll' | ForEach-Object { 'BepInEx/core/' + $_.Name }
foreach ($ndRelative in $ndPaths) {
    $ndDestination = Join-Path $ndOutput $ndRelative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($ndDestination)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $ndLoader $ndRelative) -Destination $ndDestination -Force
}
$ndPluginRoot = 'BepInEx/plugins/HappyCheats.NomadDrive/'
foreach ($ndDll in @('HappyCheats.NomadDrive.dll')) {
    $ndRelative = $ndPluginRoot + $ndDll
    $ndDestination = Join-Path $ndOutput $ndRelative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($ndDestination)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $ndRoot "modules\nomad-drive\bin\$Configuration\netstandard2.1\$ndDll") -Destination $ndDestination -Force
    $ndPaths += $ndRelative
}
[ordered]@{
    schema_version = 1; game_id = 'nomad-drive'; steam_app_id = 2223980; version = '1.0.0'; build_id = '26391437'; game_assembly_sha256 = $ndExpectedGameHash
    loader = Get-Content -LiteralPath (Join-Path $ndRoot '.tools\bepinex\5.4.23.5-x64\provenance.json') -Raw | ConvertFrom-Json
    files = @($ndPaths | ForEach-Object { [ordered]@{ path = $_.Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath (Join-Path $ndOutput $_) -Algorithm SHA256).Hash.ToLowerInvariant() } })
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ndOutput 'package.json') -Encoding utf8
Write-Host "Nomad Drive payload ready: $ndOutput ($($ndPaths.Count) owned files)."
