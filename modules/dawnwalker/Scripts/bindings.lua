-- Contracts inspected from Steam build 25232147 / CL-258504, Unreal 5.5.4.
-- Calls must be made on the game thread. Only the possessed local player is used.
-- The game's Lock* functions replace pre-existing gameplay-effect handles, so
-- this module deliberately maintains resources with the native percent setters.
local M, Instance = {}, {}
Instance.__index = Instance
local IDS = { "dawnwalker_health", "dawnwalker_stamina", "dawnwalker_blood" }
local COMBAT = "/Script/DogwoodCombat.CombatComponentBase:"
local BLOOD = "/Script/DogwoodStats.BloodBarComponent:"
local MAX_CANDIDATES = 16
local source = debug.getinfo(1, "S").source:gsub("^@", ""):gsub("\\", "/")
local scripts = assert(source:match("^(.*)/[^/]+$"))
local EXTENSION_ACTIONS = {
    dawnwalker_invulnerable="combat", dawnwalker_damage="combat", dawnwalker_invisible="combat",
    dawnwalker_fly="movement", dawnwalker_teleport="movement", dawnwalker_auto_teleport="movement",
    dawnwalker_carry="economy", dawnwalker_money="economy", dawnwalker_items="economy", dawnwalker_xp="economy",
}
local ONESHOT = { dawnwalker_refill=true, dawnwalker_teleport=true, dawnwalker_xp=true }
local registered_properties = {}
local function register_property(name, owner, kind, offset)
    -- Pinned ItemHandle is an opaque, four-byte int32. Without this reflected
    -- field UE4SS converts a returned handle to {}, losing its native value.
    local item = name == "HappyCheatsItemHandleIndex" and owner == "/Script/DogwoodInventory.ItemHandle"
        and kind == "IntProperty" and offset == 0
    -- The pinned player AI stub exposes a target-side visibility bit in this
    -- byte. Combat owns only mask 0x10 and preserves every other bit.
    local player_visibility = name == "HappyCheatsPlayerStubVisibilityFlags" and owner == "/Script/DogwoodAI.DogwoodPlayerAIStub"
        and kind == "ByteProperty" and offset == 0x30
    -- Returned HitResult bitfields are misread as whole-byte booleans by the
    -- pinned loader. Expose the verified packed byte for read-only mask decoding.
    local hit_flags = name == "HappyCheatsHitResultFlags" and owner == "/Script/Engine.HitResult"
        and kind == "ByteProperty" and offset == 0xAD
    if not item and not player_visibility and not hit_flags then return false, "Unverified custom property" end
    if registered_properties[name] ~= nil then return registered_properties[name], "Cached custom property" end
    local ok, reason = pcall(function()
        RegisterCustomProperty({Name=name, Type=PropertyTypes[kind], BelongsToClass=owner, OffsetInternal=offset})
    end)
    registered_properties[name] = ok
    return ok, ok and "Custom property registered" or tostring(reason)
end

local function valid(object)
    if object == nil then return false end
    local ok, result = pcall(function() return object:IsValid() end)
    return ok and result == true
end
local function read(object, field)
    if not valid(object) then return nil end
    local ok, value = pcall(function() return object[field] end)
    if ok then return value end
end
local function invoke(object, method, ...)
    if not valid(object) then return false, "Object unavailable" end
    local args = table.pack(...)
    return pcall(function() return object[method](object, table.unpack(args, 1, args.n)) end)
end
local function write(object, field, value)
    if not valid(object) then return false, "Object unavailable" end
    return pcall(function() object[field] = value end)
end
local function identity(object)
    if not valid(object) then return "" end
    local ok, value = pcall(function()
        return tostring(object:GetAddress()) .. ":" .. object:GetFullName()
    end)
    return ok and value or ""
end
local function same(a, b)
    local key = identity(a)
    return key ~= "" and key == identity(b)
end
local function finite(number)
    return type(number) == "number" and number == number and math.abs(number) < math.huge
end
local function copy(source)
    local result = {}
    for key, value in pairs(source) do result[key] = value end
    return result
end
local function number_from(object, method)
    local ok, value = invoke(object, method)
    return ok and finite(value) and value or nil
end

function Instance:_changed(action, message)
    if self.on_change then pcall(self.on_change, action, self.flags[action] == true, message) end
end

