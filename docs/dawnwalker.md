# The Blood of Dawnwalker

Experimental trainer **0.3.3** for HappyCheats **0.3.19**. Supports the inspected Windows Steam build **25232147**, executable version `dw1-pc-258504-shipping-patch2-all-CL-258504` (Unreal Engine 5.5.4). Other executable hashes are rejected pending a compatibility check.

HappyCheats **0.3.38** replaced the game-specific companion panel with the [shared in-game menu](in-game-menu.md). Trainer 0.3.3 and its gameplay controls are unchanged. From HappyCheats **0.3.39**, Ctrl+Del, F6 and Menu+RB open the same menu; the controller's current page mappings remain visible above the control list.

HappyCheats **0.3.40** makes **Menu+RB** the shared-menu chord for every current and future game: enable **Xbox shortcuts**, hold **Menu (☰) for 350 ms**, then tap **RB** on an Xbox/XInput controller. Dawnwalker's Ctrl+Del, F6 and direct cheat chords are unchanged. This chord shows or hides the menu; its controls still use mouse and keyboard.

Version 0.3.3 fixes **Player rotation unavailable** during manual and automatic teleport. The captured rotation schema names its pitch field `pitch`, whereas the trainer expected `Pitch`. The pinned loader uses these names exactly for both returned values and input arguments. The trainer now validates all three angles and preserves the field spelling when passing rotation to native travel. It does not guess a default rotation or relax destination checks. Regression fixtures reproduce the captured schema; successful live travel remains unverified.

Version 0.3.2 adds **Auto teleport**. Enable it with NumPad4, Menu+X or its toggle, then place a new custom map marker. It makes one travel attempt when player control permits; close the map if it is waiting for control. It uses the manual teleport checks. Enabling it does not use an existing marker. Turning it off, removing the marker, disabling all cheats or changing player session cancels queued travel. Both teleport controls are available in the shared menu; Menu+Y and Menu+X work on every controller page. Live gameplay and physical-controller validation remain pending.

A queued automatic request expires after about 30 seconds of trainer polling; place a fresh marker if it expires. New markers placed during an active or uncertain travel request do not queue another automatic jump. A refused destination is attempted only once; choose a new marker to try again.

Version 0.3.1 fixes map-marker teleport falsely rejecting even nearby flat ground. The pinned loader reads packed HitResult booleans as a whole byte: a normal blocking hit was also reported as starting inside solid collision. The trainer now decodes the two verified bits separately from the copied collision result. Ground, slope, initial-overlap, clearance, player ownership and streaming checks remain active. Failure messages distinguish missing ground from collision-data and slope failures. Regression fixtures reproduce the loader's real conversion; gameplay travel has not been retested. Distant unloaded terrain still cannot be used as a destination.

Version 0.3.0 repairs vertical flight input, cancels inactive flight timers explicitly, and fixes consumable-array and item-handle conversion. It adds guarded custom-marker teleport and player-specific enemy invisibility. Read-only inspection used the already-running game; no game function or cheat was activated by the inspection tools. These changes have offline regression coverage, with live gameplay and controller verification still pending. The user reported other 0.2.1 controls working, but that does not verify this release's new behavior or its effect on the remaining lag.

Version 0.1.2 corrects a player-readiness check, handles exFAT timestamp rounding and reduces repeated work in the trainer and companion app. With a save loaded, the corrected status reader passed a 60-second check with all 60 samples connected and ready. The user reports reduced lag, with some still present; frame times have not been measured. Resource effects and controller operation remain unverified. The earlier 0.1.1 loader fix passed a user-authorized title-screen launch with a fresh heartbeat and all controls disabled.

## Controls

| Control | Keyboard | Xbox/XInput controller (hold Menu ☰ for 350 ms first) |
| --- | --- | --- |
| Infinite health | F1 | Page 1, D-pad up |
| Infinite stamina | F2 | Page 1, D-pad right |
| Refill health and stamina once | F3 | Page 1, D-pad down |
| Refill blood continuously | F4 | Page 1, D-pad left |
| Invulnerability | F5 | Page 2, D-pad up |
| Super damage | F8 | Page 2, D-pad right |
| Invisibility | F9 | Page 2, D-pad down |
| Fly | F11 | Page 2, D-pad left |
| Teleport to map marker | F7 | Y, any page |
| Auto teleport to new custom markers | NumPad4 | X, any page |
| Infinite carry weight | F12 | Page 3, D-pad up |
| Infinite money | NumPad1 | Page 3, D-pad right |
| Unlimited item use | NumPad2 | Page 3, D-pad down |
| Add XP instantly | NumPad3 | Page 3, D-pad left |
| Next controller shortcut page | — | View |
| Disable all | F10 | LB, any page |
| Show / hide shared menu | Ctrl+Del or F6 | RB, any page |

