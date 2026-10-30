# Single-EXE distribution

Give users **`dist/HappyCheats-0.3.58-win-x64-single-exe.zip`**. Extract **`HappyCheats.exe`** from the ZIP, then run it. The ZIP contains only that executable, and its SHA-256 checksum is provided separately. The raw **`dist/HappyCheats.exe`** is also available for direct distribution.

This self-contained Windows x64 file bundles the complete release: the client, all seventeen trainer payloads, updater, runtime files and documentation. Stardew's payload includes the official SMAPI 4.5.2 installer, corresponding source and redistribution notices. It can be copied to another folder or PC by itself. The launcher does not require a separate .NET installation, installation wizard or administrator permission.

On the first launch, the wrapper verifies the embedded archive's hash, extracts it into `%LOCALAPPDATA%/HappyCheats/Bundles/<bundlehash>/HappyCheats/`, and starts the client there. Allow a few seconds for this first extraction; the exact time depends on the PC. Later launches of the same file reuse that folder. This is one file to distribute, with local files created automatically when it runs.

Settings remain at `%LOCALAPPDATA%/HappyCheats/settings.json`, so language, Library sorting and existing settings are preserved. Install the supported game through Steam first. For Stardew Valley, **Install / update** installs the bundled SMAPI 4.5.2 when needed, then the HappyCheats mod, without downloading installation files. Compatible existing SMAPI 4.5.2+ installations are reused. **Launch game** starts SMAPI, and **Remove trainer** leaves SMAPI and other mods in place. The other games keep their existing mod-loader requirements. See [Stardew setup and compatibility](stardew.md).

## Updates

Release **0.3.58** includes Phantom Blade Zero trainer **0.1.2**, replacing the failing player-function lookup with direct local-player possession properties. Redundant getter calls and an arbitrary controller-count limit are removed; unavailable rows show specific English/Chinese reasons. Close the game and choose **Install / update**. The new Lua path still needs confirmation after restart. See [release notes](releases/0.3.58.en.txt).

Release **0.3.57** includes Crimson Desert trainer **0.2.7**, fixing the class check that left Kliff's teleport and movement speed unavailable. Both complete formats include the rebuilt trainer and specific English/Chinese unavailable reasons. Close the game and choose **Install / update**. Read-only inspection confirmed the rejected class; actual teleport travel and landing remain unverified. See [release notes](releases/0.3.57.en.txt).

Release **0.3.56** includes Crimson Desert trainer **0.2.6** for Steam build **26317560 / PE3424** in both complete distributions. It updates exact hook bindings, checks resource metadata before refills and strengthens local-player identity checks. Close the game and choose **Install / update**. Validation is static and synthetic only; gameplay, DLC underwater behavior and other protagonists remain unverified. See [compatibility evidence](crimson-desert-3424.md) and [release notes](releases/0.3.56.en.txt).

Release **0.3.55** automatically downloads updates in the background. The Updates window shows progress without disabling the cheat window; closing it keeps downloads running. After verification, choose **Restart & install** or **Later**. The client checks JACK-PC's LAN feed (`192.168.2.76`), GPD-PC's LAN feed (`192.168.2.59`), then GitHub. It checks every source for the newest trusted version and prefers matching LAN packages; a stale LAN feed cannot hide a newer GitHub release. GitHub downloads remain available while either PC is off. Known official configurations adopt this list only when their signing key and channel match; custom configurations are preserved. Both complete formats include all seventeen games, Dawnwalker trainer **0.4.1** and Phantom Blade Zero trainer **0.1.1**. See [update sources and migration](github-updates.md) and [release notes](releases/0.3.55.en.txt).

Local build **0.3.51** introduced Dawnwalker trainer **0.4.1**, correcting damage activation for zero-base/positive-evaluated attributes and verifying the requested multiplier before reporting success. The previously unpublished correction is included in **0.3.53**. Actual combat remains unverified. See [build notes](releases/0.3.51.en.txt).

