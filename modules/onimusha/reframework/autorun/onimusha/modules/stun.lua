-- Experimental Steam 1.0.1 / build 24769601 bindings, copied from the live TDB.
-- This changes a qualifying hit's native Rikido damage, never UI/gauge fields.
-- Hook lifetime and thread identity APIs:
-- https://cursey.github.io/reframework-book/api/thread.html
local M = {}
local installed_result
local FEATURE = "one_hit_stun"
local PARAM = "app.cCharacterApplyDamage.cApplyParam"
local SCOPE_KEY = "onimusha_stun_scope"
local DAMAGE_KEY = "onimusha_stun_damage"
local LEAVE_ONE_KEY = "onimusha_stun_leave_one"

local function address(object)
    return object and object:get_address() or nil
end

local function is_a(object, name)
    return object ~= nil and object:get_type_definition():is_a(name)
end

local function int32(pointer)
    local value = sdk.to_int64(pointer) & 0xFFFFFFFF
    return value >= 0x80000000 and value - 0x100000000 or value
end

local function boolean(pointer)
    return (sdk.to_int64(pointer) & 1) == 1
end

local function verified_method(type_name, signature, count, return_type)
    local definition = assert(sdk.find_type_definition(type_name), "Missing type " .. type_name)
    local method = assert(definition:get_method(signature), "Missing method " .. type_name .. "." .. signature)
    assert(not method:is_static(), "Unexpected static method " .. signature)
    assert(method:get_num_params() == count, "Unexpected parameter count " .. signature)
    assert(method:get_return_type():get_full_name() == return_type, "Unexpected return type " .. signature)
    local pointer = method:get_function()
    assert(pointer ~= nil and sdk.to_int64(pointer) ~= 0, "Missing function " .. signature)
    return method, tostring(sdk.to_int64(pointer))
end

