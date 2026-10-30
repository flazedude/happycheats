# Crimson Desert (Steam)

Native trainer **0.2.6** targets Steam app **3321460**, build **26317560**, executable **1.0.0.3424**, inspected after the Charting the Unknown update. Compatibility is pinned to SHA-256 `D4855C08998638F07FA8FAE08BD6249261F10C8FEC2215EFFC29CB339E7FA525`; other executables, including the previously supported PE2976, are rejected. Validation for this build is static and synthetic only. No game was launched, live game process inspected, trainer attached or save edited for this update. See the [current compatibility evidence](crimson-desert-3424.md).

## DLC compatibility

The stat, damage, movement and marker hooks have new exact bindings. The outer movement entry changed, so its new instruction span is covered by relocation tests. Teleport still runs after the outer movement update and retains its bounded writes and later position observation.

The client-player check now requires the exact normal client actor class, the world's current self-user, the game's local-control flag, possession backreference and complete vital chain. An identity change disables controls; unknown classes and unproved player type tags are refused. This is conservative experimental support, not a claim that every protagonist or DLC situation works. Close the game and choose **Install / update** before using the new trainer.

Resource refills and health damage scaling resolve 16-bit status references through the game's current metadata and check stable resource keys. The old fallback IDs 25/26 identify equipment in the inspected data and are no longer trusted as resource aliases. Breath and equipment remain outside the refill controls. Missing or inconsistent metadata causes refusal, without invoking the game's lazy loader. See [resource evidence](crimson-desert-3424-resources.md).

## Health refresh correction in 0.3.45

The worker previously republished the player identity every 250 ms, briefly clearing its owner pointer even when every identity field was unchanged. A concurrent stat hook could apply the original update, encounter that temporary missing owner and skip the refill. Trainer 0.2.5 avoids republishing unchanged complete server and client identities. Actual changes still invalidate controls before publishing, and fresh native pointer checks remain mandatory. Invalid or expired sessions do not gain health protection.

The health fixture reproduces the old interleaving through the actual stat hook and verifies the correction, complete identity changes, reused owner addresses and expired connections. Static inspection shows the health helper rereads the committed health before its zero-health logic, so a post-original refill alone is not evidence of a separate death-order bug. Nine read-only samples showed enabled health, a valid lease and matching full server/client health, but did not capture an attack. The reported enemy-attack death and its apparent relationship to hiding a menu remain unproven. See [0.3.45 release notes](releases/0.3.45.en.txt).

## Hook allocation correction in 0.3.44

The observed damage and marker hook failures came from MinHook's 1 GiB search limit. At initialization, diagnostics found no free candidates in that range. Later read-only inspection found 60 unused slots in an existing trainer block just outside the old range, still within reach of both hooks. Address-space availability had also changed since initialization, so the later free-space scan is not treated as the initial state.

Trainer 0.2.4 keeps the original 1 GiB search first, then expands to a conservative 2 GiB minus 64 KiB range, including reuse of existing blocks. It rejects RIP-relative relocation and relay displacements outside signed 32-bit reach. Diagnostics scan the same expanded range with their existing time and region limits. Offline tests cover the five exact prologues, fallback allocation and reuse, full exhaustion and relocation safety. No live allocation probe or game memory change was used for diagnosis.

Close Crimson Desert, update HappyCheats and choose **Install / update**, then launch the game yourself and **Connect trainer**. An already loaded DLL cannot receive this fix until the game exits. Waiting for gameplay is expected at the main menu; loaded-player checks still apply. See [0.3.44 release notes](releases/0.3.44.en.txt).

## Teleport correction in 0.3.36

The previous trainer wrote the destination inside a movement substep. The enclosing movement routine can then restore its saved starting position. Both manual and automatic teleport now write after that enclosing routine returns, using a separately guarded hook. The six bounded writes count complete movement updates rather than substeps. After the final write, the trainer waits through another complete native movement update and observes the position without writing again. A position within one world unit is reported as observed near the marker; this tolerance is a reporting policy, not a terrain or safe-landing check. Rejection, cancellation or timeout cannot become a later retry.

