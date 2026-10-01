-- HappyCheats Civilization VI bridge, executed only in GameCore_Tuner.
-- No file I/O, installed mod, persistent event handler or save edits are used.
-- See ../README.md for API sources and deliberately unsupported raw editors.
HappyCheatsCiv6 = (function()
    local R = { version = "0.1.1" }
    local session, lastCommand, commandError, commandSuccess = "", "", "", false
    local seen, seenOrder = {}, {}
    local toggles = {"civ6_movement", "civ6_units", "civ6_buildings", "civ6_districts",
        "civ6_projects", "civ6_research", "civ6_civics"}
    local numeric = {"civ6_damage", "civ6_moves", "civ6_can_level", "civ6_xp", "civ6_level",
        "civ6_gold", "civ6_faith", "civ6_influence", "civ6_favor", "civ6_population",
        "civ6_amenities", "civ6_housing"}
    local enabled, toggleSet, errors = {}, {}, {}
    R.Enabled = enabled
    for _, id in ipairs(toggles) do enabled[id], toggleSet[id] = false, true end
    local unsupported = {
        civ6_level = "The stock Civilization VI Lua API has no raw unit-level setter.",
        civ6_influence = "The stock Lua API has no influence-points setter; envoy tokens are a different value.",
        civ6_amenities = "The stock Lua API has no amenities-bonus setter.",
        civ6_housing = "The stock Lua API has no housing-bonus setter."
    }
    local function quote(v)
        return '"' .. tostring(v):gsub('[%z\1-\31\\"]', function(c)
            if c == '\\' then return '\\\\' end
            if c == '"' then return '\\"' end
            return string.format('\\u%04x', string.byte(c))
        end) .. '"'
    end
    local function json(v)
        if type(v) == "string" then return quote(v) end
        if type(v) == "boolean" then return v and "true" or "false" end
        if type(v) == "number" then
            if v ~= v or v == math.huge or v == -math.huge then return "null" end
            return tostring(v)
        end
        if type(v) ~= "table" then return "null" end
        local keys, fields = {}, {}
        for k in pairs(v) do keys[#keys + 1] = k end
        table.sort(keys)
        for _, k in ipairs(keys) do fields[#fields + 1] = quote(k) .. ":" .. json(v[k]) end
        return "{" .. table.concat(fields, ",") .. "}"
    end
    local function has(obj, name)
        if obj == nil then return false end
        local ok, member = pcall(function() return obj[name] end)
        return ok and type(member) == "function"
    end
    local function read(obj, name, ...)
        if not has(obj, name) then return nil end
        local ok, value = pcall(obj[name], obj, ...)
        if ok then return value end
        return nil
    end
    local function readStatic(obj, name, ...)
        if not has(obj, name) then return nil end
        local ok, value = pcall(obj[name], ...)
        if ok then return value end
        return nil
    end
    local function validId(value)
        return type(value) == "number" and value >= 0 and value < math.huge and value == math.floor(value)
    end
    local function owned(obj, playerId)
        if obj == nil then return false end
        -- These objects have already come from this player's own collection.
        -- Some GameCore builds omit redundant owner metadata; an explicit
        -- different owner still overrides collection membership and is rejected.
        local owner = read(obj, "GetOwner")
        return owner == nil or owner == playerId
    end
    local function all(obj, names)
        for _, name in ipairs(names) do if not has(obj, name) then return false end end
        return true
    end
    local function clear()
        for _, id in ipairs(toggles) do enabled[id] = false end
    end
    local function context(playerId, unitId, cityId, verifiedSinglePlayer)
        if session == "" then return nil, "Initialize the trainer session first." end
        local multiplayer = readStatic(GameConfiguration, "IsAnyMultiplayer")
        if (multiplayer ~= nil and multiplayer ~= false)
            or readStatic(GameConfiguration, "IsHotseat") == true
            or readStatic(GameConfiguration, "IsNetworkMultiplayer") == true then
            clear(); return nil, "Single-player games only."
        end
        if multiplayer ~= false and verifiedSinglePlayer ~= true then
            return nil, "Single-player session checks are unavailable in this game context."
        end
        if not validId(playerId) then
            clear(); return nil, "Load a single-player game and select your local player."
        end
        -- The caller obtains playerId afresh from the verified InGame UI context.
        -- GameCore_Tuner can omit GetLocalPlayer or return -1 despite that loaded
        -- local game. A concrete conflicting GameCore identity is never ignored.
        local coreId = readStatic(Game, "GetLocalPlayer")
        if validId(coreId) and coreId ~= playerId then
            clear(); return nil, "The local player changed; refresh the game selection."
        end
        if not validId(coreId) and verifiedSinglePlayer ~= true then
            return nil, "Refresh the verified local player from the in-game UI."
        end
        local localId = playerId
        local player = Players and Players[localId]
        if not player or read(player, "IsHuman") == false or read(player, "IsAI") == true or read(player, "IsAlive") == false then
            clear(); return nil, "A living local human player is required."
        end
        -- Optional state flags are capability hints, not requirements shared by
        -- every resource and unit edit. Missing/nil flags must not grey all cards.
        if read(player, "IsTurnActive") == false then return nil, "Waiting for the local player's active turn." end
        local units, cities = read(player, "GetUnits"), read(player, "GetCities")
        local unit = validId(unitId) and read(units, "FindID", unitId) or nil
        local city = validId(cityId) and read(cities, "FindID", cityId) or nil
        if not owned(unit, localId) then unit = nil end
        if not owned(city, localId) then city = nil end
        return {player = player, playerId = localId, units = units, cities = cities, unit = unit, city = city,
            treasury = read(player, "GetTreasury"), religion = read(player, "GetReligion"),
            influence = read(player, "GetInfluence"), diplomacy = read(player, "GetDiplomacy"),
            techs = read(player, "GetTechs"), culture = read(player, "GetCulture"),
            experience = read(unit, "GetExperience"), playerSource = validId(coreId) and "GameCore and InGame" or "InGame"}, "Ready: local single-player game."
    end
    local function supports(c, feature)
        if unsupported[feature] then return false, unsupported[feature] end
        if not c then return false, "Load the local player's active single-player turn." end
        if feature == "civ6_movement" then
            return has(c.units, "Members") and has(UnitManager, "RestoreMovementToFormation"), "Movement API unavailable."
        elseif feature == "civ6_units" or feature == "civ6_buildings" or feature == "civ6_districts" or feature == "civ6_projects" then
            if not has(c.cities, "Members") or not GameInfo then return false, "City production API unavailable." end
            for _, city in c.cities:Members() do
                if owned(city, c.playerId) and all(read(city, "GetBuildQueue"), {"CurrentlyBuilding", "FinishProgress"}) then return true end
            end
            return false, "Found a city to use production cheats."
        elseif feature == "civ6_research" then
            return all(c.techs, {"GetResearchingTech", "GetResearchCost", "SetResearchProgress"}), "Research API unavailable."
        elseif feature == "civ6_civics" then
            return all(c.culture, {"GetProgressingCivic", "GetCultureCost", "SetCulturalProgress"}), "Civic API unavailable."
        elseif feature == "civ6_gold" then
            return all(c.treasury, {"GetGoldBalance", "ChangeGoldBalance"}), "Treasury API unavailable."
        elseif feature == "civ6_faith" then
            return all(c.religion, {"GetFaithBalance", "ChangeFaithBalance"}), "Faith API unavailable."
        elseif feature == "civ6_favor" then
            return all(c.diplomacy, {"GetFavor", "ChangeFavor"}), "Diplomatic favor requires a ruleset exposing favor (Gathering Storm)."
        elseif feature == "civ6_population" then
            return all(c.city, {"GetPopulation", "ChangePopulation"}), "Select a city owned by the local player."
        elseif feature == "civ6_damage" then
            return all(c.unit, {"GetDamage", "SetDamage"}), "Select a unit owned by the local player."
        elseif feature == "civ6_moves" then
            return has(c.unit, "GetMovesRemaining") and has(UnitManager, "ChangeMovesRemaining"), "Select an owned unit with movement API support."
        elseif feature == "civ6_can_level" then
            return all(c.experience, {"CanPromote", "ChangeStoredPromotions", "GetExperienceForNextLevel", "ChangeExperience"}), "Select an owned unit supporting promotion and experience APIs."
        elseif feature == "civ6_xp" then
            return all(c.experience, {"GetExperiencePoints", "ChangeExperience"}), "Select an owned unit supporting experience."
        end
        return false, "Unknown Civilization VI feature."
    end
    local function snapshot(playerId, unitId, cityId, verifiedSinglePlayer)
        local ok, c, message = pcall(context, playerId, unitId, cityId, verifiedSinglePlayer)
        if not ok then message, c = "Game context is unavailable: " .. tostring(c), nil end
        local values = {player_id = tostring(playerId or -1), selected_unit_id = "-1", selected_city_id = "-1"}
        if has(Game, "GetCurrentGameTurn") then
            local turnOk, turn = pcall(Game.GetCurrentGameTurn)
            if turnOk then values.turn = tostring(turn) end
        end
        for _, group in ipairs({toggles, numeric}) do
            for _, id in ipairs(group) do
                local valid, available, reason = pcall(supports, c, id)
                values["available_" .. id] = tostring(valid and available == true)
                if not valid or not available then values["reason_" .. id] = valid and reason or tostring(available) end
                if errors[id] then values["error_" .. id] = errors[id] end
            end
        end
        if c then
            values.diagnostic_player_source = c.playerSource
            local function value(key, obj, method)
                local current = read(obj, method)
                if current ~= nil then values[key] = tostring(current) end
            end
            value("selected_unit_id", c.unit, "GetID"); value("selected_city_id", c.city, "GetID")
            value("civ6_gold", c.treasury, "GetGoldBalance"); value("civ6_faith", c.religion, "GetFaithBalance")
            value("civ6_influence", c.influence, "GetPointsEarned"); value("civ6_favor", c.diplomacy, "GetFavor")
            value("civ6_damage", c.unit, "GetDamage"); value("civ6_moves", c.unit, "GetMovesRemaining")
            value("civ6_xp", c.experience, "GetExperiencePoints"); value("civ6_level", c.experience, "GetLevel")
            value("civ6_population", c.city, "GetPopulation")
            local promote = read(c.experience, "CanPromote")
            if promote ~= nil then values.civ6_can_level = promote and "1" or "0" end
        end
        return {schema_version = 1, ready = c ~= nil, session_id = session, enabled = enabled, values = values, message = message or "Not ready.",
            last_command = lastCommand, command_success = commandSuccess, command_error = commandError, version = R.version}
    end
    function R.Status(playerId, unitId, cityId, verifiedSinglePlayer) return json(snapshot(playerId, unitId, cityId, verifiedSinglePlayer)) end
    function R.Init(value)
        assert(type(value) == "string" and #value >= 8 and #value <= 128 and value:match("^[%w_%-]+$"), "Invalid session ID.")
        -- Reinitialization also discards every old toggle and replay token.
        session, lastCommand, commandError, commandSuccess = value, "", "", false
        seen, seenOrder, errors = {}, {}, {}; clear()
        return R.Status(-1, -1, -1)
    end
    local function number(value, minimum, maximum, integer)
        assert(type(value) == "number" and value == value and value >= minimum and value <= maximum,
            "Value must be a finite number between " .. minimum .. " and " .. maximum .. ".")
        assert(not integer or value == math.floor(value), "Value must be an integer.")
        return value
    end
    local function applyToggle(c, feature)
        if feature == "civ6_movement" then
            for _, unit in c.units:Members() do
                if owned(unit, c.playerId) then UnitManager.RestoreMovementToFormation(unit) end
            end
        elseif feature == "civ6_research" then
            local id = c.techs:GetResearchingTech()
            if type(id) == "number" and id >= 0 then c.techs:SetResearchProgress(id, c.techs:GetResearchCost(id)) end
        elseif feature == "civ6_civics" then
            local id = c.culture:GetProgressingCivic()
            if type(id) == "number" and id >= 0 then c.culture:SetCulturalProgress(id, c.culture:GetCultureCost(id)) end
        else
            local category = ({civ6_units = "Units", civ6_buildings = "Buildings", civ6_districts = "Districts", civ6_projects = "Projects"})[feature]
            assert(category and GameInfo[category], "Production category unavailable.")
            for _, city in c.cities:Members() do
                if owned(city, c.playerId) then
                    local queue = read(city, "GetBuildQueue")
                    assert(all(queue, {"CurrentlyBuilding", "FinishProgress"}), "City production API unavailable.")
                    local current = queue:CurrentlyBuilding()
                    -- CurrentlyBuilding returns the database type (not a queue index).
                    if current and current ~= "" and GameInfo[category][current] then queue:FinishProgress() end
                end
            end
        end
    end
    local function applyNumber(c, feature, value)
        local observed
        if feature == "civ6_gold" then
            c.treasury:ChangeGoldBalance(number(value, 0, 1000000000, true) - c.treasury:GetGoldBalance())
            observed = c.treasury:GetGoldBalance()
        elseif feature == "civ6_faith" then
            c.religion:ChangeFaithBalance(number(value, 0, 1000000000, true) - c.religion:GetFaithBalance())
            observed = c.religion:GetFaithBalance()
        elseif feature == "civ6_favor" then
            c.diplomacy:ChangeFavor(number(value, 0, 1000000000, true) - c.diplomacy:GetFavor())
            observed = c.diplomacy:GetFavor()
        elseif feature == "civ6_population" then
            c.city:ChangePopulation(number(value, 1, 1000, true) - c.city:GetPopulation())
            observed = c.city:GetPopulation()
        elseif feature == "civ6_damage" then
            local maxHitPoints = read(c.unit, "GetMaxDamage") or 100
            c.unit:SetDamage(number(value, 0, math.min(99, maxHitPoints - 1), true))
            observed = c.unit:GetDamage()
        elseif feature == "civ6_moves" then
            UnitManager.ChangeMovesRemaining(c.unit, number(value, 0, 1000, true) - c.unit:GetMovesRemaining())
            observed = c.unit:GetMovesRemaining()
        elseif feature == "civ6_xp" then
            c.experience:ChangeExperience(number(value, 0, 10000000, true) - c.experience:GetExperiencePoints())
            observed = c.experience:GetExperiencePoints()
        elseif feature == "civ6_can_level" then
            number(value, 0, 1, true)
            if value == 1 and not c.experience:CanPromote() then
                -- Firaxis scenarios grant next-level XP first: merely adding a
                -- stored promotion does not unlock a unit that cannot promote.
                local nextLevel = number(c.experience:GetExperienceForNextLevel(), 1, 10000000, false)
                c.experience:ChangeExperience(nextLevel)
            elseif value == 0 and c.experience:CanPromote() then
                -- There is no verified stored-promotion count getter. Attempt
                -- only one decrement; never guess a count by repeated writes.
                c.experience:ChangeStoredPromotions(-1)
            end
            observed = c.experience:CanPromote() and 1 or 0
        else error(unsupported[feature] or "Unknown numeric feature.") end
        assert(type(observed) == "number" and observed == observed and math.abs(observed - value) <= 0.000001,
            "The game did not accept the exact requested value (requested " .. tostring(value) ..
            ", observed " .. tostring(observed) .. "). State may have changed; this action was not retried.")
    end
    function R.Command(expectedSession, request, feature, value, playerId, unitId, cityId, verifiedSinglePlayer)
        if expectedSession ~= session or session == "" then
            lastCommand, commandSuccess, commandError = tostring(request or ""), false, "Session changed; reconnect before applying cheats."
            return R.Status(playerId, unitId, cityId, verifiedSinglePlayer)
        end
        if type(request) ~= "string" or #request < 8 or #request > 128 or not request:match("^[%w_%-]+$") then
            lastCommand, commandSuccess, commandError = "", false, "Invalid request ID."
            return R.Status(playerId, unitId, cityId, verifiedSinglePlayer)
        end
        local signature = table.concat({tostring(feature), tostring(value), tostring(playerId), tostring(unitId), tostring(cityId)}, "|")
        if seen[request] then
            lastCommand = request
            if seen[request].signature ~= signature then commandSuccess, commandError = false, "Request ID was reused with different parameters."
            else commandSuccess, commandError = seen[request].success, seen[request].message end
            return R.Status(playerId, unitId, cityId, verifiedSinglePlayer)
        end
        lastCommand, commandSuccess, commandError = request, false, ""
        local ok, problem = pcall(function()
            if feature == "disable_all" then clear(); errors = {}; return end
            -- Turning a toggle off is always allowed, including between turns.
            if toggleSet[feature] and value == false then enabled[feature], errors[feature] = false, nil; return end
            local c, reason = context(playerId, unitId, cityId, verifiedSinglePlayer)
            assert(c, reason)
            local available, explanation = supports(c, feature)
            assert(available, explanation)
            errors[feature] = nil
            if toggleSet[feature] then
                assert(type(value) == "boolean", "Toggle value must be a boolean.")
                applyToggle(c, feature)
                enabled[feature] = true
            else applyNumber(c, feature, value) end
        end)
        commandSuccess, commandError = ok, ok and "" or tostring(problem)
        if not ok and toggleSet[feature] then enabled[feature], errors[feature] = false, commandError end
        -- Record failures too: a native setter could throw after changing state.
        seen[request] = {signature = signature, success = commandSuccess, message = commandError}
        seenOrder[#seenOrder + 1] = request
        if #seenOrder > 256 then seen[table.remove(seenOrder, 1)] = nil end
        return R.Status(playerId, unitId, cityId, verifiedSinglePlayer)
    end
    function R.Tick(expectedSession, requested, playerId, unitId, cityId, verifiedSinglePlayer)
        if expectedSession ~= session or type(requested) ~= "table" then return R.Status(playerId, unitId, cityId, verifiedSinglePlayer) end
        local valid, c = pcall(context, playerId, unitId, cityId, verifiedSinglePlayer)
        if not valid or not c then return R.Status(playerId, unitId, cityId, verifiedSinglePlayer) end
        for _, feature in ipairs(toggles) do
            -- Tick can revoke a toggle, but only Command may activate one.
            if requested[feature] ~= true then enabled[feature] = false end
            if enabled[feature] then
                local ok, problem = pcall(function()
                    local available, reason = supports(c, feature)
                    assert(available, reason); applyToggle(c, feature)
                end)
                if not ok then enabled[feature], errors[feature] = false, tostring(problem) end
            end
        end
        return R.Status(playerId, unitId, cityId, verifiedSinglePlayer)
    end
    return R
end)()
