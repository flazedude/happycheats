# Phantom Blade Zero binding evidence

The first profile targets Steam application **4115450**, build **26266538**, with
the installation-relative game process **`platform/steam/PBZ.exe`**. The small
`steam/PBZ.exe` is a launcher, not the shared menu target. The inspected game
executable has SHA-256
`7B4FCD286A350780FAF0824D73AA150B21CBF1BCCDE883E6A96386CBE09973B4`.
Its embedded engine string is **Unreal Engine 5.7.4** (image RVA `0x0C9746F8`).

The evidence below was extracted read-only from the user's installed executable
on 29 October 2026. Addresses identify the inspected evidence only. Production
Lua uses reflected paths to the owned player's attribute set and its remote
`FGameplayAttributeData.CurrentValue` float fields. These are direct reflected
memory writes; the runtime contains no hard-coded game offsets or fabricated
Unreal structures and never writes an attribute's `BaseValue`. Extracted
game material and research scratch files remain under ignored
`artifacts/phantom-blade/research-agent` and are not distributed.

## Player and reflected attributes

The generated class constructor at RVA `0x0651ED90` registers
`/Script/ProjectPBZ.PBZCharacter_Player`. Trainer 0.1.2 identifies a
`PlayerController` whose reflected `Player` is an `/Script/Engine.LocalPlayer`
and whose `Player.PlayerController` points back to that controller. Its `Pawn`
must have the exact player-class relationship and a reciprocal `pawn.Controller`
link. This replaces the repeated reflected `IsLocalController`, `K2_GetPawn`,
`IsPlayerControlled` and `GetController` calls. The world, controller and pawn
addresses form a session identity. Stable ownership uses cached objects;
rediscovery is throttled after failure. Unrelated controllers do not impose a
count limit; multiple matching local players are still rejected.

The generated constructor at RVA `0x05DD24C0` registers
`/Script/CharacterBasics.BasicAttributeSet` (native size `0xE38`).

The player exposes `AttributeSetBase`, an object property typed as that class.
Each access revalidates the attribute-set class and its `GetOuter()` ownership
against the selected pawn. The trainer obtains a fresh remote struct wrapper
for each field access and requires `IsValid()`, `IsMappedToObject()` and
`IsMappedToProperty()`. It never retains a raw struct view across updates.

The pinned UE4SS implementation's ordinary remote-property read returns a
`LuaUScriptStruct` wrapper. Its `__newindex` implementation finds the nested
reflected float property and dispatches the registered `FloatProperty` setter.
This preserves the struct's other fields, including `BaseValue`. Relevant
source is `LuaUObject.cpp` (`push_structproperty`, `push_floatproperty` Set case) and
`LuaUScriptStruct.cpp` (`__newindex`) at pinned revision
`2bfa839fe2945f9230204145646b5be4ae84fde8`.

## Inspected native APIs and the marshalling limitation

`/Script/ProjectPBZ.Default__PBZBlueprintFunctionLibrary` exposes:

```text
FGameplayAttribute MakeAttribute(FString AttributeName, TSubclassOf<UAttributeSet> Set)
```

The generated function descriptor at RVA `0x0DB76438` records three parameters,
80 bytes total: `AttributeName` at offset 0, `Set` at 16, and the returned
`FGameplayAttribute` at 24. Native wrapper `0x065E7240` calls `0x06A77450`, which
enumerates the basic attribute-set fields and compares their property names.
A missing field yields an empty attribute. This function is **not used by the
trainer**: the pinned loader converts a returned local struct into a Lua table
but has no `FieldPathProperty` pusher. Consequently, passing this result back
would lose the `FGameplayAttribute.Attribute` field. Native API presence alone
does not make that calling path usable.

The generated class registration proves the utility namespace is
`/Script/SGameAttributes.AttributeUtils`; its default object exposes:

```text
float GetCharacterAttributeValue(AActor Character, const FGameplayAttribute& Attribute)
void ModifyCharacterAttributeValue(AActor Character, const FGameplayAttribute& Attribute,
                                  EGameplayModOp ModiferOp, float Value)
```

`ModiferOp` is the spelling present in the game. Function descriptors are
`0x0DB27940` and `0x0DB279A0`; native wrappers are `0x05DA7440` and `0x05DA8600`.
The latter reaches implementation `0x05DA1710`, which obtains the character's
ability system and applies the game's attribute modification path. The local
`EGameplayModOp` generated enum table at `0x0B685290` explicitly assigns
**Override = 3**, rather than relying on an enum value from another game.

