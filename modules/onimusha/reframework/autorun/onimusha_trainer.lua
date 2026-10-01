-- Onimusha WotS build 24769601: experimental trainer with bounded diagnostics.
-- Gameplay behavior must be verified in combat; metadata checks are structural.
local VERSION = "0.4.0"
local keys = {unlimited_health = 0x70, auto_parry = 0x71, one_hit_stun = 0x72, stun_immunity = 0x73}
local hotkeys = {unlimited_health = "F1", auto_parry = "F2", one_hit_stun = "F3", stun_immunity = "F4"}
local labels = {unlimited_health = "Unlimited Health [F1]", auto_parry = "Auto parry [F2]",
    one_hit_stun = "Enemy one hit stun [F3]", stun_immunity = "Player stun immunity [F4]"}
local order = {"unlimited_health", "auto_parry", "one_hit_stun", "stun_immunity"}
local names = {unlimited_health = "Unlimited Health", auto_parry = "Auto parry", one_hit_stun = "Enemy one hit stun",
    stun_immunity = "Player stun immunity"}
local hud_visible, hud_failed, notices = true, false, {}
-- ImGui/draw colors use packed ABGR. Text also spells out state for readability.
local colors = {on = 0xFF91E6A3, off = 0xFF91BBDD, fault = 0xFF9292FF,
    text = 0xFFF3F0EB, muted = 0xFFBDB8AF, background = 0xDB221D18, border = 0x805E554A}
