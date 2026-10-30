-- Steam build 25915545, CL-265636: the native AI invulnerability effect grants
-- Effect.Invulnerability with infinite duration and no persistence component.
-- Own an active-effect handle, never the game's shared LockHealth handle or a
-- loose gameplay-tag count. All calls are made on the validated game thread.
local M, Instance = {}, {}
Instance.__index = Instance
local INVULNERABLE = "dawnwalker_invulnerable"
local DAMAGE, INVISIBLE = "dawnwalker_damage", "dawnwalker_invisible"
local EFFECT = "/Script/DogwoodAI.DogwoodAIGameplayEffect_Invulnerability"
local DEFAULT = "/Script/DogwoodAI.Default__DogwoodAIGameplayEffect_Invulnerability"
local LIBRARY = "/Script/GameplayAbilities.Default__AbilitySystemBlueprintLibrary"
local COMBAT_LIBRARY = "/Script/DogwoodCombat.Default__CombatBlueprintFunctionLibrary"
local PLAYER_STUB = "/Script/DogwoodAI.DogwoodPlayerAIStub"
local AI_LIBRARY = "/Script/RebelAI.Default__RebelAISubsystem"
local VISIBILITY_FLAGS, HIDDEN_MASK = "HappyCheatsPlayerStubVisibilityFlags", 0x10
local INVISIBLE_FUNCTIONS = {
    "/Script/RebelAI.RebelAISubsystem:GetAIStubFromActor",
    "/Script/RebelAI.RebelAIStub:GetActor",
    "/Script/RebelAI.RebelAIStub:IsPlayer"
}
local DAMAGE_FIELDS = {"MeleeDamageMultiplier", "ClawsDamageMultiplier", "MagicDamageMultiplier", "UnarmedDamageMultiplier"}
local DAMAGE_FUNCTIONS = {
    "/Script/GameplayAbilities.AbilitySystemComponent:GetAllAttributes",
    "/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetDebugStringFromGameplayAttribute",
    "/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetFloatAttributeBaseFromAbilitySystemComponent",
    "/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetFloatAttributeFromAbilitySystemComponent",
    "/Script/DogwoodCombat.CombatBlueprintFunctionLibrary:SetAttributeValue",
    "/Script/DogwoodCombat.CombatComponentBase:GetAbilitySystemComponent"
}
local FUNCTIONS = {
    "/Script/GameplayAbilities.AbilitySystemComponent:BP_ApplyGameplayEffectToSelf",
    "/Script/GameplayAbilities.AbilitySystemComponent:RemoveActiveGameplayEffect",
    "/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetActiveGameplayEffectStackCount",
    "/Script/DogwoodCombat.CombatComponentBase:GetAbilitySystemComponent"
}

local function flags(value, damage, invisible)
    return { [INVULNERABLE] = value == true, [DAMAGE] = damage == true, [INVISIBLE] = invisible == true }
end

function Instance:_resolve_invisible()
    if self.invisible_bindings ~= nil then return self.invisible_bindings ~= false and self.invisible_bindings or nil end
    self.invisible_bindings = false
    if type(self.api.register_property) ~= "function" or type(self.api.write) ~= "function" then return nil end
    for _, path in ipairs(INVISIBLE_FUNCTIONS) do if not self.api.has_function(path) then return nil end end
    local class, library = self.api.resolve(PLAYER_STUB), self.api.resolve(AI_LIBRARY)
    if not self.api.valid(class) or not self.api.valid(library) then return nil end
    -- Build 25915545: PlayerAIStub is exactly 0x260 bytes. Register on that
    -- player-only subclass, never ActorStub generally or an actor flag byte.
    if not self.api.register_property(VISIBILITY_FLAGS, PLAYER_STUB, "ByteProperty", 0x30) then return nil end
    self.invisible_bindings = {class = class, library = library}
    return self.invisible_bindings
end

function Instance:_visibility_byte(stub)
    local value = self.api.read(stub, VISIBILITY_FLAGS)
    if self.api.finite(value) and value >= 0 and value <= 255 and value % 1 == 0 then return value end
end

