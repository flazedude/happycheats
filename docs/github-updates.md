# LAN-first updates with GitHub fallback

HappyCheats 0.3.46 and later try the configured LAN update feed first. If it cannot be reached, returns an HTTP error or exceeds the four-second primary request deadline, the client checks the public GitHub feed. A valid LAN response saying the client is current does not contact GitHub. Downloads still require the existing **Download update** and **Install & restart** actions.

- Source and releases: https://github.com/flazedude/happycheats
- Stable signed feed: https://github.com/flazedude/happycheats/releases/latest/download/latest.json
- Latest complete standalone: https://github.com/flazedude/happycheats/releases/latest/download/HappyCheats.exe
- LAN download: http://192.168.2.76:17845/happycheats/HappyCheats.exe

The source repository is public so clients can download releases without a GitHub account or token. Publishing authentication stays on the operator's PC. Never include tokens or the private signing key in source, packages or the served directory.

## Verification and interrupted downloads

Both feeds use the existing signing key and describe the same update ZIP bytes. The LAN feed points to the saved LAN address. The GitHub feed points to an immutable version-specific URL such as `/releases/download/v0.3.46/HappyCheats-0.3.46-win-x64.zip`, never a changing latest ZIP.

If the LAN package request fails or stalls, the client can retry via the configured GitHub feed. It verifies that feed's signature and channel and requires the exact same version, archive size and SHA-256 before switching URLs. A partial transfer restarts from zero. If GitHub has advanced to another release, check for updates again. Invalid signatures, channels, hashes and archive structure remain errors. Cancelling does not start another download.

The four-second primary download timeout measures inactivity, not total transfer time. Large packages continue downloading while data arrives. Internet requests use a 60-second feed/response deadline and a 60-second read inactivity limit. If neither host is reachable, the installed client remains usable.

## Existing installations

Pre-0.3.46 clients need one update while LAN is reachable, or users can run the new complete standalone EXE. Old clients cannot discover fallback before receiving the new code.

The updater preserves `update-settings.json`, including the trusted primary endpoint and public key. New packages also carry `update-defaults.json`. The new client supplements a missing fallback URL from bundled defaults only when the primary feed, channel and RSA public key match the preserved settings. An explicit fallback is retained; unrelated configurations are not redirected. Both signed feeds remain compatible with older clients.

## Publishing every completed iteration

The standing workflow in [AGENTS.md](../AGENTS.md) applies to every chat/window sharing this checkout. Re-read it from disk before releasing. Each completed client or trainer change requires source sync, LAN and GitHub releases and a fresh standalone EXE. Documentation-only changes require source sync but no new binary release.

1. Fetch GitHub state and inspect local changes and both hosts' latest versions. Choose a higher version and align app/build defaults. Preserve other chats' unfinished work.
2. Rebuild changed payloads, write English/Chinese notes and finish relevant checks.
3. Review, commit and push the intended source. Publishing requires a clean checkout and verifies source provenance; never silently commit unrelated changes.
4. Run in PowerShell 7:

   ```powershell
   .\scripts\Start-LanUpdateServer.ps1
   .\scripts\Publish-LanUpdate.ps1 -Version 0.3.46 `
     -NotesEnglish .\docs\releases\0.3.46.en.txt `
     -NotesChinese .\docs\releases\0.3.46.zh.txt
   ```

The command builds/tests once and stages both signed feeds and complete artifacts. It uploads and verifies assets in a GitHub draft before publishing. Packages include `release-source.json` identifying the Git commit. The default repository is `flazedude/happycheats`; saved LAN configuration and signing keys remain authoritative.

Use `-SkipPayloads` only when payloads are unchanged or every changed payload was rebuilt and validated in the current task. Do not use `-SkipTests` for normal releases. Upload credentials stay outside client packages.

Interrupted publishing retains staged artifacts. Follow the script's recovery instructions and reuse verified bytes rather than rebuilding a published version. Published archives/assets are immutable. Keep old releases and recovery backups. Report exactly which source/host/artifact steps completed if any step fails.

Finally verify anonymous GitHub downloads and both signed feeds, exercise LAN preference and unreachable-LAN fallback, compare hashes and smoke-test the packaged client. Simulated LAN failure on the host does not establish access from another PC/network. No game launch or save change is part of update verification.

Existing chats see these checked-in instructions when they next read them; running chats can retain an older snapshot. The shared publisher enforces release checks independently of that snapshot.
