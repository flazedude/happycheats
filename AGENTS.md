# HappyCheats project instructions

## Shared in-game menu

All current and future games use the client `GameOverlay` companion menu (Ctrl+Del). New `IGameModule` implementations must provide exact installation-relative `GameExecutables` and complete `FeatureDefinition` metadata; numeric and choice controls use the existing value/choice module interfaces. Do not create a separate companion renderer for a game. Keep command/session/availability checks in the existing shared client and module paths. Include new games in the catalog menu regression checks and follow `docs/in-game-menu.md`. The companion supports windowed/borderless play; do not claim exclusive-fullscreen or live gameplay validation from offline UI checks.

For every current and future game, reserve the Xbox/XInput chord **hold Menu (☰) for 350 ms, then tap RB** for showing/hiding the shared menu. Use the shared controller path and respect the common Xbox shortcuts setting; do not require a game-specific controller profile for this chord or route it to a native HUD. Keep Ctrl+Del unchanged. Existing Onimusha/Dawnwalker direct cheat chords may remain, and Onimusha's native status panel keeps F6. Menu+RB only toggles the shared menu; mouse/keyboard operate its controls, with no implied controller row navigation. Include future modules in the shared controller regression checks and distinguish synthetic input checks from physical-controller validation.

## Standing release preference (updated by user, 24 October 2026)

For work on HappyCheats in every chat/window, each completed client update or newly developed/changed cheat requires ALL of: a signed LAN update, a fresh complete standalone EXE, a public GitHub Release with the same package bytes, and the corresponding source committed and pushed to https://github.com/flazedude/happycheats. The user has authorized this normal workflow, including public releases in the source repository; do not ask again whether to build, publish or push. Preserve the existing signing key. Follow later instructions requesting development-only work, deferred publishing or a different scope. Documentation-only and instruction-only changes require source sync but do not themselves require a new binary release.

Re-read this file from disk before implementation and again before every release: other existing chats share this checkout, and their earlier instruction snapshot may be stale. Fetch remote state, inspect local changes and the latest versions on BOTH hosts. Coordinate shared-file/build output use, preserve other chats' changes, and never blindly stage unfinished unrelated work. A release is incomplete until source, LAN and GitHub all match the intended version; report any blocked step precisely.

Read `docs/lan-updates.md`, `docs/github-updates.md`, `docs/updates.md` and `docs/single-exe.md` before release work. Use the existing server and unified publish scripts. Clients try LAN first, then GitHub if the LAN request fails or times out. GitHub fallback must keep signature, channel, size and SHA-256 verification; never embed a GitHub credential in the app. Aliyun/OSS is not the selected deployment target.

## Release workflow

1. Inspect current source/build versions, the saved LAN public feed and GitHub Releases/tags. Choose a strictly higher `major.minor.patch` version than either published host. Never reuse a published version or overwrite a signed archive or published GitHub asset. Keep `scripts/Build.ps1` and `src/HappyCheats.App/HappyCheats.App.csproj` version defaults aligned.
2. Finish the requested feature, rebuild every changed trainer payload, and run relevant checks. New games require a real module, catalog registration, payload/build integration and tests; a changelog entry alone does not add a game. Preserve unrelated user changes and existing trainer behavior.
3. Write English and Chinese notes in `docs/releases/<version>.en.txt` and `<version>.zh.txt`. Record what changed and distinguish offline validation from actual gameplay verification. Update user-facing version/download references as appropriate.
4. Review the complete release diff, commit the intended source/docs/tests and push normally to GitHub without force. Verify the remote branch contains the release commit. Publishing requires a clean checkout matching that source commit. Never commit build artifacts, credentials, private signing keys or extracted game assets. Do not automatically commit other chats' unfinished work merely to satisfy this check.
5. Run `scripts/Start-LanUpdateServer.ps1` to reuse/start the configured host. Read `artifacts/lan-updates/server.json` for the authoritative endpoint and key paths. Preserve its key, port, address and startup task. The initial endpoint is `http://192.168.2.76:17845/happycheats`. Never silently switch hosts or replace a missing established key.
6. Use `scripts/Publish-LanUpdate.ps1` with the new version and BOTH note files. The normal command builds once, signs the LAN and GitHub feeds, uploads a complete GitHub draft, and publishes both hosts. Tests run by default; do not use `-SkipTests` as the normal path. Use `-SkipPayloads` only if payloads are unchanged or every changed payload was explicitly rebuilt and validated in this task. Interrupted publishing must resume verified staged artifacts; never rebuild different bytes under an already published version.
7. Verify both hosted signed feeds, anonymous GitHub downloads, LAN-first and unreachable-LAN fallback, package hashes, fresh EXE version/configuration, and a suitable packaged-client smoke check. Keep old release ZIPs and recovery backups. Do not claim second-PC, physical-controller or gameplay validation unless actually tested.
8. In the final response, give the version, concrete changes, checks/limitations, source commit/release link, clickable local versioned EXE and both LAN/GitHub downloads. New cheats must be in every complete distribution, not only in source or DLL files.

Normal command shape (substitute the actual new version and note paths):

```powershell
.\scripts\Start-LanUpdateServer.ps1
.\scripts\Publish-LanUpdate.ps1 -Version <version> `
  -NotesEnglish .\docs\releases\<version>.en.txt `
  -NotesChinese .\docs\releases\<version>.zh.txt
```

## Required artifacts

- `dist/HappyCheats.exe`: latest complete standalone EXE, not the smaller executable inside the unpacked client folder.
- `dist/HappyCheats-<version>.exe`: versioned copy of that complete standalone EXE for the user to keep.
- `dist/HappyCheats-<version>-win-x64-single-exe.zip`: versioned single-EXE ZIP.
- `dist/HappyCheats-<version>-win-x64.zip`: complete directory-format package consumed by the updater.
- Configured server's public directory: signed `latest.json`, immutable release ZIP and latest `HappyCheats.exe`.
- GitHub Release `v<version>` in `flazedude/happycheats`: signed `latest.json` pointing to its version-pinned ZIP, the same complete update ZIP, standalone EXE and single-EXE ZIP. The stable public feed is `https://github.com/flazedude/happycheats/releases/latest/download/latest.json`.
- GitHub source branch and release tag: the commit corresponding to the built package, recorded in `release-source.json`.

If prerequisites, tests, payload compilation, signing keys or hosting prevent completion, continue any independent work and report precisely which artifacts are ready and which release step is blocked. Do not publish a broken release, invent validation or quietly omit the standalone EXE. This standing preference does not authorize changing game saves, starting games for testing, or broadening firewall access.

Use PowerShell 7 for interactive repository scripts. Keep private keys outside the repository and served directory; never include their contents in logs or responses. The Windows scheduled task `HappyCheats LAN Update Server` starts the server at boot without sign-in and restarts it after failure. Preserve this task. `scripts/Install-LanUpdateStartup.ps1` installs/repairs it, `Start-LanUpdateServer.ps1` reuses it, and `Stop-LanUpdateServer.ps1` stops the task and server until explicitly started or the next boot. The task runs the Windows PowerShell 5.1-compatible `Run-LanUpdateServer.ps1` using the existing published server; no SDK or signing key access is needed at boot. This is host setup, not a Windows service or a reason to publish a new client version.
