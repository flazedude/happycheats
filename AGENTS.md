# HappyCheats project instructions

## Shared in-game menu

All current and future games use the client `GameOverlay` companion menu (Ctrl+Del). New `IGameModule` implementations must provide exact installation-relative `GameExecutables` and complete `FeatureDefinition` metadata; numeric and choice controls use the existing value/choice module interfaces. Do not create a separate companion renderer for a game. Keep command/session/availability checks in the existing shared client and module paths. Include new games in the catalog menu regression checks and follow `docs/in-game-menu.md`. The companion supports windowed/borderless play; do not claim exclusive-fullscreen or live gameplay validation from offline UI checks.

For every current and future game, reserve the Xbox/XInput chord **hold Menu (☰) for 350 ms, then tap RB** for showing/hiding the shared menu. Use the shared controller path and respect the common Xbox shortcuts setting; do not require a game-specific controller profile for this chord or route it to a native HUD. Keep Ctrl+Del unchanged. Existing Onimusha/Dawnwalker direct cheat chords may remain, and Onimusha's native status panel keeps F6. Menu+RB only toggles the shared menu; mouse/keyboard operate its controls, with no implied controller row navigation. Include future modules in the shared controller regression checks and distinguish synthetic input checks from physical-controller validation.

## Standing release preference (updated by user, 30 October 2026)

For work on HappyCheats in every chat/window, each completed client update or newly developed/changed cheat requires ALL of: a signed public GitHub update, a fresh complete standalone EXE, a public GitHub Release with the same package bytes, and the corresponding source committed and pushed to https://github.com/flazedude/happycheats. GitHub is the only normal update host. LAN hosting and publishing are deprecated because downloads must not require this PC to remain on. Do not start, enable or publish to LAN unless the user explicitly requests it. The user has authorized this normal workflow, including public releases in the source repository; do not ask again whether to build, publish or push. Preserve the existing signing key. Follow later instructions requesting development-only work, deferred publishing or a different scope. Documentation-only and instruction-only changes require source sync but do not themselves require a new binary release.

Re-read this file from disk before implementation and again before every release: other existing chats share this checkout, and their earlier instruction snapshot may be stale. Fetch remote state, inspect local changes, GitHub Releases/tags and retained local release records. Coordinate shared-file/build output use, preserve other chats' changes, and never blindly stage unfinished unrelated work. A release is incomplete until source, local complete artifacts and GitHub all match the intended version; report any blocked step precisely. LAN publication is not required.

Read `docs/github-updates.md`, `docs/updates.md` and `docs/single-exe.md` before release work. Use `scripts/Publish-Update.ps1`, whose default target is GitHub only. Official clients check GitHub directly; retained official LAN settings migrate only when their signing identity matches the bundled defaults. Keep signature, channel, size and SHA-256 verification; never embed a GitHub credential in the app. Preserve custom configurations. Aliyun/OSS is not the selected deployment target. `docs/lan-updates.md` and LAN scripts are legacy recovery references.

## Release workflow

### Working from either development PC

Both Windows development PCs use the same `flazedude/happycheats` repository and public GitHub releases. At the start of every task, fetch remote state and inspect local changes. If the checkout is clean and only behind its upstream, fast-forward it before implementing changes. If commits diverge or local work is unfinished, preserve that work and integrate the remote changes deliberately; never reset, force-push or silently overwrite it. Commit and push completed source so the other PC receives it at the start of its next task. Concurrent features should use separate branches and be integrated before publishing. Git source synchronization is separate from installing a signed HappyCheats client update.

Each PC uses its own GitHub authentication and a protected local copy of the same established signing key. Use `scripts/Initialize-Publisher.ps1` for machine-local publishing setup; never commit or upload private keys or credentials. Normal GitHub publishing must not depend on another PC's absolute paths or a running LAN server. Before publishing, fetch again and check GitHub releases/drafts/tags; if another PC has advanced the source or reserved a version, reconcile source and select an unused higher version. Preserve all published bytes and staged recovery artifacts.

