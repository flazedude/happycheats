# Nomad Drive

Independent package **1.0.2**, Steam app **2223980**, inspected build **26391437**.
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

Package 1.0.2 fixes startup on the game's stripped Unity 6000.0.73f1 Windows Mono framework. It installs four hash-pinned unstripped Windows framework DLLs in `BepInEx/nomad-runtime` and configures Doorstop's isolated search override. Original game DLLs remain untouched. The installer accepts the older 16-file ownership records and the expanded 20-file records, including the local development test record. Update/Install verifies every installed payload file before reporting completion. Foreign or modified files remain protected. Steam build 26391437 and the inspected assembly hash are unchanged; no uninspected later build is claimed.

The installer preserves foreign/modified files, records owned files, rolls back partial writes and retains the shared BepInEx loader when removing the plugin. A conflicting loader produces an actionable error. No helper launches a game or writes a save. In normal play the game can save changed survival/vehicle values; no promise is made about achievements or leaderboard eligibility.

## Evidence and limitations

Read-only inspection and compilation against the installed game verified the public stat, medicine, fuel and condition APIs. The inspected assembly SHA-256 and public method signatures are in [the binding report](nomad-drive-bindings.json); extracted game code/assets are not distributed. The loader is the repository's hash-pinned official BepInEx 5.4.23.5 x64 package. Its isolated Mono framework comes from the matching Windows Unity editor, with source provenance, pinned hashes and upstream license notices. Never substitute Linux corlibs. No game assemblies are shipped.

Search on 4 November 2026 found Nomad Drive listed with 14 cheats in [WeMod's catalog](https://www.wemod.com/cheats). Third-party guides disagree about the console; the installed assembly contains DeveloperConsole and the player stat/medicine methods used here. This module is independently implemented and does not download or reuse third-party trainer binaries.

On **14 November 2026**, at the user's explicit request, a live test on JACK-PC reproduced the preloader failure, then verified successful trainer startup and connection with the corrected Windows runtime. A fresh solo world named **HappyCheats Trainer Test** verified food refill (F2), water refill through the released client's Apply button, unlimited food (F8), and Disable all acknowledgements. Existing saves were not loaded or edited. The game may retain the new test world. Other cheat effects, physical controllers, exclusive-fullscreen overlay rendering, achievements and reboot behavior remain unverified. Offline validation also covers runtime/protocol cases, old/new ownership records, repeat installation, foreign-file rejection, rollback, payload hashes and shared menu/controller regressions.

## 中文

独立修改器 **1.0.2** 支持已检查的 Steam 构建 **26391437**，需要 HappyCheats **0.3.69 或更新版本**，不替换客户端 EXE。F1 治疗伤害；F2/F3/F4 补满食物、水分和精力；F5 治疗中毒；F6 补满最近车辆现有汽油箱；F7 修复最近车辆已安装的引擎；F8/F9/F10 切换无限食物、水分和精力；F11 快速治疗；F12 关闭本插件的持续功能。

仅支持已加载存活角色的单人主机游戏。车辆距离限制为 15 米，需要已安装对应部件；汽油箱中应先有汽油，不转换其他液体。快速治疗不能防止瞬间致死，也不能复活。按 Ctrl+Del 或按住 Menu 350 毫秒后点按 RB 打开统一菜单，使用鼠标/键盘操作。

在 HappyCheats **0.3.70 或更新版本**中，打开游戏库排序旁的“更新”，检查后选择 Nomad Drive 的“更新”或“添加修改器”。先关闭游戏；客户端会一次完成签名验证、下载、缓存修复、加载与安装，不需要外部脚本或单独重新加载游戏库。找不到游戏目录、游戏仍在运行或安装失败时，修改器保留在游戏库，可通过同一“完成更新”操作重试。模块需要 0.3.69+ 的统一开关恢复能力；不会替换 EXE。

1.0.2 修复 Unity 6000.0.73f1 精简 Windows Mono 框架导致修改器无法启动的问题。四个经过哈希校验的 Windows 框架 DLL 安装至独立目录，不替换游戏原始 DLL。安装器兼容旧版 16 文件及新版 20 文件归属记录，修复测试安装后出现的“无效修改器包或归属记录”错误，并保留外来或被修改的文件。

2026 年 11 月 14 日按用户明确要求在 JACK-PC 启动游戏，使用新建单人测试世界验证连接、F2 食物补满、客户端按钮补水、F8 无限食物及关闭全部确认。未加载或编辑已有存档；游戏可能保留新建的测试世界。其余效果、真实手柄、独占全屏覆盖菜单、成就及重启行为未验证。