HappyCheats now shows the evolving native teleport result instead of treating command acceptance as completed travel. The status file retains the requested and observed positions and progress for diagnosis. This corrects a proven ordering/reporting defect, but does not establish that the user's remote session took the rollback branch or that distant streaming and landing work. Close Crimson Desert, update HappyCheats, choose **Install / update**, then launch the game yourself and **Connect trainer**. See [0.3.36 release notes](releases/0.3.36.en.txt).

## Hook diagnostics in 0.3.24

Trainer 0.2.2 identifies each failed hook, its creation or enabling stage, and its exact MinHook status. The game's `bin64/HappyCheatsCrimsonDesert/status.json` retains those results plus read-only memory protection and dynamic-code policy snapshots. Allocation failures also record a bounded nearby-address-space scan, explicitly marked complete or truncated. Raw Windows last-error values are contextual evidence, not proof of the failure cause. Diagnostic collection never retries hooks or changes process mitigations.

The 0.3.24 diagnostic release did **not** establish or fix the underlying reported hook failure. The allocation limit is addressed in 0.3.44 above. The exact prologues and diagnostic paths pass isolated fixtures, including simulated failures; successful hooking and gameplay in the actual game remain unverified. After installing this update with the game closed, launch gameplay and click **Connect trainer**. Leave the game running if an error appears so its details can be inspected. See [0.3.24 release notes](releases/0.3.24.en.txt).

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

The movement routine is shared by characters. Disassembled callers establish the controlled client owner → actor → physics → locomotion component chain, which gates speed and teleport. Historical PE2976 read-only inspection showed that the stat manager instead contains server-side actors whose movement layout differs. The current PE3424 world-map accessor uses global `0x71AEA50` → `+0x30` service → `+0x50` owner. Trainer 0.2.6 adds the engine's self-user/local-control checks to independent possession and vital-chain checks. Combat retains the server-side identity. Only horizontal velocity is scaled; vertical velocity is preserved.

The marker sender is gated by its exact world-map placement caller and the controlled player's navigation sender object. Its original arguments pass through unchanged. Generic changes to navigation coordinates are never treated as placement events. Accepted coordinates must match the captured request. Position changes run after the matching player's enclosing movement update, recheck identity and pointers, convert absolute coordinates using the current floating origin, write only the position XYZ, and invoke the game's transform synchronization routine. Rotation, transform W and unverified velocity fields are preserved. At most six writes are made within the request deadline, followed by a separate movement update that observes persistence without another write. Immediate readback detects write failure but does not confirm travel.

Offline tests cover caller/owner discrimination, argument forwarding, delayed marker acceptance, ignoring existing markers, duplicate events, cancellation, identity changes, origin rebasing, a fixed fallback elevation and bounded requests. Static inspection proves the field and call bindings; it does not establish successful live travel. The item-decrement binding still does not distinguish consumption-only behavior.

The PE3424 controlled-player predicate accepts only the statically verified type tag 1. Type tag 9 was accepted by the older profile but has not been established for this build; Mercenary type 4 and ambiguous character transitions are also rejected. Controls may be unavailable in those cases. All-character support has not been established. A player identity change clears all toggles and restores 1× speed.

Interface research comes from [Trinity's TU 2.03 source](https://github.com/gugi97/Trinity/tree/e5b3303854e7383cfeb3489e7830995772c52ea9), licensed MIT, with independent checks against the installed executable. The bundled runtime uses [MinHook 1.3.4](https://github.com/TsudaKageyu/minhook/tree/v1.3.4) and [JSON for Modern C++ 3.12.0](https://github.com/nlohmann/json/tree/v3.12.0). Licenses are included in the payload's ThirdPartyNotices.txt. No third-party prebuilt trainer executable is used.