These native writes return void and are not invoked by the trainer. Inspection
also found the game's
`/Script/GameplayAbilities.Default__AbilitySystemBlueprintLibrary`:

```text
float GetFloatAttributeBase(AActor Actor, FGameplayAttribute Attribute,
                            bool& bSuccessfullyFoundAttribute)
```

Descriptor `0x0DAC3EE8`, wrapper `0x04C87E90`, establishes this base-value getter.
Its out boolean reports whether the attribute was found. This getter is also
unused because it consumes the same unsupported returned attribute structure.
The production binding reads the owned remote struct's `BaseValue` directly
when checking attack ownership; it never substitutes an unknown base value.

## Health and Sha-Chi

| Resource | Current field | Maximum field | Native evidence |
| --- | --- | --- | --- |
| Health | `Health` | `Health_Max` | `GetHealth` reaches `0x067E3020`, reading native attribute member `0x58`; `GetMaxHealth` reaches `0x067E3620`, reading `0x68`. Generated properties agree. |
| Sha-Chi (stamina control) | `Shachi` | `Shachi_Max` | The game's legacy-named `GetAnger` reaches `0x067DF7F0`, reading member `0x638`; `GetMaxAnger` reaches `0x067E35E0`, reading `0x658`. Generated properties name these members `Shachi` and `Shachi_Max`. |

The stamina control specifically refills the **Sha-Chi** resource; it does not
claim to identify another resource from the English word "anger" alone. Both
toggles set the remote reflected `CurrentValue` float to the maximum attribute's
`CurrentValue`. They do not change `BaseValue` or maximum values. Non-positive health blocks
every refill, so the module does not attempt resurrection. Periodic refills
cannot prevent a lethal hit or resource consumption between updates.

The game also has real `GM_SetHealth(float NewHealth)` and
`GM_GetHealth()` functions on `/Script/ProjectPBZ.PBZCheatManager`. The first
reaches native `0x06629D00`; the second only logs a result. They are evidence
supporting the resource identification and are not used by the trainer.

## Damage multiplier means attack power

`BasicAttributeSet.Attack` is a native `FGameplayAttributeData` member at
`0xC8` (generated descriptor `0x0BC79E10`). The normal combat calculator
`/Script/PBZCombatAsset.DamageCalculator_PBZNormal`, native calculation function
`0x07D6AA50`, uses configured `PBZAttackDamageData.AttackAttribute` alongside
amplification, resistance, and other combat rules. That configured attribute
member is recorded by descriptor `0x0C65B530`.

The trainer's numeric multiplier scales **attack power**, from 1 through 20.
It does not promise that every final damage channel scales by exactly that
factor. The game has a separate `PlayerDamageMultiplier` property on
`DamageCalculator_PBZPlayerScaled`; this is shared calculator configuration
and is deliberately not modified.

Before applying a multiplier, the current attack value must agree with the
reflected base value. If an active gameplay modifier causes a discrepancy, the
control refuses to apply. The trainer records the unchanged base, original
current value, proposed current override, original pawn/world, and attribute-set
identity before the write. Current readback must confirm the requested value
and base readback must confirm the base was unchanged. A write or
readback failure retains the restoration record when the result is unknown.

Reset restores only the original `CurrentValue`, only while the base remains
unchanged and current still equals the trainer's override on the original
attribute set. Any later game-owned base or current change releases ownership
and is preserved. The multiplier is disabled when normal attribute recalculation
overwrites it; the trainer does not continuously reapply or compound it. It may
therefore need to be reapplied after gameplay updates. No base attribute or
gameplay-effect modifier is altered by this implementation.
World/player transitions reset the controls; any pending restoration remains
bound to the original pawn and cannot be applied to a replacement character.

## Read-only live metadata check, 29 October 2026

After the user authorized starting the game for inspection and loaded a save,
the same hash-gated executable was inspected using query/read process access.
The controlled `BP_Renewal_Hun_C` instance in `PBZ_Xigu_WP` had the expected
`PBZCharacter_Player` ancestry. Its controller link, the controller's `Pawn`
and `AcknowledgedPawn`, and the attribute set's outer ownership agreed. The
controller's local-player flag was true and its `Player` pointed to a
`PBZLocalPlayer` instance.

