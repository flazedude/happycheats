# GTA V Enhanced — Story Mode

HappyCheats trainer 0.2.0 provides an experimental Story Mode menu for the Windows Steam edition (app 3240220), with seven companion controls and an F11 in-game menu. GTA Online, including invite-only sessions, is unsupported. **Native startup and gameplay remain unverified: GTA was kept closed throughout this update.**

## Compatibility and setup

The latest Steam public build checked on **27 September 2026** is **25261616**, matching the installed **GTA5_Enhanced.exe 1.0.1158.16**. The Steam metadata record is [`artifacts/rdr2-runtime-research/gta-steam-public-3240220.json`](../artifacts/rdr2-runtime-research/gta-steam-public-3240220.json). This confirms the release identity, not that the trainer has been tested in the game.

| Game target | Runtime used | Automatic setup |
| --- | --- | --- |
| Reviewed Steam 1.0.1158.16 / build 25261616 | Included experimental Story Mode runtime | Acquire and verify the author's ASI loader if missing. No ScriptHookV dependency for this backend. |
| 1.0.1158.13 | Alexander Blade ScriptHookV 3889.0/1158.13 | Acquire and verify the author's ScriptHookV and ASI loader when needed. |

For `.16`, the client additionally requires the reviewed executable SHA-256 `69DA07FF67D05E9DED11289E597E8B8DC5855B0A429C085F37D148DC267CB2C5`. The native runtime requires the exact loaded version string and validated, unique interfaces; failed checks leave controls inactive. Other versions or different executables are blocked. Do not replace game files to bypass these checks.

The official ScriptHookV runtime is proprietary and its source is unavailable; the public SDK contains interfaces and samples, not its implementation. The `.16` backend is a limited GPL-2.0 runtime derived from open-source Enhanced interfaces and native mappings, independent of ScriptHookV. It contains only the Story Mode functionality used here. It does not patch the official hook or bypass its compatibility checks. The `.13` path retains ScriptHookV's own checks. See [native provenance](../modules/gta-v-enhanced/native/PROVENANCE.md).

1. Close GTA V Enhanced. Select **GTA V Enhanced → Install / update** in HappyCheats. Missing recognized dependencies are obtained directly from the [official author](https://www.dev-c.com/gtav/scripthookv/) or a previously verified local cache. First-time acquisition requires internet access; the archive and selected DLLs must match pinned SHA-256 values. Proprietary runtime files are not redistributed in HappyCheats.
2. Setup installs the owned `HappyCheats.GtaV.asi`, `HappyCheatsGtaV/ThirdPartyNotices.txt`, `HappyCheatsGtaV/Source.zip` and ownership receipt. It validates package integrity and ownership before dependency changes. Unknown loaders or hook conflicts are preserved and reported. On `.13`, a recognized older hook is backed up before replacement; failed trainer installation also rolls back dependency changes where safe.
3. Start **Story Mode** when ready to test, load a living character, and use the companion or **F11** menu. Up/Down and Enter select controls. Actual startup and gameplay compatibility have not been established by this release.
4. To remove this trainer, close the game and choose **Remove trainer**. Modified and unowned files are preserved. Shared runtime DLLs, recovery backups, other trainers, launch arguments, launcher settings and saves remain unchanged by removal.

On this development PC, the existing `ScriptHookV.dll` identifies the older **3788.0/1013.34** runtime, and `NativeTrainer.asi` and `TrainerV.asi` are also installed. The `.16` setup preserves these files and does not use ScriptHookV itself. Those other ASIs can independently load the older hook and prevent startup. This known existing-mod conflict remains untested because the game was kept closed; it is not resolved by installing this menu.

The companion and native menu share one acknowledged command protocol. Commands require fresh process/session identity and expire after eight seconds. A timeout is not retried automatically because an action may already have applied. Character/death/loading transitions clear toggles when an eligible script callback is available. Invincibility restores its captured original state. The ammo API has no corresponding state getter, so avoid having two trainers control the same ammo flag simultaneously.

## Controls

| Key | Control | Behavior |
| --- | --- | --- |
| F1 | Invincibility | Toggle protection for the current Story Mode player. |
| F2 | Unlimited ammo | Keep the current player's weapon clip supplied without reloading. |
| F3 | Never wanted | Keep the wanted level clear while enabled. |
| F4 | Refill health and armor | Restore health and armor once. |
| F6 | Repair current vehicle | Repair the current vehicle; unavailable on foot. |
| F7 | Set noon | Set the Story Mode clock to 12:00 once. |
| F8 | Clear weather | Set clear Story Mode weather once. |
| F10 | Disable all | Turn off continuous controls; earlier one-shot changes are not undone. |
| F11 | Menu | Show or hide the menu. |

Shortcuts require foreground focus and an eligible Story Mode character. Once Online is observed, commands and effects remain disabled until the game restarts, including invite-only sessions. The `.13` ScriptHookV dependency also enforces its own multiplayer restriction. No Online cheats, money/RP manipulation, anti-cheat bypass or private-server resource are included. Setup does not change anti-cheat settings or launch arguments.

## Source, building and validation

The combined native module is GPL-2.0. Its complete corresponding source, tests, build instructions, vendor source and licenses accompany the binary in `HappyCheatsGtaV/Source.zip`. JSON for Modern C++ is MIT licensed; MinHook is BSD-2-Clause with retained HDE notices. Game code and proprietary SDK/runtime files are excluded. The companion remains a separate client of the file protocol.

Use PowerShell 7 and a Windows x64 GCC/MinGW toolchain:

```powershell
.\scripts\Build-GtaV.ps1
.\scripts\Test-GtaV.ps1
```

The manifest hashes the freshly built ASI, notices and source archive. Both the full LAN update package and standalone EXE include this payload. Offline checks cover native mock/synthetic hosts, script context, Online guards, dispatch and acknowledgements, Core setup/rollback/ownership and executable checks, and bilingual WPF states. These tests do not establish actual gameplay compatibility. No GTA process was launched, and installed game files and saves were not changed during development.

## 中文说明

本版本面向已核对的 Steam 最新公开版本 **25261616 / 1.0.1158.16**，提供实验性故事模式菜单，F11 打开。尚未验证游戏内加载与功能；本次开发始终保持游戏关闭。不支持 GTA 在线模式，包括邀请战局。

`.16` 使用内置的 GPL 开源运行库，不依赖 ScriptHookV；“安装 / 更新”会自动获取并校验所需 ASI 加载器。`.13` 继续使用从作者网站获取并校验的 ScriptHookV 和加载器。首次配置需要联网，无法识别的现有加载器或 Hook 文件会被保留并报告。完整原生源码、构建脚本、依赖源码及许可证随 `HappyCheatsGtaV/Source.zip` 提供。

此电脑已有旧版 ScriptHookV（3788.0/1013.34）、NativeTrainer.asi 和 TrainerV.asi。`.16` 安装会保留它们；其他 ASI 仍可能自行加载旧 Hook 并影响游戏启动。此冲突尚未进行游戏内验证，本菜单不会自动移除其他模组、修改启动参数或更改存档。
