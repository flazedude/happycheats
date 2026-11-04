[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$hcRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& dotnet run --project (Join-Path $hcRoot 'tests/LivinCream.Checks') -c Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw 'Livin Cream protocol/runtime fixture checks failed.' }
& dotnet run --project (Join-Path $hcRoot 'tests/HappyCheats.Tests') -c Release --no-launch-profile -- --livin-cream
if ($LASTEXITCODE -ne 0) { throw 'Livin Cream module/installer checks failed.' }
