# Dawnwalker patch 3 binding audit

Audit date: 24 October 2026. Target: Steam build **25915545**, **CL-265636**, patch 3. The installed `Dawnwalker/Binaries/Win64/Dawnwalker.exe` is 179,662,712 bytes, SHA-256 `E6222B815B1C376F2C85A2189120C5A9B5CD30AC5688A34B61A2C18EFBC9B117`.

This audit read the executable's generated Unreal registration data and native function bodies from disk. All addresses below are RVAs in that exact executable. It did not launch or attach to the game, invoke engine functions, change game files or saves, or validate gameplay. Local working evidence is under `artifacts/dawnwalker/update-20261024-bindings`; extracted code and game assets are not source deliverables.

## Custom fields

| Binding | New-build evidence | Consequence |
| --- | --- | --- |
| `HappyCheatsPlayerStubVisibilityFlags`, `DogwoodPlayerAIStub`, byte `+0x30`, mask `0x10` | Class registration at `0x183BB18` specifies **size `0x260`**, alignment 8. The successful-stimulus path `0x141A358` enters `0x141A5E8`, which rejects a target with this bit at `0x141A642`, before recording the stimulus. `IsPlayer` at `0x5E76884` still compares stub type at `+0x40` with 1. | The custom byte offset and mask remain valid. The previous class-size comment (`0x270`) was obsolete. Keep exact player subclass, actor/mapped-stub ownership, bit-preserving restoration, and pending cleanup checks. This does not erase existing enemy alert memory. |
| `HappyCheatsItemHandleIndex`, `ItemHandle`, int32 `+0` | Generated struct definition `0xA07FA38` has zero reflected fields, size 4, alignment 4. `GetAssetForItem`'s native lookup at `0x16E74A4` reads signed int32 `[rdx]`, rejects negative/out-of-range indices, and resolves the item asset. `IsQuestItem` at `0x1621018` also reads the index at offset zero. | Keep the existing narrow custom property and asset round-trip validation. Passing an empty opaque handle table is still insufficient. |

## Resources and combat

The generated registration data retains the following names and parameter contracts used by the trainer. Reflected access resolves fields and functions by name; the trainer must not adopt the native component offsets observed during this audit.

| Calls | Confirmed contract / evidence |
| --- | --- |
| `CombatComponentBase.SetHealthPercent`, `SetStaminaPercent`; `GetHealthPercentage`, `GetStaminaPercentage`, `GetAbilitySystemComponent` | Setters take one float; percentage getters return float; ASC getter returns object. Native exec RVAs are `0x5FEF894`, `0x5FEFEA8`, `0x5FEC8C0`, `0x5FECA48`, `0x5FEC204`. Health's body at `0x6029860` still multiplies the maximum attribute by the supplied fraction and applies the difference. |
| Blood-bar `SetBloodPercent`, `GetBlood`, `GetBloodBarLength` | One float argument / float returns; exec RVAs `0x5F1B9F8`, `0x1EBEA04`, `0x1992884`. |
| `BP_ApplyGameplayEffectToSelf` | Class object at parameter offset 0, float level at 8, 24-byte effect context at 16, 8-byte active handle return at 40; parameter frame size 48. Native `0x1839750` still detects a null context (`0x183989B`) and creates one through ASC virtual slot `+0x730` (`0x18398AA`). The existing empty-table context uses that native fallback. |
| Active effect ownership | `ActiveGameplayEffectHandle` definition `0xA01E960` remains size 8/alignment 4, with reflected `Handle` int32 at 0 and `bPassedFiltersAndWasExecuted` bool at 4 (setter helper `0x28763A0`). Stack-count and removal APIs retain handle inputs; removal also takes int32 stack count. Preserve returned-handle validation and removal of only the trainer-owned effect. |
| Invulnerability effect | `DogwoodAIGameplayEffect_Invulnerability` constructor `0x1EFABA0` sets duration policy to 1 and adds a target-tags component using the native `Effect.Invulnerability` tag. Its tag registration is `0xE35AE0`. Existing runtime CDO configuration checks remain necessary. |
| Damage attributes | `GetAllAttributes` retains its output array; debug-name lookup takes a 56-byte `GameplayAttribute` and returns string; base-value lookup takes ASC plus attribute and returns float with an output bool; `SetAttributeValue` takes owner, attribute and float. New exec RVAs are `0x58FD468`, `0x58FD9D4`, `0x58FE00C`, `0x23F44DC`. `UnarmedDamageMultiplier`, `ClawsDamageMultiplier`, `MeleeDamageMultiplier`, `MagicDamageMultiplier` remain reflected attribute structs at `+0x1B0`, `+0x1C0`, `+0x1D0`, `+0x1E0`. Preserve descriptor identity checks, output-array unwrapping, base-value readback and owned-value restoration. |

