[CmdletBinding()]
param([string]$GameDirectory='G:\Games\SteamLib\steamapps\common\Survival Log')
$ErrorActionPreference='Stop'
$slEvidence=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verified-build.json') -Raw | ConvertFrom-Json
foreach($slFile in @(
    @{Path='GameAssembly.dll';Hash=$slEvidence.game_assembly_sha256},
    @{Path='SurvivalLog_Data/il2cpp_data/Metadata/global-metadata.dat';Hash=$slEvidence.metadata_sha256}
)) {
    $slPath=Join-Path $GameDirectory $slFile.Path
    if(!(Test-Path -LiteralPath $slPath) -or (Get-FileHash -LiteralPath $slPath -Algorithm SHA256).Hash -ine $slFile.Hash){throw "Unsupported Survival Log file: $($slFile.Path). Inspect the new build before publishing."}
}
Write-Host "PASS Survival Log Steam build $($slEvidence.steam_build): inspected native assembly and IL2CPP metadata hashes; no game launched."
