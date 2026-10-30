# Server updates and release publishing

From **0.3.54**, official clients check JACK-PC's LAN feed (`192.168.2.76:17845`), GPD-PC's LAN feed (`192.168.2.59:17845`), then public GitHub Releases. They check every source and choose the newest trusted release, preferring LAN downloads only when the signed version, size and SHA-256 match. A stale LAN feed cannot hide a newer GitHub release. GitHub keeps updates available when the LAN hosts are off. Use `scripts/Publish-Update.ps1` and follow [setup, migration and publishing](github-updates.md). The generic signing commands below are historical setup and diagnostic references; they do not replace the current workflow.

The [LAN host guide](lan-updates.md) covers the two independent servers and verified GitHub mirroring. Each publishing PC distributes the same release bytes to GitHub and its local LAN; an online peer can pull that verified release into its own mirror. Server startup, mirror refresh and client installation are separate operations. Preserve the original servers, private keys, tasks and recovery artifacts. The historical 0.3.52 GitHub-only bridge remains a recovery reference, not the current publication policy.

HappyCheats can check a static HTTPS feed, show English or Chinese release notes and newly supported games, and install a signed Windows x64 release after the user chooses to update. A release contains the launcher, game modules, trainer payloads and a separate updater. An ordinary HTTPS website or object-storage bucket is enough; a database and application server are unnecessary.

Normal releases use public GitHub Releases and the publishing PC's LAN with the existing signing key. Only the public verification key is embedded; private keys and upload credentials remain outside the repository and packages. Each publishing PC uses `%LOCALAPPDATA%\HappyCheatsPublisher\publisher.json` for its protected key paths and its own `artifacts/lan-updates/server.json` for LAN configuration. See [setup on each PC](github-updates.md#one-time-publisher-setup-on-each-pc). Generic builds without `update-settings.json` still run with updates unconfigured.

Configured release clients check after startup and every 30 minutes while running, then automatically download and verify new releases in the background. Open **Updates / 客户端更新** to see progress or release notes while continuing to use the cheat window and shortcuts. Closing the update window keeps the download running. Once verified, a non-blocking prompt offers **Restart & install / 重启并安装** or **Later / 稍后**; choosing Later retains the downloaded update for this app session. Installation happens only after the user chooses it and supported games are closed. After restart, use each affected game's **Install / update** (**安装 / 更新**) action. Exiting HappyCheats cancels any unfinished download. Development runs using `--data-root`, previews and screenshot runs disable server updates.

## Historical first rollout and generic signing reference

