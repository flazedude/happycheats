# Two LAN hosts and verified GitHub mirrors

The user's latest **30 October 2026** preference restores LAN publication alongside GitHub and adds GPD-PC as a second host. From **0.3.54**, official clients check all three sources below, select the newest trusted version and prefer a matching LAN package. A reachable stale LAN feed cannot hide a newer GitHub release. GitHub remains available when either PC is off. See [client verification and publishing](github-updates.md).

| Order | Host | Signed feed |
| --- | --- | --- |
| 1 | JACK-PC | `http://192.168.2.76:17845/happycheats/latest.json` |
| 2 | GPD-PC | `http://192.168.2.59:17845/happycheats/latest.json` |
| 3 | GitHub | `https://github.com/flazedude/happycheats/releases/latest/download/latest.json` |

Both hosts share the established signing identity and GitHub repository, but keep their address, public directory, process record and startup task local. Earlier GPD records use the Windows name `JACKGPD`; do not rename the computer or copy JACK-PC's `server.json` to it. Preserve existing keys, task configuration, immutable archives and recovery backups. Source synchronization, HTTP serving, mirror refresh and client installation are separate operations.

The host needs .NET 8 SDK or later for building/publishing. Run interactive scripts in PowerShell 7 (`pwsh`); published clients and HTTP servers include their runtimes. The mirror helper needs PowerShell 7 and the public verification key, but no SDK, GitHub authentication or private key.

## Start the host

```powershell
.\scripts\Start-LanUpdateServer.ps1 -Address 192.168.2.59 -OpenFirewall
```

The example is for GPD-PC. On JACK-PC, retain `192.168.2.76`; do not replace it with `.59`. Omitting `-Address` detects a single physical connection with a gateway. Initial setup saves the local address/port (default **17845**), reuses the verified signing-key paths from that PC's publisher configuration, builds the server and starts it in the background. It does not generate a new signing identity. Both PCs already have publisher key setup; use it. Later invocations preserve the saved address and key.

Run host setup locally, then install or repair boot startup as described below. Firewall changes remain an explicit setup action: `-OpenFirewall` is limited to the host's address/port and local subnet. A successful local health request does not verify another PC's access or the boot task.

`-OpenFirewall` creates a rule when run as administrator, restricted to this executable, local address/port and clients in `LocalSubnet`. It applies to all Windows network profiles because home Wi-Fi is sometimes classified Public. Existing rules are not removed or widened.

Retain each reserved router address. The Windows scheduled task **HappyCheats LAN Update Server** starts the server at boot before sign-in, after a 30-second delay, then waits for the configured network address. Its supervisor retries an exited server after 15 seconds, without a retry limit. The task has no runtime limit and runs on battery power too. A LAN host is reachable while that PC is on; GitHub downloads do not depend on it.

To install or repair startup and enable peer mirror refresh, run this once from administrator **PowerShell 7** on that host:

```powershell
.\scripts\Install-LanUpdateStartup.ps1 -EnableMirror
```

It uses the current user's local file permissions through S4U, with no stored password and no interactive sign-in requirement. The task runs the published HTTP server through Windows' built-in PowerShell. Its supervisor launches the saved PowerShell 7 mirror helper when enabled; that helper uses only the public key. It does not build code or read the private key at boot. Keep the checkout, published server and saved PowerShell 7 runtime at their configured paths. Preserve an existing compatible task; do not replace an unrelated task with the same name.

`scripts/Start-LanUpdateServer.ps1` reuses/starts the scheduled task. `scripts/Stop-LanUpdateServer.ps1` stops the task and its server until manually started or the next boot. For permanent disabling, disable **HappyCheats LAN Update Server** in Windows Task Scheduler. Startup failures are recorded in `artifacts/lan-updates/startup-error.log` and Task Scheduler's last-run result.

## Publishing and peer synchronization

Project policy in `AGENTS.md` requires source sync, a signed GitHub release, a fresh standalone EXE and the publishing PC's local LAN mirror. The normal publisher uses all three client feeds and requires that PC's saved LAN configuration. Substitute a globally unique, strictly higher unpublished version and matching English/Chinese notes:

```powershell
.\scripts\Publish-Update.ps1 -Version <new-version> `
  -NotesEnglish .\docs\releases\<new-version>.en.txt `
  -NotesChinese .\docs\releases\<new-version>.zh.txt
```

The command builds/tests once, stages the complete distributions, signs one GitHub feed and a host-specific feed for each LAN, then publishes verified GitHub assets before installing the local mirror. All three feeds identify the same update ZIP size and SHA-256. A signed `mirror.json` covers the feed assets, complete EXE, update ZIP and other release artifacts, allowing a peer to verify them without signing anything itself. GitHub stores `latest-lan-76.json` and `latest-lan-59.json`; each host installs its own feed as `latest.json`. The local mirror contains the exact published ZIP and complete EXE. Feed URLs and signatures differ by host; package bytes do not.

`-SkipPayloads` is allowed only when payloads are unchanged or every changed payload was explicitly rebuilt and validated. Tests run by default. Reviewed release changes must be committed, pushed and integrated into shared `main`; the repository-wide publish lock prevents concurrent releases. `-PublishLan` and `Publish-LanUpdate.ps1` remain compatible with older staged releases, but the flag is unnecessary for a new release. See the [source and release workflow](github-updates.md). Optional `-NewGame 'Game name'` accepts multiple display names; the actual implementation and payload must also be included.

