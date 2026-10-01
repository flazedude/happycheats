[CmdletBinding()]
param([string]$Python)
$ErrorActionPreference='Stop'
$hcRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Python) {
    $hcBundled=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
    $Python=if (Test-Path -LiteralPath $hcBundled) { $hcBundled } else { 'python' }
}
& $Python (Join-Path $hcRoot 'modules\hotel-architect\tests\run.py')
if ($LASTEXITCODE -ne 0) { throw 'HotelArchitect Lua test suite failed.' }
$hcPackage=Join-Path $hcRoot 'payload\hotel-architect'
$hcManifest=Get-Content -LiteralPath (Join-Path $hcPackage 'package.json') -Raw | ConvertFrom-Json
if ($hcManifest.schema_version -ne 1 -or $hcManifest.game_id -ne 'hotel-architect' -or $hcManifest.version -ne '0.1.0' -or $hcManifest.app_id -ne '1602000' -or $hcManifest.steam_build -ne '25340122' -or $hcManifest.game_executable_sha256 -ne 'E0B6E24E2D0D3CA466B9DD08ED83BC989594B6B17C31CDC3B63F543355621AE8' -or $hcManifest.files.Count -ne 8) { throw 'Unexpected HotelArchitect payload manifest.' }
if (@($hcManifest.files.path | Sort-Object -Unique).Count -ne 8) { throw 'Duplicate HotelArchitect payload path.' }
foreach($hcFile in $hcManifest.files) {
    $hcPath=[IO.Path]::GetFullPath((Join-Path $hcPackage $hcFile.path))
    if (!$hcPath.StartsWith($hcPackage+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'HotelArchitect payload path escapes its root.' }
    if ((Get-FileHash -LiteralPath $hcPath -Algorithm SHA256).Hash -ne $hcFile.sha256) { throw "HotelArchitect payload hash mismatch: $($hcFile.path)" }
}
$hcMod=Join-Path $hcPackage 'HotelArchitect\Binaries\Win64\ue4ss\Mods\HappyCheatsHotelArchitect'
foreach($hcScript in @('main','bindings','protocol')) {
    if ((Get-FileHash -LiteralPath (Join-Path $hcMod "Scripts\$hcScript.lua")).Hash -ne (Get-FileHash -LiteralPath (Join-Path $hcRoot "modules\hotel-architect\Scripts\$hcScript.lua")).Hash) { throw "Stale HotelArchitect payload: $hcScript" }
}
$hcSettings=Get-Content -LiteralPath (Join-Path $hcPackage 'HotelArchitect\Binaries\Win64\ue4ss\UE4SS-settings.ini') -Raw
if ($hcSettings -notmatch '(?m)^MajorVersion\s*=\s*5\s*$' -or $hcSettings -notmatch '(?m)^MinorVersion\s*=\s*7\s*$') { throw 'Unexpected HotelArchitect engine profile.' }
$hcEnabled=@([regex]::Matches($hcSettings,'(?m)^(Hook\w+)\s*=\s*1\s*$') | ForEach-Object {$_.Groups[1].Value} | Sort-Object)
if (($hcEnabled -join ',') -ne 'HookEngineTick,HookUObjectProcessEvent') { throw 'Unexpected HotelArchitect hook profile.' }
Write-Host 'HotelArchitect payload/source hashes and engine profile verified (offline only).'
