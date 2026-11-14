# Crimson Desert PE3663 compatibility evidence

Read-only inspection on 14 November 2026 identified Steam build **26636972**, executable **1.0.0.3663**, length **335440280**, AMD64 machine **34404**, timestamp **1794494872**, image size **340692992**, preferred image base `0x140000000` and SHA-256 `8be67178d9b9a149c361a731051665e0ba4d441e250ae1dfe645bd6e524345c8`.

Independent package **1.0.4** carries native trainer **0.2.12**. The client version and standalone EXE are unchanged. PE3576 and older executable profiles are rejected. Close Crimson Desert and use Library **Update** beside Sort; the current client downloads, verifies, loads and installs this trainer in one operation.

## Current bindings

All addresses are RVAs. This update changes layouts as well as function addresses. Full-body comparisons retain every structure displacement and argument register; only RIP-relative operands and relative branch/call targets are normalized. Differences are retained and reviewed rather than hidden by broader patterns.

| Binding | PE3663 RVA | Evidence |
| --- | --- | --- |
| Stat commit | `0xB80DA10` | Unique entry. RCX entry, RDX opaque argument, R8 target and R9W flag remain unchanged. Base +18 and floor +28 are now directly visible; cap +30, normalized store `0xB80DB12` (+20), current store `0xB80DB2F` (+8). |
| Damage dispatcher | `0x194D0F0` | Unique longer signature. The complete instruction comparison changes its metadata member +A0 to +78; WORD reference forwarding to helper `0x194D520` at `0x194D1CC` is retained. |
| Locomotion | `0x39F3390` | 4225 bytes / 868 instructions compared. The component's movement proxy shifts from +2C0 to **+2D0**, with corresponding component-member shifts. Argument registers and velocity XYZ behavior remain unchanged. |
| Outer movement | `0x39FC740` | 2599 bytes / 491 instructions compared, including rollback. Its component members also shift by +10. Direct caller `0x994694` retains RCX component and XMM1 delta time. |
| Marker sender | `0xCD2210` | Map caller context at `0xFA27D4` has one normalized match; sender call `0xFA280F`, return `0xFA2814`. The 426-byte sender comparison changes a diagnostic constant and retains sender/target arguments. |
| Proxy synchronization | `0x3CEBB30` | Rollback calls it at `0x39FD105`, after the proxy +90 transform store. The 228-byte comparison changes two engine identifiers; handle +38, transform +60 and flag +172 remain present. |
| Floating origin | `0x7231330` | Capture `0x39FC7D8`, intermediate update `0x39FCB46` and rollback subtraction `0x39FD0F5` independently resolve it. |
| Movement owner chain | `0x78BDC4` | Owner +68 → actor +40 → object +140 → component. Getter `0x39F1580` independently reads component +2D0. |
| Client root / getter | `0x72451C0` / `0x9CDC00` | Map code retains root +30 service and getter +50 owner; getter field at `0x9CDC1A`. Owner +68, navigation +168 and sender +8 are retained. |
| Normal / ChildOnly vtables | `0x5991108` / `0x5990BF0` | Current complete-object RTTI identifies both primary classes at offset zero. Both +178 slots resolve local-control predicate `0x5DF1C0`, retaining owner +A0 versus service +58 and owner +94 flag checks. |
| Manager global | `0x72456F0` | Four independent retained anchors agree. Current manager iteration at `0x2D7A194` reads entries +B8 and count +C0; caller `0x2BD3A18` reaches the related manager enumeration method through the same global/holder. |
| Status registry / resolver | `0x72463C8` / `0x3DAA60` | New layout: count **+28**, table **+20**, record stride **24**, cached row pointer **+8 within each record**. The resolver now accepts a pointer to a WORD reference. |
| Status key writer | `0x178B239` | The `StatusInfo` deserializer at `0x178B1D0` names the type in its own error strings and retains semantic key at row +8. |
| Regeneration references | `0x19522C0` | Dynamic vital rows remain at root +58, with actual count read at `0x1952296` (+60), WORD references at row +0 and stride +90 at `0x19524A3`. Current code forms a WORD pointer and calls the new resolver at `0x19522D7`. |

The trainer reads only already cached status rows and never invokes the engine's lazy loader or fallback-row path. It rechecks the registry, count, table, selected cache slot and semantic key before accepting a result. A flat old table, a missing row or a table/row transition fails closed. Existing Hp/Stamina/Mp keys are retained; resource archives were not re-extracted in this task. Current code independently confirms descriptor +88 / type byte +1 at `0x5E8C02`. Runtime possession round trips, complete vital chains, current-player identity and finite/bounded values remain mandatory.

## Validation scope

All **26** exact source/disk guards pass. Five additional witnesses pin the changed movement proxy, movement ownership chain, manager entries, regeneration count and regeneration stride. The captured PE3663 fixture rejects PE3576 and older profiles. Synthetic tests reject old flat metadata caches, the wrong word of a 24-byte cache record, changing tables/slots and the old movement-proxy offset. Production health and teleport fixtures use the new layouts. The historical PE2976 capture remains unchanged and is explicitly rejected by the current movement resolver.

The native payload and offline hook/allocation, diagnostic, health and outer-movement teleport suites are rebuilt. Captured game instructions and trampolines are never executed by the prologue fixtures. Shared menu/controller contracts and host-managed requested-toggle restoration are preserved; explicit Off still cancels restoration and one-shot actions are never replayed.

Ignored `artifacts/compat-3663/` retains exact metadata, byte guards, disassembly, comparison differences and validation logs. No game was launched or attached and no save was changed. Static and synthetic checks do not prove live hooks, gameplay effects, terrain landing, streaming, DLC sessions, exclusive fullscreen or physical-controller behavior.
