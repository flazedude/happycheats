[CmdletBinding()]
param(
    [string]$GameDirectory = "G:\Games\SteamLib\steamapps\common\Livin' the Cream Ice Cream Simulator",
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$lcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lcGame = [IO.Path]::GetFullPath($GameDirectory)
$lcAssembly = Join-Path $lcGame "Livin' the Cream Ice Cream Simulator_Data\Managed\Assembly-CSharp.dll"
$lcExpectedGameHash = '3756614fb3952a6bacf1e2e8d1994efd7eb451d7f27218a66b033e386e3cb98e'
if (!(Test-Path -LiteralPath $lcAssembly)) { throw "GameDirectory must contain Livin' the Cream Ice Cream Simulator_Data/Managed/Assembly-CSharp.dll." }
if ((Get-FileHash -LiteralPath $lcAssembly -Algorithm SHA256).Hash.ToLowerInvariant() -ne $lcExpectedGameHash) { throw 'This Livin Cream assembly has not been inspected. Review bindings before updating support.' }
& dotnet run --project (Join-Path $lcRoot 'tools/InspectLivinCream') -c Release --no-launch-profile -- $lcGame
if ($LASTEXITCODE -ne 0) { throw 'Livin Cream static game binding inspection failed.' }
$lcLoader = & (Join-Path $lcRoot 'tools\InspectProjectHospital\Prepare-BepInEx.ps1')
& dotnet build (Join-Path $lcRoot 'src\LivinCream.Trainer\LivinCream.Trainer.csproj') -c $Configuration --nologo --verbosity minimal "-p:GameDirectory=$lcGame"
if ($LASTEXITCODE -ne 0) { throw 'Livin Cream trainer build failed.' }
$lcOutput = Join-Path $lcRoot 'payload\livin-cream'
New-Item -ItemType Directory -Path $lcOutput -Force | Out-Null
$lcLicensePath = Join-Path $lcOutput 'licenses'
New-Item -ItemType Directory -Path $lcLicensePath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $lcRoot '.tools\bepinex\5.4.23.5-x64\LICENSE.txt') -Destination (Join-Path $lcLicensePath 'BepInEx-LICENSE.txt') -Force
$lcResourcesPath = Join-Path $lcRoot 'tools\InspectChineseParents\third-party-resources.json'
$lcResources = Get-Content -LiteralPath $lcResourcesPath -Raw | ConvertFrom-Json
foreach ($lcResource in $lcResources) {
    $lcDestination = Join-Path $lcLicensePath $lcResource.file
    if (!(Test-Path -LiteralPath $lcDestination)) {
        $lcCached = Join-Path $lcRoot ('payload\chinese-parents\licenses\' + $lcResource.file)
        if (Test-Path -LiteralPath $lcCached) { Copy-Item -LiteralPath $lcCached -Destination $lcDestination }
        else { Invoke-WebRequest -Uri $lcResource.url -OutFile $lcDestination }
    }
    if ((Get-FileHash -LiteralPath $lcDestination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $lcResource.sha256) { throw "Third-party resource hash mismatch: $($lcResource.file)" }
}
Copy-Item -LiteralPath $lcResourcesPath -Destination (Join-Path $lcLicensePath 'third-party-resources.json') -Force
Copy-Item -LiteralPath (Join-Path $lcRoot 'tools\InspectLivinCream\THIRD-PARTY-NOTICES.md') -Destination (Join-Path $lcLicensePath 'THIRD-PARTY-NOTICES.md') -Force
$lcPaths = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')
$lcPaths += Get-ChildItem -LiteralPath (Join-Path $lcLoader 'BepInEx\core') -File -Filter '*.dll' | ForEach-Object { 'BepInEx/core/' + $_.Name }
foreach ($lcRelative in $lcPaths) {
    $lcDestination = Join-Path $lcOutput $lcRelative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($lcDestination)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $lcLoader $lcRelative) -Destination $lcDestination -Force
}
$lcPluginRoot = 'BepInEx/plugins/HappyCheats.LivinCream/'
foreach ($lcDll in @('HappyCheats.LivinCream.dll')) {
    $lcRelative = $lcPluginRoot + $lcDll
    $lcDestination = Join-Path $lcOutput $lcRelative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($lcDestination)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $lcRoot "src\LivinCream.Trainer\bin\$Configuration\netstandard2.1\$lcDll") -Destination $lcDestination -Force
    $lcPaths += $lcRelative
}
[ordered]@{
    schema_version = 1; game_id = 'livin-cream'; steam_app_id = 4621810; version = '1.0.0'; build_id = '26415994'; game_assembly_sha256 = $lcExpectedGameHash
    loader = Get-Content -LiteralPath (Join-Path $lcRoot '.tools\bepinex\5.4.23.5-x64\provenance.json') -Raw | ConvertFrom-Json
    files = @($lcPaths | ForEach-Object { [ordered]@{ path = $_.Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath (Join-Path $lcOutput $_) -Algorithm SHA256).Hash.ToLowerInvariant() } })
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $lcOutput 'package.json') -Encoding utf8
Write-Host "Livin Cream payload ready: $lcOutput ($($lcPaths.Count) owned files)."