## Inventory and progression

`InventoryItem` remains a 16-byte, 8-byte-aligned reflected struct: `ItemDataAsset` object at 0, `Quantity` int32 at 8, `Level` byte at 12. Its generated definition is `0xA07FAA8`. `GetCurrentItems` still returns an array, so the existing UE4SS output-element unwrap remains required.

`InventoryComponent.WeightLimit` remains a float at `+0x158`. `GetWeightLimit` calls `0x1333990`, which returns a nonpositive configured limit directly and adds the attribute contribution only for a positive limit. `ItemBaseDataAsset.IsSingleUse` remains a bool at `+0x90`, proven by its generated setter `0x287AD10`; its descriptor also contains the owning struct size, which must not be mistaken for the field offset. Native `UseItem` at `0x5FB20F0` still executes the item effect before the consumption decision. Existing inventory ownership, consumable type, quest exclusion and restoration checks remain in force.

Currency APIs retain an enum currency input and int32 quantity/result; `AddCurrency` returns an enum. `GetHandleForAssetInInventory` takes an asset object and returns `ItemHandle`; `GetAssetForItem` takes that handle, has an output level byte and returns the asset; `IsQuestItem` takes the handle and returns bool. Their generated parameter-frame sizes remain 12, 8, 16, 16 and 8 bytes respectively (`AddCurrency`, `GetCurrencyAmount`, handle lookup, asset lookup, quest check).

The progression APIs also retain their contracts: `GetCurrentXP`, `GetCurrentLevel` and `GetNextLevel` return int32; `IsAnyCharacterDevelopmentModeEnabled` returns bool; `GetCurrentLevelXPRequirement(int32)` returns int32; `ForceLevelUp` takes no parameters. `PlayerASC` remains a reflected weak object field, now at `+0x660`; the trainer resolves it by name and unwraps it, rather than using its old offset.

The private level map moved to `+0x8C0`, with cap/progression bytes at `+0x910/+0x911`. `GetCurrentLevelXPRequirement` (`0x5FD4490`) still returns zero for a missing cache entry, guarded at `0x5FD459C`. **`ForceLevelUp` (`0x5FD3F14`) still dereferences a missing entry unsafely.** Keep the immediate same-game-thread positive current/following threshold probes and ownership/progression checks; do not substitute a quest-reward getter or a guessed XP amount. `ForceLevelUp` computes threshold minus current XP and calls `AddXP` (`0x5FDD488`). The level-up routine (`0x5FE2D40`) still subtracts that threshold, increments the level, and runs the native reward path. The positive following-threshold check prevents a zero-cost second level after reaching zero remaining XP.

## Scope of the conclusion

No inspected custom field or reflected call contract requires a Lua behavior change for this build. Some private layouts and native addresses moved; the two custom fields used by combat and inventory were rechecked against this executable. Movement's separate HitResult field is covered in its companion audit. This is a compatibility finding, not gameplay certification.

Live CDO values, loaded Blueprint overrides, player/session discovery, runtime output marshalling, restoration during actual transitions, detection behavior, progression rewards and performance remain untested on patch 3. The audit does not exhaustively prove every native cache writer or every AI perception path. Keep all existing runtime availability, type, ownership, value and readback guards; do not claim that offline checks establish working gameplay or physical-controller behavior.
