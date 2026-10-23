# Dawnwalker patch 3 movement compatibility

Static audit on 24 October 2026 of Steam build **25915545**, game **CL-265636**, Unreal Engine **5.5.4**. The inspected `Dawnwalker/Binaries/Win64/Dawnwalker.exe` has SHA-256 `E6222B815B1C376F2C85A2189120C5A9B5CD30AC5688A34B61A2C18EFBC9B117`.

The existing flight, manual teleport and Auto teleport contracts remain compatible with the inspected native declarations. No movement behavior change is required. Teleport continues to use the game's loading-screen fast-travel sequence, as requested.

## Verified declarations

Generated reflection records were recovered from the executable and compared with the earlier build's captured metadata. All **25 function declarations across 20 inspected names** match the earlier parameter names, types and offsets. These include the movement setter, swept vertical movement, input queries, frame delta, player/subsystem lookups, capsule dimensions, ground/clearance traces, rotation/location getters, marker set/unset/query and native fast travel.

| Contract | Patch 3 evidence |
| --- | --- |
| `HitResult` | Size 256 bytes; its reflected field offsets match the previous capture. `bBlockingHit` and `bStartPenetrating` remain at byte `0xAD`, masks `0x01` and `0x02`. Generated setters at RVAs `0x2870DD0` and `0x286AF40` directly OR these masks into that byte. The existing read-only packed-byte workaround remains necessary for the pinned loader. |
| `Rotator` | Size 24 bytes, axes at offsets 0/8/16. Static descriptors spell them `Pitch`, `Yaw`, `Roll`; the earlier live capture exposed lowercase `pitch`. The existing validator accepts case variations and preserves the actual returned keys when supplying the same struct type to travel. No angle is defaulted to zero. |
| `Vector` / `Key` | `Vector` remains 24 bytes with X/Y/Z at 0/8/16. `Key` remains 24 bytes with its reflected `KeyName` at offset 0; the existing FName input mapping is unchanged. |
| Traces | `LineTraceSingle` retains OUT `HitResult` at offset 88 and Boolean return at 384; `CapsuleTraceSingle` retains them at 96 and 392. Generated native parameter-struct sizes, including trailing padding, are 392 and 400 bytes. Both remain below the loader's 512-byte argument buffer. |
| Capsule / slope | Scaled radius and half-height getters still return a float. `WalkableFloorZ` remains a reflected float at offset `0x1D0`; the trainer accesses it by name. |
| Flight | `SetMovementMode` still takes two bytes. `MOVE_Flying` remains 5; normal/custom movement values are unchanged. `K2_AddActorWorldOffset` retains vector, sweep flag, OUT hit and teleport flag in the same order. |
| Travel / markers | `FastTravelToLocation` still takes `Vector` at offset 0 and `Rotator` at 24, returning its enum at 48. Failure/MissingMapPin/FastTravel remain 0/1/2. Marker hook/query parameter layouts are unchanged. |

## Native travel sequence

The new fast-travel wrapper is RVA `0x604AA80`, calling implementation `0x604FED8`. The timer setup is `0x6051238`, phase callback `0x6051DC0`, safety-platform setup `0x6051850`, and final floor/capsule placement `0x6051B14`. The timer, phase callback and final-placement instruction sequences match the prior audit after address normalization. The entry retains its validation and state setup; its class check is now factored into a helper. The saved portion of the earlier safety-platform routine also matches.

The sequence still performs the game's area streaming, placement and movement restoration. Its floor-miss branch still uses the supplied destination plus capsule offset, so the trainer must retain its independent loaded-ground, slope, overlap and clearance checks. No direct relocation or loading-screen bypass was introduced.

## Scope and evidence

This audit only read the executable on disk. The game was not launched or attached to, and no game function, save or installed file was changed. It establishes static compatibility of the inspected bindings; it does **not** establish successful loader startup, current Blueprint behavior, flight, physical-controller input or teleport gameplay on patch 3. Those remain live-validation gaps, not detected native-contract mismatches.

Reproducible local evidence is under `artifacts/dawnwalker/update-20261024-movement/`: `audit_static.py`, `extract_contracts.py`, `native_pipeline.py`, `validate_contracts.py`, `contracts.json`, `compatibility-summary.json` and the corresponding disassemblies. These transient artifacts and extracted game metadata are not release source or distributable assets.
