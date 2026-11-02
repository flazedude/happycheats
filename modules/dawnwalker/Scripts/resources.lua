-- Exact build 25915545. Read the native lock handle, never assign it. Native
-- Lock* replaces a previous lock. A retained numeric ID can outlive its effect;
-- require -1 or a natively confirmed inactive handle before calling. Release
-- only while the field still identifies the effect created by this application.
local M, Instance = {}, {}
Instance.__index = Instance
local COMBAT = "/Script/DogwoodCombat.CombatComponentBase:"
local LIBRARY = "/Script/GameplayAbilities.Default__AbilitySystemBlueprintLibrary"
local OWNER = "/Script/DogwoodCombat.CombatComponentBase"
local CONFIG = {
    dawnwalker_health = {field="HappyCheatsHealthLockHandle", offset=0x7C8, lock="LockHealth", unlock="UnlockHealth", getter="GetHealthPercentage", setter="SetHealthPercent", label="Health", default="/Game/_Dawnwalker/Stats/Effects/GE_HealthOverride.Default__GE_HealthOverride_C"},
    dawnwalker_stamina = {field="HappyCheatsStaminaLockHandle", offset=0x7D0, lock="LockStamina", unlock="UnlockStamina", getter="GetStaminaPercentage", setter="SetStaminaPercent", label="Stamina", default="/Game/_Dawnwalker/Stats/Effects/GE_StaminaOverride.Default__GE_StaminaOverride_C"}
}
local function handle_id(a, value)
    return a.finite(value) and value >= -1 and value <= 2147483647 and value % 1 == 0
end
function Instance:_resolve(action)
    local c, a = CONFIG[action], self.api
    if self.bindings[action] ~= nil then return self.bindings[action] or nil end
    self.bindings[action] = false
    for _, method in ipairs({c.lock, c.unlock, c.getter, c.setter, "GetAbilitySystemComponent"}) do
        if not a.has_function(COMBAT .. method) then return nil end
    end
    if not a.has_function("/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetActiveGameplayEffectStackCount") then return nil end
    local library = a.resolve(LIBRARY)
    if not a.valid(library) or not a.register_property(c.field, OWNER, "IntProperty", c.offset) then return nil end
    local default = a.resolve(c.default)
    if not a.valid(default) then return nil end
    self.bindings[action] = {library=library, default=default}
    return self.bindings[action]
end
function Instance:_count(lease)
    if lease.uncertain then return nil end
    local ok, count = self.api.invoke(lease.library, "GetActiveGameplayEffectStackCount", lease.handle)
    if ok and self.api.finite(count) and count >= 0 and count % 1 == 0 then return count end
end
function Instance:_inactive(binding, id)
    if id == -1 then return true end
    local ok, count = self.api.invoke(binding.library,"GetActiveGameplayEffectStackCount",
        {Handle=id,bPassedFiltersAndWasExecuted=true})
    if not ok or not self.api.finite(count) or count < 0 or count % 1 ~= 0 then return nil end
    return count == 0
end
function Instance:_forget(action)
    self.leases[action], self.flags[action] = nil, false
end
function Instance:_disable(action)
    local lease, a, c = self.leases[action], self.api, CONFIG[action]
    if not lease then self.flags[action]=false; return true, c.label .. " lock disabled" end
    if not a.valid(lease.asc) then self:_forget(action); return true, "Previous ability system no longer exists" end
    if lease.uncertain then return false, c.label .. " lock ownership is uncertain; restart the game" end
    local count = self:_count(lease)
    if count == 0 then self:_forget(action); return true, c.label .. " lock already ended" end
    if count == nil or not a.valid(lease.combat) then return false, c.label .. " lock cleanup could not be checked" end
    local current = a.read(lease.combat, c.field)
    if current ~= lease.handle.Handle then
        -- A normal native Lock* replacement already removes our effect. If its
        -- handle is inexplicably still active, preserve the game-owned field.
        return false, c.label .. " lock changed; cleanup remains pending"
    end
    local owner_ok, owner_asc = a.invoke(lease.combat, "GetAbilitySystemComponent")
    if not owner_ok or not a.same(owner_asc, lease.asc) then return false, c.label .. " original ability-system ownership changed; cleanup remains pending" end
    local ok = a.invoke(lease.combat, c.unlock)
    if not ok then lease.uncertain=true; return false, c.label .. " unlock outcome is uncertain; restart the game" end
    if self:_count(lease) ~= 0 then return false, c.label .. " lock removal was not confirmed" end
    self:_forget(action)
    return true, c.label .. " lock disabled"
