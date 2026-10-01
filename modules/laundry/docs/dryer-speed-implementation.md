# Dryer speed: static implementation evidence

The implementation uses the cached game 7.1.4 / Steam build 22778860 artifacts already analyzed in this workspace. No game process was started, attached to, or modified for this feature, and no tests were run for this change.

The existing supported-build guard still requires GameAssembly SHA256 `7CB67E3558EB20B78090BF6A28DE3DCB002EC48970444E40CB6974C83703D7D5` and metadata SHA256 `A3B7237C05C00A3D46713B1C022BD8075B79B84A08C649679EDB5B13F9317761`.

## Cached evidence

- `artifacts/research/il2cpp/dump.cs`, type `DryingMachinePickableScript` (TypeDefIndex 405), declares `private void DryingUpdate()` at RVA `0x73ED10` and `private float timer` at offset `0xA0`.
- `artifacts/research/interop/verified-members.txt`, lines 94 and 128, confirms generated `Il2Cpp.DryingMachinePickableScript.timer` is a public `System.Single` property with get/set and `DryingUpdate()` is a public instance `System.Void` method with no arguments.
- `artifacts/research/dryer-native.txt`: `DryingEnter()` initializes the timer from `dryingDuration` at RVA `0x73EB31–0x73EB47`, subject to an existing game branch that substitutes a constant.
- `DryingUpdate()` reads the timer, calls `Time.get_deltaTime`, subtracts, and writes the result at RVA `0x73ED8B–0x73EDA5`. At `0x73EEBB–0x73EEE7`, a nonpositive timer makes the original method enter FSM state `Done` (3). `DryingExit()` zeroes the timer at `0x73EC99`.

## Patch behavior

`DryerSpeed` installs its own Harmony prefix/postfix on `DryingUpdate`. The prefix observes the timer and the postfix subtracts extra elapsed time only when the original method reduced a still-positive timer. An unchanged, increasing, nonfinite, or already completed timer is left alone. The normal game method completes the cycle on its next invocation if acceleration reaches zero. The patch does not replace state transitions, duration settings, global time, animations, or obstacle logic.

The multiplier defaults to 5 and accepts finite values from 1 to 20; 1 leaves normal speed. Changes also affect a cycle already in progress. The feature starts disabled and reports and disables only its own patch if reading or writing its timer fails. Its Harmony owner and disposal are separate from washing speed.

This is evidence from metadata, disassembly, and source review. Harmony execution and actual drying times remain unverified in the running game.
