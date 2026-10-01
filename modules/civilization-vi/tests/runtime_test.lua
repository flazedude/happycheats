-- The real bridge is executed with deterministic stand-ins for the engine API.
-- Tests prove bridge dispatch/guards, not compatibility with an installed game.
local count = 0
local function eq(actual, expected, label)
    count = count + 1
    assert(actual == expected, (label or "Mismatch") .. ": " .. tostring(actual) .. " ~= " .. tostring(expected))
end
local function contains(actual, wanted)
    count = count + 1
    assert(actual:find(wanted, 1, true), tostring(actual) .. " does not contain " .. wanted)
end
local stats = {gold = 100, faith = 200, favor = 30, xp = 10, promotions = 0, goldWrites = 0, moveWrites = 0,
    unitMoves = 2, enemyMoves = 1, damage = 12, population = 5, research = 7, civic = 8, researchFinishes = 0,
    civicFinishes = 0, active = true, alive = true, human = true, multiplayer = false, localId = 0,
    xpThreshold = 300, promotionWrites = 0}
local treasury = {GetGoldBalance = function() return stats.gold end, ChangeGoldBalance = function(_, delta)
    stats.goldWrites = stats.goldWrites + 1; stats.gold = stats.gold + delta
    if stats.throwGold then error("native setter threw after mutation") end
end}
local religion = {GetFaithBalance = function() return stats.faith end, ChangeFaithBalance = function(_, delta) stats.faith = stats.faith + delta end}
local diplomacy = {GetFavor = function() return stats.favor end, ChangeFavor = function(_, delta) stats.favor = stats.favor + delta end}
local experience = {GetExperiencePoints = function() return stats.xp end,
    ChangeExperience = function(_, delta)
        -- XP may cap at the next promotion and positive XP can be ignored while
        -- promotion is pending. These constraints must not falsely acknowledge.
        if delta > 0 and (stats.promotions > 0 or stats.xp >= stats.xpThreshold) then return end
        stats.xp = math.min(stats.xpThreshold, stats.xp + delta)
    end,
    GetExperienceForNextLevel = function() return stats.xpThreshold - stats.xp end,
    CanPromote = function() return stats.promotions > 0 or stats.xp >= stats.xpThreshold end,
    ChangeStoredPromotions = function(_, delta)
        stats.promotionWrites = stats.promotionWrites + 1
        if stats.promotions > 0 or stats.xp >= stats.xpThreshold then stats.promotions = math.max(0, stats.promotions + delta) end
    end}
local unit = {GetOwner = function() return 0 end, GetID = function() return 11 end,
    GetExperience = function() return experience end, GetMovesRemaining = function() return stats.unitMoves end,
    GetDamage = function() return stats.damage end, SetDamage = function(_, value) stats.damage = value end,
    GetMaxDamage = function() return 100 end}
local enemy = {GetOwner = function() return 1 end, GetID = function() return 12 end,
    GetMovesRemaining = function() return stats.enemyMoves end, GetExperience = function() return experience end}
local function collection(items)
    return {FindID = function(_, id) return items[id] end, Members = function() return pairs(items) end}
end
local queues, cities = {}, {}
for i, kind in ipairs({"UNIT_WARRIOR", "BUILDING_PYRAMIDS", "DISTRICT_CAMPUS", "PROJECT_RESEARCH"}) do
    local queue = {kind = kind, finished = 0}
    function queue:CurrentlyBuilding() return self.kind end
    function queue:FinishProgress() self.finished = self.finished + 1 end
    queues[i] = queue
    cities[i] = {GetOwner = function() return 0 end, GetID = function() return i end,
        GetBuildQueue = function() return queue end, GetPopulation = function() return stats.population end,
        ChangePopulation = function(_, delta) stats.population = stats.population + delta end}
end
local foreignQueue = {kind = "UNIT_WARRIOR", finished = 0, CurrentlyBuilding = queues[1].CurrentlyBuilding, FinishProgress = queues[1].FinishProgress}
cities[5] = {GetOwner = function() return 1 end, GetID = function() return 5 end, GetBuildQueue = function() return foreignQueue end}
local techs = {GetResearchingTech = function() return stats.research end, GetResearchCost = function(_, id) eq(id, 7); return 120 end,
    SetResearchProgress = function(_, id, cost) eq(id, 7); eq(cost, 120); stats.researchFinishes = stats.researchFinishes + 1 end}
