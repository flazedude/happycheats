# Independent cheat updates

HappyCheats **0.3.65** introduces independently versioned game packages. This is the one-time client upgrade needed to enable the new system. Thereafter a compatible game fix or newly supported game changes only its cheat package version. The HappyCheats EXE and client version stay the same. Changes to the client, shared interfaces or updater still use the complete client release workflow.

## Using the client

From **0.3.66**, a red **!** beside a Library game signals a module-reported compatibility/runtime problem, a newer signed package, or a downloaded version awaiting Library reload. Hover for the reason; click to open updates filtered to that game, then choose **Check this game / 检查此游戏更新** or its Update / Repair action. The normal Library Update button restores the all-games view. A disconnected game or an ordinary stale heartbeat alone does not trigger this warning. Existing module diagnostics can combine identity, schema and version failures, so the reason is shown without claiming every runtime warning proves a version difference.

Signed trainer catalogs are checked at startup and every 30 minutes without automatically downloading packages. Failed background checks are logged locally and retain known warnings; opening the update window gives an actionable check error. A download keeps its warning until the Library loads that version. A current package does not clear an unsupported-build or runtime diagnostic; no available update is not proof of compatibility. Shared contracts and trainer payloads are unchanged by this UI release.

Click **Update / 更新** beside the Library's A–Z / Z–A Sort controls. The modeless **Cheat updates / 修改器更新** window checks both LAN catalogs and GitHub, lists each game's installed and available package versions, and offers **Update**, **Add game**, or **Repair** per game. Controls stay clickable during downloads; repeated conflicting operations report a busy error and are logged locally. The trainer window stays usable while a package downloads.

After downloading a signed package, close supported games and choose **Reload library / 重新加载修改器库**. This loads the new module without replacing or restarting the HappyCheats EXE. Then use the affected game's existing **Install / update** action if its trainer files must be copied into the game. Updating the local module cache does not inject new code into a running game or alter saves. Closing the download window hides it while its operation finishes; reopening retains that operation. Exiting the client cancels unfinished transfers.

The separate bottom-left **Updates / 客户端更新** window continues to manage client releases. A cheat that needs a newer client or module API reports its minimum requirement instead of forcing a client installation.

## Versions and storage

`modules/cheat-versions.json` records each game's independent **package** version. Initial packages use **1.0.0**. These cover the module assembly, dependencies, metadata and game payload together; internal trainer protocol/payload versions, such as Dawnwalker's 0.4.2, retain their existing meaning. Increment a game's package version whenever its shipped logic, payload, metadata or setup behavior changes. Do not bump unrelated game versions or the HappyCheats client version for game-only work.

Complete client builds include the tracked versions as `cheat-versions.json` and all current bundled payloads. Downloaded packages are stored under `%LOCALAPPDATA%/HappyCheats/Cheats/`, independently of the standalone launcher's bundle cache. They survive full client updates. A newer bundled game version supersedes an older downloaded version; keep this inventory accurate when making a later complete client release. The original starter EXE remains usable as a bootstrap for adding newer games online. A cheat-only publication intentionally does not rebuild the offline complete EXE.

`HappyCheats.Contracts` contains the stable module, feature, choice, numeric and lifecycle contracts. Modules load in separate .NET assembly contexts sharing this contract, with their own module logic and private dependencies. A game archive carries `lib/HappyCheats.Core.dll` and the small private setup dependency plus **only that game's** `payload/<id>/` tree. Compiling the shared source project does not replace the client or unrelated modules. Package artwork is optional; unknown games have the HappyCheats artwork fallback. Existing English/Chinese client translations remain available; new module metadata falls back to its supplied names and descriptions when the host has no translation.

