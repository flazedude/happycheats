# Crimson Desert (Steam)

Native trainer **0.2.3**, bundled in HappyCheats **0.3.36**, targets Steam app **3321460**, build **25477059**, executable **1.0.0.2976**. Compatibility is pinned to SHA-256 `57DA440D72F4DB974F25FEF047CF84C4DADD999A88CB2A3C5AF4C9BD67FDE1E7`; other executables are rejected. The latest teleport changes have static binary and isolated fixture validation. Successful travel on the user's other PC remains unverified; no game was launched or save edited during this update.

## Teleport correction in 0.3.36

The previous trainer wrote the destination inside a movement substep. The enclosing movement routine can then restore its saved starting position. Both manual and automatic teleport now write after that enclosing routine returns, using a separately guarded hook. The six bounded writes count complete movement updates rather than substeps. After the final write, the trainer waits through another complete native movement update and observes the position without writing again. A position within one world unit is reported as observed near the marker; this tolerance is a reporting policy, not a terrain or safe-landing check. Rejection, cancellation or timeout cannot become a later retry.

HappyCheats now shows the evolving native teleport result instead of treating command acceptance as completed travel. The status file retains the requested and observed positions and progress for diagnosis. This corrects a proven ordering/reporting defect, but does not establish that the user's remote session took the rollback branch or that distant streaming and landing work. Close Crimson Desert, update HappyCheats, choose **Install / update**, then launch the game yourself and **Connect trainer**. See [0.3.36 release notes](releases/0.3.36.en.txt).

## Hook diagnostics in 0.3.24

Trainer 0.2.2 identifies each failed hook, its creation or enabling stage, and its exact MinHook status. The game's `bin64/HappyCheatsCrimsonDesert/status.json` retains those results plus read-only memory protection and dynamic-code policy snapshots. Allocation failures also record a bounded nearby-address-space scan, explicitly marked complete or truncated. Raw Windows last-error values are contextual evidence, not proof of the failure cause. Diagnostic collection never retries hooks or changes process mitigations.

This release does **not** establish or fix the underlying reported hook failure. The exact prologues and diagnostic paths pass isolated fixtures, including simulated failures; successful hooking and gameplay in the actual game remain unverified. After installing this update with the game closed, launch gameplay and click **Connect trainer**. Leave the game running if an error appears so its details can be inspected. See [0.3.24 release notes](releases/0.3.24.en.txt).

## Connection fix in 0.3.20

Trainer 0.2.0 compared the PE image size against `0x173AA000`, one page smaller than the actual `0x173AB000` (389722112 bytes). It therefore rejected the supported game before installing hooks. Trainer 0.2.1 corrects that constant while preserving the timestamp, executable hash and code-byte checks. Initialization failures now have a distinct client label instead of “Waiting for gameplay.” Close the game, update HappyCheats, choose **Install / update**, then reload the game and **Connect trainer**. The already loaded old DLL cannot receive this correction without restarting the game.

## Initial controls

| Control | Status |
| --- | --- |
| Unlimited health | Implemented; gameplay verification pending |
| Unlimited stamina | Implemented; gameplay verification pending |
| One-hit kill | Implemented as amplified outgoing player damage; scripted invulnerability can still prevent a kill; gameplay verification pending |
| Unlimited spirit | Implemented; gameplay verification pending. This is the resource requested as “heroism.” |
| Character movement speed | Implemented, 0.25×–3× horizontal speed; 1× restores normal speed; gameplay verification pending |
| Teleport to marker (F5) | Implemented; one explicit request to the current map destination; terrain landing and streaming unverified |
| Auto teleport (F9) | Implemented; starts off, then responds once to each new user world-map destination placement; gameplay verification pending |
| Unlimited item usage | Unavailable until ordinary consumption can be distinguished from sale, crafting, transfers and quest removal |

Unlimited item usage remains explicitly disabled and rejects commands. It is not a working cheat.

## Automatic marker teleport

After connecting, turn on **Auto teleport** or press **F9** while the game is foreground. Place a new custom marker on the world map. The trainer waits for the matching game destination and teleports on the next controlled-player movement update; close the map if it is waiting. Selecting a map icon as a new destination also counts as a user placement. Quest updates, ordinary navigation changes, and opening the map do not trigger it. Enabling the option ignores existing markers.