Release **0.3.50** includes Phantom Blade Zero trainer **0.1.1**, correcting the loader's deletion-listener array offset that caused the reported startup fatal error. Both complete formats carry the corrected profile and explicit object-array iteration setting. See [crash evidence](phantom-blade-loader.md) and [release notes](releases/0.3.50.en.txt).

Release **0.3.49** adds Phantom Blade Zero trainer **0.1.0**, including the inspected loader profile, health/Sha-Chi refills and a 1–20× attack-power control in both complete formats. See [setup and validation limits](phantom-blade.md) and [release notes](releases/0.3.49.en.txt).

Release **0.3.46** introduced LAN-first updates with public GitHub fallback, and **0.3.52–0.3.53** used GitHub directly. **0.3.55** checks all three configured sources while retaining signing-key and archive verification. Older clients can use their existing update route or the latest complete EXE once. [GitHub download](https://github.com/flazedude/happycheats/releases/latest/download/HappyCheats.exe), [setup and migration](github-updates.md), [historical release notes](releases/0.3.46.en.txt).

Release **0.3.45** includes Crimson Desert trainer **0.2.5**, preventing unchanged-player refreshes from briefly invalidating the identity needed for health refills. It also carries the earlier hook allocation correction. Close the game and choose **Install / update** after opening the updated client. Isolated regression checks pass; the reported death's cause and new-DLL gameplay remain unverified. See [release notes](releases/0.3.45.en.txt).

Release **0.3.44** includes Crimson Desert trainer **0.2.4**, correcting the nearby-memory search limit behind the observed damage and marker hook allocation failures. Close the game and choose **Install / update** after updating HappyCheats. The fix has isolated regression validation; hook installation with the new DLL and gameplay remain unverified. See [release notes](releases/0.3.44.en.txt).

Release **0.3.43** includes Astroneer trainer **0.3.1**, adding exact compatibility recognition for **1.45.1 / Steam build 25599237** while preserving the six controls and both multiplier selectors. Close Astroneer and choose **Install / update** after updating HappyCheats. Both distribution formats include the rebuilt trainer; current-build startup and gameplay remain unverified. See [setup](astroneer.md) and [release notes](releases/0.3.43.en.txt).

Release **0.3.42** includes Astroneer trainer **0.3.0** with **invulnerability** and **3× / 5× / 10× player run speed** in both the client and shared menu. Close Astroneer and choose **Install / update** after updating HappyCheats. Both distribution formats include the rebuilt trainer. See [setup](astroneer.md) and [release notes](releases/0.3.42.en.txt).

Release **0.3.41** adds **100×** Astroneer resource collection alongside **10× / 20×** in the client and shared menu. Both distributions include trainer **0.2.1**. Close Astroneer and choose **Install / update** after updating HappyCheats. See [setup](astroneer.md) and [release notes](releases/0.3.41.en.txt).

Release **0.3.40** adds the shared menu controller shortcut for every game: hold **Menu (☰)** for **0.35 seconds**, then tap **RB**. **Ctrl+Del** remains available. The controller setting and hints appear on every game page; menu access remains available while the trainer is loading or busy. Trainer payloads are unchanged, so no reinstall is needed. See [menu setup](in-game-menu.md) and [release notes](releases/0.3.40.en.txt).

Release **0.3.39** changes the shared menu toggle to **Ctrl+Del** (Delete), replacing Ctrl+Home for all games. Trainers are unchanged; no trainer reinstall is needed. Both distribution formats include the updated client. See [menu setup](in-game-menu.md) and [release notes](releases/0.3.39.en.txt).

Release **0.3.38** includes Astroneer trainer **0.2.0** for the inspected **1.44.4 / Steam build 25119744** executable and a shared **Ctrl+Home** in-game menu. The companion menu uses registered game controls, including confirmed choice and numeric values, in windowed/borderless mode with HappyCheats running. The same trainer and menu are included in the LAN update ZIP and complete standalone EXE. See [Astroneer validation](astroneer.md), [menu setup](in-game-menu.md) and [release notes](releases/0.3.38.en.txt).

Release **0.3.37** adds the experimental Elden Ring offline resource trainer **0.1.0**, with its compiled runtime and compatibility payload in both distribution formats. Includes four controls, direct offline launch, explicit Connect / Disconnect and runtime checks. Gameplay is unverified. See [setup and limitations](elden-ring.md) and [release notes](releases/0.3.37.en.txt).

Release **0.3.36** includes Crimson Desert trainer **0.2.3** in both distribution formats. It moves marker teleport writes past the enclosing movement routine's rollback and observes position after a later complete update. The client displays queued and later outcomes accurately. Close the game and choose **Install / update** before reconnecting. Gameplay remains unverified. See [release notes](releases/0.3.36.en.txt).

Release **0.3.35** adds Civilization VII trainer **0.1.0** with 15 experimental controls and a separate JavaScript Tuner runtime in both distribution formats. Includes the Civ VII food queue, influence, wildcard attributes and celebrations. See [setup and controls](civilization-vii.md), [additional option research](civilization-vii-research.md) and [release notes](releases/0.3.35.en.txt). Gameplay remains unverified.

Release **0.3.34** includes Witcher 3 trainer **0.1.1**, the corrected physical command-file path and immediate command queuing without a foreground-window prerequisite. Close the game and choose **Install / update** once. Live command acknowledgement and cheat effects remain unverified. See [release notes](releases/0.3.34.en.txt).

Release **0.3.33** includes Civilization VI trainer **0.1.1**, relaxed optional connection checks and function-key controls. A real single-player Tuner connection is still required. No reinstall is needed if Tuner is already enabled; reopening the new client loads the updated runtime. See [setup and shortcuts](civilization-vi.md) and [release notes](releases/0.3.33.en.txt).

Release **0.3.31** adds the experimental Witcher 3 Remastered trainer **0.1.0**, with eight controls, exact-build checks and a generated script integration. Both packages contain the same payload. Runtime compilation and gameplay remain unverified. See [setup](witcher-3.md) and [release notes](releases/0.3.31.en.txt).

Release **0.3.29** includes GTA V trainer **0.2.0**, its experimental source backend targeting the current Steam **1.0.1158.16** build, and complete corresponding native source in **Source.zip**. Setup obtains the required author ASI loader automatically; **1.0.1158.13** retains automatic ScriptHookV setup. Actual native startup and gameplay remain unverified. See [GTA setup](gta-v-enhanced.md) and [release notes](releases/0.3.29.en.txt).

Release **0.3.26** adds Project Hospital trainer **0.1.0** and its bundled BepInEx loader to both distribution formats. All five requested controls are included; Max Staff raises the hiring limit to 1,000. See [setup and validation limits](project-hospital.md) and [release notes](releases/0.3.26.en.txt).

The combined release also includes [Hotel Architect](hotel-architect.md) with 24 controls and its bundled UE4SS loader, plus [Civilization VI](civilization-vi.md) with 15 implemented controls and four unavailable raw editors. All three additions are experimental and have offline validation only. Civilization VI setup enables Tuner and disables achievements while enabled.

Release **0.3.25** adds **A–Z / Z–A** Library sorting, with **A–Z** as the default and a saved preference. Both interface languages sort by English game titles; search and the selected game are retained. Trainer payloads are unchanged. See [release notes](releases/0.3.25.en.txt) for the offline validation scope.

Normal releases built by `Publish-Update.ps1` include both LAN feeds, the public GitHub feed and the established verification key in the EXE. A direct `Build.ps1` development build requires explicit feed/key arguments to configure updates. Signed updates operate on the extracted client, and launching the same EXE again keeps using that updated client. See [update sources](github-updates.md).

The original EXE is not rewritten by a server update. Copying it to a new PC, or extracting it again after removing its cache, starts with the release originally bundled in that file. To distribute a newer starter release, build a new single EXE from the newer complete package.

## Building

Build all release artifacts with the Windows .NET 8 SDK and the existing game-specific build prerequisites:

```powershell
.\scripts\Build.ps1 -Version 0.3.58 -Zip -SingleExe
```

To package the existing release folder without rebuilding its trainers:

```powershell
.\scripts\Build-SingleExe.ps1 -PackageDirectory .\dist\HappyCheats -Zip
```

The default input is `dist/HappyCheats/` and the executable output is `dist/HappyCheats.exe`. Adding `-Zip` also writes `dist/HappyCheats-<version>-win-x64-single-exe.zip` and its separate SHA-256 checksum. The version is derived from the client in the input package, so wrapping an older package retains its version. Keep the complete self-contained input folder when building; wrapping an individual executable would leave out required payloads and runtimes. The current client, wrapper and updater release version is **0.3.58**, with Black Myth: Wukong trainer **0.1.1** (all nine initial controls; see [setup and validation limits](wukong.md)), Crimson Desert trainer **0.2.7** (health, stamina, spirit, one-hit damage, movement speed, manual marker teleport and F9 Auto teleport) and Dawnwalker trainer **0.4.1**, which uses direct manual and automatic marker teleport on Steam build **25915545 / patch 3**, with loaded-ground/collision checks and bounded position confirmation. Wukong includes its pinned UE4SS loader, corrected ProcessEvent profile and resource/progression controls. Loader startup and the original connection failure were inspected live; actual cheat effects remain unverified. Crimson Desert item usage remains unavailable. Its custom-pin teleports use current elevation because pins lack terrain height; landing and streaming are unverified. Crimson Desert 0.2.3 moves teleport writes past a movement rollback path and checks position after another complete movement update. Pending, observed and failed outcomes appear in the main message. Hook diagnostics remain available. Gameplay and landing remain unverified. See the [release notes](releases/0.3.36.en.txt) for exact behavior and validation limits. The client and standalone wrapper use the metallic-blue gamepad and wrench icon, with 16–256-pixel sizes for normal and high-DPI displays. SMAPI's installer and notices are embedded along with the rest of the Stardew payload; users do not need a second download or a neighboring SMAPI folder.

`Build.ps1 -Zip -SingleExe` produces both ZIP formats: the single-EXE ZIP for users and `HappyCheats-<version>-win-x64.zip` containing the complete folder in the existing signed server updater's package format. The `dist/HappyCheats/` directory remains the unpacked developer artifact. Users receiving the single EXE do not need either of those complete-folder artifacts beside it. The executable *inside* `dist/HappyCheats/` still relies on its neighboring files; distribute the top-level `dist/HappyCheats.exe` or its single-EXE ZIP instead.




Release **0.3.21** removes only Onimusha's unfinished teleport option and shortcuts. Its four combat controls, utilities and menu preferences remain; other games retain their existing features. See [the release notes](releases/0.3.21.en.txt).

Release **0.3.27** integrates direct-author ScriptHookRDR2 and ASI loader setup for the experimental RDR2 Story Mode trainer **0.1.0**, targeting the verified current Steam build **13773296** / executable **1.0.1491.50**. The standalone EXE includes the installer; first game setup needs internet to download the verified runtime. The runtime DLL is not redistributed. Official Red Dead Online is unimplemented; startup and gameplay remain unverified. See [RDR2 setup and validation limits](rdr2.md).

Release **0.3.23** includes the experimental GTA V Enhanced Story Mode trainer **0.1.0** in both distribution formats. It targets game **1.0.1158.13** and an externally installed matching official ScriptHookV runtime. That initial restriction is superseded by the experimental **0.3.29** source backend for **1.0.1158.16**. GTA Online and invite-only sessions are unsupported. See [GTA setup and validation limits](gta-v-enhanced.md).
