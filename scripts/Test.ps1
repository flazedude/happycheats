[CmdletBinding()]
param([string]$IndependentGameId)
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($IndependentGameId -and $IndependentGameId -cnotin (Get-Content (Join-Path $hcRoot 'modules/cheat-versions.json') -Raw | ConvertFrom-Json).PSObject.Properties.Name) { throw 'Unknown independent game test scope.' }
# These suites use temporary fixtures and never start an installed game.
& (Join-Path $PSScriptRoot 'Test-SourceSync.ps1')
& (Join-Path $PSScriptRoot 'Test-SourceSyncSafety.ps1')
& (Join-Path $PSScriptRoot 'Test-PublisherConfiguration.ps1')
& (Join-Path $PSScriptRoot 'Test-LanMirror.ps1')
& (Join-Path $PSScriptRoot 'Test-LanMirrorDownload.ps1')
& (Join-Path $PSScriptRoot 'Test-LanMirrorSupervisor.ps1')
& (Join-Path $PSScriptRoot 'Test-ReleaseConfiguration.ps1')
& (Join-Path $PSScriptRoot 'Test-GitHubRelease.ps1')
& (Join-Path $PSScriptRoot 'Test-GitHubPublishLock.ps1')
& (Join-Path $PSScriptRoot 'Test-CheatUpdates.ps1')
& (Join-Path $PSScriptRoot 'Test-GpdDeploymentIsolation.ps1')
& dotnet run --project (Join-Path $hcRoot 'tests\HappyCheats.Bootstrap.Tests') -c Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw 'Single-EXE extraction test suite failed.' }
foreach ($hcTest in @('modules/builder-simulator/tests','modules/nomad-drive/tests', 'tests\HappyCheats.Tests', 'tools\InspectChineseParents\Tests', 'modules\laundry\tests\LaundryTrainer.Tests', 'modules\laundry\tests\LaundryTrainer.Setup.Tests', 'modules\laundry\tests\LaundryBridge.Tests', 'modules\stardew\tests\HappyCheats.StardewValley.Tests')) {
    & dotnet run --project (Join-Path $hcRoot $hcTest) -c Release --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "Test suite failed: $hcTest" }
}
# Exercise modeless Updates: downloads and progress must leave trainer controls usable.
& dotnet run --project (Join-Path $hcRoot 'tests\HappyCheats.App.Tests') -c Release --no-launch-profile -- --data-root $hcRoot
if ($LASTEXITCODE -ne 0) { throw 'WPF modeless-update regression suite failed.' }
& (Join-Path $PSScriptRoot 'Test-LivinCream.ps1')
& (Join-Path $PSScriptRoot 'Test-SurvivalLog.ps1')
& (Join-Path $PSScriptRoot 'Test-BuilderSimulator.ps1') -IndependentGameId $IndependentGameId
& (Join-Path $PSScriptRoot 'Test-NomadDrive.ps1')
& (Join-Path $PSScriptRoot 'Test-Astroneer.ps1')
& (Join-Path $PSScriptRoot 'Test-Dawnwalker.ps1')
& (Join-Path $PSScriptRoot 'Test-CrimsonDesert.ps1')
& (Join-Path $PSScriptRoot 'Test-Wukong.ps1')
& (Join-Path $PSScriptRoot 'Test-PhantomBlade.ps1')
& (Join-Path $PSScriptRoot 'Test-Rdr2.ps1')
& (Join-Path $PSScriptRoot 'Test-GtaV.ps1')
& (Join-Path $PSScriptRoot 'Test-ProjectHospital.ps1')
& (Join-Path $PSScriptRoot 'Test-HotelArchitect.ps1')
& (Join-Path $PSScriptRoot 'Test-CivilizationVI.ps1')
& (Join-Path $PSScriptRoot 'Test-CivilizationVII.ps1')
& (Join-Path $PSScriptRoot 'Test-Witcher3.ps1')
& (Join-Path $PSScriptRoot 'Test-LanUpdateConfiguration.ps1')
& (Join-Path $PSScriptRoot 'Test-LanUpdateServer.ps1')
