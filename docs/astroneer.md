# Astroneer — HappyCheats module 0.3.1

Experimental Steam integration for Astroneer **1.45.1**, Steam build **25599237**, app **361420**. Its inspected shipping executable SHA-256 is `4A57B4B1AC64DEFA09EA6D6C987D5E513D79243A2B33AB05CBA099AD1E377675`. The earlier **1.44.4 / 25119744** executable (`D4FA5EA711A12F3FE87E38F7D385AE4328B2EFC7D8C8B9B0BF495C20CAC54B27`) and **1.43.2.0 / 24409322** executable (`E1764F108E51BB172148F3A4AC71AE38AC031D9ECEB41AD213E139A9CBA61F56`) remain recognized. Other executable hashes are rejected until inspected.

Close Astroneer, open the complete **HappyCheats 0.3.43** EXE, select **ASTRONEER / 异星探险家**, and choose **Install / update** to install trainer **0.3.1**. Launch through Steam and load a world hosted on this PC. Controls become available after a fresh game heartbeat confirms the required bindings and gameplay state. All toggles start off and reset on character, world, lifecycle, authority, or local-possession changes.

Keep HappyCheats running and press **Ctrl+Del** with Astroneer focused to open the [shared in-game menu](in-game-menu.md). With **Xbox shortcuts** enabled, hold **Menu (☰) for 350 ms**, then tap **RB** to show or hide the same menu; use mouse and keyboard for its controls. The menu exposes collection on/off, the **10× / 20× / 100×** selector, oxygen, backpack power, the byte grant, invulnerability, **3× / 5× / 10×** run speed and Disable all. It follows the game window and uses the same confirmed command/status channel as the launcher. Use windowed or borderless mode; the companion menu does not render over exclusive fullscreen. Existing game-focused F1–F6/F10 controls remain available without the client menu.

| Key | Control | Scope |
| --- | --- | --- |
| F1 | Resource collection · 10× / 20× / 100× | Multiplies new positive terrain-resource fragment amounts by the selected rate before the game's resource-cache callback receives them |
| F2 | Unlimited oxygen | Temporarily disables the local astronaut's oxygen consumption using non-save debug/runtime properties |
| F3 | Unlimited backpack power | Refills the local backpack's battery through the game's power component |
| F4 | Add 10,000 bytes | Requests a one-time grant through the game's research system |
| F5 | Invulnerability | Enables the local astronaut's damage-prevention flag |
| F6 | Player run speed · 3× / 5× / 10× | Applies the selected multiplier to the captured original on-foot running speed |
| F10 | Disable all | Game-focused shortcut; the launcher also has a Disable all button |

Choose **10×**, **20×** or **100×** on the collection card. Selection alone does not enable collection. When enabled, a rate change applies to the next accepted fragment amount; turning collection off or using Disable all preserves the choice. A new session resets the choice to 10× and turns collection off. HappyCheats displays the game's confirmed selection after acknowledgement. Rates are mutually exclusive and never stack.

The run-speed card has its own **3× / 5× / 10×** choices. Selecting a speed leaves the toggle unchanged. F6 enables or disables the selected speed; F10 disables both new controls along with the existing toggles. A new gameplay session resets speed to the default 3× selection with its toggle off. Collection and speed choices are independent, including their shared 10× label.

F1–F6 work with either the game or HappyCheats focused. Runtime keys use UE4SS's focus-scoped key registration. F1 toggles collection using the selected rate; F6 toggles run speed using its independently selected rate. The local astronaut must have authority over the world. This supports a solo world that Astroneer internally runs as a listen server. Remote clients are rejected. Co-op effects have not been validated; research bytes belong to the hosted world's shared research system.

The collection control targets `ResourceCacheComponent.OnAcceptResource(Type, Amount)`. It handles resource fragments collected by the local terrain tool, including partial nuggets; it does not duplicate finished items when moving them between inventory slots, multiply crafting output, or hook `StoreResource` a second time. Power and oxygen resource types are excluded. Backpack capacity and the game's nugget creation/overflow behavior remain relevant. The UI counters measure amounts passed into the collection handler, **not confirmed nuggets produced**. Actual nugget yield still needs gameplay verification.

Oxygen's previous consumption/debug values and the backpack's captured charge are restored when disabled. Previously collected resources and research grants may persist through normal game saving. Soil was investigated but is not exposed because equipped-canister ownership has not been verified.

