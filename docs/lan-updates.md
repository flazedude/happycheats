# Legacy LAN compatibility server

**LAN publication is no longer required from 30 October 2026.** Normal releases use GitHub only through `scripts/Publish-Update.ps1`; official clients from **0.3.52** check GitHub directly. The user wants the existing server and **HappyCheats LAN Update Server** scheduled task kept running. Preserve their configuration, established signing key, old archives and recovery backups. Keeping this service available does not require publishing every future release to LAN. See [GitHub migration and publishing](github-updates.md).

The one-time migration bridge serves the identical signed GitHub **0.3.52** feed at the existing LAN `latest.json`; its package URL points to the immutable GitHub ZIP. Old LAN clients can discover that update while the server remains online. Once updated, official clients use GitHub directly, so the bridge needs no ongoing LAN releases. Clients **0.3.46–0.3.51** can also use GitHub fallback when LAN is unavailable. Other installations can obtain the latest complete standalone EXE from GitHub. Preserve the signed feed unchanged rather than rebuilding or re-signing 0.3.52.

One Windows PC hosts signed releases. Other PCs on the same network install the configured starter once and then use **Updates / 客户端更新**, followed by **Install & restart**. No cloud account, domain or internet-facing port forwarding is needed. The host needs .NET 8 SDK or later for building/publishing. Run the scripts in PowerShell 7 (`pwsh`). Published clients and server include their runtimes.

## Start the host

```powershell
.\scripts\Start-LanUpdateServer.ps1 -Address 192.168.2.76 -OpenFirewall
```

Use the host's actual private IPv4 address. Omitting `-Address` detects a single physical connection with a gateway. Initial setup saves the address/port (default **17845**), generates a persistent RSA signing key outside the repository, builds the server and starts it in the background. Later invocations reuse the same address and key.

`-OpenFirewall` creates a rule when run as administrator, restricted to this executable, local address/port and clients in `LocalSubnet`. It applies to all Windows network profiles because home Wi-Fi is sometimes classified Public. Existing rules are not removed or widened.

For the existing compatibility server, retain the reserved router address. The Windows scheduled task **HappyCheats LAN Update Server** remains enabled and starts the server at boot before sign-in, after a 30-second delay, then waits for the configured Wi-Fi address. Its supervisor retries an exited server after 15 seconds, without a retry limit. The task has no runtime limit and runs on battery power too. The migration feed is reachable while this PC is on; direct GitHub updates and package downloads do not depend on it.

To install or repair boot startup, run `scripts/Install-LanUpdateStartup.ps1` once from administrator PowerShell. It uses the current user's local file permissions through S4U, with no stored password and no interactive sign-in requirement. It runs the already published server with Windows' built-in PowerShell; it does not build code or read signing keys at boot. Keep this checkout and the published server at their current paths.

`scripts/Start-LanUpdateServer.ps1` reuses/starts the scheduled task. `scripts/Stop-LanUpdateServer.ps1` stops the task and its server until manually started or the next boot. For permanent disabling, disable **HappyCheats LAN Update Server** in Windows Task Scheduler. Startup failures are recorded in `artifacts/lan-updates/startup-error.log` and Task Scheduler's last-run result.

## Legacy opt-in publishing

Project policy in `AGENTS.md` now requires a signed GitHub release, source sync and a fresh standalone EXE. LAN publication is not required. Only after a new explicit user request, add `-PublishLan` to the current publisher to include the legacy host. Substitute a strictly higher, unpublished version and matching English/Chinese note files:

```powershell
.\scripts\Publish-Update.ps1 -Version <new-version> -PublishLan `
  -NotesEnglish .\docs\releases\<new-version>.en.txt `
  -NotesChinese .\docs\releases\<new-version>.zh.txt
```

`-SkipPayloads` reuses already built trainer payloads. Omit it when rebuilding changed cheats; game-specific build prerequisites still apply. Tests run by default. The opt-in command requires reviewed, committed source, verifies/pushes it to GitHub, builds a client with the saved LAN feed/public key and GitHub fallback, then stages and publishes both signed feeds and the complete distributions. The legacy `Publish-LanUpdate.ps1` wrapper selects the same opt-in mode. See the [normal GitHub workflow](github-updates.md). Simultaneous publishing and equal/older versions are rejected. Optional `-NewGame 'Game name'` accepts multiple display names; the actual game implementation and payload must also be included in the project.

While the legacy host is available, another PC can open its retained starter:

```text
http://192.168.2.76:17845/happycheats/HappyCheats.exe
```

The retained LAN starter is not refreshed by normal GitHub publishing; use the [latest complete EXE on GitHub](https://github.com/flazedude/happycheats/releases/latest/download/HappyCheats.exe) for a current starter. Every release requires new changelog files and a higher unpublished version. Clients check at startup or when **Updates** is opened. After updating the client, use **Install / update** for affected games to apply new trainers.

Older HTTPS/loopback-only clients cannot pull a LAN HTTP release themselves: distribute the new starter once. Previously cloud-configured clients also need the LAN starter/configuration, since updates preserve their trusted feed. User settings remain in `%LOCALAPPDATA%/HappyCheats/settings.json`.

## Files and troubleshooting

- `artifacts/lan-updates/server.json`: saved address, port and paths (not secret key material).
- `artifacts/lan-updates/public/`: release ZIPs, signed feed and starter EXE.
- `%LOCALAPPDATA%/HappyCheatsPublisher/LAN/`: persistent signing keys. Back up the private key; replacing it breaks existing clients' trust.
- `artifacts/lan-updates/server.log` and `server-error.log`: server logs.
- `http://192.168.2.76:17845/health`: connectivity check from another LAN PC.

If only the host can connect, run the start command once as administrator with `-OpenFirewall`, verify both PCs are on the same LAN, and check router guest/client isolation. A proxy/VPN may require a private-address bypass. No router port forwarding is needed.

To migrate addresses, stop the server, edit `Address` and `BaseUrl` in `server.json`, and start again. Rebuild/distribute configured clients for the new address; existing clients retain their configuration. Remove the old dedicated firewall rule (`Remove-NetFirewallRule -Name HappyCheats-LAN-Updates`) and recreate it using `-OpenFirewall` when changing address/port.

HTTP is supported only on literal RFC1918 private IPv4 addresses and loopback; public endpoints still require HTTPS. LAN downloads retain signature and SHA-256 checks. HTTP does not encrypt downloads; signatures prevent substituting release content but do not prevent blocking or replaying an older feed. The server allows GET/HEAD for release filenames only, with no directory listing or upload API. Feed/starter responses bypass caching; versioned archives support ranges and immutable caching. See [recovery instructions](updates.md#recovery-and-retained-backups).

Run `scripts/Test-LanUpdateServer.ps1` for real HTTP checks. `tests/HappyCheats.Tests` also tests private address boundaries and signed LAN downloads.

