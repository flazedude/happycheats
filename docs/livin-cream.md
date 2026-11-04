# Livin' the Cream: Ice Cream Simulator

Independent cheat package **1.0.0** supports Steam app **4621810**, inspected build **26415994**, on the already-released HappyCheats **0.3.69+** client. The minimum client requirement provides shared toggle restoration through loading; older clients receive an actionable compatibility error. It adds the game through the signed cheat catalog without publishing or rebuilding a client EXE.

Client **0.3.69** hides games absent from its Library in the update list. To add this game without changing that client EXE, close HappyCheats and the game, then run `scripts/Install-LivinCream.cmd` from this source checkout (PowerShell 7 and .NET 8 SDK required). Alternatively run `scripts/Install-LivinCream.ps1` in PowerShell 7; `-InspectOnly` checks prerequisites without writes, and `-ClientDirectory` / `-GameDirectory` select nondefault folders. The helper checks all three trusted catalogs, selects and verifies the exact signed package through the existing updater/store, loads that module and uses its owned game-file installer. It preserves custom update configurations and other games, and never replaces a client EXE or starts/closes a game. If only an older client has been run, use the existing official 0.3.69+ standalone EXE once and close it before setup.

Reopen your existing HappyCheats 0.3.69+ EXE to see the new Library game. Select it, launch normally and load a shop. Subsequent published versions use **Update** beside Library Sort and the game's normal **Update** action. Keep HappyCheats open to use its controls.

| Control | Behavior |
| --- | --- |
| Money | Set once to a whole number from 0 to 100,000,000 using the native money setter and UI notification. |
| Add shop XP | Grant 1–10,000 once using `LevelManager.AddXP`, retaining normal level-up notifications and rewards. |
| Add skill points | Grant 1–1,000 once using `SkillTreeManager.EarnSkillPoint`, retaining notifications and earned-point accounting. |
| Unlimited customer patience | Override the native patience rule for the loaded shop. Refresh its current customer's timer/UI. Off restores the native rule and starts a new full normal timer when the current play style uses patience. Does not change Classic/Cozy mode. |
| Unlimited soft-serve stock | Prevent consumption of existing soft-serve reservoir contents. Fill machines normally first. Does not create flavors, scoop pans, cones, toppings or other supplies. |
| Refill soft-serve stock | Refill nonempty soft-serve reservoirs once using `RestoreStock`, preserving their flavors and updating liquid visuals. Empty reservoirs stay empty. |

Money and progression grants change the loaded game state and may be retained by the game's normal saves. Turning off a toggle does not undo prior consumption prevention, refill, money or progression grants. The trainer does not invoke saving, edit save files, unlock purchases in bulk or run a separate renderer.

The game uses the existing shared **Ctrl+Del** menu and, with Xbox shortcuts enabled, **hold Menu (☰) for 350 ms then tap RB**. Menu controls use mouse/keyboard. Use windowed or borderless mode. No game-specific controller profile or native HUD is installed. Feature metadata includes English/Chinese labels because older clients cannot contain translations for this new game.

The runtime requires a single active player, their shop, one level manager and one skill manager after `ShopSaveManager.RestoreComplete`. Restore-pending, replacement player/shop/managers or replacement database/progress identities rotate the command session and clear confirmed runtime effects. The shared client retains requested toggles and restores them only through new, ready-session commands; one-shot grants are never replayed. Explicit Off/Disable all cancels requests through the common client path. A new game process starts with effects off.

Installation uses the existing pinned BepInEx **5.4.23.5 x64** dependency and a dedicated plugin. Exact game assembly SHA-256 is enforced at build, installation and runtime: `3756614fb3952a6bacf1e2e8d1994efd7eb451d7f27218a66b033e386e3cb98e`. Unsupported builds report an actionable error. Package hashes/ownership checks preserve foreign or modified mod files; removal leaves the shared loader and other mods in place. Private keys, extracted game code and game assemblies are not distributed in source or packages.

`tools/InspectLivinCream` reads assembly metadata/IL without executing the game. It checks the native money, XP/reward, skill, restore, patience/UI and reservoir methods against the pinned assembly. `tests/LivinCream.Checks` compiles the actual plugin/protocol source against synthetic engine/game fixtures to check mutations, readiness, load transitions, duplicate grants, expiry, overflow and restoration. Module/installer tests check package integrity, unsupported builds, metadata, ownership and offline error paths. The catalog-wide shared menu/controller suites include this game automatically and explicitly require its registration.

Validation is **offline/static/synthetic**. No game was launched, no saves were changed, and actual gameplay, Unity loader startup, exclusive fullscreen and physical controller behavior have not been validated. Native bindings are inspected; this is not a claim of live gameplay verification.

## 中文

独立修改器 **1.0.0** 支持 Steam 游戏 **4621810**、已检查版本 **26415994**，需要已发布的 HappyCheats **0.3.69+** 以支持读取后的统一开关恢复，旧客户端会显示兼容性错误；不会重新编译或替换客户端 EXE。0.3.69 的更新列表会隐藏尚未加入修改器库的游戏。首次添加时请关闭 HappyCheats 和游戏，从源码目录运行 `scripts/Install-LivinCream.cmd`（需要 PowerShell 7 和 .NET 8 SDK），通过原有签名目录、下载验证、模块缓存和文件归属安装流程完成添加。重新打开现有 0.3.69+ EXE 即可看到该游戏；后续版本可使用修改器库排序旁的正常“更新”。不会自动启动或关闭游戏。

提供设置金钱、单次增加店铺经验、单次增加技能点、顾客无限耐心、软冰淇淋原料不消耗、单次补满非空软冰淇淋机器。经验和技能点使用游戏原有方法，保留正常奖励与通知；补充原料保留原有口味，空机器保持为空。无限耐心不修改游戏模式，关闭后按原模式恢复完整计时。金钱、经验、技能点及补料可能被游戏正常存档保留；关闭开关不会撤销此前的单次操作。

沿用统一的 Ctrl+Del 菜单，以及启用 Xbox 快捷键时“按住 Menu 350 毫秒，再点按 RB”的菜单开关。菜单内使用鼠标/键盘，支持窗口/无边框。读取或更换店铺时，统一客户端仅在新会话就绪后恢复用户请求的开关，不重放金钱或经验等单次操作；关闭开关或禁用全部会取消待恢复请求。

已完成静态代码检查、真实绑定编译和合成回归测试。未启动游戏、未修改存档，未验证实际游戏效果、Unity 加载器启动或实体手柄。
