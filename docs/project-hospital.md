# Project Hospital

HappyCheats **0.3.26** includes experimental Project Hospital trainer **0.1.0** for Windows Steam app **868360**, inspected build **21161440**. The module checks the SHA-256 of the installed game assembly before installation or connection. Other builds require inspection first.

Close Project Hospital, select it in HappyCheats, choose **Install / update**, then launch the game. The package includes the pinned BepInEx **5.4.23.5 x64** loader and the trainer, so setup does not need an additional download. Load a hospital for hospital controls; campaign unlocking is available at the main menu once the profile is loaded. Reopen the campaign selection screen after toggling campaign unlocking to refresh its buttons.

| Key | Control | Effect |
| --- | --- | --- |
| F1 | Max Money | Raise hospital funds to at least **999,999,999** once. A larger balance is preserved. |
| F2 | Freeze Time of Day | Stop advancement of the hospital day/time while enabled. |
| F3 | Max Staff | Raise the staff hiring limit to **1,000** while enabled. This changes capacity, not staff skills or needs. Turning it off restores the game's hiring rules and does not remove hired staff. |
| F4 | Unlock All Campaigns | Make installed campaigns selectable while enabled, without recording campaign completion or writing the profile. DLC ownership and installed content still apply. |
| F5 | Max Prestige | Keep the hospital's current calculated prestige at **100%** while enabled. |
| F10 | Disable all | Stop continuous effects. Money already granted is not reversed. |

The same controls are available in the English and Chinese HappyCheats interface. Actions require a fresh trainer session and an acknowledgement; disconnected, stale or unsupported sessions cannot receive commands. Hospital-only controls remain disabled at the main menu. All toggles start off and reset when the loaded hospital changes or closes. A native unlimited-staff setting is preserved. Freezing the day clock leaves the rest of the hospital simulation running, while clock-based shift/day/hour events wait.

Money and staff hired afterward can persist when the game saves. Time-based systems wait while the day clock is frozen. Campaign unlocking is a runtime override, not a saved completion unlock. Turning off prestige stops its ongoing override; past day statistics or progression already saved by the game are not rolled back.

The installer verifies every owned file, preserves conflicting or modified mod files, and keeps a receipt for removal. **Remove trainer** removes the owned HappyCheats plugin while preserving the shared BepInEx loader for other mods. A conflicting loader must be resolved before installation; it is not silently overwritten.

## Validation and limitations

Bindings were inspected read-only against the actual installed assembly:

`1120D899FB74D16F31115C5C27325EDBD1154B68E9419E6B7EA03CC3D43B6F24`

Offline validation covers payload compilation, game binding inspection, isolated trainer behavior, command/session gates, installation ownership and integrity, and bilingual desktop/handheld interface checks. The new payload is included in both the signed LAN update and the standalone EXE. Release evidence is retained under `artifacts/project-hospital/`.

**The game was not launched, the trainer was not installed into the real game, and no game save was changed during development.** Loader startup, actual gameplay behavior, large-hospital performance and connectivity from a second PC remain unverified.

Build with PowerShell 7:

```powershell
.\scripts\Build-ProjectHospital.ps1
.\scripts\Test-ProjectHospital.ps1
# For another installation:
.\scripts\Build.ps1 -ProjectHospitalGameDirectory 'D:\SteamLibrary\steamapps\common\Project Hospital' -Zip -SingleExe
```

The trainer targets the game's legacy .NET 3.5/Unity runtime. Game assemblies are used locally for inspection and compilation and are excluded from distribution.
