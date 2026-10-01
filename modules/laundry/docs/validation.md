# Validation

Validated 2026-09-12 on the local Windows x64 Steam installation, build 22778860 / game 7.1.4.

The runtime and automated-test results below are historical **0.1.0** evidence. The current **0.2.0** package received static inspection and compilation only, as requested; its build record is at the end of this document.

## Passed

- Release build: zero warnings and zero errors.
- Core suite: **15/15 tests, 108 assertions**. Covers money/XP readiness and arguments, inherited generic singleton properties, incompatible signatures, pause/reset/completion timer behavior, real managed Harmony patches, fault handling, and repeated cleanup.
- PowerShell installer and inspector: parsed and checked in PowerShell 7 and Windows PowerShell 5.1. Read-only check/preview, staging-directory rejection, build-fingerprint rejection, verified archive extraction, and GitHub API failure fallback were exercised.
- Installed the official checksum-verified MelonLoader 0.7.3 and the Release trainer DLL into the detected game installation.
- Launched the game successfully. MelonLoader generated its IL2CPP assemblies, loaded Laundry Trainer 0.1.0, and the mod bound money, XP, and its washing patch without a trainer error.
- Visually observed the trainer panel during loading and gameplay.
- During the live session, the user operated the hotkeys. The game log recorded cash balances of 10000, 20000, 30000, 40000, and 50000, and four successful player-XP additions of 100.0. The game HUD also displayed 50000 cash and the changed player level. The automated F5 UI input was not executed because concurrent user input was detected; the view was refreshed instead.

## Remaining limits

- A complete standard washer cycle has not yet been timed live with fast washing toggled on and off. Native countdown semantics, wrapper signatures, installed patch binding, and managed patch behavior have been checked; live timing and completion remain to verify.
- Save persistence after exiting and relaunching has not been checked. Money and XP use the game's own methods and saving path.
- The trainer panel occupies the top-left corner and can overlap the game's HUD. F5 hides it.
- Other game builds and other washer types are not covered.

The successful startup DLL SHA256 was `DC1C98997592B1A17CD94DD70D3D1190C93BD2A1B04601DFC4AFA302EB31471B`. The local evidence excerpt is in `artifacts/validation/live-session.log` (excluded from source control).

## Portable EXE package

Packaged `dist/LaundryTrainer-Setup.exe` for Windows 10/11 x64. It contains the trainer DLL, verified MelonLoader 0.7.3 archive, Microsoft-signed .NET 6.0.36 Desktop Runtime installer, notices/source for bundled components, and its own .NET 8.0.31 runtime. Size: approximately 142.8 MiB.

- Setup core: **24/24 tests passed**, covering registry-independent Steam library fixtures, different paths, spaces/Unicode, modern/legacy manifests, pending downloads, nested installation directories, junctions, incompatible builds, other mods, atomic replacement/backups, rollback, and administrator retry eligibility.
- Bundle handling: **10/10 tests passed**, covering extraction, duplicate/path rejection, preserving existing files, and scoped cleanup.
- Published a self-contained single-file EXE with zero build warnings/errors.
- Copied only the EXE to an isolated folder with spaces and non-ASCII characters. Ran `--verify-package` with a nonexistent `DOTNET_ROOT`, `DOTNET_MULTILEVEL_LOOKUP=0`, and a PATH containing only Windows directories. It exited successfully, reported .NET 8.0.31/X64, verified all three embedded executable artifacts, and discovered the actual Steam game on E: from registry/library data. No project files or external payload were needed. This verifies relocation; it is not a test on a physically separate computer.
- Visually checked the published setup UI. Its **Install & play** action verified the bundle, detected the existing required runtime, reused the matching loader/trainer, and handed off to Steam using app 3150440. The setup showed successful installation and launch handoff. A new game startup could not be confirmed: Steam window capture timed out and the game log had not advanced. Steam launch completion remains unverified on this run.
- Missing-runtime installation and the Windows UAC flow were not executed on this computer, which already has the needed runtime and writable game folder. Bundled runtime signature/hashes and clean-rollback escalation logic were verified independently.

Initial English EXE SHA256: `F177092A60911BD2D0B5A857CC6D73F018CAF05296E957C2E6F8A685FB306E67`. This was superseded by the Chinese package below. Initial relocation evidence is under `artifacts/portable-check-9a14269e9841438c9f95f08e46bb457c/Another computer 测试/verification.json`.

