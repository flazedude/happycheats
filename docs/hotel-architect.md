# Hotel Architect

HappyCheats provides an experimental Steam trainer for [Hotel Architect](https://store.steampowered.com/app/1602000/) (app **1602000**), targeting inspected Steam build **25340122**. The executable is `HotelArchitect/Binaries/Win64/HotelArchitect-Win64-Shipping.exe`. Installation is restricted to the inspected executable hash; a different build needs a new compatibility inspection.

## Setup

1. Close Hotel Architect, open HappyCheats and select **Hotel Architect**.
2. Choose **Install / update** to install the bundled UE4SS loader and trainer.
3. Launch through Steam and load a hotel. Wait for the trainer's live connection.
4. Enable continuous controls, or enter a numeric value and press **Apply**. Character and furniture editors require the appropriate selection in the game.

Existing modified or conflicting loader files are preserved. **Remove trainer** removes only unchanged files owned by HappyCheats, retaining a shared loader when another mod needs it. Do not copy this game's trainer into other games.

## Controls

| Scope | Controls |
| --- | --- |
| Hotel | Money; stars (0–5) |
| Time | Always noon; game speed; days passed; day number; hour; minute; total game hours |
| People | No needs; disable aging; selected character's restroom need, mood and health; selected employee's XP, level, retirement days and energy |
| Selected furniture | Room value multiplier; cleanliness; attractiveness; negative attractiveness; condition |
| All furniture | Unlimited furniture condition and cleanliness |

The four continuous controls are **No needs**, **Always noon**, **Unlimited furniture condition and cleanliness**, and **Disable aging**. All start off. The remaining 20 controls are explicit numeric edits. F1/F2/F3 control the first three toggles; use the client for Disable aging.

Use the client's **Disable all** to stop all four toggles. Disable aging requires HappyCheats to remain open; it captures the previous difficulty flag and restores it on disable or normal client shutdown when the same live hotel can still be confirmed. If the game changes worlds or overwrites the flag itself, the trainer preserves the new game's setting. There is no Hotel Architect F10 shortcut.

Game speed uses the game's native settings: **0 paused, 1 normal, 2 faster, 3 fastest**. Percentage editors display **0–100** and convert to the game's normalized values. Employee levels are **1–10**; current XP must stay below `110 × level`, with XP zero at level 10. The room value multiplier belongs to the selected furniture item and affects its contribution to a room.

**No needs** maintains the verified restroom, mood, health and staff-energy statistics for living characters. It does not change guest service objectives. Furniture condition and cleanliness are separate from the game's repair/replacement flags; those flags and their gameplay consequences have not been verified.

**Always noon freezes the simulation clock at 12:00**, keeping the current day. This can hold time-dependent activities at noon too. Disable it before editing hour, minute or total hours. Total game hours follows the game's own calculation, `day number × 24 + time of day`; editing it changes that calendar/time pair. Financial days passed is a separate counter.

Commands are tied to the current hotel and selected objects. Changing a hotel or selection resets the Lua toggles and rejects commands from the previous selection. This avoids applying an old editor request to a newly selected person or item.

## Validation scope

Game binaries are inspected read-only. Automated checks use temporary installation fixtures and simulated reflected game objects; they do not launch the game or modify saves. Loader startup, in-game control effects and persistence after saving require actual gameplay verification. A successful offline test is not evidence of a successful in-game action.

The installer verifies executable SHA-256 `E0B6E24E2D0D3CA466B9DD08ED83BC989594B6B17C31CDC3B63F543355621AE8`. The payload also checks the executable's PE identity and exact native getter bytes before registering the build-specific non-reflected fields. See [native binding evidence](hotel-architect-bindings.md).

Numeric edits may be persisted by the game's normal saving behavior. Disabling a continuous control stops further enforcement; it does not undo previous resource or progression edits.
