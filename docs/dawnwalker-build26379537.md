# Dawnwalker Steam build 26379537

Independent package **1.0.5**, trainer **0.5.4**, adds the installed executable
from Steam build **26379537**. It retains the exact build 25915545 hash.
The client version and standalone EXE are unchanged.

| Identity | Value |
| --- | --- |
| Executable | `Dawnwalker/Binaries/Win64/Dawnwalker.exe` |
| Steam app / depot | 3751260 / 3751261 |
| Current depot manifest | 7741881678388162750 |
| Previous depot manifest | 412770848787440441 |
| File version | `dw1-pc-265636-shipping-patch3-all-CL-265636` |
| Size | 179,662,712 bytes |
| Current SHA-256 | `75006EE2B3D662E7AA697804A5129B04F336E40F76C2FE8D3A72AD107552A8C6` |
| Retained previous SHA-256 | `E6222B815B1C376F2C85A2189120C5A9B5CD30AC5688A34B61A2C18EFBC9B117` |

## Read-only compatibility evidence

On 3 November 2026, both Steam depot manifests were present in the local
`Steam/depotcache` directory. Their mapping/chunk records were decoded using
the [SteamKit manifest structure](https://github.com/SteamRE/SteamKit/blob/master/SteamKit2/SteamKit2/Types/DepotManifest.cs).
The current executable matches its complete manifest SHA-1 and every one of
its 174 chunk hashes. Its SHA-256 is independently pinned above; manifest
hashes are inspection evidence, not the client's trust mechanism.

Of 425 mappings, only `Dawnwalker.exe` and `installscript.vdf` changed. The
executable has the same size and chunk boundaries. **172 of 174 chunks match
the previous manifest**, including their hashes, offsets and sizes. Changed
file ranges are `[0, 1048576)` and `[179049632, 179662712)`. These include the
PE header/initial code and the final relocation/bootstrap/signature region;
this comparison does not claim that the whole code section is identical or
that only the signing certificate changed.

All file-backed `.rdata`, `.data`, `.pdata` and `.rsrc` bytes are in unchanged
chunks. The `.text` region after file offset 1,048,576 is unchanged. Mapping
the audit RVAs from the patch 3 loader, binding and movement reviews, resource
locks, damage and auto-parry documents into PE file offsets confirms **111
documented addresses**, each with 32 bytes checked, in unchanged chunks.
These include the native function/registration regions used by the trainer
and the prior loader's FName, construction and tick resolver targets.
The four loader globals (`GNatives`, `GUObjectArray`, `GMalloc`, `FNamePool`)
are in zero-initialized virtual data, not file-backed bytes; they are excluded
from the 111-address byte comparison. This is not a new scanner run or proof
of runtime global values.

The generated registration/parameter descriptors and previously audited
custom-field evidence therefore remain applicable to the new exact hash.
The trainer keeps all fifteen controls, readback/ownership safeguards, minimal
UE4SS hooks and Unreal 5.5 override. No native binding, resource, damage,
parry, inventory or teleport behavior is changed. The heartbeat version moves
to 0.5.4 with the rebuilt payload so the updated module requires its matching
installed script. Unknown executable hashes still fail compatibility checks.

Local reproduction evidence is retained in the publishing worktree under
`artifacts/dawnwalker/update-20261103/verify_manifests.py` and
`manifest-comparison.json`. Manifest SHA-256 values:

- Previous: `bac82976700a80e3d9eb2b9bb2401a15eee42e237781d54512cf9e8a5785dd49`.
- Current: `a80b8347a2a461b7157d915283f24601670303b6b27de932b46fa8e2b607ee83`.

Game executable bytes, depot manifests and extracted game assets are not
committed or distributed.

## Validation and use

The rebuilt payload passes all eight Lua suites (7,729 assertions/checks) and
the runtime-profile test rejects 15 unsafe hook configurations. Exact-hash
regressions accept both inspected executables and reject a one-character
hash mutation, the unchanged version string alone and empty input. The
publisher also runs the required repository offline release suites.

Close Dawnwalker and click **Update** beside Library Sort, then update its
package. Current clients download, verify, load and install the trainer in
one operation. Completion requires installation verification. The existing
shared Ctrl+Del, F6 and Menu held 350 ms + RB shortcuts remain unchanged.

No game was launched or closed, native function called, cheat enabled or save
changed for this update. Startup/hook installation, actual combat/resource/
teleport effects, physical-controller behavior and performance on build
26379537 remain unverified. The prior package's live damage attribute
readback does not establish gameplay effects on this newly installed build.