The live `AttributeSetBase` snapshot contained:

| Field | Base | Current |
| --- | ---: | ---: |
| Health | 416.5 | 416.5 |
| Health_Max | 850 | 850 |
| Shachi | 400 | 400 |
| Shachi_Max | 400 | 400 |
| Attack | 100 | 100 |

Live reflected field schemas independently confirmed `FGameplayAttribute` is
56 bytes (`AttributeName` string, `Attribute` field path, `AttributeOwner`
object), and `FGameplayAttributeData` is 16 bytes with float `BaseValue` and
`CurrentValue` at offsets 8 and 12. The owned player object's `AttributeSetBase`
property is at `0x758`; this inspection offset is not embedded in runtime Lua.
The four native functions above were present with the expected
parameter types and native implementation pointers. In particular, the
utility parameter named `Character` is typed `AActor`; `MakeAttribute.Set` is
a class property constrained to `UAttributeSet`; and the base getter's
`bSuccessfullyFoundAttribute` is a boolean out parameter.

The resource reads used reflected member offsets solely in the inspection
helper. No game function was called, no trainer was installed or injected by
this check, no memory was written, and no cheat was enabled. This establishes
live metadata and resource/ownership observations, not trainer readiness or
cheat effects. Local evidence is retained in
`artifacts/phantom-blade/live/binding-metadata.json`,
`binding-live-player.json`, and `binding-player-ancestry-metadata.json`.

## Active-game detection failure, 31 October 2026

Trainer 0.1.1 loaded successfully and published a fresh heartbeat, but reported
that a save must be loaded while the user was already playing. Read-only process
inspection found one live `BP_Renewal_Hun_C` player and one controller, reciprocal
`Controller.Pawn` / `Pawn.Controller` links, the expected player ancestry, and
an owned `AttributeSetBase`. `Controller.Player` was `PBZLocalPlayer`, deriving
from `/Script/Engine.LocalPlayer`; its reflected `PlayerController` property
at inspection offset `0x30` pointed back to the same controller. Sampled health
was about 829/850, Sha-Chi 400/400, and attack 100 in `PBZ_Xigu_WP`.

A bounded read-only scan near the trainer's Lua state retained a complete
traceback: `bindings.lua:17: Array failed invariants check, ArrayNum exceeds
ArrayMax`, in the `index` metamethod, called by installed `bindings.lua:52`.
That line looks up `K2_GetPawn`; the error occurs before invoking the function.
The catch discarded it and reported the generic load-save message. This pins
the failing lookup path; the traceback does not identify the internal property
or function lookup operation that threw.

Trainer 0.1.2 reads the verified reflected properties instead of looking up the
four redundant game getter functions. The pinned `LuaUObject.cpp`
`handle_unreal_property_value` uses `GetFunctionByNameInChain` only when no
reflected property is found; `Pawn`, `Controller`, `Player` and
`PlayerController` have inspected property metadata. Remaining `IsA`,
`GetWorld`, `GetOuter`, and validity/address methods are UE4SS wrapper methods.
The arbitrary rejection of controller lists longer than 16 is removed;
unrelated candidates do not invalidate one matching local player.

These inspections used query/read process access only, with no engine calls,
injection, memory writes or save edits. Local evidence is retained under
`artifacts/phantom-live/` (`snapshot.json`, `readiness-traceback.json`). Tests
exercise unavailable/throwing old getter methods, direct-property failures,
remote or ambiguous players, many irrelevant controllers, session transitions
and ownership changes before refill. The changed Lua path requires a restart
before its live readiness can be established.

## Validation boundary

Static inspection establishes the executable identity, engine string, native
class/function metadata, parameter types, enum value, resource aliases and
attribute fields above. Synthetic tests establish protocol, ownership,
availability, write/readback handling and restoration behavior against mocks.
The read-only live check additionally confirms the loaded schemas, player
ownership and sampled resource values described above.

The observed 0.1.1 heartbeat establishes loader startup on this executable.
Trainer 0.1.2 readiness, successful live reflected reads/writes, actual combat
effects, controller operation and rendering over the running game remain
unverified. Those require separate live evidence after updating and restarting.
The default state has all controls off and multiplier 1. No progression or
save-editing command is implemented by this profile.
