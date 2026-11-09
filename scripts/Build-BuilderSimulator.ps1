[CmdletBinding()]
param(
    [string]$GameDirectory = 'F:\Games\steamapps\steamapps\common\Builder Simulator',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$bsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$bsGame = [IO.Path]::GetFullPath($GameDirectory)
$bsAssembly = Join-Path $bsGame 'Builder Simulator_Data\Managed\Assembly-CSharp.dll'
$bsExpectedGameHash = '0786962d80aa8b6bfce8561fcd1502c982b33f6856e59a5567864bb10de7279d'
if (!(Test-Path -LiteralPath $bsAssembly)) { throw 'GameDirectory must contain Builder Simulator_Data/Managed/Assembly-CSharp.dll.' }
if ((Get-FileHash -LiteralPath $bsAssembly -Algorithm SHA256).Hash.ToLowerInvariant() -ne $bsExpectedGameHash) { throw 'This Builder Simulator assembly has not been inspected. Review bindings before updating support.' }
& dotnet run --project (Join-Path $bsRoot "tools/InspectBuilderSimulator") -c Release -- $bsAssembly
if ($LASTEXITCODE -ne 0) { throw "Builder Simulator binding validation failed." }
$bsLoader = & (Join-Path $bsRoot 'tools\InspectProjectHospital\Prepare-BepInEx.ps1')
& dotnet build (Join-Path $bsRoot 'modules\builder-simulator\BuilderSimulator.Trainer.csproj') -c $Configuration --nologo --verbosity minimal "-p:GameDirectory=$bsGame"
if ($LASTEXITCODE -ne 0) { throw 'Builder Simulator trainer build failed.' }
$bsOutput = Join-Path $bsRoot 'payload\builder-simulator'
New-Item -ItemType Directory -Path $bsOutput -Force | Out-Null
$bsLicensePath = Join-Path $bsOutput 'licenses'
New-Item -ItemType Directory -Path $bsLicensePath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $bsRoot '.tools\bepinex\5.4.23.5-x64\LICENSE.txt') -Destination (Join-Path $bsLicensePath 'BepInEx-LICENSE.txt') -Force
$bsResourcesPath = Join-Path $bsRoot 'tools\InspectChineseParents\third-party-resources.json'
$bsResources = Get-Content -LiteralPath $bsResourcesPath -Raw | ConvertFrom-Json
foreach ($bsResource in $bsResources) {
    $bsDestination = Join-Path $bsLicensePath $bsResource.file
    if (!(Test-Path -LiteralPath $bsDestination)) {
        $bsCached = Join-Path $bsRoot ('payload\chinese-parents\licenses\' + $bsResource.file)
        if (Test-Path -LiteralPath $bsCached) { Copy-Item -LiteralPath $bsCached -Destination $bsDestination }
        else { Invoke-WebRequest -Uri $bsResource.url -OutFile $bsDestination }
    }
    if ((Get-FileHash -LiteralPath $bsDestination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $bsResource.sha256) { throw "Third-party resource hash mismatch: $($bsResource.file)" }
}
Copy-Item -LiteralPath $bsResourcesPath -Destination (Join-Path $bsLicensePath 'third-party-resources.json') -Force
Copy-Item -LiteralPath (Join-Path $bsRoot 'tools\InspectBuilderSimulator\THIRD-PARTY-NOTICES.md') -Destination (Join-Path $bsLicensePath 'THIRD-PARTY-NOTICES.md') -Force
$bsPaths = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')
$bsPaths += Get-ChildItem -LiteralPath (Join-Path $bsLoader 'BepInEx\core') -File -Filter '*.dll' | ForEach-Object { 'BepInEx/core/' + $_.Name }
foreach ($bsRelative in $bsPaths) {
    $bsDestination = Join-Path $bsOutput $bsRelative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($bsDestination)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $bsLoader $bsRelative) -Destination $bsDestination -Force
}
$bsPluginRoot = 'BepInEx/plugins/HappyCheats.BuilderSimulator/'
foreach ($bsDll in @('HappyCheats.BuilderSimulator.dll')) {
    $bsRelative = $bsPluginRoot + $bsDll
    $bsDestination = Join-Path $bsOutput $bsRelative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($bsDestination)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $bsRoot "modules\builder-simulator\bin\$Configuration\netstandard2.1\$bsDll") -Destination $bsDestination -Force
    $bsPaths += $bsRelative
}
[ordered]@{
    schema_version = 1; game_id = 'builder-simulator'; steam_app_id = 1120320; version = '1.0.1'; build_id = '26462579'; game_assembly_sha256 = $bsExpectedGameHash
    loader = Get-Content -LiteralPath (Join-Path $bsRoot '.tools\bepinex\5.4.23.5-x64\provenance.json') -Raw | ConvertFrom-Json
    files = @($bsPaths | ForEach-Object { [ordered]@{ path = $_.Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath (Join-Path $bsOutput $_) -Algorithm SHA256).Hash.ToLowerInvariant() } })
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $bsOutput 'package.json') -Encoding utf8
Write-Host "Builder Simulator payload ready: $bsOutput ($($bsPaths.Count) owned files)."