Pages are Survival, Combat & movement, and Inventory & XP. Switching pages never sends a game command. Flight keeps collision and normal horizontal movement; use Space/Left Ctrl or RT/LT to ascend/descend. Vertical movement uses a collision sweep because this game's custom locomotion may discard ordinary vertical input. The 16 ms game-thread timer is explicitly cancelled on disable; the pinned loader ignores callback return values, which previously left loops running after repeated toggles. This fixes an identified source of repeated work, but frame-time improvement has not been measured.

Invisibility sets only the owned visibility bit on the validated player's AI stub. Inspected native paths reject that target during normal sight/hearing perception and attack selection; the player model stays visible. Disabling restores that bit while preserving unrelated flag changes. Existing alerts, scripted encounters and forced events may persist; this does not globally disable AI.

For teleport, place a **custom marker after the updated trainer loads**, close the map and press F7 or Menu+Y. The trainer observes the native marker event, verifies the same player/world/map, repeats the map's ground trace and checks walkable ground and player-capsule clearance. It requests the game's own travel/loading sequence rather than directly relocating the actor. Distant unloaded terrain, steep slopes and obstructed spots are refused. A pending or uncertain travel request cannot be repeated until arrival is observed or the player session changes. Saved markers must be placed again after a restart or session change.

Invulnerability owns one native active-effect handle and removes only that effect. Super damage captures the four positive base multipliers and writes 10× values through the native ability-system setter; disable restores still-owned values and preserves later game changes. Neither uses raw attribute memory writes or the game's shared health lock.

Infinite carry weight uses the native zero limit and restores the original limit. Infinite money maintains a 999,999 coin floor; coins already granted remain after disabling. Unlimited item use changes only ordinary consumable assets currently carried by the player, excludes the game's dynamic quest-item registry, and restores recorded flags when disabled. The loader returns a Lua table of wrapped inventory entries; the trainer unwraps them and preserves the opaque native item handle through its verified int32 field. A native handle-to-asset round trip must match before quest classification. All entries are classified before any change. Enabled scans run about every two seconds; newly acquired items may not be covered immediately. Item quantities and generic item-removal functions are not intercepted.

Add XP instantly grants exactly the remaining XP to reach the next level once. It checks the game's actual level cache through its guarded getter, progression state and player ownership, rechecks immediately before the native level-up call, and confirms one level gained with zero leftover XP. It makes no quest-reward-cache calls and does not complete a quest. A positive following-level row is also required, so the button can be unavailable near the cap. Coins and XP may persist in normal saves and are not rolled back.

Continuous features periodically refill the current living player's resource through the game's native percent setters. Health replenishment does **not** prevent a lethal hit, and resources can be consumed between refreshes. The trainer does not install its own resource locks; setters follow the game's own behavior, including temporarily unlocking and relocking existing native resource locks. Toggles start off and reset when the player or world changes. A missing binding disables its control. Refill is a single action and does not enable the toggles.

The blood control is available only when the local player's blood component and native methods are available. Filling blood with this native method also restores damaged blood segments. These resource changes may be retained by normal game saves. Turning a toggle off stops replenishment; it does not undo earlier refills or segment restoration.

## Setup and overlay

1. Close Dawnwalker, open the new HappyCheats release and select **The Blood of Dawnwalker**.
2. Choose **Install / update** to install the bundled UE4SS loader and Lua trainer. Existing unrelated or modified loader files are preserved; resolve a reported conflict before retrying.
3. Launch the game and load a save. Controls activate after the trainer reports a fresh, matching session.
4. Leave HappyCheats open with Dawnwalker selected for controller shortcuts and the overlay. Use windowed or borderless mode for the overlay; exclusive fullscreen can hide it.

