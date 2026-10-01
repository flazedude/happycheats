# Astroneer — HappyCheats module 0.1.3

Experimental Steam integration for Astroneer **1.43.2.0**, Steam build **24409322**, app **361420**. The exact inspected shipping executable SHA-256 is `E1764F108E51BB172148F3A4AC71AE38AC031D9ECEB41AD213E139A9CBA61F56`. Other executable builds are rejected until inspected.

Close Astroneer, open `dist/HappyCheats/HappyCheats.exe`, select **ASTRONEER / 异星探险家**, and choose **Install / update**. Launch through Steam and load a world hosted on this PC. Controls become available after a fresh game heartbeat confirms the required bindings and gameplay state. All toggles start off and reset on character, world, lifecycle, authority, or local-possession changes.

| Key | Control | Scope |
| --- | --- | --- |
| F1 | Resource collection · 10× / 20× | Multiplies new positive terrain-resource fragment amounts by the selected rate before the game's resource-cache callback receives them |
| F2 | Unlimited oxygen | Temporarily disables the local astronaut's oxygen consumption using non-save debug/runtime properties |
| F3 | Unlimited backpack power | Refills the local backpack's battery through the game's power component |
| F4 | Add 10,000 bytes | Requests a one-time grant through the game's research system |
| F10 | Disable all | Game-focused shortcut; the launcher also has a Disable all button |

Choose **10×** or **20×** on the collection card. Selection alone does not enable collection. When enabled, a rate change applies to the next accepted fragment amount; turning collection off or using Disable all preserves the choice. A new session resets the choice to 10× and turns collection off. HappyCheats displays the game's confirmed selection after acknowledgement. Rates are mutually exclusive and never stack.

F1–F4 work with either the game or HappyCheats focused. Runtime keys use UE4SS's focus-scoped key registration. F1 toggles collection using the selected rate. The local astronaut must have authority over the world. This supports a solo world that Astroneer internally runs as a listen server. Remote clients are rejected. Co-op effects have not been validated; research bytes belong to the hosted world's shared research system.

The collection control targets `ResourceCacheComponent.OnAcceptResource(Type, Amount)`. It handles resource fragments collected by the local terrain tool, including partial nuggets; it does not duplicate finished items when moving them between inventory slots, multiply crafting output, or hook `StoreResource` a second time. Power and oxygen resource types are excluded. Backpack capacity and the game's nugget creation/overflow behavior remain relevant. The UI counters measure amounts passed into the collection handler, **not confirmed nuggets produced**. Actual nugget yield still needs gameplay verification.

Oxygen's previous consumption/debug values and the backpack's captured charge are restored when disabled. Previously collected resources and research grants may persist through normal game saving. Soil was investigated but is not exposed because equipped-canister ownership has not been verified.

## Implementation evidence

