# The Witcher 3: Wild Hunt — Remastered

HappyCheats includes an experimental trainer for the Windows Steam edition of [The Witcher 3: Wild Hunt — Remastered, app 292030](https://store.steampowered.com/app/292030/). The target is the inspected Steam build **25575366**, executable **5.0.0.1041720**, using **DirectX 12**. The older 4.x edition and DirectX 11 executable are unsupported.

The trainer is included in the full client update package and complete standalone EXE. A real ready heartbeat from the user's running game confirms script startup. Actual cheat effects remain unverified.

## Setup

1. Close The Witcher 3.
2. Select **The Witcher 3: Wild Hunt — Remastered** in HappyCheats. If Steam discovery does not locate it, choose the game installation root.
3. Choose **Install / update**. Installation verifies the target game and installs the bundled script mod. A conflicting override of the game's `r4game.ws` script is rejected; existing files are preserved. Disable or remove the conflicting override before retrying installation.
4. Launch the supported DirectX 12 game and load a save with Geralt. Wait for a fresh trainer connection before using the controls.
5. Keep HappyCheats open on the Witcher page while continuous effects are enabled. If the client stops renewing its connection, continuous effects turn off after approximately six seconds.

HappyCheats **0.3.34** and trainer **0.1.1** correct the command-file location and remove the foreground-window prerequisite. With the game closed, choose **Install / update** once. Commands are written immediately when clicked, and confirmations are accepted even after switching back to HappyCheats. If switching windows pauses the game, resume gameplay to apply the queued command. The client waits at most 60 wall-clock seconds or eight advancing game seconds and never automatically repeats an unacknowledged action.

The payload is a script mod; it does not replace the game's base files. Its two owned CSV communication files live in a new `content/content0/happycheats_witcher3/` directory because the native direct CSV reader uses that physical root. Its generated `r4game.ws` override can conflict with other mods that override the same script. HappyCheats does not silently merge or overwrite those mods. **Remove trainer**, with the game closed, removes owned unchanged files while preserving unrelated or modified files.

## Controls

Use the buttons or the listed HappyCheats shortcuts while the Witcher page is selected and the trainer is ready.

| Key | Control | Behavior |
| --- | --- | --- |
| F1 | Infinite health | Repeatedly refill living Geralt's health. A lethal hit between updates can still kill him. |
| F2 | Infinite stamina | Repeatedly refill Geralt's stamina. |
| F3 | Full adrenaline | Repeatedly fill Geralt's adrenaline. |
| F4 | Clear potion toxicity | Repeatedly clear Geralt's potion toxicity; decoction toxicity is retained. |
| F5 | Refill health and stamina | Restore health and stamina once; does not revive a dead character. |
| F6 | Add 1,000 crowns | Add 1,000 crowns once. |
| F7 | Add 1,000 base XP | Grant 1,000 base experience through game rules. New Game+ modifiers and the level cap apply. |
| F8 | Add 1 skill point | Add one skill point once. |

Continuous effects begin disabled and are bound to the current game connection. **Disable all** stops continuous effects. Money, XP and skill-point grants may persist through normal game saves; turning off or removing the trainer does not undo them. The installer does not edit save files.

The XP control requests a base grant, so qualifying New Game+ characters may receive a larger amount. At the game's level cap the grant can be rejected instead of reporting an increase. The toxicity control does not remove active decoctions or their reserved toxicity.

## Validation and limitations

The offline checks cover package integrity, owned-file installation/removal, rejection of incompatible builds and conflicting scripts, command/session handling, and the English/Chinese client controls. They use temporary fixtures and do not start the installed game. The UI captures use simulated trainer status, not gameplay.

The existing script has produced a real ready heartbeat in a loaded game. Resource changes, scene transitions, resumed command acknowledgement and save persistence still require gameplay verification. A successful package build or client smoke check does not establish those results. Other game versions, DirectX 11 and other character states are outside this initial target.

Developer commands, run from PowerShell 7:

```powershell
.\scripts\Build-Witcher3.ps1
.\scripts\Test-Witcher3.ps1
dotnet run --project .\tests\HappyCheats.App.Tests -c Release -- --data-root .
```

## 中文说明

此实验性修改器面向 Steam《巫师 3：狂猎 重制版》，对应构建 **25575366**、程序 **5.0.0.1041720** 的 **DirectX 12** 版本；不支持旧版 4.x 或 DirectX 11。完整客户端更新包与单文件 EXE 均包含此修改器。

请关闭游戏，在 HappyCheats 中选择本游戏并点击 **安装 / 更新**。安装程序会校验版本，并保留游戏基础文件。如果其他模组覆盖了 `r4game.ws`，安装会拒绝冲突，不会自动合并或覆盖；请先禁用或移除冲突的覆盖文件再重试。之后启动 DirectX 12 版游戏并载入杰洛特存档，等待连接就绪。

**F1 无限生命、F2 无限活力、F3 满肾上腺素、F4 清除魔药毒性、F5 恢复生命与活力、F6 增加 1,000 克朗、F7 增加 1,000 基础经验、F8 增加 1 技能点。** 持续功能需要保持客户端的巫师页面打开；连接中断约六秒后自动关闭。无限生命为周期性补满，两次恢复之间受到致命伤害仍可能死亡。清除毒性保留煎药的毒性与效果；经验增加受新游戏+倍率和等级上限影响，达到上限时可能被拒绝。金钱和成长变化可能随正常存档保存，关闭或移除修改器不会撤销这些变化。

已观察到用户运行中的游戏产生实际就绪心跳，确认现有脚本已加载。0.3.34 客户端与 0.1.1 修改器修正指令文件目录，并移除前台窗口要求。请关闭游戏后点击一次“安装 / 更新”。点击功能立即写入指令，切回客户端仍可接受游戏确认；游戏因切换窗口暂停时，返回实际游玩即可处理指令。未编辑游戏暂停设置或存档；作弊效果、恢复后的实时确认、场景切换和保存结果仍待验证。
