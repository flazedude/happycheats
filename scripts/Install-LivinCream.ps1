#requires -Version 7.0
[CmdletBinding()]
param([string]$ClientDirectory, [string]$GameDirectory, [switch]$InspectOnly)
$ErrorActionPreference='Stop'
$lcRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$ClientDirectory) {
    $lcBundles=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HappyCheats/Bundles'
    $lcCandidates=@(Get-ChildItem -LiteralPath $lcBundles -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $lcFolder=Join-Path $_.FullName 'HappyCheats'
        $lcExe=Join-Path $lcFolder 'HappyCheats.exe'
        if ((Test-Path -LiteralPath $lcExe -PathType Leaf) -and (Test-Path -LiteralPath (Join-Path $lcFolder 'update-settings.json') -PathType Leaf)) {
            $lcVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($lcExe).ProductVersion.Split('+')[0]
            if ($lcVersion -match '^\d+\.\d+\.\d+$') { [pscustomobject]@{Directory=$lcFolder;Version=[Version]$lcVersion} }
        }
    } | Sort-Object Version -Descending)
    if (!$lcCandidates.Count) { throw 'Run the official HappyCheats standalone EXE once, close it, then retry. Or pass -ClientDirectory with its configured unpacked client folder.' }
    $ClientDirectory=$lcCandidates[0].Directory
}
$lcArgs=@('--client',[IO.Path]::GetFullPath($ClientDirectory))
if ($GameDirectory) { $lcArgs+=@('--game',[IO.Path]::GetFullPath($GameDirectory)) }
if ($InspectOnly) { $lcArgs+='--inspect-only' }
& dotnet run --project (Join-Path $lcRoot 'tools/InstallLivinCream') -c Release --no-launch-profile -- @lcArgs
if ($LASTEXITCODE -ne 0) { throw 'Livin Cream setup did not finish. See the actionable error above; no client EXE was replaced.' }
