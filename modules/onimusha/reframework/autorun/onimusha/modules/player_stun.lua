-- Controlled-player Rikido depletion protection, Steam build 24769601.
-- Symbols are verified against reports/onimusha_discovery.json. Scripted damage
-- reactions are not bypassed; the native gauge path and return values remain.
local M = {}
local FEATURE, STORAGE = "stun_immunity", "onimusha_player_rikido"
local installed_status, installed_callbacks

local function int32(value)
    local low = sdk.to_int64(value) & 0xFFFFFFFF
    return low >= 0x80000000 and low - 0x100000000 or low
end

local function exact_method(type_name, signature, result, count)
    local def = assert(sdk.find_type_definition(type_name), "Missing type " .. type_name)
    local method = assert(def:get_method(signature), "Missing method " .. signature)
    assert(not method:is_static() and method:get_num_params() == count
        and method:get_return_type():get_full_name() == result, "Signature mismatch: " .. signature)
    local pointer = method:get_function()
    assert(pointer and sdk.to_int64(pointer) ~= 0, "Missing function: " .. signature)
    return method, tostring(sdk.to_int64(pointer))
end

local function exact_field(type_name, field_name, result)
    local def = assert(sdk.find_type_definition(type_name), "Missing type " .. type_name)
    local field = assert(def:get_field(field_name), "Missing field " .. field_name)
    assert(not field:is_static() and field:get_type():get_full_name() == result,
        "Field mismatch: " .. type_name .. "." .. field_name)
end