local culture = {GetProgressingCivic = function() return stats.civic end, GetCultureCost = function(_, id) eq(id, 8); return 150 end,
    SetCulturalProgress = function(_, id, cost) eq(id, 8); eq(cost, 150); stats.civicFinishes = stats.civicFinishes + 1 end}
local player = {IsHuman = function() return stats.human end, IsAlive = function() return stats.alive end,
    IsTurnActive = function() return stats.active end, GetUnits = function() return collection({[11] = unit, [12] = enemy}) end,
    GetCities = function() return collection(cities) end, GetTreasury = function() return treasury end,
    GetReligion = function() return religion end, GetDiplomacy = function() return diplomacy end,
    GetTechs = function() return techs end, GetCulture = function() return culture end,
    GetInfluence = function() return {GetPointsEarned = function() return 42 end} end}
Players = {[0] = player}
Game = {GetLocalPlayer = function() return stats.localId end, GetCurrentGameTurn = function() return 20 end}
GameConfiguration = {IsAnyMultiplayer = function() return stats.multiplayer end}
GameInfo = {Units = {UNIT_WARRIOR = {}}, Buildings = {BUILDING_PYRAMIDS = {}}, Districts = {DISTRICT_CAMPUS = {}}, Projects = {PROJECT_RESEARCH = {}}}
UnitManager = {RestoreMovementToFormation = function(u)
    assert(u == unit, "Movement touched a foreign unit"); stats.unitMoves = 4; stats.moveWrites = stats.moveWrites + 1
end, ChangeMovesRemaining = function(u, delta) assert(u == unit); stats.unitMoves = stats.unitMoves + delta end}
dofile("modules/civilization-vi/Scripts/runtime.lua")
local R = HappyCheatsCiv6
local session, requestNo = "session_12345678", 0
local function status(u, c) return decode(R.Status(0, u or 11, c or 1)) end
local function command(feature, value, u, c, token, verified)
    requestNo = requestNo + 1
    return decode(R.Command(session, token or ("request_" .. requestNo), feature, value, 0, u or 11, c or 1, verified))
end
local function success(feature, value, u, c)
    local s = command(feature, value, u, c); eq(s.command_success, true, s.command_error); return s
end
eq(status().ready, false, "Uninitialized guard")
R.Init(session)
eq(status().ready, true)
eq(status().schema_version, 1)
eq(status().version, "0.1.1")
eq(status().values.available_civ6_gold, "true")
eq(status().values.civ6_gold, "100")
eq(status().values.civ6_influence, "42", "Influence readout stays distinct from envoys")
local unselected = status(-1, -1)
eq(unselected.ready, true)
eq(unselected.values.available_civ6_gold, "true", "No unit selection does not disable player gold")
eq(unselected.values.available_civ6_research, "true", "No selection does not disable research")
eq(unselected.values.available_civ6_units, "true", "No city selection does not disable empire production")
eq(unselected.values.available_civ6_damage, "false", "Unit editor still needs selected unit")
eq(unselected.values.available_civ6_population, "false", "City editor still needs selected city")
for _, id in ipairs({"civ6_level", "civ6_influence", "civ6_amenities", "civ6_housing"}) do
    local s = status(); eq(s.values["available_" .. id], "false"); eq(command(id, 100).command_success, false)
