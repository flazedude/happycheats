# Hotel Architect: inspected bindings

These bindings were inspected **offline** from Steam app **1602000**, build
**25340122**, on 27 September 2026. No game was launched, no game process was
opened, and no installation files or saves were changed during inspection.
This establishes native names, types, offsets and code paths. It does not
establish gameplay or Blueprint event behavior.

The shipping executable is
`HotelArchitect/Binaries/Win64/HotelArchitect-Win64-Shipping.exe`, Unreal Engine
**5.7, CL-51494982**, x64. Its SHA-256 is
`e0b6e24e2d0d3ca466b9dd08ed83bc989594b6b17c31cdc3b63f543355621ae8`.
File size is **164258816**; PE timestamp is **2252431335**; SizeOfImage is
**168026112**. Preferred image base is `0x140000000`; all production static
addresses must instead be relocated from the actual module base.

Run `py tools/InspectHotelArchitect/inspect_game.py --verify --fingerprint` to
reproduce the static contract checks and reports in
`artifacts/hotel-architect`. The report includes native byte sequences for the
unreflected fields. Verification is pinned to this executable and rejects a
different hash.

## Runtime owners and selection

All native classes below belong to `/Script/HotelArchitect`.
`HaStandardGameMode.HaGameState` is a reflected object property (offset `0x410`)
that resolves the current `HaGameState`. Object discovery must exclude class
defaults, stale objects, and objects from other worlds.

`HaPlayerController.SelectedCharacter` (offset `0x764`) and
`SelectedGridItem` (offset `0x748`) are **weak object properties**. Resolve the
weak pointer before reading the target, then revalidate the target and world.
An absent selection must make individual editors unavailable; do not choose an
arbitrary entity from an object search.

Character statistics belong to `HaBaseCharacter`. Staff-only statistics belong
to its `HaStaffCharacter` subclass. Furniture statistics belong to
`HaGridItem`. A global maintenance toggle may enumerate valid entities in the
active world; individual edits must use the selected entity.

## Requested controls

Ranges below describe the native representation. Percentage controls can
present `0..100` in the client and convert to/from `0..1` in the runtime.

| Screenshot control | Verified binding and behavior |
| --- | --- |
| No Needs | Maintain living `HaBaseCharacter.RestroomNeed=0`, `Mood=1`, `Health=1`; maintain `HaStaffCharacter.Energy=1`. Native health updates skip characters whose health is already zero; the trainer must also exclude those entities. |
| Always Noon | Hold the simulation clock at 12:00 using the clock fields described below. This affects simulation time, rather than providing a separately verified lighting-only override. |
| Unlimited Furniture Condition and Cleanliness | Maintain active-world `HaGridItem.Condition=1` and `Cleanliness=1`. This is not an independently verified repair/replacement operation. |
| Game Speed | `HaStandardGameMode:SetGameSpeed(EHaGameSpeed)`: 0 Paused, 1 Normal, 2 Faster, 3 Fastest. Use the native method to update dilation and notification state. |
| Days Passed | `HaGameState.FinancialData.DaysPassed`, int32 at `0x30` within the financial struct, itself at game-state offset `0x318`. This is financial-history days; it is distinct from `DayNumber`. |
| Disable Aging | Negate the active native difficulty setting `bEnableAging`; see the guarded one-byte backend below. |
| Money | `HaGameState.Money`, int64, `0x4a8`. Call `ChangeMoneyAndAdjustNetWorth(delta:int64)` on the game-state instance to preserve its normal accounting/notification path. |
| Stars | `HaGameState.Stars`, int32, `0x4d0`. `HaSandboxFunctionLibrary:ChangeNumberOfStars(WorldContextObject, delta:int32)` calls the native setter, which clamps to 0..5 and raises the HUD notification. |
| Day Number | `HaGameState.DayNumber`, int64, `0x480`. |
| Hour | `HaGameState.Hour`, int32, `0x488`, 0..23. It is derived from the native seconds accumulator; coordinate clock fields when editing. |
| Minute | `HaGameState.Minute`, int32, `0x48c`, 0..59; coordinate clock fields when editing. |
| Game Hours Time Total | `HaGameState:GetTotalGameTimeInHours()` returns a double computed as `DayNumber * 24 + GameHoursTime`. There is no independently reflected `GameHoursTimeTotal` field in this build. An edit is implemented through the calendar/time fields. |
| Restroom Need | Selected `HaBaseCharacter.RestroomNeed`, float, `0x5ec`, 0..1; zero means no need. `HaCharacterFunctions:ChangeRestroomNeed(Character, delta:float)` clamps to that range. |
| Mood | Selected `HaBaseCharacter.Mood`, float, `0x5f0`, 0..1. The native `Ha.SetStaffMood` path clamps to that range. Guest mood can subsequently be recalculated by the game's activity system. |
| Health | Selected living `HaBaseCharacter.Health`, float, `0x5f4`, 0..1. `HaCharacterFunctions:ChangeCharacterHealth(Character, delta:float)` clamps and deliberately ignores characters already at zero health. |
| Current XP | Selected staff's unreflected int32 at `0x768`; validated by `GetLevelAndExperience`. Native progression requires `110 * currentLevel` XP for the next level and sets XP to zero at level 10. Exact editing should maintain those invariants. |
| Level | Selected staff's unreflected int32 at `0x76c`; validated by `GetLevelAndExperience`. Normal level range is 1..10. Raw editing does not itself reproduce every side effect of natural advancement. |
| Days Until Retirement | Selected `HaStaffCharacter.DaysUntilRetirement`, int32, `0x770`. |
| Energy | Selected `HaStaffCharacter.Energy`, float, `0x7a4`, 0..1. `ChangeEnergyLevel` applies character modifiers; a direct property edit is appropriate for an exact requested value. |
| Room Value Multiplier | Selected `HaGridItem.RoomValueMultiplier`, float, `0x3d4`. |
| Cleanliness | Selected `HaGridItem.Cleanliness`, float, `0x4c4`, 0..1. `ChangeCleanliness(delta:float)` performs the normal native notifications and clamps the value. |
| Attractiveness Value | Selected `HaGridItem.AttractivenessValue`, int32, `0x4d4`. |
| Negative Attractiveness Value | Selected `HaGridItem.NegativeAttractivenessValue`, int32, `0x4e0`. The type is signed; no narrower engine bound was established. |
| Condition | Selected `HaGridItem.Condition`, float, `0x500`; 1 is full condition. `OnConditionChanged()` is a zero-argument Blueprint event. |