function Instance:_owns_invisible(lease)
    local a = self.api
    if not a.valid(lease.stub) or not a.valid(lease.character) then return false end
    local class_ok, class = a.invoke(lease.stub, "GetClass")
    if not class_ok or not a.same(class, lease.class) then return false end
    local type_ok, is_player_class = a.invoke(lease.stub, "IsA", PLAYER_STUB)
    if not type_ok or is_player_class ~= true then return false end
    local player_ok, player = a.invoke(lease.stub, "IsPlayer")
    if not player_ok or player ~= true then return false end
    local owner_ok, owner = a.invoke(lease.stub, "GetActor")
    return owner_ok and a.same(owner, lease.character)
end

function Instance:_forget_invisible()
    self.invisible_lease, self.invisible_active = nil, false
end

function Instance:_current_invisible(lease)
    if not self:_owns_invisible(lease) then return false end
    local ok, current = self.api.invoke(lease.library, "GetAIStubFromActor", lease.character)
    return ok and self.api.same(current, lease.stub)
end

function Instance:_disable_invisible()
    local lease = self.invisible_lease
    if not lease then self.invisible_active = false; return true, "Enemy invisibility disabled" end
    if not self.api.valid(lease.stub) then self:_forget_invisible(); return true, "Previous player AI stub no longer exists" end
    lease.cleanup_pending = true
    if not self:_owns_invisible(lease) then
        return false, "Player AI ownership changed; visibility cleanup could not be confirmed"
    end
    local current = self:_visibility_byte(lease.stub)
    if current == nil then return false, "Player AI visibility could not be read; disable again before enabling" end
    if current & HIDDEN_MASK == 0 then self:_forget_invisible(); return true, "Enemy invisibility already ended" end
    local hidden = self.api.read(lease.character, "bHidden")
    if hidden == true then
        -- Population hiding also owns this bit, while other actor hides may
        -- not. Keep the lease pending until visible or the bit clears instead
        -- of either undoing a game hide or orphaning our original override.
        return false, "Wait until game-controlled player visibility ends; AI cleanup remains pending"
    end
    if hidden ~= false then return false, "Player render visibility could not be checked; no AI flag changed" end
    local restored = current & (~HIDDEN_MASK)
    self.api.write(lease.stub, VISIBILITY_FLAGS, restored)
    local readback = self:_visibility_byte(lease.stub)
    if readback == nil or readback & HIDDEN_MASK ~= 0 then
        return false, "Enemy invisibility cleanup could not be confirmed; disable again before enabling"
    end
    self:_forget_invisible()
    return true, "Enemy invisibility disabled"
end

function Instance:_apply_invisible(state, enabled)
    if type(enabled) ~= "boolean" then return false, "An explicit toggle state is required" end
    if not enabled then return self:_disable_invisible() end
    if not state or state.key ~= self.key then return false, "Player session changed; reconnect before enabling" end
    if self.invisible_lease then
        local lease = self.invisible_lease
        local current = self:_visibility_byte(lease.stub)
        if self.invisible_active and not lease.cleanup_pending and self.api.same(state.character, lease.character) and self:_current_invisible(lease)
            and self.api.read(lease.character, "bHidden") == false and current ~= nil and current & HIDDEN_MASK ~= 0 then
            return true, "Enemy invisibility already enabled"
        end
        return false, "Resolve previous player AI visibility before enabling again"
    end
    local bindings = self:_resolve_invisible()
    if not bindings then return false, "Verified player AI visibility binding unavailable" end
    local found, stub = self.api.invoke(bindings.library, "GetAIStubFromActor", state.character)
    if not found or not self.api.valid(stub) then return false, "Player AI stub unavailable" end
    local lease = {stub = stub, character = state.character, class = bindings.class, library = bindings.library, key = state.key}
    if not self:_owns_invisible(lease) then return false, "Player AI ownership could not be verified" end
    local current = self:_visibility_byte(stub)
    if current == nil then return false, "Player AI visibility byte is unavailable" end
    if current & HIDDEN_MASK ~= 0 or self.api.read(state.character, "bHidden") ~= false then
        return false, "The game already controls player visibility; try after it ends"
    end
    -- The standard sight/hearing stimulus path and native AI target selection
    -- reject target stubs with this bit. No mesh, collision, actor lifecycle,
    -- NPC perception settings, or global AI state is changed.
    self.invisible_lease = lease
    local written = current | HIDDEN_MASK
    local ok = self.api.write(stub, VISIBILITY_FLAGS, written)
    if not ok or self:_visibility_byte(stub) ~= written then
        local cleaned, message = self:_disable_invisible()
        return false, cleaned and "Player AI visibility change was not confirmed" or message
    end
    self.invisible_active, self.invisible_reason = true, nil
    return true, "Enemies ignore the player; existing alert memory may persist"
