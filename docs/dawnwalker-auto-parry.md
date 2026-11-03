# Dawnwalker automatic parry

Trainer **0.5.2**, independent Dawnwalker package **1.0.3**, uses the existing exact executable pin for Steam build **25915545 / CL-265636**. Enable **Auto parry** with **NumPad5** or the main client/shared menu. It starts off and resets on player/world changes. Existing controller shortcuts, Ctrl+Del, Menu+RB and instant marker teleport are unchanged.

The option watches the game's next incoming attacker on the shared 16 ms game-thread timer, active only while flight or auto parry is enabled. It uses native predicted hit-window time and the animation parry point to request one directional guard. It releases its request after a bounded pulse, attack cancellation, target changes, pause/cutscene, lost possession, disable or session transition. The existing 250 ms heartbeat remains. No synthetic input, damage-result replacement, additional native hooks or global actor scan is used.

Eligible enemies must be living, hostile, in the same world and targeting the current player's combat component. Attacks require a valid action, montage, instance, timestamp and direction and must not be flagged unblockable. The enemy must lie inside the native defensive facing cone, within 1,200 horizontal and 300 vertical game units. Native stamina, defense availability, animation, cooldown and parry-window rules still decide the result. Simultaneous, rear, projectile, grab and scripted attacks are not guaranteed to be defended. The displayed count records **attempts**, not confirmed parries.

The guard press precedes direction selection: both native operations update cooldown timestamps, so reversing the order can trigger the anti-spam penalty. The native `ParryInputCooldown` is respected with a 32 ms margin. Each attack is identified by enemy/action/montage identity, montage instance and `TimePerformed`; history is bounded to 64 identities. Missing/inconsistent data starts no new guard request.

Manual guard takes priority. The validated controller's own `EnhancedPlayerInput.EnhancedActionMappings` is read for effective `IA_Combat_Block` mappings, including remapped keys and gamepad buttons. Each FKey is reconstructed with a copied FName; borrowed mapping wrappers and shared KeyDetails allocations are not retained across context rebuilds. Physical key states are checked before starting/releasing guard. Missing combat mappings are a normal waiting state when arming in exploration; unavailable or malformed input data fails closed. An existing pulse retains verified keys for cleanup if opening a menu removes the mapping context; new pulses require current mappings. Pressing manual guard during a pulse hands ownership to the player. Later game/player direction, component and request changes are preserved. Failed cleanup disables auto parry, reports the problem and prevents rearming until cleanup succeeds. `GetBoundActionValue` remains unused because the inspected pawn had zero value bindings.

The 0.5.1 interface call was invalid: exact-build `QueryKeysMappedToAction` thunk **0x59E05DC** dispatches vtable slot **0xD0** on an interface pointer. The captured subsystem's interface subobject is at **+0x30**, whose slot targets **0x59D6F48**; passing its UObject address selects **0x125B890** instead. UE4SS ProcessEvent forwarding does not supply this interface adjustment. The new path uses reflected mapping properties and makes no interface exec call or pointer adjustment. The captured exploration map had 64–72 rows, L1 mapped to drawing the sword and no active block row; the loaded RT combat mapping context separately contains the block action. The user confirmed manual block works on L1. This is user feedback, not agent-operated physical-controller or successful auto-parry validation.

## Exact-build inspection

These RVAs are audit references, not runtime offsets. Gameplay fields are reflected.

| Contract | Evidence |
| --- | --- |
| `DogwoodAICombatBlueprintFunctionLibrary.GetNextIncomingAttacker` | Player stub, separate OUT bool `bIsHitIncoming`, OUT float `TimeToHitWindow`, returned enemy stub. Thunk `0x60FC7B4`; native `0x60F51B0` selects the earliest hit and subtracts world game time. |
| `CombatBlueprintFunctionLibrary.GetParryPointNotificationTime` | Enemy stub to absolute world-time float. Thunk `0x5FE8D40`, implementation `0x600517C`; missing notifies may return a sentinel. |
| `CombatComponentBase.SetDesiredBlockState` | Bool. Thunk `0x5FEF6B4`, current PlayerCombatComponent virtual target `0x6044FE8`. |
| `PlayerCombatComponent.SetBlockingDirection` | `EBlockingDirection`; thunk `0x5FFC7CC`, change notification `0x603FAB4`. |
| Native direction conversion | `0x60391D0`: attack Left (1) → guard Right (8), Right (2) → Left (4), Top (3) → Top (1), Bottom (4) → Bottom (2). Unknown directions are rejected, avoiding the native random fallback. |
| Native defense decision | Player target `0x603952C`, enemy parry eligibility `0x603D29C`; neither is intercepted or overridden. |
| Cooldown | Reflected `CombatConfig.ParryInputCooldown`; current read-only value **0.5 seconds**. No config or attribute is changed. |
| Guard-key query | Controller-owned `EnhancedPlayerInput.EnhancedActionMappings` (reflected offset 0x538); reflected EnhancedActionKeyMapping size 0x50, Action +0x20, Key +0x28; runtime uses reflected names, not those offsets. No interface thunk is invoked. |

Marshalling was checked against pinned UE4SS `2bfa839f`, including [explicit function contexts and OUT tables](https://github.com/UE4SS-RE/RE-UE4SS/blob/2bfa839f/UE4SS/src/LuaType/LuaUObject.cpp).

## Validation limits

Read-only inspection used the already-running exact-hash game. It captured metadata, virtual targets, enums, input bindings and configuration. A bounded 45-second passive capture from ten cached enemy combat components recorded **no attacks targeting the player**; it establishes neither attack coverage nor real timing. No game function/cheat was activated, input generated, save edited or running trainer installation changed.

Offline suites cover timing, guard ordering, direction mapping, cooldown/deduplication, manual remapping/handoff, cancellation/pause, possession loss, partial native failures, cleanup/rearm protection, bounded history, production routing, keyboard/protocol and shared timer lifecycle. Module/installer and catalog menu checks cover the new metadata and payload. Actual parry success, physical controllers and frame-time cost remain unverified.

Close Dawnwalker, choose its **Update** action in Library’s Cheat updates window, relaunch and enable **Auto parry**. Installation into the running game remains refused. Live combat validation is pending.
