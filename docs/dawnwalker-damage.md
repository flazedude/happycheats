# Dawnwalker damage multiplier audit

## Local trainer 0.5.3 candidate: complete GAS key identity (3 November 2026)

**Native enable/readback/restore validated; actual hit damage and publication pending.** After the 0.5.2 install, the live status recorded failures for melee and claws. Subsequent read-only sampling found all four base/current pairs zero, while the same ASC still contained positive additive modifiers and intact damage aggregators. A normal request through the installed 0.5.2 module then refused activation because no positive factors remained; its normal off command was acknowledged. The user initially kept the game open, then closed it for candidate testing. A later launch still loaded 0.5.2, confirmed by its startup log and four installed Lua files differing from the candidate; the cause of replacement was not established. After another user-initiated close, the normal installer reinstalled and hash-verified every 0.5.3 manifest file. No game was launched automatically.

On 3 November 2026 at approximately 17:16 Asia/Shanghai, the user's next session (PID 38336) loaded 0.5.3 and passed a normal candidate-module enable/off round trip. An independent read-only sampler observed evaluated melee **1.381799817 → 13.817997932 → 1.381799817**, claws **1.311999917 → 13.119999886 → 1.311999917**, magic **1.107999921 → 11.079998970 → 1.107999921**, and unarmed **1 → 10 → 1**. All four original zero bases and evaluated values restored exactly. Both commands were acknowledged; Super Damage was left off and unrelated toggles were preserved. This validates native multiplier application and cleanup, not actual hit outcomes. Local evidence: `artifacts/native-evidence/damage-click-20261103e.json` and `artifacts/validation/dawnwalker-053-damage-{enable,disable}-20261103e.json`. Automatic parry still awaits an eligible enemy attack before publication.

The earlier two-field descriptor copy was incomplete. Exact native setter `0x1342868` hashes the resolved property (`0x1AF3D70`) but also compares weak-owner identity, serialized path count and FName path contents when matching its existing aggregator map. Scalar reads/debug names can succeed without that path. All four captured map keys had one path component matching the attribute name, capacity two, and the same CharacterBaseAttributeSet weak owner. The empty-path copy consequently missed the existing map key and reached the base/current fallback, bypassing the existing bonuses. Restoring base zero through the same incomplete descriptor also left the cached current value zero. Merely changing damage tolerances or the correction formula would not fix this lookup.

The candidate retains the original descriptor source and adds narrowly allowlisted Int64 aliases for the path-data pointer (+0x20) and packed count/capacity (+0x28). The temporary argument borrows that unchanged path only during the audited synchronous calls. It rejects absent/misaligned storage, empty or excessive paths and changed source identity before writing. It never assigns damage attribute memory, effect records or UFunction flags.

