# Crimson Desert PE3424 compatibility evidence

Inspected the installed Steam build **26317560**, executable **1.0.0.3424**, on
30 October 2026 after downloading the Charting the Unknown update. The file is
336,264,088 bytes; SHA-256 is
`D4855C08998638F07FA8FAE08BD6249261F10C8FEC2215EFFC29CB339E7FA525`.
Its AMD64 PE timestamp is 1793296370 and SizeOfImage is 341,544,960 bytes.

## Static hook mapping

All addresses below are RVAs. Mapping used bounded pre-update comparison windows,
instruction-aware masking of relative addresses, current callsites and disassembly.
Short prologues alone were not treated as unique signatures.

| Binding | PE3424 RVA | Evidence |
| --- | --- | --- |
| Stat commit | `0xB8E0190` | Unique normalized context; base `+0x18`, normalized `+0x20`, cap `+0x30`, current `+8` and four-argument ABI retained |
| Damage | `0x18E1930` | Unique long context after accounting for a changed TLS selector; argument forwarding retained |
| Locomotion | `0x396DCD0` | Unique 256-byte normalized context; component proxy `+0x2C0` and velocity XYZ inputs retained |
| Outer movement | `0x3977060` | Unique caller at `0x964B34` and new 29-byte entry guard; component/delta-time ABI retained |
| Marker sender | `0xC97330` | Unique long context and world-map caller at `0xF64499`; original arguments and changed native message ID are forwarded unchanged |
| Transform synchronization | `0x3C62A10` | Unique long context after accounting for changed TLS slots; handle `+0x38` and transform `+0x60` retained |

The outer movement call path reaches locomotion through `0x396EFB0` and
`0x39729C0`. Its final rollback still subtracts floating origin `0x719ACB0`,
writes proxy position `+0x90`, then synchronizes the transform. Teleport must
therefore remain after the outer original call. The previous outer prologue also
matches unrelated functions in the new image; simply scanning it would be wrong.

## Player ownership and layouts

The world-map caller at `0xF6445E` loads client root global `0x71AEA50`, service
`+0x30`, then accessor `0x99C9F0`, whose owner field is `+0x50`. The same map path
uses owner `+0x68`, actor navigation `+0x168`, and sender `+8`. Its marker sender
returns to `0xF6449E`.

Three independent manager anchors resolve server global `0x71AEF78`. Manager
method `0x2D07570` confirms data `+0xB8`, count `+0xC0`, eight-byte elements and
owner descriptor `+0x88` with type byte `+1`. Instructions at `0x2CA0847` and
`0x2CA0851` confirm the owner `+0xA0` to possessor `+0xD0` roundtrip.
The vital-chain path at `0x3B13D9` uses owner `+0x68`, actor `+0x20`, marker
`+0x18`; stat accessor `0x18E1580` uses root `+0x58` and stride `0x90`.

RTTI and constructors identify the primary `ClientNormalInGameActor` vtable
at `0x59056D8` (constructor store at `0x8C4AF94`) and
`ClientChildOnlyInGameActor` at `0x5905BE0` (constructor load at `0x8C4F81E`).
Both virtual slots `+0x178`
point to `0x5B20D0`, the engine's local-control predicate: owner possessor
`+0xA0` equals the same service's self-user `+0x58`, and owner byte `+0x94`
is nonzero. The runtime pins the method's 39 bytes and ASLR-adjusted slot target,
then reproduces bounded reads rather than calling that virtual function.
Possession backreference and the complete vital chain must independently match.
The anchor is reread, and a self-user change invalidates the cached identity,
enabled controls and pending teleport even when the owner address is reused.

Trainer 0.2.7 accepts these two exact client classes and descriptor tag 1.
Other vtables, server classes, historical tag 9 and Mercenary tag 4 remain
rejected. Both classes retain the same local-self, possession, complete vital
chain and movement checks; adding ChildOnly does not authorize an arbitrary NPC.

On 30 October 2026 at 22:20 China time, a read-only observation of the user's
already-running Kliff session confirmed `ClientChildOnlyInGameActor` at
`0x5905BE0`, tag 1, local flag 1 and local-control slot `0x5B20D0`.
Service self-user matched owner possessor, its pawn pointed back to this owner,
and the vital backlink, health key `1000000`, 20-entry stat count, movement
component, proxy and navigation sender were valid. The owner's identity remained
stable across rereads. Trainer 0.2.6 incorrectly rejected this class before
checking those fields, leaving speed and both teleports unavailable while combat
controls remained available. The observed map destination was zero, so manual
teleport also correctly requires placing a marker after the identity fix;
automatic teleport can be armed before one is placed.

The observation opened only a query/read process handle. It did not inject or
replace a trainer, enable a control, place a marker, move the character, start or
stop the game, or edit a save. This confirms the availability regression and
pointer identity, not successful travel or landing.

## Resource identity

Current base and DLC `statusinfo` / `statusgroupinfo` tables were inspected
read-only. The old numeric fallback references 25 and 26 identify main-weapon
equipment entries in this data. They must not be assumed to mean stamina or
spirit. The current base references 22, 23 and 24 identify Stamina, Mp and Breath;
reference order alone is not a durable contract when DLC tables merge.

The native stat entry stores a **16-bit table reference**, not a 32-bit enum.
The trainer resolves it through the current bounded status registry and checks
the row's stable key: health `1000000`, stamina `1000026`, spirit/Mp `1000027`.
Equipment, breath, missing rows and invalid references do not authorize refills.
Damage scaling likewise verifies the health identity. This preserves semantics
when table indices move and avoids treating padding as part of a reference.
See [resource layout and evidence](crimson-desert-3424-resources.md).

## Reproducible checks and limits

`tools/InspectCrimsonDesert/verify_bindings.py` compares the source's PE/hash
guards, every code-byte guard and both local-control vtable slots against a
read-only file mapping.
It does not prove object ownership, field meanings, live hooking or gameplay.
The separate native tests exercise hooks only in disposable fixture processes.
The historical PE2976 live pointer fixture remains labeled and uses its original
addresses. Current regression tests separately exercise both PE3424 class values,
local-self/possession rejection and production teleport availability, queueing,
movement observation and cancellation in fixture-owned memory.

No game was launched, trainer attached, cheat enabled or save edited during
this inspection. DLC gameplay, other protagonists, underwater behavior,
terrain landing, streaming and physical-controller operation remain unverified.
