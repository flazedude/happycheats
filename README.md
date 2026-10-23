# HappyCheats

HappyCheats **0.3.46** checks LAN first and falls back to GitHub when LAN is unavailable. Both routes verify signatures and package hashes. [Download the latest complete EXE](https://github.com/flazedude/happycheats/releases/latest/download/HappyCheats.exe) or use the [LAN download](http://192.168.2.76:17845/happycheats/HappyCheats.exe). Existing clients need one LAN update or the new EXE to gain fallback. See [setup and publishing](docs/github-updates.md) and [release notes](docs/releases/0.3.46.en.txt).

HappyCheats **0.3.45** includes Crimson Desert trainer **0.2.5**, correcting a possible missed health refill during unchanged-player refreshes. It also includes the earlier damage/marker hook allocation fix. Close the game, open the updated client and choose **Install / update** before reconnecting. The correction has isolated regression validation; the reported death and gameplay with the new DLL remain unverified. See [Crimson Desert setup](docs/crimson-desert.md) and [release notes](docs/releases/0.3.45.en.txt).

HappyCheats **0.3.44** includes Crimson Desert trainer **0.2.4**, fixing the nearby-memory search limit behind the observed damage and map-marker hook allocation errors. Close Crimson Desert, update HappyCheats and choose **Install / update**, then launch the game yourself and reconnect. The correction passes isolated regression fixtures; live hook installation with the new DLL and gameplay remain unverified. See [Crimson Desert setup](docs/crimson-desert.md) and [release notes](docs/releases/0.3.44.en.txt).

HappyCheats **0.3.43** recognizes Astroneer **1.45.1 / Steam build 25599237** with trainer **0.3.1**. The previous exact executable check rejected this game update even though the installed trainer files were intact. Close Astroneer and choose **Install / update** after updating HappyCheats. The six controls, independent collection/run-speed choices, and **Ctrl+Del / Menu+RB** menu shortcuts remain unchanged. Current-build startup and gameplay remain unverified. See [Astroneer setup](docs/astroneer.md) and [release notes](docs/releases/0.3.43.en.txt).

HappyCheats **0.3.42** adds Astroneer **invulnerability (F5)** and **player run speed (F6)** with **3× / 5× / 10×** choices. Both are available in the client and shared **Ctrl+Del** menu. Choose a speed, then enable it; disabling restores the captured original values. Both controls start off and reset on gameplay-session changes. Close Astroneer and choose **Install / update** for trainer **0.3.0**. Actual gameplay remains unverified. See [Astroneer setup](docs/astroneer.md) and [release notes](docs/releases/0.3.42.en.txt).

HappyCheats **0.3.41** adds **100×** Astroneer terrain-resource collection alongside **10× / 20×** in the client and shared in-game menu. Trainer **0.2.1** applies the selected rate to new collection events; choosing a rate does not enable collection. Close Astroneer and choose **Install / update** after updating the client. The **Ctrl+Del** and **Menu+RB** menu shortcuts remain available. Live 100× gameplay remains unverified. See [Astroneer setup](docs/astroneer.md) and [release notes](docs/releases/0.3.41.en.txt).

HappyCheats **0.3.40** adds the same Xbox/XInput shared-menu shortcut to every current and future game: enable **Xbox shortcuts**, **hold Menu (☰) for 350 ms, then tap RB**. **Ctrl+Del** is unchanged. Onimusha's Menu+RB now opens the shared menu; F6 keeps its native status panel, and existing Onimusha/Dawnwalker direct cheat chords remain. This shortcut only shows or hides the menu; use mouse and keyboard for its controls. Trainers are unchanged, so no reinstall is needed. Physical-controller and gameplay validation remain pending. See [menu setup](docs/in-game-menu.md) and [release notes](docs/releases/0.3.40.en.txt).

HappyCheats **0.3.39** changes the shared menu toggle to **Ctrl+Del** (Delete) for every game. It replaces Ctrl+Home. Menu labels and instructions are updated; trainers are unchanged, so no trainer reinstall is needed. See [menu setup](docs/in-game-menu.md) and [release notes](docs/releases/0.3.39.en.txt).

HappyCheats **0.3.38** updates Astroneer compatibility for **1.44.4 / Steam build 25119744** and adds a shared **Ctrl+Home** in-game menu for the library. The menu uses each game's registered controls, including Astroneer's confirmed **10× / 20×** selector, and supports windowed/borderless play while HappyCheats is running. Close Astroneer and choose **Install / update** to apply trainer **0.2.0**. See [Astroneer compatibility and validation](docs/astroneer.md), [menu setup](docs/in-game-menu.md) and [release notes](docs/releases/0.3.38.en.txt).

HappyCheats **0.3.37** adds **Elden Ring** with four experimental offline resource controls: refill HP, FP and stamina, and restore all three once. Includes explicit connection controls, a direct offline launcher, version and offline-session checks, and Chinese/English UI. Setup leaves game files and saves unchanged; actual gameplay is unverified. Both distributions include the trainer. See [setup and limitations](docs/elden-ring.md) and [release notes](docs/releases/0.3.37.en.txt).

HappyCheats **0.3.36** corrects Crimson Desert's teleport update order and premature success message. Trainer **0.2.3** writes after the game's enclosing movement routine, then checks position after another complete update. Manual and auto teleport share the correction. Close the game, update HappyCheats and choose **Install / update** before reconnecting. Offline validation does not establish successful travel or landing on the test PC. See [release notes](docs/releases/0.3.36.en.txt).

HappyCheats **0.3.35** adds **Civilization VII (Steam)** with 15 experimental controls, including influence, wildcard leader attributes, celebrations and the food queue alongside familiar movement, production, research and unit controls. Includes Chinese/English UI and function keys. API bindings were inspected in the installed game; gameplay remains unverified. See [setup and controls](docs/civilization-vii.md), [more options explored](docs/civilization-vii-research.md) and [release notes](docs/releases/0.3.35.en.txt).

HappyCheats **0.3.34** corrects the Witcher 3 command-file location and removes the foreground-window requirement. Controls queue immediately, and switching back to the client no longer blocks confirmation. Close the game and choose **Install / update** once for trainer **0.1.1**. Live cheat effects remain unverified. See [release notes](docs/releases/0.3.34.en.txt).

HappyCheats **0.3.33** relaxes Civilization VI connection checks and adds function keys for all 15 implemented controls. Keep its page selected: F1–F11 and Shift+F1–F4 operate controls; F12 disables continuous effects. Already enabled Tuner connections no longer require a setup receipt. Offline checks passed; the reported friend-PC connection and gameplay remain unverified. See [setup and key mappings](docs/civilization-vi.md) and [release notes](docs/releases/0.3.33.en.txt).

**LAN updates:** [Host signed updates on your own PC](docs/lan-updates.md). Other PCs install the configured starter once, then download releases from inside HappyCheats.

**The Witcher 3: Wild Hunt — Remastered** now has eight experimental controls for the inspected Steam build **25575366 / 5.0.0.1041720 (DX12)**: health, stamina, adrenaline, potion toxicity, refill, crowns, base XP and skill points. Decoction toxicity is retained; XP follows New Game+ modifiers and the level cap. Keep its page open for continuous effects. Close the game before **Install / update**; conflicting script mods are preserved. Game script compilation and gameplay remain unverified. See [setup, controls and limitations](docs/witcher-3.md).

[中文说明](README.zh-CN.md) · The launcher defaults to Chinese. Use **中文 / English** in its menu to switch instantly; the choice is saved.

A native Windows trainer library for Chinese Parents, Onimusha: Way of the Sword, Laundry Store Simulator, ASTRONEER, Stardew Valley, The Blood of Dawnwalker, Crimson Desert, Black Myth: Wukong, Red Dead Redemption 2, GTA V Enhanced Story Mode, Project Hospital, Hotel Architect, Civilization VI, Civilization VII, The Witcher 3: Wild Hunt — Remastered, and Elden Ring. The launcher takes its charcoal and warm-gold design from the existing Onimusha trainer, with a searchable library and consistent live controls.

Download **`HappyCheats-0.3.46-win-x64-single-exe.zip`**, extract **`HappyCheats.exe`**, then run it. This is the complete Windows x64 release in one file: it includes the launcher, all sixteen trainer payloads, updater, runtimes, the official SMAPI installer and documentation. It needs no neighboring files or separate .NET installation for the launcher. The first launch verifies and extracts its contents to `%LOCALAPPDATA%/HappyCheats/Bundles/`, then opens the app; later launches reuse that local copy. No installation wizard or administrator permission is needed for the launcher. The unpackaged single EXE is also available at `dist/HappyCheats.exe`. See [single-EXE packaging](docs/single-exe.md) for cache locations, updates and build commands.

HappyCheats **0.3.29** adds a source backend targeting the current GTA V Enhanced Steam build **25261616 / 1.0.1158.16**, automatic loader / ScriptHookV setup, and corresponding source in both distribution formats. The seven Story Mode controls and F11 menu remain experimental; native startup and gameplay are unverified because GTA was kept closed. See [setup](docs/gta-v-enhanced.md) and [release notes](docs/releases/0.3.29.en.txt).

HappyCheats **0.3.26** adds **Project Hospital** with Max Money, Freeze Time of Day, Max Staff (raise the hiring limit to 1,000), Unlock All Campaigns, and Max Prestige. The bundled trainer targets inspected Steam build **21161440**. Close the game and choose **Install / update** before launching. The trainer has offline validation; game startup and gameplay effects remain unverified. See [setup and controls](docs/project-hospital.md) and [release notes](docs/releases/0.3.26.en.txt).

The same release adds **Hotel Architect** with 24 hotel, person, time and furniture controls, and **Civilization VI** with 15 implemented single-player controls plus four clearly unavailable raw editors. Civilization VI uses the local FireTuner interface; enabling Tuner disables achievements, and continuous effects need its page to stay open. These additions have offline validation only. See [Hotel Architect setup](docs/hotel-architect.md) and [Civilization VI setup and limitations](docs/civilization-vi.md).

HappyCheats **0.3.25** adds **A–Z / Z–A** sorting to the Library, with **A–Z** as the default. Your choice is saved; both languages use English game titles for a consistent order. Search and the selected game are preserved. Trainer payloads are unchanged. See [release notes](docs/releases/0.3.25.en.txt).

HappyCheats **0.3.24** updates Crimson Desert to trainer **0.2.2**, reporting exact hook setup errors and retaining memory diagnostics for the next run. The underlying hook failure remains unconfirmed; this release does not claim to fix it. Close the game, update the client and choose **Install / update** before reconnecting. See [release notes](docs/releases/0.3.24.en.txt).

HappyCheats **0.3.23** adds an experimental **GTA V Enhanced Story Mode** menu: invincibility, unlimited clip, never wanted, refill, vehicle repair, noon and clear weather. **F11** opens its in-game menu. That initial release targeted **1.0.1158.13** and an external ScriptHookV runtime; **0.3.29** supersedes that restriction with an experimental current-build backend. GTA Online, including invite-only sessions, is unavailable. Offline tests passed; gameplay is unverified. See [setup and limitations](docs/gta-v-enhanced.md).

HappyCheats **0.3.27** integrates automatic ScriptHookRDR2 and ASI loader setup for the **Red Dead Redemption 2 Story Mode** menu. Choose **Install / update**; first setup downloads verified files directly from the author. Current Steam build **13773296**, executable **1.0.1491.50**, was confirmed through a fresh SteamCMD query. The seven controls cover health, stamina, Dead Eye, infinite clip, horse health/stamina and wanted clearing. Official Red Dead Online is unimplemented. Startup and gameplay remain unverified. See [setup and limits](docs/rdr2.md).

HappyCheats **0.3.21** removes Onimusha's unfinished teleport card and shortcuts. Its four combat controls, F5/F6 utilities, controller shortcuts and hidden REFramework startup menu remain. Other games keep their existing features. See [release notes](docs/releases/0.3.21.en.txt).

HappyCheats **0.3.20** also adds the experimental **Black Myth: Wukong** trainer **0.1.0**: unlimited health, mana, stamina and gourd, 10x attack, instant max focus, and one-shot XP, Sparks and Will grants with editable amounts. Close Wukong and choose **Install / update**, then launch through Steam. Offline tests passed; loader startup, actual gameplay effects and progression persistence remain unverified. See [setup and controls](docs/wukong.md).

HappyCheats **0.3.20** fixes Crimson Desert initialization rejecting its supported executable. Trainer **0.2.1** corrects the image-size check and the client reports initialization failures clearly. Close Crimson Desert, update the client and choose **Install / update**, then reload and **Connect trainer**. Read-only live compatibility checks passed; cheat behavior remains unverified. See [release notes](docs/releases/0.3.20.en.txt).

HappyCheats **0.3.19** fixes the Dawnwalker **Player rotation unavailable** teleport error in trainer **0.3.3**. Manual and Auto teleport now preserve the reflected rotation field names. Close Dawnwalker and choose **Install / update** before relaunching. Offline regression checks reproduce the reported error and validate the correction; actual travel remains unverified. See [release notes](docs/releases/0.3.19.en.txt).

HappyCheats **0.3.18** adds **Crimson Desert Auto teleport (F9)** and manual **Teleport to marker (F5)** in trainer **0.2.0**. Enable auto teleport, then place a new world-map marker; an existing marker does not trigger when enabling. Close the map if it is waiting for a movement update. Pins without height preserve current elevation; terrain landing and streaming remain unverified. Close the game and choose **Install / update**, then load gameplay and **Connect trainer**. See [setup and limitations](docs/crimson-desert.md) and [release notes](docs/releases/0.3.18.en.txt).

HappyCheats **0.3.17** adds Dawnwalker **Auto teleport**: enable it with NumPad4 or Menu+X, then place a new custom map marker. It attempts travel once when player control permits (close the map if waiting) and retains the manual teleport ground and collision checks. Enabling it does not jump to an existing marker. Close Dawnwalker and choose **Install / update** to install trainer **0.3.2**. Offline checks cover lifecycle, controller and overlay behavior; live automatic travel remains unverified. See [controls](docs/dawnwalker.md) and [release notes](docs/releases/0.3.17.en.txt).

HappyCheats **0.3.16** adds the initial experimental **Crimson Desert** trainer: unlimited health, stamina and spirit, one-hit damage, and player movement speed from 0.25× to 3×. Close the game and choose **Install / update**; after loading gameplay choose **Connect trainer**. In that initial release, teleport and item usage were unavailable; marker teleport is added in 0.3.18. This targets Steam build 25477059 / executable 1.0.0.2976, with offline validation only. See [setup and limitations](docs/crimson-desert.md) and [0.3.16 release notes](docs/releases/0.3.16.en.txt).

HappyCheats **0.3.15** updates Dawnwalker to trainer **0.3.0**: corrected vertical flight and consumable use, explicit flight-timer cleanup, guarded custom-marker teleport and enemy invisibility. The three-page overlay includes the new keyboard and controller actions. Invisibility blocks normal enemy perception and targeting while keeping the player model visible; existing alerts and scripted events may persist. Teleport requires a newly placed custom marker and loaded, clear ground. New behavior has offline validation and read-only binding inspection; live gameplay, physical-controller operation and frame-time improvement remain unverified. Close Dawnwalker, run the new client and choose **Install / update**. See [0.3.15 release notes](docs/releases/0.3.15.en.txt).

HappyCheats **0.3.11** fixes Dawnwalker's reported startup failure by disabling incompatible optional loader hooks. Trainer **0.1.1** reaches the title screen with a live heartbeat on the tested build; gameplay controls remain unverified. It also adds bounded player discovery, a single game-thread timer and startup diagnostics. Close Dawnwalker and choose **Install / update** after updating the client. See [0.3.11 release notes](docs/releases/0.3.11.en.txt).

HappyCheats **0.3.10** extends Stardew Valley's **Unlimited use items (F4)** to normal NPC gifting, including the last item in a stack. Normal daily and weekly gift limits remain. After the client update, close Stardew Valley and choose **Install / update** to apply mod **0.1.1**. See [0.3.10 release notes](docs/releases/0.3.10.en.txt).

HappyCheats **0.3.9** fixes the library turning white behind the Updates dialog. The disabled library retains the selected theme, and the release tests now cover the actual Updates open/close workflow. See [0.3.9 release notes](docs/releases/0.3.9.en.txt).

HappyCheats **0.3.8** adds a saved **Light / Dark** toggle beside the language buttons. The library, trainer controls, Updates window and companion overlay share the selected theme, including text fields, progress bars and supported Windows title bars. See [0.3.8 release notes](docs/releases/0.3.8.en.txt).

HappyCheats **0.3.7** adds the gamepad-and-wrench app logo, bundled game icons and banner logos, swipe scrolling, larger touch controls and a responsive Windows handheld layout. Narrow windows reflow controls, and the window fits the available screen without shrinking text. See [0.3.7 release notes](docs/releases/0.3.7.en.txt). Handheld layouts were checked offline; physical touch hardware remains untested. This is a Windows client; SteamOS support is unchanged.

HappyCheats **0.3.6** adds an experimental **The Blood of Dawnwalker** trainer: periodic health, stamina and blood replenishment, one-shot health/stamina refill, Xbox shortcuts and a non-activating overlay for windowed/borderless play. Health top-up does not prevent lethal hits. The shipped map teleport API is a disabled stub, so no teleport button is exposed. Close the game before **Install / update**. Native bindings were inspected read-only; game startup and actual gameplay effects remain unverified. See [setup and controls](docs/dawnwalker.md) and [0.3.6 release notes](docs/releases/0.3.6.en.txt).

HappyCheats **0.3.3** includes **SMAPI 4.5.2** for offline Stardew Valley setup. **Install / update** installs the included loader when needed, then the HappyCheats mod; a compatible existing SMAPI installation is reused. The compact cheat rows, inline controls and hover descriptions from 0.3.2 remain. Existing settings and updates to the cached client are preserved. Game module versions and cheat features are unchanged; see the [release notes](docs/releases/0.3.3.en.txt).

Select a game, choose **Install / update**, then **Launch game**. Load a character or save; controls become available once the game confirms a live trainer connection. Steam installations on different drives are discovered automatically; **Browse** supports manual locations. Games and their required mod loaders still follow the installation requirements below.

## Chinese Parents

Steam app **736190**, inspected build **24710774**. First three controls: **F1 add 1,000 money**, **F2 refill action points (行动力)**, **F3 add 1,000 learning points (学习点数)**. Learning points bind to the Wisdom currency spent by the game's StudySkill method. Additional controls: unlimited action points, clear stress, keep stress at zero, clear psychological shadow, add 100 to six core attributes, and add 100 face. Uses the game's setters and caps. Continuous cheats start off and reset when the current character changes.

Resource and progression edits may be saved by the game. Turning a toggle off stops its continuous effect; it does not reverse earlier changes. The plugin does not directly edit save files. See [bindings and validation](docs/chinese-parents.md).

## Stardew Valley

New in HappyCheats **0.3.0**: unlimited health, unlimited energy, add money, easy crafting, unlimited item use, editable current and maximum health, game time, and unlimited watering cans. Three additional controls freeze time, set a movement speed bonus, and refill health, energy and water. Numeric controls use explicit **Apply** buttons and show the game's confirmed current values.

Requires the Windows Steam game **1.6.15+ within the 1.6 series**. Close the game and choose **Install / update** to install the included official [SMAPI **4.5.2**](https://github.com/Pathoschild/SMAPI/releases/tag/4.5.2) and HappyCheats mod without a setup download. Compatible existing SMAPI **4.5.2+** installations are reused. Then choose **Launch game** to start SMAPI directly and load a single-player save. **Remove trainer** leaves SMAPI and other mods installed. Multiplayer controls are disabled. Easy crafting covers known non-cooking recipes; unlimited items covers supported direct use, eating, planting, placing and normal NPC gifting, including the last item. Gift limits remain unchanged. Transfers, machine inputs, special quest hand-ins, fishing bait and tackle remain excluded.

The mod and SMAPI are installed on this PC. Earlier Stardew compilation, offline protocol/patch checks and UI checks passed; the current release's installation checks are recorded in [validation](docs/validation.md). Actual game startup and gameplay validation remain pending. See [all controls, ranges and installation](docs/stardew.md).

## ASTRONEER

Experimental module **0.3.1**, Steam build **25599237** (game **1.45.1**); the previous inspected **1.44.4 / 25119744** and **1.43.2.0 / 24409322** builds are also recognized. **F1 terrain resource collection**, with a selectable **10× / 20× / 100×** rate, **F2 unlimited oxygen**, **F3 unlimited backpack power**, **F4 add 10,000 research bytes**, **F5 invulnerability**, and **F6 player run speed (3× / 5× / 10×)**. **F10** disables all in game. Open the shared menu with **Ctrl+Del** while HappyCheats is running. Choose the collection rate, then enable it; changing the rate while enabled affects the next collection event. Turning collection off preserves the selected rate. Load a world hosted on this PC; toggles start off and the rate resets to 10× on session changes. Collection multiplies new resource fragments, leaving inventory transfers untouched.

The older game build successfully loaded the trainer and recorded 10× terrain-fragment collection events. Version **0.2.0** adds the updated executable's inspected compatibility profile. Historical gameplay evidence does not establish effects on the new build. Close the game before **Install / update**. See [bindings, install details and current validation](docs/astroneer.md).

## Existing trainers

Onimusha retains the 0.4.0 REFramework trainer and its install ownership receipt, with unlimited health, auto-parry, one-hit stun and stun immunity. Its Xbox shortcuts remain available while HappyCheats is open: hold Menu (☰) for 350 ms, then D-pad up/right/down/left for those four toggles or LB to disable all. Menu+RB now toggles the shared menu, as it does for every game; F6 still toggles Onimusha's native status panel. Enable Xbox shortcuts on the selected game page. Menu controls use mouse and keyboard. Auto-parry remains experimental. Chinese Parents uses BepInEx 5 x86; Laundry uses MelonLoader 0.7.3. These loaders stay isolated in their own games.

Launching Onimusha through HappyCheats starts the left REFramework menu hidden, with the Onimusha status panel at the top right. Insert still opens REFramework when needed; F6 controls the trainer status panel.

Laundry adds a session-checked launcher bridge to the existing cash, XP, shop level, washing and drying features. Install / update is necessary to load that bridge. Existing game-specific hotkeys remain available. The platform does not automatically update a running game.

The launcher waits for acknowledgements and shows confirmed state. Missing, stale or incompatible game sessions keep trainer controls disabled. Installers verify bundled payloads, reject conflicting mod files, and preserve modified files. Chinese Parents removal keeps the shared BepInEx loader for other mods.

## Server updates

Updater-capable releases can download signed client and trainer-library updates from a configured HTTPS server, including release notes and newly supported games. Existing users need one manually installed starter release containing your server URL and public verification key. After a client update, use each affected game's **Install / update** to refresh its installed trainer.

The single EXE preserves this update support: server updates replace its extracted client, and later launches of the same EXE use that updated copy. The EXE itself still contains its original release. Normal releases include the saved LAN endpoint and public GitHub fallback, with only the public verification key embedded. See [LAN-first updates and publishing](docs/github-updates.md).

## Development

Requires Windows, .NET 8 SDK, and the installed supported games to build their plugins. Stardew's build also needs installed SMAPI references. Its payload includes the official SMAPI installer, corresponding source and redistribution notices; proprietary game assemblies are not bundled. Astroneer's offline Lua tests additionally need Python and `lupa==2.6`; see its [developer checks](docs/astroneer.md#developer-checks). Destination PCs use the self-contained release; no SDK is needed. Laundry's in-game loader separately needs .NET 6 Desktop Runtime, bundled with the package.

```powershell
.\scripts\Build.ps1 -Version 0.3.46 -Zip -SingleExe
# Wrap the existing complete release folder without rebuilding its trainers:
.\scripts\Build-SingleExe.ps1 -PackageDirectory .\dist\HappyCheats -Zip
# For a different Chinese Parents source-build installation:
.\scripts\Build.ps1 -ChineseParentsGameDirectory 'D:\SteamLibrary\steamapps\common\ChineseParent' -Zip
# Rebuild the launcher using existing payloads:
.\scripts\Build.ps1 -Version 0.3.46 -SkipPayloads
# All offline tests, without starting installed games:
.\scripts\Test.ps1
# Read-only Steam/build detection:
dotnet run --project .\tools\HappyCheats.Cli -c Release -- . probe
# Development UI with workspace payloads:
dotnet run --project .\src\HappyCheats.App -- --data-root .
```

Code is separated into `HappyCheats.App` (WPF), `HappyCheats.Core` (game modules and installation), `ChineseParents.Trainer` (Unity plugin), and copied module sources under `modules`. Add games through `IGameModule` and `ModuleCatalog`; the UI renders their feature definitions automatically. Original sibling projects are preserved.

Distribute `dist/HappyCheats-0.3.46-win-x64-single-exe.zip` or `dist/HappyCheats.exe` to users. The single-EXE ZIP contains only `HappyCheats.exe`; its SHA-256 checksum is provided separately. `Build.ps1 -Zip -SingleExe` also produces the complete-folder `HappyCheats-0.3.46-win-x64.zip` in the existing server-update format. The `dist/HappyCheats/` folder remains a development artifact; do not substitute the smaller executable inside that folder for the complete single-file release.

Official dependency provenance and redistribution notices are included with the relevant payload. No proprietary game assemblies are distributed. See [validation](docs/validation.md) for the precise tested scope.

