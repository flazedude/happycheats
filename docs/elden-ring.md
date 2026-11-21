# Elden Ring

HappyCheats provides an experimental offline trainer for Steam Elden Ring (App ID 1245620). Nightreign is a different game and is unsupported. Elden Ring uses the existing shared client and companion menu.

Independent package **1.0.2**, runtime/profile **0.2.0**, adds incoming-hit protection, no consumable consumption and a one-shot rune value editor to the existing resource refills. The worldwide target remains **1.17.2 / file 2.7.2.0**. Download the independent Elden Ring package through Library Update; the HappyCheats client version and complete EXE are unchanged. Requires client **0.3.69** or newer.

## Controls

| Control | Shortcut | Effect |
| --- | --- | --- |
| Refill health | F1 (client focused) | Periodically refill the living local character's current HP. Lethal damage can still kill. |
| Refill FP | F2 (client focused) | Periodically refill current FP to the current maximum. |
| Refill stamina | F3 (client focused) | Periodically refill current stamina to the current maximum. |
| Restore all resources | F4 (client focused) | Refill HP, FP and stamina once. |
| Incoming-hit protection | Menu/client button | Enable the local player's incoming-hit protection flag. Other damage and death mechanisms are not covered by a blanket invulnerability claim. Gameplay behavior is unverified. |
| No consumable consumption | Menu/client button | Enable the local player's no-consumption flag. Which consumables, ammunition or other item uses it affects remains unverified. |
| Set runes | Numeric editor | Set the current rune balance once to a whole number from 0 to 999,999,999. This is an absolute value, not an amount to add. |
| Disable all | F12 (client focused) | Turn off periodic refills and flag toggles, and cancel pending toggle restoration. Rune edits are not undone. |

Keep HappyCheats open on the Elden Ring page. Health, FP and stamina refills run periodically; they do not remove costs, and lethal damage can kill between refills. Incoming-hit protection is a separate experimental control. Maximum resource values and attributes are unchanged. The rune editor changes the current balance only when explicitly applied; normal gameplay can save that balance.

From client **0.3.69**, requested toggle states remain On through loading, death, character/session replacement and disconnection within the current client process. The shared client shows **On · waiting to restore** until the new session is connected, ready and the feature is available, then restores the request through the module's normal command path. Explicit Off and Disable all cancel pending restoration. Failed restoration is reported and logged without an endless retry loop; Off then On retries it. A new client process starts with no remembered requests. Restore all resources and Set runes are one-shot actions and are never automatically replayed.

Leaving the Elden Ring page disconnects the memory session. Returning to it still requires an explicit **Connect trainer** before pending toggles can restore. F1–F4 and F12 work while **HappyCheats has focus**. For operation while the game has focus, open the shared **Ctrl+Del / Menu+RB** menu and click its controls. This is a pre-existing limitation of independently downloaded Elden Ring modules on the current client; this package leaves the client and its keyboard hook unchanged. The three new controls use the client buttons and shared menu and have no direct function-key shortcut.

Flag cleanup restores only the bits owned by this connection when the same local player can still be verified. During a temporary player absence, cleanup can remain pending until that same player returns while connected. A verified replacement releases the former ownership record. Cleanup always resolves a fresh player target and never writes a saved pointer after disappearance or replacement. If deactivation during a page change or client exit cannot confirm cleanup, it records the failure locally. Rune edits do not change level or the game's total-earned-runes field.

The shared menu opens with **Ctrl+Del**, or, with Xbox shortcuts enabled, **hold Menu (☰) for 350 ms, then tap RB**. Use mouse/keyboard for its controls and rune editor. The menu supports windowed/borderless display; these offline checks do not establish exclusive-fullscreen, live-game or physical-controller behavior. All controls remain clickable while disconnected, unavailable or busy; execution reports an error if its requirements are not met.

## Connecting

1. Close the game if it was started normally through Steam. Keep Steam running under the account that owns the game.
2. Choose Elden Ring in HappyCheats. Use **Find in Steam**, or select the Elden Ring root or its `Game` folder.
3. Choose **Offline launch**. This starts `Game/eldenring.exe` with the Steam application ID in its child environment. It does not rename an executable, replace Easy Anti-Cheat, install a service, or change game files.
4. Load an offline single-player character, then choose **Connect trainer**. Connection and writes must pass the version, offline-session, anti-cheat and operation-specific target checks. Buttons remain clickable and report any unmet requirement.
5. Use **Disconnect trainer** or leave the Elden Ring page to release the connection. Close the game before returning to ordinary Steam launch.

