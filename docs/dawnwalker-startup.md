# Dawnwalker startup investigation — 26 September 2026

The first 0.1.1 candidate still failed its authorized startup test. The trainer-disabled UE4SS run also failed, so removing repeated player scans was insufficient. The reduced-hook configuration below then passed a live title-screen launch with the complete HappyCheats trainer enabled.

## Exact target and local evidence

- Steam build: **25232147**, Dawnwalker CL **258504**, Unreal Engine **5.5.4**.
- `Dawnwalker.exe` SHA-256: `CB9B7D7BD88A6754C0A9C08318AA64D5013DDFD92D5BADCAE84E1B4EA980DCFC`.
- Dependency archive: `UE4SS_v3.0.1-1133-gb4cefa18.zip`, SHA-256 `89B7EED47C37D6FF6EAA144A41311A75098279A3454777F4EDD2446CEA1EA7A8`. Its DLL reports embedded commit **2bfa839f** in the runtime log. Do not equate the archive suffix with the embedded source identity.
- `artifacts/dawnwalker/startup-0.1.1-attempt1/UE4SS.log` and `startup-0.1.1-loader-only/UE4SS.log` record separate trainer-enabled and loader-only failures. The loader-only log contains no started HappyCheats Lua mod.
- Both logs resolve `ProcessLocalScriptFunction` to runtime address `0x7FF7F2344B40`, with executable base `0x7FF7EEA40000`, giving RVA **0x3904B40**. Local executable disassembly identifies this target as the security-cookie check routine: compare RCX against the cookie, rotate/test its high bits, return on success, or jump to RVA `0x39050AC` and execute `int 29h` with failure code 2. The FName constructor and ProcessEvent epilogues call this check at RVAs `0x103CC57` and `0x27F2752`. Hooking it as `ProcessLocalScriptFunction` therefore uses the wrong native target. This is concrete evidence for disabling that hook; it does not prove that every other default hook is correct.
- The captured hang and decoded stacks are retained as `artifacts/dawnwalker/startup-hang-33852.dmp`, `.json`, and `.unwound.json`. A thread waiting in loader/FName-related code is a symptom; by itself it does not establish a bad FName constructor signature.

## Primary-source compatibility reports

The [DawnWALKING author's technical notes](https://www.nexusmods.com/thebloodofdawnwalker/mods/65) prescribe an engine 5.5 override and only the EngineTick and UObjectProcessEvent configurable hooks. The package uses upstream experimental 1111, commit 97b7e501. This is evidence for the configuration approach, not validation of HappyCheats' newer DLL.

[Grimpil's loader support post](https://www.nexusmods.com/thebloodofdawnwalker/mods/55?tab=posts) attributes their startup crashes to incorrect ProcessInternal and ProcessLocalScriptFunction matches. The author reports that the reduced hook set retains native `/Script/` calls, properties, and game-thread execution while disabling Blueprint `/Game/` RegisterHook functionality. Their report does not identify our exact Steam build.

The [Dawnwalker Cheat Menu author's tested configuration](https://www.nexusmods.com/thebloodofdawnwalker/mods/119) does identify the exact executable hash and build above. It uses upstream 1111, commit `97b7e501c19d8b2b7c662feee73aaa0dc1f0a4d1`, with game-specific signatures. The author separately reproduced a LoadMap post-callback access violation with the menu disabled and uses `HookLoadMap = 0`. This supports leaving that unneeded hook disabled; it does not validate our different DLL or supply a replacement signature.

The [Vercadi RC5 description](https://www.nexusmods.com/thebloodofdawnwalker/mods/18) retrieved during this investigation targets build 25129649. Its published function addresses must not be transplanted into build 25232147. No third-party signature or custom DLL was copied from these reports.

## Validated startup configuration and trainer requirements

```ini
[EngineVersionOverride]
MajorVersion = 5
MinorVersion = 5

[Hooks]
HookProcessInternal = 0
HookProcessLocalScriptFunction = 0
HookInitGameState = 0
HookLoadMap = 0
HookCallFunctionByNameWithArguments = 0
HookBeginPlay = 0
HookEndPlay = 0
HookLocalPlayerExec = 0
HookAActorTick = 0
HookEngineTick = 1
HookGameViewportClientTick = 0
HookUObjectProcessEvent = 1
HookProcessConsoleExec = 0
HookUStructLink = 0
```

HappyCheats uses the EngineTick game-thread timer and native reflected calls through ProcessEvent. It does not register Blueprint script, LoadMap, BeginPlay, or EndPlay callbacks. `NotifyOnNewObject` uses UE4SS's separate construction notification path, which is not one of these configurable `Hook*` switches. The [embedded-commit LuaMod source](https://github.com/UE4SS-RE/RE-UE4SS/blob/2bfa839f/UE4SS/src/Mod/LuaMod.cpp) registers the StaticConstructObject callback separately; the [matching settings source](https://github.com/UE4SS-RE/RE-UE4SS/blob/2bfa839f/assets/UE4SS-settings.ini) lists the configurable switches.

The trainer's constructor notification only retains the wrapper. The timer later validates ownership, component readiness, and liveness before reads or requested changes. The reduced profile deliberately does not promise compatibility with unrelated Blueprint mods. Do not re-enable hooks to support them without validating their targets on this exact executable.

## Live result and limits

On 26 September 2026 at approximately **21:46 Asia/Shanghai**, the title screen was visually confirmed for process **15564** with the complete trainer enabled and the reduced hook profile. The log recorded constructor notification registration at **21:44:48.918** and the first game-thread poll at **21:44:51.825**. A fresh status heartbeat reported that it was waiting for a living player; all feature toggles were false. No save was entered and no cheat was activated. The game was left at the title screen for the user. The successful log, status and settings are archived in `artifacts/dawnwalker/startup-0.1.1-success/`.

This validates startup and trainer polling for the tested installation. Several unused hooks were disabled together, so the successful launch does not isolate the contribution of each hook. The incorrect ProcessLocalScriptFunction target is independently established by disassembly. The run does not validate health, stamina, blood, save loading, or long gameplay sessions.