end
function Instance:_top_up(state, action)
    local a, c = self.api, CONFIG[action]
    local ok, value = a.invoke(state.combat, c.getter)
    if not ok or not a.finite(value) or value < 0 then return false, c.label .. " value unavailable" end
    if value >= 0.9999 then return true end
    if action == "dawnwalker_health" and value <= 0 then return false, "A living player is required" end
    if not a.invoke(state.combat, c.setter, 1.0) then return false, c.label .. " setter failed" end
    ok, value = a.invoke(state.combat, c.getter)
    return ok and a.finite(value) and value >= 0.999, c.label .. " refill was not confirmed"
end
function Instance:apply(state, action, enabled)
    local c, a = CONFIG[action], self.api
    if not c or type(enabled) ~= "boolean" then return false, "An explicit resource toggle is required" end
    if not enabled then return self:_disable(action) end
    if not state or state.key ~= self.key then return false, "Player session changed; reconnect before enabling" end
    local binding = self:_resolve(action)
    if not binding then return false, c.label .. " lock binding unavailable" end
    local asc = a.read(state.character, "AbilitySystemComponent")
    local ok, combat_asc = a.invoke(state.combat, "GetAbilitySystemComponent")
    if not a.valid(asc) or not ok or not a.same(asc, combat_asc) then return false, "Player ability-system ownership unavailable" end
    local lease = self.leases[action]
    if lease then
        if self.flags[action] and not lease.uncertain and a.same(asc, lease.asc) and a.same(state.combat, lease.combat)
            and a.read(lease.combat, c.field) == lease.handle.Handle and self:_count(lease) == 1 then
            return true, c.label .. " lock already enabled"
        end
        return false, c.label .. " previous lock must be resolved before enabling"
    end
    if a.read(binding.default, "DurationPolicy") ~= 1 or a.read(binding.default, "StackingType") ~= 0 then
        return false, c.label .. " lock effect configuration differs from the verified build"
    end
    local before = a.read(state.combat, c.field)
    if not handle_id(a, before) then return false, c.label .. " lock handle unavailable" end
    local inactive = self:_inactive(binding,before)
    if inactive == nil then return false,c.label .. " existing lock could not be checked; try again" end
    if not inactive then return false, "The game already owns the " .. c.label:lower() .. " lock; try after it ends" end
    local topped, reason = self:_top_up(state, action)
    if not topped then return false, reason end
    local asc_ok, current_asc = a.invoke(state.combat, "GetAbilitySystemComponent")
    local pawn_ok, pawn = a.invoke(state.controller, "K2_GetPawn")
    local owner_ok, owner = a.invoke(state.combat, "GetCharacter")
    local local_ok, local_player = a.invoke(state.character, "IsLocallyControlled")
    local authority_ok, authority = a.invoke(state.character, "HasAuthority")
    local alive_ok, alive = a.invoke(state.character, "IsAlive")
    local after = a.read(state.combat,c.field)
    if (after ~= before and after ~= -1) or not handle_id(a,after) or self:_inactive(binding,after) ~= true
        or not a.same(a.read(state.character, "AbilitySystemComponent"), asc)
        or not a.same(a.read(state.character, "Controller"), state.controller)
        or not a.same(a.read(state.character, "CombatComponent"), state.combat)
        or not asc_ok or not a.same(current_asc, asc)
        or not pawn_ok or not a.same(pawn, state.character) or not owner_ok or not a.same(owner, state.character)
        or not local_ok or not local_player or not authority_ok or not authority or not alive_ok or not alive then
        return false, c.label .. " ownership changed before locking"
    end
    lease = {asc=asc, combat=state.combat, library=binding.library, uncertain=true, key=state.key}
    self.leases[action] = lease
    local applied = a.invoke(state.combat, c.lock)
    local id = a.read(state.combat, c.field)
    if not applied or not handle_id(a, id) then return false, c.label .. " lock outcome is uncertain; restart the game" end
    if id == -1 then self:_forget(action); return false, c.label .. " lock was not created" end
    lease.handle, lease.uncertain = {Handle=id, bPassedFiltersAndWasExecuted=true}, false
    local read_ok, value = a.invoke(state.combat, c.getter)
    if self:_count(lease) ~= 1 or not read_ok or not a.finite(value) or value < 0.999 then
        local cleaned, message = self:_disable(action)
        return false, cleaned and (c.label .. " lock was not confirmed") or message
    end
    self.flags[action]=true
    return true, c.label .. " locked full; native resource depletion is blocked"
