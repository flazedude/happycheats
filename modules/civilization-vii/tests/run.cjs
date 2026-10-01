"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const source = fs.readFileSync(path.join(__dirname, "../Scripts/runtime.js"), "utf8");
let checks = 0;
function test(name, action) { action(); checks++; console.log("PASS " + name); }
function fixture() {
    const calls = [], units = new Map(), cities = new Map();
    const key = id => `${id.owner}:${id.id}`;
    const unit = (owner, id) => ({ owner, id: { owner, id, type: 1 }, Health: { damage: 40, maxDamage: 100, isDead: false },
        Movement: { movementMovesRemaining: 0, maxMoves: 3 }, Experience: { experiencePoints: 10 } });
    const queue = type => ({ items: [{ orderType: type, type: 100 + type }], completed: 0,
        getQueue() { return this.items; }, completeProduction() { this.completed++; calls.push("production:" + type); } });
    const city = (owner, id, type) => ({ owner, id: { owner, id, type: 2 }, population: 5,
        BuildQueue: queue(type), FoodQueue: queue(4), addRuralPopulation(n) { this.population += n; calls.push("population"); } });
    const u = unit(0, 10), foreignUnit = unit(1, 20), c = city(0, 30, 1), building = city(0, 31, 2), project = city(0, 32, 3), foreignCity = city(1, 40, 1);
    [u, foreignUnit].forEach(x => units.set(key(x.id), x)); [c, building, project, foreignCity].forEach(x => cities.set(key(x.id), x));
    const p = { id: 0, isHuman: true, isAlive: true, isTurnActive: true,
        Treasury: { goldBalance: 100, changeGoldBalance(n, reason) { assert.equal(reason, -1); this.goldBalance += n; calls.push("gold"); } },
        DiplomacyTreasury: { diplomacyBalance: 25, changeDiplomacyBalance(n) { this.diplomacyBalance += n; calls.push("influence"); } },
        Identity: { points: 0, getWildcardPoints() { return this.points; }, addWildcardAttributePoints(n) { this.points += n; calls.push("attributes"); } },
        Happiness: { turns: 0, getGoldenAgeTurnsLeft() { return this.turns; }, startGoldenAge(id) { assert.equal(id, 0); this.turns = 10; calls.push("golden_age"); } },
        Units: { getUnitIds: () => [u.id, foreignUnit.id] }, Cities: { getCityIds: () => [c.id, building.id, project.id, foreignCity.id] },
        Techs: { researching: { type: 1001, depth: 0, maxDepth: 2 }, getResearching() { return this.researching; } },
        Culture: { researching: { type: 2001, depth: 0, maxDepth: 1 }, getResearching() { return this.researching; } } };
    const state = { inGame: true, multiplayer: false, turnComplete: false, configuration: { isNetworkMultiplayer: false, isHotseat: false, gameSeed: 12345 }, selectedUnit: u.id, selectedCity: c.id };
    const sandbox = { console, UI: { isInGame: () => state.inGame, isMultiplayer: () => state.multiplayer,
        Player: { getHeadSelectedUnit: () => state.selectedUnit, getHeadSelectedCity: () => state.selectedCity } },
        Configuration: { getGame: () => state.configuration }, GameContext: { localPlayerID: 0, hasSentTurnComplete: () => state.turnComplete },
        Players: { get: id => id === 0 ? p : null, isAlive: () => true, isHuman: () => true },
        Units: { get: id => units.get(key(id)), restoreMovement(id) { const x = units.get(key(id)); x.Movement.movementMovesRemaining = x.Movement.maxMoves; calls.push("movement:" + id.owner); },
            setDamage(id, n) { units.get(key(id)).Health.damage = n; calls.push("damage"); },
            changeExperience(id, n) { units.get(key(id)).Experience.experiencePoints += n; calls.push("xp"); } },
        Cities: { get: id => cities.get(key(id)) }, OrderTypes: { ORDER_TRAIN: 1, ORDER_CONSTRUCT: 2, ORDER_ADVANCE: 3, ORDER_FOOD_TRAIN: 4 },
        PlayerOperationTypes: { GRANT_TREE_NODE: 90 }, Game: { turn: 15, age: 1,
            PlayerOperations: { sendRequest(id, operation, args) { calls.push({ id, operation, args: { ...args } }); } } } };
    vm.createContext(sandbox); vm.runInContext(source, sandbox);
    const runtime = sandbox.HappyCheatsCiv7;
    const session = "fixture_session_01"; runtime.Init(session);
    let request = 0;
    const command = (id, value = null, requestId = `request_${++request}`, expectedContext = JSON.parse(runtime.Status()).values) =>
        JSON.parse(runtime.Command(session, requestId, id, value, expectedContext));
    const status = () => JSON.parse(runtime.Status());
    const tick = () => JSON.parse(runtime.Tick(session));
    return { runtime, session, sandbox, state, calls, u, foreignUnit, c, building, project, foreignCity, p, command, status, tick };
}
function success(result) { assert.equal(result.command_success, true, result.command_error); }
function failure(result, pattern) { assert.equal(result.command_success, false); if (pattern) assert.match(result.command_error, pattern); }
test("status has all 15 feature capabilities and JSON protocol", () => {
    const f = fixture(), s = f.status(); assert.equal(s.ready, true); assert.equal(s.schema_version, 1); assert.equal(s.version, "0.1.0");
    assert.equal(Object.keys(s.values).filter(x => x.startsWith("available_")).length, 15);
    assert.equal(Object.values(s.values).filter(x => x === "false").length, 0);
});
test("gold and influence set exact balances", () => {
    const f = fixture(); success(f.command("civ7_gold", 12345)); success(f.command("civ7_influence", 2345));
    assert.equal(f.p.Treasury.goldBalance, 12345); assert.equal(f.p.DiplomacyTreasury.diplomacyBalance, 2345);
});
test("selected damage, experience, rural population and wildcard increments", () => {
    const f = fixture(); success(f.command("civ7_damage", 0)); success(f.command("civ7_xp", 100)); success(f.command("civ7_population", 2)); success(f.command("civ7_attributes", 3));
    assert.equal(f.u.Health.damage, 0); assert.equal(f.u.Experience.experiencePoints, 110); assert.equal(f.c.population, 7); assert.equal(f.p.Identity.points, 3);
});
test("selected movement, food queue, celebration actions", () => {
    const f = fixture(); success(f.command("civ7_restore_moves")); success(f.command("civ7_growth")); success(f.command("civ7_golden_age"));
    assert.equal(f.u.Movement.movementMovesRemaining, 3); assert.equal(f.c.FoodQueue.completed, 1); assert.equal(f.p.Happiness.turns, 10);
    failure(f.command("civ7_golden_age"), /already active/);
});
test("continuous movement restores owned living units only", () => {
    const f = fixture(); success(f.command("civ7_movement", true)); assert.equal(f.u.Movement.movementMovesRemaining, 3); assert.equal(f.foreignUnit.Movement.movementMovesRemaining, 0);
    f.u.Movement.movementMovesRemaining = 1; f.tick(); assert.equal(f.u.Movement.movementMovesRemaining, 3);
    f.u.Health.isDead = true; f.u.Movement.movementMovesRemaining = 0; f.tick(); assert.equal(f.u.Movement.movementMovesRemaining, 0);
});
test("production filters each type, excludes foreign city and deduplicates unchanged head", () => {
    const f = fixture(); success(f.command("civ7_units", true)); assert.equal(f.c.BuildQueue.completed, 1); assert.equal(f.building.BuildQueue.completed, 0);
    success(f.command("civ7_buildings", true)); success(f.command("civ7_projects", true));
    assert.equal(f.building.BuildQueue.completed, 1); assert.equal(f.project.BuildQueue.completed, 1); assert.equal(f.foreignCity.BuildQueue.completed, 0);
    f.tick(); assert.equal(f.c.BuildQueue.completed, 1); f.sandbox.Game.turn++; f.tick(); assert.equal(f.c.BuildQueue.completed, 2);
});
test("research dispatch targets chosen node, does not grant all, and awaits change", () => {
    const f = fixture(); success(f.command("civ7_research", true)); success(f.command("civ7_civics", true)); f.tick();
    const requests = () => f.calls.filter(x => typeof x === "object"); assert.equal(requests().length, 2);
    assert.deepEqual(requests()[0], { id: 0, operation: 90, args: { ProgressionTreeNodeType: 1001, FullyUnlock: 0 } });
    f.p.Techs.researching.depth = 1; f.tick(); assert.equal(requests().length, 3);
    f.p.Techs.researching = { type: -1, depth: 0, maxDepth: 0 }; f.tick(); assert.equal(requests().length, 3);
});
test("fast units finishes food-funded training without completing non-unit food items", () => {
    const f = fixture(); f.building.FoodQueue.items = [{ orderType: 99, type: 90 }];
    // Identical queue IDs in different channels must not suppress either dispatch.
    f.c.BuildQueue.items = [{ orderType: 4, type: 104 }];
    success(f.command("civ7_units", true)); assert.equal(f.c.BuildQueue.completed, 1); assert.equal(f.c.FoodQueue.completed, 1);
    assert.equal(f.building.FoodQueue.completed, 0); assert.equal(f.foreignCity.FoodQueue.completed, 0);
    f.tick(); assert.equal(f.c.FoodQueue.completed, 1);
});
test("negative research hashes are valid except the no-research sentinel", () => {
    const f = fixture(); f.p.Techs.researching.type = -23456789; success(f.command("civ7_research", true));
    assert.equal(f.calls.filter(x => typeof x === "object")[0].args.ProgressionTreeNodeType, -23456789);
});
test("range, finite, integer, type, and action payload validation prevents writes", () => {
    const f = fixture(); for (const [id, bad] of [["civ7_gold", -1], ["civ7_gold", 100000001], ["civ7_gold", NaN], ["civ7_gold", Infinity], ["civ7_gold", "10"],
        ["civ7_influence", 1000001], ["civ7_damage", 100], ["civ7_xp", 0], ["civ7_population", 101], ["civ7_attributes", 0.5], ["civ7_movement", 1], ["civ7_growth", 1]]) failure(f.command(id, bad));
    assert.equal(f.calls.length, 0); f.u.Health.maxDamage = 50; failure(f.command("civ7_damage", 50));
});
test("single player, hotseat, missing classification, dead/AI and inactive-turn guards", () => {
    const cases = [f => f.state.multiplayer = true, f => f.state.configuration.isHotseat = true, f => f.state.configuration.isNetworkMultiplayer = true,
        f => delete f.state.configuration.isHotseat, f => f.p.isHuman = false, f => f.p.isAlive = false, f => f.p.isTurnActive = false,
        f => f.state.turnComplete = true, f => f.state.inGame = false];
    for (const change of cases) { const f = fixture(); change(f); failure(f.command("civ7_gold", 900)); assert.equal(f.calls.length, 0); assert.equal(f.status().ready, false); }
});
test("foreign selection rejected without blocking unrelated gold", () => {
    const f = fixture(); f.state.selectedUnit = f.foreignUnit.id; f.state.selectedCity = f.foreignCity.id;
    failure(f.command("civ7_damage", 0)); failure(f.command("civ7_population", 1)); success(f.command("civ7_gold", 900));
    assert.equal(f.foreignUnit.Health.damage, 40); assert.equal(f.foreignCity.population, 5);
});
test("conflicting object ownership does not trust collection or selected IDs", () => {
    const f = fixture(); f.u.owner = 1; failure(f.command("civ7_xp", 5)); success(f.command("civ7_movement", true)); assert.equal(f.u.Movement.movementMovesRemaining, 0);
});
test("request replay is at most once even after selection changes", () => {
    const f = fixture(), expected = f.status().values; success(f.command("civ7_xp", 5, "repeat_request", expected)); f.state.selectedUnit = f.foreignUnit.id;
    success(f.command("civ7_xp", 5, "repeat_request", expected)); assert.equal(f.u.Experience.experiencePoints, 15); assert.equal(f.foreignUnit.Experience.experiencePoints, 10);
    failure(f.command("civ7_xp", 6, "repeat_request"), /different parameters/);
});
test("stale own-settlement, unit and Age context never writes", () => {
    const f = fixture(), expected = f.status().values;
    f.state.selectedCity = f.building.id; failure(f.command("civ7_population", 2, "stale_city_request", expected), /settlement changed/);
    // Change an otherwise valid owned unit identity, as can happen after selection or recreation.
    f.u.id.id = 11; failure(f.command("civ7_xp", 10, "stale_unit_request", expected), /unit changed/);
    f.sandbox.Game.age++; failure(f.command("civ7_gold", 200, "stale_age_request", expected), /Age changed/);
    assert.equal(f.calls.length, 0);
});
test("mutation without expected context rejected but cleanup remains available", () => {
    const f = fixture(); failure(JSON.parse(f.runtime.Command(f.session, "missing_context", "civ7_gold", 200)), /Age changed/);
    success(JSON.parse(f.runtime.Command(f.session, "cleanup_context", "disable_all", null)));
    assert.equal(f.calls.length, 0);
});
test("mutate then throw is recorded and never retried", () => {
    const f = fixture(); f.p.Identity.addWildcardAttributePoints = function(n) { this.points += n; throw new Error("native error after mutation"); };
    failure(f.command("civ7_attributes", 2, "error_request")); failure(f.command("civ7_attributes", 2, "error_request")); assert.equal(f.p.Identity.points, 2);
});
test("silent native clamp is reported rather than acknowledged as exact", () => {
    const f = fixture(); f.p.Treasury.changeGoldBalance = function() { this.goldBalance = 99; };
    failure(f.command("civ7_gold", 1000), /did not confirm/); assert.equal(f.p.Treasury.goldBalance, 99);
});
test("disable all and individual off work outside active turn", () => {
    const f = fixture(); success(f.command("civ7_movement", true)); f.p.isTurnActive = false;
    success(f.command("civ7_movement", false)); success(f.command("disable_all")); assert.equal(f.status().enabled.civ7_movement, false);
});
test("tick cannot activate toggles and wrong session cannot mutate", () => {
    const f = fixture(); f.tick(); assert.equal(f.calls.length, 0);
    failure(JSON.parse(f.runtime.Command("wrong_session", "valid_request", "civ7_gold", 1000)), /Session changed/);
    f.runtime.Tick("wrong_session"); assert.equal(f.calls.length, 0);
});
test("age, backward turn, game seed, local player, unloaded state and reinit reset toggles", () => {
    for (const change of [f => f.sandbox.Game.age++, f => f.sandbox.Game.turn--, f => f.sandbox.GameContext.localPlayerID = 1, f => f.state.inGame = false,
        f => f.state.configuration.gameSeed++,
        f => f.runtime.Init("new_session_01")]) {
        const f = fixture(); success(f.command("civ7_movement", true)); f.u.Movement.movementMovesRemaining = 0; change(f); f.tick();
        assert.equal(f.status().enabled.civ7_movement, false); assert.equal(f.u.Movement.movementMovesRemaining, 0);
    }
});
test("between-turn polling pauses writes but preserves toggle until active", () => {
    const f = fixture(); success(f.command("civ7_movement", true)); f.p.isTurnActive = false; f.u.Movement.movementMovesRemaining = 0; f.tick();
    assert.equal(f.u.Movement.movementMovesRemaining, 0); assert.equal(f.status().enabled.civ7_movement, true);
    f.p.isTurnActive = true; f.tick(); assert.equal(f.u.Movement.movementMovesRemaining, 3);
});
test("missing capability only disables corresponding feature", () => {
    const f = fixture(); delete f.p.Identity.getWildcardPoints; assert.equal(f.status().values.available_civ7_attributes, "false");
    assert.equal(f.status().values.available_civ7_gold, "true"); failure(f.command("civ7_attributes", 1)); success(f.command("civ7_gold", 200));
});
test("empty food queue refuses action; unknown feature refuses mutation", () => {
    const f = fixture(); f.c.FoodQueue.items = []; failure(f.command("civ7_growth"), /no food queue/); failure(f.command("missing_feature", 10)); assert.equal(f.calls.length, 0);
});
test("runtime creates no timers, event subscriptions, file or network access", () => {
    assert.doesNotMatch(source, /\b(?:setInterval|setTimeout|fetch|XMLHttpRequest|WebSocket|require|addEventListener)\s*\(/);
});
console.log(`Civilization VII runtime: ${checks} offline checks passed. Gameplay remains unverified.`);
