# Phantom Blade Zero

Experimental trainer 0.1.1 for the inspected Steam build **26266538**, AppID **4115450**, executable version **CL-301524_JKS-17219** (Unreal Engine 5.7.4). The module pins the actual `platform/steam/PBZ.exe` SHA-256; the small `steam/PBZ.exe` is a launcher and is not a menu or trainer target. Different executable hashes require a compatibility review.

## Controls

| Control | Input | Behavior |
| --- | --- | --- |
| Unlimited health | F1 or toggle | Periodically replenishes a living local player's health. A lethal hit between refills can still kill the player. |
| Unlimited stamina (Sha-Chi) | F2 or toggle | Periodically replenishes the native `Shachi` resource to `Shachi_Max`. The game's native `GetAnger`/`GetMaxAnger` getters read these fields. |
| Damage multiplier (attack power) | Numeric control, 1–20× | Scales the player's attack attribute. Final damage still follows the game's attack definitions, amplification and resistance rules. This does not promise every damage source scales equally. |
| Disable all | F10 or button | Stops refills and restores a damage change only while the trainer still owns the applied value. |

Damage defaults to 2× in the editor and stays inactive until applied. Applying 1× requests restoration. Use **Disable all** when the player is temporarily unavailable and numeric controls are disabled. All controls start off; player/session changes reset them. Normal stat recalculation, equipment changes or other gameplay effects may cancel the attack multiplier, requiring it to be applied again. Resource refills are not undone when disabled.

## Installation and shared menu

Trainer **0.1.1** corrects the object-deletion listener layout that caused a fatal error before Lua startup in 0.1.0. The loader now uses the inspected object array directly because its hash-table self-test found incompatible game layouts. See [crash analysis](phantom-blade-loader.md).

Close the game and select **Install / update** in HappyCheats. The package includes the pinned UE4SS loader and the Lua trainer; unrelated or modified mod files are preserved and can block installation. Start the game through Steam and load a save. A fresh matching trainer heartbeat and a validated local player are required before controls become available.

Use the existing [shared menu](in-game-menu.md): **Ctrl+Del**, or enable **Xbox shortcuts**, hold **Menu (☰) for 350 ms**, then tap **RB**. Mouse and keyboard operate the controls. Keep HappyCheats open with this game selected and use windowed/borderless mode; exclusive fullscreen and controller row navigation are not supported. No game-specific controller profile is needed.

## Validation

The implementation writes reflected current-value fields on the local player's owned attribute set while preserving base values, as documented in [binding evidence](phantom-blade-bindings.md). It uses no hard-coded game offsets in the Lua bindings and includes no save-file editor. Offline tests cover command/session gates, installation ownership, restoration, resource/player checks, numeric controls and shared-menu/controller routing.

Loader startup, actual cheat effects, physical-controller input and display over real gameplay remain unverified. Read-only inspection of a running game is not cheat-effect validation. The complete payload is included in both the updater package and standalone EXE.

## 中文

面向 Steam 构建 **26266538** 的实验性修改器。F1 持续恢复生命，F2 持续恢复杀气（Sha-Chi）；伤害倍率通过数值框设置为 1–20 倍，影响攻击力属性，最终伤害仍遵循游戏自身规则。设为 1 或“关闭全部”（F10）可请求恢复仍由修改器持有的原值。游戏重新计算属性时可能取消攻击力倍率，需要重新应用。持续恢复并非无敌，致命伤害仍可能导致死亡。

关闭游戏后选择“安装 / 更新”，再通过 Steam 启动并载入存档。Ctrl+Del 或按住 Menu 350 毫秒后点按 RB 可开关统一菜单；菜单使用鼠标与键盘操作，支持窗口 / 无边框模式。所有开关初始关闭，尚未验证实际修改效果或实体手柄操作。
