# Nomad Drive

Independent package **1.0.1**, Steam app **2223980**, inspected build **26391437**.
HappyCheats **0.3.69+** and contract API 1 are required. The client version and standalone EXE stay unchanged.

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

In **HappyCheats 0.3.70+**, open **Update** beside Library Sort, then **Check for updates**. Choose **Update** for Nomad Drive, or **Add trainer** if it is absent from Library. Close Nomad Drive first. The client downloads and verifies the signed package, repairs the cache if necessary, loads only this module, discovers the game and runs its existing installer in one operation. No first-add script or separate Library reload is required.

If the folder is missing, the game is running or setup fails, the downloaded trainer remains in Library with a retryable **Finish update** action. Use Find in Steam / Browse folder when needed, then retry the same update. Current/older packages are omitted after verified installation. The module requires client 0.3.69+ for shared toggle restoration; the catalog's Add trainer flow requires the existing 0.3.70+ client feature. This game update does not rebuild or replace the EXE. Older first-add scripts remain historical compatibility utilities and are not the supported setup path for current clients.

Package 1.0.1 revalidates all eleven controls against the installed assembly on **14 November 2026** and adds a final installed-file SHA-256 check before committing an installation receipt or reporting completion. Existing owned 1.0.0 installations upgrade through the normal installer; foreign or modified files remain protected. Steam build 26391437 and the inspected assembly hash are unchanged; no uninspected later build is claimed.

The installer preserves foreign/modified files, records owned files, rolls back partial writes and retains the shared BepInEx loader when removing the plugin. A conflicting loader produces an actionable error. No helper launches a game or writes a save. In normal play the game can save changed survival/vehicle values; no promise is made about achievements or leaderboard eligibility.

## Evidence and limitations

Read-only inspection and compilation against the installed game verified the public stat, medicine, fuel and condition APIs. The inspected assembly SHA-256 and public method signatures are in [the binding report](nomad-drive-bindings.json); extracted game code/assets are not distributed. The loader is the repository's hash-pinned official BepInEx 5.4.23.5 x64 package, with upstream license/source notices. Only the plugin and loader are shipped, never game assemblies.

Search on 4 November 2026 found Nomad Drive listed with 14 cheats in [WeMod's catalog](https://www.wemod.com/cheats). Third-party guides disagree about the console; the installed assembly contains DeveloperConsole and the player stat/medicine methods used here. This module is independently implemented and does not download or reuse third-party trainer binaries.

Validation includes synthetic runtime/protocol cases, transactional installer fixtures, exact-build signatures, payload hashes, shared overlay/controller regression and signed-package cache loading. These do **not** establish game startup, gameplay effects, physical-controller operation, exclusive-fullscreen rendering, achievement behavior or reboot behavior. No game was launched and no save was changed for validation.

## 中文

独立修改器 **1.0.1** 支持已检查的 Steam 构建 **26391437**，需要 HappyCheats **0.3.69 或更新版本**，不替换客户端 EXE。F1 治疗伤害；F2/F3/F4 补满食物、水分和精力；F5 治疗中毒；F6 补满最近车辆现有汽油箱；F7 修复最近车辆已安装的引擎；F8/F9/F10 切换无限食物、水分和精力；F11 快速治疗；F12 关闭本插件的持续功能。

仅支持已加载存活角色的单人主机游戏。车辆距离限制为 15 米，需要已安装对应部件；汽油箱中应先有汽油，不转换其他液体。快速治疗不能防止瞬间致死，也不能复活。按 Ctrl+Del 或按住 Menu 350 毫秒后点按 RB 打开统一菜单，使用鼠标/键盘操作。

在 HappyCheats **0.3.70 或更新版本**中，打开游戏库排序旁的“更新”，检查后选择 Nomad Drive 的“更新”或“添加修改器”。先关闭游戏；客户端会一次完成签名验证、下载、缓存修复、加载与安装，不需要外部脚本或单独重新加载游戏库。找不到游戏目录、游戏仍在运行或安装失败时，修改器保留在游戏库，可通过同一“完成更新”操作重试。模块需要 0.3.69+ 的统一开关恢复能力；不会替换 EXE。

1.0.1 已于 2026 年 11 月 14 日重新检查已安装的 Steam 构建 26391437，程序集哈希未变；新增安装完成前的逐文件 SHA-256 校验，兼容原有 1.0.0 归属记录升级，保留外来或被修改的文件。已完成静态和离线合成检查，未验证实际游戏效果或真实手柄，未启动游戏或修改存档。
