# Phantom Blade Zero

Experimental trainer 0.1.3 for the inspected Steam build **26266538**, AppID **4115450**, executable version **CL-301524_JKS-17219** (Unreal Engine 5.7.4). The module pins the actual `platform/steam/PBZ.exe` SHA-256; the small `steam/PBZ.exe` is a launcher and is not a menu or trainer target. Different executable hashes require a compatibility review.

## Controls

| Control | Input | Behavior |
| --- | --- | --- |
| Unlimited health | F1 or toggle | Uses the game's own health recovery function for the living local player, checking its result and the restored health values. A lethal hit between refills can still kill the player. |
| Unlimited stamina (Sha-Chi) | F2 or toggle | Periodically replenishes the native `Shachi` resource to `Shachi_Max`. The game's native `GetAnger`/`GetMaxAnger` getters read these fields. |
| Damage multiplier (attack power) | Numeric control, 1–20× | Scales the player's attack attribute. Final damage still follows the game's attack definitions, amplification and resistance rules. This does not promise every damage source scales equally. |
| Disable all | F10 or button | Stops refills and restores a damage change only while the trainer still owns the applied value. |

Damage defaults to 2× in the editor and stays inactive until applied. Applying 1× requests restoration. Use **Disable all** when the player is temporarily unavailable and numeric controls are disabled. All controls start off; player/session changes reset them. Normal stat recalculation, equipment changes or other gameplay effects may cancel the attack multiplier, requiring it to be applied again. Resource refills are not undone when disabled.

## Installation and shared menu

Trainer **0.1.1** corrects the object-deletion listener layout that caused a fatal error before Lua startup in 0.1.0. The loader now uses the inspected object array directly because its hash-table self-test found incompatible game layouts. See [crash analysis](phantom-blade-loader.md).

Trainer **0.1.2** reads the local player's reflected possession links directly, removing repeated `IsLocalController`, `K2_GetPawn`, `IsPlayerControlled` and `GetController` calls. Unrelated controllers no longer trigger an arbitrary 16-controller rejection. Local-player identity, reciprocal possession, player class, current world/session and owned attributes are still checked. A failed detection now names the failed stage in the client and shared menu; bounded technical details remain in the status values.

Trainer **0.1.3** replaces the health-only raw `CurrentValue` write with the game's targeted `ResetHealthToMax` function. This updates the underlying health and its normal notifications. A full current value alone no longer counts as a successful refill. Failed recovery stops the health toggle and explains the failure; choose **Disable all** before retrying a rejected or unconfirmed recovery. Sha-Chi and attack behavior are unchanged.

Close the game and select **Install / update** in HappyCheats. The package includes the pinned UE4SS loader and the Lua trainer; unrelated or modified mod files are preserved and can block installation. Start the game through Steam and load a save. A fresh matching trainer heartbeat and a validated local player are required before controls become available.

Use the existing [shared menu](in-game-menu.md): **Ctrl+Del**, or enable **Xbox shortcuts**, hold **Menu (☰) for 350 ms**, then tap **RB**. Mouse and keyboard operate the controls. Keep HappyCheats open with this game selected and use windowed/borderless mode; exclusive fullscreen and controller row navigation are not supported. No game-specific controller profile is needed.

## Validation

Health uses a reflected native function with the validated player passed explicitly. Sha-Chi and attack retain reflected current-value writes while preserving their base values, as documented in [binding evidence](phantom-blade-bindings.md). Lua contains no hard-coded game offsets or save-file editor. Offline tests cover command/session gates, installation ownership, recovery failures, resource/player checks, numeric controls and shared-menu/controller routing.

On 31 October 2026, the 0.1.1 log and fresh heartbeat confirmed loader/Lua startup; a retained traceback identified `K2_GetPawn` member lookup failing before invocation. The 0.1.2 property path subsequently reached readiness in the user's running game. The user then reported ineffective health recovery despite enabled/full-health telemetry. Static analysis confirms the old direct write bypassed the game's attribute base, aggregation and notifications. The revised 0.1.3 health path has native-code and offline fixture evidence; live recovery still needs confirmation after restart. Physical-controller input and display over real gameplay remain unverified. Both complete distributions include the rebuilt payload.

## 中文

面向 Steam 构建 **26266538** 的实验性修改器。F1 持续恢复生命，F2 持续恢复杀气（Sha-Chi）；伤害倍率通过数值框设置为 1–20 倍，影响攻击力属性，最终伤害仍遵循游戏自身规则。设为 1 或“关闭全部”（F10）可请求恢复仍由修改器持有的原值。游戏重新计算属性时可能取消攻击力倍率，需要重新应用。持续恢复并非无敌，致命伤害仍可能导致死亡。

关闭游戏后选择“安装 / 更新”，再通过 Steam 启动并载入存档。Ctrl+Del 或按住 Menu 350 毫秒后点按 RB 可开关统一菜单；菜单使用鼠标与键盘操作，支持窗口 / 无边框模式。所有开关初始关闭，尚未验证实际修改效果或实体手柄操作。

0.1.2 直接读取已验证的本地玩家控制关系，移除重复的原生函数检查及最多 16 个控制器的任意限制。已观察到 0.1.2 成功识别玩家，但用户报告生命并未真正恢复。0.1.3 改为调用游戏自身的生命恢复函数，同时检查结果和恢复后的数值；失败时关闭该开关并显示原因。被拒绝或无法确认的恢复需先选择“关闭全部”再重试。杀气和攻击倍率行为保持不变；新版实际恢复效果仍待重启后验证。
