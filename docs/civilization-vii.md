# Civilization VII (Steam)

Experimental HappyCheats trainer **0.1.0** for Steam app **1295660**, included in client **0.3.35**. It uses Civilization VII's local JavaScript FireTuner interface. The API bindings were checked against the Firaxis Tuner panels and UI scripts in the local Steam installation, build **25245002**. Steam also recorded pending target build **25516395**; that pending build was not inspected. Actual Tuner connection and gameplay effects remain unverified.

## Setup

1. Install Civilization VII through Steam. Start it yourself once to create its profile, then close it.
2. In HappyCheats, choose **Civilization VII** and **Find in Steam**. Manual browsing accepts the game root or `Base/Binaries/Win64` containing the DX12 or Vulkan executable.
3. Choose **Install / update** while the game is closed. This enables `EnableTuner 1` in the existing `AppOptions.txt` and backs up the original. It supports Civ VII's commented `;EnableTuner -1` default and explicit settings. Achievement behavior with Tuner enabled has not been verified for Civ VII.
4. Start the game yourself and load single-player. Close official FireTuner and other Tuner clients. HappyCheats connects only to `127.0.0.1:4318` and verifies that the accepted socket belongs to the discovered Civ VII process before sending code.
5. Keep the Civilization VII page open for continuous effects and hotkeys. Select your own unit or settlement in the game for selection-specific controls. Function keys work with HappyCheats or the connected Civ VII process in the foreground.

Setup changes only the owned Tuner configuration line. It does not install a mod into the game, replace an executable, alter a save, or launch the game. Removal restores the original Tuner line while retaining unrelated edits and pre-existing enabled settings. Profile discovery uses `%LOCALAPPDATA%/Firaxis Games/Sid Meier's Civilization VII/`; ambiguous explicitly supplied profiles are rejected. An already enabled, authenticated Tuner connection does not require a HappyCheats setup receipt.

## Controls

| Key | Control | Meaning |
| --- | --- | --- |
| F1 | Unlimited movement | Restore normal movement for your units while connected. Does not add attacks or restore consumed units. |
| F2 | Fast build units | Complete current unit production in your settlements, including food-trained units where applicable. |
| F3 | Fast build buildings and wonders | Complete current constructible production. |
| F4 | Fast projects | Complete current project production; repeat projects may complete repeatedly. |
| F5 | Fast research | Request the current technology node through the game's grant-node operation. |
| F6 | Fast civics | Request the current civic node through the same operation. |
| F7 | Edit unit damage | Set selected owned unit damage, 0–99; zero heals. Values reaching its actual lethal threshold are rejected. |
| F8 | Restore selected unit movement | Restore the unit's normal movement allowance once. |
| F9 | Add unit XP | Add 1–100,000 XP to the selected owned unit/commander; promotion rules remain in force. |
| Shift+F1 | Edit gold | Set treasury balance to 0–100,000,000. |
| Shift+F2 | Edit influence | Set diplomacy influence balance to 0–1,000,000. |
| Shift+F3 | Add rural population | Add 1–100 rural population to the selected settlement; this is an increment, not a total-population setter. |
| F10 | Complete food queue | Complete the selected settlement's food-funded item, which may be growth or a unit. |
| Shift+F4 | Add wildcard attribute points | Add 1–100 unspent leader wildcard points. Spend them in the game. |
| F11 | Start celebration | Call the game's Golden Age API; resolve celebration choices in-game. |
| F12 | Disable all | Stop future continuous effects. Completed changes remain. |

Numeric hotkeys apply the value entered in the card. These are 15 implemented controls plus the common Disable all action. Resource editors set a total; XP, population and attributes add an amount. Research and production operations may resolve asynchronously, so acknowledgement confirms dispatch and does not promise instantaneous completion. Game caps and unsupported APIs can reject a control. Continuous effects are driven by HappyCheats polling; switching pages or closing the client stops further effects. Session and local-player/age transitions reset continuous state.

Multiplayer, hot-seat, foreign-owned selections and actions outside the local player's active turn are rejected. No multiplayer fallback debug port is used. Commands are session-bound and range checked, use unique acknowledgements, and are never automatically retried after an uncertain network result.

## Comparison and further options

Civilization VI's Lua runtime cannot be reused in VII. VII provides a real influence balance, wildcard leader attributes, celebrations and a food production queue. Its mechanics do not justify copying VI's faith, diplomatic favor, housing, amenities, district-production, or raw unit-level cards. Movement restoration and adding XP are supported; arbitrary remaining-move and level setters are not implemented.

See [the API research and additional candidates](civilization-vii-research.md) for options such as map reveal, unit spawning, traditions and policy slots, and the validation still needed before implementing them.

## Evidence and validation

The primary API references are the installed Firaxis files under `Base/Platforms/Windows/Config/TunerPanels/` (`Player.ltp`, `Units.ltp`, `Cities.ltp`) and `Base/modules/` UI scripts. They remain game-owned and are not redistributed. Transport framing was independently implemented using the author's [FireTuner Terminal protocol source](https://github.com/ghost-ng/FiretunerTerminal/blob/main/civ7_terminal/protocol.py) as a reference.

Offline checks cover the actual JavaScript runtime with game API fixtures, local TCP framing/acknowledgement failure handling, profile ownership and restoration, numeric input, session/selection restrictions, Chinese UI, hotkey routing and packaged-client rendering. Offline fixtures do not establish real-game compatibility. No game was launched, save opened, or actual profile changed for testing. Gameplay, achievement effects, physical in-game hotkey handling and second-PC connectivity remain unverified.

Build with `scripts/Build-CivilizationVII.ps1` and validate with `scripts/Test-CivilizationVII.ps1`. Node.js is a development/test dependency only; distributed clients require neither Node.js nor the SDK.