## Simplified Chinese 0.1.0 package

Localized the installer, trainer panel, authored status/error messages, and user instructions. The trainer requests an installed Chinese font with fallbacks and wraps panel text. Configuration keys and gameplay behavior are unchanged. Third-party software retains its own language settings.

- Rebuilt the trainer and published the Windows x64 standalone installer successfully. All **49 tests passed**: 15 trainer tests (108 assertions), 24 installer tests, and 10 bundle tests.
- Visually checked the final published Chinese installer: title, instructions, detected path, status, and buttons render correctly without clipped text or missing glyphs. Closed the inspection window without invoking installation or Steam launch.
- Copied only the Chinese-named EXE to a new folder containing spaces and Chinese characters. With nonexistent `DOTNET_ROOT`/`DOTNET_ROOT_X64`, multilevel lookup disabled, and only Windows directories on PATH, `--verify-package` exited 0, reported .NET 8.0.31/X64, verified the embedded payload, and read the actual Steam library location successfully.
- Verified UTF-8 BOM encoding, script parsing, and Chinese filename decoding in Windows PowerShell 5.1. Both distributed EXE names contain identical bytes; checksum files accompany each.
- At the user's request, this localization pass did not install into, launch, or interact with the game. The new Chinese in-game panel and its font rendering have not been tested live. The earlier live gameplay evidence applies to the pre-localization DLL.

Chinese 0.1.0 EXE (now superseded): `dist/洗衣店模拟器修改器-中文版.exe` (149741002 bytes, approximately 142.8 MiB).

Chinese 0.1.0 EXE SHA256: `9A617B1D97632406DFAB78D1FFE5065F7B802CF2E51F0A26D50006736EAB86C5`.

Embedded Chinese 0.1.0 trainer DLL SHA256: `323C30546107611BBAB3001FF36C4A095F824234B8F3D2040F0AC2BBC86505CF`.

Chinese relocation evidence: `artifacts/chinese-portable-check-537a15ed3a1a4f759058e698bb60a6be/另一台电脑 测试/verification.json`. See `README.zh-CN.md` or `dist/使用说明.txt` for Chinese usage instructions.

## Version 0.2.0: dryer speed and laundry shop progression

- Added separate dryer acceleration, default 5x and configurable from 1–20x. F9 toggles it, Ctrl+F9 decreases the multiplier, and Shift+F9 increases it. Settings persist through MelonLoader preferences. Scene changes disable both machine accelerators.
- Added F10 to increase laundry shop rank by one using its own XP, challenge completion, cap checks, progression callbacks, and regular save path. Player XP remains a separate F7 action.
- Inspected cached generated wrappers and native dryer countdown methods. Inspected shop promotion/cap/save methods statically in the supported native binary without loading it into a game process. Independent source review found no actionable defect in the inspected paths. Detailed evidence is in `dryer-speed-implementation.md` and `shop-level-implementation.md`.
- Release trainer compilation succeeded with **zero warnings and zero errors**. The standalone Windows x64 installer published successfully, with product and embedded trainer versions both **0.2.0**. Updated Chinese instructions and feature text are included.
- Microsoft's release metadata endpoint failed with TLS EOF responses, so packaging used `scripts/Build-Setup.ps1 -UseCachedDependencies`. This explicitly reuses the preceding package's .NET 8.0.31 and existing downloads; the build still verifies the pinned loader hash and .NET 6.0.36 SHA512/Microsoft signature. The build script retains a UTF-8 BOM for Windows PowerShell 5.1 Chinese filenames.
- **No automated tests, trainer execution, installer execution, package smoke runs, or game launches/attachments were performed for 0.2.0. No installed-game files were changed.** Actual dryer timing, shop upgrades, event listeners, visible unlocks/rewards, preference persistence, and save/reload behavior remain untested in this revision.

Current package: `dist/洗衣店模拟器修改器-中文版.exe`, **149744586 bytes**, approximately 142.8 MiB. The English-named distribution copy is identical.

Current EXE SHA256: `58FA3820111413981CF35EFEF32B835BBC2D17840C8E0C76C6F570AB947658B5`.

Embedded 0.2.0 trainer DLL SHA256: `ED2A5445AB246FC3BE60B3F26D2FE0FB7CC945F229FD0528049C88DF41B81E8D`.
