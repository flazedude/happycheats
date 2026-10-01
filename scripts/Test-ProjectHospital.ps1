[CmdletBinding()]
param(
    [string]$GameDirectory = 'E:\Games\SteamLibrary\steamapps\common\Project Hospital',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$phRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& dotnet run --project (Join-Path $phRoot 'modules\project-hospital\tests\ProjectHospital.Trainer.Tests.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Project Hospital offline runtime tests failed.' }
$phAssembly = Join-Path $GameDirectory 'ProjectHospital_Data\Managed\Assembly-CSharp.dll'
if (Test-Path -LiteralPath $phAssembly) {
    & (Join-Path $phRoot 'tools\InspectProjectHospital\Validate-GameBindings.ps1') -GameAssembly $phAssembly
    if ($LASTEXITCODE -ne 0) { throw 'Project Hospital static binding verification failed.' }
} else { Write-Host 'Installed Project Hospital assembly not found; live-build static binding inspection skipped (fixture and payload tests still run).' }
$phPayload = Join-Path $phRoot 'payload\project-hospital'
$phPackage = Get-Content -LiteralPath (Join-Path $phPayload 'package.json') -Raw | ConvertFrom-Json
if ($phPackage.game_id -ne 'project-hospital' -or $phPackage.steam_app_id -ne 868360 -or $phPackage.loader.architecture -ne 'win-x64') { throw 'Project Hospital package identity is invalid.' }
foreach ($phFile in $phPackage.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $phPayload $phFile.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $phFile.sha256) { throw "Project Hospital payload hash mismatch: $($phFile.path)" }
}
Write-Host "Project Hospital runtime scenarios and $($phPackage.files.Count) payload hashes passed (offline; no gameplay run)."