The shared menu is a Windows companion panel for windowed/borderless play. Clicking it gives it keyboard focus for editing; close it to return to the game. Enable Xbox shortcuts to use controller chords; this setting is available for every game. Dawnwalker's existing feature chords operate directly and do not navigate menu rows. Menu+View selects the controller shortcut page shown in the menu hint; the menu arrows filter control groups independently. Ctrl+Del, F6 or Menu+RB show or hide it. For Menu+RB, hold Menu (☰) for 350 ms before tapping RB. Input is not suppressed from the game, so Menu may also open its pause menu. Onimusha's direct cheat chords are preserved, while its Menu+RB now opens the shared menu and F6 retains its native status panel. The panel hides when another application is foreground.

中文：启用“Xbox 手柄快捷键”后，在 Xbox/XInput 手柄上**按住 Menu（☰）350 毫秒，再点按 RB**，即可显示或隐藏统一菜单。这一操作从 **0.3.40** 起适用于所有现有及未来游戏；Ctrl+Del、黎明行者之血的 F6 和原有直接功能组合键保持不变。菜单功能仍使用鼠标和键盘操作，手柄不导航菜单行。Menu+View 仍切换黎明行者之血的手柄快捷键页。鬼武者的 Menu+RB 也改为切换统一菜单，F6 仍切换其原生状态面板。

The listed cheat keyboard shortcuts are also registered in the in-game Lua trainer and work without HappyCheats. F6, controller input and the companion overlay require HappyCheats. Installation/removal is blocked while Dawnwalker is running. The loader is shared only when its existing files match; removal preserves other mods and modified files.

## Readiness and polling changes in 0.1.2

The controller ownership check now calls the reflected `K2_GetPawn` method. The earlier `GetPawn` lookup did not identify that reflected method and could keep a living player unavailable. Setter-function availability is cached per player session instead of repeating global lookups on every poll; invalid or missing bindings disable the affected control until the next session. Ownership and liveness checks remain active.

Resource maintenance still runs every 250 ms. Routine status files are now written at most once per second, while session, readiness, availability, toggle and command-acknowledgement changes publish immediately. The client briefly retries reads during the file's remove/rename gap.

Live inspection identified another cause of alternating waiting states: the game's exFAT drive reported a fresh status file's modification time about 1.74 seconds ahead of the current UTC time, exceeding the old one-second limit. The client now accepts up to two seconds of forward timestamp rounding. It still rejects timestamps further ahead, status older than six seconds, status preceding the current game process, and malformed or mismatched sessions. This is a bounded timestamp tolerance, not a cache of previously enabled controls.

The overlay keeps its button instances between updates, including changes of language, theme and connection state. Process/window inspection runs off the UI thread. Routine installation discovery reuses its package comparison only while relevant file paths, existence, sizes and timestamps match; installation and removal retain their complete checksum checks. These changes reduce identified repeated work, but an improvement in in-game frame times has not yet been measured.

## Startup changes in 0.1.1

The loader incorrectly identified the game's MSVC stack-cookie checker at executable RVA `0x3904B40` as Unreal's `ProcessLocalScriptFunction` and installed a script-dispatch hook there. The Dawnwalker profile now disables that unused hook and other optional hooks the trainer does not need, retaining `EngineTick` and `UObjectProcessEvent`. It explicitly selects the inspected Unreal Engine 5.5 family. See [startup diagnosis and evidence](dawnwalker-startup.md).

Player discovery now records player-construction notifications and checks a bounded list of at most 16 candidates. It no longer repeatedly scans the game's entire object array while waiting for a player. Construction callbacks only retain references; possession, ownership, component and liveness checks run later on the game thread. If the list overflows, controls remain unavailable until the game restarts, so an incomplete list cannot select the wrong player. Enabling or reloading the trainer after a player was already created also requires a game restart.

One 250 ms game-thread timer replaces the asynchronous polling loop and its repeated scheduling handoff. An initial status heartbeat and a first-poll log marker help distinguish trainer loading from game-thread polling if startup stalls again. All resource toggles still start disabled; these startup changes add no new gameplay controls.

## Discovered systems and limits

