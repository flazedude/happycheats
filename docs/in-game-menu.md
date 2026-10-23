# Shared in-game menu

All games in the HappyCheats library use the same companion menu. Select the game in HappyCheats, launch it normally, and press **Ctrl+Del** to show or hide the menu. With **Xbox shortcuts** enabled, an Xbox/XInput controller can do the same: **hold Menu (☰) for 350 ms, then tap RB**. The **In-game menu** button on every game page also toggles it. Keep HappyCheats running with that game selected.

From HappyCheats **0.3.40**, the controller chord and the Xbox shortcuts setting apply to every current and future game. Menu+RB only shows or hides the menu; use a mouse and keyboard to operate its controls. It does not add controller navigation through menu rows.

Use **windowed or borderless** display mode. This is a Windows companion window; exclusive fullscreen is not supported. It attaches only when one matching game process can be identified in the selected installation. It hides when the game is minimized, another application takes focus, the selected game changes, or the client opens Updates.

The menu provides the game's registered toggles, one-shot actions, numeric values and choices, including Astroneer's independent 10× / 20× / 100× collection and 3× / 5× / 10× run-speed selectors, plus invulnerability. Groups organize larger lists. Chinese/English and light/dark settings follow the client. Polling retains the controls and any unfinished numeric entry. Existing connection, session and feature availability checks still apply; opening the menu does not connect or enable a cheat.

Clicking the menu gives it keyboard focus so you can type values and navigate its controls. Close it, press Ctrl+Del, or use Menu+RB again to return to the game. Some games pause when focus moves away; a command may wait for gameplay to resume. You can close the menu while a command is pending. Input can also reach the game; Menu may open its pause menu.

Existing trainer hotkeys and native menus remain available. Onimusha's **Menu+RB** now toggles the shared menu; **F6** still toggles its native status panel. Its direct cheat chords are unchanged. Dawnwalker's **F6** also opens the shared menu, and its direct cheat chords are unchanged. Its three controller shortcut pages remain selectable with **Menu+View**; current mappings appear on the client page and in the shared menu. Controller shortcut pages and menu control groups remain independent.

## Adding a game

Register the new `IGameModule` in `ModuleCatalog` and provide `GameExecutables`: exact executable paths relative to the discovered installation. List supported renderer/launcher variants where needed. The locator validates those paths, checks the running executable and refuses ambiguous matches. An empty list leaves the menu unable to attach, so every catalog module must provide target metadata; the catalog regression check enforces this.

Declare every control in `GameDefinition.Features`, including its group, hotkey, toggle flag and optional `FeatureNumber` or `FeatureChoice` metadata. Implement `IFeatureValueModule` for numeric controls and `IFeatureChoiceModule` for selections. Report confirmed state and feature availability through `TrainerStatus`. The shared menu uses those existing models and dispatch paths automatically; do not add a renderer or a game-ID branch for a new game.

The shared client also supplies Ctrl+Del and the Xbox/XInput Menu+RB chord automatically. Reserve **hold Menu (☰) for 350 ms, then tap RB** for showing/hiding the shared menu in every new module. Respect the common Xbox shortcuts setting. Game-specific direct cheat chords may supplement this convention, but must not replace it or send a native HUD command for Menu+RB. The shared chord is a toggle, not controller navigation.

`GameOverlayChecks` exercises all catalog entries and an unknown future-game fixture, retained numeric drafts, command routing, connection and busy gates, themes/languages, bounds, targeting ambiguity and shortcut edges without launching games. These checks do not establish gameplay effects, physical-controller operation or rendering over each actual game.

## 中文说明

在 HappyCheats 中选择游戏，正常启动游戏，然后按 **Ctrl+Del** 显示或隐藏统一菜单。启用“Xbox 手柄快捷键”后，也可使用 Xbox/XInput 手柄：**按住 Menu（☰）350 毫秒，再点按 RB**。每个游戏页面也有“游戏内菜单”按钮。请保持客户端运行，并保持该游戏页面被选中。

从 **0.3.40** 起，这一手柄组合键及“Xbox 手柄快捷键”设置适用于所有现有及未来游戏。Menu+RB 只负责显示或隐藏菜单；菜单中的功能仍使用鼠标和键盘操作，没有新增手柄菜单导航。

支持**窗口 / 无边框模式**，不支持独占全屏。点击菜单后可以输入数值；关闭菜单、再次按 Ctrl+Del 或使用 Menu+RB 返回游戏。部分游戏失去焦点后会暂停，指令可能需要返回游戏后才能完成。菜单不会自动开启功能，也不会绕过连接及可用性检查。按键也可能传递给游戏，Menu 可能同时打开游戏的暂停菜单。

菜单包含各游戏登记的开关、单次操作、数值和选项，跟随客户端的中英文与明暗主题设置。切换到其他应用、最小化游戏或更换选中的游戏时，菜单自动隐藏。鬼武者的 Menu+RB 现改为切换统一菜单，F6 仍切换原生状态面板；鬼武者和黎明行者之血原有的直接功能组合键保持不变。黎明行者之血也可按 F6 切换统一菜单，按 Menu+View 切换三页手柄快捷键，当前映射显示在客户端页面和统一菜单中。手柄快捷键页与菜单功能分组相互独立。尚未在所有实际游戏中验证显示与操作效果，也未完成实体手柄验证。

新增游戏应沿用客户端统一的 Ctrl+Del 和 Menu+RB（先按住 Menu 350 毫秒）约定，并遵循“Xbox 手柄快捷键”设置。Menu+RB 保留给统一菜单的显示 / 隐藏操作，不应被新模块改为原生状态面板指令。可另行添加游戏专属的直接功能组合键；统一菜单中的操作仍使用鼠标和键盘。