This borrowing rule is specific to the shipped **b4cefa18** loader and exact game functions: its [parameter buffer](https://github.com/UE4SS-RE/RE-UE4SS/blob/b4cefa18/UE4SS/include/LuaType/LuaUFunction.hpp) is a zeroed raw 0x200-byte array without reflected-field destruction; the native setter's reference thunk does not mutate/destroy its input, and native by-value copies own their copied paths. The [loader marshalling implementation](https://github.com/UE4SS-RE/RE-UE4SS/blob/b4cefa18/UE4SS/src/LuaType/LuaUObject.cpp) was checked against this actual payload revision, rather than assuming the earlier related source revision covered ownership. This is not a general-purpose borrowed-struct API and must be re-audited if the loader/functions change.

Offline tests require full native-key identity at the setter boundary, reject missing/invalid path storage before any write, and retain correction/cleanup ownership checks. Errors also retain requested/observed base and evaluated numbers. The subsequent live round trip above establishes successful native calls and restoration; actual tenfold hit damage remains unmeasured.

## Trainer 0.5.2 custom struct-name correction (3 November 2026)

Package **1.0.3** fixes the subsequent `Native damage descriptor could not be copied` failure. A fresh read-only snapshot of the running 0.5.1 trainer found both custom attribute aliases absent from Unreal's name pool. Pinned `LuaUScriptStruct::prepare_to_handle` converts an indexed string with **FNAME_Find** and returns nil immediately for comparison index zero, before consulting the custom-property registry. Registering the alias alone therefore did not make userdata indexing possible. UObject custom-property access and struct-to-table conversion follow different paths, so the existing health-handle aliases did not expose this problem.

The two allowlisted attribute alias names are now interned with normal `FName(name)` construction before custom-property registration. Registration fails closed if name creation fails. No offset, pointer-copy bound or damage verification is relaxed. The regression fixture now models the separate name pool and requires interning before registration. New binding gameplay and actual tenfold damage still require confirmation after installing 0.5.2; no running trainer file was changed during inspection.

## Trainer 0.5.1 reference-parameter correction (3 November 2026)

Independent Dawnwalker package **1.0.2** corrects an argument-marshalling defect missed by the earlier audit. Exact-build reflection marks `CombatBlueprintFunctionLibrary.SetAttributeValue.AttributeToSet` with flags **0x18000008000180**: OUT and ReferenceParm, without ConstParm. The two scalar getter attributes are ordinary input parameters. Pinned UE4SS explicitly requires a Lua **table** for a non-const OUT argument before calling ProcessEvent; passing the getter's UScriptStruct userdata to the setter is rejected before native execution. The earlier generic verification error discarded that invocation failure and suggested active scaling instead.

A normal request through the installed 0.4.2 module reproduced the error, followed by its off/cleanup command. A bounded read-only sampler observed no melee base/current change. The possessed ASC's registered CharacterAttributeSet was confirmed. Its bases remained zero, with evaluated factors approximately **1.26654 melee**, **1.096 claws**, **1.012 magic**, **1 unarmed**. Active modifier metadata was inspected read-only. No new trainer was installed into the running game, and no save was edited. Deferred evaluation was considered but was not established as this failure's cause; no frame-delay workaround was added.

A plain attribute table would lose the unsupported FieldPathProperty. The new binding therefore exposes two tightly pinned Int64 aliases on `/Script/GameplayAbilities.GameplayAttribute`: resolved FProperty at **0x10**, and its weak-owner index/serial at **0x18**. These are copied from the original native descriptor into a fresh temporary parameter table, together with normal reflected AttributeName/AttributeOwner. The path array stays empty and owned by the parameter; no borrowed allocation or ref-counted path is copied bitwise. Native constructor `0x1058A34` and resolver fast paths were checked for this exact build. No live attribute-set data or UFunction parameter flags are written.

Before any mutation, the reconstructed descriptor must reproduce the original native debug name, base value and evaluated value through the normal getters. Correction and cleanup each receive a fresh OUT table, so output conversion cannot corrupt the stored descriptor snapshot. All existing ownership checks, bounded correction, final evaluated verification and conditional restoration remain. Actual setter-binding failures now report their native/loader reason separately from a successful setter whose evaluated factor misses its target.

Offline fixtures model the setter's table-only reference contract, validate alias registration/layouts, reject damaged descriptors before writes and exercise destructive OUT-table conversion during correction/cleanup. The rebuilt 0.5.1 binding and final tenfold combat damage still require gameplay confirmation after updating and relaunching.

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

The initial audit used read-only executable inspection and existing trainer log/status files. The saved trainer status was 0.4.0, with a ready player and Super damage available but disabled; its most recent command concerned automatic teleport. No damage failure message remained in that snapshot, and the game was initially closed.

Later on 30 October, the user opened Dawnwalker and loaded a save. A read-only process inspection confirmed the possessed local player's controller, pawn, ability-system owner/avatar and attribute-set ownership. All four damage bases were zero; evaluated values were approximately **1.18 melee**, **1 claws**, **1.0414 magic** and **1 unarmed**. These live values confirm that the previous positive-base requirement rejects an active player's valid attributes. The inspection did not determine the active aggregator's additive/multiplicative operations or test the new setter/readback path. No game function was invoked by the inspection tools, and no memory, save or installed trainer file was changed. The user chose to install the new trainer later.

The zero-base rejection and base-only confirmation are established code defects. The exact runtime condition behind the user's earlier error was not captured. Offline regression fixtures and native readback guards improve the implementation, but do not establish a tenfold final hit in gameplay: damage remains subject to the game's other combat rules. Actual gameplay, form/equipment transitions and physical-controller operation still need user testing.

## Trainer 0.4.2 follow-up (1 November 2026)

The live 0.4.1 status and client error log captured the failed 10x MeleeDamageMultiplier confirmation. Read-only inspection of PID 29372 confirmed the possessed local pawn and original ASC. Native damage bases were all zero; evaluated factors were about 1.15 melee, 1.05 claws, 1.0414 magic and 1 unarmed. The previous delta assumes unit response to a base change and refuses scaled aggregation.

The new path retains the initial bounded proposal. If the base matches the owned write but the evaluated response is positive and misses the target, it computes one correction from the observed response: original base + (target current - original current) * base delta / evaluated delta. The correction is bounded to 1/16–16 times the initial delta and a base of 0–1,000,000. It rechecks both observations before writing, retains both possible owned values for a lost/ignored second write, and requires all final factors to match. Flat overrides, clamps, nonlinear changes and unreadable results still fail closed with conditional restoration. There is no polling write loop or compounding.

Offline fixtures cover scaled zero bases, bounded attempts, ignored correction and rollback. No correction or native setter was invoked in the running game by the inspection tools. A final tenfold hit and transitions remain unverified.
