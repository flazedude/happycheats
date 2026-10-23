# Dawnwalker patch 3 compatibility

Trainer **0.3.4**, included in HappyCheats **0.3.47**, targets the installed Steam
app **3751260**, build **25915545**, inspected on 24 October 2026.

| Identity | Value |
| --- | --- |
| Executable | `Dawnwalker/Binaries/Win64/Dawnwalker.exe` |
| Version | `dw1-pc-265636-shipping-patch3-all-CL-265636` |
| Size | 179,662,712 bytes |
| SHA-256 | `E6222B815B1C376F2C85A2189120C5A9B5CD30AC5688A34B61A2C18EFBC9B117` |
| Embedded engine | Unreal Engine 5.5.4 |

The former exact executable pin rejected this patch. The new pin follows
independent static checks of the installed executable, its generated reflection
descriptors and native implementations. It does not accept arbitrary future
builds. The client, package manifest and Lua heartbeat use trainer version 0.3.4,
so a previously installed 0.3.3 trainer must be updated.

## Compatibility evidence

- [Loader and resolver review](dawnwalker-patch3-loader.md): pinned dependency,
  engine override and required scanner results. Only the existing EngineTick and
  UObject ProcessEvent hooks remain enabled.
- [Resource, combat and inventory review](dawnwalker-patch3-bindings.md): native
  contracts, changed player layout, item-handle and AI visibility fields. The
  player ability-system property moved, but the trainer resolves it by name.
- [Movement and travel review](dawnwalker-patch3-movement.md): marker events,
  rotation, collision-result flags and native travel contracts.

The trainer retains its existing native reflected calls and all 14 controls.
The three custom fields remain ItemHandle index at offset 0, player AI-stub
visibility byte at 0x30 (owned mask 0x10), and HitResult flags at 0xAD (masks 1
and 2). The player AI-stub class size changed from 0x270 to 0x260; its visibility
byte remains verified at the same offset. No old player component/cache offsets
are copied into production code.

The existing bounded player discovery, 250 ms maintenance, one-second heartbeat,
explicit flight-timer cancellation and session/command checks remain. Manual and
automatic teleport still use the native fast-travel/loading sequence. The user
confirmed that sequence worked before this patch and requested it be retained.
Shared-menu Ctrl+Del, F6 and Menu+RB behavior is unchanged.

## Validation and installation

The six Lua fixture suites pass **3,086 assertions**, and the runtime-profile
test rejects **15 unsafe profiles**. They exercise native-call boundaries and
failure paths with mocks. The release also runs the repository's client,
installer, menu/controller and packaging suites. Distribution verification checks
the signed hosted feeds, matching archive bytes, complete standalone package and
LAN-to-GitHub fallback.

No game was launched, native game function executed, cheat enabled or save
modified during this compatibility inspection. A static scanner cannot prove
successful runtime detour installation, Blueprint behavior or actual gameplay.
Patch 3 startup, cheat effects, physical-controller behavior and performance
remain unverified. Earlier user reports apply to the previous game build.

Close Dawnwalker and choose **Install / update** in HappyCheats 0.3.47, then
launch normally. The client continues to require a fresh matching trainer
session before enabling commands.

The old read-only process helpers in `tools/InspectDawnwalker` remain explicitly
pinned to build 25232147. Their old RVAs must not be used on patch 3; their
executable-hash refusal is intentional. Local binary inspection artifacts are
kept outside source control and distributions.
