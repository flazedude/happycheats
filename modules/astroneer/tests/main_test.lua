-- Exercise the actual main.lua and protocol against an in-memory bridge and
-- queued game-thread callbacks. No game process or bridge disk files are used.
local checks = 0
local function equal(actual, expected, message)
    checks = checks + 1
    assert(actual == expected, (message or "Mismatch") .. ": " .. tostring(actual) .. " ~= " .. tostring(expected))
end
local function deep_copy(value)
    if type(value) ~= "table" then return value end
    local result = {}
    for key, item in pairs(value) do result[key] = deep_copy(item) end
    return result
end

local files, jobs, keys, messages = {}, {}, {}, {}
local latest, interval_callback
local now, game_thread = 1000, false
local state = { ready = true, key = "world-1/player-1/game", bytes = 0, selected_multiplier = 10, selected_run_speed = 3,
    flags = { resource_collection_10x = false, unlimited_oxygen = false, unlimited_power = false, invulnerability = false, player_run_speed = false },
    refresh_count = 0, disable_count = 0, apply_calls = {} }
local trainer = {}
local function on_game_thread()
    assert(game_thread, "Engine binding accessed outside the scheduled game thread")
end
function trainer:refresh()
    on_game_thread()
    state.refresh_count = state.refresh_count + 1
    if state.refresh_error then error("Mock transient lifecycle failure") end
    if state.bound_key ~= state.key then
        state.bound_key = state.key
        state.selected_multiplier = 10
        state.selected_run_speed = 3
        for name in pairs(state.flags) do state.flags[name] = false end
    end
    return state.ready, state.ready and "Ready" or "Load a save",
        { bytes = state.bytes, collection_multiplier = state.flags.resource_collection_10x and state.selected_multiplier or 1,
            selected_collection_multiplier = state.selected_multiplier, selected_run_speed_multiplier = state.selected_run_speed,
            run_speed_multiplier = state.flags.player_run_speed and state.selected_run_speed or state.pending_run_speed or 1,
            restoration_pending = state.restoration_pending == true },
        { resource_collection_10x = true, set_collection_multiplier = true, unlimited_oxygen = true, unlimited_power = true,
            add_bytes = true, disable_all = true, invulnerability = true, player_run_speed = true, set_run_speed_multiplier = true }
end
function trainer:session_key() on_game_thread(); return state.key end
function trainer:enabled() on_game_thread(); return deep_copy(state.flags) end
function trainer:disable_all()
    on_game_thread()
    state.disable_count = state.disable_count + 1
    for name in pairs(state.flags) do state.flags[name] = false end
    state.restoration_pending, state.pending_run_speed = false, nil
    return true, "All features disabled"
