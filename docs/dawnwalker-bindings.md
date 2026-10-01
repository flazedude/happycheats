# Dawnwalker binding evidence

Inspected on 26 September 2026 from the user's installed Steam game and its
already running process. Inspection used file reads and `ReadProcessMemory`
only. No game was launched, stopped, injected into or modified, and no save was
written for this investigation. Native functions were **not** executed in the
game during development. The shipped Lua bindings still need gameplay testing
after the user installs the loader and starts the game normally.

## Inspected build

* Steam app: `3751260`; build: `25232147`.
* Executable: `Dawnwalker/Binaries/Win64/Dawnwalker.exe`.
* Version: `dw1-pc-258504-shipping-patch2-all-CL-258504`.
* Embedded engine version: `Unreal Engine 5.5.4`.
* SHA-256: `cb9b7d7bd88a6754c0a9c08318aa64d5013ddfd92d5badcae84e1b4ea980dcfc`.
* Native Unreal reflection and IOStore content are present; no pre-existing
  UE4SS installation was found in this installation.

The read-only object snapshot contained 418,183 non-null objects. The inspected
object-array header reported 433,055 allocated slots. Native class metadata,
function parameter metadata and a possessed `BP_PlayerCharacter_C` instance all
corroborated the bindings below. Full generated snapshots are local artifacts
under `artifacts/dawnwalker/`; they are not part of the trainer distribution.

## Implemented resource top-ups

The native player class is `/Script/Dawnwalker.DawnwalkerPlayerCharacter`.
Its reflected `CombatComponent` property points to
`/Script/DogwoodCombat.PlayerCombatComponent`, derived from
`/Script/DogwoodCombat.CombatComponentBase`. The latter provides:

| Function | Reflected parameters | Use |
| --- | --- | --- |
| `GetCharacter` | Character return value | Verify the component belongs to the selected pawn |
| `IsAlive` | Boolean return value | Reject dead/non-gameplay characters |
| `GetHealthPercentage` | Float return value | Read health ratio |
| `GetStaminaPercentage` | Float return value | Read stamina ratio |
| `SetHealthPercent` | Float `InPercent` | Replenish health |
| `SetStaminaPercent` | Float `InPercent` | Replenish stamina |

The native health setter was inspected in disassembly: it computes maximum
health multiplied by `InPercent`, confirming that `1.0` means full health.
The live player's reflected `CharacterAttributeSet` supplied internally
consistent values: health approximately 518.95 of 550 and stamina 250 of 250.
These are read-only corroboration, not a gameplay validation of setter calls.

The player also has a reflected `BloodBar` property pointing to
`/Script/DogwoodStats.BloodBarComponent`. It provides `GetBlood()` returning a
float, `GetBloodBarLength()` returning total segment capacity, and
`SetBloodPercent(float InBloodPercent)`. The current player's property
was verified to resolve to a live instance of this exact class. Blood is exposed
as an additional independent top-up. Native disassembly confirms that the blood
setter also reduces permanent segment damage to fit the requested fill: full
blood restores that damage. The one-shot refill action affects health and
stamina only. All setters require a successful getter readback before the
trainer acknowledges success, and resources already full are left alone.

All calls are scheduled by the runtime on the game thread. Before a write, the
bindings verify local control, authority, the controller's current pawn, both
the pawn's and component's `IsAlive`, positive health, component ownership, and
world/pawn/component identity. A world, controller, pawn, combat component or
blood-bar change clears enabled toggles. Features start disabled. Disconnecting
or disabling stops further top-ups; it does not lower replenished resources.

These features are **periodic top-ups, not invincibility**. A lethal hit between
updates can still kill the player, and gameplay spending may occur between
top-ups. No claim of gameplay validation is made for this build.

### Why the native Lock functions are not used

Reflection revealed `LockHealth`, `UnlockHealth`, `LockStamina`, `UnlockStamina`,
`LockBlood` and `UnlockBlood`. Disassembly showed that `LockHealth` first removes
the existing health lock, then replaces its gameplay-effect handle; stamina
follows the same pattern. The health/stamina handles are internal, unreflected
members. No reflected lock-state getter or ownership token was found.

