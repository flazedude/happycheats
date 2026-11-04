#requires -Version 7.0
[CmdletBinding()]
param([string]$GameDirectory='F:\Games\steamapps\steamapps\common\Builder Simulator')
$ErrorActionPreference='Stop'
$bsRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$bsPayload=Join-Path $bsRoot 'payload/builder-simulator'
$bsManifest=Get-Content -LiteralPath (Join-Path $bsPayload 'package.json') -Raw | ConvertFrom-Json
if ($bsManifest.game_id -cne 'builder-simulator' -or $bsManifest.version -cne '1.0.0' -or $bsManifest.steam_app_id -ne 1120320 -or $bsManifest.loader.architecture -cne 'win-x64') { throw 'Invalid Builder Simulator payload identity.' }
foreach ($bsFile in $bsManifest.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $bsPayload $bsFile.path)).Hash -ine $bsFile.sha256) { throw "Builder Simulator payload hash failed: $($bsFile.path)" }
}
if (Get-ChildItem -LiteralPath $bsPayload -Recurse -File | Where-Object Name -In @('Assembly-CSharp.dll','UnityEngine.dll','Mirror.dll','Newtonsoft.Json.dll')) { throw 'Game assemblies must never be distributed.' }
$bsAssembly=Join-Path $GameDirectory 'Builder Simulator_Data/Managed/Assembly-CSharp.dll'
if (Test-Path -LiteralPath $bsAssembly) {
    & dotnet run --project (Join-Path $bsRoot 'tools/InspectBuilderSimulator') -c Release -- $bsAssembly
    if ($LASTEXITCODE -ne 0) { throw 'Installed Builder Simulator binding verification failed.' }
} else { Write-Host 'Builder Simulator is not installed on this PC; installed-build binding check skipped.' }
& dotnet build (Join-Path $bsRoot 'tools/AddBuilderSimulator') -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Builder Simulator signed-package setup helper did not compile.' }
Write-Host "Builder Simulator: $($bsManifest.files.Count) payload hashes and offline build checks passed. No game was launched."