New games still need a real `IGameModule`, catalog registration in source, exact installation-relative `GameExecutables`, full feature metadata, payload/build integration, a package-version entry and relevant checks. Register their payload builder in `Publish-CheatUpdate.ps1`. The package can then be added to already installed compatible clients without recompiling their EXE. The shared GameOverlay, Ctrl+Del and Menu held 350 ms + RB path remain mandatory; no separate renderer or game-specific controller profile is needed.

## Trust and feed selection

The official client's existing trusted key and stable channel configure:

1. `http://192.168.2.76:17845/happycheats/cheats.json`
2. `http://192.168.2.59:17845/happycheats/cheats.json`
3. `https://raw.githubusercontent.com/flazedude/happycheats/cheat-feed/cheats.json`

Custom client feed configurations are preserved and are not silently pointed at official cheat feeds. Their user sees an actionable configuration error for independent updates. No credential is embedded in a client.

Every reachable catalog is independently verified with the established RSA identity, stable channel and schema. The highest signed revision wins even when a LAN feed is stale. Previously verified catalog state and installed package receipts prevent downgrades and same-version substitutions. Older catalogs may lack newer games; a newer catalog may not remove or downgrade their entries. Package downloads prefer a LAN source only if that source's independently verified catalog identifies the selected exact version, hash, size, source and compatibility metadata. GitHub is the final download source. Transport failures may advance; integrity failures stop without silently selecting different bytes.

Packages use immutable `cheat-<game-id>-<version>` GitHub release tags and version-pinned ZIP URLs. They are public GitHub prereleases with `make_latest=false` so legacy clients continue seeing the latest complete client release at `releases/latest`. Prerelease here separates distribution tracks, not the stable channel signature. The `cheat-feed` branch contains the mutable signed catalog and is updated with GitHub's compare-and-swap file SHA under the existing global publishing lock. Package assets and prior signed stages remain immutable.

Downloads verify signed size and SHA-256, reject unsafe archive paths and links, validate complete module/feature metadata, and probe the shared interface before atomically activating a signed per-game receipt. Startup/reload rechecks the retained signed receipt, archive and extracted files before loading executable module bytes. A failed replacement leaves the prior active receipt unchanged. Explicit Repair retains damaged files under the cache's recovery folder. Cross-process locks prevent overlapping installation/catalog mutations. The normal local client error log records failures. These modules run trusted signed code, not sandboxed third-party plugins.

## Publishing a game update

Read current `AGENTS.md`, receive shared source, work on an isolated `codex/` task branch and inspect all existing versions/reservations. Modify the module/payload and increment **only** its entry in `modules/cheat-versions.json`. Write English and Chinese notes under `docs/cheats/<game-id>/<version>.en.txt` and `.zh.txt`. Commit reviewed work, integrate it into `main`, push normally and verify the publishing checkout exactly matches `origin/main`.

From PowerShell 7 on either configured publishing PC:

```powershell
.\scripts\Publish-CheatUpdate.ps1 -GameId dawnwalker -Version 1.0.1 `
  -NotesEnglish .\docs\cheats\dawnwalker\1.0.1.en.txt `
  -NotesChinese .\docs\cheats\dawnwalker\1.0.1.zh.txt
```

The publisher rebuilds that game's payload, runs the offline suites, compiles the module, stages an immutable ZIP and signs the catalog. `-SkipPayloads` is appropriate only when those exact payloads were already rebuilt and validated in the current task. Tests are not skipped. Set `-MinimumClientVersion` when newer client capabilities are required; contract API changes also need a deliberate client release.

The publisher requires current integrated source, the PC's established protected signing key and its existing LAN configuration. It uses both the local publication lock and `refs/tags/happycheats-publish-lock`; respect live locks and follow the existing recovery procedure. It checks remote drafts/tags, the catalog and retained local stages for that game. Keep offline peer reservations documented and coordinate them before selecting a version. Retrying uses verified staged bytes and never rebuilds a published version. Published ZIP and signed `cheat.json` proof assets are verified before the shared index advances. A failed LAN mirror remains an incomplete step recoverable from GitHub.