Calling these functions and later unlocking would risk removing a quest or
cutscene's lock. The trainer instead uses percent setters and never calls
`Lock*` or `Unlock*`. The inspected health setter itself preserves whether the
game had a lock, removes it temporarily, sets the resource, and reinstates a
lock at the new value. The trainer creates no long-lived lock requiring cleanup.

## Additional discoveries

* `BloodBarComponent` also exposes `HealAndReplenishAllSegments`,
  `RestoreAmount`, `RestorePercentage` and getters for permanent blood damage.
  These broader restoration functions are not invoked by this module.
* Gameplay attributes include health, stamina, permanent stamina damage,
  exhaustion, weapon damage, experience/progression-related sets, movement,
  blood regeneration, and multiple combat cost/damage multipliers. Discovery of
  a property does not establish safe writable semantics. Trainer 0.2.1 adds
  the specific native routes documented below; other discovered properties
  remain unexposed.
* `/Script/DogwoodMap.MappinSystemBlueprintLibrary.DebugTeleportPlayer` exists
  with `MappinSystemImpl`, a double-precision `Vector2D Location`, and an output
  result enum. The map library also exposes user-map-pin and tracked-map-pin
  functions, location getters, and fast-travel destination lookup. Further
  native inspection found that the shipped teleport implementation immediately
  returns without performing teleportation. Its result-output wrapper alone is
  not evidence of working teleport support. Map teleport is unavailable;
  another route would require coordinate, collision/streaming and runtime
  validation.
* `DebugUnlockAllFastTravelDestinations` exists. It can alter persistent world
  progression and is not used.

## Native additions in trainer 0.2.1

- Invulnerability: `/Script/DogwoodAI.DogwoodAIGameplayEffect_Invulnerability`
  has infinite duration and grants `Effect.Invulnerability`. Runtime checks
  require non-stacking configuration. The trainer records its own active
  effect handle and removes that handle only; uncertain application/removal
  blocks retries instead of altering unidentified effects.
- Super damage: `CombatBlueprintFunctionLibrary.SetAttributeValue` (wrapper
  RVA `0x2359380`, native `0x15FEB3C`) updates the ability system. Descriptors
  come from `AbilitySystemComponent.GetAllAttributes`; verified debug names
  identify the four `CharacterBaseAttributeSet` damage multipliers. The game
  uses `{0}.{1}` formatting. Base getters and exact written-value snapshots
  support reversible 10× changes without writing GameplayAttributeData memory.
- Carry weight: `InventoryComponent.WeightLimit` zero is the native unlimited
  sentinel in pickup-fit, prospective-weight and overweight checks. The
  original reflected value is restored only while the override is still owned.
- Currency: Coin enum 0, `GetCurrencyAmount` and `AddCurrency` maintain a
  999,999 floor through bounded positive additions.
- Item use: ordinary `ItemConsumableDataAsset.IsSingleUse` is checked by native
  `UseItem` after applying the effect and before removing one item. Only
  currently carried non-quest assets are changed, with dynamic `IsQuestItem`
  checks and restoration; generic `RemoveItem` is not hooked.
- XP: the guarded `GetCurrentLevelXPRequirement(level)` reads the actual native
  level cache, returning zero on missing entries. Positive current-next and
  following thresholds, enabled progression, local ASC ownership and exact
  next-level identity are rechecked immediately before `ForceLevelUp`. It grants
  only the remaining XP to the next level and requires exact level+1 / XP0
  readback. No quest-reward-cache function is called. The former `AddQuestXP`
  route was withdrawn because loaded DataTable rows alone did not establish
  internal cache readiness; see the 0.3.14 release notes.
- Flight: `CharacterMovementComponent.SetMovementMode(5,0)` uses normal flying
  movement. The original mode/custom mode is retained. Cached native FKeys
  feed `AddMovementInput` only on the game thread while flight is enabled.

All additions were checked through static executable/reflection evidence and
isolated Lua boundary tests. Actual native calls and gameplay effects were not
tested in a live game for this release.

## Inspection tooling

`tools/InspectDawnwalker` contains read-only Python inspection helpers. They
require Python 3 and `pefile`; native disassembly investigation used `capstone`.
The live helper's initial RVAs were derived from the exact build above. They
are investigation data, not production trainer memory offsets. The production
Lua module uses reflected property/function names and does not write raw
addresses. A different build must be inspected anew if reflection changes.