end
success("civ6_gold", 500); eq(stats.gold, 500)
success("civ6_gold", 5); eq(stats.gold, 5, "Exact total may decrease")
success("civ6_faith", 50); eq(stats.faith, 50)
success("civ6_favor", 2); eq(stats.favor, 2)
success("civ6_population", 20); eq(stats.population, 20)
success("civ6_damage", 0); eq(stats.damage, 0)
success("civ6_moves", 12); eq(stats.unitMoves, 12)
success("civ6_xp", 100); eq(stats.xp, 100)
success("civ6_xp", 0); eq(stats.xp, 0)
success("civ6_can_level", 1); eq(stats.xp, 300); eq(stats.promotions, 0, "First promotion granted through XP")
success("civ6_can_level", 1); eq(stats.xp, 300, "Promotion set is idempotent")
eq(command("civ6_xp", 1000).command_success, false); eq(stats.xp, 300, "Ignored XP is not acknowledged")
local promotionWrites = stats.promotionWrites
eq(command("civ6_can_level", 0).command_success, false)
eq(stats.promotionWrites, promotionWrites + 1, "Only one removal attempt, never 100 guesses")
stats.xp, stats.promotions = 0, 3
eq(command("civ6_can_level", 0).command_success, false); eq(stats.promotions, 2)
stats.promotions = 1; success("civ6_can_level", 0); eq(stats.promotions, 0)
eq(command("civ6_xp", 1000).command_success, false); eq(stats.xp, 300, "Capped XP is not acknowledged")
stats.xp = 0
for _, bad in ipairs({-1, 0.5, 1000000001, math.huge}) do eq(command("civ6_gold", bad).command_success, false) end
eq(command("civ6_gold", 0/0).command_success, false)
eq(command("civ6_damage", 100).command_success, false); eq(stats.damage, 0)
eq(command("civ6_population", 0).command_success, false); eq(stats.population, 20)
eq(command("civ6_can_level", 2).command_success, false)
eq(command("civ6_xp", 50, -1).command_success, false)
eq(command("civ6_xp", 50, 12).command_success, false, "Foreign selection rejected")
eq(command("civ6_population", 50, 11, 5).command_success, false)
eq(status(12, 5).values.selected_unit_id, "-1")
eq(status(12, 5).values.selected_city_id, "-1")
local writes = stats.goldWrites
local first = command("civ6_gold", 1000, 11, 1, "replay_request")
local replay = command("civ6_gold", 1000, 11, 1, "replay_request")
eq(first.command_success, true); eq(replay.command_success, true); eq(stats.goldWrites, writes + 1, "Replay does not mutate twice")
eq(command("civ6_gold", 5000, 11, 1, "replay_request").command_success, false); eq(stats.gold, 1000)
stats.throwGold = true; writes = stats.goldWrites
eq(command("civ6_gold", 1001, 11, 1, "partial_failure").command_success, false)
stats.throwGold = false
eq(command("civ6_gold", 1001, 11, 1, "partial_failure").command_success, false)
eq(stats.goldWrites, writes + 1, "Even a partial native failure is never retried")
local stale = decode(R.Command("old_session", "stale_request", "civ6_gold", 300, 0, 11, 1))
eq(stale.command_success, false); eq(stats.gold, 1001)
eq(decode(R.Command(session, "bad", "civ6_gold", 5, 0, 11, 1)).command_success, false)
local tick = decode(R.Tick(session, {civ6_movement = true}, 0, 11, 1))
eq(tick.enabled.civ6_movement, false); eq(stats.moveWrites, 0, "Tick cannot activate")
success("civ6_movement", true); eq(stats.unitMoves, 4); eq(stats.enemyMoves, 1)
stats.unitMoves = 0
R.Tick(session, R.Enabled, 0, 11, 1); eq(stats.unitMoves, 4)
R.Status(0, 11, 1); local moves = stats.moveWrites; R.Status(0, 11, 1); eq(stats.moveWrites, moves, "Status is read-only")
success("civ6_movement", false); stats.unitMoves = 0; R.Tick(session, R.Enabled, 0, 11, 1); eq(stats.unitMoves, 0)
for i, id in ipairs({"civ6_units", "civ6_buildings", "civ6_districts", "civ6_projects"}) do
    success(id, true); eq(queues[i].finished, 1, "Correct production category")
    for later = i + 1, 4 do eq(queues[later].finished, 0, "Other categories not completed") end