These examples document the original updater setup for a separate installation. The existing HappyCheats release workflow already has a signing key and GitHub host: preserve that key and use the [current publisher](github-updates.md#publishing-every-completed-iteration). Do not generate a replacement key or configure another host during normal releases.

Clients released before this update system must obtain one configured starter release manually. They cannot discover a feed retroactively. Build with `-SingleExe` and distribute `dist/HappyCheats.exe` by itself. Its first launch extracts the full configured client into a writable per-user cache, where the existing updater can replace it. Alternatively, distribute the entire starter ZIP for manual extraction. The executable inside the unpacked `dist/HappyCheats/` folder still needs its neighboring files. See [single-EXE packaging](single-exe.md).

1. Generate the signing key once, in a protected location **outside this repository**. Back up the private key securely. On Windows, restrict the folder's permissions to the release operator; do not place it in a shared or publicly synchronized folder.

   ```powershell
   $hcKeyDirectory = Join-Path $env:LOCALAPPDATA 'HappyCheatsPublisher'
   dotnet run --project .\tools\HappyCheats.Release -c Release -- keygen `
     --private-key "$hcKeyDirectory\release-private.pem" `
     --public-key "$hcKeyDirectory\release-public.pem"
   ```

   `keygen` creates a 3072-bit RSA key pair and refuses to overwrite either key file. Only the public key belongs in client packages or source control. Never upload the private key to the download server.

2. Set up an HTTPS location for releases. The following `updates.example.com` address is a **placeholder**; replace it with your actual domain and path. Build the configured starter:

   ```powershell
   $hcFeedUrl = 'https://updates.example.com/happycheats/latest.json'
   .\scripts\Build.ps1 -Version 0.2.0 -Zip -SingleExe `
     -UpdateFeedUrl $hcFeedUrl `
     -UpdatePublicKeyPath "$hcKeyDirectory\release-public.pem"
   ```

   Add `-SkipPayloads` when intentionally reusing already built payloads. The normal build runs the existing offline checks and builds trainer payloads. The outputs include `dist/HappyCheats.exe` for direct distribution and `dist/HappyCheats-0.2.0-win-x64.zip` for server updates. Their bundled `update-settings.json` contains the feed URL, public verification key and `stable` channel. No private signing key is embedded. The `0.2.0` examples below illustrate the initial updater rollout; use the actual new release version and matching release notes when publishing.

3. Sign the starter release and create a local directory ready for upload:

   ```powershell
   dotnet run --project .\tools\HappyCheats.Release -c Release -- publish `
     --package .\dist\HappyCheats-0.2.0-win-x64.zip `
     --version 0.2.0 `
     --base-url 'https://updates.example.com/happycheats/' `
     --private-key "$hcKeyDirectory\release-private.pem" `
     --output .\artifacts\updates\stable `
     --notes-en .\docs\releases\0.2.0.en.txt `
     --notes-zh .\docs\releases\0.2.0.zh.txt
   ```

4. Upload `HappyCheats-0.2.0-win-x64.zip` to that HTTPS directory **first**. Confirm the URL returns the complete ZIP. Then upload `latest.json` to the feed URL. Distribute `dist/HappyCheats.exe` to existing users as the configured starter. The publisher only writes local files; it does not access or configure a hosting account.

For an existing updater-capable installation, you can generate the same public configuration directly:

```powershell
dotnet run --project .\tools\HappyCheats.Release -c Release -- configure `
  --feed-url 'https://updates.example.com/happycheats/latest.json' `
  --public-key "$hcKeyDirectory\release-public.pem" `
  --output 'D:\Apps\HappyCheats\update-settings.json'
```

Restart the client after changing its configuration. Always supply the public PEM to `configure`; it rejects private keys.

## Generic publisher reference

The commands in this section describe the low-level signing tool. Normal HappyCheats releases require both English and Chinese notes and the complete GitHub workflow, including source sync and standalone artifacts.

Choose a strictly increasing `major.minor.patch` version, such as `0.2.1`, and write UTF-8 changelog text files. Prerelease suffixes and leading zeros are not accepted. Build with `-Version 0.2.1`, using the same feed URL and public key, then publish that ZIP with `--version 0.2.1`. Add `--new-game 'Game name'` for each newly supported game; this information appears with the release notes. `--notes-zh` is optional. Use a separate feed URL and `--channel` only when also distributing a matching client channel configuration; the normal setup uses `stable` throughout.

The publisher checks the shared package structure and archive safety rules and verifies that the embedded `HappyCheats.dll` assembly version matches the declared release version. It computes the actual ZIP size and SHA-256, signs the manifest, copies an immutable versioned ZIP into the output directory and atomically replaces local `latest.json`. Publishing an existing archive name is rejected; use a new version for changed content. Use one release-publishing process per feed directory.

Upload the new ZIP before replacing the feed, and keep old ZIPs available for clients that have already read an older feed. With object storage, use a complete object upload before changing `latest.json`; with a filesystem web server, upload to a temporary filename and rename into place. Do not rename, recompress or modify a ZIP after signing it. If a feed must be restored temporarily, restore the previous signed feed and keep its package; clients will not install a version older than their current one. To fix an already installed release, publish the corrected code under a higher version.

Adding cheats to an existing game means updating its module and trainer payload, then publishing the complete bundle. Adding a game also requires its `IGameModule` implementation, registration in `ModuleCatalog`, and inclusion in the build's payload list and relevant payload-building script. `NewGames` is display information; it does not itself implement a game or download executable instructions from changelog text.

After the launcher restarts into the new release, users should close the affected game and choose its **Install / update** action to copy updated trainer files into that game's directory. Updating the client library does not replace trainers inside a running game.

Release **0.3.3** includes the official SMAPI 4.5.2 installer, corresponding source and notices in Stardew's payload. Use `docs/releases/0.3.3.en.txt` and `docs/releases/0.3.3.zh.txt` when publishing this version. The complete-folder server-update ZIP and the single EXE both carry these files. After updating the client, Stardew's **Install / update** can install the bundled loader and HappyCheats mod without downloading setup files; it reuses compatible SMAPI 4.5.2+ installations. **Remove trainer** preserves SMAPI and other mods. This changes the installation workflow; it does not add a new game or establish live gameplay validation.

For single-EXE users, server updates replace the client under `%LOCALAPPDATA%/HappyCheats/Bundles/<bundlehash>/HappyCheats/`. Subsequent launches of that same EXE reuse the updated client. Its embedded release does not change: distributing the original EXE to a new user still starts from its original version. Build a new single EXE when distributing a newer starter. Main settings remain at `%LOCALAPPDATA%/HappyCheats/settings.json`.

## Historical alternative hosting: Alibaba Cloud

This alternative was not selected. It is retained as a historical design reference, not a current hosting recommendation; provider requirements and network performance need fresh verification before any future change. Normal publication uses GitHub and the configured local LAN mirror.

For an initial rollout without an existing mainland ICP filing, consider an Alibaba Cloud OSS bucket in **Hong Kong**, with a custom download domain and HTTPS. Verify download speed from the actual mainland networks your users use; location alone does not guarantee performance. The client needs public HTTPS GET access to the feed and ZIPs. Keep bucket write credentials on the release operator's machine or in the publishing pipeline, never in the client.

Alibaba Cloud documents that custom domains for **mainland China OSS buckets require ICP filing**, while Hong Kong and other non-mainland regions do not. Mainland or global Alibaba Cloud CDN acceleration still requires ICP filing regardless of the origin's location; Hong Kong origin storage does not remove that requirement. Once your filing is ready, mainland OSS and/or mainland CDN can be evaluated for the intended audience. See the official [OSS custom-domain requirements](https://www.alibabacloud.com/help/en/oss/user-guide/access-buckets-via-custom-domain-names) and [CDN limits and domain requirements](https://www.alibabacloud.com/help/en/cdn/product-overview/limits).

Use your custom HTTPS domain as the stable feed address so that storage can be moved behind it later. Upload the immutable ZIP with the OSS console or your deployment tool, then upload `latest.json`. Set the feed object's `Content-Type` to `application/json` and `Cache-Control` to `no-cache, max-age=0, must-revalidate`. Give versioned ZIPs `Cache-Control: public, max-age=31536000, immutable`. If a CDN is added, ensure it respects the feed's cache policy or refresh the feed URL when publishing.

## Historical alternative hosting: static server

This example is retained for reference. A separate web server is not required for GitHub updates.

An HTTPS Nginx server can serve the same files. In an existing TLS-enabled `server` block, this example maps the feed and packages to `/srv/www/updates/happycheats/`:

```nginx
location = /happycheats/latest.json {
    root /srv/www/updates;
    default_type application/json;
    add_header Cache-Control "no-cache, max-age=0, must-revalidate" always;
    try_files $uri =404;
}

location ~ ^/happycheats/HappyCheats-[0-9]+\.[0-9]+\.[0-9]+-win-x64\.zip$ {
    root /srv/www/updates;
    default_type application/zip;
    add_header Cache-Control "public, max-age=31536000, immutable";
    try_files $uri =404;
}
```

Serve downloads without an interactive login page. Do not host private keys under the web root. Public feed and package URLs require HTTPS; private LAN IPv4 addresses and loopback also support HTTP. The client does not require browser CORS headers.

## Trust, package layout and limits

The signed `latest.json` envelope contains `payload` (base64-encoded UTF-8 manifest JSON) and `signature` (base64 RSA PKCS#1 v1.5 / SHA-256 signature over those exact payload bytes). The manifest records schema version, release version, channel, publication time, package URL, byte size, SHA-256, English and Chinese changelogs and newly supported games. The public key pinned in the local configuration verifies the release before its package is installed. Keep the same signing key for future releases; key rotation currently requires a deliberate trusted client-configuration rollout.

Server update packages must include the launcher and `updater/` executables, their managed DLLs/runtime configuration and game payload files, all under a single `HappyCheats/` top-level directory as produced by `Build.ps1 -Zip`. Publish that ZIP through the release publisher; the standalone EXE is a starter distribution format, not an update-feed package. Archives are limited to 2 GiB compressed and 4 GiB unpacked and reject unsafe paths, links, duplicate names and incomplete launcher/updater layouts. The updater checks the downloaded package against its signed size and SHA-256 and retains the installation's existing trusted update configuration even when the archive contains a configuration file.

This first version distributes complete Windows x64 bundles. There are no delta patches, automatic new-game implementation, account-gated downloads, release analytics, automatic signing-key rotation. The application directory must be writable by the current user, and updates need enough free disk space for the download, extracted bundle and previous installation. Keep game saves backed up according to your existing workflow; the release system updates application and payload files, and the per-game installer remains responsible for modifying game directories.

## Recovery and retained backups

Update working directories are siblings of the client folder, under `<client-parent>/.happycheats-updates/<GUID>/`. A successful installation retains the previous client directory there for recovery. Normal replacement exceptions and a detected failure to launch the new client trigger a rollback. Main user settings live outside the application folder and are preserved.

The work directory's `transaction.json` records the installation state; diagnostic failures are written to `%LOCALAPPDATA%/HappyCheats/update-error.log`. An abrupt power loss between directory renames can require manual recovery. Close HappyCheats and its updater, inspect that transaction's retained `previous` directory, and restore it to the original client location if the client folder is missing or incomplete. Preserve the incomplete folder separately before restoring. Do not delete a transaction that is still in progress.

This version does not automatically prune retained backups. Once the new client has been verified and no updater is running, old completed update work directories can be removed manually to recover disk space.
