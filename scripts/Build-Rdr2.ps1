[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$hcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hcNative = Join-Path $hcRoot 'modules/rdr2/native'
$hcBuild = Join-Path $hcRoot 'artifacts/rdr2-build'
$hcPackage = Join-Path $hcRoot 'payload/red-dead-redemption-2'
$hcCompiler = (Get-Command g++ -ErrorAction Stop).Source
$hcDllTool = Join-Path (Split-Path $hcCompiler) 'dlltool.exe'
if ((& $hcCompiler -dumpmachine) -notmatch '^x86_64-') { throw 'RDR2 requires an x64 MinGW compiler.' }
New-Item -ItemType Directory -Force -Path $hcBuild,$hcPackage,(Join-Path $hcPackage 'HappyCheatsRdr2/bridge'),(Join-Path $hcPackage 'licenses') | Out-Null
$hcImport = Join-Path $hcBuild 'scripthook-import.a'
& $hcDllTool -d (Join-Path $hcNative 'scripthook.def') -l $hcImport
if ($LASTEXITCODE -ne 0) { throw 'RDR2 verified ScriptHook ABI import library generation failed.' }
$hcAsi = Join-Path $hcBuild 'HappyCheats.Rdr2.asi'
& $hcCompiler -std=c++20 -O2 -Wall -Wextra -Werror -Wno-cast-function-type -DUNICODE -D_UNICODE -shared -static -static-libgcc -static-libstdc++ -I (Join-Path $hcNative 'vendor') (Join-Path $hcNative 'trainer.cpp') (Join-Path $hcNative 'scripthook_import.S') $hcImport -o $hcAsi -lbcrypt -lversion '-Wl,--no-insert-timestamp'
if ($LASTEXITCODE -ne 0) { throw 'RDR2 Story Mode ASI compilation failed.' }
& (Join-Path $PSScriptRoot 'Test-Rdr2.ps1') -AsiPath $hcAsi -SkipPayloadValidation
Copy-Item -LiteralPath $hcAsi -Destination (Join-Path $hcPackage 'HappyCheats.Rdr2.asi') -Force
[IO.File]::WriteAllText((Join-Path $hcPackage 'HappyCheatsRdr2/bridge/README.txt'), "HappyCheats RDR2 Story Mode 0.1.0 session-bound JSON mailbox.`nCommands expire after eight seconds. All controls default off and reset on player/death/session changes.`nRed Dead Online is unsupported and runtime-blocked. HappyCheats Install / update downloads and configures verified ScriptHookRDR2.dll and its ASI loader; first setup needs internet.`n")
Copy-Item -LiteralPath (Join-Path $hcNative 'vendor/nlohmann/LICENSE.MIT') -Destination (Join-Path $hcPackage 'licenses/nlohmann-MIT.txt') -Force
Copy-Item -LiteralPath (Join-Path $hcRoot 'modules/rdr2/README.md') -Destination (Join-Path $hcPackage 'README.md') -Force
[ordered]@{json_dependency='nlohmann/json';version='3.12.0';license='MIT';url='https://github.com/nlohmann/json/tree/v3.12.0';native_database='https://github.com/alloc8or/rdr3-nativedb-data/tree/e8558ab99c7e990edd7200a348379d6880c14f96';runtime='ScriptHookRDR2 is downloaded directly from its author by Install / update and is not redistributed';validation='Offline native mock, protocol, ABI registration and unsupported-host fixtures only; gameplay unverified'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hcPackage 'licenses/provenance.json') -Encoding utf8NoBOM
$hcFiles = @('HappyCheats.Rdr2.asi','HappyCheatsRdr2/bridge/README.txt') | ForEach-Object { [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $hcPackage $_) -Algorithm SHA256).Hash} }
$hcSources = @('trainer.cpp','trainer_core.h','native_api.h','scripthook.def','scripthook_import.S','vendor/nlohmann/json.hpp') | ForEach-Object { [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $hcNative $_) -Algorithm SHA256).Hash} }
[ordered]@{schema_version=1;game_id='red-dead-redemption-2';version='0.1.0';app_id='1174180';profile_file_version='1.0.1491.50';files=@($hcFiles);sources=@($hcSources)} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $hcPackage 'package.json') -Encoding utf8NoBOM
Write-Host "Built RDR2 Story Mode 0.1.0 payload: $hcAsi (gameplay unverified; integrated direct-author ScriptHook setup)."