end
function trainer:apply(action, enabled, multiplier)
    on_game_thread()
    state.apply_calls[#state.apply_calls + 1] = { action = action, enabled = enabled, multiplier = multiplier }
    if action == "disable_all" then return self:disable_all() end
    if not state.ready then return false, "Load a save" end
    if action == "set_collection_multiplier" then
        assert(enabled == false and (multiplier == 10 or multiplier == 20 or multiplier == 100), "Invalid selection forwarded")
        state.selected_multiplier = multiplier
        return true, "Multiplier selected"
    end
    if action == "set_run_speed_multiplier" then
        assert(enabled == false and (multiplier == 3 or multiplier == 5 or multiplier == 10), "Invalid run speed selection forwarded")
        state.selected_run_speed = multiplier
        return true, "Run speed selected"
    end
    if action == "add_bytes" then state.bytes = state.bytes + 10000; return true, "Added 10,000 bytes" end
    if action == "player_run_speed" and enabled == false and state.fail_run_speed_off then
        state.flags.player_run_speed = false
        state.restoration_pending, state.pending_run_speed = true, state.selected_run_speed
        return false, "Disabled; restore failed"
    end
    if state.flags[action] == nil then return false, "Unknown action" end
    state.flags[action] = enabled == true
    return true, enabled and "Enabled" or "Disabled"
end

local protocol = dofile("modules/astroneer/Scripts/protocol.lua")
local real_json = protocol.json
protocol.json = function(value)
    if type(value) == "table" and value.schema_version then latest = deep_copy(value) end
    return real_json(value)
end
local function basename(path) return path:gsub("\\", "/"):match("([^/]+)$") end
local fake_io = {}
function fake_io.open(path, mode)
    local name = basename(path)
    assert(name == "command.txt" or name == "status.tmp" or name == "status.json", "Unexpected bridge path")
    if mode == "rb" and files[name] == nil then return nil end
    local file = { closed = false, data = mode == "wb" and "" or files[name] }
    function file:read(limit)
        assert(not self.closed)
        return type(limit) == "number" and self.data:sub(1, limit) or self.data
    end
    function file:write(text)
        assert(not self.closed)
        self.data = self.data .. text
        return self
    end
    function file:close()
        if mode == "wb" then files[name] = self.data end
        self.closed = true
        return true
    end
    return file
end
local fake_os = { time = function() return now end }
function fake_os.remove(path) files[basename(path)] = nil; return true end
function fake_os.rename(from, to)
    local old_name, new_name = basename(from), basename(to)
    assert(files[old_name] ~= nil, "Status temporary file was not written")
    files[new_name], files[old_name] = files[old_name], nil
    return true
end
local env = setmetatable({ io = fake_io, os = fake_os,
    Key = { F1 = 1, F2 = 2, F3 = 3, F4 = 4, F5 = 5, F6 = 6, F10 = 10 },
    print = function(message) messages[#messages + 1] = tostring(message) end }, { __index = _G })
function env.dofile(path)
    if basename(path) == "protocol.lua" then return protocol end
    if basename(path) == "bindings.lua" then return { new = function() return trainer end } end
    error("Unexpected main.lua dependency: " .. path)
end
function env.LoopAsync(interval, callback)
    equal(interval, 250)
    assert(not interval_callback, "Polling loop registered twice")
    interval_callback = callback
end
function env.ExecuteInGameThread(callback) jobs[#jobs + 1] = callback end
function env.RegisterKeyBind(key, callback)
    assert(not keys[key], "Hotkey registered twice")
    keys[key] = callback
end
assert(loadfile("modules/astroneer/Scripts/main.lua", "t", env))()
local function flush()
    local maximum = 20
    while #jobs > 0 do
        maximum = maximum - 1
        assert(maximum >= 0, "Unbounded game-thread scheduling")
        game_thread = true
        table.remove(jobs, 1)()
        game_thread = false
    end
end
local function tick() interval_callback(); flush() end
local function press(key) keys[key](); flush() end
local function queue(action, id, enabled, session, created, multiplier)
    files["command.txt"] = table.concat({ "schema_version=1", "session_id=" .. (session or latest.session_id),
        "request_id=" .. string.format("%032x", id), "action=" .. action,
        "enabled=" .. (enabled and "1" or "0"), "created_utc=" .. tostring(created or now), "" }, "\n")
    if multiplier ~= nil then files["command.txt"] = files["command.txt"] .. "multiplier=" .. tostring(multiplier) .. "\n" end
end

equal(#state.apply_calls, 0, "Loading the module changes no game state")
equal(files["status.json"], nil)
interval_callback(); interval_callback()
equal(#jobs, 1, "Polling does not build up callbacks while one is pending")
flush()
equal(latest.version, "0.3.1")
equal(latest.ready, true)
equal(state.disable_count, 1, "Initial session always starts with toggles off")
equal(files["status.tmp"], nil, "Status publication finishes rename")
equal(type(files["status.json"]), "string")
equal(type(keys[1]), "function")
equal(type(keys[2]), "function")
equal(type(keys[3]), "function")
equal(type(keys[4]), "function")
equal(type(keys[5]), "function")
equal(type(keys[6]), "function")
equal(type(keys[10]), "function")

-- Stale queued actions are consumed without running a binding.
queue("add_bytes", 1, false, nil, now - 9)
tick()
equal(#state.apply_calls, 0)
equal(files["command.txt"], nil)
equal(state.bytes, 0)
queue("add_bytes", 1, false)
tick()
equal(state.bytes, 10000)
equal(latest.last_success, true)
local accepted_id = latest.last_command
queue("add_bytes", 1, false)
tick()
equal(state.bytes, 10000, "Duplicate one-shot ID is dispatched exactly once")
equal(#state.apply_calls, 1)
equal(latest.last_command, accepted_id)
equal(files["command.txt"], nil)

-- An old session cannot mutate the new character/save.
local first_session = latest.session_id
queue("unlimited_oxygen", 2, true)
state.key = "world-2/player-2/game"
tick()
equal(latest.session_id ~= first_session, true)
equal(#state.apply_calls, 1)
equal(latest.last_command, "")
equal(state.flags.unlimited_oxygen, false)
equal(state.disable_count, 2)
queue("unlimited_oxygen", 3, true)
tick()
equal(state.flags.unlimited_oxygen, true)
queue("disable_all", 4, false)
tick()
equal(state.flags.unlimited_oxygen, false)
equal(state.apply_calls[#state.apply_calls].action, "disable_all")

-- Each focused game shortcut creates one dispatch after game-thread refresh.
local before_key = #state.apply_calls
keys[1]()
equal(#state.apply_calls, before_key, "Hotkeys defer all engine mutations")
equal(#jobs, 1)
flush()
equal(#state.apply_calls, before_key + 1)
equal(state.apply_calls[#state.apply_calls].action, "resource_collection_10x")
equal(state.flags.resource_collection_10x, true)
equal(latest.enabled.resource_collection_10x, true)
press(1)
equal(#state.apply_calls, before_key + 2)
equal(state.flags.resource_collection_10x, false)
press(4)
equal(state.bytes, 20000, "F4 adds bytes despite not being a persistent toggle")
equal(state.apply_calls[#state.apply_calls].action, "add_bytes")
press(2); press(3)
equal(state.flags.unlimited_oxygen, true)
equal(state.flags.unlimited_power, true)
press(10)
equal(state.apply_calls[#state.apply_calls].action, "disable_all")
equal(state.flags.unlimited_oxygen, false)
equal(state.flags.unlimited_power, false)

-- Menu previews cannot receive queued or keyboard cheat actions. Emergency
-- off remains available there and the previous gameplay session is invalidated.
local game_session = latest.session_id
state.key, state.ready = "world-2/player-2/title-menu", false
tick()
equal(latest.ready, false)
equal(latest.session_id ~= game_session, true)
local before_menu = #state.apply_calls
queue("add_bytes", 5, false)
tick()
equal(#state.apply_calls, before_menu)
equal(latest.last_success, false)
equal(state.bytes, 20000)
press(1); press(4)
equal(#state.apply_calls, before_menu, "Menu F1/F4 are ignored")
press(10)
equal(#state.apply_calls, before_menu + 1)
equal(state.apply_calls[#state.apply_calls].action, "disable_all")
queue("disable_all", 6, false)
tick()
equal(#state.apply_calls, before_menu + 2)
equal(latest.last_success, true)

-- Polling failures turn everything off, publish the failure and release the
-- pending slot so a later successful lifecycle refresh can recover.
state.ready, state.key = true, "world-3/player-3/game"
tick(); press(2)
equal(state.flags.unlimited_oxygen, true)
state.refresh_error = true
tick()
equal(latest.ready, false)
equal(state.flags.unlimited_oxygen, false)
equal(latest.message:find("Mock transient lifecycle failure", 1, true) ~= nil, true)
state.refresh_error = false
tick()
equal(latest.ready, true)
equal(state.flags.unlimited_oxygen, false)
equal(#jobs, 0)

-- The protocol selection value reaches the binding as a number and is visible
-- in the same published status, without enabling the legacy/F1 toggle.
equal(latest.values.selected_collection_multiplier, 10)
queue("set_collection_multiplier", 20, false, nil, nil, 20)
tick()
equal(state.apply_calls[#state.apply_calls].multiplier, 20)
equal(state.apply_calls[#state.apply_calls].enabled, false)
equal(latest.values.selected_collection_multiplier, 20)
equal(latest.values.collection_multiplier, 1)
equal(latest.enabled.resource_collection_10x, false)
press(1)
equal(latest.values.collection_multiplier, 20)
equal(latest.enabled.resource_collection_10x, true)
press(1)
equal(latest.values.collection_multiplier, 1)
equal(latest.values.selected_collection_multiplier, 20)
press(10)
equal(latest.values.selected_collection_multiplier, 20)
press(1)
queue("set_collection_multiplier", 21, false, nil, nil, 10)
tick()
equal(latest.values.collection_multiplier, 10)
equal(latest.enabled.resource_collection_10x, true)
local before_selection_replay = #state.apply_calls
queue("set_collection_multiplier", 21, false, nil, nil, 20)
tick()
equal(#state.apply_calls, before_selection_replay)
equal(latest.values.selected_collection_multiplier, 10)
queue("set_collection_multiplier", 22, false, nil, nil, 20)
tick()
equal(latest.values.selected_collection_multiplier, 20)

-- A 100x selection has an acknowledgement and never implicitly enables the
-- collection toggle. Replays and expired requests cannot change that choice.
press(1)
queue("set_collection_multiplier", 30, false, nil, nil, 100)
tick()
equal(latest.last_command, string.format("%032x", 30))
equal(latest.last_success, true)
equal(latest.last_message, "Multiplier selected")
equal(state.apply_calls[#state.apply_calls].multiplier, 100)
equal(state.apply_calls[#state.apply_calls].enabled, false)
equal(latest.values.selected_collection_multiplier, 100)
equal(latest.values.collection_multiplier, 1)
equal(latest.enabled.resource_collection_10x, false)
local before_hundred_replay = #state.apply_calls
queue("set_collection_multiplier", 30, false, nil, nil, 10)
tick()
equal(#state.apply_calls, before_hundred_replay)
equal(latest.last_command, string.format("%032x", 30))
equal(latest.last_success, true)
equal(latest.values.selected_collection_multiplier, 100)
press(1)
equal(latest.values.collection_multiplier, 100)
equal(latest.enabled.resource_collection_10x, true)
queue("set_collection_multiplier", 31, false, nil, nil, 20)
tick()
equal(latest.values.collection_multiplier, 20)
queue("set_collection_multiplier", 32, false, nil, nil, 100)
tick()
equal(latest.last_command, string.format("%032x", 32))
equal(latest.last_success, true)
equal(latest.values.collection_multiplier, 100)
equal(latest.enabled.resource_collection_10x, true)
press(1)
equal(latest.values.collection_multiplier, 1)
equal(latest.values.selected_collection_multiplier, 100)
press(1); press(10)
equal(latest.values.selected_collection_multiplier, 100)
equal(latest.values.collection_multiplier, 1)
equal(latest.enabled.resource_collection_10x, false)
local before_expired_hundred = #state.apply_calls
queue("set_collection_multiplier", 33, false, nil, now - 9, 100)
tick()
equal(#state.apply_calls, before_expired_hundred)
equal(latest.last_command, string.format("%032x", 32), "Expired selection does not receive a success acknowledgement")
local previous_selection_session = latest.session_id
queue("set_collection_multiplier", 23, false, previous_selection_session, nil, 100)
state.key = "world-4/player-4/game"
tick()
equal(latest.values.selected_collection_multiplier, 10)
equal(latest.enabled.resource_collection_10x, false)
equal(latest.session_id ~= previous_selection_session, true)
local before_bad_selection = #state.apply_calls
queue("set_collection_multiplier", 24, true, nil, nil, 100)
tick()
equal(#state.apply_calls, before_bad_selection)
queue("set_collection_multiplier", 25, false, nil, nil, 15)
tick()
equal(#state.apply_calls, before_bad_selection)
queue("resource_collection_10x", 26, true, nil, nil, 100)
tick()
equal(#state.apply_calls, before_bad_selection)
state.ready, state.key = false, "world-4/player-4/menu"
tick()
queue("set_collection_multiplier", 27, false, nil, nil, 100)
tick()
equal(#state.apply_calls, before_bad_selection)
equal(latest.last_command, string.format("%032x", 27))
equal(latest.last_success, false)
equal(latest.values.selected_collection_multiplier, 10)

-- Run speed uses a separate selector, and F5/F6 affect only their own toggles.
state.ready, state.key = true, "world-5/player-5/game"
tick()
equal(latest.values.selected_run_speed_multiplier, 3)
equal(latest.values.run_speed_multiplier, 1)
equal(latest.enabled.invulnerability, false)
equal(latest.enabled.player_run_speed, false)
queue("set_collection_multiplier", 40, false, nil, nil, 100)
tick()
queue("set_run_speed_multiplier", 41, false, nil, nil, 5)
tick()
equal(latest.last_command, string.format("%032x", 41))
equal(latest.last_success, true)
equal(latest.last_message, "Run speed selected")
equal(state.apply_calls[#state.apply_calls].multiplier, 5)
equal(state.apply_calls[#state.apply_calls].enabled, false)
equal(latest.values.selected_collection_multiplier, 100)
equal(latest.values.selected_run_speed_multiplier, 5)
equal(latest.values.run_speed_multiplier, 1)
equal(latest.enabled.player_run_speed, false)
press(5)
equal(latest.enabled.invulnerability, true)
equal(latest.enabled.player_run_speed, false)
press(6)
equal(latest.enabled.player_run_speed, true)
equal(latest.values.run_speed_multiplier, 5)
equal(latest.enabled.invulnerability, true)
queue("set_run_speed_multiplier", 42, false, nil, nil, 10)
tick()
equal(latest.last_success, true)
equal(latest.values.run_speed_multiplier, 10)
equal(latest.values.selected_run_speed_multiplier, 10)
equal(latest.enabled.player_run_speed, true)
local before_speed_replay = #state.apply_calls
queue("set_run_speed_multiplier", 42, false, nil, nil, 3)
tick()
equal(#state.apply_calls, before_speed_replay)
equal(latest.values.selected_run_speed_multiplier, 10)
queue("set_run_speed_multiplier", 43, false, nil, now - 9, 3)
tick()
equal(#state.apply_calls, before_speed_replay)
equal(latest.last_command, string.format("%032x", 42))
press(6)
equal(latest.values.run_speed_multiplier, 1)
equal(latest.values.selected_run_speed_multiplier, 10)
equal(latest.enabled.invulnerability, true)
press(6); press(10)
equal(latest.enabled.player_run_speed, false)
equal(latest.enabled.invulnerability, false)
equal(latest.values.run_speed_multiplier, 1)
equal(latest.values.selected_run_speed_multiplier, 10)
equal(latest.values.selected_collection_multiplier, 100)
queue("invulnerability", 44, true)
tick()
equal(latest.last_success, true)
equal(latest.enabled.invulnerability, true)
queue("player_run_speed", 45, true)
tick()
equal(latest.last_success, true)
equal(latest.values.run_speed_multiplier, 10)
queue("set_run_speed_multiplier", 46, false, nil, nil, 3)
tick()
equal(latest.values.run_speed_multiplier, 3)
local before_invalid_speed = #state.apply_calls
for _, action in ipairs({ "invulnerability", "player_run_speed" }) do
    queue(action, 47, true, nil, nil, 3)
    tick()
    equal(#state.apply_calls, before_invalid_speed)
end
queue("set_run_speed_multiplier", 48, false, nil, nil, 20)
tick()
equal(#state.apply_calls, before_invalid_speed)
queue("set_run_speed_multiplier", 49, true, nil, nil, 5)
tick()
equal(#state.apply_calls, before_invalid_speed)
local prior_speed_session = latest.session_id
queue("set_run_speed_multiplier", 50, false, prior_speed_session, nil, 10)
state.key = "world-6/player-6/game"
tick()
equal(latest.session_id ~= prior_speed_session, true)
equal(latest.values.selected_run_speed_multiplier, 3)
equal(latest.values.run_speed_multiplier, 1)
equal(latest.values.selected_collection_multiplier, 10)
equal(latest.enabled.player_run_speed, false)
equal(latest.enabled.invulnerability, false)
equal(#state.apply_calls, before_invalid_speed)
state.ready, state.key = false, "world-6/player-6/menu"
tick()
press(5); press(6)
equal(#state.apply_calls, before_invalid_speed, "Menu F5/F6 cannot activate cheats")
queue("set_run_speed_multiplier", 51, false, nil, nil, 5)
tick()
equal(#state.apply_calls, before_invalid_speed)
equal(latest.last_command, string.format("%032x", 51))
equal(latest.last_success, false)
equal(latest.values.selected_run_speed_multiplier, 3)

-- A rejection may change the requested flag while the old effect awaits
-- restoration. The same failure acknowledgement must publish those new
-- diagnostics, and must not turn into success or replay the command.
state.ready, state.key = true, "world-7/player-7/game"
tick()
queue("set_run_speed_multiplier", 60, false, nil, nil, 5)
tick(); press(6)
equal(latest.enabled.player_run_speed, true)
equal(latest.values.restoration_pending, false)
state.fail_run_speed_off = true
queue("player_run_speed", 61, false)
tick()
equal(latest.last_command, string.format("%032x", 61))
equal(latest.last_success, false)
equal(latest.last_message, "Disabled; restore failed")
equal(latest.enabled.player_run_speed, false)
equal(latest.values.restoration_pending, true)
equal(latest.values.run_speed_multiplier, 5)
local before_failed_replay = #state.apply_calls
queue("player_run_speed", 61, false)
tick()
equal(#state.apply_calls, before_failed_replay)
equal(latest.last_success, false)
state.fail_run_speed_off = false
press(10); press(6)
equal(latest.values.restoration_pending, false)
state.fail_run_speed_off = true
press(6)
equal(latest.enabled.player_run_speed, false)
equal(latest.values.restoration_pending, true)
equal(latest.values.run_speed_multiplier, 5)
equal(latest.message, "Disabled; restore failed")
equal(latest.last_command, string.format("%032x", 61), "Hotkey rejection does not invent a new IPC acknowledgement")
equal(latest.last_success, false)
print("Astroneer main bridge/hotkeys: " .. checks .. " assertions passed")
