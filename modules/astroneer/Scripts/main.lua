-- HappyCheats Astroneer 0.1.3. All Unreal access occurs on the game thread.
local source = debug.getinfo(1, "S").source:gsub("^@", ""):gsub("\\", "/")
local scripts = assert(source:match("^(.*)/[^/]+$"), "Cannot locate trainer scripts")
local bridge = scripts .. "/../bridge/"
local P = dofile(scripts .. "/protocol.lua")
local Bindings = dofile(scripts .. "/bindings.lua")
local trainer = Bindings.new()
local serial = 0
local boot = tostring(os.time()) .. "-" .. tostring({}):gsub("[^%w]", "")
local session, identity = boot .. "-0", nil
local last_command, last_success, last_message = "", false, ""
local seen = {}
local status = {schema_version=1, version="0.1.3", session_id=session, ready=false,
    message="Connected · load a world hosted on this PC to use Astroneer controls.", enabled={}, values={}, available={}}

local function publish()
    status.session_id = session
    status.enabled = trainer:enabled()
    status.last_command, status.last_success, status.last_message = last_command, last_success, last_message
    -- Windows rename cannot replace an existing file; readers tolerate the brief replacement gap.
    local f = io.open(bridge .. "status.tmp", "wb")
    if not f then return end
    f:write(P.json(status)); f:close()
    os.remove(bridge .. "status.json")
    os.rename(bridge .. "status.tmp", bridge .. "status.json")
end
local function tick()
    status.ready, status.message, status.values, status.available = trainer:refresh()
    local current = trainer:session_key()
    if identity ~= current then
        identity = current; serial = serial + 1; session = boot .. "-" .. serial
        seen = {}; last_command, last_success, last_message = "", false, ""
        trainer:disable_all()
    end
    local f = io.open(bridge .. "command.txt", "rb")
    if f then
        local raw = f:read(2049); f:close()
        -- Consume before applying. Retried/duplicate requests cannot repeat a one-shot.
        os.remove(bridge .. "command.txt")
        local command, reason = P.parse(raw, session, os.time())
        if command and not seen[command.request_id] then
            seen[command.request_id] = true
            last_command = command.request_id
            if not status.ready and command.action ~= "disable_all" then
                last_success, last_message = false, status.message
            else
                last_success, last_message = trainer:apply(command.action, command.enabled, command.multiplier)
                if last_success then
                    status.ready, status.message, status.values, status.available = trainer:refresh()
                end
            end
        elseif reason then
            print("[HappyCheats] Ignored command: " .. reason .. "\n")
        end
    end
    publish()
end
local pending = false
LoopAsync(250, function()
    if pending then return false end
    pending = true
    ExecuteInGameThread(function()
        local ok, err = pcall(tick)
        if not ok then
            pcall(function() trainer:disable_all() end)
            status.ready = false; status.message = "Astroneer trainer paused: " .. tostring(err)
            pcall(publish)
            print("[HappyCheats] " .. status.message .. "\n")
        end
        pending = false
    end)
    return false
end)
-- UE4SS delivers these only when the game has focus. The launcher handles its
-- own focused shortcuts, so one press cannot toggle twice across both windows.
for _, shortcut in ipairs({ {Key.F1, "resource_collection_10x"}, {Key.F2, "unlimited_oxygen"},
    {Key.F3, "unlimited_power"}, {Key.F4, "add_bytes"}, {Key.F10, "disable_all"} }) do
    local action = shortcut[2]
    RegisterKeyBind(shortcut[1], function()
        ExecuteInGameThread(function()
            local ok, err = pcall(function()
                tick() -- Refresh lifecycle/session before accepting a key.
                if not status.ready and action ~= "disable_all" then return end
                local flags = trainer:enabled()
                local success, message = trainer:apply(action, flags[action] == false)
                if success then
                    status.ready, status.message, status.values, status.available = trainer:refresh()
                end
                status.message = message
                publish()
                if not success then print("[HappyCheats] " .. tostring(message) .. "\n") end
            end)
            if not ok then print("[HappyCheats] Shortcut failed: " .. tostring(err) .. "\n") end
        end)
    end)
end
print("[HappyCheats] Astroneer 0.1.3 loaded; all cheats start off.\n")
