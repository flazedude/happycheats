[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcArchive = Join-Path $hcRoot 'artifacts\astroneer\UE4SS_v3.0.1-1133-gb4cefa18.zip'
$hcArchiveHash = '89B7EED47C37D6FF6EAA144A41311A75098279A3454777F4EDD2446CEA1EA7A8'
$hcLibraryHash = '03C9D35F6A55666839565358EDEA09F5F11C7929440FE5C809E457D6D527C71F'
$hcUrl = 'https://github.com/UE4SS-RE/RE-UE4SS/releases/download/experimental/UE4SS_v3.0.1-1133-gb4cefa18.zip'
$hcExtract = Join-Path $hcRoot 'artifacts\phantom-blade\ue4ss'
$hcPackage = Join-Path $hcRoot 'payload\phantom-blade-zero'
$hcBinary = Join-Path $hcPackage 'platform\steam'
$hcMod = Join-Path $hcBinary 'ue4ss\Mods\HappyCheatsPhantomBlade'
New-Item -ItemType Directory -Force -Path (Split-Path $hcArchive),$hcExtract,$hcBinary,(Join-Path $hcBinary 'ue4ss\UE4SS_Signatures'),(Join-Path $hcMod 'Scripts'),(Join-Path $hcMod 'bridge'),(Join-Path $hcPackage 'licenses') | Out-Null
if (!(Test-Path -LiteralPath $hcArchive)) { Invoke-WebRequest -Uri $hcUrl -OutFile $hcArchive }
if ((Get-FileHash -LiteralPath $hcArchive -Algorithm SHA256).Hash -ne $hcArchiveHash) { throw 'PhantomBlade UE4SS dependency checksum mismatch.' }
Expand-Archive -LiteralPath $hcArchive -DestinationPath $hcExtract -Force
# The archive filename suffix differs from the DLL's embedded source commit.
# Pin the actual binary as well as its container before copying into the package.
if ((Get-FileHash -LiteralPath (Join-Path $hcExtract 'ue4ss\UE4SS.dll') -Algorithm SHA256).Hash -ne $hcLibraryHash) { throw 'PhantomBlade UE4SS binary checksum mismatch.' }
Copy-Item -LiteralPath (Join-Path $hcExtract 'dwmapi.dll') -Destination $hcBinary -Force
Copy-Item -LiteralPath (Join-Path $hcExtract 'ue4ss\UE4SS.dll') -Destination (Join-Path $hcBinary 'ue4ss') -Force
Copy-Item -LiteralPath (Join-Path $hcRoot 'modules\phantom-blade\MemberVariableLayout.ini') -Destination (Join-Path $hcBinary 'ue4ss') -Force
Copy-Item -LiteralPath (Join-Path $hcRoot 'modules\phantom-blade\UE4SS_Signatures\GUObjectArray.lua') -Destination (Join-Path $hcBinary 'ue4ss\UE4SS_Signatures') -Force
$hcSettings = Get-Content -LiteralPath (Join-Path $hcExtract 'ue4ss\UE4SS-settings.ini') -Raw
# The pinned executable contains Unreal Engine 5.7.4. Startup is unverified.
$hcSettings = $hcSettings -replace '(?m)^MajorVersion\s*=.*$', 'MajorVersion = 5' -replace '(?m)^MinorVersion\s*=.*$', 'MinorVersion = 7'
# The live loader self-test found incompatible game hash-table layouts.
# Use the inspected object array directly instead of probing those tables.
$hcSettings = $hcSettings -replace '(?m)^bForceGUObjectArrayForIteration\s*=.*$', 'bForceGUObjectArrayForIteration = true'
$hcSettings = $hcSettings -replace '(?m)^(Hook\w+)\s*=.*$', '$1 = 0'
$hcSettings = $hcSettings -replace '(?m)^HookEngineTick\s*=.*$', 'HookEngineTick = 1' -replace '(?m)^HookUObjectProcessEvent\s*=.*$', 'HookUObjectProcessEvent = 1'
[IO.File]::WriteAllText((Join-Path $hcBinary 'ue4ss\UE4SS-settings.ini'), $hcSettings)
[IO.File]::WriteAllText((Join-Path $hcBinary 'ue4ss\Mods\mods.txt'), "HappyCheatsPhantomBlade : 1`n")
foreach ($hcScript in @('main','bindings','protocol')) {
    Copy-Item -LiteralPath (Join-Path $hcRoot "modules\phantom-blade\Scripts\$hcScript.lua") -Destination (Join-Path $hcMod 'Scripts') -Force
}
[IO.File]::WriteAllText((Join-Path $hcMod 'bridge\README.txt'), "Session-bound HappyCheats mailbox. Commands expire after eight seconds; actions are dispatched once with no automatic retry.`n")
Copy-Item -LiteralPath (Join-Path $hcExtract 'ue4ss\LICENSE') -Destination (Join-Path $hcPackage 'licenses\UE4SS-MIT.txt') -Force
[ordered]@{dependency='UE4SS';version='3.0.1-1133-gb4cefa18';source_commit='2bfa839fe2945f9230204145646b5be4ae84fde8';unreal_dependency_commit='671c7aad5515f919cbcbbbf971944ce7ae227530';archive_url=$hcUrl;archive_sha256=$hcArchiveHash;binary_sha256=$hcLibraryHash;license='MIT';api_reference='https://docs.ue4ss.com/';loader_profile_reference='docs/phantom-blade-loader.md';game_bindings_reference='docs/phantom-blade-bindings.md; local native reflection registration and executable hash; startup and gameplay unverified'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hcPackage 'licenses\provenance.json') -Encoding utf8NoBOM
$hcRuntimeFiles = @('dwmapi.dll','ue4ss/UE4SS.dll','ue4ss/UE4SS-settings.ini','ue4ss/MemberVariableLayout.ini','ue4ss/UE4SS_Signatures/GUObjectArray.lua','ue4ss/Mods/mods.txt','ue4ss/Mods/HappyCheatsPhantomBlade/Scripts/main.lua','ue4ss/Mods/HappyCheatsPhantomBlade/Scripts/bindings.lua','ue4ss/Mods/HappyCheatsPhantomBlade/Scripts/protocol.lua','ue4ss/Mods/HappyCheatsPhantomBlade/bridge/README.txt')
$hcFiles = @($hcRuntimeFiles | ForEach-Object { $hcPath = Join-Path $hcBinary $_; [ordered]@{path=[IO.Path]::GetRelativePath($hcPackage,$hcPath).Replace('\','/');sha256=(Get-FileHash -LiteralPath $hcPath -Algorithm SHA256).Hash} })
[ordered]@{version='0.1.1';app_id='4115450';steam_build='26266538';executable_sha256='7B4FCD286A350780FAF0824D73AA150B21CBF1BCCDE883E6A96386CBE09973B4';files=$hcFiles} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $hcPackage 'package.json') -Encoding utf8NoBOM
Write-Host 'Built PhantomBlade 0.1.1 payload with pinned UE4SS and verified file hashes.'
