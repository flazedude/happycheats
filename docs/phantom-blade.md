# Phantom Blade Zero

Current independent package **1.0.3** / trainer **0.1.6** targets Steam **26367565** and requires HappyCheats **0.3.69+**. This independent update does not change the client version or EXE. Eleven controls use the existing shared menu and module command paths. See the [expansion evidence](phantom-blade-expansion.md) and [release notes](cheats/phantom-blade-zero/1.0.3.en.txt). Its exact caller is `0x164B8F6`, object array `0xDE28C50`, allocation `0x164D080`, SHA-256 `4415933ce33476f14c837abb61a1b60af8b78cd81783896501913a7aac69e423`. See the [current loader static review](compatibility-20261109.md). Earlier build numbers, addresses and live observations below are retained historical evidence, not current-build gameplay validation.

AppID **4115450** uses Unreal Engine **5.7.4**. The module pins the actual `platform/steam/PBZ.exe` SHA-256; the small `steam/PBZ.exe` is a launcher and is not a menu or trainer target. Different executable hashes require a compatibility review.

## Controls

| Control | Input | Behavior |
| --- | --- | --- |
| Unlimited health | F1 or toggle | Uses the game's own health recovery function for the living local player, checking its result and the restored health values. A lethal hit between refills can still kill the player. |
| Unlimited stamina (Sha-Chi) | F2 or toggle | Periodically replenishes the native `Shachi` resource to `Shachi_Max`. The game's native `GetAnger`/`GetMaxAnger` getters read these fields. |
| Damage multiplier (attack power) | Numeric control, 1–20× | Scales the player's attack attribute. Final damage still follows the game's attack definitions, amplification and resistance rules. This does not promise every damage source scales equally. |
| Player damage guard | Toggle | Disables the player's ordinary actor damage gate. Scripted or ability damage may bypass it; this is not complete invulnerability. |
| Restore health now | Action | Performs one verified health recovery for the living local player. |
| Refill Sha-Chi now | Action | Performs one Sha-Chi refill for the living local player. |
| Player actor speed | Numeric control, 1–5× | Scales the player's actor time dilation; world speed is unchanged. |
| Sha-Chi regeneration | Numeric control, 1–10× | Scales the owned regeneration attribute's current value. Stat recalculation may cancel the override. |
| Movement speed limit | Numeric control, 1–5× | Scales the owned movement component's walking-speed limit. Game movement logic may bypass it. |
| Jump velocity | Numeric control, 1–5× | Scales the owned movement component's jump velocity. Game-specific jump animations may bypass it. |
| Reduced gravity | Numeric control, 1–5× reduction | Divides the owned movement component's gravity scale by the selected factor. |
| Disable all | F10 or button | Cancels requested restoration, stops refills, and restores reversible changes only while the trainer still owns their applied values. |

Numeric changes stay inactive until applied; applying 1× requests restoration. All controls remain clickable, with operation-time errors for unavailable targets. New client processes start with no remembered requests. Requested toggle states survive loads and player/session changes through the existing client command path, after the new session is connected, ready and the feature is available. Explicit Off and **Disable all** cancel pending restoration. The two manual refill actions are never replayed. Normal stat recalculation, equipment changes or other gameplay effects may cancel attack and regeneration overrides. Resource refills are not undone when disabled.