To refresh this host from the latest signed GitHub release without rebuilding or publishing, run in PowerShell 7:

```powershell
.\scripts\Sync-LanUpdate.ps1
```

The helper reads local `server.json`, downloads the signed `mirror.json`, verifies its established key, repository, stable channel, version and source commit, then downloads the host-specific feed, update ZIP and complete EXE from that version's pinned GitHub release. It verifies signed sizes/hashes, the LAN feed's exact endpoint and package identity, and the package's source metadata before exposing the new feed. It refuses downgrade, conflicting same-version feeds and changed immutable archives. Old feed/EXE copies are retained as backups. GitHub failures or invalid assets leave the existing published feed available; retry when the cause is resolved. Releases before 0.3.54 lack `mirror.json`, so refresh can report a missing asset until the first new-format release is published. A failed local mirror after GitHub publication is an incomplete release step: rerun the original publisher against its immutable stage or run the mirror helper.

With `-EnableMirror`, the boot supervisor runs this refresh when the server starts, then **300 seconds after each completed attempt** by default. `-MirrorIntervalSeconds` accepts 60–86400 seconds during setup. A refresh exceeding 30 minutes is stopped and retried after the interval; HTTP serving continues. The helper shares the local publication lock, so a release or another refresh cannot write the same mirror concurrently. This mechanism must be enabled locally on each PC; pulling source or starting an ordinary server does not enable it automatically. On the original PC, first fetch and integrate the current scripts, preserve its `.76` configuration, then run `Install-LanUpdateStartup.ps1 -EnableMirror` once from administrator PowerShell 7. Offline peers catch up after they come online and successfully refresh.

When the corresponding mirror is current and the host is reachable, another LAN PC can download either complete starter:

```text
http://192.168.2.76:17845/happycheats/HappyCheats.exe
http://192.168.2.59:17845/happycheats/HappyCheats.exe
```

The [latest complete EXE on GitHub](https://github.com/flazedude/happycheats/releases/latest/download/HappyCheats.exe) remains available independently. Every release requires new changelog files and a higher unpublished version. Clients check at startup or when **Updates** is opened; download and **Install & restart** remain user actions. After updating the client, use **Install / update** for affected games to apply new trainers.

Known official earlier configurations migrate at runtime only when their key and channel match the bundled defaults. Custom configurations are preserved. Older clients without a compatible update route need the latest standalone EXE once. User settings remain in `%LOCALAPPDATA%/HappyCheats/settings.json`.

## Historical bridge and setup status

The one-time **0.3.52** migration bridge served the identical signed GitHub feed at `.76`, with its ZIP URL pointing to GitHub. Versions **0.3.52–0.3.53** used GitHub directly; **0.3.46–0.3.51** could fall back to GitHub when LAN failed. Preserve those published artifacts and backups; do not rebuild a different package under an old version.

During 0.3.54 preparation on 30 October 2026, the `.59` HTTP server answered local health checks, while `.76` still served the 0.3.52 bridge. The original PC's automatic mirror setup remains to be installed and verified locally. These observations do not establish cross-PC access, firewall configuration, boot startup or successful future synchronization. Check each host's current signed feed and task state when assessing deployment.

## Files and troubleshooting

- `artifacts/lan-updates/server.json`: saved address, port and paths (not secret key material).
- `artifacts/lan-updates/public/`: release ZIPs, signed feed and starter EXE.
- `%LOCALAPPDATA%/HappyCheatsPublisher/publisher.json`: machine-local protected key paths; existing older key locations remain valid. Back up the private key; replacing it breaks existing clients' trust.
- `artifacts/lan-updates/server.log` and `server-error.log`: server logs.
- `artifacts/lan-updates/mirror.log`, `mirror-error.log` and `mirror-supervisor.log`: refresh output and supervisor results.
- `artifacts/lan-updates/mirror-cache/` and `mirror-backups/`: verified release downloads and retained previous public files.
- `http://192.168.2.76:17845/health` and `http://192.168.2.59:17845/health`: connectivity checks, which must also be tested from another PC before claiming cross-PC access.

If only the host can connect, run the start command once as administrator with `-OpenFirewall`, verify both PCs are on the same LAN, and check router guest/client isolation. A proxy/VPN may require a private-address bypass. No router port forwarding is needed.

Address changes require a coordinated update to server configuration, signed host feeds and bundled client defaults; the current mirror accepts only the two official endpoints above. Do not silently change saved addresses or broaden firewall access. Preserve old configuration and artifacts during any explicitly requested migration.

HTTP is supported only on literal RFC1918 private IPv4 addresses and loopback; public endpoints still require HTTPS. LAN downloads retain signature and SHA-256 checks. HTTP does not encrypt downloads; signatures prevent substituting release content but do not prevent blocking or replaying an older feed. The server allows GET/HEAD for release filenames only, with no directory listing or upload API. Feed/starter responses bypass caching; versioned archives support ranges and immutable caching. See [recovery instructions](updates.md#recovery-and-retained-backups).

Run `scripts/Test-LanUpdateServer.ps1` for real HTTP checks. `tests/HappyCheats.Tests` also tests private address boundaries and signed LAN downloads.