## Implementation evidence

- [ResourceCacheComponent](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/ResourceCacheComponent.h) and [OnAcceptResource delegate](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/OnAcceptResourceDelegate.h) identify the reflected collection event and signed integer amount.
- [AstroCharacter](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/AstroCharacter.h), [PowerComponent](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/PowerComponent.h), and [ResearchSystem](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/ResearchSystem.h) identify the companion controls' APIs.
- [AstroCharacterMovementComponent](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/AstroCharacterMovementComponent.h) declares Astroneer's custom movement fields and character ownership. The character header also declares the non-save `bInvincible` flag; the current executable is inspected separately before enabling its runtime binding.
- [AstroGameMode](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/AstroGameMode.h) and [EGameLifecycleState](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/EGameLifecycleState.h) identify `GameStarted = 9`; the menu's preview astronaut is not sufficient evidence of loaded gameplay.
- [Astroneer modding FAQ](https://astroneermodding.readthedocs.io/en/latest/guides/faq.html) documents UE4SS use. [UE4SS key registration](https://docs.ue4ss.com/lua-api/global-functions/registerkeybind.html) documents focus-scoped callbacks.
- [Astroneer's terrain tool](https://astroneer.wiki.gg/wiki/Terrain_Tool) describes fragments accumulating into nuggets, partial resource caches, and overflow to the ground.

These community SDK declarations are binding evidence, not proof that every behavior works on this game build. Runtime lookup and ownership/lifecycle checks fail closed when a binding is absent. No proprietary game assembly is redistributed.

## Installation and protocol

The package contains UE4SS `3.0.1-1133-gb4cefa18`, pinned to archive SHA-256 `89B7EED47C37D6FF6EAA144A41311A75098279A3454777F4EDD2446CEA1EA7A8`. Provenance and its MIT license are in `payload/astroneer/licenses`. The native loader and mod live below `Astro/Binaries/Win64`; the other games' loaders are separate.

The installer checks each payload hash, rejects conflicting or modified files, stages writes, publishes the native bootstrap last, and rolls back failed transactions. It tracks files actually written in `happycheats-astroneer-install.json`. Unowned or modified files survive removal. If another UE4SS mod is present, shared loader files are retained. Existing foreign UE4SS settings may require manual reconciliation; the installer does not overwrite them.

The Lua bridge accepts a fixed line-based command format; it never executes command text as code. Commands have an eight-second expiry, request IDs, current-session matching and duplicate suppression. The `set_collection_multiplier` action accepts only 10, 20 or 100, checks collection availability, and does not change the toggle. Arithmetic rejects input that would overflow a signed 32-bit amount at the chosen rate. The launcher validates a unique process at the exact shipping executable path, file freshness relative to process start, runtime version, and feature availability; it waits for game acknowledgements. No save file is directly edited by the trainer.

## Compatibility update — 20 October 2026

HappyCheats **0.3.43** / trainer **0.3.1** adds the exact **1.45.1 / Steam build 25599237** executable profile and retains both previously inspected profiles. [System Era's October patch notes](https://blog.astroneer.space/patch-notes/patch-45/) identify patch **1.45.1**. The earlier exact-hash check rejected the updated executable; all nine installed trainer files still matched the previous payload. This update changes compatibility recognition, not the six controls, their independent selectors or menu shortcuts.

Read-only extraction of `DesignAstro.uasset` and `DesignAstro.uexp` from the current game package produced the same SHA-256 hashes as build 25119744. The complete player Blueprint, including lifecycle events, the normal-damage `bInvincible` gate and the forced-death bypass, is unchanged. The two-file comparison and provenance are recorded in `artifacts/astroneer/blueprint-review-25599237/`.

The independent native executable audit passed **50 structural checks**, covering consumed function signatures, property shapes and class ownership, lifecycle/movement enum values, the non-save `bInvincible` setter, and the unchanged native movement block at its new address. No Lua binding correction was indicated. The reproducible evidence is in `artifacts/astroneer/compatibility-25599237/`. The patch label comes from the official notes, not an executable version string.

Offline C# and Lua checks passed. Trainer **0.3.1** was rebuilt and installed with Astroneer closed; all nine installed file hashes match the payload, and CLI discovery reports the current **1.45.1 / 25599237** installation compatible. See [validation](validation.md) for complete check and release results.

No game was launched and no save was read or modified during this compatibility update. **Trainer startup, actual cheat effects, menu operation on build 25599237 and second-PC LAN access remain unverified.** Earlier gameplay observations below apply only to their recorded builds.

## Invulnerability and run speed — 2 October 2026

Trainer **0.3.0**, included in HappyCheats **0.3.42**, adds the two player controls. Bindings are resolved by reflected names and restricted to the living, locally possessed astronaut with authority over the loaded world. Original field values are captured per object and restored on disable or session changes. Missing properties, lost ownership and unconfirmed writes fail the affected control rather than reporting it enabled.

Invulnerability maintains the character's non-save `bInvincible` flag. The inspected `DesignAstro` player Blueprint checks this flag before calling its normal damage routine, so the control blocks that path before health subtraction. It also preserves pre-existing invincibility when turned off. Explicit forced-death events may bypass normal damage; the control does not claim to prevent those events.

Run speed uses the astronaut's own `AstroMovementComponent`, checks `GetCharacterOwner`, and multiplies the captured `MaxSpeed`. Switching 3× → 5× → 10× always uses the original baseline. The normal sprint multiplier and acceleration are preserved. While airborne, sliding, flying, driving or on a hoverboard, it restores the baseline and reports an effective multiplier of 1×; the selected value and enabled toggle are retained so the boost can resume on foot. Selection while disabled changes only the chosen rate. These transitions are checked on the existing game-thread refresh loop; actual movement and damage behavior still require gameplay validation.

The new source and executable evidence is retained under `artifacts/astroneer/player-features-25119744/`, with read-only player Blueprint inspection under `artifacts/astroneer/blueprint-review/`. Extracted game assets are inspection artifacts only and are not included in either distribution.

## 100× collection — 2 October 2026

Trainer **0.2.1**, included in HappyCheats **0.3.41**, adds **100×** to the same client and shared-menu selector. The default remains 10×. Choosing 100× while collection is off leaves it off; switching while enabled changes the next accepted fragment. Disabling preserves the choice, and changing gameplay sessions resets to 10× with collection off. Only the exact values 10, 20 and 100 are accepted throughout the command path.

At 100×, an input of 21,474,836 fragments can be represented as 2,147,483,600; 21,474,837 would exceed the signed 32-bit maximum and is rejected without modifying that collection event. Backpack capacity and nugget creation remain game-controlled. Offline tests cover arithmetic boundaries, protocol validation, switching, acknowledgements, reset behavior and the three-choice UI. Actual 100× mining and final nugget yield remain unverified.

## Compatibility update — 2 October 2026

On 2 October, the installed Steam manifest reported build **25119744**, with a **111,153,224-byte** shipping executable. [System Era's patch notes](https://blog.astroneer.space/patch-notes/patch-44/) identify patch **1.44.4**. The old launcher rejected this updated executable because its compatibility check accepted only the September 13 hash. Trainer **0.2.0** adds an exact profile for the inspected executable, retains the previous profile, and displays the detected version/build instead of a fixed 1.43.2 label. Discovery and connection check the current executable, and installation forces a fresh hash check.

Read-only inspection of the new executable confirms the UE4.27 engine marker, lifecycle enum values `TitleMenu = 8` and `GameStarted = 9`, and compiled registration signatures for the consumed reflected functions and named scalar properties. Some scalar-property owner classes are not established by descriptor candidates alone; the trainer still checks bindings and object ownership at runtime. In particular, `OnAcceptResource` remains native and accepts a resource class plus a signed 32-bit amount; `SetCurrentCharge` retains its float argument/return, and `AuthorityGrantResearchPoints` retains its object/int32/bool arguments and bool return. The reproducible script and 26-check report are saved under `artifacts/astroneer/compatibility-25119744/`.

The update also fixes a reproduced restoration bug. If disabling oxygen/power cannot restore an original value, re-enabling now finishes that restoration before capturing a new baseline, or rejects the request if restoration is still failing. This prevents a previously modified value from replacing the original baseline. Disabling remains available for retry, and invalid objects from an old session do not block a new session.

The rebuilt trainer was installed while Astroneer was closed; all nine installed payload hashes match the new package. For the 0.2.0 compatibility update, validation passed 41 Astroneer client checks, 180 Lua binding/arithmetic assertions, 88 bridge/hotkey assertions and the protocol suite. The 0.2.1 checks are recorded above and in docs/validation.md. The shared menu passes the WPF suite: 40 cases, 1,180 top-level assertions plus 644 menu assertions covering all sixteen modules and a future-module fixture. Chinese/English and light/dark captures were inspected. Astroneer status errors refresh correctly, and F10 works when the menu has focus outside text/choice entry.

No game was launched or save changed during this update. **Actual startup, cheat effects and menu operation on the updated game remain unverified**, as does second-PC LAN access. Earlier September 13 runtime logs came from the old executable and are not evidence of gameplay on this update.

## Earlier validation — 13 September 2026

Completed: offline C# installation/protocol tests; Lua arithmetic, mocked bindings and protocol tests; release compilation; Chinese/English WPF captures; Steam discovery and exact executable inspection. The installed **0.1.0 trial** successfully booted UE4SS on the actual game and registered native hook IDs `(1, 2)` for `OnAcceptResource`. It emitted a fresh heartbeat and resolved oxygen, backpack-power and byte APIs.

That startup check exposed the menu preview astronaut being marked ready; **0.1.1** added the game's lifecycle check. A subsequent live heartbeat from the user's loaded world correctly reported `diagnostic_lifecycle = 9`, but returned `Single-player session required`: the `IsStandalone` check incorrectly rejected the game's host-style world. **0.1.2** checks local possession and `AActor.HasAuthority()` instead, preserving the lifecycle guard. Regression tests include a locally controlled authoritative player with `IsStandalone = false`, remote clients, lost possession, and menu transitions. The launcher now shows the specific blocking reason instead of a generic instruction to load a game.

The **0.1.2 fix was installed and verified in the actual game**: menu lifecycle 8 correctly reported not ready; loading the user's recently saved world produced lifecycle 9, local control and authority true, `Connected = true`, `Ready = true`, and all four public controls available. Clicking the HappyCheats 10× collection button received a successful game acknowledgement and `collection_multiplier = 10`. The 10× toggle was left enabled; oxygen and power stayed off and no research bytes were granted. Evidence is saved in `artifacts/astroneer/host-ready-0.1.2.json` and `host-enabled-0.1.2.json`.

The user's subsequent gameplay produced **1,098 accepted collection events** in the 0.1.2 heartbeat: 2,293,008 base fragments and 22,930,080 total fragments, exactly 10×, with zero rejected events. This verifies the runtime's collection callback measurements; final nugget production was not counted. Evidence: `artifacts/astroneer/collection-observed-0.1.2.json`.

**0.1.3 added selectable 10× / 20× collection.** Offline checks covered both rates, the 20× signed-integer overflow boundary, selection without enabling, switching rates while enabled, preserving selection when disabled, session reset, strict command fields, and confirmed status publication. All three Lua suites passed (163 arithmetic/binding assertions, 88 bridge/hotkey assertions, plus protocol checks), and the full .NET regression suites passed. Chinese and English selector layouts were rendered and inspected. At that point, installation and live 20× selection verification were pending the running game's restart. The current trainer is now 0.3.1; see the current validation above.

**Final nugget output, oxygen/power behavior, byte grants, hotkeys and restoration were not verified in that gameplay session.** Existing save files were copied into timestamped local backups under `artifacts/astroneer` before those verification launches. The launcher rejects older module heartbeats until the current module is loaded.

For a live check, use a separate solo test save: mine one resource with F1 off, observe a baseline fragment count, enable F1, and confirm each accepted amount changes from `n` to `10n`. Switch to 20×, then 100×, and confirm the next accepted amounts become `20n` and `100n`. Count full nuggets as well as partial-cache progress and repeat with a full backpack. Then test F1 off, moving an existing nugget, a second resource type, oxygen away from a tether, power drain, one byte grant of exactly 10,000, F10, and return to menu/reload. Confirm that no toggle carries across the session transition and the rate returns to 10×.

## Developer checks

Use Python with `lupa==2.6` for offline Lua 5.4 tests. Install it for the Python interpreter selected by `scripts/Test-Astroneer.ps1` (or pass `-Python` explicitly):

```powershell
python -m pip install --target artifacts/astroneer/python lupa==2.6
.\scripts\Test-Astroneer.ps1 -Python python
.\scripts\Build-Astroneer.ps1
.\scripts\Test.ps1
.\scripts\Build.ps1 -SkipPayloads
```

The release package does not require Python or a .NET SDK on the destination PC. Tests never launch a game or modify an installed game.
