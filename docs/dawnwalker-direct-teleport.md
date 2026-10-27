# Dawnwalker direct marker teleport

Static inspection on 27 October 2026 supports using the possessed player's
`Actor.K2_TeleportTo` for destinations whose ground and capsule space are already
loaded. This bypasses the trainer's call to `FastTravelSystem.FastTravelToLocation`
and its loading sequence. It does not make unloaded destinations safe.

The inspected Steam build is **25915545 / CL-265636**, executable SHA-256
`E6222B815B1C376F2C85A2189120C5A9B5CD30AC5688A34B61A2C18EFBC9B117`.
All addresses below are RVAs in that exact executable. Inspection read only the
file on disk; no game process was opened, launched, stopped or modified.

## Native contracts

| Binding | Static evidence |
| --- | --- |
| `Actor.K2_TeleportTo` | Generated function parameters at `0x9FC6EF0`: `DestLocation` Vector at 0, `DestRotation` Rotator at 24, Boolean `ReturnValue` at 48, total native frame size 56. The return setter at `0x2873660` writes byte `+0x30`. Vector and Rotator are the same 24-byte structs used by the existing location/rotation getters. |
| Native teleport dispatch | Exec wrapper `0x503A838` calls actor virtual slot `0x5D8` with both `bIsATest` and `bNoCheck` false, then copies its Boolean return. The `DawnwalkerPlayerCharacter` constructor at `0x1DCBBB8` installs vtable `0x798AA00`; this slot points to `Actor::TeleportTo` at `0x502F530`. It is not a fast-travel override. |
| Collision and position | The actor implementation requires a root component, checks destination encroachment, and can adjust the destination to find room. The move uses the root component with teleport-physics mode. It takes the actor/root position, so a capsule-root character needs the checked **capsule center**, `ground.Z + scaledHalfHeight + clearance`, rather than the ground-contact point. |
| Character notification | Successful movement calls actor slot `0x5E0`, which is `0x54C05F0` for this player. That obtains the movement component through actor slot `0x728` and calls its teleport notification at slot `0x630`. The getter at `0x2881920` reads the character's native movement component. |
| Player movement notification | `DWPlayerMovementComponent` registration is `0x20F1E18`; its constructor wrapper `0x1A36068` reaches `0x1A36094`, which installs vtable `0x78AC900`. Slot `0x630` points to `0x515E1A0`: it sets `bJustTeleported` and refreshes floor, movement mode/base state. The reflected flag setter `0x286DC50` confirms byte `+0x554`, mask `0x20`; production code must not write that offset directly. |
| `MovementComponent.StopMovementImmediately` | No parameters or return value; generated function descriptor `0x9FD7890`, exec wrapper `0x519D998`, virtual slot `0x550`. The player's movement vtable resolves this to `0x1882FA4`, which calls `0x1882FC8` to clear velocity and update component velocity, then invokes the native movement interface. `Velocity`'s reflected Vector descriptor at `0x91285E0` is offset `0xD0`; the zeroing function writes the verified 24-byte zero constant from `0xA123038` there. Use the reflected call on the owned movement component, never raw writes. |

No fast-travel, map reload or loading-screen request appears in the inspected
teleport/notification chain. Moving the actor can still cause ordinary world
streaming and overlap/gameplay callbacks; static inspection cannot rule out every
Blueprint or scripted response to a destination.

## Required trainer behavior

Retain the existing current-player/session checks, same-map custom marker,
living player, player-control checks, loaded-ground trace, slope test, packed
HitResult decoding and capsule-clearance sweep. Require the actor's root to be
the validated capsule component. Preserve the actual reflected rotation keys.

The teleport notification does not unconditionally clear velocity. Call the
verified native `StopMovementImmediately` only after the destination and ownership
checks, immediately before the direct teleport, so falling or running velocity
is not deliberately carried into the landing. That stop is itself a mutation;
a later rejected teleport may still have stopped the character.

Treat only a Boolean `true` as acceptance and check position after the native
call and again on a subsequent normal game-thread poll. Native collision
adjustment means the observed position can differ slightly from the requested
center; use an explicit bounded tolerance. A true return alone does not prove
the game's asynchronous locomotion keeps the new position. Errors, malformed
results and failed confirmation must not cause an automatic retry or a fallback
to native fast travel. Automatic requests still require a fresh marker and must
not overlap pending confirmation.

## Validation limits

This establishes the inspected native call contracts and ownership of the
teleport notification path. It does not establish gameplay success, absence of
all loading hitches, collision/streaming behavior at every destination, camera
behavior, fall damage or controller behavior. Those need separately authorized
gameplay verification. The user confirmed the previous fast-travel method worked,
but subsequently reported its loading behavior and requested direct movement.

Local reproducible evidence is in
`artifacts/dawnwalker/direct-teleport-20261027/`, including `audit.py`, `native.py`,
`proof.py`, `native-proof.json` and focused disassemblies. Extracted binary data
and these transient working artifacts are not source-control deliverables.
