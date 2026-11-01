# Crimson Desert PE3479 compatibility evidence

Read-only inspection on 2 November 2026 identified installed Steam build **26361232**, executable **1.0.0.3479**, length **330738072**, AMD64 machine **34404**, timestamp **1793516520**, image size **336015360**, preferred base `0x140000000` and SHA-256 `df531a585bf84ceb47779304cc8b3caaf976aea88424c32605885623cd4519c7`.

Independent package **1.0.2** ships native trainer **0.2.10** against the existing shared contracts. The HappyCheats client version and standalone EXE remain unchanged. The [PE3462 evidence](crimson-desert-3462.md) is historical; its executable is rejected by this profile.

## Current bindings

All addresses are RVAs. Normalized comparisons mask only RIP-relative operands and relative calls/branches, preserving structure offsets and argument registers. The retained complete locomotion (4225 bytes), outer-movement (2599 bytes), marker sender (426 bytes), and map caller context (64 bytes) each have one normalized match. Short ambiguous entry patterns alone do not select a binding.

| Binding | PE3479 RVA | Current evidence |
| --- | --- | --- |
| Stat commit | `0xB6C4340` | Unique 17-byte entry; RCX entry, RDX opaque argument, R8 target, R9W flag. Inspected protected body retains base +18, normalized +20, floor +28, cap +30 and current +8. |
| Damage | `0x18E55C0` | Unique longer retained signature; WORD status reference is forwarded to `0x18E59D0` at `0x18E569F`. |
| Locomotion | `0x39733F0` | Complete normalized body matches; R8 velocity and component proxy +2C0 remain unchanged. |
| Outer movement | `0x397C780` | Complete normalized body matches, including rollback; direct caller `0x965674`. |
| Marker sender | `0xC986F0` | Complete normalized body matches; map placement call `0xF65F29`, return `0xF65F2E`. |
| Proxy synchronization | `0x3C68130` | Rollback calls it at `0x397D145`, after storing XYZ at proxy +90; handle +38, transform +60 and flag +172 remain present. |
| Floating origin | `0x71A2EB0` | Independent capture at `0x397C818` and rollback subtraction at `0x397D135` resolve the same global. |
| Client root / getter | `0x71B6C50` / `0x99D5D0` | Map caller `0xF65EEE` loads root, service +30 and the getter; getter +50 owner field is at `0x99D5EA`. Map code retains owner +68, navigation +168, sender +8. |
| Normal / ChildOnly vtables | `0x590C798` / `0x590CCA0` | Current complete-object RTTI identifies both primary classes at offset zero. Both +178 slots point to unchanged predicate `0x5B2B20`. |
| Manager global | `0x71B7178` | Three independent retained manager anchors agree; current holder dereference precedes lookup `0x2D0BA30`. |
| Status registry / resolver | `0x71B7C28` / `0x3DD440` | Exact lookup guard is unchanged; count +8, cache +58 and row-key +8 remain required. |
| Status key writer | `0x1720449` | Current deserializer at `0x17203E0` names `StatusInfo` in its own error strings and stores the key at row +8. |
| Regeneration references | `0x18EA730` | WORD reference calls resolver at `0x18EA73D`; actual count read at `0x18EA702`, stride +90 at `0x18EA91A`. |

The protected commit body does not match the previous body as a whole. Its normalized store is now `0xB6C445A`, current-value store `0xB6C4477`. Two obfuscated base-offset calculations were evaluated directly against the current file: DWORD at `0xF764110` (`0x83490000`) XOR `0x83490018` gives `0x18`; DWORD at `0xD35EA5C` (`0x48000000`) plus `0xB8000018`, modulo 32 bits, also gives `0x18`. Direct +30 cap accesses and both stores were inspected separately.

The local-control predicate still compares owner +A0 with service +58 and requires owner +94. Possession round trips, fresh identities, semantic resource keys, finite values and bounded teleport writes remain mandatory. Normal and ChildOnly support is retained; no item-consumption binding or broader character support is introduced. Current resources were not extracted, and DLC gameplay is unverified.

## Validation scope

The disk verifier passes all **21** source guards, including the exact executable digest/header, hook entries, caller bytes and both vtable slots. The new captured metadata fixture rejects PE3462 as well as older profiles. Native compilation and disposable-process regressions cover hook relocation/allocation, diagnostics, health refresh, movement and teleport policy. Captured game instructions and trampolines are never executed by those fixtures.

Ignored `artifacts/compat-3479/` retains metadata, source-guard reports, bounded disassembly, normalized comparisons and evaluated constants. No game was launched or attached and no save was changed. Static inspection and synthetic checks do not prove live hooks, gameplay effects, terrain landing, streaming, exclusive fullscreen or physical-controller behavior.
