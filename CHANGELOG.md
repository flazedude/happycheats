# Changelog / 更新日志

**[Download the latest release / 下载最新版本](https://github.com/flazedude/happycheats/releases/latest)** · [Home](README.md) · [中文首页](README.zh-CN.md)

## Latest · 0.3.71

- Trainer updates show source/stage activity, download percentage/bytes, elapsed time and timestamped logs with Copy log.
- Installer output stays visible in the update window; package processing and installation run off the UI thread with throttled download notifications.
- Busy clicks, retryable failures, verified completion, English/Chinese controls and all 21 current payloads are preserved.

修改器更新显示阶段、进度、用时及可复制的时间戳日志，安装消息直接可见。离线/合成验证，未启动游戏或更改存档。

[English](docs/releases/0.3.71.en.txt) · [中文](docs/releases/0.3.71.zh.txt)
## Independent compatibility updates · 9 November 2026

- Builder Simulator 1.0.1, Livin' the Cream 1.0.3, Survival Log 1.0.1, Elden Ring 1.0.1, Crimson Desert 1.0.3 and Phantom Blade Zero 1.0.1 target the inspected installed builds. HappyCheats 0.3.70 and its complete EXE are unchanged.
- Wukong, Hotel Architect and Stardew Valley's installed-file warnings were repaired with existing payloads. GTA V Enhanced's changed packed executable remains unsupported pending verified binding evidence.
- [Build identities, checks and limitations](docs/compatibility-20261109.md). Validation is offline/static/synthetic; no games were started or saves changed.

六款游戏独立更新兼容性，客户端及完整EXE保持不变。另三款游戏通过现有安装器修复文件。GTA V增强版的新加壳程序仍待绑定证据验证。仅离线/静态/模拟检查，未启动游戏或更改存档。

## 0.3.70

- Library Update offers Add trainer for signed catalog games missing from this client's Library.
- New modules download, verify, load and install in one operation. Missing folders, running games and failed setup remain retryable.
- Complete distributions include all 21 games, including Livin' the Cream 1.0.1; independent package versions stay unchanged.

游戏库更新支持添加原先不存在的新修改器，保留目录缺失、游戏运行中和安装失败时的重试。完整包包含 21 款游戏及甜蜜冰淇淋 1.0.1。离线/合成验证，实际游戏效果尚未验证。

[English](docs/releases/0.3.70.en.txt) · [中文](docs/releases/0.3.70.zh.txt)

## 0.3.69

- Requested toggles survive loading/session changes across all trainer modules; pending requests are distinct from confirmed effects.
- Off and Disable all cancel restoration; one-shot actions are never replayed. Failures are reported and logged without endless retries.
- Dawnwalker 1.0.6 / payload 0.5.5 renews retired health/stamina locks and F4 refills both blood and stamina.

加载或会话变化后保留功能开启请求，验证新会话后恢复；关闭或全部关闭可取消，单次操作不会重复。黎明行者之血同时修复资源锁定失效，并合并持续血量与耐力恢复。实际新版游戏效果仍待验证。

[English](docs/releases/0.3.69.en.txt) · [中文](docs/releases/0.3.69.zh.txt)

## 0.3.68

- Trainer updates list only newer packages for games installed on this PC. Current trainers and missing games are omitted, including in focused checks.
- Verified installations leave the list; unfinished setup remains retryable. Empty lists explain that no installed games need updates.
- English/Chinese WPF and signed synthetic regressions; no gameplay or physical-controller validation.

修改器更新只显示本机已安装游戏的较新版本，不再列出已是最新的修改器或未安装的游戏。完成后自动移除，未完成的安装仍可重试。仅离线及合成验证。

[English](docs/releases/0.3.68.en.txt) · [中文](docs/releases/0.3.68.zh.txt)

## 0.3.67

- One-click per-game Update downloads, verifies, repairs, loads and installs the trainer. No separate Repair or Reload library action is needed.
- Verifies installation before completion, reports actionable errors and retries unfinished setup through the same action. Other game modules stay loaded.
- Complete distributions include all seventeen payloads, with the previously published Chinese Parents 0.1.1 and Crimson Desert 0.2.9 trainers. Offline/synthetic validation only.

按游戏一键完成下载、验证、修复、加载和安装，无需单独修复或重新加载修改器库。安装验证通过后才报告完成，失败可直接重试。仅离线及合成验证。

[English](docs/releases/0.3.67.en.txt) · [中文](docs/releases/0.3.67.zh.txt)

## 0.3.66

- Adds red per-game sidebar warnings for reported trainer compatibility/runtime mismatches and newer signed packages, with a game-specific update check.
- Checks signed trainer catalogs at startup and every 30 minutes; retains warnings for downloaded packages until Library reload. Client Updates stays separate.
- Offline/synthetic signed-package and WPF validation; no games launched or saves changed.

新增按游戏显示的红色版本 / 兼容性警告，点击即可检查该游戏的独立修改器更新。下载后保留重新加载提示；客户端更新保持独立。仅离线 / 合成验证。

[English](docs/releases/0.3.66.en.txt) · [中文](docs/releases/0.3.66.zh.txt)

## 0.3.65

- Adds independent signed per-game module/payload updates, including new games, through Update beside Library Sort. Client version stays unchanged for compatible cheat-only releases.
- Adds per-game package versions, reload without replacing the EXE, compatibility errors, repair and separate GitHub/LAN publishing/mirroring.
- Offline signed-package, WPF, shared-menu and HTTP validation; no gameplay or physical-controller claims.

新增独立游戏修改器更新、软件包版本与无须替换 EXE 的重新加载；客户端更新保持独立。仅进行离线及合成验证。

[English](docs/releases/0.3.65.en.txt) · [中文](docs/releases/0.3.65.zh.txt)

## 0.3.64

- Dawnwalker trainer 0.4.2 uses owned native health/stamina locks and a bounded, verified correction for scaled damage factors.
- Shared-menu native no-activation protection strengthens game-focus handling while retaining explicit numeric typing.
- Captured live diagnostics read-only; repaired gameplay and physical-controller behavior remain unverified. Close Dawnwalker and choose Install / update.

《黎明行者之血》改用原生资源锁定，修正超级伤害倍率确认，统一菜单加强焦点保护。实机新版效果尚未验证。

[English](docs/releases/0.3.64.en.txt) · [中文](docs/releases/0.3.64.zh.txt)

## 0.3.63

- Fixes Project Hospital's startup runtime-type failure and keeps its heartbeat connected while the game is backgrounded. Includes trainer 0.1.1; close the game and choose Install / update.
- Shared-menu button clicks and mouse navigation keep foreground focus with the game. Numeric text editing still needs keyboard focus; windowed/borderless support is unchanged.
- Adds startup diagnostics and actual-game runtime-reference validation. Live main-menu startup, background connection and a queued cleanup acknowledgement passed; hospital gameplay remains unverified.

修复《医院计划》启动及后台连接问题，统一菜单按钮不再夺取游戏焦点。更新客户端后关闭游戏并安装修改器 0.1.1。增加启动诊断及实际运行库引用检查，已验证主菜单连接和指令确认，未验证医院经营。

[English](docs/releases/0.3.63.en.txt) · [中文](docs/releases/0.3.63.zh.txt)

## 0.3.62

- All client and shared-menu controls remain clickable, including when disconnected, unready, unavailable or busy.
- Failed attempts show an error and write to `%LOCALAPPDATA%/HappyCheats/client-errors.log`. Existing operations report busy errors on repeated clicks.
- All 17 trainer payloads are unchanged. Offline catalog and synthetic dispatch checks cover this behavior; other-PC gameplay remains unverified.

所有客户端及共享菜单控件始终可点击，断开连接或忙碌时也不会置灰。失败时显示错误并写入本地日志；已有操作进行时重复点击会显示提示。全部 17 款游戏修改器未变，验证范围为离线及模拟测试。

[English](docs/releases/0.3.62.en.txt) · [中文](docs/releases/0.3.62.zh.txt)

## 0.3.61

- Automatically removes older completed update history, keeping the current installation and newest completed rollback.
- Preserves settings, trainer payloads, interrupted updates and files in use. No trainer reinstall is needed.
- Validated with offline regression fixtures and packaged-client preview checks; gameplay, physical controllers and a second PC were not validated.

自动清理较早且已完成的更新记录，保留当前安装和最近一次回退备份。设置和修改器内容保持不变，无需重新安装修改器。验证范围为离线回归测试及打包客户端预览检查，未验证实际游戏、实体手柄或第二台电脑。

## Release notes / 历史记录

This index preserves the project's release and build notes, including historical local builds. [GitHub Releases](https://github.com/flazedude/happycheats/releases) lists published downloads. Each note records the scope and limitations at that time.

此索引保留历史发行说明和本地构建记录；已公开的下载以 GitHub Releases 为准。各条记录中的验证范围仅对应当时版本。

| Version | Release / build notes | Language |
| --- | --- | --- |
| 0.3.61 | HappyCheats 0.3.61 — Automatic update-history cleanup | [English](docs/releases/0.3.61.en.txt) · [中文](docs/releases/0.3.61.zh.txt) |
| 0.3.60 | HappyCheats 0.3.60 — Phantom Blade Zero health recovery | [English](docs/releases/0.3.60.en.txt) · [中文](docs/releases/0.3.60.zh.txt) |
| 0.3.59 | HappyCheats 0.3.59 — Crimson Desert hotfix compatibility | [English](docs/releases/0.3.59.en.txt) · [中文](docs/releases/0.3.59.zh.txt) |
| 0.3.58 | HappyCheats 0.3.58 — Phantom Blade Zero player detection | [English](docs/releases/0.3.58.en.txt) · [中文](docs/releases/0.3.58.zh.txt) |
| 0.3.57 | HappyCheats 0.3.57 | [English](docs/releases/0.3.57.en.txt) · [中文](docs/releases/0.3.57.zh.txt) |
| 0.3.56 | HappyCheats 0.3.56 | [English](docs/releases/0.3.56.en.txt) · [中文](docs/releases/0.3.56.zh.txt) |
| 0.3.55 | HappyCheats 0.3.55 | [English](docs/releases/0.3.55.en.txt) · [中文](docs/releases/0.3.55.zh.txt) |
| 0.3.54 | HappyCheats 0.3.54 — two LAN hosts and GitHub updates | [English](docs/releases/0.3.54.en.txt) · [中文](docs/releases/0.3.54.zh.txt) |
| 0.3.53 | HappyCheats 0.3.53 — shared development and GitHub updates | [English](docs/releases/0.3.53.en.txt) · [中文](docs/releases/0.3.53.zh.txt) |
| 0.3.52 | HappyCheats 0.3.52 — GitHub-only updates | [English](docs/releases/0.3.52.en.txt) · [中文](docs/releases/0.3.52.zh.txt) |
| 0.3.51 | HappyCheats 0.3.51 — Dawnwalker damage activation | [English](docs/releases/0.3.51.en.txt) · [中文](docs/releases/0.3.51.zh.txt) |
| 0.3.50 | HappyCheats 0.3.50 — Phantom Blade Zero startup correction | [English](docs/releases/0.3.50.en.txt) · [中文](docs/releases/0.3.50.zh.txt) |
| 0.3.49 | HappyCheats 0.3.49 — Phantom Blade Zero | [English](docs/releases/0.3.49.en.txt) · [中文](docs/releases/0.3.49.zh.txt) |
| 0.3.48 | HappyCheats 0.3.48 — Direct Dawnwalker marker teleport | [English](docs/releases/0.3.48.en.txt) · [中文](docs/releases/0.3.48.zh.txt) |
| 0.3.47 | HappyCheats 0.3.47 — Dawnwalker patch 3 compatibility | [English](docs/releases/0.3.47.en.txt) · [中文](docs/releases/0.3.47.zh.txt) |
| 0.3.46 | HappyCheats 0.3.46 — LAN-first updates with GitHub fallback | [English](docs/releases/0.3.46.en.txt) · [中文](docs/releases/0.3.46.zh.txt) |
| 0.3.45 | HappyCheats 0.3.45 — Crimson Desert health refresh correction | [English](docs/releases/0.3.45.en.txt) · [中文](docs/releases/0.3.45.zh.txt) |
| 0.3.44 | HappyCheats 0.3.44 — Crimson Desert hook allocation fix | [English](docs/releases/0.3.44.en.txt) · [中文](docs/releases/0.3.44.zh.txt) |
| 0.3.43 | HappyCheats 0.3.43 — Astroneer 1.45.1 compatibility | [English](docs/releases/0.3.43.en.txt) · [中文](docs/releases/0.3.43.zh.txt) |
| 0.3.42 | HappyCheats 0.3.42 — Astroneer invulnerability and run speed | [English](docs/releases/0.3.42.en.txt) · [中文](docs/releases/0.3.42.zh.txt) |
| 0.3.41 | HappyCheats 0.3.41 — Astroneer 100× collection | [English](docs/releases/0.3.41.en.txt) · [中文](docs/releases/0.3.41.zh.txt) |
| 0.3.40 | HappyCheats 0.3.40 — Controller menu toggle | [English](docs/releases/0.3.40.en.txt) · [中文](docs/releases/0.3.40.zh.txt) |
| 0.3.39 | HappyCheats 0.3.39 — Ctrl+Del menu shortcut | [English](docs/releases/0.3.39.en.txt) · [中文](docs/releases/0.3.39.zh.txt) |
| 0.3.38 | HappyCheats 0.3.38 — Astroneer compatibility and shared in-game menu | [English](docs/releases/0.3.38.en.txt) · [中文](docs/releases/0.3.38.zh.txt) |
| 0.3.37 | HappyCheats 0.3.37 — Elden Ring offline resource trainer | [English](docs/releases/0.3.37.en.txt) · [中文](docs/releases/0.3.37.zh.txt) |
| 0.3.36 | HappyCheats 0.3.36 — Crimson Desert marker teleport correction | [English](docs/releases/0.3.36.en.txt) · [中文](docs/releases/0.3.36.zh.txt) |
| 0.3.35 | HappyCheats 0.3.35 — Civilization VII for Steam | [English](docs/releases/0.3.35.en.txt) · [中文](docs/releases/0.3.35.zh.txt) |
| 0.3.34 | HappyCheats 0.3.34 — Witcher 3 command connection repair | [English](docs/releases/0.3.34.en.txt) · [中文](docs/releases/0.3.34.zh.txt) |
| 0.3.33 | HappyCheats 0.3.33 — Civilization VI connection compatibility and function keys | [English](docs/releases/0.3.33.en.txt) · [中文](docs/releases/0.3.33.zh.txt) |
| 0.3.32 | HappyCheats 0.3.32 | [English](docs/releases/0.3.32.en.txt) · [中文](docs/releases/0.3.32.zh.txt) |
| 0.3.31 | HappyCheats 0.3.31 | [English](docs/releases/0.3.31.en.txt) · [中文](docs/releases/0.3.31.zh.txt) |
| 0.3.30 | HappyCheats 0.3.30 — Black Myth: Wukong connection repair | [English](docs/releases/0.3.30.en.txt) · [中文](docs/releases/0.3.30.zh.txt) |
| 0.3.29 | HappyCheats 0.3.29 — GTA V Enhanced current-build backend and runtime setup | [English](docs/releases/0.3.29.en.txt) · [中文](docs/releases/0.3.29.zh.txt) |
| 0.3.28 | HappyCheats 0.3.28 — GTA V Enhanced current-build backend and runtime setup | [English](docs/releases/0.3.28.en.txt) · [中文](docs/releases/0.3.28.zh.txt) |
| 0.3.27 | HappyCheats 0.3.27 — RDR2 automatic ScriptHook setup | [English](docs/releases/0.3.27.en.txt) · [中文](docs/releases/0.3.27.zh.txt) |
| 0.3.26 | HappyCheats 0.3.26 — Project Hospital, Hotel Architect and Civilization VI | [English](docs/releases/0.3.26.en.txt) · [中文](docs/releases/0.3.26.zh.txt) |
| 0.3.25 | HappyCheats 0.3.25 — Alphabetical Library sorting | [English](docs/releases/0.3.25.en.txt) · [中文](docs/releases/0.3.25.zh.txt) |
| 0.3.24 | HappyCheats 0.3.24 — Crimson Desert hook failure diagnostics | [English](docs/releases/0.3.24.en.txt) · [中文](docs/releases/0.3.24.zh.txt) |
| 0.3.23 | HappyCheats 0.3.23 — GTA V Enhanced Story Mode menu | [English](docs/releases/0.3.23.en.txt) · [中文](docs/releases/0.3.23.zh.txt) |
| 0.3.22 | HappyCheats 0.3.22 — Red Dead Redemption 2 Story Mode menu | [English](docs/releases/0.3.22.en.txt) · [中文](docs/releases/0.3.22.zh.txt) |
| 0.3.21 | HappyCheats 0.3.21 — Remove the Onimusha teleport option | [English](docs/releases/0.3.21.en.txt) · [中文](docs/releases/0.3.21.zh.txt) |
| 0.3.20 | HappyCheats 0.3.20 — Crimson Desert connection fix and Black Myth: Wukong | [English](docs/releases/0.3.20.en.txt) · [中文](docs/releases/0.3.20.zh.txt) |
| 0.3.19 | HappyCheats 0.3.19 — Dawnwalker teleport rotation fix | [English](docs/releases/0.3.19.en.txt) · [中文](docs/releases/0.3.19.zh.txt) |
| 0.3.18 | HappyCheats 0.3.18 — Crimson Desert automatic marker teleport | [English](docs/releases/0.3.18.en.txt) · [中文](docs/releases/0.3.18.zh.txt) |
| 0.3.17 | HappyCheats 0.3.17 — Dawnwalker Auto teleport | [English](docs/releases/0.3.17.en.txt) · [中文](docs/releases/0.3.17.zh.txt) |
| 0.3.16 | HappyCheats 0.3.16 — initial Crimson Desert Steam trainer | [English](docs/releases/0.3.16.en.txt) · [中文](docs/releases/0.3.16.zh.txt) |
| 0.3.15 | HappyCheats 0.3.15 — Dawnwalker movement, inventory and stealth | [English](docs/releases/0.3.15.en.txt) · [中文](docs/releases/0.3.15.zh.txt) |
| 0.3.14 | The Blood of Dawnwalker controls and controller pages | [English](docs/releases/0.3.14.en.txt) · [中文](docs/releases/0.3.14.zh.txt) |
| 0.3.13 | The Blood of Dawnwalker controls and controller pages | [English](docs/releases/0.3.13.en.txt) · [中文](docs/releases/0.3.13.zh.txt) |
| 0.3.12 | HappyCheats 0.3.12 | [English](docs/releases/0.3.12.en.txt) · [中文](docs/releases/0.3.12.zh.txt) |
| 0.3.11 | HappyCheats 0.3.11 | [English](docs/releases/0.3.11.en.txt) · [中文](docs/releases/0.3.11.zh.txt) |
| 0.3.10 | HappyCheats 0.3.10 | [English](docs/releases/0.3.10.en.txt) · [中文](docs/releases/0.3.10.zh.txt) |
| 0.3.9 | HappyCheats 0.3.9 | [English](docs/releases/0.3.9.en.txt) · [中文](docs/releases/0.3.9.zh.txt) |
| 0.3.8 | HappyCheats 0.3.8 | [English](docs/releases/0.3.8.en.txt) · [中文](docs/releases/0.3.8.zh.txt) |
| 0.3.7 | HappyCheats 0.3.7 | [English](docs/releases/0.3.7.en.txt) · [中文](docs/releases/0.3.7.zh.txt) |
| 0.3.6 | HappyCheats 0.3.6 | [English](docs/releases/0.3.6.en.txt) · [中文](docs/releases/0.3.6.zh.txt) |
| 0.3.5 | HappyCheats 0.3.5 | [English](docs/releases/0.3.5.en.txt) · [中文](docs/releases/0.3.5.zh.txt) |
| 0.3.4 | HappyCheats 0.3.4 | [English](docs/releases/0.3.4.en.txt) · [中文](docs/releases/0.3.4.zh.txt) |
| 0.3.3 | HappyCheats 0.3.3 | [English](docs/releases/0.3.3.en.txt) · [中文](docs/releases/0.3.3.zh.txt) |
| 0.3.2 | HappyCheats 0.3.2 | [English](docs/releases/0.3.2.en.txt) · [中文](docs/releases/0.3.2.zh.txt) |
| 0.3.1 | HappyCheats 0.3.1 | [English](docs/releases/0.3.1.en.txt) · [中文](docs/releases/0.3.1.zh.txt) |
| 0.2.0 | HappyCheats 0.2.0 | [English](docs/releases/0.2.0.en.txt) · [中文](docs/releases/0.2.0.zh.txt) |
