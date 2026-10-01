# Elden Ring

HappyCheats 0.3.37 adds an experimental offline resource trainer for Steam Elden Ring (App ID 1245620). Nightreign is a different game and is unsupported. Both the signed LAN update and the complete standalone EXE include the runtime and its compatibility profile.

## Controls

| Control | Shortcut | Effect |
| --- | --- | --- |
| Refill health | F1 | Periodically refill the living local character's current HP. Lethal damage can still kill. |
| Refill FP | F2 | Periodically refill current FP to the current maximum. |
| Refill stamina | F3 | Periodically refill current stamina to the current maximum. |
| Restore all resources | F4 | Refill HP, FP and stamina once. |
| Disable all | F12 | Turn off all periodic refills. |

Keep HappyCheats open on the Elden Ring page. These are periodic refills, not invulnerability or removal of resource costs. Maximum values, attributes, runes and inventory are unchanged. Death, character changes, disconnection and leaving the page clear the toggles; enable them again deliberately. A long interruption in polling also clears them. Function keys operate while the connected game or HappyCheats has focus.

## Connecting

1. Close the game if it was started normally through Steam. Keep Steam running under the account that owns the game.
2. Choose Elden Ring in HappyCheats. Use **Find in Steam**, or select the Elden Ring root or its `Game` folder.
3. Choose **Offline launch**. This starts `Game/eldenring.exe` with the Steam application ID in its child environment. It does not rename an executable, replace Easy Anti-Cheat, install a service, or change game files.
4. Load an offline single-player character, then choose **Connect trainer**. Connection must pass the version, offline-session and anti-cheat checks before controls become available.
5. Use **Disconnect trainer** or leave the Elden Ring page to release the connection. Close the game before returning to ordinary Steam launch.

Elden Ring uses [Easy Anti-Cheat](https://en.bandainamcoent.eu/elden-ring/news/elden-ring-patch-notes-version-1101). Selecting the game's offline menu option alone does not remove the protected Steam launch. HappyCheats rejects protected launchers/EAC and detected online or Seamless Co-op sessions. It does not hide from, disable, patch or unload a running anti-cheat component. The direct executable and application-ID launch follows the approach documented in the [ModEngine2 launcher source](https://github.com/soulsmods/ModEngine2/blob/main/launcher/launcher.cpp); application identity alone is not proof of an offline session, so runtime checks are also required.

Use these controls only for offline play. There is no guarantee against account penalties if you later take a modified gameplay session online. Setup does not edit save files; the game can still autosave normal gameplay outcomes while refills are used.

## Compatibility and validation

The initial profile targets **App Ver. 1.17.1** (worldwide Windows executable version **2.7.1.0**). This comes from maintained modding-source layouts, not inspection of an installed game on this host. Other versions are rejected. The runtime checks process identity, executable metadata, memory signatures, pointers, resource ranges and the offline state. Missing or ambiguous evidence blocks controls. Updating the game can require a new trainer release. Source references and pinned revisions are included in the payload's `profile.json` and `ThirdPartyNotices.txt`.

Validation covers isolated memory fixtures, session and command handling, anti-cheat/online rejection, Chinese and English UI, packaging and signed LAN downloads. Elden Ring is not installed in this host's registered Steam libraries. No game was started and no save was changed during development. Gameplay effects, game launch on the gaming PC and connectivity from a second PC remain unverified.

## 中文说明

这是 Steam《艾尔登法环》的实验性离线资源补满功能，面向 **App Ver. 1.17.1（全球版 Windows 程序版本 2.7.1.0）**，不支持其他版本或《黑夜君临》。F1 定期补满生命，F2 补满专注值，F3 补满精力，F4 一次补满三种资源，F12 关闭持续功能。补满生命并非无敌，致命伤害仍可导致死亡。请保持 HappyCheats 的本游戏页面打开；死亡、角色变化、断开连接、切换页面或长时间停止轮询会清除开关。

先关闭通过 Steam 正常启动的游戏，保持 Steam 运行，在 HappyCheats 中选择“离线启动”，载入离线单人角色后选择“连接修改器”。启动方式不替换游戏文件或反作弊组件。只有受支持版本、明确的离线状态及反作弊检查全部通过后才能写入。检测到 Easy Anti-Cheat、受保护启动器、联网或无缝联机时拒绝工作。

仅限离线使用。安装流程不修改存档文件；游戏仍可能自动保存正常游玩产生的进度，因此不保证以后联网时不会受到处罚。本机未安装游戏，尚未验证真实游戏效果、另一台电脑上的启动或局域网连接。
