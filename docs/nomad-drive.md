# Nomad Drive

Independent package **1.0.0**, Steam app **2223980**, inspected build **26391437**.
HappyCheats **0.3.65+** and contract API 1 are required. The client version and standalone EXE stay unchanged.

## Controls

| Key | Action |
| --- | --- |
| F1 | Heal direct damage (no resurrection) |
| F2 / F3 / F4 | Refill food / water / energy |
| F5 | Cure poison using the game's medicine API |
| F6 | Fill the nearest vehicle's gasoline tank |
| F7 | Repair the nearest vehicle's installed engine |
| F8 / F9 / F10 | Toggle unlimited food / water / energy |
| F11 | Toggle rapid healing |
| F12 | Stop this payload's continuous actions |

Only a living local player in a **solo hosted game** is supported. Joining another host or adding a remote player makes actions unavailable and clears payload toggles. The shared client can restore requested toggles after a supported session returns. Rapid healing replenishes damage health each frame; it does not prevent instant lethal damage or revive a dead character. Refills stop when turned off; they do not restore the earlier depleted stat values.

Vehicle controls target the nearest initialized vehicle within **15 metres** and reject ambiguous distances. They require existing parts. Fuel preserves the liquid type and requires gasoline already in the tank; it does not convert water or guess empty-tank fuel. Engine repair restores condition only, without replacing parts or promising to fix overheating, oil, coolant or electrical faults.

Use the common **Ctrl+Del** menu, or enable Xbox shortcuts and hold **Menu for 350 ms, then tap RB**. These shortcuts use the existing shared overlay/controller implementation. Mouse and keyboard operate the menu; windowed/borderless play is supported. No separate HUD or controller profile is installed.

## Add to the existing client

Clients 0.3.68/0.3.69 hide catalog entries that are absent from their Library. For this new game, close HappyCheats and supported games, then run in PowerShell 7 from a synced source checkout with .NET 8 installed:

```powershell
.\scripts\Add-NomadDrive.ps1 -ClientDirectory '<existing unpacked HappyCheats client folder>'
```

The helper checks the existing EXE version, pins the established public signing key, checks both LAN catalogs and GitHub, and uses the existing trusted cache API to verify and install the exact signed package. It installs the downloaded game's trainer files while the game is closed. Reopen the **same HappyCheats EXE** to load the new Library entry. Subsequent Nomad Drive updates use Update beside Library Sort and the existing game setup action. No shared client changes are required.

The installer preserves foreign/modified files, records owned files, rolls back partial writes and retains the shared BepInEx loader when removing the plugin. A conflicting loader produces an actionable error. No helper launches a game or writes a save. In normal play the game can save changed survival/vehicle values; no promise is made about achievements or leaderboard eligibility.

## Evidence and limitations

Read-only inspection and compilation against the installed game verified the public stat, medicine, fuel and condition APIs. The inspected assembly SHA-256 and public method signatures are in [the binding report](nomad-drive-bindings.json); extracted game code/assets are not distributed. The loader is the repository's hash-pinned official BepInEx 5.4.23.5 x64 package, with upstream license/source notices. Only the plugin and loader are shipped, never game assemblies.

Search on 4 November 2026 found Nomad Drive listed with 14 cheats in [WeMod's catalog](https://www.wemod.com/cheats). Third-party guides disagree about the console; the installed assembly contains DeveloperConsole and the player stat/medicine methods used here. This module is independently implemented and does not download or reuse third-party trainer binaries.

Validation includes synthetic runtime/protocol cases, transactional installer fixtures, exact-build signatures, payload hashes, shared overlay/controller regression and signed-package cache loading. These do **not** establish game startup, gameplay effects, physical-controller operation, exclusive-fullscreen rendering, achievement behavior or reboot behavior. No game was launched and no save was changed for validation.

## 中文

独立修改器 **1.0.0** 支持已检查的 Steam 构建 **26391437**，需要 HappyCheats **0.3.65 或更新版本**，不替换客户端 EXE。F1 治疗伤害；F2/F3/F4 补满食物、水分和精力；F5 治疗中毒；F6 补满最近车辆现有汽油箱；F7 修复最近车辆已安装的引擎；F8/F9/F10 切换无限食物、水分和精力；F11 快速治疗；F12 关闭本插件的持续功能。

仅支持已加载存活角色的单人主机游戏。车辆距离限制为 15 米，需要已安装对应部件；汽油箱中应先有汽油，不转换其他液体。快速治疗不能防止瞬间致死，也不能复活。按 Ctrl+Del 或按住 Menu 350 毫秒后点按 RB 打开统一菜单，使用鼠标/键盘操作。

0.3.68/0.3.69 会隐藏尚未加入游戏库的新游戏，请关闭客户端和受支持游戏，使用上方 Add-NomadDrive.ps1 命令首次安装签名模块，再打开原 EXE。后续可使用游戏库排序旁的更新按钮。已完成静态和离线合成检查，尚未验证游戏启动、实际效果或真实手柄；没有启动游戏或改动存档。正常游玩时游戏可能保存修改后的数值，未承诺成就或排行榜资格。
