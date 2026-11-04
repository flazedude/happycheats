# Builder Simulator

Independent package **1.0.0** supports the inspected Steam Windows build **26240179**, game **2.3.1**, app **1120320**, Unity Mono x64. The HappyCheats client and standalone EXE stay at their existing version. Unknown game assemblies are rejected with an update/install error.

## Controls

| Key | Control | Behavior |
| --- | --- | --- |
| F1 | Unlimited money / 无限金钱 | A virtual spendable balance of at least 100,000,000; cash writes freeze while enabled. Off restores the current in-memory original balance. Saves made while enabled can retain the displayed amount. |
| F2 | Max skill level / 技能满级 | Upgrade every loaded skill to level 3 through the game's level-up method. One-shot progression; Disable all does not undo it. |
| F3 | Materials never decrease / 材料不减 | Keep already owned construction materials and furniture when consumed. Buy items first; DLC ownership checks remain. |
| F4 | Mixes never decrease / 混合料不减 | Keep existing concrete, mortar or sand in either supported wheelbarrow while using it. Fill first. Deliberate emptying remains available. |
| F5 | Super walk speed / 超级行走速度 | Toggle 2×, 3× or 5× movement; default 3×. |
| F6 | Super jump / 超级跳跃 | Toggle 2×, 3× or 5× jump height; default 3×. |
| F7 | Game speed / 游戏速度 | Toggle 0.5×, 1.5×, 2× or 3× simulation time; default 2×. Preserve a zero-time pause and restore trainer-owned time changes on Off. |
| Client / menu | Disable all / 全部关闭 | Stop continuous effects and cancel pending toggle restoration. |

Multipliers are selected in the client or shared in-game menu; selecting one does not turn its effect on. The existing shared client keeps toggle requests through site loads and player/equipment/profile replacement, then restore through the normal action path after readiness checks. Confirmed effects clear during loading; the shared client displays pending requests separately. Explicit Off and Disable all cancel pending requests. Skill upgrades never replay automatically. The payload clears confirmed effects on every context boundary and waits for fresh module commands. A new client process has no remembered requests.

Use **Ctrl+Del**, or enable the common Xbox shortcuts setting and **hold Menu (☰) for 350 ms, then tap RB**, to toggle the shared menu. Mouse/keyboard operate its rows. Use windowed/borderless mode; exclusive fullscreen and physical-controller operation have not been validated.

## Installation

For first-time addition to the unchanged official client, close HappyCheats and supported games, then double-click `scripts/Install-BuilderSimulator.cmd` (or run `scripts/Add-BuilderSimulator.ps1` with PowerShell 7). It finds an existing official unpacked client (0.3.69 or newer), verifies the established trust identity, obtains the signed game package, and installs its trainer through that downloaded module. Reopen the same HappyCheats EXE and choose Builder Simulator. The helper does not launch or close games and does not edit saves. An unpacked client folder can be specified with `-ClientDirectory`.

Once added, **Library → Update** downloads, verifies, loads and installs later compatible game packages in one operation. Use **Install / update** for ordinary current-package installation/repair. Existing unrelated or modified loader/plugin files are preserved; a conflict produces an error rather than an overwrite. Removal deletes only the verified owned trainer plugin and retains the shared BepInEx loader.

## Research and validation

Checked on 4 November 2026:

- [WeMod's Builder Simulator page](https://www.wemod.com/cheats/builder-simulator-trainers) lists unlimited money, maximum skill level, materials never decrease, and mixes never decrease.
- [Cheat Happens' Builder Simulator page](https://www.cheathappens.com/77400-PC-Builder-Simulator-trainer) lists super walk speed, super jump, unlimited money, and game speed. Its listed trainer is dated 12 June 2022, for the original Steam version.

These seven distinct options define the feature scope. This is an independent implementation against the installed game; no competitor trainer code or binaries are used. Website feature lists do not establish that their trainers support this newer build.

Read-only metadata and method inspection pins Assembly-CSharp SHA-256 `364320218449c22db4a6e851276f4236cdc51dc74fcbf8eba4afb7fdfc1556f0`. `tools/InspectBuilderSimulator/verified-build.json` records the inspected contracts. The payload compiles against local game references without distributing them. Offline tests cover installer rollback/conflict/ownership rules, command/session validation, acknowledgements and replay rejection, consumption patches, multipliers, pause/time ownership, toggle restoration, shared menu controls, and synthetic Menu+RB input.

**No game was launched and no save was changed for validation.** Actual Harmony patch application inside Unity, gameplay effects, physical-controller behavior and cross-game UI focus remain untested. Skill upgrades and construction done by the player can subsequently be saved by the game normally.

## 中文

独立包 **1.0.0** 支持 Steam 构建 **26240179**（游戏 **2.3.1**）。首次请关闭客户端及支持的游戏，用 PowerShell 7 运行 `scripts/Add-BuilderSimulator.ps1`，完成签名验证和安装后重新打开原有 EXE。后续通过**修改器库 → 更新**一次完成下载、验证、加载和安装；客户端版本及EXE不变。

支持无限金钱、技能满级、材料不减、混合料不减、超级行走速度、超级跳跃和游戏速度。材料需要先拥有，手推车需要先装料；仍可手动清空。技能满级为一次性成长操作，不由“全部关闭”撤销，也不会在载入后自动重放。开关请求跨载入保留，等待新场景就绪后恢复；关闭或全部关闭可取消等待恢复。

使用 Ctrl+Del 或按住 Menu 350 毫秒再点按 RB 打开统一菜单，使用鼠标键盘操作。已完成离线编译、静态绑定检查及合成回归；未启动游戏、未修改存档，尚未验证实际游戏效果、Unity内补丁加载或实体手柄。