1. Inspect current source/build versions, GitHub Releases/tags and retained local release records (including the last LAN feed during migration). Choose a strictly higher `major.minor.patch` version than any published version. Never reuse a published version or overwrite a signed archive or published GitHub asset. Keep `scripts/Build.ps1` and `src/HappyCheats.App/HappyCheats.App.csproj` version defaults aligned.
2. Finish the requested feature, rebuild every changed trainer payload, and run relevant checks. New games require a real module, catalog registration, payload/build integration and tests; a changelog entry alone does not add a game. Preserve unrelated user changes and existing trainer behavior.
3. Write English and Chinese notes in `docs/releases/<version>.en.txt` and `<version>.zh.txt`. Record what changed and distinguish offline validation from actual gameplay verification. Update user-facing version/download references as appropriate.
4. Review the complete release diff, commit the intended source/docs/tests and push normally to GitHub without force. Verify the remote branch contains the release commit. Publishing requires a clean checkout matching that source commit. Never commit build artifacts, credentials, private signing keys or extracted game assets. Do not automatically commit other chats' unfinished work merely to satisfy this check.
5. Use machine-local `%LOCALAPPDATA%/HappyCheatsPublisher/publisher.json` for the established signing-key paths. The old `artifacts/lan-updates/server.json` remains a compatibility source on the original PC; its historical directory name does not require a running LAN server. Never replace a missing established key. Preserve saved LAN configuration and old artifacts for recovery.
6. Use `scripts/Publish-Update.ps1` with the new version and BOTH note files. The normal command builds once, signs the GitHub feed, uploads and verifies a complete GitHub draft, then publishes it. Tests run by default; do not use `-SkipTests` as the normal path. Use `-SkipPayloads` only if payloads are unchanged or every changed payload was explicitly rebuilt and validated in this task. Interrupted publishing must resume verified staged artifacts; never rebuild different bytes under an already published version.
7. Verify the hosted signed feed, anonymous GitHub downloads, direct GitHub configuration, trusted LAN-settings migration, package hashes, fresh EXE version/configuration, and a suitable packaged-client smoke check. Keep old release ZIPs and recovery backups. Do not claim second-PC, physical-controller or gameplay validation unless actually tested.
8. In the final response, give the version, concrete changes, checks/limitations, source commit/release link, clickable local versioned EXE and GitHub downloads. New cheats must be in every complete distribution, not only in source or DLL files.

Normal command shape (substitute the actual new version and note paths):

```powershell
.\scripts\Publish-Update.ps1 -Version <version> `
  -NotesEnglish .\docs\releases\<version>.en.txt `
  -NotesChinese .\docs\releases\<version>.zh.txt
```

## Required artifacts

- `dist/HappyCheats.exe`: latest complete standalone EXE, not the smaller executable inside the unpacked client folder.
- `dist/HappyCheats-<version>.exe`: versioned copy of that complete standalone EXE for the user to keep.
- `dist/HappyCheats-<version>-win-x64-single-exe.zip`: versioned single-EXE ZIP.
- `dist/HappyCheats-<version>-win-x64.zip`: complete directory-format package consumed by the updater.
- GitHub Release `v<version>` in `flazedude/happycheats`: signed `latest.json` pointing to its version-pinned ZIP, the same complete update ZIP, standalone EXE and single-EXE ZIP. The stable public feed is `https://github.com/flazedude/happycheats/releases/latest/download/latest.json`.
- GitHub source branch and release tag: the commit corresponding to the built package, recorded in `release-source.json`.

If prerequisites, tests, payload compilation, signing keys or hosting prevent completion, continue any independent work and report precisely which artifacts are ready and which release step is blocked. Do not publish a broken release, invent validation or quietly omit the standalone EXE. This standing preference does not authorize changing game saves, starting games for testing, or broadening firewall access.

Use PowerShell 7 for interactive repository scripts. Keep private keys outside the repository and served directory; never include their contents in logs or responses. The Windows scheduled task `HappyCheats LAN Update Server` is retired: keep it disabled and stop its server. Preserve the task definition, configuration, signing key, old release ZIPs and backups. Do not run `Install-LanUpdateStartup.ps1` or `Start-LanUpdateServer.ps1` during normal releases. Legacy clients from 0.3.46 use their GitHub fallback when LAN is unavailable; older clients need the latest standalone EXE once. A stale running LAN feed can hide newer GitHub versions from old LAN-first clients.
