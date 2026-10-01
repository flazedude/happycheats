# GTA V Enhanced Story Mode payload

Trainer 0.2.0 provides seven controls and an F11 menu. It is experimental:
**native startup and gameplay are unverified**, and GTA was kept closed during
this update. GTA Online, including invite-only sessions, is unsupported.

## Runtime targets

- **Enhanced 1.0.1158.16 / Steam public build 25261616** uses the included limited
  GPL-2.0 Story Mode runtime. The client requires the reviewed Steam executable
  hash; the runtime requires the exact loaded build string, unique patterns,
  validated globals, executable memory and function boundaries. Missing or
  ambiguous interfaces leave the controls inactive. It does not require or
  import ScriptHookV.dll. Setup obtains the verified author's ASI loader if
  missing.
- **Enhanced 1.0.1158.13** uses the external Alexander Blade ScriptHookV runtime.
  Setup obtains the pinned hook and loader directly from the author when needed,
  and backs up the recognized previous hook before replacement. The proprietary
  runtime is not redistributed. Its public SDK is not its implementation source.

Steam public build 25261616 was checked on 27 September 2026. Release identity is
not gameplay verification. The `.16` runtime derives only the required Enhanced
interfaces and native mappings from the source identified in
[native/PROVENANCE.md](native/PROVENANCE.md); it does not bypass the official
hook's version gate. Unknown loaders are preserved and block setup. `.16` setup
preserves existing ScriptHookV.dll and other mods without evaluating or changing
them. On this PC the older 3788.0/1013.34 hook, NativeTrainer.asi and TrainerV.asi
remain present; those ASIs can independently load that hook and prevent startup.
This known conflict remains untested because the game was kept closed.

## Controls and protocol

F1 invincibility, F2 unlimited ammo/no reload, F3 never wanted, F4 health/armor,
F6 vehicle repair, F7 noon, F8 clear weather, F10 disable toggles, F11 menu.
Up/Down and Enter navigate; F11, Backspace or Escape closes the menu. Shortcuts
require foreground focus and a living, controllable Story Mode character.
Pause, loading, death and character changes clear toggles when an eligible script
callback is available. Turning toggles off does not undo immediate actions.

Invincibility restores the captured player flag. Ammo cleanup addresses only the
original entity with its original model. The native clip flag has no getter, so
another trainer's simultaneous changes cannot be distinguished.

The first observed Online session locks the runtime until process restart; no
subsequent commands or cleanup natives execute. The `.13` ScriptHookV dependency
also applies its own multiplayer restriction. This is not an Online/private-server
trainer, and it changes no anti-cheat, launcher or save settings.

The bridge is `HappyCheatsGtaV/status.json` and `command.json`. Schema 1 and trainer
version 0.2.0 apply to both backends. Integer UNIX heartbeat time, process identity,
32-hex session/request IDs, freshness, availability and acknowledgement are
required. Requests expire after eight seconds or when more than two seconds in
the future. Only toggles have a boolean `enabled` field. Input is bounded and
linked paths are rejected. Source-script interruption renews the session before
accepting commands again; failed initialization publishes an inactive diagnostic.

## Build, source and validation

Use `pwsh -File scripts/Build-GtaV.ps1` and `pwsh -File scripts/Test-GtaV.ps1` with
a Windows x64 GCC/MinGW toolchain. No proprietary SDK or prebuilt import library
is needed. [Native build instructions](native/README.md) also describe rebuilding
the standalone source archive.

The package contains `HappyCheats.GtaV.asi`,
`HappyCheatsGtaV/ThirdPartyNotices.txt` and `HappyCheatsGtaV/Source.zip`, each hashed
in the manifest. The combined native module is GPL-2.0; the archive includes its
complete corresponding source, tests, build scripts and vendor source/licenses.
JSON for Modern C++ is MIT licensed; MinHook is BSD-2-Clause with retained HDE
notices. No proprietary game or ScriptHookV runtime/SDK files are package inputs.
Existing two-file trainer receipts upgrade to include the source archive.

Offline fixtures exercise real compiled binaries in mock/synthetic hosts, script
context restoration, native dispatch, Online lockout and file acknowledgements.
Client fixtures cover dependency transactions, executable identity, ownership,
rollback and bilingual UI states. Tests run no game and must not be described as
actual startup or gameplay verification. See [user setup](../../docs/gta-v-enhanced.md).
