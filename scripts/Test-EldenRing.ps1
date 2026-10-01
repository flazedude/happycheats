[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcEldenRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& dotnet run --project (Join-Path $hcEldenRoot 'tests/HappyCheats.Tests/HappyCheats.Tests.csproj') --configuration Release -- --elden-ring
if ($LASTEXITCODE -ne 0) { throw 'Elden Ring fixture validation failed.' }
Write-Host 'Elden Ring offline fixture checks passed. No game was launched and no gameplay or saves were tested.'