`ChangeCondition(delta, Staff)` is **not** a generic one-argument condition
setter: its native implementation returns immediately for an invalid staff
argument. The zero-argument `OnConditionChanged()` event can notify a property
edit, but its Blueprint implementation has not been inspected. Existing
`bNeedsRepair` / `bNeedsReplacement` states are distinct from condition; this
inspection does not establish that changing condition clears them.

## Clock consistency

The native setter at RVA `0x4a143e0` establishes these `HaGameState` fields:

| Offset | Representation |
| --- | --- |
| `0x494` | float: seconds accumulated toward hourly gameplay events |
| `0x498` | float: time-of-day seconds |
| `0x49c` | reflected `GameHoursTime`, float: time-of-day hours |
| `0x4a0` | float: previous time-of-day hours |

The hours-to-seconds constant at RVA `0x8100e98` is **3600**. Native tick at
RVA `0x4a14410` advances the seconds fields and recomputes Hour, Minute and
GameHoursTime. Merely changing those three reflected display fields would be
overwritten on the next tick.

The native setter writes both seconds accumulators; repeated noon maintenance
must **not** reset the hourly-event accumulator to 43200 every poll, because
that would repeatedly process twelve hourly events. Preserve the hourly-event
accumulator while coordinating time-of-day seconds, current/previous hours,
Hour and Minute. The total-hours getter at RVA `0x4a12310` confirms
`DayNumber * 24 + GameHoursTime`; deliberate calendar/clock editing changes that
reported total as well. Time-dependent gameplay remains an actual-play check.

## Guarded native aging setting

`HaDifficultySettingsFunctions:GetDifficultySettings(WorldContextObject)`
returns a copy of a 64-byte native static struct. Modifying that returned Lua
struct cannot be assumed to modify the active settings.

The native getter at RVA `0x4a11b00`, bytes `48 8d 05 19 24 91 04 c3`, returns
the active struct at RVA **`0x9323f20`**. The generated bool setter for
`bEnableAging` at RVA `0x4789490`, bytes `c6 41 01 01 c3`, establishes offset
**1**. Consequently the live aging flag is the single byte at
**module base + `0x9323f21`**, in the image's writable `.data` section.

The real daily aging gate at RVA `0x4a13165` reads that same byte and branches
over retirement updates when it is zero. The subsequent code at RVA
`0x4a1324d` decrements staff offset `0x770` and retires staff whose remaining
days reach zero. These code sequences are included in the fingerprint report.

The client backend must verify the full on-disk executable hash, PE identity,
loaded-module identity, and code sentinels before touching this byte. Accept
only original values 0 or 1. Capture the original value when enabling the
override, write zero, and restore only for the same process and world while
the current value still equals the trainer's override. A world/session change
must discard stale restoration ownership rather than replaying old difficulty
settings into the newly loaded world. Do not edit neighboring difficulty flags.

## Validation limits

The reproducible static contract passes for the installed build, including
twenty property descriptors and five native code sentinels. Lua/mock and
packaging checks can establish trainer logic and distribution integrity.
Actual UE4SS attachment, Blueprint notification side effects, guest/staff AI,
hotel accounting over time, and second-PC LAN reachability require their own
tests and must not be described as verified by this inspection.
