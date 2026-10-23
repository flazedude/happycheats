# Dawnwalker Patch 3 loader audit — 24 October 2026

The existing pinned UE4SS loader resolves all required initial-scan targets in Steam build **25915545**. Keep the **5.5 engine override** and the existing minimal hook profile. This is an offline compatibility result, not a successful startup or gameplay test.

## Inspected files

- Installed executable: `Dawnwalker/Binaries/Win64/Dawnwalker.exe`, 179,662,712 bytes.
- Version: `dw1-pc-265636-shipping-patch3-all-CL-265636`.
- SHA-256: `E6222B815B1C376F2C85A2189120C5A9B5CD30AC5688A34B61A2C18EFBC9B117`.
- The executable contains the Unreal version string `5.5.4` at RVA `0x9C1301C`.
- Installed and packaged `UE4SS.dll` match SHA-256 `03C9D35F6A55666839565358EDEA09F5F11C7929440FE5C809E457D6D527C71F`. The retained loader log identifies embedded commit `2bfa839f`; the archive's `b4cefa18` suffix is not its embedded source identity.
- No game process was running during this audit. The installed loader log was last written on 27 September and describes the previous executable, so it is not Patch 3 startup evidence.

## Exact scanner and results

The [UE4SS `2bfa839f` dependency entry](https://github.com/UE4SS-RE/RE-UE4SS/tree/2bfa839f/deps/first/patternsleuth) pins [patternsleuth `1d90b02c7610b595f940af04ce97e0f6feb06923`](https://github.com/trumank/patternsleuth/tree/1d90b02c7610b595f940af04ce97e0f6feb06923). Its unmodified CLI was built locally and run with `--path` against the installed PE file. It was not injected into or attached to a game process.

The file image base is `0x140000000`; the table subtracts that base from scanner results.

| Resolver | Patch 3 result |
| --- | --- |
| BuildConfiguration | Shipping |
| Stats | Off |
| GUObjectArray | RVA `0xA35E240` |
| GMalloc | RVA `0xA3CA128` |
| FNameToString | RVA `0x1052E04` |
| FNameCtorWchar | RVA `0x1058758` |
| StaticConstructObjectInternal | RVA `0x10D0C50` |
| GNatives | RVA `0xA2D93D0` |
| UGameEngineTick | RVA `0x14CA4FC` |
| FNamePool | RVA `0xA4AF2C0` |

Independent PE checks place the four function targets in `.text`, each at an exact exception/unwind function entry. Disassembly shows appropriate function bodies rather than the security-cookie helper. The four global targets reside in writable `.data`.

Three additional resolvers remain unresolved: `EngineVersion` and `FUObjectHashTablesGet` find no target; `ConsoleManagerSingleton` finds four candidates. The latter two also failed in the previous successful startup log. The embedded `5.5.4` string supports retaining the explicit major/minor override instead of depending on automatic version discovery. These results do not justify enabling optional console or hash-table features.

## Hook decision and limits

Retain only `HookEngineTick = 1` and `HookUObjectProcessEvent = 1`; leave every other configurable hook disabled. Object construction notifications use the loader's separate construction path. The trainer still requires that path and native reflected calls, so disabling all hooks is not a working substitute.

The new executable's security-cookie checker is at RVA `0x3AF99A0`, with failure branch `0x3AF9F0C`; the old build's address was `0x3904B40`. The previous loader failure involved this helper being mistaken for `ProcessLocalScriptFunction`. This audit does **not** establish the disabled hook's current scanner result. Preserve the disabled profile and do not transplant the old address.

The standalone scanner does not validate runtime `ProcessEvent` discovery, FName verification hooks, actual hook installation, constructor callbacks, or game-thread polling. A subsequently authorized startup check should confirm those stages, a fresh current-version heartbeat, all controls initially off, and player ownership/readiness after the user loads a save. Feature effects and long-session stability remain separate gameplay checks.

## Reproduction and local evidence

Build the pinned patternsleuth checkout with `cargo build --locked --release -p patternsleuth_cli`. Then run:

```powershell
patternsleuth.exe scan --path '<installed path>\Dawnwalker.exe' --summary `
  -r BuildConfiguration -r Stats -r GUObjectArray -r GMalloc `
  -r FNameToString -r FNameCtorWchar -r StaticConstructObjectInternal `
  -r GNatives -r UGameEngineTick -r FNamePool -r EngineVersion `
  -r FUObjectHashTablesGet -r ConsoleManagerSingleton
```

Local investigation files are under `artifacts/dawnwalker/update-20261024-loader/`: `Run-StaticScan.ps1` checks the executable hash before scanning; `pinned-scan.txt` contains raw results; `static-binary.json`, `resolved-targets.json`, and `security-cookie.json` record the independent PE/disassembly checks. These artifacts and downloaded scanner sources are not release payloads or source-control deliverables.