Elden Ring uses [Easy Anti-Cheat](https://en.bandainamcoent.eu/elden-ring/news/elden-ring-patch-notes-version-1101). Selecting the game's offline menu option alone does not remove the protected Steam launch. HappyCheats rejects protected launchers/EAC and detected online or Seamless Co-op sessions. It does not hide from, disable, patch or unload a running anti-cheat component. The direct executable and application-ID launch follows the approach documented in the [ModEngine2 launcher source](https://github.com/soulsmods/ModEngine2/blob/main/launcher/launcher.cpp); application identity alone is not proof of an offline session, so runtime checks are also required.

Use these controls only for offline play. There is no guarantee against account penalties if you later take a modified gameplay session online. Setup does not edit save files; the game can still autosave normal gameplay outcomes while refills are used.

## Compatibility and validation

The current profile targets **App Ver. 1.17.2** (worldwide Windows executable version **2.7.2.0**). It was checked against maintained, pinned modding sources and the installed Steam build 25827938 on 9 November 2026. Other versions are rejected. The runtime checks process identity, executable metadata, memory signatures, pointers, resource ranges and the offline state. Missing or ambiguous evidence blocks controls. Updating the game can require a new trainer release. Source references and pinned revisions are included in the payload's `profile.json` and `ThirdPartyNotices.txt`.

The existing client's `MainWindow.CivilizationShortcutScope` identifies Elden Ring through a concrete `EldenRingModule` type check. An independently downloaded module is loaded in a separate assembly load context, so that type identity does not match the client's original module type. The game-focused F-key hook therefore does not acquire its Elden Ring scope for that downloaded module. Client-focused F1–F4/F12 and the metadata-driven shared menu remain the available paths; this game-only package does not change the frozen host.

Validation covers isolated memory fixtures, session and command handling, anti-cheat/online rejection, numeric metadata and English/Chinese UI with the shared menu. The installed executable and unique signature targets previously passed read-only checks on 9 November 2026. For the 21 November expansion, the game was not launched or attached and no save was changed. Gameplay effects, item-consumption coverage, live-game menu behavior, physical-controller behavior and connectivity from a second PC remain unverified. Fixture success is not gameplay verification.

## 中文说明

这是 Steam《艾尔登法环》的实验性离线修改器，面向 **App Ver. 1.17.2（全球版 Windows 程序版本 2.7.2.0）**，不支持其他版本或《黑夜君临》。运行时/配置 **0.2.0** 保留 F1 定期补满生命、F2 补满专注值、F3 补满精力、F4 一次补满三种资源，并增加“来袭攻击保护”“消耗品不消耗”和“设置卢恩”。前三种补满功能不取消资源消耗，生命补满并非无敌，致命伤害仍可能导致死亡。

来袭攻击保护使用本地角色的受击保护标志，不保证防止其他伤害或所有死亡机制。消耗品不消耗使用本地角色的不消耗标志；具体覆盖哪些消耗品、弹药或其他物品使用，尚未在游戏中验证。设置卢恩通过数值框将当前余额一次性设为 **0 至 999,999,999 的整数**，不是增加指定数量；正常游戏可能保存该余额。三项新功能通过客户端按钮或统一菜单操作，没有新增 F5/F6 快捷键。F12 关闭持续功能并取消等待恢复的请求，不撤销已经完成的卢恩修改。

从客户端 **0.3.69** 起，当前客户端进程会保留加载、死亡、角色/会话变化或断开连接前的开启请求；效果未确认时显示“已开启 · 等待恢复”。只有新会话已连接、已就绪且功能可用后，才通过原有模块指令恢复。主动关闭或“关闭全部”会取消恢复。恢复失败会报告并记录，不会无限重试；可关闭后重新开启来重试。新启动的客户端不会记住旧请求。恢复全部资源、设置卢恩等单次操作绝不会自动重放。

请保持 HappyCheats 的本游戏页面打开；切换页面会断开内存连接，返回后仍需明确点击“连接修改器”，之后才会恢复待处理的开关。F1–F4 和 F12 需在 **HappyCheats 获得焦点时**使用。游戏获得焦点时，请打开统一菜单后点击相应功能：按 **Ctrl+Del**，或启用 Xbox 快捷键后，**按住 Menu（☰）350 毫秒再点按 RB**。菜单使用鼠标和键盘操作，支持窗口/无边框模式。所有控件保持可点击，执行条件不满足时会显示错误。

关闭标志功能或断开连接时，仅在仍能验证同一本地角色的情况下恢复本连接拥有的标志位。角色暂时消失时，可在保持连接期间等待同一角色返回后清理；确认角色已替换则放弃旧角色的归属记录。清理始终重新解析当前角色，角色消失或替换后不会写入保存的旧指针。切换页面或退出客户端时若无法确认清理成功，会在本地记录失败。卢恩编辑不修改等级或游戏的累计获得卢恩字段。

现有客户端的 `MainWindow.CivilizationShortcutScope` 使用具体 `EldenRingModule` 类型来识别游戏；独立下载的模块位于另一个程序集加载上下文中，类型身份与客户端原始模块不同，因此游戏获得焦点时的 F 键钩子无法取得其艾尔登法环作用域。这是此前独立模块已有的限制，本次仅更新游戏包，未修改客户端或键盘钩子。可在客户端获得焦点时使用 F1–F4/F12，或通过统一菜单操作。

先关闭通过 Steam 正常启动的游戏，保持 Steam 运行，在 HappyCheats 中选择“离线启动”，载入离线单人角色后选择“连接修改器”。启动方式不替换游戏文件或反作弊组件。只有受支持版本、明确的离线状态、反作弊及当前操作目标检查全部通过后才能写入。检测到 Easy Anti-Cheat、受保护启动器、联网或无缝联机时拒绝工作。此说明供用户自行使用；本次开发不会启动游戏或连接其进程。

仅限离线使用。安装流程不直接修改存档文件；游戏仍可能自动保存正常游玩和卢恩编辑产生的结果，因此不保证以后联网时不会受到处罚。2026 年 11 月 21 日扩展仅进行隔离内存、指令、数值和界面检查；没有启动游戏、连接游戏进程或修改存档。真实游戏效果、物品不消耗覆盖范围、实际游戏内菜单、实体手柄及另一台电脑的局域网连接均未验证。

Current independent compatibility review: [9 November 2026](compatibility-20261109.md).