function M.install(ctx)
    if installed_result then return installed_result end
    if not thread or not thread.get_id or not thread.get_hook_storage then
        local detail = "REFramework thread.get_id and thread.get_hook_storage are required"
        ctx.error(FEATURE, detail)
        return nil, detail
    end

    local function report(detail)
        -- A reporting failure must not escape into the native damage pipeline.
        pcall(ctx.error, FEATURE, tostring(detail))
    end

    local scopes_to_hook, gauge_method, gauge_address, leave_one_method, leave_one_address
    local valid, validation_error = pcall(function()
        scopes_to_hook = {}
        for _, name in ipairs({"doApplyStockDamage", "execFirstDamage", "execSecondDamage", "execThirdDamage"}) do
            local method, native_address = verified_method("app.cEnemyApplyDamage", name .. "(" .. PARAM .. ")", 1, "System.Void")
            scopes_to_hook[#scopes_to_hook + 1] = {name = name, method = method, address = native_address}
        end
        gauge_method, gauge_address = verified_method("app.cEnemyRikidoSupporter", "applyDamage(System.Int32, System.Boolean, System.Boolean)", 3, "System.Boolean")
        leave_one_method, leave_one_address = verified_method("app.cEnemyApplyDamage", "checkRikidoLeaveOne(" .. PARAM .. ")", 1, "System.Boolean")
        verified_method("app.cCharacterModuleBase", "get_OwnerCharacter()", 0, "app.CharacterBase")
        verified_method("app.cStockDamageInfoBase", "get_AttackCharacter()", 0, "app.CharacterBase")
        local param_type = assert(sdk.find_type_definition(PARAM), "Missing apply parameter type")
        assert(param_type:get_field("DamageInfo"), "Missing cApplyParam.DamageInfo")
        for signature, return_type in pairs({
            ["getRikidoValue()"] = "System.Int32",
            ["getRikidoMaxValue()"] = "System.Single",
            ["isRikidoBreak()"] = "System.Boolean",
            ["get_IsRikidoBreakTrigger()"] = "System.Boolean",
            ["isValidRikido()"] = "System.Boolean",
            ["isEnableRikidoDamage()"] = "System.Boolean",
        }) do
            verified_method("app.cCharacterRikidoSupporter", signature, 0, return_type)
        end
    end)
    if not valid then
        report(validation_error)
        return nil, tostring(validation_error)
    end

    -- Each OS thread has its own stack. Stack frames hold primitives only;
    -- their exact cleanup token is owned by REFramework's recursive hook storage.
    -- An unmatched nested hit masks its outer hit instead of borrowing its source.
    local scopes_by_thread = {}
    local hooked_addresses = {}
    local ready = false

    -- Live baseline overkill stopped at one. Use the explicitly named decision;
    -- the two applyDamage Boolean parameter names are stripped in this build.
    local function before_leave_one(args)
        local state = {matched = false}
        thread.get_hook_storage()[LEAVE_ONE_KEY] = state
        ctx.count("stun.leaveone.calls")
        local receiver = sdk.to_managed_object(args[2])
        local param = sdk.to_managed_object(args[3])
        if not is_a(receiver, "app.cEnemyApplyDamage") or not is_a(param, PARAM) then return end
        local owner = receiver:call("get_OwnerCharacter()")
        if not is_a(owner, "app.EnemyCharacter") then return end
        local info = param:get_field("DamageInfo")
        if not is_a(info, "app.cStockDamageInfoCharacter") then return end
        local attacker = info:call("get_AttackCharacter()")
        local player = ctx.player()
        if not player or not player.character or not attacker then return end
        state.owner = address(owner)
        state.unique_id = param:get_field("UniqueID")
        state.matched = ctx.same(attacker, player.character) and not ctx.same(owner, player.character)
        if state.matched then ctx.count("stun.leaveone.matched") end
    end

    local function after_leave_one(retval)
        local storage = thread.get_hook_storage()
        local state = storage[LEAVE_ONE_KEY]
        storage[LEAVE_ONE_KEY] = nil
        if not state then return retval end
        local native_result = boolean(retval)
        if native_result then ctx.count("stun.leaveone.native_true") end
        local override = ready and ctx.enabled.one_hit_stun and state.matched and native_result
        if override then ctx.count("stun.leaveone.overridden") end
        ctx.event("stun.leaveone.result", {
            owner = state.owner, unique_id = state.unique_id,
            matched = state.matched, native_result = native_result, overridden = override,
        })
        if override then return sdk.to_ptr(false) end
        return retval
    end

    local function enter_scope(name, args)
        local storage = thread.get_hook_storage()
        local id = tostring(thread.get_id())
        local stack = scopes_by_thread[id]
        if not stack then
            stack = {}
            scopes_by_thread[id] = stack
        end
        local frame = {thread_id = id, name = name, matched = false}
        storage[SCOPE_KEY] = frame
        stack[#stack + 1] = frame
        ctx.count("stun.scope." .. name)

        local receiver = sdk.to_managed_object(args[2])
        local param = sdk.to_managed_object(args[3])
        if not is_a(receiver, "app.cEnemyApplyDamage") or not is_a(param, PARAM) then return end
        local owner = receiver:call("get_OwnerCharacter()")
        if not is_a(owner, "app.EnemyCharacter") then return end
        frame.owner_address = address(owner)
        frame.unique_id = param:get_field("UniqueID")

        local damage_info = param:get_field("DamageInfo")
        if not is_a(damage_info, "app.cStockDamageInfoCharacter") then return end
        local attacker = damage_info:call("get_AttackCharacter()")
        frame.attacker_address = address(attacker)
        local player = ctx.player()
        if not player or not player.character or not attacker then return end
        frame.matched = ctx.same(attacker, player.character) and not ctx.same(owner, player.character)
        if frame.matched then ctx.count("stun.scope.controlled_player") end
        ctx.event("stun.scope.enter", {
            stage = name, unique_id = frame.unique_id,
            owner = frame.owner_address, attacker = frame.attacker_address,
            controlled_player = frame.matched,
            rikido_damage = param:get_field("RikidoDamage"),
            rikido_decrease = param:get_field("RikidoDecrease"),
        })
    end

    local function leave_scope()
        local storage = thread.get_hook_storage()
        local frame = storage[SCOPE_KEY]
        storage[SCOPE_KEY] = nil
        if not frame then return end
        local stack = scopes_by_thread[frame.thread_id]
        if not stack or stack[#stack] ~= frame then
            -- Fail closed after an unexpected unwind; never reuse a stale source.
            scopes_by_thread[frame.thread_id] = nil
            report("Damage scope stack mismatch at " .. frame.name)
            return
        end
        stack[#stack] = nil
        if #stack == 0 then scopes_by_thread[frame.thread_id] = nil end
        ctx.count("stun.scope.exit." .. frame.name)
    end

    local function snapshot(supporter)
        return {
            current = supporter:call("getRikidoValue()"),
            maximum = supporter:call("getRikidoMaxValue()"),
            broken = supporter:call("isRikidoBreak()"),
            break_trigger = supporter:call("get_IsRikidoBreakTrigger()"),
        }
    end

    local function before_damage(args)
        ctx.count("stun.rikido.calls")
        local supporter = sdk.to_managed_object(args[2])
        if not is_a(supporter, "app.cEnemyRikidoSupporter") then return end
        local owner = supporter:call("get_OwnerCharacter()")
        if not is_a(owner, "app.EnemyCharacter") then return end
        local owner_address = address(owner)
        local stack = scopes_by_thread[tostring(thread.get_id())]
        local frame = stack and stack[#stack]
        local matched = frame ~= nil and frame.matched and frame.owner_address == owner_address
        if matched then
            ctx.count("stun.rikido.controlled_scope_match")
        elseif not frame then
            ctx.count("stun.rikido.no_scope")
        else
            ctx.count("stun.rikido.scope_mismatch")
        end

        local amount = int32(args[3])
        local state = {
            supporter = supporter, -- Retained only until this hook's post callback.
            owner = owner_address,
            scope = frame and frame.name or nil,
            unique_id = frame and frame.unique_id or nil,
            matched = matched,
            original_amount = amount,
            applied_amount = amount,
            boolean_1 = boolean(args[4]),
            boolean_2 = boolean(args[5]),
            before = snapshot(supporter),
            modified = false,
        }
        thread.get_hook_storage()[DAMAGE_KEY] = state

        local current = state.before.current
        if ready and ctx.enabled.one_hit_stun and matched and amount > 0
            and type(current) == "number" and current > 0 and current <= 0x7FFFFFFF
            and not state.before.broken
            and supporter:call("isValidRikido()")
            and supporter:call("isEnableRikidoDamage()") then
            local replacement = math.max(amount, math.floor(current))
            if replacement > amount then
                args[3] = sdk.to_ptr(replacement)
                state.applied_amount = replacement
                state.modified = true
                ctx.count("stun.rikido.amount_replaced")
            end
        end
        -- Both Boolean arguments, the original method, and its return are intact.
        ctx.event("stun.rikido.before", {
            owner = state.owner, scope = state.scope, unique_id = state.unique_id,
            matched = state.matched, original_amount = amount,
            applied_amount = state.applied_amount, modified = state.modified,
            boolean_1 = state.boolean_1, boolean_2 = state.boolean_2,
            gauge = state.before,
        })
    end

    local function after_damage(retval)
        local storage = thread.get_hook_storage()
        local state = storage[DAMAGE_KEY]
        storage[DAMAGE_KEY] = nil
        if not state then return end
        local supporter = state.supporter
        state.supporter = nil
        local after = snapshot(supporter)
        if state.modified and after.broken then ctx.count("stun.rikido.modified_hit_broken") end
        ctx.event("stun.rikido.after", {
            owner = state.owner, scope = state.scope, unique_id = state.unique_id,
            matched = state.matched, original_amount = state.original_amount,
            applied_amount = state.applied_amount, modified = state.modified,
            boolean_1 = state.boolean_1, boolean_2 = state.boolean_2,
            result = boolean(retval), before = state.before, after = after,
        })
    end

    local success, install_error = pcall(function()
        -- Validate and deduplicate exact native function addresses before hooks.
        local unique_scopes = {}
        assert(leave_one_address ~= gauge_address, "Leave-one and Rikido method share a function")
        for _, candidate in ipairs(scopes_to_hook) do
            assert(candidate.address ~= gauge_address, "Damage scope and Rikido method share a function")
            assert(candidate.address ~= leave_one_address, "Damage scope and leave-one method share a function")
            if not hooked_addresses[candidate.address] then
                hooked_addresses[candidate.address] = candidate.name
                unique_scopes[#unique_scopes + 1] = candidate
            else
                ctx.event("stun.hook.alias", {method = candidate.name, same_as = hooked_addresses[candidate.address]})
            end
        end
        for _, candidate in ipairs(unique_scopes) do
            local name = candidate.name
            sdk.hook(candidate.method, function(args)
                local ok, err = pcall(enter_scope, name, args)
                if not ok then
                    local frame = thread.get_hook_storage()[SCOPE_KEY]
                    if frame then frame.matched = false end
                    report(err)
                end
            end, function(retval)
                local ok, err = pcall(leave_scope)
                if not ok then
                    scopes_by_thread[tostring(thread.get_id())] = nil
                    report(err)
                end
                return retval
            end)
        end
        sdk.hook(gauge_method, function(args)
            local ok, err = pcall(before_damage, args)
            if not ok then report(err) end
        end, function(retval)
            local ok, err = pcall(after_damage, retval)
            if not ok then report(err) end
            return retval
        end)
        sdk.hook(leave_one_method, function(args)
            local ok, err = pcall(before_leave_one, args)
            if not ok then
                thread.get_hook_storage()[LEAVE_ONE_KEY] = nil
                report(err)
            end
        end, function(retval)
            local ok, result = pcall(after_leave_one, retval)
            if not ok then report(result); return retval end
            return result
        end)
        ready = true
        installed_result = {installed = true, experimental = true, scope_hooks = #unique_scopes, leave_one_hook = true}
    end)
    if not success then
        -- Previously registered hooks remain observation-only on partial install.
        ready = false
        report(install_error)
        return nil, tostring(install_error)
    end
    return installed_result
end

return M
