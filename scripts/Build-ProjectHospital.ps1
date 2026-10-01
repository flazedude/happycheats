[CmdletBinding()]
param(
    [string]$GameDirectory = 'E:\Games\SteamLibrary\steamapps\common\Project Hospital',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$phRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$phGame = [IO.Path]::GetFullPath($GameDirectory)
$phAssembly = Join-Path $phGame 'ProjectHospital_Data\Managed\Assembly-CSharp.dll'
$phExpectedGameHash = '1120d899fb74d16f31115c5c27325edbd1154b68e9419e6b7ea03cc3d43b6f24'
if (!(Test-Path -LiteralPath $phAssembly)) { throw 'GameDirectory must contain ProjectHospital_Data/Managed/Assembly-CSharp.dll.' }
if ((Get-FileHash -LiteralPath $phAssembly -Algorithm SHA256).Hash.ToLowerInvariant() -ne $phExpectedGameHash) { throw 'This Project Hospital assembly has not been inspected. Review bindings before updating support.' }
$phLoader = & (Join-Path $phRoot 'tools\InspectProjectHospital\Prepare-BepInEx.ps1')
& dotnet build (Join-Path $phRoot 'modules\project-hospital\ProjectHospital.Trainer.csproj') -c $Configuration --nologo --verbosity minimal "-p:GameDirectory=$phGame"
if ($LASTEXITCODE -ne 0) { throw 'Project Hospital trainer build failed.' }
$phOutput = Join-Path $phRoot 'payload\project-hospital'
New-Item -ItemType Directory -Path $phOutput -Force | Out-Null
$phLicensePath = Join-Path $phOutput 'licenses'
New-Item -ItemType Directory -Path $phLicensePath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $phRoot '.tools\bepinex\5.4.23.5-x64\LICENSE.txt') -Destination (Join-Path $phLicensePath 'BepInEx-LICENSE.txt') -Force
$phResourcesPath = Join-Path $phRoot 'tools\InspectChineseParents\third-party-resources.json'
$phResources = Get-Content -LiteralPath $phResourcesPath -Raw | ConvertFrom-Json
foreach ($phResource in $phResources) {
    $phDestination = Join-Path $phLicensePath $phResource.file
    if (!(Test-Path -LiteralPath $phDestination)) {
        $phCached = Join-Path $phRoot ('payload\chinese-parents\licenses\' + $phResource.file)
        if (Test-Path -LiteralPath $phCached) { Copy-Item -LiteralPath $phCached -Destination $phDestination }
        else { Invoke-WebRequest -Uri $phResource.url -OutFile $phDestination }
    }
    if ((Get-FileHash -LiteralPath $phDestination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $phResource.sha256) { throw "Third-party resource hash mismatch: $($phResource.file)" }
}
Copy-Item -LiteralPath $phResourcesPath -Destination (Join-Path $phLicensePath 'third-party-resources.json') -Force
Copy-Item -LiteralPath (Join-Path $phRoot 'tools\InspectProjectHospital\THIRD-PARTY-NOTICES.md') -Destination (Join-Path $phLicensePath 'THIRD-PARTY-NOTICES.md') -Force
$phPaths = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')
$phPaths += Get-ChildItem -LiteralPath (Join-Path $phLoader 'BepInEx\core') -File -Filter '*.dll' | ForEach-Object { 'BepInEx/core/' + $_.Name }
foreach ($phRelative in $phPaths) {
    $phDestination = Join-Path $phOutput $phRelative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($phDestination)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $phLoader $phRelative) -Destination $phDestination -Force
}
$phPluginRoot = 'BepInEx/plugins/HappyCheats.ProjectHospital/'
foreach ($phDll in @('HappyCheats.ProjectHospital.dll')) {
    $phRelative = $phPluginRoot + $phDll
    $phDestination = Join-Path $phOutput $phRelative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($phDestination)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $phRoot "modules\project-hospital\bin\$Configuration\net35\$phDll") -Destination $phDestination -Force
    $phPaths += $phRelative
}
[ordered]@{
    schema_version = 1; game_id = 'project-hospital'; steam_app_id = 868360; version = '0.1.0'; build_id = '21161440'; game_assembly_sha256 = $phExpectedGameHash
    loader = Get-Content -LiteralPath (Join-Path $phRoot '.tools\bepinex\5.4.23.5-x64\provenance.json') -Raw | ConvertFrom-Json
    files = @($phPaths | ForEach-Object { [ordered]@{ path = $_.Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath (Join-Path $phOutput $_) -Algorithm SHA256).Hash.ToLowerInvariant() } })
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $phOutput 'package.json') -Encoding utf8
Write-Host "Project Hospital payload ready: $phOutput ($($phPaths.Count) owned files)."