function M.install(ctx)
    if installed_status then return installed_status, installed_callbacks end
    assert(thread and thread.get_hook_storage, "REFramework hook storage is required")
    local function report(detail) pcall(ctx.error, FEATURE, tostring(detail)) end
    local candidates, shared_supporter = {}, false
    local valid, validation_error = pcall(function()
        local signature = "applyDamage(System.Int32, System.Boolean, System.Boolean)"
        local damage_method, damage_address = exact_method("app.cCharacterRikidoSupporter", signature, "System.Boolean", 3)
        local _, enemy_address = exact_method("app.cEnemyRikidoSupporter", signature, "System.Boolean", 3)
        -- Never register a second hook over the confirmed working enemy hook.
        shared_supporter = damage_address == enemy_address
        if not shared_supporter then
            candidates[#candidates + 1] = {method = damage_method, address = damage_address,
                kind = "damage", signature = signature}
        end
        for _, entry in ipairs({{"addValue", "add"}, {"addValueLeaveOne", "add"}, {"setValue", "set"}}) do
            local sig = entry[1] .. "(System.Int32, System.Boolean)"
            local method, native_address = exact_method("app.cRikidoGuage", sig, "System.Void", 2)
            assert(native_address ~= enemy_address, "Player gauge method shares the enemy damage function")
            candidates[#candidates + 1] = {method = method, address = native_address,
                kind = entry[2], signature = sig}
        end
        exact_method("app.cCharacterRikidoSupporter", "setRikidoValueFromRate(System.Single)", "System.Void", 1)
        for _, entry in ipairs({
            {"app.cCharacterEntityBase", "<RikidoSupporter>k__BackingField", "app.cCharacterRikidoSupporter"},
            {"app.cCharacterModuleBase", "_OwnerCharacter", "app.CharacterBase"},
            {"app.cCharacterRikidoSupporter", "_Guage", "app.cRikidoGuage"},
            {"app.cCharacterRikidoSupporter", "_IsRikidoBreak", "System.Boolean"},
            {"app.cRikidoGuage", "_OwnerCharacter", "app.CharacterBase"},
            {"app.cRikidoGuage", "_CurrentValue", "System.Int32"},
            {"app.cRikidoGuage", "_MaxValue", "System.Int32"},
            {"app.cHealthManager", "<Health>k__BackingField", "System.Int32"},
            {"app.cHealthManager", "_IsHealthZeroTrigger", "System.Boolean"},
        }) do exact_field(entry[1], entry[2], entry[3]) end
        assert(sdk.find_type_definition("app.cPlayerRikidoSupporter"), "Missing player Rikido supporter")
        assert(sdk.find_type_definition("app.cPlayerRikidoGuage"), "Missing player Rikido gauge")
    end)
    if not valid then report(validation_error); return nil, tostring(validation_error) end

    -- Resolve live references per operation. Nothing is cached across behavior
    -- ticks or scene/player changes; hook storage alone retains a gauge until post.
    local function selected_player()
        local player = ctx.player()
        if not player or not player.character or not player.entity or not player.health then return end
        local hp = player.health:get_field("<Health>k__BackingField")
        if type(hp) ~= "number" or hp <= 0 or player.health:get_field("_IsHealthZeroTrigger") ~= false then return end
        local supporter = player.entity:get_field("<RikidoSupporter>k__BackingField")
        if not supporter or not supporter:get_type_definition():is_a("app.cPlayerRikidoSupporter")
            or not ctx.same(supporter:get_field("_OwnerCharacter"), player.character) then return end
        local gauge = supporter:get_field("_Guage")
        if not gauge or not gauge:get_type_definition():is_a("app.cPlayerRikidoGuage")
            or not ctx.same(gauge:get_field("_OwnerCharacter"), player.character) then return end
        local maximum, current = gauge:get_field("_MaxValue"), gauge:get_field("_CurrentValue")
        if type(maximum) ~= "number" or maximum <= 0 or maximum > 0x7FFFFFFF
            or type(current) ~= "number" then return end
        return {supporter = supporter, gauge = gauge, current = current, maximum = maximum}
    end

    local ready = false
    local function refill(selected)
        if selected.current >= selected.maximum then return end
        selected.supporter:call("setRikidoValueFromRate(System.Single)", 1.0)
        local after = selected.gauge:get_field("_CurrentValue")
        if after > selected.current then
            ctx.count("player_stun.refilled")
            ctx.event("player_stun.refill", {before = selected.current, after = after, maximum = selected.maximum})
        end
    end

    local function before(entry, args)
        local selected = selected_player()
        if not selected then return end
        local expected = entry.kind == "damage" and selected.supporter or selected.gauge
        -- Tiny engine methods can share native code with unrelated types. Compare
        -- raw receiver identity before accessing anything through the hook's args.
        if sdk.to_int64(args[2]) ~= expected:get_address() then return end
        local amount = int32(args[3])
        local item = {gauge = selected.gauge, supporter = selected.supporter,
            before = selected.current, requested = amount, kind = entry.kind,
            method = entry.signature, prevented = false}
        thread.get_hook_storage()[STORAGE] = item
        ctx.count("player_stun.calls")
        if not ready or not ctx.enabled.stun_immunity then return end
        if entry.kind == "damage" then
            -- If enabled between behavior ticks, refill before native zero-damage
            -- processing can observe an already empty gauge.
            refill(selected)
            if amount > 0 then args[3] = sdk.to_ptr(0); item.prevented = true end
        elseif entry.kind == "add" then
            if amount < 0 then args[3] = sdk.to_ptr(0); item.prevented = true end
        elseif amount < selected.maximum then
            args[3] = sdk.to_ptr(selected.maximum)
            item.prevented = true
        end
        if item.prevented then ctx.count("player_stun.prevented") end
    end

    local function after(retval)
        local storage = thread.get_hook_storage()
        local item = storage[STORAGE]
        storage[STORAGE] = nil
        if not item then return end
        local current = item.gauge:get_field("_CurrentValue")
        if item.prevented or current ~= item.before then
            ctx.event("player_stun.change", {method = item.method, requested = item.requested,
                before = item.before, after = current, prevented = item.prevented,
                broken = item.supporter:get_field("_IsRikidoBreak")})
        end
    end

    local unique, seen = {}, {}
    for _, entry in ipairs(candidates) do
        local previous = seen[entry.address]
        if previous and previous.kind ~= entry.kind then
            local detail = "Incompatible player Rikido methods share native code"
            report(detail); return nil, detail
        elseif not previous then
            unique[#unique + 1] = entry; seen[entry.address] = entry
        end
    end
    local ok, install_error = pcall(function()
        for _, entry in ipairs(unique) do
            sdk.hook(entry.method, function(args)
                local succeeded, detail = pcall(before, entry, args)
                if not succeeded then report(detail) end
            end, function(retval)
                local succeeded, detail = pcall(after, retval)
                if not succeeded then report(detail) end
                return retval
            end)
        end
    end)
    if not ok then report(install_error); return nil, tostring(install_error) end
    ready = true
    installed_status = {installed = true, experimental = true, hooks = #unique,
        supporter_hook_skipped_shared = shared_supporter, mode = "controlled_player_rikido"}
    installed_callbacks = {
        update = function()
            if not ready or not ctx.enabled.stun_immunity then return end
            local selected = selected_player()
            if selected then refill(selected) end
        end,
        snapshot = function()
            local selected = selected_player()
            if not selected then return {ready = false} end
            return {ready = true, current = selected.current, maximum = selected.maximum,
                broken = selected.supporter:get_field("_IsRikidoBreak")}
        end,
    }
    return installed_status, installed_callbacks
end

return M
