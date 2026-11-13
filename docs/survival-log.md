# Survival Log / 生存日志

Independent package **1.0.2**, native trainer **0.1.2**, Steam app **4164790**, inspected Steam build **26615062**. Requires HappyCheats **0.3.69** for the current shared toggle-restoration behavior. The existing EXE and client version stay unchanged.

On client 0.3.70 or newer, close the game and use **Library → Update** beside Sort to add or update this trainer in one operation. No EXE is replaced.

For first installation on older clients 0.3.68/0.3.69, new games absent from their Library are hidden. Close HappyCheats and supported games, then run `scripts/Add-SurvivalLog.ps1` in PowerShell 7 from this source checkout (.NET 8 is required). The helper locates a compatible extracted official client; `-ClientDirectory` can select an unpacked client explicitly. It pins the established public signing key, checks both LAN catalogs and GitHub, verifies the selected signed package through the existing cache API, and installs its trainer files while the game is closed. Reopen the **same EXE** to load Survival Log. Later updates use **Library → Update** beside Sort.

Then launch the game yourself, load a living character, and choose **Connect trainer / 连接修改器** (F8). Discovery, opening the page and installing files never inject or launch the game.

| Control | Behavior |
| --- | --- |
| F1 Unlimited satiety | Keep the player's satiety at its current maximum on its next stat write. |
| F2 Unlimited morale | Keep the player's morale at its current maximum on its next stat write. |
| F3 Unlimited stamina | Keep the player's stamina at its current maximum on its next stat write. |
| F4 Unlimited health | Keep the player's health attribute at maximum on its next stat write. |
| F5 Unlimited life | Keep the player's vitality at maximum on its next stat write. |
| F6 Freeze clock | Set the native clock-frozen setting; timed activities may also stop. Turning it off restores the prior setting only while the trainer still owns it. |
| F7 High carrying capacity | A player-only carrying limit of 1,000,000; inventory slots are unchanged. |
| Set money | Set gold once, from 0 to 100,000,000, through the game's own `LeadingRole.GmSetGold` method and notification path. |
| Add survival points | Add 1 to 1,000,000 points once through the native points manager. Overflow is rejected. |
| Skill XP multiplier | Multiply newly earned base and discovery skill XP by 1–100, preserving source/level arguments and the native award path. Resets to 1 after loading, Disable all or lease expiry. |

The seven toggle requests use the existing shared client restoration path after loading/player replacement. The native module clears confirmed effects when the player changes and waits for a fresh command. Money and points are never automatically replayed. Stat refills, currency, points and earned XP can be saved by the game; disabling does not undo prior gains. Scripted deaths remain possible.

Use **Ctrl+Del** or, with Xbox shortcuts enabled, **hold Menu for 350 ms, then tap RB** for the shared companion menu. Use mouse/keyboard for its controls. Windowed/borderless mode is supported by the shared menu; exclusive fullscreen and controller row navigation are not supported.

## Bindings and validation