end

function Instance:_resolve_damage()
    if self.damage_bindings ~= nil then return self.damage_bindings ~= false and self.damage_bindings or nil end
    self.damage_bindings = false
    for _, path in ipairs(DAMAGE_FUNCTIONS) do
        if not self.api.has_function(path) then
            self.damage_reason = "Damage binding unavailable: " .. path
            return nil
        end
    end
    local library, combat = self.api.resolve(LIBRARY), self.api.resolve(COMBAT_LIBRARY)
    if not self.api.valid(library) then self.damage_reason = "Damage attribute library unavailable"; return nil end
    if not self.api.valid(combat) then self.damage_reason = "Damage setter library unavailable"; return nil end
    self.damage_bindings = {library = library, combat = combat}
    return self.damage_bindings
end

function Instance:_damage_base(lease, entry)
    -- Reuse scalar OUT tables while polling; always clear the flag so a failed
    -- native conversion cannot inherit an earlier successful lookup.
    entry.base_out = entry.base_out or {}
    local found = entry.base_out
    found.bSuccessfullyFoundAttribute = nil
    local ok, value = self.api.invoke(lease.library, "GetFloatAttributeBaseFromAbilitySystemComponent", lease.asc, entry.attribute, found)
    if not ok then return nil, "Damage base-value getter failed: " .. entry.name end
    if found.bSuccessfullyFoundAttribute ~= true then return nil, "Damage attribute was not found: " .. entry.name end
    if not self.api.finite(value) then return nil, "Damage base value is not finite: " .. entry.name end
    return value
end

function Instance:_damage_current(lease, entry)
    entry.current_out = entry.current_out or {}
    local found = entry.current_out
    found.bSuccessfullyFoundAttribute = nil
    local ok, value = self.api.invoke(lease.library, "GetFloatAttributeFromAbilitySystemComponent", lease.asc, entry.attribute, found)
    if not ok then return nil, "Damage current-value getter failed: " .. entry.name end
    if found.bSuccessfullyFoundAttribute ~= true then return nil, "Current damage attribute was not found: " .. entry.name end
    if not self.api.finite(value) then return nil, "Current damage value is not finite: " .. entry.name end
    return value
end

local function damage_close(value, expected)
    if expected == 0 then return value == 0 end
    return value ~= nil and math.abs(value - expected) <= math.max(1e-44, math.abs(expected) * 0.00001)
end