Read-only inspection of the installed executable and running game's reflection metadata identified `CombatComponentBase.SetHealthPercent`, `SetStaminaPercent`, and `BloodBarComponent.SetBloodPercent`, their float parameters, ownership paths and resource getters. Native lock methods exist but replace an existing lock internally; this implementation avoids them. See [binding evidence](dawnwalker-bindings.md).

The map system exposes tracked/user pins and pin locations. Its shipped `DebugTeleportPlayer` function is a **disabled stub**. The implemented teleport uses `FastTravelSystem.FastTravelToLocation`, with the inspected map ground trace and additional capsule-clearance checks. See [teleport investigation](dawnwalker-teleport.md). Invisibility uses the separately verified player AI-stub visibility bit described above.

## Validation scope

For 0.3.0, the six Lua suites cover loader-shaped inventory and attribute wrappers, owned effect/property restoration, partial failures, quest exclusion, XP boundaries, vertical flight sweeps, explicit timer cancellation, marker observation and guarded travel. Client tests cover the 13 control contracts, controller pages and stable scrolling overlay controls. These use mocked native boundaries, not execution of the game's new functions. All six scripts are packaged and hash checked. The minimal loader profile and connection guards remain unchanged. See [0.3.15 release notes](releases/0.3.15.en.txt).

The original binding investigation used read-only inspection of an already running game. No game function was invoked by the inspection tools and no saves were edited. The 0.1.1 startup investigation combined loader logs, executable inspection and a diagnostic thread snapshot with user-authorized startup tests. The snapshot showed multiple threads blocked in the loader's hook path; binary inspection identified the incorrect stack-cookie-checker hook.

For 0.1.1, the Lua fixtures passed **375 assertions**: bindings 231, mailbox/startup/hotkeys 92, and protocol 52. The full C# test run also passed. Coverage included deferred candidate inspection, bounded discovery and overflow refusal, player/lifecycle guards, resource setters, command expiry/replay/session binding, startup heartbeat, timer registration and scheduler errors. These checks use isolated fixtures and do not execute the real game's functions.

**Live title-screen startup passed on 26 September 2026 with the full rebuilt 0.1.1 trainer.** The trainer registered its player notification, entered its first game-thread poll and published a fresh waiting-for-player status with all toggles off. No save was loaded and no cheat was activated. Actual combat/resource behavior, physical controller gameplay and overlay interaction over the game remain unverified; map teleport remains unavailable. See [validation](validation.md) for the recorded scope and subsequent release checks.

The 0.1.2 offline checks passed **437 Lua assertions** (257 bindings, 128 startup/mailbox/hotkeys, 52 protocol), **228 core checks**, and **25 WPF cases with 640 assertions**. They cover the reflected pawn method, session-scoped function lookups, timestamp limits, status-publication gaps and heartbeat cadence, file-metadata cache invalidation, and stable overlay controls. WPF fixtures also exercise state changes, language/theme updates and command binding without sending commands to a game. A stationary fixture followed 100 times produced no native window-position changes; this does not measure gameplay performance. See the [0.3.12 release notes](releases/0.3.12.en.txt) and [validation](validation.md) for final release checks.

During a subsequent user-run 0.1.2 game session, Coen was visible in the loaded world and the trainer reported a ready player with all four resource controls available. Health and stamina toggles had been enabled by the user. Seeing those flags and resource values establishes player detection, not that the refills work correctly under consumption or damage.

The corrected core reader then passed a **60-second read-only probe: 60/60 connected, 60/60 ready, zero dropouts, errors or session changes**, from 14:11:30 to 14:12:30 UTC on 26 September 2026. The probe sent no game commands. The already-open test client still contained the older timestamp check; the corrected GUI was not restarted during that observation. Restart HappyCheats to use the corrected reader. This is a bounded connection check, not long-session or frame-time validation. The user subsequently reported that lag was reduced but still present; the remaining lag is unresolved.

Run `pwsh -NoProfile -File scripts/Build-Dawnwalker.ps1` to build the pinned payload, and `pwsh -NoProfile -File scripts/Test-Dawnwalker.ps1` for Lua checks. Tests reuse the existing `lupa==2.6` test runtime under `artifacts/astroneer/python`; production does not need Python. See [UE4SS documentation](https://docs.ue4ss.com/) for loader and Lua APIs. Dependency provenance and the MIT license are bundled in `payload/dawnwalker/licenses`.