The publishing PC exposes `cheats.json` and identical game ZIPs alongside its existing client feed. `Sync-CheatUpdate.ps1` verifies and mirrors the independent catalog/packages with only the public key, preserving old archives and feed backups. `Sync-LanUpdate.ps1` invokes it as part of the already enabled mirror refresh. Deploy the updated LAN server executable on each host before claiming its cheat URLs are served; preserve both machines' addresses, tasks, keys and configurations. A peer that is off or still running older mirror/server code catches up after receiving/deploying this source and refreshing. GitHub remains available while either host is off.

## Validation

### Deploy the updated GPD host

Use `Deploy-GPD-Cheats.cmd` on GPD-PC to fetch official main into a separate clean, pinned worktree. Its current development branch, dirty tracked files and untracked work remain untouched. This host-only deployment does not change the HappyCheats client version or publish new trainer bytes. The helper checks the `.59` identity, clean deployment source, existing task action, corrected firewall application path, listening process and established public signing key. It stages an isolated server build before stopping anything and retains server/configuration/task backups. It changes only the existing task's action to use the retained clean source with GPD's existing host root; principal, triggers, settings, address, firewall scope and signing identity remain intact. Failed replacement or startup restores the backup and previous task action. An active publisher/mirror causes a retryable busy error before service interruption. `-PrepareOnly` builds a stage without replacing the active host.

For the shared V: drive handoff, copy `scripts/Deploy-GPD-Cheats.cmd` and `scripts/Deploy-GPD-Cheats.ps1` together and double-click the CMD on GPD. It defaults to the established `D:\Coding\happycheats` host and retains the deployment worktree under `%LOCALAPPDATA%/HappyCheatsPublisher/HostDeployments/`. Keep that worktree: startup uses its pinned server/mirror scripts. The launcher stages itself locally before UAC, and uses GPD's own host configuration and key. Result JSON and deployment logs are copied beside it, including failures, so the other PC can inspect them. No private key or another host's configuration is copied.

When GitHub source fetching is unavailable, supply `Deploy-GPD-Cheats-source.json` beside the launcher with `commit`, `file` and `sha256`, plus `HappyCheats-GPD-source-<commit>.bundle` containing only the verified integrated `refs/heads/main`. The launcher verifies the bundle digest, imports it under a dedicated deployment ref and leaves the development branch/index unchanged. It waits for the elevated setup process to exit, rather than compiler servers that may outlive setup. To collect an existing result and recent mirror logs without deploying again, copy `Collect-GPD-Cheats.cmd` and `Collect-GPD-Cheats.ps1` beside each other on V and run the CMD on GPD without elevation. The collector saves `GPD-cheat-diagnostics-*.json` beside itself and reads no private keys or credentials.

The existing task must point to this host's `Run-LanUpdateServer.ps1` or a previously recorded deployment of that host; an unrelated action, missing task or intentionally disabled task stops deployment for deliberate repair. Private keys, credentials, LAN addresses, existing public packages and custom refresh intervals are preserved. Explicit configuration paths keep both mirrors' locks, cache and backups in the original host state directory even when scripts run from the isolated source. Cheat refresh runs before client bulk downloads, so a slow full-client transfer does not delay available game packages. The first automatic refresh may still be pending when the helper returns; verify `cheats.json` and package hashes from the other PC before claiming both hosts work. No reboot validation is implied.

The regression suites load a signed synthetic game absent from the built-in catalog, dispatch toggle/value/choice interfaces, exercise its shared overlay, and verify modeless/busy UI behavior. They cover signatures, compatibility, package corruption, repair, cancellation, mutation locks, stale LAN selection, exact-package fallback and integrity-stop behavior. HTTP server tests cover the allowlisted independent feed/package URLs, ranges, caching and write rejection. These are offline/synthetic checks and do not establish gameplay, physical-controller behavior, exclusive-fullscreen support, peer availability or a reboot test.
