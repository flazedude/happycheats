# HappyCheats GTA V Enhanced native source

Trainer 0.2.0 is an experimental Story Mode ASI with seven controls and an in-game
menu. GTA Online, including invite-only sessions, is disabled. Actual native
startup and gameplay were not tested: the user requested that GTA remain closed.

## Build targets

- Enhanced 1.0.1158.16 uses the included source runtime. The companion admits the
  inspected Steam executable SHA-256
  `69DA07FF67D05E9DED11289E597E8B8DC5855B0A429C085F37D148DC267CB2C5`.
  Steam public build 25261616 was checked on 27 September 2026. The ASI validates
  the host version and the loaded image's build string, unique patterns, globals,
  executable memory, and unwind function boundaries before installing its one
  script-scheduler hook. Missing or ambiguous interfaces leave controls inactive.
- Enhanced 1.0.1158.13 retains the external Alexander Blade ScriptHookV path.
  ScriptHookV is loaded from beside the ASI by a worker after DllMain returns.
  The proprietary runtime is not part of this source or binary distribution.

The 1158.16 backend does not import, replace or require ScriptHookV.dll. Other ASIs
can still load their own ScriptHook dependency; an incompatible third-party mod
can prevent the game from starting independently of this trainer.

## Source and licenses

This combined native module is distributed under GNU GPL version 2. See COPYING
and PROVENANCE.md for the upstream Enhanced interface/hash source and exact commit.
The original trainer, modifications, limited runtime, tests, and build scripts are
included in the accompanying `HappyCheatsGtaV/Source.zip`. JSON for Modern C++ is
MIT licensed. MinHook is BSD-2-Clause with retained HDE notices. All corresponding
vendor source and notices are in `vendor/`. No game code, proprietary SDK/runtime,
or unrelated upstream menu functionality is included.

Extract Source.zip, install a Windows x64 GCC/MinGW toolchain and PowerShell 7,
then run from this directory:

```powershell
./Compile.ps1 -OutputPath ./build/HappyCheats.GtaV.asi
```

The normal repository build also runs native fixtures and packages this source
with the ASI. The build requires no proprietary SDK or prebuilt import library.

## Runtime behavior

F1 invincibility, F2 unlimited ammo, F3 never wanted, F4 health/armor, F6 vehicle
repair, F7 noon, F8 clear weather, F10 disable toggles, F11 menu. Arrow keys and
Enter operate the menu. Shortcuts require foreground focus and a living,
controllable Story Mode character. Pause, loading, death and character changes
clear toggles. Immediate actions are not undone by disabling toggles.

The source backend executes only in a validated Story Mode script context on the
game scheduler thread and restores its prior TLS context afterward. All natives
are gated by the current session flags; the existing four native Online checks
provide an additional guard. A detected Online session permanently disables the
backend until process restart. There is no network modification or anti-cheat
bypass. Launch arguments, launcher settings and saves are not changed by setup.

Invincibility restores the captured player flag. Ammo cleanup only addresses the
original entity with a matching model. The game API has no getter for its clip
flag, so simultaneous ammo changes from another trainer cannot be distinguished.
Absent script context prevents cleanup until an eligible script callback resumes.

`HappyCheatsGtaV/status.json` and `command.json` provide the same bounded,
acknowledged interface for both backends. Commands need the current process and
session, explicit toggle state and an eight-second expiry. A source-script
interruption renews the session before accepting commands again. An initialization
failure publishes an inactive diagnostic; it does not enable controls.

## Validation limits

Fixtures exercise the real compiled ASI with a mock ScriptHook, real MinHook
trampolines in a synthetic PE, native arguments, script/TLS restoration, protocol
acknowledgements, cleanup, invalid-pattern rejection and permanent Online lockout.
These establish offline behavior, not actual game compatibility. The protected
installed EXE cannot supply initialized game addresses through raw-file scanning.
