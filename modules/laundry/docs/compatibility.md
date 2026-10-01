# Compatibility

Inspected locally on 2026-09-12: Steam app 3150440, build 22778860, game version 7.1.4, Unity 2022.3.62f2, Windows x64 IL2CPP metadata version 31.

| Input | SHA256 |
| --- | --- |
| GameAssembly.dll | `7CB67E3558EB20B78090BF6A28DE3DCB002EC48970444E40CB6974C83703D7D5` |
| global-metadata.dat | `A3B7237C05C00A3D46713B1C022BD8075B79B84A08C649679EDB5B13F9317761` |

Both hashes are required at startup. The trainer disables all actions if either differs. Do not update the constants merely to bypass this check: inspect the new method bodies and wrapper signatures, then test the new build.

## Bindings

The following generated types are in Assembly-CSharp.dll under the `Il2Cpp` namespace. Global-namespace and older prefix forms are accepted only when the binary fingerprints still match.

- `GameManager.AddCurrency(MoneyType, float, bool)` adds cash with `MoneyType.Lp`. `GetCurrency(MoneyType)` reads the resulting balance. `isFinishLoading` gates changes. The normal method path handles the game's obscured numeric representation and update events.
- `LevelXPManager.AddXP(float, bool, bool)` adds player XP with the optional IAP multiplier off and popup on. `hasLoaded` gates changes. The return value reports the actual addition after game limits and bonuses. Shop progression is independent and uses the separate F10 action below.
- `WashingMachinePickableScript.WashingUpdate()` advances its `timer` float property. A Harmony prefix captures the timer and a postfix measures the decrease caused by the original method. It subtracts the additional elapsed time for the chosen multiplier, clamped to zero. A paused, blocked, reset, or already-completed timer is left alone. The original method performs completion on its next update.
- `DryingMachinePickableScript.DryingUpdate()` counts down its own `timer`. A separate Harmony prefix/postfix extends only the decrease made by the original method and lets the game handle completion. The default is 5x, adjustable from 1–20x, and disabled at startup/scene changes. See [static dryer evidence](dryer-speed-implementation.md).
- `LaundryTierSystem` owns shop progression. F10 fills the current upgrade's XP, then challenge requirements, and invokes the game's one-tier promotion path, respecting its maximum rank. No direct rank overwrite is used. See [static shop evidence](shop-level-implementation.md).

The two 0.2.0 additions were inspected statically and compiled; neither automated tests nor live game checks were performed for this revision, as requested.

Stored singleton `_instance` properties are read on constructed generic base types. The trainer does not invoke singleton getters that could create new scene objects. Game calls run through MelonLoader's main-thread update callback; it does not make gameplay calls from a background thread.

## Evidence and dependencies

Local metadata and native methods were inspected using official [Il2CppDumper 6.7.46](https://github.com/Perfare/Il2CppDumper/releases/tag/v6.7.46). Native washer countdown behavior was confirmed at `WashingUpdate` RVA `0x75B470`; `WashingEnter` RVA `0x75B180` initializes it. These addresses are research evidence and are not used for patching. Generated wrapper shapes were independently checked offline with Il2CppInterop 1.5.1 using MelonLoader's prefix settings.

The project compiles against official [LavaGang.MelonLoader 0.7.3](https://www.nuget.org/packages/LavaGang.MelonLoader/0.7.3). It ships only the mod DLL. MelonLoader supplies Harmony and IL2CPP interop at runtime. The package's unused legacy Microsoft.Bcl dependency is excluded; its framework warning is suppressed only for that package.

Loader archive SHA256: `5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416`, pinned to the official [v0.7.3 release](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3).
