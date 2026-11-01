# Dawnwalker resource-lock repair, trainer 0.4.2

On 1 November 2026, read-only inspection of the already-running Steam build 25915545 / CL-265636 captured the reported Super damage error and the possessed local player. Executable SHA-256 remains E6222B815B1C376F2C85A2189120C5A9B5CD30AC5688A34B61A2C18EFBC9B117. Inspection requested process query/read rights only. No native call, memory write, installed-module edit, save edit or game launch was performed.

The user reported draining health/stamina bars. Trainer 0.4.1 used percent setters every 250 ms, which permits consumption between refills. The repair uses the game's native continuous resource-lock effects after filling the resource.

## Ownership evidence for this exact executable

The previous implementation avoided LockHealth/LockStamina because they first remove the current game lock and the internal handles were not reflected. The new audit establishes narrow read-only custom IntProperty fields on CombatComponentBase:

| Resource | Native handle ID offset | Lock implementation RVA | Unlock implementation RVA |
| --- | --- | --- | --- |
| Health | 0x7C8 | 0x6020300 | 0x602D494 |
| Stamina | 0x7D0 | 0x60203D0 | 0x602D4E0 |

Lock implementations call their matching unlock, apply the resource override, and store the returned eight-byte active handle at those offsets. Unlock checks the first int32 against -1, removes the recorded effect through the ASC, then writes an invalid handle (-1, false). Only the int32 ID is exposed to Lua; trainer code never assigns either handle field. A corresponding table can be passed to the existing stack-count getter, whose identity is the active handle ID.

The possessed player's read-only snapshot showed both handle IDs at -1. Loaded GE_HealthOverride and GE_StaminaOverride defaults under /Game/_Dawnwalker/Stats/Effects have infinite duration (1), no stacking (0), one Override modifier (3) with SetByCaller magnitude (3), no executions and no GEComponents. Native Lock* provides the captured full-resource magnitude. Runtime checks require the inspected infinite/non-stacking defaults before activation.

Before invoking Lock*, require an invalid handle, a verified living possessed local player and matching character/combat ASC. Fill the resource, then repeat ownership and invalid-handle checks after setter callbacks. Record the resulting ID, require one active stack and a full-resource getter readback. Disabling invokes Unlock* only while the component still contains that exact owned ID. An ended effect can be forgotten without touching a replacement game lock. Uncertain application/removal and unreadable ownership block retry/cleanup instead of guessing.

Refill while an owned lock is active releases and reacquires that lock, preventing the game's percent setter from silently replacing its handle. A changed maximum resource is handled the same way. Session cleanup targets the original component/ASC. Pre-existing game locks are refused; later native game replacements are preserved. Blood remains a periodic refill. Scripted death and non-resource costs are outside this feature's promise.

## Verification limits

Lua fixtures exercise the actual extension, invalid/existing/replaced handles, independent controls, maximum changes, one-shot refill, callback ownership changes, native errors, pending cleanup and changed effect configuration. Installer requirements and payload manifests include resources.lua. Shared-menu Windows-style and native mouse-activation checks are synthetic/offline. Full release checks do not start the game.

The repaired native calls have not been loaded into the user's running game. Native lock application, bar behavior under combat/sprinting, disabling after sustained combat, final 10x damage, form/equipment transitions and physical-controller/menu focus behavior remain unverified in gameplay. Close Dawnwalker, install trainer 0.4.2 with client 0.3.64, then restart it normally.