Research used [WeMod's 50-option page](https://www.wemod.com/cheats/phantom-blade-zero-trainers) and [Cheat Happens' 53-option page](https://www.cheathappens.com/83549-PC-Phantom-Blade-Zero-trainer). The implemented eleven controls above are grounded in this game's reflected bindings; the reference lists do not establish support for every option in this trainer.

## Installation and shared menu

Trainer **0.1.1** corrects the object-deletion listener layout that caused a fatal error before Lua startup in 0.1.0. The loader now uses the inspected object array directly because its hash-table self-test found incompatible game layouts. See [crash analysis](phantom-blade-loader.md).

Trainer **0.1.2** reads the local player's reflected possession links directly, removing repeated `IsLocalController`, `K2_GetPawn`, `IsPlayerControlled` and `GetController` calls. Unrelated controllers no longer trigger an arbitrary 16-controller rejection. Local-player identity, reciprocal possession, player class, current world/session and owned attributes are still checked. A failed detection now names the failed stage in the client and shared menu; bounded technical details remain in the status values.

Trainer **0.1.3** replaces the health-only raw `CurrentValue` write with the game's targeted `ResetHealthToMax` function. This updates the underlying health and its normal notifications. A full current value alone no longer counts as a successful refill. Failed recovery stops the health toggle and explains the failure; choose **Disable all** before retrying a rejected or unconfirmed recovery. Sha-Chi and attack behavior are unchanged.

Close the game and use Library **Update** to download, verify and install the independent package in one operation. **Install / update** remains available for ordinary setup or repair. The package includes the pinned UE4SS loader and the Lua trainer; unrelated or modified mod files are preserved and can block installation. A fresh matching trainer heartbeat and a validated local player are checked when an operation needs them. Controls remain clickable and report actionable failures.

Use the existing [shared menu](in-game-menu.md): **Ctrl+Del**, or enable **Xbox shortcuts**, hold **Menu (☰) for 350 ms**, then tap **RB**. Mouse and keyboard operate the controls. Keep HappyCheats open with this game selected and use windowed/borderless mode; exclusive fullscreen and controller row navigation are not supported. No game-specific controller profile is needed.

## Validation

Health uses a reflected native function with the validated player passed explicitly. Sha-Chi, attack and regeneration use reflected current-value writes while preserving their base values, as documented in [binding evidence](phantom-blade-bindings.md) and [expansion evidence](phantom-blade-expansion.md). Other added controls use reflected properties on the owned pawn or movement component, with readback and conditional restoration. Lua contains no hard-coded game offsets or save-file editor. Offline tests cover command/session gates, installation ownership, recovery failures, resource/player checks, numeric controls and shared-menu/controller routing. No game was launched or attached and no save was changed for 1.0.3; actual effects remain unverified.

On 31 October 2026, the 0.1.1 log and fresh heartbeat confirmed loader/Lua startup; a retained traceback identified `K2_GetPawn` member lookup failing before invocation. The 0.1.2 property path subsequently reached readiness in the user's running game. The user then reported ineffective health recovery despite enabled/full-health telemetry. Static analysis confirms the old direct write bypassed the game's attribute base, aggregation and notifications. The revised 0.1.3 health path has native-code and offline fixture evidence; live recovery still needs confirmation after restart. Physical-controller input and display over real gameplay remain unverified. These historical observations do not establish 0.1.6 gameplay validation.

## 中文

独立包 **1.0.2** / 修改器 **0.1.5**，面向 Steam 构建 **26367565**，需要 HappyCheats **0.3.69+**；当前 **0.3.71** 客户端及 EXE 不变。保留持续恢复生命、持续恢复杀气和 1–20 倍攻击力；新增玩家伤害防护、单次恢复生命/杀气、玩家动作速度、杀气恢复倍率、移动速度上限、跳跃初速度和降低重力，共 11 项。玩家动作/移动/跳跃及重力控制范围为 1–5 倍，杀气恢复为 1–10 倍；动作速度不改变世界速度。

伤害防护并非完全无敌，游戏脚本或技能可能绕过；移动逻辑及动画也可能绕过移动设置。属性重算可能取消攻击力或杀气恢复倍率。设为 1 或“关闭全部”（F10）会请求恢复仍由修改器持有的原值。开关请求可通过现有客户端路径在载入后的新会话就绪时恢复；明确关闭或“关闭全部”会取消待恢复请求，单次恢复操作不会重放。

关闭游戏后，在游戏库排序旁选择“更新”，一次完成下载、校验及安装。Ctrl+Del 或按住 Menu 350 毫秒后点按 RB 可开关统一菜单；菜单使用鼠标与键盘操作，支持窗口 / 无边框模式。控件始终可点击，不可用时显示具体操作错误；新启动的客户端不会记住上次请求。本次仅进行静态/元数据及离线模拟验证，未启动或连接游戏、未改存档；实际效果及实体手柄操作仍未验证。

0.1.2 直接读取已验证的本地玩家控制关系，移除重复的原生函数检查及最多 16 个控制器的任意限制。已观察到 0.1.2 成功识别玩家，但用户报告生命并未真正恢复。0.1.3 改为调用游戏自身的生命恢复函数，同时检查结果和恢复后的数值；失败时关闭该开关并显示原因。被拒绝或无法确认的恢复需先选择“关闭全部”再重试。杀气和攻击倍率行为保持不变；新版实际恢复效果仍待重启后验证。
