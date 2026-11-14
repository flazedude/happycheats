#requires -Version 7.0
[CmdletBinding()]
param([string]$GameDirectory='G:\Games\SteamLib\steamapps\common\Nomad Drive')
$ErrorActionPreference='Stop'
$ndRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ndPayload=Join-Path $ndRoot 'payload/nomad-drive'
$ndManifest=Get-Content -LiteralPath (Join-Path $ndPayload 'package.json') -Raw | ConvertFrom-Json
if ($ndManifest.game_id -cne 'nomad-drive' -or $ndManifest.version -cne '1.0.1' -or $ndManifest.steam_app_id -ne 2223980 -or $ndManifest.loader.architecture -cne 'win-x64') { throw 'Invalid Nomad Drive payload identity.' }
foreach ($ndFile in $ndManifest.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $ndPayload $ndFile.path)).Hash -ine $ndFile.sha256) { throw "Nomad Drive payload hash failed: $($ndFile.path)" }
}
if (Get-ChildItem -LiteralPath $ndPayload -Recurse -File | Where-Object Name -In @('Assembly-CSharp.dll','UnityEngine.dll','Mirror.dll','Newtonsoft.Json.dll')) { throw 'Game assemblies must never be distributed.' }
$ndAssembly=Join-Path $GameDirectory 'Nomad Drive_Data/Managed/Assembly-CSharp.dll'
if (Test-Path -LiteralPath $ndAssembly) {
    & dotnet run --project (Join-Path $ndRoot 'tools/InspectNomadDrive') -c Release -- $ndAssembly
    if ($LASTEXITCODE -ne 0) { throw 'Installed Nomad Drive binding verification failed.' }
} else { Write-Host 'Nomad Drive is not installed on this PC; installed-build binding check skipped.' }
Write-Host "Nomad Drive: $($ndManifest.files.Count) payload hashes and offline build checks passed. No game was launched."
