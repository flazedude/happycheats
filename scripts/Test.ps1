[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# These suites use temporary fixtures and never start an installed game.
& (Join-Path $PSScriptRoot 'Test-PublisherConfiguration.ps1')
& (Join-Path $PSScriptRoot 'Test-GitHubRelease.ps1')
& (Join-Path $PSScriptRoot 'Test-GitHubPublishLock.ps1')
& dotnet run --project (Join-Path $hcRoot 'tests\HappyCheats.Bootstrap.Tests') -c Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw 'Single-EXE extraction test suite failed.' }
foreach ($hcTest in @('tests\HappyCheats.Tests', 'tools\InspectChineseParents\Tests', 'modules\laundry\tests\LaundryTrainer.Tests', 'modules\laundry\tests\LaundryTrainer.Setup.Tests', 'modules\laundry\tests\LaundryBridge.Tests', 'modules\stardew\tests\HappyCheats.StardewValley.Tests')) {
    & dotnet run --project (Join-Path $hcRoot $hcTest) -c Release --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "Test suite failed: $hcTest" }
}
# Exercise the real modal Updates path: its busy owner must keep the library themed.
& dotnet run --project (Join-Path $hcRoot 'tests\HappyCheats.App.Tests') -c Release --no-launch-profile -- --data-root $hcRoot
if ($LASTEXITCODE -ne 0) { throw 'WPF modal-update regression suite failed.' }
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
