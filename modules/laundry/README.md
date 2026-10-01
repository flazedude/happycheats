# Laundry Store Simulator module

Imported from the user's `cheats-laundry` project. Original source remains unchanged. The original compatibility and implementation evidence is retained under `docs/`; those documents describe the earlier 0.2.0 standalone trainer.

Happy Cheats trainer **0.3.0** adds launcher controls through a local file bridge while keeping the original in-game hotkeys and build fingerprints. Supports only Steam app **3150440**, build **22778860**, game **7.1.4**. GameAssembly and metadata hashes are checked both before installation and when the mod starts.

The launcher uses the copied `LaundryTrainer.Setup.Core` for Steam discovery and transactional trainer/loader installation. `LaundryPackage` verifies the trainer manifest and pinned official MelonLoader 0.7.3 archive before extracting into an owned temporary directory. The package includes the pinned Microsoft-signed .NET 6.0.36 Desktop Runtime installer, used only when installation is requested and the runtime is missing. Removal moves only the trainer DLL into a dated backup; the loader and user data remain installed.

Build and verification from the repository root:

```powershell
.\scripts\Build-Laundry.ps1
dotnet run --project .\modules\laundry\tests\LaundryTrainer.Tests -c Release
dotnet run --project .\modules\laundry\tests\LaundryTrainer.Setup.Tests -c Release
dotnet run --project .\modules\laundry\tests\LaundryBridge.Tests -c Release
```

The packaging script first reuses verified artifacts from `payload/laundry` or the original sibling project. If the loader/runtime are absent it downloads their pinned official versions and verifies them. License notices and the Il2CppInterop source archive must be available from the original package or preserved in `payload/laundry/notices`. No proprietary game files are copied or redistributed. The launcher must ship the complete `payload/laundry` directory.

## Launcher transport

The mod advertises `UserData/HappyCheats/laundry/status.json` under the game directory. Commands are atomically published as `commands/<guid>.json`; responses appear as `responses/<guid>.json`. The launcher requires a fresh heartbeat and verifies the running PID belongs to the selected game executable before sending. Every command contains the current unpredictable session ID and a five-second expiry. The session rotates on scene changes. Commands are claimed before application, processed only from `MelonMod.OnUpdate`, and never replayed automatically after an error or restart. Repeated IDs are rejected for the lifetime of the session, even after the response was removed. Timeouts report an unknown outcome instead of retrying a progression change.

Actions are `cash` (10,000), `xp` (100), `shop` (one promotion), `wash`, `dry`, and `disable_all`. Launcher adapter actions `dry_slower` and `dry_faster` set the dryer factor from 1× through 20×. Washing and drying remain independent and turn off on scene changes. Multiplier preferences persist; enabled state does not persist. `disable_all` turns off both accelerators; earlier currency, XP, or rank changes remain subject to the game's normal save behavior.

## Validation for this integration

- Trainer Release build: zero warnings/errors.
- Existing gameplay binding/Harmony suite: 15 tests, 108 assertions passed.
- Existing installer suite: 24 tests passed against temporary fixtures.
- New launcher transport/API suite: 18 checks passed, including main-thread callback execution, duplicate handling after success/failure, expiration, stale and rotated sessions, malformed/oversized messages, removal without a progress callback, and preserved backups.
- Official loader/runtime hashes verified; runtime Authenticode signature verified.

No game installation, game launch, trainer attachment, or live gameplay mutation was performed for this integration. Live launcher controls, dryer timing, shop promotions, and save/reload behavior still need an in-game check. Historical live evidence in `docs/validation.md` applies only to the earlier features explicitly named there.