local function notice(message, color)
    if #notices >= 3 then table.remove(notices, 1) end
    notices[#notices + 1] = {text = message, color = color or colors.text, expires = os.time() + 4}
end
local current, frames, key_down = nil, 0, {}
local session_id = tostring(os.time()) .. "-" .. tostring(math.floor(os.clock() * 1000000))
local statuses, counters, errors, events, reported_errors = {}, {}, {}, {}, {}
local module_callbacks = {}
local ctx = {enabled = {unlimited_health = false, auto_parry = false, one_hit_stun = false, stun_immunity = false}}
local last_command, alive = nil, true
local function clean(value)
    return tostring(value):gsub("0[xX]%x+", "<address>"):sub(1, 500)
end
function ctx.player() return alive and current or nil end
function ctx.same(a, b) return a ~= nil and b ~= nil and a == b end
function ctx.count(name) counters[name] = (counters[name] or 0) + 1 end
function ctx.event(name, data)
    if #events >= 100 then table.remove(events, 1) end
    events[#events + 1] = {frame = frames, name = name, data = data}
end
local function set_hud_visible(value)
    local visible = value == true
    if hud_visible == visible then return end
    hud_visible = visible
    ctx.event("hud", {visible = visible})
    notice("Trainer status: " .. (visible and "VISIBLE" or "HIDDEN"), colors.muted)
end
function ctx.error(feature, detail)
    ctx.enabled[feature] = false
    if reported_errors[feature] then return end
    reported_errors[feature] = true
    if #errors < 30 then errors[#errors + 1] = {feature = feature, detail = clean(detail)} end
    if statuses[feature] then statuses[feature].fault = clean(detail) end
    notice((names[feature] or "Trainer") .. ": unavailable", colors.fault)
    log.error("[Onimusha trainer] " .. feature .. ": " .. clean(detail))
end
local function disable_all(show_message)
    local changed = false
    for _, key in ipairs(order) do
        if ctx.enabled[key] then changed = true end
        ctx.enabled[key] = false
    end
    if changed or show_message then notice("All trainer features: OFF", colors.off) end
end
local function set_enabled(key, value)
    if statuses[key] and statuses[key].installed and not statuses[key].fault then
        local previous = ctx.enabled[key]
        ctx.enabled[key] = value == true
        ctx.event("toggle", {feature = key, enabled = ctx.enabled[key]})
        if previous ~= ctx.enabled[key] then
            notice(names[key] .. ": " .. (ctx.enabled[key] and "ON" or "OFF"),
                ctx.enabled[key] and colors.on or colors.off)
        end
    else
        notice((names[key] or key) .. ": unavailable", colors.fault)
    end
end
local function render_hud()
    if not draw or not imgui or hud_failed then return end
    local now = os.time()
    for i = #notices, 1, -1 do if notices[i].expires <= now then table.remove(notices, i) end end
    local display = imgui.get_display_size()
    local font_size = imgui.get_default_font_size()
    local pad, row = math.max(12, font_size * 0.7), math.max(24, font_size * 1.4)
    local width = 310
    for _, key in ipairs(order) do
        width = math.max(width, imgui.calc_text_size("[" .. hotkeys[key] .. "] " .. names[key] .. "   UNAVAILABLE").x + pad * 2)
    end
    local x, y = math.max(12, display.x - width - 24), 24
    if hud_visible then
        local height = row * (#order + 2) + pad * 2
        draw.filled_rect(x, y, width, height, colors.background)
        draw.outline_rect(x, y, width, height, colors.border)
        draw.text("ONIMUSHA TRAINER", x + pad, y + pad, colors.text)
        for i, key in ipairs(order) do
            local available = statuses[key] and statuses[key].installed and not statuses[key].fault
            local state = not available and "UNAVAILABLE" or (ctx.enabled[key] and "ON" or "OFF")
            local color = not available and colors.fault or (ctx.enabled[key] and colors.on or colors.off)
            draw.text("[" .. hotkeys[key] .. "] " .. names[key], x + pad, y + pad + row * i, colors.text)
            draw.text(state, x + width - pad - imgui.calc_text_size(state).x, y + pad + row * i, color)
        end
        draw.text("F5  All off    F6  Hide status", x + pad, y + pad + row * (#order + 1), colors.muted)
        y = y + height + 10
    end
    for _, item in ipairs(notices) do
        local w = math.max(width, imgui.calc_text_size(item.text).x + pad * 2)
        local toast_x = math.max(12, display.x - w - 24)
        draw.filled_rect(toast_x, y, w, row + pad * 2, colors.background)
        draw.filled_rect(toast_x, y, 4, row + pad * 2, item.color)
        draw.text(item.text, toast_x + pad, y + pad, item.color)
        y = y + row + pad * 2 + 8
    end
end
local function exact_method(type_name, signature, result_name, arity)
    local def = sdk.find_type_definition(type_name)
    assert(def, "Missing type " .. type_name)
    local method = def:get_method(signature)
    assert(method and not method:is_static() and method:get_num_params() == arity and
        method:get_return_type():get_full_name() == result_name, "Signature mismatch: " .. type_name .. "." .. signature)
    return method
end

local getters = {}
local function refresh_player()
    -- Drop the prior snapshot before traversing scene state. Never reuse it on failure.
    current = nil
    local manager = sdk.get_managed_singleton("app.PlayerManager")
    if not manager then return end
    local info = getters.player:call(manager)
    if not info or info:get_field("<IsControl>k__BackingField") ~= true then return end
    local character = info:get_field("<Character>k__BackingField")
    local entity = info:get_field("<CharacterEntity>k__BackingField")
    local context = info:get_field("_ContextHolder")
    if not character or not entity or not context or context:get_field("_Valid") ~= true then return end
    if character:call("get_IsAnyDestroyState()") then return end
    local chara = getters.chara:call(context)
    local health = chara and chara:get_field("<HealthManager>k__BackingField")
    if not health or health:get_type_definition():get_full_name() ~= "app.cHealthManager" then return end
    current = {info = info, character = character, entity = entity, health = health}
end

local function int32(value)
    local low = sdk.to_int64(value) & 0xFFFFFFFF
    return low >= 0x80000000 and low - 0x100000000 or low
end
local function install_health()
    local definitions = {
        {"addHealth(System.Int32, System.Boolean)", 2, "add"},
        {"setHealth(System.Int32)", 1, "set"},
        {"set_Health(System.Int32)", 1, "set"},
        {"setHealthNormalized(System.Single)", 1, "normalized"},
    }
    local resolved, seen = {}, {}
    for _, entry in ipairs(definitions) do
        local method = exact_method("app.cHealthManager", entry[1], "System.Void", entry[2])
        local address = tostring(method:get_function())
        if seen[address] then
            assert(seen[address] == entry[3], "Incompatible merged health methods")
        else
            seen[address] = entry[3]
            resolved[#resolved + 1] = {method = method, kind = entry[3], signature = entry[1]}
        end
    end
    for _, entry in ipairs(resolved) do
        sdk.hook(entry.method, function(args)
            local p = ctx.player()
            -- Trivial setters may share machine code with unrelated types. Compare
            -- the raw receiver with the known health object before reading anything.
            if not p then return end
            local ok, detail = pcall(function()
                if sdk.to_int64(args[2]) ~= p.health:get_address() then return end
                local storage = thread.get_hook_storage()
                local hp = p.health:get_field("<Health>k__BackingField")
                local maximum = p.health:get_field("<MaxHealth>k__BackingField")
                local incoming = entry.kind == "normalized" and sdk.to_float(args[3]) or int32(args[3])
                storage.onimusha_health = {object = p.health, before = hp, input = incoming,
                    signature = entry.signature, changed = false}
                ctx.count("health.calls")
                if not ctx.enabled.unlimited_health or hp <= 0 or maximum <= 0 or
                    p.health:get_field("_IsHealthZeroTrigger") ~= false then return end
                if entry.kind == "add" then
                    if incoming < 0 then args[3] = sdk.to_ptr(0); storage.onimusha_health.changed = true end
                elseif entry.kind == "normalized" then
                    if incoming < 1.0 then args[3] = sdk.float_to_ptr(1.0); storage.onimusha_health.changed = true end
                elseif incoming < maximum then
                    args[3] = sdk.to_ptr(maximum); storage.onimusha_health.changed = true
                end
                if storage.onimusha_health.changed then ctx.count("health.prevented") end
            end)
            if not ok then ctx.error("unlimited_health", detail) end
        end, function(retval)
            local ok, detail = pcall(function()
                local item = thread.get_hook_storage().onimusha_health
                if item then
                    local after = item.object:get_field("<Health>k__BackingField")
                    if item.changed or after ~= item.before then
                        ctx.event("health.change", {method = item.signature, before = item.before,
                            after = after, requested = item.input, prevented = item.changed})
                    end
                end
            end)
            if not ok then ctx.error("unlimited_health", detail) end
            return retval
        end)
    end
    return {installed = true, experimental = true, hooks = #resolved}
end

local function telemetry()
    local player = {ready = current ~= nil}
    if current then
        local ok, hp, maximum = pcall(function()
            return current.health:get_field("<Health>k__BackingField"), current.health:get_field("<MaxHealth>k__BackingField")
        end)
        if ok then player.health = hp; player.maximum_health = maximum end
        local immunity = module_callbacks.stun_immunity
        if immunity and immunity.snapshot and not statuses.stun_immunity.fault then
            local read_ok, snapshot = pcall(immunity.snapshot)
            if read_ok then player.rikido = snapshot end
        end
    end
    return {schema_version = 1, version = VERSION, session_id = session_id, last_command = last_command or "",
        frame = frames, game = reframework:get_game_name(),
        intended_steam_build = "24769601", behavior_verified = false, player = player,
        hud = {visible = hud_visible, notifications = #notices, failed = hud_failed},
        enabled = ctx.enabled, features = statuses, counters = counters, errors = errors, events = events}
end
local function export()
    local ok, result = pcall(json.dump_file, "onimusha_trainer_status.json", telemetry(), 2)
    if not ok or result ~= true then log.error("[Onimusha trainer] Status export failed") end
end
local function read_command()
    local ok, cmd = pcall(json.load_file, "onimusha_trainer_command.json")
    if not ok or type(cmd) ~= "table" or cmd.session_id ~= session_id or
        type(cmd.request_id) ~= "string" or cmd.request_id == last_command then return end
    if cmd.disable_all == true then disable_all(true) end
    if type(cmd.enabled) == "table" then
        for _, key in ipairs(order) do
            if type(cmd.enabled[key]) == "boolean" then set_enabled(key, cmd.enabled[key]) end
        end
    end
    if type(cmd.hud_visible) == "boolean" then set_hud_visible(cmd.hud_visible) end
    last_command = cmd.request_id
    ctx.event("command", {request_id = last_command})
    return true
end

local boot_ok, boot_error = pcall(function()
    assert(reframework:get_game_name() == "onimusha_wots" and sdk.get_tdb_version() == 82, "Unsupported game or TDB")
    getters.player = exact_method("app.PlayerManager", "getControllingPlayer()", "app.cPlayerManageInfo", 0)
    getters.chara = exact_method("app.cGameContextHolder", "get_Chara()", "app.cCharacterContextParam", 0)
    exact_method("app.CharacterBase", "get_IsAnyDestroyState()", "System.Boolean", 0)
    local health_type = assert(sdk.find_type_definition("app.cHealthManager"))
    for field_name, type_name in pairs({["<Health>k__BackingField"] = "System.Int32",
        ["<MaxHealth>k__BackingField"] = "System.Int32", _IsHealthZeroTrigger = "System.Boolean"}) do
        local field = health_type:get_field(field_name)
        assert(field and not field:is_static() and field:get_type():get_full_name() == type_name,
            "Health field mismatch: " .. field_name)
    end
    for _, entry in ipairs({{"unlimited_health", install_health},
        {"auto_parry", function() return require("onimusha.modules.parry").install(ctx) end},
        {"one_hit_stun", function() return require("onimusha.modules.stun").install(ctx) end},
        {"stun_immunity", function() return require("onimusha.modules.player_stun").install(ctx) end}}) do
        local ok, result, reason = pcall(entry[2])
        if ok and type(result) == "table" and result.installed then
            statuses[entry[1]] = result
            if type(reason) == "table" then module_callbacks[entry[1]] = reason end
        else
            local detail = ok and (reason or (type(result) == "table" and (result.detail or result.error))) or result
            statuses[entry[1]] = {installed = false, fault = clean(detail or "Module installation failed")}
            ctx.error(entry[1], detail or "Module installation failed")
        end
    end
end)
if not boot_ok then ctx.error("initialization", boot_error) end

re.on_application_entry("UpdateBehavior", function()
    frames = frames + 1
    if boot_ok then
        local ok, detail = pcall(refresh_player)
        if not ok then current = nil; disable_all(); if frames % 120 == 0 then ctx.error("player", detail) end end
    end
    for key, callbacks in pairs(module_callbacks) do
        if callbacks.update and not statuses[key].fault then
            local ok, detail = pcall(callbacks.update)
            if not ok then ctx.error(key, detail) end
        end
    end
    if frames % 30 == 0 then
        if read_command() then export() end
        -- Refill a living, already wounded player; lethal damage is handled by hooks.
        if current and ctx.enabled.unlimited_health then
            local ok, detail = pcall(function()
                local hp = current.health:get_field("<Health>k__BackingField")
                local maximum = current.health:get_field("<MaxHealth>k__BackingField")
                if hp > 0 and maximum > hp and current.health:get_field("_IsHealthZeroTrigger") == false then
                    current.health:call("setHealth(System.Int32)", maximum)
                end
            end)
            if not ok then ctx.error("unlimited_health", detail) end
        end
    end
    if frames % 120 == 0 then export() end
end)
re.on_frame(function()
    for _, key in ipairs(order) do
        local down = reframework:is_key_down(keys[key])
        if down and not key_down[key] then set_enabled(key, not ctx.enabled[key]) end
        key_down[key] = down
    end
    local down = reframework:is_key_down(0x74)
    if down and not key_down.disable then disable_all(true) end
    key_down.disable = down
    local hud_key = reframework:is_key_down(0x75)
    if hud_key and not key_down.hud then
        set_hud_visible(not hud_visible)
    end
    key_down.hud = hud_key
    local ok, detail = pcall(render_hud)
    if not ok and not hud_failed then
        hud_failed = true
        log.error("[Onimusha trainer] HUD rendering failed: " .. clean(detail))
    end
end)
re.on_draw_ui(function()
    if not imgui.tree_node("Onimusha Trainer") then return end
    imgui.text("Experimental build " .. VERSION)
    imgui.text(current and "Player connected" or "Waiting for a living game scene")
    for _, key in ipairs(order) do
        local status = statuses[key]
        if status and status.installed and not status.fault then
            local changed, value = imgui.checkbox(labels[key], ctx.enabled[key])
            if changed then set_enabled(key, value) end
        else imgui.text(labels[key] .. ": unavailable - " .. (status and status.fault or "initialization failed")) end
    end
    if imgui.button("Disable all [F5]") then disable_all(true) end
    local hud_changed, hud_value = imgui.checkbox("Show in-game status [F6]", hud_visible)
    if hud_changed then set_hud_visible(hud_value) end
    imgui.text("Combat behavior is under validation. Features start off after each reset.")
    if current then
        local hp = current.health:get_field("<Health>k__BackingField")
        local maximum = current.health:get_field("<MaxHealth>k__BackingField")
        imgui.text("Health: " .. tostring(hp) .. " / " .. tostring(maximum))
    end
    imgui.text("Health changes prevented: " .. tostring(counters["health.prevented"] or 0))
    if #errors > 0 then imgui.text("Last error: " .. errors[#errors].detail) end
    imgui.tree_pop()
end)
re.on_script_reset(function()
    alive = false; disable_all(); current = nil
end)
-- The JSON loader logs missing files; initialize the local command mailbox once.
json.dump_file("onimusha_trainer_command.json", {session_id = session_id, request_id = "boot"}, 2)
notice("Trainer loaded - all features OFF", colors.muted)
export()
