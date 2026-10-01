# Civilization VI trainer bridge

HappyCheats connects to Civilization VI's local FireTuner console and loads
`Scripts/runtime.lua` into `GameCore_Tuner`. There is no gameplay mod or persistent
event handler. Continuous effects run only when HappyCheats polls this game page.
Closing the page stops further enforcement; already completed production,
research, promotions, and edited totals are ordinary game-state changes and are
not undone.

The bridge is experimental: its real Lua source passes offline execution checks,
but it has not been verified in live gameplay. The local Civ VI Steam folder was
an empty installation remnant during development, so no game was launched, save
changed, or live Tuner command sent. The installed client's setup action enables
Tuner while the game is closed; Tuner disables achievements. Only the verified
local single-player context is accepted. Explicit AI, dead-player, inactive-turn,
multiplayer, and conflicting-player checks still reject edits when exposed.

Version 0.1.1 removes redundant blanket checks that greyed every control on builds
whose GameCore context omits player metadata. Missing `IsHuman`, `IsAlive`, or
`IsTurnActive` is no longer treated as a negative result. A freshly verified local
player from the `InGame` UI can supply identity and single-player evidence when
the corresponding GameCore methods are absent or return no player. Individual
controls still require their own getters and setters; a missing selected unit
does not disable gold, faith, research, or city production.

## Implemented controls

- Unlimited movement: restores movement of local-owned units, including formations.
  It does not replenish attacks or recreate consumed units.
- Fast units, buildings/wonders, districts, and projects: completes the current
  matching production item in local-owned cities on each enforcement pass.
  Repeated projects can complete repeatedly while enabled.
- Fast research and civics: completes the currently selected technology or civic.
- Selected unit damage, remaining movement, promotion availability, and XP.
  Damage is bounded below a lethal value. Promotion availability set to one grants
  the XP needed for the next promotion if necessary. Zero attempts to remove one
  stored promotion and reports failure if the unit can still promote. It does not
  repeatedly decrement an unknown count or select a promotion for the player.
- Player gold, faith, and diplomatic favor as exact requested totals. Favor needs
  a ruleset that exposes `PlayerDiplomacy:GetFavor` and `ChangeFavor`. Each numeric
  edit reads back the actual value; a cap or ignored edit is reported as failure.
  In particular, the game may cap or discard XP while a promotion is pending.
- Selected local-owned city population.

Four requested raw editors remain explicitly unavailable: unit level, influence
points, amenities bonus, and housing bonus. The stock Lua interface exposes no
verified raw setters for these fields. Envoy tokens are not influence points;
total housing/amenities are not their internal bonus fields. Unsupported controls
return an explanation and never pretend to apply changes.

## Interface

Every public call returns a compact JSON string (print it through FireTuner).

```lua
HappyCheatsCiv6.Init(sessionId)
HappyCheatsCiv6.Status(localPlayerId, selectedUnitId, selectedCityId, verifiedSinglePlayer)
HappyCheatsCiv6.Command(sessionId, requestId, featureId, value,
    localPlayerId, selectedUnitId, selectedCityId, verifiedSinglePlayer)
HappyCheatsCiv6.Tick(sessionId, HappyCheatsCiv6.Enabled,
    localPlayerId, selectedUnitId, selectedCityId, verifiedSinglePlayer)
```

Use `-1` for absent selections. Obtain local player and selected owner/ID through
the `InGame` UI context. Pass the optional final argument as boolean `true` only
after that same UI query verifies a nonnegative local player and single-player
mode. This evidence permits missing GameCore identity/mode metadata; explicit
GameCore multiplayer, hotseat, network mode, AI/dead/inactive status, or a different
nonnegative local player always takes precedence. Objects are resolved through
that player's own collections and rejected when their owner metadata disagrees.
Absent redundant owner metadata does not disable objects from those collections.
`Tick` never activates a toggle; activation requires a successful `Command`.
`disable_all` and individual off commands are available between turns.

Responses contain `schema_version: 1`, `version: "0.1.1"`, `ready`, `session_id`,
`enabled`, `values`, `message`, `last_command`, `command_success`, and
`command_error`. Values contain `available_<feature>` strings (`true`/`false`) and
`reason_<feature>` explanations. Numeric values are strings. Request identifiers
are deduplicated, including requests whose setter throws, because a throwing
native setter might already have changed state. The client must not automatically
retry an ambiguous command after reconnection.

## API evidence

The bridge implements original dispatch/validation code; external runtime code
is not bundled. The API names and context boundaries were checked against:

- [Firaxis Tuner panel mirror: Unit.ltp](https://github.com/Nanookao/Civ6-FireTuner-debug/blob/main/Civ6/Debug/Unit.ltp)
  demonstrates `UnitManager.RestoreMovementToFormation`, `SetDamage`,
  `GetExperience():ChangeExperience`.
- [Firaxis Tuner panel mirror: Player.ltp](https://github.com/Nanookao/Civ6-FireTuner-debug/blob/main/Civ6/Debug/Player.ltp)
  demonstrates gold/faith mutation, research completion, civic selection, and
  `GetDiplomacy():GetFavor` / `ChangeFavor` with expansion capability checks.
- [TunerCityPanel.lua mirror](https://github.com/Nanookao/Civ6-FireTuner-debug/blob/main/Civ6/Assets-UI-Tuner/TunerCityPanel.lua)
  resolves `CurrentlyBuilding()` through `GameInfo.Buildings`, `Districts`,
  `Units`, and `Projects`. The production bridge uses that database type and does
  not assume it is a queue index.
- [Sukritact's API corpus](https://github.com/Sukritact/Civilization-VI-Modding-Knowledge-Base/tree/master/Objects)
  records gameplay availability for `Game.GetLocalPlayer`,
  `GameConfiguration.IsAnyMultiplayer`, `Player:IsTurnActive`,
  `UnitExperience:CanPromote` / `ChangeStoredPromotions`,
  `PlayerCulture:SetCulturalProgress`, and `CityBuildQueue:FinishProgress`.
  It also distinguishes UI-only getters from gameplay setters.
- [Hemmelfort's Lua examples](https://github.com/Hemmelfort/Civ6ModdingNotes/blob/master/%E6%96%87%E6%98%8E6_Lua%E6%89%8B%E5%86%8C.md)
  demonstrate `UnitManager.ChangeMovesRemaining(unit, delta)`,
  `FinishProgress()`, and population/experience mutations.
- [LeeS' scenario-derived promotion example and tested mod](https://forums.civfanatics.com/threads/free-xp-or-promotion-level-from-barracks-possible.642691/)
  documents granting next-level XP before a unit can promote, stored promotions
  after promotion becomes available, and the engine's XP limitations.

These sources establish intended API use, not live compatibility of every game
build or ruleset. Missing capabilities return unavailable instead of trying
undocumented native memory writes.

## Offline checks

Run `scripts/Test-CivilizationVI.ps1` or invoke `tests/run.py` with the repository's
bundled Python runtime and existing `lupa==2.6` dependency. The suite executes the
actual payload with isolated game API fixtures and tests ownership, turn and
multiplayer guards, exact values, category routing, negative/fractional/bounded
inputs, replay protection, partial native failures, stale sessions, unsupported
fields, missing DLC APIs, no implicit toggle activation, and stop behavior.
Compatibility cases additionally cover absent/nil/failing optional player flags,
verified UI fallback, explicit contradictory identities/modes, missing redundant
owner metadata, and per-feature readiness when a single setter is unavailable.
