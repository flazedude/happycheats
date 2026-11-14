# Phantom Blade Zero expansion evidence, 14 November 2026

This review read the installed executable and retained metadata without starting the game, invoking trainer commands, or changing saves. Steam application `4115450` remains build **26367565**. The installation-relative game executable is `platform/steam/PBZ.exe`; SHA-256 is `4415933ce33476f14c837abb61a1b60af8b78cd81783896501913a7aac69e423`. It matches the existing compatibility gate. The older read-only live metadata is supporting evidence, not current gameplay validation.

The addresses below are executable-relative static evidence. Runtime controls use reflected properties and validated owned objects; these addresses are not memory-write offsets in the trainer.

## Reflected fields in the current executable

| Owning class | Field | Generated descriptor RVA | Type / native field offset |
| --- | --- | --- | --- |
| `/Script/Engine.Actor` | `bCanBeDamaged` | `0xB0505A0` | BoolProperty, byte `0x5A`, mask `0x04` |
| `/Script/Engine.Actor` | `CustomTimeDilation` | `0xB050B38` | FloatProperty, `0x68` |
| `/Script/Engine.Character` | `CharacterMovement` | `0xB0D0DE0` | ObjectProperty, `0x338`, typed `CharacterMovementComponent` |
| `/Script/Engine.CharacterMovementComponent` | `GravityScale` | `0xB0F9420` | FloatProperty, `0x1A8` |
| `/Script/Engine.CharacterMovementComponent` | `JumpZVelocity` | `0xB0F9498` | FloatProperty, `0x1B0` |
| `/Script/Engine.CharacterMovementComponent` | `MaxWalkSpeed` | `0xB0F98C8` | FloatProperty, `0x288` |
| `/Script/CharacterBasics.BasicAttributeSet` | `ShachiRegen` | `0xBCE3AF0` | GameplayAttributeData StructProperty, `0x678` |
| `/Script/CharacterBasics.BasicAttributeSet` | `ShachiRegenCooldown` | `0xBCE3B30` | GameplayAttributeData StructProperty, `0x688` |

The native generated boolean initializer at `0x3A9F960` sets only mask `0x04` at actor byte `0x5A`. The retained `artifacts/phantom-blade/live/binding-player-ancestry-metadata.json` independently identifies the same `bCanBeDamaged` boolean and `CustomTimeDilation` float on the player ancestry, and identifies `CharacterMovement` as a `CharacterMovementComponent` object.

Current class constructor `0x3CEE240` registers `/Script/Engine.CharacterMovementComponent`. Class parameters at `0xB0FDFB0` refer to its property array `0xB0FD5B0`, which includes the gravity, jump and walking-speed descriptors above. This establishes their class ownership, beyond a string-name match.

Current class constructor `0x5E1B3A0` registers `/Script/CharacterBasics.BasicAttributeSet`. Its class parameters at `0xBCE6240` refer to property array `0xBCE5AF0`; array entries `0xBCE5E08` and `0xBCE5E10` select the regeneration and cooldown descriptors. Both are generated struct properties with the same GameplayAttributeData type constructor (`0x4C75440`) as the existing health/attack attributes. Retained read-only metadata also identifies these fields on this exact class.

## Calling and write semantics

The pinned loader source at commit [`2bfa839fe2945f9230204145646b5be4ae84fde8`](https://github.com/UE4SS-RE/RE-UE4SS/blob/2bfa839fe2945f9230204145646b5be4ae84fde8/UE4SS/src/LuaType/LuaUObject.cpp#L1297) implements reflected float reads and writes. Its boolean handler respects the reflected byte offset and masks, preserving other bits. These are supported scalar property operations, without constructing an unsupported FGameplayAttribute value.

The existing exact-target health function remains `/Script/ProjectPBZ.PBZRestoreQueryLibrary:ResetHealthToMax`, called with its matching default object and the owned pawn. Its current native wrapper is `0x65C7830`, calling implementation `0x6923240`; the reflected signature is `bool ResetHealthToMax(AActor* Target)` with target at parameter offset zero and return boolean at offset eight. A manual health refill can use that existing path, including its native result and base/current/maximum readback. A manual Sha-Chi refill can use the existing reflected refill path. Neither action should be remembered or replayed after a load.

`bCanBeDamaged = false` is an actor damage guard, not proof of complete invulnerability: the game's ability or scripted damage paths may bypass the ordinary actor gate. `CustomTimeDilation` changes the selected actor's simulation and animation speed; it is not global game speed. Movement settings are component limits and can be replaced by game-owned movement or ability calculations. Increasing `JumpZVelocity` does not prove every game-specific jump animation uses that value.

Regeneration scaling uses the owned attribute's `CurrentValue`, with unchanged captured `BaseValue`, like the existing attack override. It does not invoke a canonical attribute setter or promise persistence through the game's recalculation. The existing marshalling restriction on FGameplayAttribute still applies; see [binding evidence](phantom-blade-bindings.md).

Any reversible scalar override needs fresh player/session validation, exact target class and ownership, finite bounded values, readback, and a lease recording its original value. Restoration belongs to that original object only and must preserve subsequent game-owned changes. Missing or changed targets must produce specific operation errors. Requested toggles can be reapplied through the existing command path to a new ready session; one-shot refill actions cannot.

## Deliberately unestablished options

The executable contains names such as `GM_InfiniteMedicine`, `GM_InfiniteUltimateShachi`, and `GM_State`. Their presence does not prove an effective, owned-target, reversible command path. Some share a protected implementation entry; this review did not equate that entry with an empty function. No currency grant, medicine inventory edit, talent unlock, enemy mass action, save modification, or global cheat-manager mutation is justified by those names alone.

This evidence supports implementation and offline fixtures. It does not establish live effects, startup of the current loader in gameplay, physical-controller behavior, or save compatibility.