end
eq(foreignQueue.finished, 0, "Foreign cities untouched")
success("civ6_research", true); eq(stats.researchFinishes, 1)
success("civ6_civics", true); eq(stats.civicFinishes, 1)
stats.research, stats.civic = -1, -1
R.Tick(session, R.Enabled, 0, 11, 1); eq(stats.researchFinishes, 1); eq(stats.civicFinishes, 1)
stats.active = false
writes = stats.goldWrites; eq(status().ready, false); eq(command("civ6_gold", 1).command_success, false)
local completed = queues[1].finished; R.Tick(session, R.Enabled, 0, 11, 1); eq(queues[1].finished, completed)
success("civ6_units", false); eq(R.Enabled.civ6_units, false, "Off works between turns")
success("disable_all", false)
for _, state in pairs(R.Enabled) do eq(state, false) end
stats.active = true
success("civ6_movement", true); stats.multiplayer = true
eq(status().ready, false); eq(command("civ6_gold", 1).command_success, false); eq(R.Enabled.civ6_movement, false)
stats.multiplayer = false
local guard = GameConfiguration.IsAnyMultiplayer; GameConfiguration.IsAnyMultiplayer = nil
eq(status().ready, false); eq(command("civ6_gold", 1).command_success, false)
GameConfiguration.IsAnyMultiplayer = guard
stats.localId = 1; eq(status().ready, false); stats.localId = 0
stats.alive = false; eq(status().ready, false); stats.alive = true
stats.human = false; eq(status().ready, false); stats.human = true
local changeFavor = diplomacy.ChangeFavor; diplomacy.ChangeFavor = nil
eq(status().values.available_civ6_favor, "false"); eq(command("civ6_favor", 2).command_success, false)
diplomacy.ChangeFavor = changeFavor
success("civ6_movement", true)
local restore = UnitManager.RestoreMovementToFormation
UnitManager.RestoreMovementToFormation = function() error('fixture "native" failure\nline two') end
tick = decode(R.Tick(session, R.Enabled, 0, 11, 1)); eq(tick.enabled.civ6_movement, false)
contains(tick.values.error_civ6_movement, '"native" failure\nline two')
UnitManager.RestoreMovementToFormation = restore

-- Compatibility: unavailable optional GameCore flags do not disable unrelated
-- valid resource/unit APIs. Explicit contrary facts always take precedence.
for _, name in ipairs({"IsHuman", "IsAlive", "IsTurnActive"}) do
    local original = player[name]
    player[name] = nil
    eq(status().ready, true, name .. " missing is not a blanket gate")
    eq(command("civ6_gold", stats.gold).command_success, true)
    player[name] = function() return nil end
    eq(status().ready, true, name .. " nil is not false")
    player[name] = function() error("optional metadata unavailable") end
    eq(status().ready, true, name .. " failing only disables that metadata")
    player[name] = original
end
local isHuman = player.IsHuman
player.IsHuman = nil; player.IsAI = function() return true end
eq(decode(R.Status(0, 11, 1, true)).ready, false, "Known AI cannot use UI compatibility fallback")
eq(command("civ6_gold", stats.gold, 11, 1, nil, true).command_success, false)
player.IsAI = nil; player.IsHuman = isHuman
for _, flag in ipairs({"human", "alive", "active"}) do
    stats[flag] = false
    local before = stats.goldWrites
    eq(decode(R.Status(0, 11, 1, true)).ready, false, "Explicit " .. flag .. " false overrides UI compatibility evidence")
    eq(command("civ6_gold", 999, 11, 1, nil, true).command_success, false)
    eq(stats.goldWrites, before, "UI compatibility cannot bypass known player state")
    stats[flag] = true
end

local getLocalPlayer = Game.GetLocalPlayer
local fallbackReaders = {
    function() return nil end,
    function() return -1 end,
    function() error("GameCore local identity not exposed") end
}
for _, reader in ipairs(fallbackReaders) do
    Game.GetLocalPlayer = reader
    eq(status().ready, false, "Unknown GameCore identity requires verified UI evidence")
    eq(decode(R.Status(0, 11, 1, true)).ready, true)
    eq(command("civ6_gold", stats.gold, 11, 1, nil, true).command_success, true)
    eq(decode(R.Status(0, 11, 1, true)).values.diagnostic_player_source, "InGame")
