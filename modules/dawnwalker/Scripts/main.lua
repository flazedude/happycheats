-- HappyCheats Dawnwalker 0.3.3. Engine access is scheduled on the game thread.
local source = debug.getinfo(1, "S").source:gsub("^@", ""):gsub("\\", "/")
local scripts = assert(source:match("^(.*)/[^/]+$"), "Cannot locate trainer scripts")
local bridge = scripts .. "/../bridge/"
local P = dofile(scripts .. "/protocol.lua")
local Bindings = dofile(scripts .. "/bindings.lua")
local trainer = Bindings.new()
local boot = tostring(os.time()) .. "-" .. tostring({}):gsub("[^%w]", "")
local serial, identity, session = 0, nil, boot .. "-0"
local seen = {}
local last_command, last_success, last_message = "", false, ""
local status = {schema_version=1, version="0.3.3", session_id=session, ready=false,
    message="Dawnwalker trainer starting; waiting for the game thread", enabled={}, values={}, available={}}

local flight_timer
local function stop_flight_timer()
    if not flight_timer then return true end
    -- This pinned UE4SS build discards callback return values. Only cancelling
    -- its returned handle removes a loop; returning true leaves it running.
    local ok, removed = pcall(CancelDelayedAction, flight_timer.handle)
    if ok and removed then flight_timer = nil; return true end
    -- Keep the handle on failure so refresh cannot allocate a duplicate loop.
    return false
end
local marker_hooks_attempted, marker_hooks_ready = false, false
local function register_marker_hooks()
    if marker_hooks_attempted then return end
    marker_hooks_attempted = true
    local function failed(reason)
        marker_hooks_ready = false
        -- Pure Lua cleanup only: a partial or broken observer must not leave
        -- automatic travel armed, and later hook callbacks stay inert.
        pcall(function() trainer:set_marker_observation_ready(false) end)
        pcall(function() trainer:forget_marker(nil) end)
        print("[HappyCheats] Map marker observation failed: " .. tostring(reason) .. "\n")
    end
    local ok, reason = pcall(function()
        local prefix = "/Script/DogwoodMap.MappinSystemBlueprintLibrary:"
        RegisterHook(prefix .. "SetUserMappin", function() end, function(_, map_param, location_param)
            if not marker_hooks_ready then return end
            local captured, error = pcall(function()
                local map, location = map_param:get(), location_param:get()
                -- Copy the struct during its native call; parameter wrappers
                -- expire when the hook returns and must never be retained.
                trainer:observe_marker(map, {X=location.X, Y=location.Y, Z=location.Z})
            end)
            if not captured then failed(error) end
        end)
        RegisterHook(prefix .. "UnsetUserMappin", function() end, function(_, map_param)
            if not marker_hooks_ready then return end
            local captured, error = pcall(function() trainer:forget_marker(map_param:get()) end)
            if not captured then failed(error) end
        end)
    end)
    marker_hooks_ready = ok
    if ok then trainer:set_marker_observation_ready(true) else failed(reason) end
end
local function refresh()
    status.ready, status.message, status.values, status.available = trainer:refresh()
    local current = trainer:session_key()
    if identity ~= current then
        identity = current; serial = serial + 1; session = boot .. "-" .. serial
        seen = {}
        last_command, last_success, last_message = "", false, ""
        trainer:disable_all()
    end
    if status.ready then register_marker_hooks() end
    if not trainer:enabled().dawnwalker_fly then stop_flight_timer() end
    if trainer:enabled().dawnwalker_fly and not flight_timer then
        local timer = {}
        flight_timer = timer
        -- High-frequency work exists only while flight is enabled. It touches
        -- no files and is removed on disable instead of adding an idle timer.
        local scheduled, handle = pcall(LoopInGameThreadWithDelay, 16, function()
            if flight_timer ~= timer then return end
            if not trainer:enabled().dawnwalker_fly then stop_flight_timer(); return end
            local ok, success = pcall(function() return trainer:tick() end)
            if not ok or not success then
                pcall(function() trainer:disable_all() end)
                stop_flight_timer()
            end
        end)
        if not scheduled then flight_timer = nil; error(handle) end
        timer.handle = handle
    end
end
local published_at, published_controls
local function publish()
    status.session_id, status.enabled = session, trainer:enabled()
    status.last_command, status.last_success, status.last_message = last_command, last_success, last_message
    local now = os.time()
    -- Keep resource maintenance at 250 ms, but avoid disk writes on every poll.
    -- Control/session changes and acknowledgements are still published at once;
    -- continuously changing resource values are sampled by the 1 Hz heartbeat.
    local controls = P.json({session=session, ready=status.ready, message=status.message,
        enabled=status.enabled, available=status.available, last_command=last_command,
        last_success=last_success, last_message=last_message})
    if now == published_at and controls == published_controls then return end
    local file = io.open(bridge .. "status.tmp", "wb")
    if not file then return end
    local wrote = file:write(P.json(status))
    local closed = file:close()
    if not wrote or not closed then return end
    -- Removing the old file invalidates our committed-publication cache. If
    -- rename fails and controls return to their previous values this second,
    -- the next poll must still recreate status.json instead of suppressing it.
    published_at, published_controls = nil, nil
    os.remove(bridge .. "status.json")
    if os.rename(bridge .. "status.tmp", bridge .. "status.json") then
        published_at, published_controls = now, controls
    end