The installed game uses IL2CPP metadata version 31. Read-only inspection used [Il2CppDumper v6.7.46](https://github.com/Perfare/Il2CppDumper/releases/tag/v6.7.46), native PE disassembly and the locally installed metadata; no extracted game assemblies/assets are shipped. `tools/InspectSurvivalLog/verified-build.json` records the native/metadata fingerprints and short code sentinels. The builder verifies both hashes; the DLL verifies both again before installing hooks and checks all hook/call prologues in memory. Unsupported builds fail with a visible error.

| Binding | Inspected evidence |
| --- | --- |
| `BattleLogicWorld.OnUpdateAction` / `EndBattle` | Main game thread dispatch and explicit teardown. World agent manager at `+0x40`, clock at `+0x80`, proficiency at `+0x158`, survival results at `+0x160`. |
| `AgentManager.GetLeadingRole` | Resolves the current leading role; its base entity ID is at `+0x10`, dead flag at `+0x40`. |
| `AttributeComponent.SetBaseValue_Float/Int` | Match `BaseComponent.ownerInstanceId` at `+0x30` to the current player. `AttrName` 1–5 map to satiety, morale, stamina, health and vitality; maxima are 101–105. Preserve the native setter and use the corresponding total-value API. |
| `BagComponent.GetMaxBurden` | Match the component's owner ID before changing the computed limit. Shared trivial property getters are deliberately left alone. |
| `GameTimeManager.IsClockFrozen` | Boolean at `+0x44`; capture/restore against object identity and value ownership. |
| `LeadingRole.GmSetGold` | Native explicit balance setter, including the game's gold notification. |
| `SurvivalResultsManager.AddSurvivalPoint` | Native additive award; current points at `+0x30` and lifetime total at `+0x34` both guard against integer overflow. |
| `ProficiencyManager.AddExp` | Hook the seven-argument overload with its output parameter; only the current world's manager receives the multiplier. |

Commands require the current process-start timestamp/session, version/schema, a bounded request ID and a five-second deadline. File writes are atomic. A client lease expires after five seconds; effects stop and the prior clock is restored on the next valid game frame. Runtime operations serialize, and numeric grants execute once on the game thread. Installation uses checksummed owned-file receipts and rollback, preserving foreign/modified files. Only the DLL and third-party notices are installed under `HappyCheatsSurvivalLog/`; there is no autoload proxy or separate overlay.

Offline tests cover module metadata, exact process/session/heartbeat validation, numeric bounds, Off while loading, acknowledgements, compatibility rejection, ownership and installation rollback. Native fixtures cover player-only stat/carry effects, XP saturation, expired grants, player replacement, clock restoration/conflicting writes and all six actual hook prologues with MinHook in synthetic executable memory. Shared menu/controller tests include every catalog game.

**Live startup, gameplay effects, physical controller use and cross-PC game execution are unverified. No game was launched and no saves were changed during validation.** Infinite items, free crafting, instant growth/cooking, power cheats and unlock/achievement edits are outside this initial package because their specific mutation paths were not validated.

## 中文使用说明

支持 Steam 生存日志版本 **26615062**，独立修改器包 **1.0.2**，原生修改器 **0.1.2**。0.3.70及更新客户端请关闭游戏后在修改器库排序旁点击“更新”，直接添加或更新修改器。0.3.68/0.3.69 会隐藏尚未加入库的新游戏；首次安装请关闭客户端及受支持游戏，在源代码目录用 PowerShell 7 运行 `scripts/Add-SurvivalLog.ps1`（需.NET 8）。助手查找兼容的官方已解包客户端，校验签名并安装游戏文件；也可用 `-ClientDirectory` 指定客户端目录。重新打开原EXE即可看到生存日志，之后使用修改器库排序旁的“更新”。自行启动游戏并载入角色后，点击“连接修改器”（F8）。安装及扫描不会自动启动游戏或注入。

F1–F5 保持饱食度、士气、体力、健康、生命满值，效果在对应属性下次更新时生效。F6 冻结游戏时钟，计时活动也可能暂停；关闭后仅在仍持有该设置时恢复原值。F7 提高玩家负重上限，不改变背包格数。数值控件可一次设置金钱、增加生存点数，以及调整技能经验倍率。金钱、生存点和获得的经验可能被游戏保存，关闭功能不会撤销既有收益。

加载或更换角色后由共享客户端重新发送已请求的开关；金钱与生存点不会重复发放。关闭功能和“全部禁用”会取消待恢复请求。经验倍率在加载或断连后恢复1倍。Ctrl+Del 或按住 Xbox Menu 350毫秒后轻按 RB 打开共享菜单，使用鼠标键盘操作。所有验证均为离线或模拟检查，尚未验证实际启动、游戏效果和实体手柄。

Current compatibility review (13 November 2026): all 11 typed method signatures were relocated to Steam build **26615062** and matched x64 function-entry records and fresh 24-byte code sentinels. The 12 inspected types retain their previous fields, offsets and enum values, including the player, clock, points and XP manager paths. All six hook prologues were rebuilt and tested in synthetic executable memory. This supersedes the Survival Log build in the [9 November review](compatibility-20261109.md).
