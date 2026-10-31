[CmdletBinding()]
param([string]$Python)
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Python) {
    $hcBundled = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
    $Python = if (Test-Path -LiteralPath $hcBundled) { $hcBundled } else { 'python' }
}
& $Python (Join-Path $hcRoot 'modules\phantom-blade\tests\run.py')
if ($LASTEXITCODE -ne 0) { throw 'Phantom Blade Zero Lua checks failed.' }
$hcPackage = Join-Path $hcRoot 'payload\phantom-blade-zero'
$hcManifest = Get-Content -LiteralPath (Join-Path $hcPackage 'package.json') -Raw | ConvertFrom-Json
if ($hcManifest.version -ne '0.1.3' -or $hcManifest.app_id -ne '4115450' -or $hcManifest.steam_build -ne '26266538' -or $hcManifest.files.Count -ne 10 -or $hcManifest.executable_sha256 -ne '7B4FCD286A350780FAF0824D73AA150B21CBF1BCCDE883E6A96386CBE09973B4') { throw 'Unexpected Phantom Blade Zero manifest.' }
foreach ($hcFile in $hcManifest.files) {
    $hcPath = [IO.Path]::GetFullPath((Join-Path $hcPackage $hcFile.path))
    if (!$hcPath.StartsWith($hcPackage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Phantom Blade payload path escapes its root.' }
    if ((Get-FileHash -LiteralPath $hcPath -Algorithm SHA256).Hash -ne $hcFile.sha256) { throw "Phantom Blade payload checksum mismatch: $($hcFile.path)" }
}
$hcBinary = Join-Path $hcPackage 'platform\steam'
foreach ($hcProfile in @('MemberVariableLayout.ini','UE4SS_Signatures/GUObjectArray.lua')) {
    $hcOwnedPath = 'platform/steam/ue4ss/' + $hcProfile
    if (@($hcManifest.files | Where-Object { $_.path -ceq $hcOwnedPath }).Count -ne 1) { throw "Phantom Blade must own its loader profile: $hcProfile" }
    $hcBuilt = Join-Path $hcBinary ('ue4ss/' + $hcProfile)
    $hcSource = Join-Path $hcRoot ('modules/phantom-blade/' + $hcProfile)
    if ((Get-FileHash -LiteralPath $hcBuilt).Hash -ne (Get-FileHash -LiteralPath $hcSource).Hash) { throw "Stale Phantom Blade loader profile: $hcProfile" }
}
foreach ($hcScript in @('main','bindings','protocol')) {
    $hcBuilt = Join-Path $hcBinary "ue4ss\Mods\HappyCheatsPhantomBlade\Scripts\$hcScript.lua"
    $hcSource = Join-Path $hcRoot "modules\phantom-blade\Scripts\$hcScript.lua"
    if ((Get-FileHash -LiteralPath $hcBuilt).Hash -ne (Get-FileHash -LiteralPath $hcSource).Hash) { throw "Stale Phantom Blade script: $hcScript" }
}
$hcSettings = Get-Content -LiteralPath (Join-Path $hcBinary 'ue4ss\UE4SS-settings.ini') -Raw
if ($hcSettings -notmatch '(?m)^MajorVersion\s*=\s*5\s*$' -or $hcSettings -notmatch '(?m)^MinorVersion\s*=\s*7\s*$') { throw 'Unexpected Phantom Blade engine profile.' }
if ($hcSettings -notmatch '(?m)^bForceGUObjectArrayForIteration\s*=\s*true\s*$') { throw 'Phantom Blade must use the verified object array rather than incompatible hash tables.' }
$hcHooks = @([regex]::Matches($hcSettings,'(?m)^(Hook\w+)\s*=\s*1\s*$') | ForEach-Object { $_.Groups[1].Value } | Sort-Object)
if (($hcHooks -join ',') -ne 'HookEngineTick,HookUObjectProcessEvent') { throw 'Unexpected Phantom Blade hooks.' }
Write-Host 'Phantom Blade Zero offline payload/source checks passed; startup and gameplay remain unverified.'