end
Game.GetLocalPlayer = nil
eq(status().ready, false)
eq(decode(R.Status(0, 11, 1, true)).ready, true, "Absent GetLocalPlayer accepts verified UI player")
local isAlive, isTurnActive = player.IsAlive, player.IsTurnActive
player.IsHuman, player.IsAlive, player.IsTurnActive = nil, nil, nil
eq(decode(R.Status(0, 11, 1, true)).ready, true, "Verified UI identity works without redundant GameCore flags")
eq(command("civ6_movement", true, 11, 1, nil, true).command_success, true)
stats.unitMoves = 0
R.Tick(session, R.Enabled, 0, 11, 1, true); eq(stats.unitMoves, 4, "Tick forwards UI evidence")
stats.unitMoves = 0
R.Tick(session, R.Enabled, 0, 11, 1); eq(stats.unitMoves, 0, "Tick without identity evidence cannot mutate")
player.IsHuman, player.IsAlive, player.IsTurnActive = isHuman, isAlive, isTurnActive
Game.GetLocalPlayer = getLocalPlayer
stats.localId = 1
eq(decode(R.Status(0, 11, 1, true)).ready, false, "Explicit GameCore identity mismatch wins over UI evidence")
eq(command("civ6_gold", stats.gold, 11, 1, nil, true).command_success, false)
stats.localId = 0

local gameConfiguration = GameConfiguration
GameConfiguration = nil
eq(status().ready, false, "Unknown mode without UI evidence is rejected")
eq(decode(R.Status(0, 11, 1, true)).ready, true, "Positive UI single-player evidence covers absent GameCore mode API")
eq(command("civ6_gold", stats.gold, 11, 1, nil, true).command_success, true)
eq(decode(R.Status(0, 11, 1, "true")).ready, false, "UI evidence must be boolean")
GameConfiguration = {IsAnyMultiplayer = function() error("mode not exposed") end}
eq(decode(R.Status(0, 11, 1, true)).ready, true)
GameConfiguration = gameConfiguration
stats.multiplayer = true
eq(decode(R.Status(0, 11, 1, true)).ready, false, "Known multiplayer cannot be overridden")
eq(command("civ6_gold", stats.gold, 11, 1, nil, true).command_success, false)
stats.multiplayer = false
for _, name in ipairs({"IsHotseat", "IsNetworkMultiplayer"}) do
    GameConfiguration[name] = function() return true end
    eq(decode(R.Status(0, 11, 1, true)).ready, false, name .. " explicit multiplayer guard")
    GameConfiguration[name] = nil
end
Players[0] = nil
eq(decode(R.Status(0, 11, 1, true)).ready, false, "UI evidence cannot create a missing player")
Players[0] = player
local unitOwner, cityOwner = unit.GetOwner, cities[1].GetOwner
unit.GetOwner, cities[1].GetOwner = nil, nil
eq(status().values.available_civ6_damage, "true", "Own collection proves unit ownership when owner metadata absent")
eq(status().values.available_civ6_population, "true", "Own collection proves city ownership when owner metadata absent")
success("civ6_damage", 5); eq(stats.damage, 5)
success("civ6_population", 21); eq(stats.population, 21)
success("civ6_movement", true); eq(stats.unitMoves, 4)
local productionBefore = queues[1].finished
success("civ6_units", true); eq(queues[1].finished, productionBefore + 1)
eq(foreignQueue.finished, 0, "Explicit foreign ownership still excluded")
eq(command("civ6_xp", 50, 12, 1, nil, true).command_success, false)
eq(command("civ6_population", 50, 11, 5, nil, true).command_success, false)
unit.GetOwner, cities[1].GetOwner = unitOwner, cityOwner
local damageSetter = unit.SetDamage
unit.SetDamage = nil
eq(status().ready, true, "Missing setter is per-feature only")
eq(status().values.available_civ6_damage, "false")
eq(status().values.available_civ6_gold, "true")
eq(command("civ6_damage", 1).command_success, false)
unit.SetDamage = damageSetter
writes = stats.goldWrites
eq(decode(R.Command("stale_ui_session", "stale_ui_request", "civ6_gold", 5, 0, 11, 1, true)).command_success, false)
eq(stats.goldWrites, writes, "Verified UI evidence cannot override stale session")
success("civ6_movement", true); R.Init("new_session_123")
eq(R.Enabled.civ6_movement, false); eq(command("civ6_gold", 1).command_success, false)
print("Civilization VI runtime: " .. count .. " offline assertions passed.")
