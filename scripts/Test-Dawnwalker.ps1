[CmdletBinding()]
param([string]$Python)
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Assert-DawnwalkerRuntimeSettings([string]$Settings) {
    # Validate the built file that gets installed, not Build-Dawnwalker's source.
    # Missing hooks inherit UE4SS defaults, so every pinned hook must be explicit.
    $hcSections = @{}
    $hcSection = $null
    $hcLineNumber = 0
    foreach ($hcLine in ($Settings -split '\r?\n')) {
        $hcLineNumber++
        $hcTrimmed = $hcLine.Trim()
        if (!$hcTrimmed -or $hcTrimmed.StartsWith(';') -or $hcTrimmed.StartsWith('#')) { continue }
        if ($hcTrimmed -match '^\[([A-Za-z_][A-Za-z0-9_]*)\]$') {
            $hcSection = $Matches[1]
            if ($hcSections.ContainsKey($hcSection)) { throw "Duplicate UE4SS section: $hcSection" }
            $hcSections[$hcSection] = @{}
            continue
        }
        if (!$hcSection -or $hcTrimmed -notmatch '^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
            throw "Malformed UE4SS setting at line $hcLineNumber."
        }
        $hcKey, $hcValue = $Matches[1], $Matches[2].Trim()
        if ($hcSections[$hcSection].ContainsKey($hcKey)) { throw "Duplicate UE4SS setting: [$hcSection] $hcKey" }
        if ($hcKey -like 'Hook*' -and $hcSection -ne 'Hooks') {
            throw "UE4SS hook setting is outside [Hooks]: $hcKey"
        }
        $hcSections[$hcSection][$hcKey] = $hcValue
    }
    foreach ($hcRequiredSection in @('EngineVersionOverride', 'General', 'Hooks')) {
        if (!$hcSections.ContainsKey($hcRequiredSection)) { throw "Missing UE4SS section: $hcRequiredSection" }
    }
    if ($hcSections.EngineVersionOverride.MajorVersion -cne '5' -or $hcSections.EngineVersionOverride.MinorVersion -cne '5') {
        throw 'Dawnwalker must explicitly use Unreal Engine 5.5 layouts.'
    }
    if ($hcSections.General.DefaultExecuteInGameThreadMethod -cne 'EngineTick' -or $hcSections.Hooks.EngineTickResolveMethod -cne 'Scan') {
        throw 'Dawnwalker must use the verified scanned engine-tick scheduler.'
    }
    $hcEnabledHooks = @('HookEngineTick', 'HookUObjectProcessEvent')
    $hcKnownHooks = @('HookProcessInternal', 'HookProcessLocalScriptFunction', 'HookInitGameState', 'HookLoadMap',
        'HookCallFunctionByNameWithArguments', 'HookBeginPlay', 'HookEndPlay', 'HookLocalPlayerExec',
        'HookAActorTick', 'HookEngineTick', 'HookGameViewportClientTick', 'HookUObjectProcessEvent',
        'HookProcessConsoleExec', 'HookUStructLink')
    foreach ($hcKey in $hcKnownHooks) {
        if (!$hcSections.Hooks.ContainsKey($hcKey)) { throw "Missing explicit Dawnwalker hook setting: $hcKey" }
    }
    foreach ($hcKey in $hcSections.Hooks.Keys) {
        if ($hcKey -notlike 'Hook*') { continue }
        $hcExpected = if ($hcKey -in $hcEnabledHooks) { '1' } else { '0' }
        if ($hcSections.Hooks[$hcKey] -cne $hcExpected) {
            throw "Unsafe Dawnwalker hook setting: $hcKey must be $hcExpected."
        }
    }
}

$hcSettingsPath = Join-Path $hcRoot 'payload\dawnwalker\Dawnwalker\Binaries\Win64\ue4ss\UE4SS-settings.ini'
if (!(Test-Path -LiteralPath $hcSettingsPath -PathType Leaf)) { throw 'Built Dawnwalker runtime settings are missing. Run Build-Dawnwalker.ps1 first.' }
$hcRuntimeSettings = Get-Content -LiteralPath $hcSettingsPath -Raw
Assert-DawnwalkerRuntimeSettings $hcRuntimeSettings
# Mutate only in-memory copies of the actual payload. These exercise rejection
# paths without altering the packaged settings or any installed game files.
$hcRejectedProfiles = [ordered]@{
    EnabledCookieHook = $hcRuntimeSettings -replace '(?m)^HookProcessLocalScriptFunction\s*=.*$', 'HookProcessLocalScriptFunction = 1'
    EnabledProcessInternal = $hcRuntimeSettings -replace '(?m)^HookProcessInternal\s*=.*$', 'HookProcessInternal = 1'
    EnabledLoadMap = $hcRuntimeSettings -replace '(?m)^HookLoadMap\s*=.*$', 'HookLoadMap = 1'
    MissingCookieHook = $hcRuntimeSettings -replace '(?m)^HookProcessLocalScriptFunction\s*=.*$', ''
    MissingProcessInternal = $hcRuntimeSettings -replace '(?m)^HookProcessInternal\s*=.*$', ''
    MissingLoadMap = $hcRuntimeSettings -replace '(?m)^HookLoadMap\s*=.*$', ''
    DisabledEngineTick = $hcRuntimeSettings -replace '(?m)^HookEngineTick\s*=.*$', 'HookEngineTick = 0'
    MissingProcessEvent = $hcRuntimeSettings -replace '(?m)^HookUObjectProcessEvent\s*=.*$', ''
    UnknownEnabledHook = $hcRuntimeSettings -replace '(?m)^\[Hooks\]', "[Hooks]`nHookFutureDefault = 1"
    DuplicateHook = $hcRuntimeSettings -replace '(?m)^HookLoadMap\s*=.*$', "HookLoadMap = 0`nhookloadmap = 1"
    DuplicateSection = $hcRuntimeSettings + "`n[Hooks]`nHookLoadMap = 1`n"
    MalformedHook = $hcRuntimeSettings -replace '(?m)^HookLoadMap\s*=.*$', 'HookLoadMap 0'
    WrongEngine = $hcRuntimeSettings -replace '(?m)^MinorVersion\s*=.*$', 'MinorVersion = 4'
    MissingEngine = $hcRuntimeSettings -replace '(?m)^MajorVersion\s*=.*$', ''
    WrongScheduler = $hcRuntimeSettings -replace '(?m)^DefaultExecuteInGameThreadMethod\s*=.*$', 'DefaultExecuteInGameThreadMethod = ProcessEvent'
}
foreach ($hcCase in $hcRejectedProfiles.GetEnumerator()) {
    $hcRejected = $false
    try { Assert-DawnwalkerRuntimeSettings $hcCase.Value } catch { $hcRejected = $true }
    if (!$hcRejected) { throw "Dawnwalker runtime guard accepted unsafe profile: $($hcCase.Key)" }
}
Write-Host "Dawnwalker packaged runtime: verified engine 5.5 and minimal hook profile; $($hcRejectedProfiles.Count) unsafe profiles rejected."

if (!$Python) {
    $hcBundledPython = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
    $Python = if (Test-Path -LiteralPath $hcBundledPython) { $hcBundledPython } else { 'python' }
}
& $Python (Join-Path $hcRoot 'modules\dawnwalker\tests\run.py')
if ($LASTEXITCODE -ne 0) { throw 'Dawnwalker Lua test suite failed.' }
