# Chinese Parents trainer

The adapter supports the installed **Steam app 736190, build 24710774**, using the game's managed player setters. It does not patch the game assembly, edit a save file, use fixed memory addresses, or guess values. The current game has C# gameplay logic; a Lua hook is unnecessary.

## Status and build

The plugin builds successfully against this game's Unity `netstandard2.1` runtime. Static inspection verifies all bound properties, setter visibility, UI callbacks, singleton access and readiness members. Live gameplay validation is still required; compilation and inspection alone do not prove the game loads the plugin or every action works during play.

```powershell
pwsh -File scripts/Build-ChineseParents.ps1 -GameDirectory 'E:\Games\SteamLibrary\steamapps\common\ChineseParent'
dotnet run --project tools/InspectChineseParents -- 'E:\Games\SteamLibrary\steamapps\common\ChineseParent'
dotnet run --project tools/InspectChineseParents/Tests/TrainerChecks.csproj
```

The build script only reads the game installation and writes the workspace payload. `payload/chinese-parents/package.json` records the supported assembly hash and every installer-owned file hash. No proprietary game DLL is copied into the payload.

## Inspected game contract

`Assembly-CSharp.dll` SHA-256:

`0262f1b24b148d99e8cba54a79b6b7299622c46be4bbc000a500d7519bf3bf3a`

`NewPlayerDataMgr : SingleTon<NewPlayerDataMgr>` owns the values. The plugin reads the existing private static `m_instance` field rather than calling a singleton getter, which would create a manager at the main menu. Readiness additionally requires `NewRecordMgr.CurrentRecord`, `CurrentRoleCoreInfo` and the player's `CurTable`. Continuous cheats reset when a character is unloaded or a different record is selected.

| Action | Inspected API | Behavior |
| --- | --- | --- |
| `add_money` | `Money` | Add 1,000 |
| `refill_energy` | `Potentiality`, `Potential_max` | Refill action points to this round's cap |
| `add_knowledge` | `Wisdom` | Add 1,000 learning points; game caps at 99,999 |
| `clear_stress` | `Pressure` | Set to zero |
| `clear_shadow` | `Shadow` | Set psychological shadow to zero |
| `add_attributes` | `Iq`, `Eq`, `Imagination`, `Memory`, `Stamination`, `Charm` | Add 100 to each |
| `add_face` | `Face` | Add 100 |
| `unlimited_energy` | `Potentiality` | Refill every Unity frame while enabled |
| `no_stress` | `Pressure` | Clear every Unity frame while enabled |
| `disable_all` | Plugin state | Disable both continuous cheats, including at the main menu |

All value operations run in `MonoBehaviour.Update` on the Unity main thread and call the game's setters. The inspected setters clamp values and invoke `OnMainInfoValueChange`, allowing game UI listeners to refresh. Additive edits use a conservative 1,000,000 ceiling and never lower a value already above it. The game's `Money` setter also contains its normal achievement logic; using game setters retains these side effects. Normal in-game saving persists changed character values. Turning a continuous cheat off stops future changes; it does not reverse previous edits.

| Hotkey | Action |
| --- | --- |
| F1 | Add 1,000 money |
| F2 | Refill action points (行动力) |
| F3 | Add 1,000 learning points (学习点数 / 悟性) |
| F4 | Clear stress |
| F5 | Clear psychological shadow |
| F6 | Add 100 to all six attributes |
| F7 | Add 100 face |
| F8 | Toggle continuous action point refill |
| F9 | Toggle continuous stress removal |
| F10 | Disable both continuous cheats |

Hotkeys only respond while the game window is focused. The plugin skips setter calls when the desired value is already present.

### Learning currency evidence

Read-only inspection of the installed assembly confirms the learning currency is `Wisdom`, rather than a separate unimplemented property. `NewSkillMgr.StudySkill(TableSkill)` reads `TableSkill.RealNeedWisdom`, applies the game's cost modifiers, then at IL `009d–00ab` reads `NewPlayerDataMgr.Wisdom`, subtracts the adjusted skill cost and calls `set_Wisdom`. The launcher labels this resource learning points (学习点数 / 悟性); the protocol retains `add_knowledge` and the `knowledge` value key for compatibility.

`StringUtil.GetMainInfoName(EMainInfoType)` maps `Wisdom` (enum 6) to localization key `CheatKnowl.`, `Potentiality` (enum 7) to `Potentiality`, and `Money` (enum 8) to `Money`. These are directly observed code mappings. The Chinese labels above describe the requested resources; they are not claimed as a transcription from a running game screen.

The action-point binding is also confirmed by the expenditure path: `naodong.Dig()` reads `TableNaodong.Need_potentiality`, applies modifiers, checks available `Potentiality` at IL `0110–011c`, and subtracts the adjusted cost through `set_Potentiality` at IL `0163–0171`. Refill therefore changes the currency used by brain-map digging; it is distinct from the `Wisdom` spent when learning a skill.

## Local protocol

IPC lives inside this installation at `BepInEx/HappyCheats/chinese-parents/`.

The plugin writes `status.json` twice per second with `schema_version`, `plugin_version`, `session_id` (new GUID at startup and whenever a character is loaded, unloaded or changed), `process_id`, `ready`, `message`, `values`, `enabled`, `last_command`, `updated_utc` (Unix seconds) and `assembly_sha256`. A client must match the live process and fresh heartbeat before offering actions. Value keys are `money`, `knowledge`, `energy`, `energy_max`, `stress`, `shadow`, `face`, `iq`, `eq`, `imagination`, `memory`, `constitution`, `charm` and `round`. Values are invariant-culture strings; `enabled` contains booleans for the two toggle action IDs.

Write command JSON into a temporary file and atomically rename it to `commands/<request_id>.json`. `request_id` is a GUID in `N` or `D` format; its exact text must match the filename. Example:

```json
{
  "schema_version": 1,
  "session_id": "<current status session_id>",
  "request_id": "<new GUID>",
  "action": "unlimited_energy",
  "enabled": true,
  "created_utc": 1789300000
}
```

`enabled` is required only for toggles. Commands older than 30 seconds or more than 5 seconds in the future are rejected. Unsupported actions, mismatched sessions, unsupported game builds and requests without a loaded character are rejected. `disable_all` is allowed without a character. The plugin limits commands to 8 KiB, processes at most eight per poll, and never replays an acknowledged request. `responses/<request_id>.json` is the acknowledgement, with `schema_version`, `session_id`, `request_id`, `success`, `message`, `updated_utc`. `last_command` alone is not an acknowledgement of success.

Status and response files are written by atomic replace. Session IDs and process identity avoid sending an old command to another game run. This is a local desktop protocol, not a network service or a security boundary against programs running as the same user.

`startup.json` preserves a process's binding initialization result (`bindings_valid` and `initialization_error`), assembly hash and process ID even if the game immediately exits. Those diagnostic fields also remain in `status.json` after shutdown. Successful initialization proves the loader and reflection contract loaded; it does not prove gameplay changes work.

## Pinned loader

The payload uses the [official BepInEx 5.4.23.5 release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5), Windows x86. The downloaded archive is verified against the upstream GitHub asset digest:

`37651c79e40d6f909572a4f461ac25350bb3ef8fe7fbd29f1aa8791a33b84c82`

`Prepare-BepInEx.ps1` records its URL and provenance. The loader uses its original `winhttp.dll` and `doorstop_config.ini`; the game assemblies remain original. The launcher handles installation, conflict detection, ownership records and uninstall. Future Steam builds are blocked by the exact assembly hash until the read-only inspection and live checks are repeated.
