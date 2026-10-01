# Civilization VI

Experimental HappyCheats trainer **0.1.1** for the Windows Steam edition (app 289070), using the game's local FireTuner Lua interface. No game executable patch, injected DLL or downloaded trainer is used. Actual game connection and gameplay remain unverified; the installed game files were not available during development. Offline checks exercise Lua behavior against fixtures, the real TCP framing against a local mock server, configuration ownership, hotkey routing, and packaging.

## Setup

1. Install Civilization VI through Steam. Start it yourself once to create its profile, then close it.
2. Open HappyCheats, choose **Civilization VI**, and use **Find in Steam** or browse to the game root containing `Base/Binaries` (the executable folder is also accepted). Automatic discovery can use the exact path of a running game when its Steam manifest is unavailable.
3. Choose **Install / update**. This validates the bundled payload and enables `EnableTuner 1` in the existing `AppOptions.txt`. **Enabling Tuner disables achievements.** The original file is backed up. Other settings and saves are preserved.
4. Start Civilization VI and load a single-player game. Keep the official FireTuner application and other Tuner clients closed; HappyCheats uses the local connection on `127.0.0.1:4318`.
5. Keep HappyCheats open on the Civilization VI page. Continuous effects run when the client polls during your active turn. Switching pages, closing the client or losing the connection stops further polling; completed changes are not undone.

Profile discovery checks `%LOCALAPPDATA%/Firaxis Games/Sid Meier's Civilization VI/` and the Windows Documents folder under `My Games/Sid Meier's Civilization VI/`. Multiple matching profiles are rejected to avoid changing the wrong configuration. The game must be closed for setup/removal. **Remove trainer** restores only the Tuner setting owned by HappyCheats, preserving later unrelated edits and a Tuner setting that was already enabled before setup. No setup action was applied to the user's actual profile during development.

Starting with client **0.3.33**, an already enabled Tuner can connect without a HappyCheats setup receipt or backup. Receipt ownership still controls removal. Missing or ambiguous profile files no longer veto an actual connection: the client verifies that the accepted local socket belongs to the detected game. If Tuner is already enabled, opening the updated client reloads the bundled runtime; no game-file reinstall is needed.

If everything stays grey, read the status at the bottom of the page. A real Tuner connection and loaded single-player session are still required. For **No Civilization VI Tuner connection**, close the game, choose **Install / update**, restart it and load single-player; close other Tuner clients. Optional player flags no longer block all controls, and a verified InGame local player can be used when GameCore lacks that information. Missing unit/city selection disables only affected editors. Explicit multiplayer, foreign ownership, a dead/AI player or another player's turn still prevents the corresponding action.

## Function keys

Keep HappyCheats running with the Civilization VI page selected and connected. Shortcuts work with Civilization VI or HappyCheats in the foreground. Numeric shortcuts apply the value currently entered in that card; edit the amount in HappyCheats first. Select your own unit or city in-game before its shortcut. Hold/repeat does not repeat commands. While the connected game has focus, the mapped keys are reserved for HappyCheats, including while a command is pending.

| Key | Action |
| --- | --- |
| F1 | Toggle unlimited movement |
| F2 | Toggle fast units |
| F3 | Toggle fast buildings and wonders |
| F4 | Toggle fast districts |
| F5 | Toggle fast projects |
| F6 | Toggle fast research |
| F7 | Toggle fast civics |
| F8 | Apply unit damage (default 0 heals) |
| F9 | Apply unit movement |
| F10 | Apply promotion availability |
| F11 | Apply unit XP |
| Shift+F1 | Apply gold |
| Shift+F2 | Apply faith |
| Shift+F3 | Apply diplomatic favor |
| Shift+F4 | Apply city population |
| F12 | Disable all continuous effects |

F12 stops further continuous effects; it does not reverse completed edits. The four unsupported editors have no shortcuts. Physical in-game key handling remains unverified.

## Controls from the requested picture

| Control | Behavior |
| --- | --- |
| Unlimited movement | Replenishes movement for your units while connected; does not add attacks or restore consumed units. |
| Fast build units | Finishes current unit production in your cities. |
| Fast build buildings and wonders | Finishes current building/wonder production. |
| Fast specialized buildings | Finishes current **district** production. |
| Fast projects | Finishes current city projects; repeat projects can finish repeatedly. |
| Fast research / Fast civics | Finishes the currently chosen technology/civic. |
| Edit current damage done to unit | Sets damage on your selected unit; zero heals. The editor cannot kill the unit. |
| Edit number of moves remaining | Sets your selected unit's remaining movement. |
| Edit unit can level up | 1 raises XP to unlock promotion; 0 attempts to remove stored promotion availability. The result is checked and engine refusals are reported. This is not a raw level edit and does not undo XP. |
| Edit unit XP | Requests an XP amount through the experience API and reads it back. Game caps or pending promotions can prevent an exact target; these are reported as failures rather than success. |
| Edit gold / Edit faith | Sets your current resource amount. |
| Edit diplomatic favor | Sets favor when the Gathering Storm diplomacy API is present. |
| Edit population | Sets the population of your selected city. |

These are **15 implemented controls** (the combined rows above each include two). Unit/city edits require selection of an object you own. Commands are session-bound, range checked, and acknowledged; uncertain commands are never automatically retried. Each feature checks its required methods before allowing use. Multiplayer and hot-seat sessions are not supported.

Four cards are intentionally unavailable and explain why: **raw influence points, raw unit level, amenities bonus and housing bonus**. No verified setter for these raw values is implemented. Envoys are not substituted for influence, and total amenities/housing are not presented as their bonus fields. XP and stored promotion availability remain separate controls. This limited release scope was approved by the user.

## Validation and sources

The Lua API bindings were checked against the [FireTuner panel source mirror](https://github.com/Nanookao/Civ6-FireTuner-debug/tree/main/Civ6) and [Civilization VI API reference corpus](https://github.com/Sukritact/Civilization-VI-Modding-Knowledge-Base/tree/master/Objects). Transport framing was researched from the [civ6-mcp source](https://github.com/lmwilki/civ6-mcp/blob/main/src/civ_mcp/tuner_client.py). HappyCheats contains its own implementation. Test doubles validate logic and protocol handling; they do not establish compatibility with an actual game build. No gameplay, second-PC connectivity, or achievement behavior was tested during development.
