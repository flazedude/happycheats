# Development

JACK-PC and GPD-PC share source through GitHub. Run `.\scripts\Sync-Source.ps1` before work to receive changes, and `.\scripts\Sync-Source.ps1 -Publish` after reviewing and committing completed work. The helper preserves unfinished work and stops when integration is needed. See [source synchronization and branch handoff](source-sync.md).

Requires Windows, .NET 8 SDK, and the installed supported games to build their plugins. Stardew's build also needs installed SMAPI references. Its payload includes the official SMAPI installer, corresponding source and redistribution notices; proprietary game assemblies are not bundled. Astroneer's offline Lua tests additionally need Python and `lupa==2.6`; see its [developer checks](astroneer.md#developer-checks). Destination PCs use the self-contained release; no SDK is needed. Laundry's in-game loader separately needs .NET 6 Desktop Runtime, bundled with the package.

```powershell
.\scripts\Build.ps1 -Version 0.3.61 -Zip -SingleExe
# Wrap the existing complete release folder without rebuilding its trainers:
.\scripts\Build-SingleExe.ps1 -PackageDirectory .\dist\HappyCheats -Zip
# For a different Chinese Parents source-build installation:
.\scripts\Build.ps1 -ChineseParentsGameDirectory 'D:\SteamLibrary\steamapps\common\ChineseParent' -Zip
# Rebuild the launcher using existing payloads:
.\scripts\Build.ps1 -Version 0.3.61 -SkipPayloads
# All offline tests, without starting installed games:
.\scripts\Test.ps1
# Read-only Steam/build detection:
dotnet run --project .\tools\HappyCheats.Cli -c Release -- . probe
# Development UI with workspace payloads:
dotnet run --project .\src\HappyCheats.App -- --data-root .
```

Code is separated into `HappyCheats.App` (WPF), `HappyCheats.Core` (game modules and installation), `ChineseParents.Trainer` (Unity plugin), and copied module sources under `modules`. Add games through `IGameModule` and `ModuleCatalog`; the UI renders their feature definitions automatically. Original sibling projects are preserved.

Distribute `dist/HappyCheats-0.3.61-win-x64-single-exe.zip` or `dist/HappyCheats.exe` to users. The single-EXE ZIP contains only `HappyCheats.exe`; its SHA-256 checksum is provided separately. `Build.ps1 -Zip -SingleExe` also produces the complete-folder `HappyCheats-0.3.61-win-x64.zip` in the existing server-update format. The `dist/HappyCheats/` folder remains a development artifact; do not substitute the smaller executable inside that folder for the complete single-file release.

Official dependency provenance and redistribution notices are included with the relevant payload. No proprietary game assemblies are distributed. See [validation](validation.md) for the precise tested scope.

