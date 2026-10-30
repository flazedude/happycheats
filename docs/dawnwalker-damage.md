# Dawnwalker damage multiplier audit

Inspected on 30 October 2026 against the installed Steam build **25915545 / CL-265636**. The executable SHA-256 is `E6222B815B1C376F2C85A2189120C5A9B5CD30AC5688A34B61A2C18EFBC9B117`, matching the trainer's existing compatibility gate. All addresses below are executable-relative RVAs for that exact binary.

## What the previous implementation missed

Trainer 0.4.0 required all four damage **base** values to be positive, multiplied those bases by ten, and checked only the base values after writing. That does not establish that the **evaluated** attributes consumed by combat increased tenfold. A valid zero base was rejected before any write, and active gameplay modifiers can make a base and its evaluated value differ.

The native `CharacterBaseAttributeSet` constructor is registered through `0x162283C` and constructor thunk `0x1D52AD8`, reaching `0x1D537D8`. It initializes the base/current pairs of all four damage attributes to zero:

| Attribute | Attribute struct offset | Zero-initialization instruction |
| --- | --- | --- |
| UnarmedDamageMultiplier | `0x1B0` | `0x1D53962`: zero qword at `+0x1B8` |
| ClawsDamageMultiplier | `0x1C0` | `0x1D53970`: zero qword at `+0x1C8` |
| MeleeDamageMultiplier | `0x1D0` | `0x1D5397E`: zero qword at `+0x1D8` |
| MagicDamageMultiplier | `0x1E0` | `0x1D5398C`: zero qword at `+0x1E8` |

These constructor defaults prove that zero is a valid native value. They do **not** prove a particular loaded player's values: initialization, equipment and gameplay effects can change them later.

## Combat uses evaluated multipliers directly

The physical damage capture constructor `0x1ADC9A8` obtains the reflected melee descriptor through `0x23DB810`, unarmed through `0x24A4318`, and claws through `0x24A43B4`. It registers them using `0x1ADDFE0`. The corresponding capture members are `+0xC0`, `+0x100`, and `+0x200`.

The physical multiplier selector at `0x5F23DB8` chooses those captures. Its consumer at `0x5F21DCC` calls that selector through virtual slot `+0x2E8` at `0x5F21E71`, retains the result in `xmm15`, and multiplies by it at `0x5F21F3C`. The magic calculation capture constructor `0x1ADC168` uses descriptor getter `0x1AD9D6C`. The consumer `0x5F20F48` evaluates the magic capture at `0x5F21238`, retains it in `xmm7`, and multiplies by it at `0x5F212AA`.

The capture evaluator `0x13C3638` reads the captured aggregator through `0x13C6A44`. These paths provide no evidence for an implicit `1 + attribute` convention. Consequently, changing the old formula to `(1 + base) * 10 - 1` would be an unsupported assumption.

## Verified setter and readback contracts

`/Script/DogwoodCombat.CombatBlueprintFunctionLibrary:SetAttributeValue` takes **AbilitySystemComponent**, GameplayAttribute and float. The name `AttributeOwner` does not mean Actor: property descriptor `0x95A45C0` references constructor `0x2378418`, which resolves to `0x12DE1C8`, the `/Script/GameplayAbilities.AbilitySystemComponent` class registration. Passing the player's ASC was correct in the previous implementation.

Its native exec wrapper `0x23F44DC` calls `0x1342070`, then `0x1342868`. This is a **base-value** update: it runs the attribute set's base-change callbacks, changes `GameplayAttributeData.BaseValue`, and updates/dirties an existing aggregator. It does not bypass gameplay modifiers to force an evaluated value.

Both required readback functions exist on the same ability-system Blueprint library:

- `GetFloatAttributeBaseFromAbilitySystemComponent`: native exec `0x58FE00C`, implementation `0x58DCB50`.
- `GetFloatAttributeFromAbilitySystemComponent`: native exec `0x1D5AF88`, implementation `0x15A177C`. Function definition `0xA01C860` uses property table `0x93FDC18`, with `AbilitySystem`, `Attribute`, `bSuccessfullyFoundAttribute`, and `ReturnValue`. The implementation checks ASC/attribute existence before returning the evaluated numeric attribute through `0x12DF2EC`.

Both expose the output Boolean under the exact spelling `bSuccessfullyFoundAttribute`. Base and evaluated values can therefore be checked through native APIs without raw attribute-memory writes.

The improved implementation targets ten times each positive evaluated multiplier, proposes a base increase of nine times the captured evaluated value, and requires native readback to confirm the requested evaluated result. This is a guarded proposal, not an assumption that every active gameplay modifier is additive. A clamp, override or other modifier that prevents the requested result causes failure and conditional restoration of the trainer's writes. Zero-valued inactive attack types remain unchanged; they must not prevent supported active attack types from being checked. Later game-owned base changes remain protected by exact owned-value comparisons.

## Loader conversion check

The pinned UE4SS source is commit `2bfa839fe2945f9230204145646b5be4ae84fde8`.

`GetAllAttributes` is an output-array path. In [LuaUObject.cpp](https://github.com/UE4SS-RE/RE-UE4SS/blob/2bfa839fe2945f9230204145646b5be4ae84fde8/UE4SS/src/LuaType/LuaUObject.cpp), array elements use `Operation::GetParam`; struct elements become `LocalUnrealParam<ScriptStructWrapper>`. Its `get()` in [LuaUObject.hpp](https://github.com/UE4SS-RE/RE-UE4SS/blob/2bfa839fe2945f9230204145646b5be4ae84fde8/UE4SS/include/LuaType/LuaUObject.hpp) requests `Operation::Get`, producing UScriptStruct userdata. When that userdata is passed back to a native struct parameter, the loader uses `CopyCompleteValue` and preserves the native field path.

This differs from a directly returned struct converted to a Lua table, which can lose unsupported `FieldPathProperty` contents. The existing output-array unwrapping is retained; the direct-return limitation described in the Phantom Blade audit is not a demonstrated defect in this Dawnwalker path. The native debug-name formatter `0x58DC3B0` also retains the `{0}.{1}` format at `0x989A348`.

## Validation limits

This audit used read-only executable inspection and existing trainer log/status files. The saved trainer status was 0.4.0, with a ready player and Super damage available but disabled; its most recent command concerned automatic teleport. No damage failure message remained in that snapshot. No Dawnwalker process was running when process inspection was checked, so no live attribute values were read. No game was launched, no game function was invoked by the inspection tools, and no save or installed trainer file was changed.

The zero-base rejection and base-only confirmation are established code defects. The exact runtime condition behind the user's earlier error was not captured. Offline regression fixtures and native readback guards improve the implementation, but do not establish a tenfold final hit in gameplay: damage remains subject to the game's other combat rules. Actual gameplay, form/equipment transitions and physical-controller operation still need user testing.
