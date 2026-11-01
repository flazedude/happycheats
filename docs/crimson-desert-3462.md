# Crimson Desert PE3462 compatibility evidence

Read-only inspection on 1 November 2026 identified installed Steam build **26356046**, executable **1.0.0.3462**, length **328690072**, AMD64 machine **34404**, timestamp **1793455655**, image size **333967360**, preferred base `0x140000000` and SHA-256 `2c98b00e8922f14ac1937b5ac996956c7b9ca2d0fbcb0049dc0436321a09dd77`.

Package **1.0.1** ships native trainer **0.2.9** without changing client **0.3.66**. The previous [PE3446 profile](crimson-desert-3446.md) remains historical; it is rejected by the new exact build guard.

## Current mappings

All addresses are RVAs. The retained Trinity entry signatures uniquely locate stat commit, damage and locomotion. The outer-movement entry and its caller also each have one normalized match. Short ambiguous prologues alone were not used to select other bindings: the world-map getter/caller chain, rollback call target and RTTI establish their roles.

| Binding | Current RVA | Evidence |
| --- | --- | --- |
| Stat commit | `0xB5DBEA0` | Unique entry; RCX entry, RDX opaque argument, R8 target, R9W flag. Base +18, cap +30, normalized store at `0xB5DBFBD`, current store at `0xB5DBFDA` (+8). |
| Damage | `0x18E5610` | Unique longer signature; word status reference forwards at `0x18E56EF` to `0x18E5A20`. |
| Locomotion | `0x39734C0` | Unique longer signature; R8 velocity preserved in RDI, component proxy +2C0. |
| Outer movement | `0x397C850` | Full saved-start rollback path; caller at `0x9656A4`; original position restored at `0x397D20D` before sync. |
| Marker sender | `0xC98720` | World-map getter at `0xF65F49`, owner +68, navigation +168, sender +8; call `0xF65F79`, return `0xF65F7E`. |
| Proxy sync | `0x3C68200` | Called by rollback at `0x397D215`; retains handle +38, transform +60 and flag +172. |
| Floating origin | `0x71A2EB0` | Independently referenced by initial world-position capture `0x397C8E8` and rollback subtraction `0x397D205`. |
| Client root | `0x71B6C50` | Current map caller `0xF65F3E` and independent local-control predicate `0x5B2B20`. |
| Client getter | `0x99D600` | Service +50 field at `0x99D61A`. |
| Normal / ChildOnly vtables | `0x590C798` / `0x590CCA0` | MSVC complete-object RTTI names `ClientNormalInGameActor` and `ClientChildOnlyInGameActor`, offset zero. Both virtual +178 slots point to `0x5B2B20`. |
| Manager global | `0x71B7178` | Three independent retained manager signatures agree; the anchor at `0x2A688E2` dereferences the holder before lookup. |
| Status registry | `0x71B7C28` | Resolver `0x3DD440`: global load at `0x3DD455`, count +8, cached row table +58. |
| Status key writer | `0x1720499` | Current `StatusInfo` deserializer `0x1720430`, identified by its own error strings, stores the key at row +8. The nearby `StatusGroupInfo` writer is a separate structure. |
| Regeneration metadata | `0x18EA780` | WORD references call current resolver; actual array count read at `0x18EA752`, stride +90 at `0x18EA96A`. |

The local-control predicate still compares owner +A0 to service +58 and requires owner +94. Current constructor/destructor assignments at `0x8D624C4` and `0x8D62E64` independently reference the Normal/ChildOnly vtables. Movement callers at `0x5B805F` and `0x75BB74` retain owner +68 → actor +40 → object +140. Runtime ownership round trips, identity refresh, finite values and semantic Hp/Stamina/Mp key checks remain mandatory. No item-consumption binding is introduced. No current resource extraction or DLC gameplay claim is made.

## Validation scope

`verify_bindings.py` checks all **21** current source guards against the disk image, including the exact executable digest/header, hook entries, caller bytes and both local-control vtable slots. The captured metadata fixture and synthetic class fixtures are updated to PE3462; older metadata still fails closed. Disposable-process hook tests never execute captured game instructions or trampolines.

Ignored `artifacts/compat-3462/` contains the inspection, bounded disassembly, signature and RTTI reports. Neither a game process nor a save was opened or modified. Static inspection and offline/synthetic regressions do not prove runtime hooks, gameplay effects, terrain landing, streaming, exclusive fullscreen or physical-controller behavior.
