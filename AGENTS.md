# HappyCheats project instructions

## Standing release preference (user instruction, 26 September 2026)

For work on HappyCheats in any conversation, treat a completed client update or newly developed/changed cheat as requiring BOTH a LAN update release and a freshly built standalone EXE. The user has authorized this normal release workflow; do not ask again whether to build the EXE or publish the completed release to the existing LAN server. Follow later user instructions when they request development-only work, deferred publishing or a different scope. Documentation-only and instruction-only changes do not themselves require a release.

Read `docs/lan-updates.md`, `docs/updates.md` and `docs/single-exe.md` before release work. Use the existing local server and scripts; the earlier proposed Aliyun/OSS hosting is not the selected deployment target.

## Release workflow

1. Inspect the current source/build versions and the latest published feed in the saved server's public directory. Choose a strictly higher `major.minor.patch` version. Never reuse a published version or overwrite its signed archive. Keep the version defaults in `scripts/Build.ps1` and `src/HappyCheats.App/HappyCheats.App.csproj` aligned with the new release.
2. Finish the requested feature, rebuild every changed trainer payload, and run relevant checks. New games require a real module, catalog registration, payload/build integration and tests; a changelog entry alone does not add a game. Preserve unrelated user changes and existing trainer behavior.
3. Write English and Chinese notes in `docs/releases/<version>.en.txt` and `<version>.zh.txt`. Record what changed and distinguish offline validation from actual gameplay verification. Update user-facing version/download references as appropriate.
4. Run `scripts/Start-LanUpdateServer.ps1` to reuse or start the configured host. Read `artifacts/lan-updates/server.json` for the actual endpoint and key paths. Preserve the existing signing key, trusted client configuration, port and address. The initial endpoint is `http://192.168.2.76:17845/happycheats`; saved configuration is authoritative. Never silently switch hosts or generate a replacement for a missing established signing key.
5. Use `scripts/Publish-LanUpdate.ps1` with the new version and BOTH note files. It builds the full update ZIP and single-EXE distribution, signs the release and publishes it locally. Tests run by default. Do not use `-SkipTests` as the normal release path. Use `-SkipPayloads` only if payloads are unchanged, or all changed payloads were explicitly rebuilt and validated in this task; never ship stale cheats after editing trainer sources.
6. Verify the hosted signed feed/version and downloads, confirm the fresh EXE contains the new version and correct LAN configuration, and perform a suitable packaged-client smoke check. Keep old release ZIPs and recovery backups. Do not claim second-PC connectivity or gameplay validation unless actually tested.
7. In the final response, give the release version, concrete changes, checks/limitations, a clickable local link to the new versioned EXE and the LAN download link. New cheats must be available in both distribution paths, not only in source or DLL files.

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

If prerequisites, tests, payload compilation, signing keys or hosting prevent completion, continue any independent work and report precisely which artifacts are ready and which release step is blocked. Do not publish a broken release, invent validation or quietly omit the standalone EXE. This standing preference does not authorize changing game saves, starting games for testing, or broadening firewall access.

Use PowerShell 7 for interactive repository scripts. Keep private keys outside the repository and served directory; never include their contents in logs or responses. The Windows scheduled task `HappyCheats LAN Update Server` starts the server at boot without sign-in and restarts it after failure. Preserve this task. `scripts/Install-LanUpdateStartup.ps1` installs/repairs it, `Start-LanUpdateServer.ps1` reuses it, and `Stop-LanUpdateServer.ps1` stops the task and server until explicitly started or the next boot. The task runs the Windows PowerShell 5.1-compatible `Run-LanUpdateServer.ps1` using the existing published server; no SDK or signing key access is needed at boot. This is host setup, not a Windows service or a reason to publish a new client version.
