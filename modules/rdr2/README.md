# Red Dead Redemption 2 trainer

The HappyCheats RDR2 module provides an experimental **Story Mode** trainer through an original native ASI plugin. The client exposes player health, stamina, Dead Eye, ammunition, mounted-horse health and stamina, and a one-shot clear-current-wanted action. Official **Red Dead Online is not implemented** by this payload.

## Runtime requirements and limits

- PC RDR2 executable version `1.0.1491.50`; other versions require a separate compatibility check. A fresh SteamCMD public-branch query on 27 September 2026 confirmed build `13773296`, matching the installed `.50` executable.
- **Install / update** downloads the verified [Script Hook RDR2 runtime and original ASI loader directly from its author](https://www.dev-c.com/rdr2/scripthookrdr2/) when missing. First setup needs internet; an installed, verified runtime is reused offline. The original loader is `dinput8.dll`; the scripting runtime is `ScriptHookRDR2.dll`, next to `RDR2.exe`.
- The SDK terms prohibit redistributing `ScriptHookRDR2.dll` with a plugin. HappyCheats packages its own plugin and downloader, without third-party runtime binaries. The official SDK includes headers, an import library and sample plugin source, but does not publish the runtime implementation. Removing the trainer retains these shared runtime files and the author's readme.
- Script Hook's author documents that the runtime does not support Online and terminates the game when entering multiplayer. The HappyCheats plugin also checks the two network-session natives before player/horse access and refuses Online actions.
- Health is repeatedly restored, not made invulnerable. Instant lethal damage can still kill the player or horse. The wanted action does not promise to erase recorded regional bounties.
- Controls require a living Story Mode player; horse controls also require a living current mount. Effects reset on player/session changes. Keep the game closed while installing, updating, or removing the plugin; live ASI unloading is unsupported.
- The package has not been verified in gameplay. Native fixture tests and executable/version inspection are offline checks, not proof that a native works in every mission, cutscene, death transition, or mount state.

## Local inspection, 27 September 2026

The current official runtime is `1.0.1491.17`. Its readme describes automatic address detection for game versions not yet listed, until the script system changes. This supports trying the current `.50` game without downgrading; only real game testing can establish successful startup and effects. The pinned runtime ZIP is downloaded over HTTPS with the author's page as Referer and checked before any game writes:

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| `ScriptHookRDR2_1.0.1491.17.zip` | 294614 | `A3BE69DCD33E6CFFE316D7C17EE5C4F7FCEDF0C7B7FA8CF68177398E6594C39F` |
| `bin/ScriptHookRDR2.dll` | 185344 | `3AC29FBE8C92B664E358F7D4F0AF2EC9F1CA674885975087EF76BD98BF972A4C` |
| `bin/dinput8.dll` | 131072 | `956FB3765572D00F6C08BCAE11E9856A00A68107464A87B6CCC6C1FFED46B88A` |
| `readme.txt` | 3922 | `9CD1476DB98143936BA632ED9A25F076EF9953197BEE32C23BD06A5AA4EBAD58` |

The sample `NativeTrainer.asi` is not installed. Unknown/modified existing runtime files block installation rather than being replaced. Runtime and trainer setup share one rollback transaction, with the loader activated last; removal retains the shared runtime. The download's SDK/sample source is not a replacement for ScriptHook's closed runtime implementation.

The inspected Steam installation was `D:\Steam\steamapps\common\Red Dead Redemption 2`. Its app ID is `1174180`, Steam build ID is `13773296`, and executable file/product version is `1.0.1491.50`.

The inspected `RDR2.exe` SHA-256 was:

```text
B56C9548F670654A9B73BF25DEF3CD73AF12E269F6E47DBA28A34079ADAF465E
```

This digest records the inspected file; it does not imply that all distribution variants share the same digest. The native profile uses an executable-version guard.

The game directory contained an existing `Rampage.asi`, which must be preserved. It did not contain `ScriptHookRDR2.dll`, `dinput8.dll`, or `version.dll` during this inspection. No game was launched, injected, installed into, or used to change a save for these checks.

WinLibs MinGW-w64 `g++` and CMake are available locally. MSVC, Visual Studio, and `clang-cl` were not found in the inspected standard locations or command path. The original HappyCheats plugin can be built with MinGW and the ordinary Windows SDK headers supplied by that toolchain.

## Native/API provenance

Function hashes and signatures were checked against [alloc8or's RDR3 native database at commit `e8558ab99c7e990edd7200a348379d6880c14f96`](https://github.com/alloc8or/rdr3-nativedb-data/blob/e8558ab99c7e990edd7200a348379d6880c14f96/natives.json), dated 19 September 2026. RDR2 native IDs must not be substituted with similarly named GTA V IDs.

| Purpose | Native/hash | Signature |
| --- | --- | --- |
| Player handle | `PLAYER_ID` / `0x217E9DC48139933D` | `Player()` |
| Player ped | `PLAYER_PED_ID` / `0x096275889B8E0EE0` | `Ped()` |
| Player active | `IS_PLAYER_PLAYING` / `0xBFFB35986CAAE58C` | `BOOL(Player)` |
| Entity exists | `DOES_ENTITY_EXIST` / `0xD42BD6EB2E0F1677` | `BOOL(Entity)` |
| Entity dead | `IS_ENTITY_DEAD` / `0x7D5B1F88E7504BBA` | `BOOL(Entity)` |
| Ped dead/dying | `IS_PED_DEAD_OR_DYING` / `0x3317DEDB88C95038` | `BOOL(Ped, BOOL)` |
| Session started | `NETWORK_IS_SESSION_STARTED` / `0x9DE624D2FC4B603F` | `BOOL()` |
| Network game | `NETWORK_IS_GAME_IN_PROGRESS` / `0x10FAB35428CCC9D7` | `BOOL()` |
| Maximum health | `GET_ENTITY_MAX_HEALTH` / `0x15D757606D170C3C` | `int(Entity, BOOL)` |
| Set health | `SET_ENTITY_HEALTH` / `0xAC2767ED8BDFAB15` | `void(Entity, int, Entity)` |
| Restore player stamina | `RESTORE_PLAYER_STAMINA` / `0xC41F4B6E23FE6A4A` | `void(Player, float)` |
| Restore ped stamina | `_RESTORE_PED_STAMINA` / `0x675680D089BFA21F` | `void(Ped, float)` |
| Maximum Dead Eye | `_GET_PLAYER_MAX_DEAD_EYE` / `0x592F58BC4D2A2CF3` | `float(Player, Any)` |
| Restore Dead Eye amount | `_SPECIAL_ABILITY_RESTORE_BY_AMOUNT` / `0x51345AE20F22C261` | `void(Player, float, int, int, int)` |
| Restore special ability | `_SPECIAL_ABILITY_START_RESTORE` / `0x1D77B47AFA584E90` | `void(Player, int, BOOL)` |
| Set core | `_SET_ATTRIBUTE_CORE_VALUE` / `0xC6258F41D86676E0` | `void(Ped, int, int)` |
| Infinite clip | `_SET_PED_INFINITE_AMMO_CLIP` / `0xFBAA1E06B6BCA741` | `void(Ped, BOOL)` |
| Mounted | `IS_PED_ON_MOUNT` / `0x460BC76A0E10655E` | `BOOL(Ped)` |
| Current mount | `GET_MOUNT` / `0xE7E11B8DCBED1058` | `Ped(Ped)` |
| Clear wanted score | `CLEAR_WANTED_SCORE` / `0x062B4A4A3396351D` | `void(Player)` |
| Clear pursuit | `_SET_BOUNTY_HUNTER_PURSUIT_CLEARED` / `0x55F37F5F3F2475E1` | `void()` |

`CLEAR_PLAYER_WANTED_LEVEL` is documented as an empty function in RDR2 and must not be used to implement the wanted control. Core indices are health `0`, stamina `1`, and Dead Eye `2`.

The implementation restores Dead Eye by the reported maximum with trailing arguments `(0, 0, 1)`, the combination documented by the native database. The older special-ability-start-restore call below is additional SDK reference evidence.

The [original Script Hook RDR2 SDK](https://www.dev-c.com/files/ScriptHookRDR2_SDK_1.0.1207.73.zip) was inspected in memory from the author's site. Its native-trainer sample uses `RESTORE_PLAYER_STAMINA(player, 100.0)`, the current ped-stamina native with `100.0`, and the current special-ability-start-restore native with `(player, -1, FALSE)`. Its headers declare:

```cpp
void nativeInit(uint64_t hash);
void nativePush64(uint64_t value);
uint64_t* nativeCall();
void scriptRegister(HMODULE module, void (*entry)());
void scriptUnregister(HMODULE module);
void scriptWait(DWORD milliseconds);
```

The installed `Rampage.asi` import table independently confirmed the corresponding x64 export names: `?nativeInit@@YAX_K@Z`, `?nativePush64@@YAX_K@Z`, `?nativeCall@@YAPEA_KXZ`, `?scriptRegister@@YAXPEAUHINSTANCE__@@P6AXXZ@Z`, `?scriptUnregister@@YAXPEAUHINSTANCE__@@@Z`, and `?scriptWait@@YAXK@Z`. Native arguments occupy 64-bit slots; float arguments preserve their 32-bit representation. Native calls run on the registered game script fiber.

## Official Online research and remaining work

[YimMenu/HorseMenu (Terminus)](https://github.com/YimMenu/HorseMenu) describes a separate direct-injection backend for both RDR2 and official Red Dead Online. Its existence establishes that Script Hook's limitation is not proof that every independent Online implementation is impossible. Its source was last pushed on 31 January 2025 at inspection time, and the published nightly asset was dated 6 November 2024; repository metadata updated in 2026 is not a newer source release.

That repository's [CI](https://github.com/YimMenu/HorseMenu/blob/master/.github/workflows/ci.yml) uses MSVC x64 and Ninja. No `LICENSE` or `COPYING` file appeared in its recursive source tree and GitHub reported no license, so permission to incorporate or redistribute that implementation was not established. No external backend source or binary was copied into HappyCheats.

A minimal independent backend would still need a validated native-handler resolver, native call-context ABI, game-script-thread scheduling, and the active script context. Read-only pattern scans of the inspected on-disk `RDR2.exe`, using the public backend's discovery patterns, produced **zero matches for each of five essential targets**: native-handler lookup, vector-result fixup, script-thread execution, current script context, and network-session state. Runtime unpacking may explain these results; the scan does not establish either compatibility or incompatibility of the loaded game image.

No loaded-game addresses, Online ABI, or Online gameplay behavior were verified. Consequently this release does not ship or advertise a working Online backend. Completing that part requires further implementation and authorized runtime verification; it cannot be achieved by relabeling the Story Mode ASI or removing its session guard.
