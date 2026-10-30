# GitHub updates and publishing

From **0.3.52**, official HappyCheats clients check public GitHub Releases directly. Downloads remain available while the publisher's PC is off. Normal LAN publishing is no longer required. The existing LAN server and scheduled startup task remain running for compatibility, as requested on **30 October 2026**. Downloads still require **Download update** and **Install & restart**.

- [Source and releases](https://github.com/flazedude/happycheats)
- [Stable signed feed](https://github.com/flazedude/happycheats/releases/latest/download/latest.json)
- [Latest complete standalone EXE](https://github.com/flazedude/happycheats/releases/latest/download/HappyCheats.exe)

The repository is public so clients can download releases without a GitHub account or token. Publishing authentication stays on the operator's PC. Never include tokens or the private signing key in source, packages or a public directory.

## Verification and downloads

GitHub releases retain the established signing key. The signed feed points to an immutable version-specific package URL such as `/releases/download/v0.3.52/HappyCheats-0.3.52-win-x64.zip`. The client verifies the signature, channel, archive size and SHA-256 before installation, then validates the complete package structure. Invalid signatures, channels, hashes and archive structure remain errors.

Internet requests use a 60-second feed/response deadline and a 60-second read inactivity limit; the update window also bounds its overall check. A continuously progressing package download is not limited to 60 seconds in total. Cancellation ends the operation. If GitHub is unavailable, the installed client remains usable and the user can retry later. Official 0.3.52 clients do not retry the legacy LAN server.

## Existing installations

Official **0.3.46–0.3.51** clients already include GitHub fallback. When LAN is unavailable, they can discover and install 0.3.52 through that fallback after their existing LAN timeout. The existing LAN server also provides a one-time migration bridge: its `latest.json` serves the identical signed GitHub **0.3.52** feed, whose package URL downloads the published ZIP from GitHub. This avoids a stale LAN feed hiding the migration release while allowing the server and startup task to remain running. After this update, official clients check GitHub directly; future releases do not require further LAN publication.

**Pre-0.3.46** clients do not know the GitHub feed endpoint. They need a reachable compatible LAN configuration to discover the bridge, or can run the [latest complete standalone EXE](https://github.com/flazedude/happycheats/releases/latest/download/HappyCheats.exe) once to obtain the configured client. User preferences remain in `%LOCALAPPDATA%/HappyCheats/settings.json`.

The updater continues preserving `update-settings.json` and its trusted signing key. When 0.3.52 loads bundled `update-defaults.json`, it switches the effective primary endpoint to GitHub only if all of these conditions hold:

- The saved primary feed is exactly the original official LAN endpoint, `http://192.168.2.76:17845/happycheats/latest.json`.
- The bundled default is the official GitHub feed above, without a fallback.
- The saved and bundled RSA public keys and update channels match.
- The saved fallback is absent or is the official GitHub feed.

This migration does not rewrite the preserved settings file. Custom primary feeds, explicit custom fallbacks and mismatched signing identities remain unchanged. Existing generic fallback support remains available for custom configurations. Fresh official standalone releases contain GitHub as their primary feed, without a LAN fallback.

## Developing on either PC

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

The established signing key has already been transferred to the second PC, as confirmed by the user on 30 October 2026. Use its existing local key path with the initializer below; another transfer is unnecessary. Publisher initialization and release validation on that PC remain unverified.

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

The standing workflow in [AGENTS.md](../AGENTS.md) applies to every chat/window sharing this checkout. Re-read it from disk before implementation and before releasing. Each completed client or trainer change requires source sync, a signed public GitHub Release and a fresh complete standalone EXE. Documentation-only changes require source sync but no new binary release.

1. Sync source using the workflow above and inspect GitHub Releases, drafts, tags and retained local release records, including the last LAN feed during migration. Choose a strictly higher unused version than every previously published version and align app/build defaults. Preserve other chats' unfinished work.
2. Rebuild changed payloads, write English and Chinese notes and finish relevant checks.
3. Review and commit the intended source, integrate the release changes into `main`, then push normally and verify `origin/main` contains the release commit. Publishing requires a clean checkout matching that integrated commit and verifies source provenance; never commit unrelated unfinished work to satisfy this check.
4. Run in PowerShell 7, substituting the new version and matching notes:

   ```powershell
   .\scripts\Publish-Update.ps1 -Version 0.3.52 `
     -NotesEnglish .\docs\releases\0.3.52.en.txt `
     -NotesChinese .\docs\releases\0.3.52.zh.txt
   ```

The command builds and tests once, signs the GitHub feed, stages complete artifacts, then uploads and verifies a GitHub draft before publishing it. It does not start the LAN server or publish files to the old LAN public directory. Packages include `release-source.json` identifying the Git commit. The default repository is `flazedude/happycheats`.

The publisher prefers the machine-local `%LOCALAPPDATA%\HappyCheatsPublisher\publisher.json` created by the initializer. For the original PC's existing setup, it can still read established signing-key paths from `artifacts/lan-updates/server.json`; that compatibility path does not require a running LAN server. Preserve the old configuration, established key, archives and recovery backups. If a key is missing, restore it rather than generating a replacement. The private key stays outside the repository and public download directories.

Use `-SkipPayloads` only when payloads are unchanged or every changed payload was rebuilt and validated in the current task. Do not use `-SkipTests` for normal releases. `-PublishLan` and the legacy `Publish-LanUpdate.ps1` wrapper are retained solely for a future explicit user request to publish to LAN; they are not part of normal releases.

## Artifacts, recovery and verification

Every release produces the complete `dist/HappyCheats.exe`, a versioned `dist/HappyCheats-<version>.exe`, the single-EXE ZIP and the complete directory-format update ZIP. GitHub receives the signed `latest.json`, the exact update ZIP, complete standalone EXE and single-EXE ZIP. Shared `main` must contain the package's source commit, and the release tag must identify that same commit. The smaller executable inside the unpacked client folder is not a standalone distribution.

Interrupted publishing retains verified staged artifacts under `artifacts/releases/<version>/`. Follow the script's recovery instructions and resume with the original source and targets. Reuse the staged bytes rather than rebuilding a published version. Published archives and assets are immutable. Report exactly which source, hosting or artifact step remains incomplete if a step fails.

A process interruption or network failure can leave the GitHub publishing lock behind. Before manual recovery, verify that **neither PC is publishing**, then inspect the lock tag's version, source and creation time. Only after confirming it is abandoned, explicitly remove **only** `refs/tags/happycheats-publish-lock`; preserve all versioned release tags, drafts, assets and local staged files. Retry the original verified stage. Never remove a live lock or treat its age alone as permission to take it over.

Before calling a release complete, verify the hosted signature and channel, anonymous GitHub downloads, package sizes/hashes, direct GitHub configuration, trusted LAN-settings migration, fresh EXE version and a suitable packaged-client smoke check. Synthetic migration and network tests do not establish second-PC or physical-network validation. No game launch or save change is part of update verification.

Existing chats receive these checked-in instructions when they next read them; running chats can retain an older snapshot. Re-read the current instructions: GitHub is the required publishing target, and the existing LAN service may remain running without ongoing LAN releases.