- [ResourceCacheComponent](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/ResourceCacheComponent.h) and [OnAcceptResource delegate](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/OnAcceptResourceDelegate.h) identify the reflected collection event and signed integer amount.
- [AstroCharacter](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/AstroCharacter.h), [PowerComponent](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/PowerComponent.h), and [ResearchSystem](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/ResearchSystem.h) identify the companion controls' APIs.
- [AstroGameMode](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/AstroGameMode.h) and [EGameLifecycleState](https://github.com/AstroTechies/ModdingKit/blob/master/Source/Astro/Public/EGameLifecycleState.h) identify `GameStarted = 9`; the menu's preview astronaut is not sufficient evidence of loaded gameplay.
- [Astroneer modding FAQ](https://astroneermodding.readthedocs.io/en/latest/guides/faq.html) documents UE4SS use. [UE4SS key registration](https://docs.ue4ss.com/lua-api/global-functions/registerkeybind.html) documents focus-scoped callbacks.
- [Astroneer's terrain tool](https://astroneer.wiki.gg/wiki/Terrain_Tool) describes fragments accumulating into nuggets, partial resource caches, and overflow to the ground.

These community SDK declarations are binding evidence, not proof that every behavior works on this game build. Runtime lookup and ownership/lifecycle checks fail closed when a binding is absent. No proprietary game assembly is redistributed.

## Installation and protocol

The package contains UE4SS `3.0.1-1133-gb4cefa18`, pinned to archive SHA-256 `89B7EED47C37D6FF6EAA144A41311A75098279A3454777F4EDD2446CEA1EA7A8`. Provenance and its MIT license are in `payload/astroneer/licenses`. The native loader and mod live below `Astro/Binaries/Win64`; the other games' loaders are separate.

The installer checks each payload hash, rejects conflicting or modified files, stages writes, publishes the native bootstrap last, and rolls back failed transactions. It tracks files actually written in `happycheats-astroneer-install.json`. Unowned or modified files survive removal. If another UE4SS mod is present, shared loader files are retained. Existing foreign UE4SS settings may require manual reconciliation; the installer does not overwrite them.

The Lua bridge accepts a fixed line-based command format; it never executes command text as code. Commands have an eight-second expiry, request IDs, current-session matching and duplicate suppression. The `set_collection_multiplier` action accepts only 10 or 20, checks collection availability, and does not change the toggle. Arithmetic rejects input that would overflow a signed 32-bit amount at the chosen rate. The launcher validates a unique process at the exact shipping executable path, file freshness relative to process start, runtime version, and feature availability; it waits for game acknowledgements. No save file is directly edited by the trainer.

## Validation — 13 September 2026

Completed: offline C# installation/protocol tests; Lua arithmetic, mocked bindings and protocol tests; release compilation; Chinese/English WPF captures; Steam discovery and exact executable inspection. The installed **0.1.0 trial** successfully booted UE4SS on the actual game and registered native hook IDs `(1, 2)` for `OnAcceptResource`. It emitted a fresh heartbeat and resolved oxygen, backpack-power and byte APIs.

That startup check exposed the menu preview astronaut being marked ready; **0.1.1** added the game's lifecycle check. A subsequent live heartbeat from the user's loaded world correctly reported `diagnostic_lifecycle = 9`, but returned `Single-player session required`: the `IsStandalone` check incorrectly rejected the game's host-style world. **0.1.2** checks local possession and `AActor.HasAuthority()` instead, preserving the lifecycle guard. Regression tests include a locally controlled authoritative player with `IsStandalone = false`, remote clients, lost possession, and menu transitions. The launcher now shows the specific blocking reason instead of a generic instruction to load a game.

The **0.1.2 fix was installed and verified in the actual game**: menu lifecycle 8 correctly reported not ready; loading the user's recently saved world produced lifecycle 9, local control and authority true, `Connected = true`, `Ready = true`, and all four public controls available. Clicking the HappyCheats 10× collection button received a successful game acknowledgement and `collection_multiplier = 10`. The 10× toggle was left enabled; oxygen and power stayed off and no research bytes were granted. Evidence is saved in `artifacts/astroneer/host-ready-0.1.2.json` and `host-enabled-0.1.2.json`.

The user's subsequent gameplay produced **1,098 accepted collection events** in the 0.1.2 heartbeat: 2,293,008 base fragments and 22,930,080 total fragments, exactly 10×, with zero rejected events. This verifies the runtime's collection callback measurements; final nugget production was not counted. Evidence: `artifacts/astroneer/collection-observed-0.1.2.json`.

**0.1.3 adds selectable 10× / 20× collection.** Offline checks cover both rates, the 20× signed-integer overflow boundary, selection without enabling, switching rates while enabled, preserving selection when disabled, session reset, strict command fields, and confirmed status publication. All three Lua suites pass (163 arithmetic/binding assertions, 88 bridge/hotkey assertions, plus protocol checks), and the full .NET regression suites pass. Chinese and English selector layouts were rendered and inspected. Installation and live 20× selection verification are pending the running game's restart.

**Final nugget output, oxygen/power behavior, byte grants, hotkeys and restoration have not yet been verified in gameplay.** Existing save files were copied into timestamped local backups under `artifacts/astroneer` before verification launches. Close the game before installing **0.1.3**; the launcher rejects older module heartbeats until the current module is loaded.

For a live check, use a separate solo test save: mine one resource with F1 off, observe a baseline fragment count, enable F1, and confirm each accepted amount changes from `n` to `10n`. Switch to 20× and confirm the next accepted amount becomes `20n`. Count full nuggets as well as partial-cache progress and repeat with a full backpack. Then test F1 off, moving an existing nugget, a second resource type, oxygen away from a tether, power drain, one byte grant of exactly 10,000, F10, and return to menu/reload. Confirm that no toggle carries across the session transition and the rate returns to 10×.

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