end
local first_tick = true
local function tick()
    if first_tick then
        first_tick = false
        status.message = "Dawnwalker game thread active; waiting for a player"
        publish()
        print("[HappyCheats] Dawnwalker first game-thread poll entered.\n")
    end
    refresh()
    -- A command expires after eight seconds. Keep duplicate IDs longer than
    -- that window without rotating a live session or erasing acknowledgements.
    local now = os.time()
    for id, created in pairs(seen) do if now - created > 10 then seen[id] = nil end end
    local file = io.open(bridge .. "command.txt", "rb")
    if file then
        local raw = file:read(2049); file:close()
        -- Remove before applying; a failed removal must never repeat a one-shot.
        local removed = os.remove(bridge .. "command.txt")
        local command, reason = P.parse(raw, session, now)
        if removed and command and not seen[command.request_id] then
            seen[command.request_id] = tonumber(command.created_utc)
            last_command = command.request_id
            local disabling = command.enabled == false and trainer:enabled()[command.action] ~= nil
            if not status.ready and command.action ~= "disable_all" and not disabling then
                last_success, last_message = false, status.message
            else
                local ok, success, message = pcall(function() return trainer:apply(command.action, command.enabled) end)
                last_success = ok and success == true
                last_message = ok and tostring(message) or "Control failed: " .. tostring(success)
                if not ok then trainer:disable_all() end
                refresh()
            end
        elseif reason then
            print("[HappyCheats] Ignored command: " .. reason .. "\n")
        end
    end
    -- Marker POST hooks only copy a pending request. Dispatch once here, after
    -- commands, so off/disable-all can cancel before travel changes the world.
    -- nil means idle or waiting for player control; a boolean means consumed.
    local automatic, message = trainer:process_auto_teleport()
    if automatic ~= nil then
        refresh()
        if not automatic then print("[HappyCheats] " .. tostring(message) .. "\n") end
    end
    publish()
end
local function guarded(work)
    local ok, error = pcall(work)
    if not ok then
        pcall(function() trainer:disable_all() end)
        stop_flight_timer()
        status.ready = false
        status.message = "Dawnwalker trainer paused: " .. tostring(error)
        pcall(publish)
        print("[HappyCheats] " .. status.message .. "\n")
    end
end
-- This initial heartbeat uses only Lua state and files. It remains visible if
-- engine scheduling cannot start, instead of silently waiting for a first tick.
pcall(publish)
guarded(function()
    NotifyOnNewObject("/Script/Dawnwalker.DawnwalkerPlayerCharacter", function(character)
        -- Constructors can notify before properties/components are initialized.
        -- Retain the wrapper only; the timer checks ownership and liveness later.
        trainer:observe(character)
    end)
    -- One persistent game-thread timer avoids sharing trainer state with an
    -- asynchronous Lua loop and never queues a backlog while loading or paused.
    LoopInGameThreadWithDelay(250, function() guarded(tick); return false end)
end)
-- UE4SS dispatches keyboard shortcuts only while the game is focused. F6 is
-- owned by the companion overlay; gamepad shortcuts are owned by HappyCheats.
for _, shortcut in ipairs({{Key.F1,"dawnwalker_health"}, {Key.F2,"dawnwalker_stamina"},
    {Key.F3,"dawnwalker_refill"}, {Key.F4,"dawnwalker_blood"}, {Key.F5,"dawnwalker_invulnerable"},
    {Key.F7,"dawnwalker_teleport"}, {Key.F8,"dawnwalker_damage"}, {Key.F9,"dawnwalker_invisible"},
    {Key.F10,"disable_all"}, {Key.F11,"dawnwalker_fly"}, {Key.F12,"dawnwalker_carry"},
    {Key.NUM_ONE,"dawnwalker_money"}, {Key.NUM_TWO,"dawnwalker_items"}, {Key.NUM_THREE,"dawnwalker_xp"},
    {Key.NUM_FOUR,"dawnwalker_auto_teleport"}}) do
    local action = shortcut[2]
    RegisterKeyBind(shortcut[1], function()
        ExecuteInGameThread(function()
            guarded(function()
                refresh()
                local was_enabled = trainer:enabled()[action] == true
                if not status.ready and action ~= "disable_all" and not was_enabled then publish(); return end
                local enabled = trainer:enabled()[action] == false
                local success, message = trainer:apply(action, enabled)
                refresh()
                status.message = tostring(message)
                publish()
                if not success then print("[HappyCheats] " .. tostring(message) .. "\n") end
            end)
        end)
    end)
end
print("[HappyCheats] Dawnwalker 0.3.3 loaded. All toggles start off.\n")
