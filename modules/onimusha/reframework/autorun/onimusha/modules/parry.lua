-- Experimental native parry input. Exact signatures were obtained from TDB82.
-- This module never changes hook arguments/results, timing grades or animations.
-- Detailed telemetry is bounded. All mutation requires ctx.enabled.auto_parry.
local M = {}

function M.install(ctx)
    local installed = false
    local faulted = false
    local counts = {}
    local methods = {}
    local function count(name)
        counts[name] = (counts[name] or 0) + 1
        ctx.count(name)
        return counts[name]
    end
    local function event(name, data)
        local n = count(name)
        if n <= 20 or n % 100 == 0 then
            ctx.event(name, type(data) == "function" and data() or data or {})
        end
    end
    local function fail(detail)
        installed = false
        if faulted then return end
        faulted = true
        ctx.error("auto_parry", tostring(detail):gsub("0[xX]%x+", "<address omitted>"))
    end
    local function resolve(key, owner, signature, return_type, is_static)
        local td = sdk.find_type_definition(owner)
        assert(td, "Missing type: " .. owner)
        local method = td:get_method(signature)
        assert(method, "Missing method: " .. owner .. "." .. signature)
        assert(method:is_static() == (is_static == true), "Static signature changed: " .. signature)
        local result = method:get_return_type()
        assert(result and result:get_full_name() == return_type, "Return type changed: " .. signature)
        methods[key] = method
    end
    local function signed_int(value)
        local n = sdk.to_int64(value) & 0xffffffff
        if n >= 0x80000000 then n = n - 0x100000000 end
        return n
    end
    local function current_entity(object)
        local player = ctx.player()
        if player and player.character and player.entity and ctx.same(object, player.entity) then
            return player
        end
        return nil
    end
    local function current_updater(object)
        local player = ctx.player()
        if not player or not player.character or not player.entity or not object then return nil end
        if not ctx.same(object:get_field("_OwnerCharacter"), player.character) then return nil end
        local actual = player.entity:get_field("<JustFrameUpdater>k__BackingField")
        if ctx.same(actual, object) then return player end
        return nil
    end

    local ok, detail = pcall(function()
        assert(thread and thread.get_hook_storage, "REFramework thread hook storage is unavailable")
        resolve("sensor", "app.cPlayerCharacterEntity",
            "evAttackSensorHitProcess(app.AttackSensorHitInfo)", "app.Hit.RESULT")
        resolve("enable", "app.cPlayerCharacterEntity",
            "isEnableJustGuard(app.cPlayerJustFrameUpdater.TYPE, app.AttackSensorHitInfo)", "System.Boolean")
        resolve("grade", "app.cPlayerJustFrameUpdater",
            "checkGrade(app.cPlayerJustFrameUpdater.TYPE, via.GameObject, app.JustFrame.cJustActionRankOverwriteData)",
            "app.JustFrame.GRADE")
        resolve("input", "app.cPlayerJustFrameUpdater",
            "setInputOn(app.cPlayerJustFrameUpdater.TYPE)", "System.Void")
        resolve("frame", "app.cPlayerJustFrameUpdater",
            "getFrameCounter(app.cPlayerJustFrameUpdater.TYPE)", "System.Single")
        resolve("previous", "app.cPlayerJustFrameUpdater",
            "getPrevFrameCounter(app.cPlayerJustFrameUpdater.TYPE)", "System.Single")
        resolve("active", "app.cPlayerJustFrameUpdater",
            "isActive(app.cPlayerJustFrameUpdater.TYPE)", "System.Boolean")
        resolve("input_enabled", "app.cPlayerJustFrameUpdater", "get_IsEnableInput()", "System.Boolean")
        resolve("destroyed", "app.CharacterBase", "get_IsAnyDestroyState()", "System.Boolean")
        resolve("event", "app.CharacterBase", "get_IsEventStart()", "System.Boolean")
        resolve("valid", "app.cCharacterEntityBase", "get_IsActionValid()", "System.Boolean")
        resolve("grapple", "app.CharacterUtil",
            "isGrappleAction(app.CharacterBase)", "System.Boolean", true)
        for key, signature in pairs({
            sync_damage = "isSyncDamage()", counter_issen = "isCounterIssenAction()",
            break_issen = "isBreakIssenAction()", block_issen = "isSimpleBlockIssenAction()",
            ultimate = "isUltimateAttackAction()", dead_heat = "isDeadHeatAction()",
            special = "isSpecialAction()",
        }) do
            resolve(key, "app.cPlayerCharacterEntity", signature, "System.Boolean")
        end

        local enum_type = assert(sdk.find_type_definition("app.cPlayerJustFrameUpdater.TYPE"))
        local enum_field = assert(enum_type:get_field("PARRY"), "PARRY enum missing")
        assert(enum_field:is_static() and enum_field:is_literal(), "PARRY is not an enum literal")
        local parry = enum_field:get_data(nil)
        assert(parry == 3, "PARRY enum differs from the verified game build")

        local function frame_state(updater)
            if not updater then return {} end
            return {
                frame = methods.frame:call(updater, parry),
                previous_frame = methods.previous:call(updater, parry),
                active = methods.active:call(updater, parry),
                input_enabled = methods.input_enabled:call(updater),
            }
        end
        local function guard_snapshot(player, updater)
            local action_valid = methods.valid:call(player.entity)
            local input_enabled = methods.input_enabled:call(updater)
            return {
                ready = player.character:get_field("_IsReady"),
                action_valid_method = action_valid,
                action_valid_lua_type = type(action_valid),
                action_valid_field = player.entity:get_field("_IsActionValid"),
                input_enabled = input_enabled,
                input_enabled_lua_type = type(input_enabled),
                triggered_input_group = updater:get_field("_TriggeredInputType"),
                disabled_input_group = updater:get_field("_DisableInputType"),
                grapple_action = methods.grapple:call(nil, player.character),
                grapple_track_present = player.entity:get_field("<CharacterGrappleTrackSeq>k__BackingField") ~= nil,
                parry = frame_state(updater),
            }
        end
        local function eligible(player, updater)
            if methods.destroyed:call(player.character) then return false, "destroying" end
            if methods.event:call(player.character) then return false, "event" end
            -- Live combat produced 638 false action-valid checks with a ready,
            -- undestroyed player; the raw _IsActionValid field is also false.
            -- It is not a reliable character-readiness test at the sensor phase.
            if player.character:get_field("_IsReady") ~= true then return false, "not_ready" end
            -- The track component exists in ordinary combat, including successful
            -- manual parries. Use the native active-action predicate instead.
            if methods.grapple:call(nil, player.character) then
                return false, "grapple_action"
            end
            for _, key in ipairs({
                "sync_damage", "counter_issen", "break_issen", "block_issen",
                "ultimate", "dead_heat", "special",
            }) do
                if methods[key]:call(player.entity) then return false, key end
            end
            if updater:get_field("_IsDestroy") ~= false then return false, "updater_destroying" end
            if methods.input_enabled:call(updater) ~= true then return false, "native_input_disabled" end
            return true
        end
        -- Per-call storage is popped by REFramework after each post-hook, including
        -- nested/recursive calls. No shared pre/post scratch variables are used.
        sdk.hook(methods.sensor, function(args)
            local storage = thread.get_hook_storage()
            storage.onimusha_parry_sensor = nil
            storage.onimusha_parry_updater = nil
            local success, err = pcall(function()
                local entity = sdk.to_managed_object(args[2])
                local player = current_entity(entity)
                if not player or not sdk.to_managed_object(args[3]) then return end
                local updater = entity:get_field("<JustFrameUpdater>k__BackingField")
                if not current_updater(updater) then return end
                local sample = {enabled = ctx.enabled.auto_parry == true, requested = false}
                storage.onimusha_parry_sensor = sample
                storage.onimusha_parry_updater = updater
                count("parry.sensor_calls")
                if not installed or ctx.enabled.auto_parry ~= true then return end
                local allowed, reason = eligible(player, updater)
                if not allowed then
                    event("parry.skipped." .. reason, function()
                        return {reason = reason, guards = guard_snapshot(player, updater)}
                    end)
                    return
                end
                sample.before = frame_state(updater)
                -- The only gameplay change: queue the game's normal PARRY input
                -- for this incoming player sensor event. Native eligibility and
                -- reaction construction still execute with their original values.
                methods.input:call(updater, parry)
                sample.requested = true
                sample.after = frame_state(updater)
                event("parry.input_requested", sample)
            end)
            if not success then fail(err) end
            return sdk.PreHookResult.CALL_ORIGINAL
        end, function(retval)
            local success, err = pcall(function()
                local sample = thread.get_hook_storage().onimusha_parry_sensor
                if sample then
                    sample.hit_result = signed_int(retval)
                    local updater = thread.get_hook_storage().onimusha_parry_updater
                    if sample.requested and current_updater(updater) then
                        sample.after_original = frame_state(updater)
                        if sample.after_original.active then
                            count("parry.input_active_after_sensor")
                        else
                            count("parry.input_not_active_after_sensor")
                        end
                    end
                    event("parry.sensor_result", sample)
                end
            end)
            if not success then fail(err) end
            return retval
        end)

        sdk.hook(methods.grade, function(args)
            local storage = thread.get_hook_storage()
            storage.onimusha_parry_grade = nil
            local success, err = pcall(function()
                if signed_int(args[3]) ~= parry then return end
                local updater = sdk.to_managed_object(args[2])
                local player = current_updater(updater)
                if not player then return end
                storage.onimusha_parry_grade = frame_state(updater)
                storage.onimusha_parry_grade.enabled = ctx.enabled.auto_parry == true
                storage.onimusha_parry_grade.guards = guard_snapshot(player, updater)
            end)
            if not success then fail(err) end
            return sdk.PreHookResult.CALL_ORIGINAL
        end, function(retval)
            local success, err = pcall(function()
                local sample = thread.get_hook_storage().onimusha_parry_grade
                if sample then
                    sample.grade = signed_int(retval)
                    event("parry.grade." .. sample.grade, sample)
                end
            end)
            if not success then fail(err) end
            return retval
        end)

        sdk.hook(methods.enable, function(args)
            local storage = thread.get_hook_storage()
            storage.onimusha_parry_enable = nil
            local success, err = pcall(function()
                if signed_int(args[3]) ~= parry then return end
                local entity = sdk.to_managed_object(args[2])
                if not current_entity(entity) then return end
                storage.onimusha_parry_enable = {enabled = ctx.enabled.auto_parry == true}
            end)
            if not success then fail(err) end
            return sdk.PreHookResult.CALL_ORIGINAL
        end, function(retval)
            local success, err = pcall(function()
                local sample = thread.get_hook_storage().onimusha_parry_enable
                if sample then
                    sample.native_result = (sdk.to_int64(retval) & 1) ~= 0
                    event("parry.native_eligible." .. tostring(sample.native_result), sample)
                end
            end)
            if not success then fail(err) end
            return retval
        end)
        installed = true
    end)
    if not ok then
        fail(detail)
        return {installed = false, experimental = true, detail = tostring(detail)}
    end
    return {
        installed = true, experimental = true, mode = "native_parry_input",
        detail = "Observation active; input injection follows auto_parry setting. Combat outcome remains unverified.",
    }
end

return M
