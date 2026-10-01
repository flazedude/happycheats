[CmdletBinding()]
param(
    [string]$GameDirectory = 'E:\Games\SteamLibrary\steamapps\common\ChineseParent',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$cpRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$cpGame = [IO.Path]::GetFullPath($GameDirectory)
$cpAssembly = Join-Path $cpGame 'game_Data\Managed\Assembly-CSharp.dll'
$cpExpectedGameHash = '0262f1b24b148d99e8cba54a79b6b7299622c46be4bbc000a500d7519bf3bf3a'
if (!(Test-Path -LiteralPath $cpAssembly)) { throw 'GameDirectory must contain game_Data/Managed/Assembly-CSharp.dll.' }
if ((Get-FileHash -LiteralPath $cpAssembly -Algorithm SHA256).Hash.ToLowerInvariant() -ne $cpExpectedGameHash) {
    throw 'This game assembly has not been inspected. Run the read-only inspector and review the bindings before updating support.'
}
$cpLoader = & (Join-Path $cpRoot 'tools\InspectChineseParents\Prepare-BepInEx.ps1')
& dotnet build (Join-Path $cpRoot 'src\ChineseParents.Trainer\ChineseParents.Trainer.csproj') -c $Configuration --nologo --verbosity minimal "-p:GameDirectory=$cpGame"
if ($LASTEXITCODE -ne 0) { throw 'Chinese Parents trainer build failed.' }
$cpOutput = Join-Path $cpRoot 'payload\chinese-parents'
New-Item -ItemType Directory -Path $cpOutput -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $cpOutput 'licenses') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $cpRoot '.tools\bepinex\5.4.23.5\LICENSE.txt') -Destination (Join-Path $cpOutput 'licenses\BepInEx-LICENSE.txt') -Force
$cpResourcesPath = Join-Path $cpRoot 'tools\InspectChineseParents\third-party-resources.json'
$cpResources = Get-Content -LiteralPath $cpResourcesPath -Raw | ConvertFrom-Json
foreach ($cpResource in $cpResources) {
    $cpResourceDestination = Join-Path $cpOutput ('licenses\' + $cpResource.file)
    if (!(Test-Path -LiteralPath $cpResourceDestination)) { Invoke-WebRequest -Uri $cpResource.url -OutFile $cpResourceDestination }
    if ((Get-FileHash -LiteralPath $cpResourceDestination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $cpResource.sha256) {
        throw "Third-party resource hash mismatch: $($cpResource.file)"
    }
}
Copy-Item -LiteralPath $cpResourcesPath -Destination (Join-Path $cpOutput 'licenses\third-party-resources.json') -Force
Copy-Item -LiteralPath (Join-Path $cpRoot 'tools\InspectChineseParents\THIRD-PARTY-NOTICES.md') -Destination (Join-Path $cpOutput 'licenses\THIRD-PARTY-NOTICES.md') -Force
$cpPaths = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')
$cpPaths += Get-ChildItem -LiteralPath (Join-Path $cpLoader 'BepInEx\core') -File -Filter '*.dll' | ForEach-Object { 'BepInEx/core/' + $_.Name }
foreach ($cpRelative in $cpPaths) {
    $cpDestination = Join-Path $cpOutput $cpRelative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($cpDestination)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $cpLoader $cpRelative) -Destination $cpDestination -Force
}
$cpPluginRelative = 'BepInEx/plugins/HappyCheats.ChineseParents/HappyCheats.ChineseParents.dll'
$cpPluginDestination = Join-Path $cpOutput $cpPluginRelative
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($cpPluginDestination)) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $cpRoot "src\ChineseParents.Trainer\bin\$Configuration\netstandard2.1\HappyCheats.ChineseParents.dll") -Destination $cpPluginDestination -Force
$cpPaths += $cpPluginRelative
$cpPackage = [ordered]@{
    schema_version = 1; game_id = 'chinese-parents'; steam_app_id = 736190
    version = '0.1.0'; build_id = '24710774'; game_assembly_sha256 = $cpExpectedGameHash
    loader = Get-Content -LiteralPath (Join-Path $cpRoot '.tools\bepinex\5.4.23.5\provenance.json') -Raw | ConvertFrom-Json
    files = @($cpPaths | ForEach-Object {
        [ordered]@{ path = $_.Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath (Join-Path $cpOutput $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
}
$cpPackage | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $cpOutput 'package.json') -Encoding utf8
Write-Host "Chinese Parents payload ready: $cpOutput ($($cpPaths.Count) owned files)."