function Instance:_disable_damage()
    local lease = self.damage_lease
    if not lease then self.damage_active = false; return true, "Super damage disabled" end
    if not self.api.valid(lease.asc) then self.damage_lease, self.damage_active = nil, false; return true, "Previous ability system no longer exists" end
    local pending, preserved = {}, false
    for i = #lease.entries, 1, -1 do
        local entry = lease.entries[i]
        local current = self:_damage_base(lease, entry)
        if current == nil then pending[#pending + 1] = entry
        elseif current == entry.written then
            self.api.invoke(lease.combat, "SetAttributeValue", lease.asc, entry.attribute, entry.original)
            local restored = self:_damage_base(lease, entry)
            if restored == nil or restored == entry.written then pending[#pending + 1] = entry
            elseif restored ~= entry.original then preserved = true end
        elseif current ~= entry.original then preserved = true end
    end
    lease.entries = pending
    if #pending > 0 then return false, "Some owned damage values could not be restored; disable again before enabling" end
    self.damage_lease, self.damage_active = nil, false
    return true, preserved and "Super damage disabled; game-changed values were preserved" or "Super damage disabled"
end

function Instance:_apply_damage(state, enabled)
    if type(enabled) ~= "boolean" then return false, "An explicit toggle state is required" end
    if not enabled then return self:_disable_damage() end
    if not state or state.key ~= self.key then return false, "Player session changed; reconnect before enabling" end
    if self.damage_lease then
        local owned = self.api.same(self.api.read(state.character, "AbilitySystemComponent"), self.damage_lease.asc)
        if owned and self.damage_active then
            for _, entry in ipairs(self.damage_lease.observations) do
                if self:_damage_base(self.damage_lease, entry) ~= entry.written
                    or not damage_close(self:_damage_current(self.damage_lease, entry), entry.expected_current) then
                    local cleaned, message = self:_disable_damage()
                    return false, cleaned and "Damage factors changed; enable super damage again" or message
                end
            end
            return true, "Super damage already enabled"
        end
        return false, "Restore previous damage values before enabling again"
    end
    local bindings = self:_resolve_damage()
    if not bindings then return false, self.damage_reason or "Native damage attribute setters unavailable" end
    local asc = self.api.read(state.character, "AbilitySystemComponent")
    local combat_ok, combat_asc = self.api.invoke(state.combat, "GetAbilitySystemComponent")
    if not self.api.valid(asc) or not combat_ok or not self.api.same(asc, combat_asc) then return false, "Player ability-system ownership is unavailable" end
    local attributes = {}
    if not self.api.invoke(asc, "GetAllAttributes", attributes) then return false, "Player attribute descriptors unavailable" end
    local descriptors, inspected = {}, 0
    -- The native descriptors preserve FFieldPath ownership; no attribute struct
    -- pointers are fabricated and no GameplayAttributeData memory is assigned.
    for _, attribute in pairs(attributes) do
        inspected = inspected + 1
        if inspected > 512 then return false, "Unexpected number of player attributes" end
        -- UE4SS's OUT-array conversion yields LocalUnrealParam wrappers. The
        -- native struct argument expects the owned UScriptStruct from :get().
        local unwrapped, descriptor = pcall(function() return attribute:get() end)
        if not unwrapped or descriptor == nil then return false, "Player attribute descriptor could not be unwrapped" end
        attribute = descriptor
        local ok, debug_name = self.api.invoke(bindings.library, "GetDebugStringFromGameplayAttribute", attribute)
        if ok and type(debug_name) ~= "string" then
            ok, debug_name = pcall(function() return debug_name:ToString() end)
        end
        if ok and type(debug_name) == "string" then
            for _, name in ipairs(DAMAGE_FIELDS) do
                if debug_name == "CharacterBaseAttributeSet." .. name then
                    if descriptors[name] then return false, "Ambiguous damage attribute descriptors" end
                    descriptors[name] = attribute
                end
            end
        end
    end
    local lease = {asc = asc, library = bindings.library, combat = bindings.combat, entries = {}}
    local planned, active_fields = {}, 0
    for _, name in ipairs(DAMAGE_FIELDS) do
        if not descriptors[name] then return false, "Verified damage attribute missing: " .. name end
        local entry = {attribute = descriptors[name], name = name}
        local reason
        entry.original, reason = self:_damage_base(lease, entry)
        if entry.original == nil then return false, reason end
        if entry.original < 0 or entry.original > 100000 then return false, "Damage base value outside the supported range: " .. name end
        entry.current, reason = self:_damage_current(lease, entry)
        if entry.current == nil then return false, reason end
        if entry.current < 0 or entry.current > 100000 then return false, "Current damage value outside the supported range: " .. name end
        if entry.current > 0 then active_fields = active_fields + 1 end
        -- These are raw damage factors, not additive percentage bonuses. Their
        -- native base can be zero while game effects supply a positive current
        -- factor. Apply a bounded base delta, then require the evaluated factor
        -- to reach the target. Override/scaling/clamp effects that prevent that
        -- result cause rollback; do not guess a neutral +1 or retry native writes.
        entry.expected_current = string.unpack("<f", string.pack("<f", entry.current * 10))
        -- Native GAS values are float32. Retain the exact expected native value
        -- before calling, so even a lost readback can be restored conditionally.
        entry.written = string.unpack("<f", string.pack("<f", entry.original + entry.current * 9))
        planned[#planned + 1] = entry
    end
    if active_fields == 0 then return false, "No positive player damage factors are active; equip an attack before enabling" end
    lease.observations = planned
    self.damage_lease = lease
    for _, entry in ipairs(planned) do
        if self:_damage_base(lease, entry) ~= entry.original or self:_damage_current(lease, entry) ~= entry.current then
            local cleaned, message = self:_disable_damage()
            return false, cleaned and "A damage value changed before application; try again" or message
        end
        -- An inactive attack factor stays zero. Keep monitoring it, but do not
        -- create an ownership lease for a value that was never changed.
        if entry.written ~= entry.original then
            lease.entries[#lease.entries + 1] = entry
            local ok = self.api.invoke(lease.combat, "SetAttributeValue", lease.asc, entry.attribute, entry.written)
            local readback = self:_damage_base(lease, entry)
            local current = self:_damage_current(lease, entry)
            if not ok or readback ~= entry.written or not damage_close(current, entry.expected_current) then
                local cleaned, message = self:_disable_damage()
                return false, cleaned and ("The game did not confirm 10x damage for " .. entry.name .. "; active effects may override or scale the factor") or message
            end
        end
    end
    -- A later setter can trigger recalculation of an earlier field. Confirm all
    -- four factors again before acknowledging success, including untouched zeros.
    for _, entry in ipairs(planned) do
        if self:_damage_base(lease, entry) ~= entry.written or not damage_close(self:_damage_current(lease, entry), entry.expected_current) then
            local cleaned, message = self:_disable_damage()
            return false, cleaned and "Damage factors changed during application; owned changes were restored" or message
        end
    end
    self.damage_active, self.damage_reason = true, nil
    return true, "Super damage enabled: current native attack factors x10; inactive factors unchanged"
end

function Instance:_resolve()
    if self.bindings ~= nil then return self.bindings ~= false and self.bindings or nil end
    self.bindings = false
    for _, path in ipairs(FUNCTIONS) do
        if not self.api.has_function(path) then self.reason = "Invulnerability binding unavailable"; return nil end
    end
    local class, default, library = self.api.resolve(EFFECT), self.api.resolve(DEFAULT), self.api.resolve(LIBRARY)
    if not self.api.valid(class) or not self.api.valid(default) or not self.api.valid(library) then
        self.reason = "Native invulnerability effect unavailable"; return nil
    end
    self.bindings = { class = class, default = default, library = library }
    return self.bindings
end

function Instance:_count(lease)
    if lease.application_uncertain then return nil end
    local ok, count = self.api.invoke(lease.library, "GetActiveGameplayEffectStackCount", lease.handle)
    if ok and self.api.finite(count) and count >= 0 and count % 1 == 0 then return count end
end

function Instance:_forget()
    self.lease, self.active = nil, false
end

function Instance:_disable_invulnerable()
    local lease = self.lease
    if not lease then self.active = false; return true, "Combat modifiers disabled" end
    if not self.api.valid(lease.asc) then self:_forget(); return true, "Previous ability system no longer exists" end
    if lease.application_uncertain then return false, "Invulnerability application lost its effect handle; restart the game" end
    if lease.removal_uncertain then return false, "Invulnerability cleanup could not be confirmed; restart the game" end
    local count = self:_count(lease)
    if count == 0 then self:_forget(); return true, "Invulnerability already ended" end
    if count == nil then return false, "Invulnerability handle could not be checked; no effect removed" end
    -- Remove only the one stack this application added, on its original ASC.
    -- A second effect, even of the same class, has a different active handle.
    local ok, removed = self.api.invoke(lease.asc, "RemoveActiveGameplayEffect", lease.handle, 1)
    if not ok then
        lease.removal_uncertain = true
        return false, "Invulnerability cleanup could not be confirmed; restart the game"
    end
    if removed ~= true then
        if self:_count(lease) == 0 then self:_forget(); return true, "Invulnerability already ended" end
        return false, "The game did not remove the owned invulnerability effect"
    end
    self:_forget()
    return true, "Invulnerability disabled"
end

function Instance:disable_all()
    local damage_ok, damage_message = self:_disable_damage()
    local invulnerable_ok, invulnerable_message = self:_disable_invulnerable()
    local invisible_ok, invisible_message = self:_disable_invisible()
    if not damage_ok then return false, damage_message end
    if not invulnerable_ok then return false, invulnerable_message end
    if not invisible_ok then return false, invisible_message end
    return true, "Combat modifiers disabled"
end

function Instance:refresh(state)
    local available = flags(false)
    local values = {}
    if not state or type(state.key) ~= "string" or state.key == "" then
        local ok, message = self:disable_all()
        if ok then self.key, self.bindings, self.damage_bindings, self.invisible_bindings, self.damage_reason = nil, nil, nil, nil, nil end
        values.diagnostic_invulnerable = message
        values.diagnostic_damage = message
        values.diagnostic_invisible = message
        return available, values
    end
    if self.key ~= state.key then
        local ok, message = self:disable_all()
        if not ok then values.diagnostic_invulnerable = message; values.diagnostic_damage = message; values.diagnostic_invisible = message; return available, values end
        self.key, self.bindings, self.damage_bindings, self.invisible_bindings, self.reason, self.damage_reason = state.key, nil, nil, nil, nil, nil
    end
    local bindings = self:_resolve()
    available[INVULNERABLE] = bindings ~= nil
    available[DAMAGE] = self:_resolve_damage() ~= nil
    available[INVISIBLE] = self:_resolve_invisible() ~= nil
    if self.invisible_lease then
        local lease = self.invisible_lease
        if not self.api.same(state.character, lease.character) or not self:_current_invisible(lease) then
            local cleaned, message = self:_disable_invisible()
            self.invisible_reason = message
            if not cleaned then available[INVISIBLE] = false end
        else
            local current, hidden = self:_visibility_byte(lease.stub), self.api.read(lease.character, "bHidden")
            if current == nil or type(hidden) ~= "boolean" then
                available[INVISIBLE] = false; self.invisible_reason = "Owned player AI visibility could not be confirmed"
            elseif current & HIDDEN_MASK == 0 or hidden or lease.cleanup_pending then
                local cleaned, message = self:_disable_invisible()
                self.invisible_reason = message
                if not cleaned then available[INVISIBLE] = false end
            end
        end
    end
    if self.damage_lease then
        local lease, changed, unreadable = self.damage_lease, false, false
        if not self.api.same(self.api.read(state.character, "AbilitySystemComponent"), lease.asc) then changed = true end
        if not changed then
            for _, entry in ipairs(lease.observations or lease.entries) do
                local current = self:_damage_base(lease, entry)
                if current == nil then unreadable = true elseif current ~= entry.written then changed = true end
                local evaluated = self:_damage_current(lease, entry)
                if evaluated == nil then unreadable = true
                elseif not damage_close(evaluated, entry.expected_current) then changed = true end
            end
        end
        if changed then
            local cleaned, message = self:_disable_damage()
            self.damage_reason = cleaned and "Damage factors changed; super damage disabled. Enable it again if needed" or message
            if not cleaned then available[DAMAGE] = false end
        elseif unreadable or not self.damage_active then
            available[DAMAGE] = false; self.damage_reason = "Owned damage values could not be confirmed"
        end
    end
    if self.lease and not self.api.same(self.api.read(state.character, "AbilitySystemComponent"), self.lease.asc) then
        local cleaned, message = self:_disable_invulnerable()
        self.reason = cleaned and "Player ability system changed; enable invulnerability again" or message
        available[INVULNERABLE] = cleaned and bindings ~= nil
    end
    if self.lease then
        local count = 0
        if self.api.valid(self.lease.asc) then count = self:_count(self.lease) end
        if count == 0 then
            self:_forget(); self.reason = "Invulnerability ended; enable it again if needed"
        elseif count == nil or self.lease.removal_uncertain then
            available[INVULNERABLE] = false
            self.reason = "Owned invulnerability state could not be confirmed"
        end
    end
    values.diagnostic_invulnerable = self.reason or (self.active and "Owned native invulnerability effect active" or "Native invulnerability effect ready")
    values.diagnostic_damage = self.damage_reason or (self.damage_active and "Current native attack factors x10; inactive factors unchanged" or "Native damage factors ready; base and evaluated values checked on enable")
    values.diagnostic_invisible = self.invisible_reason or (available[INVISIBLE]
        and (self.invisible_active and "Enemies ignore the player; existing alert memory may persist" or "Player-only enemy detection override ready")
        or "Verified player AI visibility binding unavailable")
    return available, values
end

function Instance:apply(state, action, enabled)
    if action == DAMAGE then
        local ok, message = self:_apply_damage(state, enabled)
        self.damage_reason = message
        return ok, message
    end
    if action == INVISIBLE then return self:_apply_invisible(state, enabled) end
    if action ~= INVULNERABLE then return false, "This combat modifier has no verified reversible binding" end
    if type(enabled) ~= "boolean" then return false, "An explicit toggle state is required" end
    if not enabled then return self:_disable_invulnerable() end
    if not state or state.key ~= self.key then return false, "Player session changed; reconnect before enabling" end
    if self.lease then
        local owned = self.api.same(self.api.read(state.character, "AbilitySystemComponent"), self.lease.asc)
        if owned and self.active and not self.lease.removal_uncertain and self:_count(self.lease) == 1 then return true, "Invulnerability already enabled" end
        return false, "Resolve the previous invulnerability effect before enabling again"
    end
    local bindings = self:_resolve()
    if not bindings then return false, self.reason end
    -- A stacking or time-limited effect cannot provide this ownership contract.
    -- Recheck mutable defaults immediately before applying, not on every poll.
    if self.api.read(bindings.default, "DurationPolicy") ~= 1 or self.api.read(bindings.default, "StackingType") ~= 0 then
        return false, "Native invulnerability effect configuration differs from the verified build"
    end
    local asc = self.api.read(state.character, "AbilitySystemComponent")
    local combat_ok, combat_asc = self.api.invoke(state.combat, "GetAbilitySystemComponent")
    if not self.api.valid(asc) or not combat_ok or not self.api.same(asc, combat_asc) then
        return false, "Player ability-system ownership is unavailable"
    end
    -- ContextHandle's shared pointer is opaque to UE4SS's Lua-table converter.
    -- The verified native BP_Apply implementation makes its own context when
    -- the incoming default context is empty; do not marshal shared pointers.
    local applied, handle = self.api.invoke(asc, "BP_ApplyGameplayEffectToSelf", bindings.class, 1.0, {})
    local handle_id = type(handle) == "table" and handle.Handle or nil
    local handle_ok = self.api.finite(handle_id) and handle_id >= -1 and handle_id <= 2147483647
        and handle_id % 1 == 0 and type(handle.bPassedFiltersAndWasExecuted) == "boolean"
    if not applied or not handle_ok then
        -- The native call may have succeeded before marshalling failed. Without
        -- its unique handle, retrying could orphan another effect and removing
        -- by class could remove the game's effect. Keep this ASC quarantined.
        self.lease = {asc = asc, application_uncertain = true, key = state.key}
        self.reason = "Invulnerability application lost its effect handle; restart the game"
        return false, self.reason
    end
    if handle_id == -1 then return false, "The game did not create an active invulnerability effect" end
    self.lease = { asc = asc, handle = handle, library = bindings.library, key = state.key }
    local count = self:_count(self.lease)
    if count ~= 1 or handle.bPassedFiltersAndWasExecuted ~= true then
        local cleaned, message = self:_disable_invulnerable()
        return false, cleaned and "The game did not confirm the invulnerability effect" or message
    end
    self.active, self.reason = true, nil
    return true, "Invulnerability enabled"
end

function Instance:enabled() return flags(self.active, self.damage_active, self.invisible_active) end
function M.new(api)
    assert(type(api) == "table", "Combat adapter required")
    for _, name in ipairs({"valid", "read", "invoke", "same", "finite", "has_function", "resolve"}) do
        assert(type(api[name]) == "function", "Missing combat adapter: " .. name)
    end
    return setmetatable({ api = api, active = false }, Instance)
end
return M
