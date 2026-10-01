# Civilization VII implementation research

Research date: **1 October 2026**. This records source evidence and implementation limits, not gameplay validation.

## Installed build and transport

The installed Windows Steam game is app **1295660**, build **25245002**. Its Steam manifest also names target build **25516395** and a pending download; therefore compatibility with that target build has not been established. The inspected game root was `E:/Games/SteamLibrary/steamapps/common/Sid Meier's Civilization VII`. Supported executable names are `Base/Binaries/Win64/Civ7_Win64_DX12_FinalRelease.exe` and `Civ7_Win64_Vulkan_FinalRelease.exe`.

The installed official Firaxis files contain JavaScript FireTuner panels in `Base/Platforms/Windows/Config/TunerPanels/`. Their compatible state is **App UI**, rather than Civilization VI's Lua GameCore. HappyCheats uses its own JavaScript runtime and the existing game's local debug channel. The independent [FiretunerTerminal implementation](https://github.com/ghost-ng/FiretunerTerminal) documents TCP port 4318, little-endian framed messages and the type-3 `CMD:65535:` JavaScript command envelope. This is transport evidence; HappyCheats does not depend on that terminal or run its code.

The existing local profile at `%LOCALAPPDATA%/Firaxis Games/Sid Meier's Civilization VII/AppOptions.txt` contains the commented default `;EnableTuner -1`; its accompanying comment describes `1` as enabled, `2` as disabled, and `-1` as default. The separate `AchievementsRestrictedByTuner` default is `1`. Setup must own and restore only its Tuner-setting change, including the original comment/default syntax. There is no need to change the achievement restriction or enable the separate UI debugger. No user's game settings, saves or installed game files were changed during this research, and the game was not started.

## Source-backed initial controls

Paths below are relative to the inspected game root. They are primary sources shipped by Firaxis; the runtime implements the calls itself and does not redistribute those files.

| Control | Native API and evidence |
| --- | --- |
| Set gold | `player.Treasury.changeGoldBalance(delta, -1)` in `TunerPanels/Player.ltp:150`; `goldBalance` in the shipped yield banner. |
| Set influence | `player.DiplomacyTreasury.changeDiplomacyBalance(delta)` in `Player.ltp:169`; `diplomacyBalance` in the yield banner. This is Civ VII influence, not Civ VI diplomatic favor or envoys. |
| Unlimited movement / restore selected movement | `Units.restoreMovement(componentId)` in `Units.ltp:31,114`. `unit.Movement.movementMovesRemaining` and `maxMoves` in `Base/modules/base-standard/ui/commander-interact/model-commander-interact.js:336`. This restores movement only; it does not reset attacks. |
| Set selected unit damage | `Units.setDamage(unit.id, amount)` in `Units.ltp:63`; getters `unit.Health.damage`, `maxDamage`. The trainer prevents a lethal value and restricts edits to living, owned units. |
| Add unit or commander XP | `Units.changeExperience(unit.id, amount)` in `Units.ltp:130,229`; `unit.Experience.experiencePoints` in `Base/modules/base-standard/ui/unit-promotion/model-unit-promotion.js:149`. Ordinary units without an experience component cannot use it. |
| Fast units, buildings/wonders, projects | `city.BuildQueue.completeProduction()` in `Cities.ltp:54`; `getQueue()` in `Cities.ltp:923`. `Base/modules/base-standard/ui/build-queue/model-build-queue.js:91,108,126` categorizes `ORDER_CONSTRUCT`, `ORDER_TRAIN` / `ORDER_FOOD_TRAIN`, and `ORDER_ADVANCE`. Fast units also inspects `FoodQueue`, filtering unit orders only. |
| Fast current technology / civic | `player.Techs.getResearching()` / `player.Culture.getResearching()` in `Player.ltp:590,623`. The selected node is granted through `Game.PlayerOperations.sendRequest(localPlayerID, PlayerOperationTypes.GRANT_TREE_NODE, {ProgressionTreeNodeType: node.type, FullyUnlock: 0})`, as shown at `Player.ltp:55-56,99-100`. This grants one depth, not every node in the tree. |
| Add rural population | `city.addRuralPopulation(amount)` in `Cities.ltp:104`; `city.population` in the shipped city-details model. This adds rural population, rather than claiming to set arbitrary total or specialist population. |
| Complete food queue | `city.FoodQueue.completeProduction()` in `Cities.ltp:88`. The official panel calls this **Complete Build (Food)**. It may finish a food-funded item; it is not a guaranteed instant-population-growth API. |
| Add wildcard attributes | `player.Identity.addWildcardAttributePoints(amount)` in `Player.ltp:190`; `getWildcardPoints()` in `Base/modules/base-standard/ui/attribute-trees/model-attribute-trees.js:88`. |
| Start celebration | `player.Happiness.startGoldenAge(player.id)` in `Player.ltp:287`; `getGoldenAgeTurnsLeft()` in `Base/modules/base-standard/ui/sub-system-dock/panel-sub-system-dock.js:564`. Uses the native Golden Age API; availability and behavior depend on the current ruleset and Age. |

`TunerPanels/` above abbreviates `Base/Platforms/Windows/Config/TunerPanels/`.

## Runtime behavior and validation limits

The bridge requires a loaded, verified single-player session, a living human local player and their active turn. `Configuration.getGame().isNetworkMultiplayer` and `.isHotseat` come from the shipped pause/load-screen models. `UI.isMultiplayer()` and `UI.isInGame()` are used throughout the official UI. `panel-action.js:506` additionally checks `GameContext.hasSentTurnComplete()`; HappyCheats follows that pending-end-turn guard. Selected objects must have both a component ID and object ownership matching the local player, even when they came from a nominally owned collection.

Runtime reload or Init clears all toggles. Detected local-player changes, Age changes, game-seed changes, backward turn changes and loss of a loaded single-player game also clear them. `Configuration.getGame().gameSeed` is displayed by the official pause-menu model. Waiting between normal turns pauses writes. Every command compares its prepared player, Age and relevant unit/settlement selection against the current context immediately before writing. There are no persistent timers or event handlers: further effects require the client to poll. Replacing a save with another branch using the same seed, player, Age and turn without an observable unload cannot be independently identified from these fields; reconnect before switching saves.

Gold, influence, damage, XP, population, movement and attribute edits read the result back; unconfirmed results, engine caps or a native exception report a failure with no automatic retry. XP can trigger engine promotion rules, so an exact additive readback is not assumed to succeed universally. Research and queue completion acknowledge native dispatch, not confirmed asynchronous completion. An unchanged research node/depth or queue head is not dispatched repeatedly within a turn. Consequently, identical repeat production may finish once per turn rather than once per client poll.

Command IDs are remembered for the whole runtime session, including failures after possible mutation. After 4096 distinct commands, new mutations require reconnecting; disabling toggles remains available. Offline fixtures exercise ownership, multiplayer rejection, missing capabilities, range/type validation, turn/Age transitions, command replay, setter failures, queue categories and readbacks. Passing them does not establish that the current retail binary exposes every binding or accepts every request. Actual gameplay, physical hotkeys, achievements and second-PC connectivity remain untested.

## Further options explored

These are feasible candidates with concrete source evidence, **not shipped controls**:

| Candidate | Evidence and remaining work |
| --- | --- |
| Add science, culture or happiness | `Players.grantYield(player.id, YieldTypes.YIELD_SCIENCE / YIELD_CULTURE / YIELD_HAPPINESS, amount)` in `Player.ltp:226,245,264`. Good next additions after verifying how overflow, selected research, celebrations and Age transitions consume the grants. Happiness yield must not be mislabeled as a city amenities or happiness-bonus setter. |
| Extend or restore trade distance | `Players.increaseMaxTradeDistance(amount, player.id)` and `Players.restoreMaxTradeDistance(player.id)` in `Player.ltp:353,372`. Needs a readback, defined restoration ownership and confirmation that restoring does not remove another mod's effect. |
| Repair selected owned buildings / improvements | `Constructibles.setDamaged(constructible.id, false)` in `Cities.ltp:120,135`. Needs a reliable selected constructible, city ownership checks and Age-specific testing. |
| Controlled map reveal | `Visibility.changeVisibilityCountForPlots(playerId, 1, plotIndices)` in `Player.ltp:392`. Needs balanced ownership of visibility increments and a clear policy on permanent knowledge; blind repeated calls leak counters. |
| Grant chosen technologies or civics, including mastery | `GRANT_TREE_NODE` with the chosen database node and `FullyUnlock:1` is in the official panel. A safe picker must scope the active Age and distinguish a mastery depth from a different technology. The current release only follows the player's selected research. |
| Unlock traditions; add policy / tradition slots | `player.Culture.unlockTradition(type)` in `Player.ltp:481`; `Players.grantCultureSlot(player.id, CultureSlotTypes.POLICY_CULTURE_SLOT / TRADITION_CULTURE_SLOT, amount)` in `Player.ltp:503,523`. The shipped `ui/policies/model-policies.js` reads `getNumCultureSlots(slotType)`. Strong candidates after confirming slot caps, current-Age availability and unlock behavior; granting a slot and assigning a tradition are separate operations. |
| Spawn selected owned unit / settlement / constructible | `Game.PlayerOperations.sendRequest(..., "CREATE_ELEMENT", args)` in `Base/modules/base-standard/ui/tuner-input/tuner-input.js`. Needs validated type/position/owner pickers, terrain and settlement checks, acknowledgement, and playtesting. |
| Unit activity commands | `Units.setActivity(unit.id, UnitActivityTypes.AWAKE / SLEEP / HEAL / SENTRY)` in `Units.ltp`. Useful convenience actions; they do not grant healing or an attack reset. |
| Great works, legacy triggers, map editing | `Players.grantGreatWork()` in `Player.ltp`, `player.Legacies.setForceTriggered()` in `Legacies.ltp`, and `WorldBuilder.MapPlots` setters in `tuner-input.js`. These have larger persistent consequences and need dedicated selection/rules validation rather than generic numeric editors. |

There is no evidence in the inspected panels for a raw remaining-movement setter, a raw unit-level setter, arbitrary promotion flags, or Civ VI-style faith, favor, housing and amenities-bonus editors. They are not invented or substituted with another resource. Official source calls establish a reasonable implementation candidate; gameplay testing is still necessary before claiming reliability.

Suggested order for follow-up work: first yield grants and policy/tradition slots, because their selection and readback requirements are small; next selected-building repair and carefully balanced map visibility; then unit, settlement and constructible creation with explicit type/location pickers. Legacy triggers, broad map changes and great-work injection need their own ruleset-specific validation.