function Instance:disable_all()
    -- Resource refills own no persistent lock. Extensions separately release
    -- only their recorded property overrides and native effect handles.
    for _, action in ipairs(IDS) do
        local enabled = self.flags[action]
        self.flags[action] = false
        if enabled then self:_changed(action, "Disabled") end
    end
    local success, messages = true, {}
    for _, extension in pairs(self.extensions) do
        local ok, message = extension:disable_all()
        if not ok then success = false; messages[#messages + 1] = message end
    end
    return success, success and "All controls disabled" or table.concat(messages, "; ")
end

function Instance:_owned(character)
    if not valid(character) then return false, "Player object unavailable" end
    local local_ok, local_player = invoke(character, "IsLocallyControlled")
    if not local_ok then return false, "Local-player check unavailable" end
    if local_player ~= true then return false, "Waiting for local player control" end
    local authority_ok, authority = invoke(character, "HasAuthority")
    if not authority_ok then return false, "Player authority check unavailable" end
    if authority ~= true then return false, "Waiting for player authority" end
    return true
end

function Instance:_resolve(path)
    -- Native setter functions are resolved once per player session. Repeated
    -- global lookups on every status poll are unnecessary; a cached wrapper is
    -- revalidated cheaply and an invalid/missing binding fails closed until the
    -- next session. Availability does not invoke the setter.
    local object = self.functions[path]
    if object == nil then
        local ok, found = pcall(StaticFindObject, path)
        object = ok and valid(found) and found or false
        self.functions[path] = object
    end
    if object ~= false and valid(object) then return object end
    self.functions[path] = false
    return nil
end
function Instance:_has_function(path) return self:_resolve(path) ~= nil end

function Instance:observe(character)
    -- Construction notifications only retain wrappers. Even IsValid/GetAddress
    -- are deferred until the regular game-thread poll. Raw equality must not
    -- dispatch a userdata equality metamethod while the object is constructing.
    if character == nil then return end
    for _, candidate in ipairs(self.candidates) do
        if rawequal(candidate, character) then return end
    end
    if #self.candidates >= MAX_CANDIDATES then
        -- We cannot inspect this new object here, and evicting an older pawn
        -- could hide ambiguous ownership. Keep the bounded cache unchanged and
        -- fail closed until restart instead of trusting an incomplete set.
        self.discovery_overflow = true
        return
    end
    self.candidates[#self.candidates + 1] = character
end

function Instance:_discover()
    -- Never scan the global object array during startup/menu polling. The
    -- notification is registered before normal gameplay creates its pawn;
    -- enabling/reloading this module after that requires a game restart.
    if self.discovery_overflow then return nil, "Trainer candidate limit reached; restart the game" end
    local retained, found, ambiguous = {}, nil, false
    local reason = "Waiting for a player object; load a save"
    for _, character in ipairs(self.candidates) do
        if valid(character) then
            retained[#retained + 1] = character
            local state, unavailable = self:_state(character)
            if state then
                if found and found.key ~= state.key then ambiguous = true end
                found = state
            elseif unavailable then
                reason = unavailable
            end
        end
    end
    self.candidates = retained
    -- Recheck every retained candidate, even when the previously selected pawn
    -- remains valid. A second local pawn must disable writes, not be ignored.
    if ambiguous then return nil, "More than one locally controlled player is available" end
    return found, reason
end

function Instance:_state(character)
    local owned, reason = self:_owned(character)
    if not owned then return nil, reason end
    local controller = read(character, "Controller")
    if not valid(controller) then return nil, "Waiting for player possession" end
    -- Controller.GetPawn is a C++ helper, not a reflected UFunction. The saved
    -- build's reflection exposes /Script/Engine.Controller.K2_GetPawn instead.
    local pawn_ok, pawn = invoke(controller, "K2_GetPawn")
    if not pawn_ok then return nil, "Controlled-pawn binding unavailable" end
    if not same(pawn, character) then return nil, "Waiting for player possession" end
    local combat = read(character, "CombatComponent")
    local owner_ok, owner = invoke(combat, "GetCharacter")
    if not owner_ok or not same(owner, character) then return nil, "Waiting for the player's combat component" end
    local alive_ok, alive = invoke(combat, "IsAlive")
    local pawn_alive_ok, pawn_alive = invoke(character, "IsAlive")
    if not alive_ok or not pawn_alive_ok then return nil, "Player liveness binding unavailable" end
    if alive ~= true or pawn_alive ~= true then return nil, "Waiting for a living player" end
    local health = number_from(combat, "GetHealthPercentage")
    local stamina = number_from(combat, "GetStaminaPercentage")
    if not health or health <= 0 or not stamina or stamina < 0 then return nil, "Player resources unavailable" end
    -- GetWorld is supplied by UE4SS's AActor wrapper, despite not appearing as
    -- an Unreal reflected function. Constructor notifications use that wrapper.
    local world_ok, world = invoke(character, "GetWorld")
    if not world_ok or identity(world) == "" then return nil, "Player world unavailable" end
    local blood = read(character, "BloodBar")
    return { character = character, controller = controller, world = world, combat = combat, blood = blood, health = health,
        stamina = stamina, blood_amount = number_from(blood, "GetBlood"),
        blood_capacity = number_from(blood, "GetBloodBarLength"),
        key = identity(world) .. "/" .. identity(controller) .. "/" .. identity(character)
            .. "/" .. identity(combat) .. "/" .. identity(blood) }
end

function Instance:_top_up(action, state)
    -- All ownership/liveness checks run immediately before an action, including
    -- command processing between regular status polls.
    local fresh = self:_state(state.character)
    if not fresh or fresh.key ~= self.key then return false, "Player session changed; enable again" end
    if action == "dawnwalker_health" then
        if fresh.health >= 0.9999 then return true end
        if not invoke(fresh.combat, "SetHealthPercent", 1.0) then return false, "Health setter failed" end
        local value = number_from(fresh.combat, "GetHealthPercentage")
        return value ~= nil and value >= 0.999, "Health setter did not refill the resource"
    elseif action == "dawnwalker_stamina" then
        if fresh.stamina >= 0.9999 then return true end
        if not invoke(fresh.combat, "SetStaminaPercent", 1.0) then return false, "Stamina setter failed" end
        local value = number_from(fresh.combat, "GetStaminaPercentage")
        return value ~= nil and value >= 0.999, "Stamina setter did not refill the resource"
    elseif action == "dawnwalker_blood" then
        if not finite(fresh.blood_amount) or fresh.blood_amount < 0
            or not finite(fresh.blood_capacity) or fresh.blood_capacity <= 0 then
            return false, "Blood bar unavailable"
        end
        if fresh.blood_amount >= fresh.blood_capacity * 0.9999 then return true end
        if not invoke(fresh.blood, "SetBloodPercent", 1.0) then return false, "Blood setter failed" end
        local value = number_from(fresh.blood, "GetBlood")
        return value ~= nil and value >= fresh.blood_capacity * 0.999, "Blood setter did not refill the resource"
    end
    return false, "Unknown top-up"
end

function Instance:refresh()
    local state, unavailable = self:_discover()
    local key = state and state.key or ""
    if key ~= self.key then
        self:disable_all()
        self.key = key
        self.functions = {}
    end
    self.character = state and state.character or nil
    self.ready = state ~= nil
    self.available = { dawnwalker_health = false, dawnwalker_stamina = false,
        dawnwalker_blood = false, dawnwalker_refill = false, disable_all = true }
    for action in pairs(EXTENSION_ACTIONS) do self.available[action] = false end
    self.values = { diagnostic_local_player = self.ready,
        diagnostic_player_candidates = #self.candidates,
        diagnostic_player_state = self.ready and "ready" or unavailable,
        diagnostic_health_mode = "Periodic top-up; lethal hits are not prevented" }
    if not state then
        self:disable_all()
        self.message = unavailable or "Load a save and take control of a living player"
        return false, self.message, copy(self.values), copy(self.available)
    end
    self.available.dawnwalker_health = self:_has_function(COMBAT .. "SetHealthPercent")
    self.available.dawnwalker_stamina = self:_has_function(COMBAT .. "SetStaminaPercent")
    self.available.dawnwalker_blood = finite(state.blood_amount) and state.blood_amount >= 0
        and finite(state.blood_capacity) and state.blood_capacity > 0
        and self:_has_function(BLOOD .. "SetBloodPercent")
    self.available.dawnwalker_refill = self.available.dawnwalker_health and self.available.dawnwalker_stamina
    self.values.health_percent = state.health * 100
    self.values.stamina_percent = state.stamina * 100
    self.values.blood = state.blood_amount
    self.values.blood_capacity = state.blood_capacity
    for _, extension in pairs(self.extensions) do
        local available, values = extension:refresh(state)
        for action, supported in pairs(available) do self.available[action] = supported end
        for name, value in pairs(values) do self.values[name] = value end
    end
    self.message = "Ready; health, stamina and blood use periodic top-ups"
    for _, action in ipairs(IDS) do
        if self.flags[action] then
            local ok = self.available[action] and self:_top_up(action, state)
            if not ok then
                self.flags[action] = false
                self.message = "A top-up became unavailable and was disabled"
                self:_changed(action, self.message)
            end
        end
    end
    return self.ready, self.message, copy(self.values), copy(self.available)
end

function Instance:apply(action, enabled)
    if action == "disable_all" then return self:disable_all() end
    local extension = self.extensions[EXTENSION_ACTIONS[action]]
    -- Turning off an owned override must remain possible after readiness drops.
    if extension and not ONESHOT[action] and enabled == false then
        return extension:apply(nil, action, false)
    end
    if self.flags[action] ~= nil and enabled == false then
        self.flags[action] = false
        self:_changed(action, "Disabled")
        return true, "Disabled"
    end
    if not extension and action ~= "dawnwalker_refill" and self.flags[action] == nil then return false, "Unknown action" end
    if ONESHOT[action] and enabled ~= false and enabled ~= nil then return false, "One-shot action cannot be enabled" end
    if not ONESHOT[action] and enabled ~= true then return false, "A boolean enabled value is required" end
    local command_key = self.key
    self:refresh()
    if command_key ~= self.key then return false, "Player session changed; try again" end
    if not self.ready then return false, self.message end
    if not self.available[action] then return false, "This binding is unavailable in the running build" end
    local state = self:_state(self.character)
    if not state or state.key ~= self.key then return false, "Player session changed; try again" end
    if extension then return extension:apply(state, action, enabled) end
    if action == "dawnwalker_refill" then
        local health_ok = self:_top_up("dawnwalker_health", state)
        local stamina_ok = self:_top_up("dawnwalker_stamina", state)
        if not health_ok or not stamina_ok then return false, "The game did not complete both top-ups" end
        self:_changed(action, "Health and stamina refilled")
        return true, "Health and stamina refilled"
    end
    local ok = self:_top_up(action, state)
    if not ok then return false, "The native top-up could not be applied" end
    self.flags[action] = true
    self:_changed(action, "Periodic top-up enabled")
    return true, "Periodic top-up enabled"
end

function Instance:enabled()
    local flags = copy(self.flags)
    for _, extension in pairs(self.extensions) do
        for action, enabled in pairs(extension:enabled()) do flags[action] = enabled end
    end
    return flags
end
function Instance:tick()
    local movement = self.extensions.movement
    if movement then return movement:tick() end
    return true
end
function Instance:process_auto_teleport()
    local movement = self.extensions.movement
    if movement and movement.process_auto_teleport then return movement:process_auto_teleport() end
end
function Instance:set_marker_observation_ready(ready)
    local movement = self.extensions.movement
    if movement and movement.set_marker_observation_ready then movement:set_marker_observation_ready(ready) end
end
function Instance:observe_marker(map, location)
    local movement = self.extensions.movement
    if movement and movement.observe_marker then movement:observe_marker(map, location) end
end
function Instance:forget_marker(map)
    local movement = self.extensions.movement
    if movement and movement.forget_marker then movement:forget_marker(map) end
end
function Instance:session_key() return self.key end
function M.new(on_change, factories)
    local result = setmetatable({ on_change = on_change, ready = false, key = "",
        flags = {}, extensions = {}, available = {}, values = {}, candidates = {}, functions = {}, message = "Waiting for the player" }, Instance)
    for _, action in ipairs(IDS) do result.flags[action] = false end
    local api = { valid=valid, read=read, write=write, invoke=invoke, same=same, identity=identity, finite=finite,
        register_property=register_property,
        has_function=function(path) return result:_has_function(path) end,
        resolve=function(path) return result:_resolve(path) end,
        fname=function(name) return FName(name) end }
    if factories == nil then
        factories = {}
        for _, name in ipairs({"combat", "movement", "economy"}) do factories[name] = dofile(scripts .. "/" .. name .. ".lua") end
    end
    for name, factory in pairs(factories) do result.extensions[name] = factory.new(api) end
    return result
end
return M
