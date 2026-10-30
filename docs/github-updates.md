# GitHub updates and publishing

From **0.3.54**, the official update configuration checks **JACK-PC LAN, GPD-PC LAN, then public GitHub Releases**. Every source is checked, so an older reachable LAN feed cannot hide a newer signed GitHub release. Matching LAN packages are preferred for the selected release; GitHub keeps downloads available while either PC is off. Downloads still require **Download update** and **Install & restart**. This follows the user's latest **30 October 2026** preference, superseding the earlier GitHub-only configuration in 0.3.52–0.3.53.

- [Source and releases](https://github.com/flazedude/happycheats)
- [Stable signed feed](https://github.com/flazedude/happycheats/releases/latest/download/latest.json)
- [Latest complete standalone EXE](https://github.com/flazedude/happycheats/releases/latest/download/HappyCheats.exe)

The repository is public so clients can download releases without a GitHub account or token. Publishing authentication stays on the operator's PC. Never include tokens or the private signing key in source, packages or a public directory.

## Verification and downloads

Official releases configure this ordered `feedUrls` list:

1. `http://192.168.2.76:17845/happycheats/latest.json` — JACK-PC.
2. `http://192.168.2.59:17845/happycheats/latest.json` — GPD-PC.
3. `https://github.com/flazedude/happycheats/releases/latest/download/latest.json` — shared public GitHub release.

The client independently verifies each available feed's established signature and update channel, then selects the highest trusted release version. Equal versions with different signed package sizes or SHA-256 values are rejected. It prefers a matching local package from JACK-PC, then GPD-PC, then GitHub; a LAN bridge whose package URL points to GitHub does not outrank an actual matching LAN download. Before retrying a failed connection on an alternate source, it obtains and verifies that source's feed again and requires the selected version, size and hash to match. Different-version feeds are skipped; a different archive cannot become a package fallback. The downloaded archive still undergoes size, SHA-256 and complete-package structure checks before installation.

GitHub's signed feed points to an immutable version-specific URL such as `/releases/download/v0.3.54/HappyCheats-0.3.54-win-x64.zip`. Each LAN feed points to that host's local copy of the same package bytes. The signing key is unchanged. Connection failures are skipped and stale trusted feeds cannot hide a newer release. Signature, channel, metadata or package-integrity failures stop the operation; the client does not conceal them by retrying elsewhere. If checking or downloading fails, the installed client remains usable and the user can retry after resolving the error.

LAN checks have bounded waits. Internet requests use a 60-second feed/response deadline and a 60-second read inactivity limit; the official three-source update check has a 73-second overall UI budget. A continuously progressing package download is not limited to 60 seconds in total. Cancellation ends the operation. The legacy `feedUrl` and `fallbackFeedUrl` fields remain for older clients, but current official clients use the explicit ordered list.

## Existing installations

Official **0.3.52–0.3.53** clients check GitHub directly and can obtain the current release there. Official **0.3.46–0.3.51** clients already include GitHub fallback when their LAN request fails. Their older first-success behavior can still hide a newer GitHub version behind a stale but reachable LAN feed; keeping the LAN mirror current or using the latest complete EXE gets those clients onto the new all-source checker.

**Pre-0.3.46** clients do not know the GitHub feed endpoint. They need a reachable compatible LAN configuration, or can run the [latest complete standalone EXE](https://github.com/flazedude/happycheats/releases/latest/download/HappyCheats.exe) once to obtain the configured client. User preferences remain in `%LOCALAPPDATA%/HappyCheats/settings.json`.

The updater continues preserving `update-settings.json` and its trusted signing key. The client can adopt the bundled official three-source list at runtime only when the saved configuration is a recognized earlier official layout and its signing identity and channel match:

- The saved endpoints are the original `.76` LAN feed alone, that LAN feed with the official GitHub fallback, or the official GitHub-only feed.
- The bundled defaults specify exactly the ordered official list above.
- The saved and bundled RSA public keys and update channels match.

This migration does not rewrite the preserved settings file. Custom endpoints, custom feed lists and mismatched signing identities remain unchanged. Existing custom configurations without `feedUrls` keep their original single-feed or primary/fallback behavior. Fresh official releases contain the three-source list.

The historical 0.3.52 bridge served the identical GitHub-signed feed at the original LAN endpoint, with its package URL still pointing to GitHub. Preserve that release's artifacts for recovery; current LAN mirrors instead serve local packages with independently signed feeds under the same established key.

## Developing on either PC

**JACK-PC** and **GPD-PC** are equal publishers. Work completed on GPD-PC travels through the shared GitHub repository and releases to JACK-PC, and vice versa. Earlier GPD setup records use the name `JACKGPD`; this documentation does not rename Windows. JACK-PC's configured LAN address is `.76`, and GPD-PC's is `.59`, both on port `17845`. Each host keeps its own local server configuration and mirrors verified shared GitHub releases. Clients check both hosts and GitHub, so neither PC needs the other to be online.

GitHub is the shared source of truth for both checkouts. At the start of work on either PC, read `AGENTS.md`, inspect local changes and fetch the remote branch:

```powershell
git status --short --branch
git fetch --prune origin
```

For a clean `main` checkout, bring it up to date before editing:

```powershell
git pull --ff-only origin main
```

If there are unfinished edits or local commits, preserve them and review how they relate to incoming changes. Finish or save the intended work and merge deliberately; do not discard changes, reset the checkout or force-push to make the PCs agree. If a fast-forward fails, inspect the divergence before proceeding. Re-read `AGENTS.md` after updating because the other PC may have changed the shared instructions.

Use a separate `codex/` branch for each change and PC, with shared `main` as the integration branch. Concurrent local chats should use separate checkouts/worktrees when they need isolated files or build outputs. Fetch again before integration and pushing. Merge incoming `origin/main` changes into an already shared task branch; rebase only unpublished local commits. Resolve conflicts while preserving both intended changes and rerun affected checks. A rejected push means fetch and integrate before retrying, not force-push over the other PC.

After completing a change, review and commit its source, notes and tests, then push normally. Verify the remote task branch contains the intended commit before switching PCs. Before publishing, integrate the completed, reviewed release changes into `main` and verify `origin/main` contains the package's release commit. Publish from a clean checkout matching that integrated commit; do not omit other intended integrated changes or include another chat's unfinished work. The second PC receives the work through its next source sync. Build output, trainer build caches, publisher settings, credentials and signing keys are local and are not synchronized by Git.

Both PCs can build and publish when each has its existing build prerequisites, the same established signing key and its own GitHub authentication. A client update from **Updates / 客户端更新** updates the installed application; it does not pull source into a development checkout. Conversely, pulling source does not update an installed client. Use the app's update action or the latest standalone EXE for that.

Only one PC may publish at a time. Immediately before choosing a version and again before publishing, inspect GitHub releases, drafts and tags as well as known staged/reserved release records on both PCs. Choose a globally unique, monotonically increasing version above published and reserved versions; an in-progress draft on the other PC is not an available version. If publication was interrupted, resume the original verified stage and source commit rather than rebuilding different bytes on the other PC under the same version. Other PCs may continue developing separate changes while publication runs.

The current publisher serializes GitHub draft creation, uploads and finalization with the repository-wide ref `refs/tags/happycheats-publish-lock`. Its tag metadata identifies the version, source commit, random ownership token and creation time; it contains no credentials or signing key. A second publisher stops when the lock is held and can retry after the first finishes. The script releases its own lock on normal exit and never steals another publisher's lock. Both PCs must sync the new publisher scripts before relying on this protection; an older script does not honor it.

## One-time publisher setup on each PC

Source synchronization carries the scripts and project instructions to the other PC; it does not configure that PC remotely. Run this setup locally on each publishing PC. The second PC's actual setup and release capability must be verified there.

The established signing key was transferred privately to GPD-PC on 30 October 2026, and its publisher setup was verified against the existing signed GitHub feed. Both PCs have machine-local publisher configuration. Use that existing setup; another transfer is unnecessary. The steps below remain the setup/recovery reference for an additional PC, not a claim that network access or a release was tested there.

1. Sign in to GitHub separately on each PC using its existing Git credential manager, or `gh auth login --hostname github.com`. The account needs permission to push source and publish releases to `flazedude/happycheats`. Credentials stay in that PC's credential store and are never copied into source or client packages.
2. Locate the **existing private signing key** already present on that PC. For any additional PC without a copy, transfer the established key once using a private method; never commit it, attach it to a GitHub release or place it in a public/synchronized repository directory. Both PCs must use the same signing identity already trusted by installed clients. Do not generate a new key.
3. In PowerShell 7, point the initializer to that existing key. Replace the example path with the protected file's actual location:

   ```powershell
   .\scripts\Initialize-Publisher.ps1 -PrivateKeyPath 'D:\secure-transfer\release-private.pem'
   ```

The initializer downloads the current public GitHub feed anonymously, derives the public key from the supplied private key and verifies the live feed's signature, stable channel and version-pinned package URL before copying any key. A wrong key or unavailable feed stops setup. It never generates a replacement key, preserves the supplied file and refuses to replace a different existing key or publisher configuration.

Successful setup stores the protected signing files under `%LOCALAPPDATA%\HappyCheatsPublisher\Signing\flazedude_happycheats\` and writes repository/key paths to `%LOCALAPPDATA%\HappyCheatsPublisher\publisher.json`. Each PC has its own local paths; no LAN address, server, task or shared folder is required. The JSON file contains configuration paths, not the private key itself.

On the original publishing PC, the established key path can be read from the preserved legacy configuration without displaying the key:

```powershell
$hcLegacyPublisher = Get-Content -LiteralPath .\artifacts\lan-updates\server.json -Raw | ConvertFrom-Json
.\scripts\Initialize-Publisher.ps1 -PrivateKeyPath $hcLegacyPublisher.PrivateKeyPath
```

After setup, either PC uses the same normal publishing command below. Keep a secure backup of the established private key. Source sync alone cannot recover a missing signing key or GitHub credential.

## Publishing every completed iteration

The standing workflow in [AGENTS.md](../AGENTS.md) applies to every chat/window sharing this checkout. Re-read it from disk before implementation and before releasing. Each completed client or trainer change requires source sync, a signed public GitHub Release, the publishing PC's local LAN mirror with the same package bytes, and a fresh complete standalone EXE. Documentation-only changes require source sync but no new binary release.

1. Sync source using the workflow above and inspect both LAN feeds, GitHub Releases, drafts, tags and retained local release records. Choose a globally unique version above published and reserved versions, and align app/build defaults. Preserve other chats' unfinished work.
2. Rebuild changed payloads, write English and Chinese notes and finish relevant checks.
3. Review and commit the intended source, integrate the release changes into `main`, then push normally and verify `origin/main` contains the release commit. Publishing requires a clean checkout matching that integrated commit and verifies source provenance; never commit unrelated unfinished work to satisfy this check.
4. Run in PowerShell 7, substituting the new version and matching notes:

   ```powershell
   .\scripts\Publish-Update.ps1 -Version <new-version> `
     -NotesEnglish .\docs\releases\<new-version>.en.txt `
     -NotesChinese .\docs\releases\<new-version>.zh.txt
   ```

The command builds and tests once, signs the GitHub and two host-specific LAN feeds, stages complete artifacts, then uploads and verifies a GitHub draft before publishing it. It installs the publishing PC's local LAN mirror from those same verified bytes after GitHub is public. It requires that PC's saved LAN configuration; it does not set up another host remotely. Packages include `release-source.json` identifying the Git commit. The default repository is `flazedude/happycheats`.

The publisher uses machine-local `%LOCALAPPDATA%\HappyCheatsPublisher\publisher.json` for signing-key paths and this PC's `artifacts/lan-updates/server.json` for its official LAN endpoint and public directory. The original configuration remains a compatibility source for established key paths. Preserve configuration, keys, archives and recovery backups. If a key is missing, restore it rather than generating a replacement. The private key stays outside the repository and public download directories.

Use `-SkipPayloads` only when payloads are unchanged or every changed payload was rebuilt and validated in the current task. Do not use `-SkipTests` for normal releases. New releases include both LAN feed assets automatically; `-PublishLan` and the legacy `Publish-LanUpdate.ps1` wrapper remain compatible with older staged releases, but the flag is unnecessary for a new release.

Run `scripts/Sync-LanUpdate.ps1` in PowerShell 7 to refresh a peer's local mirror from GitHub without rebuilding, publishing or accessing the private key. It verifies the signed `mirror.json`, host-specific feed, package and complete EXE before exposing the new feed. Enable recurring refresh locally with `scripts/Install-LanUpdateStartup.ps1 -EnableMirror` from administrator PowerShell 7. The original PC must first fetch and integrate the current scripts, preserve its existing `.76` configuration, then perform this one-time setup; source synchronization alone does not activate mirror refresh. See [LAN setup and synchronization](lan-updates.md#publishing-and-peer-synchronization).

## Artifacts, recovery and verification

Every release produces the complete `dist/HappyCheats.exe`, a versioned `dist/HappyCheats-<version>.exe`, the single-EXE ZIP and the complete directory-format update ZIP. GitHub receives the signed `latest.json`, host-specific `latest-lan-76.json` and `latest-lan-59.json`, signed `mirror.json`, the exact update ZIP, complete standalone EXE and single-EXE ZIP. The publishing PC installs its host-specific signed feed as local `latest.json`, with the identical update ZIP and complete EXE. Shared `main` must contain the package's source commit, and the release tag must identify that same commit. The smaller executable inside the unpacked client folder is not a standalone distribution.

Interrupted publishing retains verified staged artifacts under `artifacts/releases/<version>/`. Follow the script's recovery instructions and resume with the original source and targets. Reuse the staged bytes rather than rebuilding a published version. Published archives and assets are immutable. If GitHub succeeds but the local mirror fails, report that incomplete step and retry the immutable stage or verified mirror helper. An offline peer can catch up after its configured refresh succeeds. Report exactly which source, hosting or artifact step remains incomplete if a step fails.

A process interruption or network failure can leave the GitHub publishing lock behind. Before manual recovery, verify that **neither PC is publishing**, then inspect the lock tag's version, source and creation time. Only after confirming it is abandoned, explicitly remove **only** `refs/tags/happycheats-publish-lock`; preserve all versioned release tags, drafts, assets and local staged files. Retry the original verified stage. Never remove a live lock or treat its age alone as permission to take it over.

Before calling a release complete, verify hosted signatures and channels, anonymous GitHub downloads, the local LAN feed and package, package sizes/hashes, the ordered three-source configuration, stale/offline LAN handling, matching-package fallback, trusted configuration migration, fresh EXE version and a suitable packaged-client smoke check. Report an offline or untested peer separately. Synthetic migration and network tests do not establish second-PC or physical-network validation. No game launch or save change is part of update verification.

Existing chats receive these checked-in instructions when they next read them; running chats can retain an older snapshot. Re-read the current instructions: publish to shared GitHub and the local LAN, let online peers mirror the verified GitHub release, and keep both LAN endpoints plus GitHub in future official clients.