Each placement gets one request. Repeated updates for that request do not create another jump. A later placement replaces it; clearing the marker, turning the option off, F10, a changed player/movement context or an expired connection cancels it. Requests expire after ten seconds; place a new marker if one expired while the world was paused. Failed position updates are not retried later. The game may treat selecting the same existing marker as **clear**, in which case it cancels rather than teleports.

**Custom pins do not contain terrain height.** When a destination has no height, teleport keeps the character's current world elevation, captured once for the request. It does not find ground, test collision clearance or wait for distant terrain to stream. Landing inside terrain, falling or streaming problems remain possible and gameplay is unverified. Manual F5 uses the same experimental positioning path and limitations.

## Use

1. Close Crimson Desert. Select the game in HappyCheats and choose **Install / update**.
2. Launch the game yourself and load gameplay. Select **Connect trainer** in HappyCheats. Passive Steam scans do not inject a DLL.
3. Keep this game selected in HappyCheats while using the trainer. All toggles initially start off. F1 toggles health, F2 stamina, F3 one-hit damage, F4 spirit, F5 teleports once, F9 toggles auto teleport, and F10 disables all. In-game hotkeys require Crimson Desert to be the foreground process.
4. If HappyCheats stops refreshing its session lease for five seconds, all controls switch off. Switch back to Crimson Desert in HappyCheats before enabling them again. Returning to a title screen or changing character invalidates old pointers and requires a fresh player resolution.

The DLL remains loaded until Crimson Desert exits; disable all before closing the client, and restart the game to completely unload it. **Remove trainer** requires the game closed and preserves foreign or modified files. No proxy loader is installed and no game executable is patched on disk. Health/stamina/spirit refills may enter ordinary game saves through the game's own save behavior.

## Evidence and remaining work

`tools/InspectCrimsonDesert/inspect_binary.py` performs read-only PE/signature inspection. The inspected executable contains unique current signatures for stat commit, damage dispatch and the character manager. Four separate manager anchors agree on the same RIP-relative global. Disassembly verifies the type descriptor and controller-to-character roundtrip; the runtime repeats pointer/type checks before changing a value.

The movement routine is shared by characters. Three independent disassembled callers establish the controlled client owner → actor → physics → locomotion component chain, which gates speed and teleport. Read-only live inspection showed that the stat manager instead contains server-side actors whose movement layout differs. Trainer 0.2.1 therefore resolves the current client owner through the world-map accessor (global `0x6D69190` → `+0x30` service → `+0x50` owner), with independent possession and vital-chain checks. Combat retains the server-side identity. Only horizontal velocity is scaled; vertical velocity is preserved.

The marker sender is gated by its exact world-map placement caller and the controlled player's navigation sender object. Its original arguments pass through unchanged. Generic changes to navigation coordinates are never treated as placement events. Accepted coordinates must match the captured request. Position changes run after the matching player's enclosing movement update, recheck identity and pointers, convert absolute coordinates using the current floating origin, write only the position XYZ, and invoke the game's transform synchronization routine. Rotation, transform W and unverified velocity fields are preserved. At most six writes are made within the request deadline, followed by a separate movement update that observes persistence without another write. Immediate readback detects write failure but does not confirm travel.

Offline tests cover caller/owner discrimination, argument forwarding, delayed marker acceptance, ignoring existing markers, duplicate events, cancellation, identity changes, origin rebasing, a fixed fallback elevation and bounded requests. Static inspection proves the field and call bindings; it does not establish successful live travel. The item-decrement binding still does not distinguish consumption-only behavior.

The controlled-player predicate accepts the verified player type tags 1 and 9. Secondary protagonists represented as Mercenary type 4 and ambiguous character transitions are deliberately rejected; controls may be unavailable in those cases. All-character support has not been established. A player identity change clears all toggles and restores 1× speed.

Interface research comes from [Trinity's TU 2.03 source](https://github.com/gugi97/Trinity/tree/e5b3303854e7383cfeb3489e7830995772c52ea9), licensed MIT, with independent checks against the installed executable. The bundled runtime uses [MinHook 1.3.4](https://github.com/TsudaKageyu/minhook/tree/v1.3.4) and [JSON for Modern C++ 3.12.0](https://github.com/nlohmann/json/tree/v3.12.0). Licenses are included in the payload's ThirdPartyNotices.txt. No third-party prebuilt trainer executable is used.
