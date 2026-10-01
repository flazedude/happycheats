# Stardew Valley — HappyCheats module 0.1.1

Adds 12 controls to HappyCheats for the Windows Steam edition, app **413150**. Requires **Stardew Valley 1.6.15 or later within the 1.6 series**. HappyCheats **0.3.3** includes the official **SMAPI 4.5.2** installer and installs it when needed. Compatible existing **SMAPI 4.5.2+** installations are reused. Multiplayer sessions are disabled. Actual game startup and gameplay validation are still pending.

## Start playing

1. Install Stardew Valley through Steam, then close the game before setup.
2. Extract `HappyCheats.exe` from `HappyCheats-0.3.10-win-x64-single-exe.zip` and run it. Select **Stardew Valley / 星露谷物语**, then choose **Install / update**. HappyCheats checks the included SMAPI installer and runs it in the background when the loader is missing or outdated, then installs the HappyCheats mod. Setup uses bundled files and needs no installation download. A compatible existing SMAPI installation is reused.
3. Choose **Launch game**. HappyCheats starts `StardewModdingAPI.exe` directly with the Steam app identity. Load a single-player save; controls unlock when the mod confirms a fresh, ready connection.

On the development PC, SMAPI **4.5.2** and the HappyCheats mod are already installed in `D:\Steam\steamapps\common\Stardew Valley`. The official SMAPI release archive was SHA-256 verified before installation. The game has not been launched for this work.

The distributed HappyCheats package contains its own mod and the unmodified official SMAPI 4.5.2 installer. SMAPI's corresponding source, license and redistribution notices are included under `payload/stardew/smapi/`. Proprietary game assemblies are not distributed. **Remove trainer** removes only its owned, unmodified mod files; SMAPI and other mods remain. Close the game before installing, updating or removing the mod.

If SMAPI is incomplete or unrecognized, HappyCheats preserves it and asks you to repair it with the [official installer](https://smapi.io/) before scanning again. If a bundled-file integrity check fails, obtain the current HappyCheats release again. The game folder must be writable for setup. Installing the launcher alone does not modify the game; choose **Install / update** to apply the loader and mod.

## Controls

| Control | Key / input | Behavior |
| --- | --- | --- |
| Unlimited health | F1 | Keeps health full and prevents damage to the local farmer |
| Unlimited energy | F2 | Refills energy and clears exhaustion |
| Add money | Apply: 1–100,000,000 | Adds the entered amount; total money is capped at 999,999,999 |
| Easy crafting | F3 | Crafts known, non-cooking recipes without ingredients; recipes are not unlocked |
| Unlimited use items | F4 | Preserves stacks during supported direct use, eating, planting, placing and normal NPC gifting, including the last item |
| Health amount | Apply: 1–10,000 | Sets current health; values above maximum health are rejected |
| Max health | Apply: 1–10,000 | Sets maximum health and clamps current health to that maximum |
| Game time | Apply: 0600–2550 | Uses HHMM with minutes 00, 10, 20, 30, 40 or 50; 1430 means 14:30 |
| Unlimited watering can | F5 | Refills watering cans in the farmer's inventory |
| Freeze time | F6 | Pauses the day clock |
| Movement speed | Apply: 0–10 | Adds a temporary movement speed bonus; 0 restores normal speed |
| Refill health, energy and water | F7 | Refills health, energy and inventory watering cans once |
| Disable all | F10 in game / launcher button | Turns off continuous controls and clears the movement bonus |

F1–F7 work while either the game or HappyCheats is focused. Numeric changes require **Apply**; typing alone does nothing. Draft inputs survive heartbeat refreshes, while a separate current-value label shows the game's reported value. The launcher waits for command acknowledgement. If a money request times out, check the game before repeating it because it may already have applied.

Since HappyCheats **0.3.10** / Stardew module **0.1.1**, unlimited item use also preserves items given through normal NPC gifting, including a stack of one. Enable **Unlimited use items** with **F4** or its launcher toggle before gifting. NPC friendship effects and normal daily/weekly gift limits remain unchanged. After updating the client, close Stardew Valley and choose **Install / update** to apply the updated mod before launching again.

Unlimited item use excludes machine inputs, special quest hand-ins, shopping, shipping, inventory transfers, fishing bait and tackle. Easy crafting is separate and does not affect cooking. Turn off unlimited health before setting a lower current-health value. Time changes are refused during events and minigames.

Toggles and the movement bonus reset when a save is loaded or the active farmer/session changes. Money, maximum health and other world/player edits follow normal game saving. Turning controls off does not undo previous edits; no save files are edited directly.

## Verification and remaining checks

For the **0.3.10 / module 0.1.1** gifting fix, the mod compiled against the installed game without warnings. The standalone Harmony harness passed **50 assertions across all 10 patched methods**. It verifies preservation for stacks of one and three, normal consumption when the toggle is off or the player/session is unavailable, and a targeted change to ordinary gift consumption while leaving six special quest consumption paths untouched. These are offline checks; actual gameplay gifting remains unverified.

Inspected local installation: Steam build **16826371**, game assembly **1.6.15.24356**, SMAPI **4.5.2**. The mod compiled against these actual local references. The following earlier HappyCheats 0.3.0 validation completed without loading a game instance or save:

- 22 core module checks covering discovery/version gates, installation ownership, integrity, rollback and command handling.
- 7 protocol test groups with 289 assertions.
- 28 assertions in a standalone Harmony harness, including installation of all nine production patches against the actual game assemblies.
- 36 numeric UI/localization checks and visual inspection of compact Chinese and English screens.
- The complete repository offline suite (`scripts/Test.ps1`) passes.
- Windows x64 self-contained HappyCheats 0.3.0 and its updater publish successfully; the packaged executable rendered Stardew's actual discovered, installed state.

These checks do not establish live gameplay effectiveness or verify the new bundled-installer workflow. See the consolidated [validation record](validation.md) for the separate 0.3.3 setup checks. The next live pass must confirm SMAPI startup, connection after loading a single-player save, every control's actual effect, reset on save changes, normal persistence, and multiplayer refusal.

## Research and development

[SMAPI's website](https://smapi.io/) and its [4.5.2 release](https://github.com/Pathoschild/SMAPI/releases/tag/4.5.2) provide the loader requirements and official distribution. [CJB Cheats Menu](https://github.com/CJBok/SDV-Mods/blob/master/CJBCheatsMenu/docs/README.md) informed the feature survey, while [CraftFromContainers' crafting patches](https://github.com/aedenthorn/StardewValleyMods/blob/master/CraftFromContainers/CodePatches.cs) provided examples of ingredient-check and consumption bindings. HappyCheats does not require either of those mods.

Fishing helpers, daily luck, weather controls and instant crop growth were researched as possible additions. This release implements only three extras beyond the nine requested controls: freeze time, movement speed and one-shot refill.

`Build-Stardew.ps1` runs the production patch-binding harness before creating the payload. It requires the locally installed game and SMAPI reference assemblies and does not launch the game. `Prepare-SmapiBundle.ps1` prepares and validates the official installer, corresponding source and notices under `payload/stardew/smapi/`. Release packaging embeds that complete payload in both distribution formats.

```powershell
# Build the mod and payload using the installed game and SMAPI references:
.\scripts\Build-Stardew.ps1 -GameDirectory 'D:\Steam\steamapps\common\Stardew Valley'
# Run the repository's offline tests without launching games:
.\scripts\Test.ps1
# Package the client using the already-built trainer payloads:
.\scripts\Build.ps1 -Version 0.3.10 -SkipPayloads -Zip -SingleExe
```