end
function Instance:refill(state, action)
    local was_enabled = self.flags[action]
    local ok, message = self:_disable(action)
    if not ok then return false, message end
    if was_enabled then return self:apply(state, action, true) end
    return self:_top_up(state, action)
end
function Instance:disable_all()
    local success, messages = true, {}
    for action in pairs(CONFIG) do
        local ok, message = self:_disable(action)
        if not ok then success=false; messages[#messages+1]=message end
    end
    return success, success and "Resource locks disabled" or table.concat(messages, "; ")
end
function Instance:refresh(state)
    local available, values = {}, {diagnostic_health_mode="Owned native full-health lock; no periodic refill gaps", diagnostic_stamina_mode="Owned native full-stamina lock; no periodic refill gaps"}
    if not state or self.key ~= state.key then
        local cleaned, message = self:disable_all()
        if not cleaned then
            for action in pairs(CONFIG) do available[action]=false end
            values.diagnostic_resource_locks=message
            return available, values
        end
        self.key, self.bindings = state and state.key or nil, {}
    end
    if not state then
        for action in pairs(CONFIG) do available[action]=false end
        return available, values
    end
    for action,c in pairs(CONFIG) do
        available[action] = self:_resolve(action) ~= nil
        local lease = self.leases[action]
        if lease then
            local current = self.api.read(lease.combat, c.field)
            local count = self:_count(lease)
            if not self.api.same(self.api.read(state.character, "AbilitySystemComponent"), lease.asc)
                or not self.api.same(state.combat, lease.combat) or current ~= (lease.handle and lease.handle.Handle) or count ~= 1 then
                local cleaned, message = self:_disable(action)
                available[action] = available[action] and cleaned
                values["diagnostic_" .. action .. "_lock"] = message
            elseif self.flags[action] then
                local ok, percent = self.api.invoke(state.combat, c.getter)
                if not ok or not self.api.finite(percent) then available[action]=false
                elseif percent < 0.999 then
                    -- Refill and reacquire only our own lock after a max-resource
                    -- change. Never replace a game-created lock or revive a pawn.
                    local restored, message = self:refill(state, action)
                    values["diagnostic_" .. action .. "_lock"] = message
                    if not restored then self.flags[action]=false end
                end
            end
        end
    end
    return available, values
end
function Instance:enabled()
    return {dawnwalker_health=self.flags.dawnwalker_health, dawnwalker_stamina=self.flags.dawnwalker_stamina}
end
function M.new(api)
    return setmetatable({api=api, leases={}, bindings={}, flags={dawnwalker_health=false,dawnwalker_stamina=false}}, Instance)
end
return M
