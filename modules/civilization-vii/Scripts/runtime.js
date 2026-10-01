// HappyCheats Civilization VII 0.1.0. Evaluated in the official FireTuner App UI context.
// APIs are taken from the installed Firaxis TunerPanels and shipped UI scripts.
// No file access, persistent event handler, timer, DLL injection or save editing.
globalThis.HappyCheatsCiv7 = (() => {
    "use strict";
    const version = "0.1.0";
    const toggles = ["civ7_movement", "civ7_units", "civ7_buildings", "civ7_projects", "civ7_research", "civ7_civics"];
    const numerics = ["civ7_damage", "civ7_xp", "civ7_gold", "civ7_influence", "civ7_population", "civ7_attributes"];
    const actions = ["civ7_restore_moves", "civ7_growth", "civ7_golden_age"];
    const features = [...toggles, ...numerics, ...actions];
    const enabled = Object.fromEntries(toggles.map(id => [id, false]));
    let session = "", lastCommand = "", commandSuccess = false, commandError = "";
    let seen = new Map(), errors = {}, dispatched = new Set(), identity = null;
    const has = (object, name) => object != null && typeof object[name] === "function";
    const finite = value => typeof value === "number" && Number.isFinite(value);
    const validId = value => Number.isInteger(value) && value >= 0;
    const token = value => typeof value === "string" && /^[A-Za-z0-9_-]{8,128}$/.test(value);
    const assert = (value, message) => { if (!value) throw new Error(message); };
    const errorText = error => String(error && error.message || error);
    const read = (object, name, ...args) => has(object, name) ? object[name](...args) : undefined;
    function clear() { for (const id of toggles) enabled[id] = false; }
    function resetContext() { clear(); dispatched.clear(); identity = null; }
    function number(value, min, max) {
        assert(finite(value) && Number.isInteger(value) && value >= min && value <= max,
            `Value must be an integer between ${min} and ${max}.`);
        return value;
    }
    function owned(object, playerId) {
        return object != null && object.owner === playerId && object.id != null && object.id.owner === playerId && validId(object.id.id);
    }
    function selected(collection, selection, playerId) {
        if (!selection || selection.owner !== playerId || !validId(selection.id)) return null;
        const object = read(collection, "get", selection);
        return owned(object, playerId) ? object : null;
    }
    function context() {
        assert(session !== "", "Initialize the trainer session first.");
        if (!has(globalThis.UI, "isInGame") || UI.isInGame() !== true) {
            resetContext(); throw new Error("Load a single-player game to connect.");
        }
        const configuration = read(globalThis.Configuration, "getGame");
        if (!has(UI, "isMultiplayer") || UI.isMultiplayer() !== false || !configuration ||
            configuration.isNetworkMultiplayer !== false || configuration.isHotseat !== false) {
            resetContext(); throw new Error("A verified single-player game is required; multiplayer and hotseat are unsupported.");
        }
        const playerId = globalThis.GameContext && GameContext.localPlayerID;
        if (!validId(playerId)) { resetContext(); throw new Error("No local player is available."); }
        const player = read(globalThis.Players, "get", playerId);
        const human = read(globalThis.Players, "isHuman", playerId);
        const alive = read(globalThis.Players, "isAlive", playerId);
        if (!player || player.isHuman === false || human === false || player.isAlive === false || alive === false ||
            !(human === true || player.isHuman === true) || !(alive === true || player.isAlive === true)) {
            resetContext(); throw new Error("A living local human player is required.");
        }
        const turn = globalThis.Game && Game.turn, age = globalThis.Game && Game.age;
        const seed = configuration.gameSeed;
        assert(validId(turn) && (finite(age) || typeof age === "string"), "Game turn or Age is unavailable.");
        if (identity && (identity.player !== playerId || identity.age !== age || turn < identity.turn ||
            (identity.seed !== undefined && seed !== undefined && identity.seed !== seed))) {
            clear(); dispatched.clear(); errors = {};
        }
        if (identity && identity.turn !== turn) dispatched.clear();
        identity = { player: playerId, age, turn, seed };
        assert(player.isTurnActive === true && read(GameContext, "hasSentTurnComplete") !== true,
            "Waiting for the local player's active turn.");
        return { player, playerId, turn, age,
            unit: selected(globalThis.Units, read(UI.Player, "getHeadSelectedUnit"), playerId),
            city: selected(globalThis.Cities, read(UI.Player, "getHeadSelectedCity"), playerId) };
    }
    function ownedCities(c) {
        const ids = read(c.player.Cities, "getCityIds");
        assert(Array.isArray(ids), "City collection API is unavailable.");
        return ids.map(id => selected(globalThis.Cities, id, c.playerId)).filter(Boolean);
    }
    function ownedUnits(c) {
        const ids = read(c.player.Units, "getUnitIds");
        assert(Array.isArray(ids), "Unit collection API is unavailable.");
        return ids.map(id => selected(globalThis.Units, id, c.playerId)).filter(Boolean);
    }
    function supports(c, id) {
        if (!c) return "Load the local player's active single-player turn.";
        const p = c.player, u = c.unit, city = c.city;
        switch (id) {
            case "civ7_movement": return has(p.Units, "getUnitIds") && has(globalThis.Units, "restoreMovement") ? "" : "Unit movement API unavailable.";
            case "civ7_units": case "civ7_buildings": case "civ7_projects":
                return globalThis.OrderTypes && OrderTypes[id === "civ7_units" ? "ORDER_TRAIN" : id === "civ7_buildings" ? "ORDER_CONSTRUCT" : "ORDER_ADVANCE"] !== undefined &&
                    ownedCities(c).some(x => [x.BuildQueue, ...(id === "civ7_units" ? [x.FoodQueue] : [])]
                        .some(q => has(q, "getQueue") && has(q, "completeProduction"))) ? "" : "Found a city with a supported production queue.";
            case "civ7_research": case "civ7_civics":
                return has(id === "civ7_research" ? p.Techs : p.Culture, "getResearching") && has(globalThis.Game && Game.PlayerOperations, "sendRequest") &&
                    globalThis.PlayerOperationTypes && PlayerOperationTypes.GRANT_TREE_NODE !== undefined ? "" : "Research grant API unavailable.";
            case "civ7_gold": return has(p.Treasury, "changeGoldBalance") && finite(p.Treasury.goldBalance) ? "" : "Gold treasury API unavailable.";
            case "civ7_influence": return has(p.DiplomacyTreasury, "changeDiplomacyBalance") && finite(p.DiplomacyTreasury.diplomacyBalance) ? "" : "Influence treasury API unavailable.";
            case "civ7_damage": return u && has(globalThis.Units, "setDamage") && finite(u.Health?.damage) && finite(u.Health?.maxDamage) && u.Health.isDead !== true ? "" : "Select your living unit with a health component.";
            case "civ7_xp": return u && has(globalThis.Units, "changeExperience") && finite(u.Experience?.experiencePoints) ? "" : "Select your commander or unit with experience support.";
            case "civ7_restore_moves": return u && has(globalThis.Units, "restoreMovement") && finite(u.Movement?.movementMovesRemaining) && finite(u.Movement?.maxMoves) ? "" : "Select your unit with movement support.";
            case "civ7_population": return city && has(city, "addRuralPopulation") && finite(city.population) ? "" : "Select your settlement with rural population support.";
            case "civ7_growth": return city && has(city.FoodQueue, "completeProduction") && has(city.FoodQueue, "getQueue") ? "" : "Select your settlement with a food queue.";
            case "civ7_attributes": return has(p.Identity, "addWildcardAttributePoints") && has(p.Identity, "getWildcardPoints") ? "" : "Wildcard attribute API unavailable.";
            case "civ7_golden_age": return has(p.Happiness, "startGoldenAge") && has(p.Happiness, "getGoldenAgeTurnsLeft") ? "" : "Celebration API unavailable in this Age or ruleset.";
            default: return "Unknown trainer feature.";
        }
    }
    function snapshot() {
        let c = null, message = "Ready: local single-player game. Gameplay compatibility is unverified.";
        try { c = context(); } catch (error) { message = errorText(error); }
        const values = { player_id: c ? String(c.playerId) : "-1", selected_unit_id: c?.unit ? String(c.unit.id.id) : "-1",
            selected_city_id: c?.city ? String(c.city.id.id) : "-1" };
        for (const id of features) {
            let reason;
            try { reason = supports(c, id); } catch (error) { reason = errorText(error); }
            values["available_" + id] = reason ? "false" : "true";
            if (reason) values["reason_" + id] = reason;
            if (errors[id]) values["error_" + id] = errors[id];
        }
        if (c) {
            const put = (key, value) => { if (finite(value)) values[key] = String(value); };
            values.turn = String(c.turn); values.age = String(c.age);
            put("civ7_gold", c.player.Treasury?.goldBalance); put("civ7_influence", c.player.DiplomacyTreasury?.diplomacyBalance);
            put("civ7_damage", c.unit?.Health?.damage); put("civ7_xp", c.unit?.Experience?.experiencePoints);
            put("civ7_moves", c.unit?.Movement?.movementMovesRemaining); put("civ7_population", c.city?.population);
            try { put("civ7_attributes", read(c.player.Identity, "getWildcardPoints")); } catch (_) { /* optional status */ }
            try { put("civ7_golden_age", read(c.player.Happiness, "getGoldenAgeTurnsLeft")); } catch (_) { /* optional status */ }
        }
        return JSON.stringify({ schema_version: 1, version, session_id: session, ready: c !== null, message,
            last_command: lastCommand, command_success: commandSuccess, command_error: commandError, enabled, values });
    }
    function exact(expected, actual) {
        assert(finite(actual) && Math.abs(actual - expected) < 0.000001,
            `The game did not confirm the requested result (expected ${expected}, observed ${actual}). State may have changed; this action was not retried.`);
    }
    function orderMatches(item, id) {
        if (!item || !globalThis.OrderTypes) return false;
        const equals = key => OrderTypes[key] !== undefined && item.orderType === OrderTypes[key];
        if (id === "civ7_units") return equals("ORDER_TRAIN") || equals("ORDER_FOOD_TRAIN");
        if (id === "civ7_buildings") return equals("ORDER_CONSTRUCT");
        return id === "civ7_projects" && equals("ORDER_ADVANCE");
    }
    function dispatchOnce(key, action) {
        if (dispatched.has(key)) return;
        assert(dispatched.size < 8192, "Too many queued operations in one turn; advance the turn before continuing.");
        // Record before calling native code: a setter can mutate, then throw.
        dispatched.add(key); action();
    }
    function productionKey(c, city, item, id, channel) {
        return JSON.stringify([id, c.playerId, c.age, c.turn, city.id.id, channel, item.orderType, item.type,
            item.unitType, item.constructibleType, item.projectType, item.location, item.instance]);
    }
    function applyToggle(c, id) {
        if (id === "civ7_movement") {
            for (const unit of ownedUnits(c)) {
                if (unit.Health?.isDead !== true && finite(unit.Movement?.movementMovesRemaining) &&
                    finite(unit.Movement?.maxMoves) && unit.Movement.movementMovesRemaining < unit.Movement.maxMoves)
                    Units.restoreMovement(unit.id);
            }
        } else if (id === "civ7_research" || id === "civ7_civics") {
            const research = (id === "civ7_research" ? c.player.Techs : c.player.Culture).getResearching();
            // Database type hashes may be signed; only -1 is the no-research sentinel.
            if (!research || !Number.isInteger(research.type) || research.type === -1 || !validId(research.depth) || !validId(research.maxDepth) || research.depth >= research.maxDepth) return;
            const key = JSON.stringify([id, c.playerId, c.age, c.turn, research.type, research.depth]);
            dispatchOnce(key, () => {
                const result = Game.PlayerOperations.sendRequest(c.playerId, PlayerOperationTypes.GRANT_TREE_NODE,
                    { ProgressionTreeNodeType: research.type, FullyUnlock: 0 });
                assert(result !== false && result?.Success !== false, "The game refused the research grant request.");
            });
        } else {
            for (const city of ownedCities(c)) {
                // Food-funded training has a separate queue in Civ VII. Never complete
                // its non-unit items when only the fast-units toggle is enabled.
                for (const channel of id === "civ7_units" ? ["BuildQueue", "FoodQueue"] : ["BuildQueue"]) {
                    const queue = city[channel];
                    if (!has(queue, "getQueue") || !has(queue, "completeProduction")) continue;
                    const item = queue.getQueue()[0];
                    if (orderMatches(item, id)) dispatchOnce(productionKey(c, city, item, id, channel), () => queue.completeProduction());
                }
            }
        }
    }
    function applyNumeric(c, id, value) {
        const p = c.player, u = c.unit, city = c.city;
        switch (id) {
            case "civ7_gold": number(value, 0, 100000000); p.Treasury.changeGoldBalance(value - p.Treasury.goldBalance, -1); exact(value, p.Treasury.goldBalance); break;
            case "civ7_influence": number(value, 0, 1000000); p.DiplomacyTreasury.changeDiplomacyBalance(value - p.DiplomacyTreasury.diplomacyBalance); exact(value, p.DiplomacyTreasury.diplomacyBalance); break;
            case "civ7_damage": number(value, 0, Math.min(99, u.Health.maxDamage - 1)); Units.setDamage(u.id, value); exact(value, u.Health.damage); break;
            case "civ7_xp": {
                number(value, 1, 100000); const before = u.Experience.experiencePoints; Units.changeExperience(u.id, value);
                exact(before + value, u.Experience.experiencePoints); break;
            }
            case "civ7_population": {
                number(value, 1, 100); const before = city.population; city.addRuralPopulation(value); exact(before + value, city.population); break;
            }
            case "civ7_attributes": {
                number(value, 1, 100); const before = read(p.Identity, "getWildcardPoints"); assert(finite(before), "Cannot read current wildcard points.");
                p.Identity.addWildcardAttributePoints(value); exact(before + value, p.Identity.getWildcardPoints()); break;
            }
            default: throw new Error("Unknown numeric feature.");
        }
    }
    function applyAction(c, id, value) {
        assert(value === null || value === undefined, "An action does not accept a value.");
        switch (id) {
            case "civ7_restore_moves": Units.restoreMovement(c.unit.id); exact(c.unit.Movement.maxMoves, c.unit.Movement.movementMovesRemaining); break;
            case "civ7_growth": assert(c.city.FoodQueue.getQueue().length > 0, "The selected settlement has no food queue item."); c.city.FoodQueue.completeProduction(); break;
            case "civ7_golden_age":
                assert(c.player.Happiness.getGoldenAgeTurnsLeft() <= 0, "A celebration is already active.");
                c.player.Happiness.startGoldenAge(c.playerId);
                assert(c.player.Happiness.getGoldenAgeTurnsLeft() > 0, "The game did not confirm a celebration. State may have changed; this action was not retried."); break;
            default: throw new Error("Unknown trainer action.");
        }
    }
    function init(value) {
        assert(token(value), "Invalid session ID.");
        session = value; lastCommand = ""; commandSuccess = false; commandError = "";
        seen = new Map(); errors = {}; resetContext(); return snapshot();
    }
    function command(expectedSession, request, feature, value, expectedContext) {
        if (expectedSession !== session || !session) {
            lastCommand = String(request || ""); commandSuccess = false; commandError = "Session changed; reconnect before applying cheats."; return snapshot();
        }
        if (!token(request)) { lastCommand = ""; commandSuccess = false; commandError = "Invalid request ID."; return snapshot(); }
        const signature = JSON.stringify([feature, value, expectedContext?.player_id, expectedContext?.age,
            expectedContext?.selected_unit_id, expectedContext?.selected_city_id]);
        lastCommand = request;
        if (seen.has(request)) {
            const previous = seen.get(request);
            commandSuccess = previous.signature === signature && previous.success;
            commandError = previous.signature === signature ? previous.error : "Request ID was reused with different parameters.";
            return snapshot();
        }
        commandSuccess = false; commandError = "";
        try {
            // Never evict replay tokens: a repeated additive command must never execute again.
            assert(seen.size < 4096 || feature === "disable_all" || (toggles.includes(feature) && value === false),
                "Command limit reached. Reconnect to start a new trainer session.");
            if (feature === "disable_all") { clear(); errors = {}; }
            else if (toggles.includes(feature) && value === false) { enabled[feature] = false; delete errors[feature]; }
            else {
                const c = context();
                assert(expectedContext && typeof expectedContext === "object" && expectedContext.player_id === String(c.playerId) && expectedContext.age === String(c.age),
                    "The player or Age changed since the command was prepared. Refresh before applying cheats.");
                if (["civ7_damage", "civ7_xp", "civ7_restore_moves"].includes(feature))
                    assert(c.unit && expectedContext.selected_unit_id === String(c.unit.id.id), "The selected unit changed. Refresh before applying this action.");
                if (["civ7_population", "civ7_growth"].includes(feature))
                    assert(c.city && expectedContext.selected_city_id === String(c.city.id.id), "The selected settlement changed. Refresh before applying this action.");
                const reason = supports(c, feature); assert(!reason, reason); delete errors[feature];
                if (toggles.includes(feature)) {
                    assert(typeof value === "boolean", "Toggle value must be a boolean.");
                    applyToggle(c, feature); enabled[feature] = true;
                } else if (numerics.includes(feature)) applyNumeric(c, feature, value);
                else applyAction(c, feature, value);
            }
            commandSuccess = true;
        } catch (error) {
            commandError = errorText(error);
            if (toggles.includes(feature)) { enabled[feature] = false; errors[feature] = commandError; }
        }
        if (seen.size < 4096) seen.set(request, { signature, success: commandSuccess, error: commandError });
        return snapshot();
    }
    function tick(expectedSession) {
        if (expectedSession !== session || !session) return snapshot();
        let c;
        try { c = context(); } catch (_) { return snapshot(); }
        for (const id of toggles) {
            if (!enabled[id]) continue;
            try { const reason = supports(c, id); assert(!reason, reason); applyToggle(c, id); }
            catch (error) { enabled[id] = false; errors[id] = errorText(error); }
        }
        return snapshot();
    }
    return Object.freeze({ version, Init: init, Status: snapshot, Command: command, Tick: tick });
})();
