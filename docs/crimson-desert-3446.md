# Crimson Desert 3446 static compatibility evidence

This profile was inspected on 31 October 2026 from the installed executable
and a bounded set of installed resource files. No game was launched, no process
memory was read, and no game file or save was modified. Static checks establish
the bindings below; they do not establish gameplay effectiveness.

## Executable identity

| Field | Observed value |
| --- | --- |
| Steam build | `26329202` |
| File/product version | `1.0.0.3446` |
| File length | `332786072` bytes |
| SHA-256 | `fec07be1b725a46624d31fcd74bf3730ea809765226cea6bde01607ab2d79f66` |
| PE machine | `34404` / AMD64 |
| PE timestamp | `1793355216` |
| SizeOfImage | `338067456` |
| Preferred image base | `0x140000000` |

The earlier 3424 and 2976 captures remain historical fixtures. Their addresses
are not evidence for an active 3446 session.

## Hook and movement mapping

Addresses in this document are RVAs. Searches compared saved 3424 instruction
bytes with the new executable, masking only RIP-relative displacements and
relative branch/call operands. No structure offsets or argument registers were
masked. The following complete saved function bodies each have one normalized
match in the new executable:

| Function | 3424 RVA | 3446 RVA | Compared bytes |
| --- | --- | --- | ---: |
| Damage dispatcher | `0x18E1930` | `0x18E2500` | 347 |
| Locomotion | `0x396DCD0` | `0x396F5D0` | 4225 |
| Outer movement | `0x3977060` | `0x3978960` | 2599 |
| Marker sender | `0xC97330` | `0xC973F0` | 426 |
| Proxy synchronization | `0x3C62A10` | `0x3C64310` | 228 |
| Movement substep | `0x396EFB0` | `0x39708B0` | 1702 |
| Movement dispatch | `0x396F660` | `0x3970F60` | 2432 |
| Additional locomotion caller | `0x396CD00` | `0x396E600` | 554 |

The commit function moved from `0xB8E0190` to `0xB764900`. Its 17-byte hook
entry remains unique and unchanged, but its protected body does **not** match
the old body as a whole. Direct inspection establishes the same arguments:
RCX is the entry, RDX is forwarded as the opaque second argument, R8 is the
target value and R9W is the flag. Base `+0x18`, normalized value `+0x20`, floor
`+0x28`, cap `+0x30` and current value `+8` retain their roles. The normalized
store is at `0xB764A2D`; the current-value store is at `0xB764A4A`.

Three changed offset calculations were evaluated against the actual file:

| Instruction RVA | Calculation, modulo 32 bits | Result |
| --- | --- | --- |
| `0xB764944` | `0xF73B7CD0 + DWORD[0x1229D088]`, value `0x08C48348` | base `0x18` |
| `0xB7649AF` | `0xC1C749C8 XOR DWORD[0xB5D28C8]`, value `0xC1C749F8` | cap `0x30` |
| `0xB7649D9` | `0x7FC5C739 XOR DWORD[0xCC48868]`, value `0x7FC5C749` | stack argument `0x70` |

All six existing entry guards are unchanged. The caller bindings change to:

| Binding | RVA | Exact guarded bytes |
| --- | --- | --- |
| Outer-movement caller | `0x964B64` | `C5 FA 59 CF 48 8B CE E8 F0 3D 01 03 4C 8D 9C 24 80 01 00 00` |
| Marker UI call | `0xF64969` | `E8 82 2A D3 FF 83 E3 FE` |
| Marker UI return | `0xF6496E` | return immediately after that call |

The outer caller also uniquely matches 253 normalized bytes from the saved
caller context. Two short marker-call shapes reach the current sender, so the
eight-byte pattern alone is insufficient to choose the UI event. Only
`0xF64969` matches the saved 64-byte caller context and follows the current
client getter, owner `+0x68`, actor navigation `+0x168`, and sender `+8` chain.

The current outer update calls substep `0x39708B0` at `0x3978EB0`. That calls
`0x39742C0` at `0x3970A57`, which calls locomotion at `0x39745A6` and
`0x3974769`. The locomotion input remains an XYZ vector passed in R8; the
component's proxy remains at `+0x2C0`.

Floating origin is now `0x719DCA0`, independently referenced by the outer
update at `0x39789F8`, `0x3978D66` and `0x3979315`. The rollback path subtracts
that origin, stores the transform at proxy `+0x90` at `0x397931D`, then calls
the mapped synchronization function at `0x3979325`. Synchronization retains
its handle `+0x38`, transform `+0x60` and flag `+0x172` accesses.

## Identity and resource contracts

The client root is `0x71B1A40`; the current getter is `0x99CAB0`, with its
service `+0x50` access at `0x99CACA`. The world-map caller is `0xF6492E`.
The Normal and Child primary vtables are `0x5907788` and `0x5907C90`,
respectively. Both virtual slots `+0x178` point to `0x5B2140`, whose current
local-control predicate requires owner `+0xA0` to equal service `+0x58` and
owner flag `+0x94` to be nonzero. Both class gates remain supported, preserving
the Child-class correction introduced after the initial 3424 profile.

Three independent manager anchors resolve `0x71B1F68`. The controlled-owner
possession checks remain required; a matched global alone does not identify
the player.

All eight freshly extracted base/DLC `statusinfo` and `statusgroupinfo`
files have the same sizes and SHA-256 hashes as the
[3424 resource evidence](crimson-desert-3424-resources.md). Hp, Stamina and Mp
retain keys `1000000`, `1000026` and `1000027`. Historical indices 25 and 26
remain equipment rows and must not authorize resource writes.

The native status registry moves to `0x71B2A18`; resolver `0x3DCFC0` retains
one global dereference, count `+8`, cached-row table `+0x58` and row key `+8`.
The current word-reference consumer is `0x18E7670`; the key writer is
`0x171D389`. The actual regeneration count and stride witnesses move to
`0x18E7642` and `0x18E785A`. The damage path at `0x18E25D6` forwards the same
word status reference to `0x18E2910`, whose `0x18E293F` call resolves that
reference through the status table. Resource and damage checks continue to
use semantic keys, reject missing metadata rows, and respect the actual
regeneration-array count.

## Offline evidence retained

Ignored `artifacts/crimson-3446/` reports retain the exact mapping comparisons,
bounded current function bytes/disassembly, evaluated commit constants,
identity/field witnesses and resource hashes. The prologue fixtures use the
new RVAs and image size. Their disposable-process checks exercise hook
creation, redirection and restoration; captured game instructions and
trampolines are never executed. These checks do not imply live gameplay,
physical-controller or DLC-session validation.
