[CmdletBinding()]
param([string]$Python)
$ErrorActionPreference='Stop'
$hcRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Python) {
    $hcBundled=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
    $Python=if (Test-Path -LiteralPath $hcBundled) { $hcBundled } else { 'python' }
}
& $Python (Join-Path $hcRoot 'modules\wukong\tests\run.py')
if ($LASTEXITCODE -ne 0) { throw 'Wukong Lua test suite failed.' }
$hcPackage=Join-Path $hcRoot 'payload\black-myth-wukong'
$hcManifest=Get-Content -LiteralPath (Join-Path $hcPackage 'package.json') -Raw | ConvertFrom-Json
if ($hcManifest.version -ne '0.1.1' -or $hcManifest.app_id -ne '2358720' -or $hcManifest.files.Count -ne 9) { throw 'Unexpected Wukong payload manifest.' }
foreach($hcFile in $hcManifest.files) {
    $hcPath=[IO.Path]::GetFullPath((Join-Path $hcPackage $hcFile.path))
    if (!$hcPath.StartsWith($hcPackage+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Wukong payload path escapes its root.' }
    if ((Get-FileHash -LiteralPath $hcPath -Algorithm SHA256).Hash -ne $hcFile.sha256) { throw "Wukong payload hash mismatch: $($hcFile.path)" }
}
$hcMod=Join-Path $hcPackage 'b1\Binaries\Win64\ue4ss\Mods\HappyCheatsWukong'
$hcLayoutPath=Join-Path $hcPackage 'b1\Binaries\Win64\ue4ss\VTableLayout.ini'
if (@($hcManifest.files | Where-Object { $_.path -ceq 'b1/Binaries/Win64/ue4ss/VTableLayout.ini' }).Count -ne 1) { throw 'Wukong package must own its ProcessEvent vtable layout.' }
if ((Get-FileHash -LiteralPath $hcLayoutPath -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $hcRoot 'modules\wukong\VTableLayout.ini') -Algorithm SHA256).Hash) { throw 'Stale Wukong payload: VTableLayout.ini' }
# UE4SS consumes ordered names. Each base section contributes count minus one;
# an entry written as ProcessEvent=0x250 would not override ProcessEvent at all.
$hcLayoutSections=@{}
$hcLayoutSection=$null
foreach ($hcLine in Get-Content -LiteralPath $hcLayoutPath) {
    $hcEntry=($hcLine -split ';',2)[0].Trim()
    if (!$hcEntry) { continue }
    if ($hcEntry -match '^\[([^\]]+)\]$') {
        $hcLayoutSection=$Matches[1]
        if ($hcLayoutSections.ContainsKey($hcLayoutSection)) { throw 'Duplicate Wukong vtable layout section.' }
        $hcLayoutSections[$hcLayoutSection]=[Collections.Generic.List[string]]::new()
    } else {
        if (!$hcLayoutSection -or $hcEntry -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw 'Wukong vtable layout must contain ordered names, not key/value offsets.' }
        $hcLayoutSections[$hcLayoutSection].Add($hcEntry)
    }
}
if ($hcLayoutSections.Count -ne 3 -or !$hcLayoutSections.ContainsKey('UObjectBase') -or !$hcLayoutSections.ContainsKey('UObjectBaseUtility') -or !$hcLayoutSections.ContainsKey('UObject')) { throw 'Unexpected Wukong vtable layout sections.' }
$hcBase=$hcLayoutSections['UObjectBase']; $hcUtility=$hcLayoutSections['UObjectBaseUtility']; $hcObject=$hcLayoutSections['UObject']
if ($hcBase.Count -ne 1 -or $hcUtility.Count -ne 1 -or $hcObject.Count -ne 75 -or $hcObject[74] -cne 'ProcessEvent') { throw 'Unexpected Wukong ProcessEvent layout slot.' }
$hcProcessEventOffset=($hcBase.Count-1+$hcUtility.Count-1+$hcObject.IndexOf('ProcessEvent'))*8
if ($hcProcessEventOffset -ne 0x250 -or @($hcObject | Where-Object { $_ -ceq 'ProcessEvent' }).Count -ne 1) { throw 'Wukong ProcessEvent must resolve to byte offset 0x250, not stock 0x258.' }
if ($hcBase[0] -cne 'HappyCheatsUnusedBaseSlot0' -or $hcUtility[0] -cne 'HappyCheatsUnusedUtilitySlot0') { throw 'Unexpected Wukong inherited vtable overrides.' }
for ($hcIndex=0;$hcIndex -lt 74;$hcIndex++) {
    if ($hcObject[$hcIndex] -cne "HappyCheatsUnusedObjectSlot$hcIndex") { throw 'Unexpected Wukong vtable override outside ProcessEvent.' }
}
foreach($hcScript in @('main','bindings','protocol')) {
    if ((Get-FileHash -LiteralPath (Join-Path $hcMod "Scripts\$hcScript.lua")).Hash -ne (Get-FileHash -LiteralPath (Join-Path $hcRoot "modules\wukong\Scripts\$hcScript.lua")).Hash) { throw "Stale Wukong payload: $hcScript" }
}
$hcSettings=Get-Content -LiteralPath (Join-Path $hcPackage 'b1\Binaries\Win64\ue4ss\UE4SS-settings.ini') -Raw
if ($hcSettings -notmatch '(?m)^MajorVersion\s*=\s*5\s*$' -or $hcSettings -notmatch '(?m)^MinorVersion\s*=\s*0\s*$') { throw 'Unexpected Wukong engine profile.' }
$hcEnabled=@([regex]::Matches($hcSettings,'(?m)^(Hook\w+)\s*=\s*1\s*$') | ForEach-Object {$_.Groups[1].Value} | Sort-Object)
if (($hcEnabled -join ',') -ne 'HookEngineTick,HookUObjectProcessEvent') { throw 'Unexpected Wukong hook profile.' }
Write-Host 'Wukong payload/source hashes, engine profile and ProcessEvent offset 0x250 verified (offline only).'
