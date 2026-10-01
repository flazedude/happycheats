# Black Myth: Wukong

Experimental Steam trainer **0.1.1**, included in HappyCheats **0.3.30**. Steam app **2358720**, installed build **21393610**. Installation is gated by the inspected executable's SHA-256; another executable requires a compatibility review.

Version 0.1.1 corrects the loader's `UObject::ProcessEvent` entry for Wukong's customized engine. The old entry returned without invoking game functions, leaving every option grey even in a loaded save. The payload now includes an owned `VTableLayout.ini`; connection failures also report the exact discovery stage instead of always asking for a save. Close Wukong, update the client and choose **Install / update** before relaunching.

Close the game, select **Black Myth: Wukong** in HappyCheats, and choose **Install / update**. This installs the bundled UE4SS runtime and trainer. Launch through Steam and load a living player. The controls require a fresh trainer connection and valid resource bindings. Existing or modified loader/mod files are preserved; an installation conflict requires resolving the reported file rather than overwriting another mod.

| Key | Control | Behavior |
| --- | --- | --- |
| F1 | Unlimited health | Refills current health every 100 ms while enabled. A lethal hit between refills may still kill the player. |
| F2 | Unlimited mana | Refills to current maximum mana. |
| F3 | Unlimited stamina | Refills to current maximum stamina. |
| F4 | Super damage | Multiplies calculated attack by 10 through the game's attack modifier. Final damage still follows game rules. |
| F5 | Unlimited gourd meter | Refills the equipped gourd's current meter to its current capacity. |
| F6 | Instant max focus | Fills current focus capacity once; does not unlock additional focus capacity. |
| F7 | Add experience | Adds the selected amount once; default 1,000, range 1–1,000,000. |
| F8 | Add sparks | Adds the selected number once; default 1, range 1–100. |
| F9 | Add will | Adds the selected amount once; default 1,000, range 1–1,000,000. |
| F10 | Disable all | Stops refills and restores the trainer's attack modifier when still owned. |

The client has amount fields for XP, Sparks and Will. In-game F7–F9 shortcuts use the defaults listed above. These actions dispatch the game's own progression commands once; the API returns no balance result, so check the in-game total before repeating an action after an uncertain outcome. Added progression may be persisted by the game's normal saves. Disabling controls does not undo those grants or resource refills.

All toggles start off and turn off when the player session changes or dies. The trainer operates only on the single possessed local player. Super damage captures the original attack modifier; disabling restores it only when its current value still matches the trainer's override. A game-driven modifier change turns the toggle off and is preserved. A temporary failed read retains the restoration record for a later attempt.

Commands are bounded, allowlisted, session-bound, expire after eight seconds and are consumed before dispatch. The trainer never evaluates mailbox text as code. All game calls use one game-thread timer; idle resource observation publishes at most one heartbeat per second. Remove trainer deletes only unchanged files owned by HappyCheats and preserves foreign mods/shared loaders.

## Validation

Offline checks cover discovery, executable identity, installer ownership/rollback (including the loader profile), current payload hashes, command identity/expiry/amount validation, at-most-once dispatch, resource caps, player ownership, session changes, damage multiplier math and restoration, diagnostic failures, and the English/Chinese UI.

On 29 September 2026, the user launched build 21393610 and loaded a save. Live trainer logs and read-only native inspection confirmed loader startup, the possessed player and resource function metadata, and identified the incorrect engine entry. The corrected profile uses the verified `ProcessEvent` slot 74 (byte offset `0x250`). Actual cheat effects, transformed-character behavior, progression totals and save/load persistence remain **unverified**. API/enum/command provenance is recorded in [binding evidence](wukong-bindings.md). No progression commands were sent during diagnosis and no save files were edited.

Both the complete LAN update package and standalone EXE contain the same Wukong module, installer, scripts and loader. No separate C# loader or reference game assemblies are distributed.

After installing 0.1.1 and reloading the user's save on 29 September 2026, the corrected loader logged the verified `ProcessEvent` implementation. The real client status parser reported **Connected / Ready**, all nine controls available, and plausible live health, mana, stamina, gourd, focus and attack values. All toggles remained off and no action commands were sent. This confirms the connection repair; cheat effects and progression persistence still require gameplay testing.
