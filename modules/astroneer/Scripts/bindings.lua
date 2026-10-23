-- Reflected bindings from AstroTechies/ModdingKit (master, 2026-09-13):
-- Source/Astro/Public/{ResourceCacheComponent,OnAcceptResourceDelegate,
-- ItemType,AstroCharacter,PowerComponent,ResearchSystem,AstroGameMode,
-- EGameLifecycleState,AstroCharacterMovementComponent,AstroMovementMode}.h
-- No offsets and no saved creative/free-oxygen flags are modified.
local Collection = require("collection")
local M, Instance = {}, {}
Instance.__index = Instance
local singleton
local HOOK = "/Script/Astro.ResourceCacheComponent:OnAcceptResource"
local IDS = { "resource_collection_10x", "unlimited_oxygen", "unlimited_power", "unlimited_soil", "invulnerability", "player_run_speed" }
local DEFAULT_RUN_SPEED = 3
local MAX_FLOAT = 3.4028234663852886e38
local MAX_BASE_SPEED = MAX_FLOAT / 10

local function valid(object)
    if object == nil then return false end
    local ok, result = pcall(function() return object:IsValid() end)
    return ok and result == true
end

local function read(object, field)
    if not valid(object) then return nil end
    local ok, result = pcall(function() return object[field] end)
    if ok then return result end
end

local function invoke(object, name, ...)
    if not valid(object) then return false, "Object is unavailable" end
    local args = table.pack(...)
    return pcall(function() return object[name](object, table.unpack(args, 1, args.n)) end)
end

local function identity(object)
    if not valid(object) then return "" end
    local ok, result = pcall(function()
        return tostring(object:GetAddress()) .. ":" .. object:GetFullName()
    end)
    return ok and result or ""
end

local function same(left, right)
    local key = identity(left)
    return key ~= "" and key == identity(right)
end

local function find(path)
    local ok, result = pcall(StaticFindObject, path)
    if ok and valid(result) then return result end
end

local function has_function(path) return valid(find(path)) end
local function finite(value)
    return type(value) == "number" and value == value and math.abs(value) < math.huge
end
local function run_multiplier(value)
    return type(value) == "number" and (value == 3 or value == 5 or value == 10)
end
local function equivalent(actual, expected)
    if type(expected) ~= "number" then return actual == expected end
    if actual == expected then return true end
    return finite(actual) and finite(expected) and expected ~= 0
        and math.abs(actual - expected) <= math.abs(expected) * 0.000001
end
local function write_field(object, field, value)
    if not valid(object) then return false end
    local ok = pcall(function() object[field] = value end)
    return ok and equivalent(read(object, field), value)
end
local function copy(source)
    local result = {}
    for key, value in pairs(source) do result[key] = value end
    return result
end

function Instance:_changed(action, message)
    if self.on_change then pcall(self.on_change, action, self.flags[action] == true, message) end
end

function Instance:_local_authority(character)
    local local_ok, local_control = invoke(character, "IsLocallyControlled")
    local authority_ok, authority = invoke(character, "HasAuthority")
    local is_local = local_ok and local_control == true
    local has_authority = authority_ok and authority == true
    -- Astroneer may run a solo world as a listen server. Network mode therefore
    -- cannot distinguish the user's own world from a remotely hosted world.
    return is_local and has_authority, has_authority, is_local
end

function Instance:_gameplay(character)
    local mode_ok, mode = invoke(self.gameplay_library, "GetGameMode", character)
    if not mode_ok or not valid(mode) then return false, -1 end
    local state_ok, state = invoke(mode, "GetCurrentLifecycleState")
    if not state_ok or type(state) ~= "number" then return false, -1 end
    -- EGameLifecycleState is declared by Astroneer: TitleMenu=8, GameStarted=9.
    -- Menu preview astronauts also have health/backpack/power, so possession
    -- alone cannot establish that a save is running.
    return state == 9, state
end

function Instance:_discover_character()
    local ok, characters = pcall(FindAllOf, "AstroCharacter")
    if not ok or type(characters) ~= "table" then return nil end
    for _, character in ipairs(characters) do
        local local_ok, is_local = invoke(character, "IsLocallyControlled")
        local health = read(character, "Health")
        if local_ok and is_local == true and finite(health) and health > 0
            and read(character, "Dying") ~= true then return character end
    end
end

function Instance:_power_component(character)
    local backpack_ok, backpack = invoke(character, "GetBackpack")
    if not backpack_ok or not valid(backpack) then return nil end
    local ok, power = invoke(self.power_library, "ActorPowerComponent", backpack)
    if not ok or not valid(power) then return nil end
    local capacity = read(power, "StorageCapacity")
    local charge_ok, charge = invoke(power, "GetCurrentCharge")
    if finite(capacity) and capacity > 0 and charge_ok and finite(charge) then return power end
end

function Instance:_movement_component(character)
    local movement = read(character, "AstroMovementComponent")
    local owner_ok, owner = invoke(movement, "GetCharacterOwner")
    if not owner_ok or not same(owner, character) then return nil end
    local speed, mode = read(movement, "MaxSpeed"), read(movement, "CurrentMovementMode")
    if not finite(speed) or speed <= 0 or speed > MAX_FLOAT
        or type(mode) ~= "number" or mode % 1 ~= 0 or mode < 0 or mode > 4
        or type(read(character, "Driving")) ~= "boolean"
        or type(read(character, "bOnHoverboard")) ~= "boolean" then return nil end
    return movement
end

function Instance:_restore(action)
    local records = self.originals[action] or {}
    local remaining = {}
    for _, record in ipairs(records) do
        if valid(record.object) and identity(record.object) == record.identity then
            local ok
            if record.setter then
                ok = invoke(record.object, record.setter, record.value)
            else
                ok = write_field(record.object, record.field, record.value)
            end
            if not ok then remaining[#remaining + 1] = record end
        end
    end
    self.originals[action] = #remaining > 0 and remaining or nil
    return #remaining == 0
end

function Instance:_remember(action, object, field, value, setter)
    local records = self.originals[action] or {}
    self.originals[action] = records
    records[#records + 1] = { object = object, identity = identity(object),
        field = field, value = value, setter = setter }
end

function Instance:_restoration_pending()
    for action, records in pairs(self.originals) do
        if not self.flags[action] and #records > 0 then return true end
    end
    return false
end

function Instance:disable_all()
    local restored = true
    self.run_speed_active = false
    for _, action in ipairs(IDS) do
        local was_enabled = self.flags[action]
        self.flags[action] = false
        if not self:_restore(action) then restored = false end
        if was_enabled then self:_changed(action, "Disabled") end
    end
    return restored, restored and "All features disabled" or "Disabled; an unloaded object could not be restored"
end

function Instance:_maintain_run_speed()
    self.run_speed_active = false
    if not self.flags.player_run_speed then return end
    local records = self.originals.player_run_speed
    local record = records and records[1]
    local movement = self:_movement_component(self.character)
    if not record or not same(record.object, movement) then
        self.flags.player_run_speed = false
        self:_restore("player_run_speed")
        self:_changed("player_run_speed", "Movement component changed; enable again")
        return
    end
    local on_foot = read(movement, "CurrentMovementMode") == 0
        and read(self.character, "Driving") == false and read(self.character, "bOnHoverboard") == false
    -- MaxSpeed is also consumed by non-walking physics. Keep the original
    -- value there; do not alter flight, sliding, hoverboard or vehicle speeds.
    -- Always multiply the saved baseline, never the current modified value.
    local target = record.value * (on_foot and self.selected_run_speed or 1)
    if not write_field(movement, "MaxSpeed", target) then
        self.flags.player_run_speed = false
        self:_restore("player_run_speed")
        self:_changed("player_run_speed", "Run speed is unavailable")
        return
    end
    self.run_speed_active = on_foot
end

function Instance:_maintain_invulnerability()
    if not self.flags.invulnerability then return end
    local records = self.originals.invulnerability
    local record = records and records[1]
    -- DesignAstro's ReceiveAnyDamage checks !bInvincible before calling Deal
    -- Damage. Set that gate; never refill Health or alter immunity initiators.
    if not record or not same(record.object, self.character)
        or not write_field(self.character, "bInvincible", true) then
        self.flags.invulnerability = false
        self:_restore("invulnerability")
        self:_changed("invulnerability", "Invulnerability is unavailable")
    end
end

function Instance:_resource_hook(context, resource_param, amount_param)
    if self.in_hook or not self.ready or not valid(self.character) then return end
    self.in_hook = true
    local ok, err = pcall(function()
        -- Recheck possession and authority for every write, including between
        -- polls and when a listen host changes sessions or loses its pawn.
        if not self:_local_authority(self.character) or not self:_gameplay(self.character) then return end
        local cache, resource = context:get(), resource_param:get()
        if not valid(cache) or not valid(resource) then return end
        local owner_ok, owner = invoke(cache, "GetCharacter")
        if not owner_ok or not same(owner, self.character) then return end
        -- Power/oxygen fragments share this handler in legacy builds. Never
        -- multiply them, and fail closed if the resource CDO cannot be resolved.
        local cdo_ok, cdo = invoke(resource, "GetCDO")
        if not cdo_ok or not valid(cdo) then return end
        local is_power, is_oxygen = read(cdo, "IsPower"), read(cdo, "IsOxygen")
        if type(is_power) ~= "boolean" or type(is_oxygen) ~= "boolean"
            or is_power or is_oxygen then return end
        local original = amount_param:get()
        if not Collection.is_positive_int32(original) then
            self.counters.collection_rejected_events = self.counters.collection_rejected_events + 1
            return
        end
        local total = original
        if self.flags.resource_collection_10x then
            total = Collection.multiply(original, self.selected_multiplier)
            if not total then
                self.counters.collection_rejected_events = self.counters.collection_rejected_events + 1
                return
            end
            amount_param:set(total)
        end
        local name_ok, resource_name = pcall(function() return resource:GetFullName() end)
        Collection.record(self.counters, original, total, name_ok and resource_name or "")
    end)
    self.in_hook = false
    if not ok then
        self.flags.resource_collection_10x = false
        self.hook_error = tostring(err):sub(1, 300)
        self:_changed("resource_collection_10x", "Collection hook unavailable: " .. self.hook_error)
    end
end

function Instance:_install_hook()
    if self.hook_ids or self.hook_error then return end
    if not has_function(HOOK) then return end
    local ok, pre, post = pcall(RegisterHook, HOOK, function(context, resource, amount)
        self:_resource_hook(context, resource, amount)
    end)
    if ok and type(pre) == "number" and type(post) == "number" then
        self.hook_ids = { pre, post }
    else
        self.hook_error = ok and "Hook registration did not return IDs" or tostring(pre):sub(1, 300)
    end
end

function Instance:_maintain()
    if not self.ready or not self:_local_authority(self.character) or not self:_gameplay(self.character) then
        self:disable_all()
        return
    end
    self:_maintain_invulnerability()
    self:_maintain_run_speed()
    if self.flags.unlimited_oxygen then
        local ok, err = pcall(function()
            self.character.OxygenUseMultiplier = 0
            self.character.bFreeOxygenDebug = true
        end)
        if not ok then
            self.flags.unlimited_oxygen = false
            self:_restore("unlimited_oxygen")
            self:_changed("unlimited_oxygen", tostring(err))
        end
    end
    if self.flags.unlimited_power then
        local records = self.originals.unlimited_power
        local power = records and records[1] and records[1].object
        if not valid(power) or not same(power, self:_power_component(self.character)) then
            self.flags.unlimited_power = false
            self:_restore("unlimited_power")
            self:_changed("unlimited_power", "Backpack changed; enable again")
            return
        end
        local capacity = read(power, "StorageCapacity")
        local ok = finite(capacity) and capacity > 0 and invoke(power, "SetCurrentCharge", capacity)
        if not ok then
            self.flags.unlimited_power = false
            self:_restore("unlimited_power")
            self:_changed("unlimited_power", "Backpack power is unavailable")
        end
    end
end

function Instance:refresh()
    self.gameplay_library = find("/Script/Engine.Default__GameplayStatics")
    self.power_library = find("/Script/Astro.Default__PowerComponent")
    self.research_library = find("/Script/Astro.Default__ResearchSystem")
    self:_install_hook()
    local character = self:_discover_character()
    local world_ok, world = invoke(character, "GetWorld")
    local game_started, lifecycle = self:_gameplay(character)
    local local_authority, authority, local_control = self:_local_authority(character)
    local new_key = valid(character) and world_ok and valid(world)
        and identity(world) .. "/" .. identity(character) .. "/lifecycle:" .. tostring(lifecycle)
            .. "/authority:" .. tostring(authority) .. "/local:" .. tostring(local_control) or ""
    if new_key ~= self.key then
        self:disable_all()
        self.key = new_key
        self.counters = Collection.new_counters()
        self.selected_multiplier = Collection.DEFAULT_MULTIPLIER
        self.selected_run_speed = DEFAULT_RUN_SPEED
        self.character = character
    end
    self.ready = new_key ~= "" and game_started and local_authority
    self.available = { resource_collection_10x = false, set_collection_multiplier = false, unlimited_oxygen = false,
        unlimited_power = false, unlimited_soil = false, invulnerability = false, player_run_speed = false, set_run_speed_multiplier = false,
        add_bytes = false, disable_all = true }
    if not self.ready then
        self:disable_all()
        if not valid(character) then
            self.message = "Waiting for your astronaut to spawn."
        elseif not authority then
            self.message = "This world is hosted elsewhere. Run the trainer on the host."
        else
            self.message = "Load a save and enter the world"
        end
    else
        self.character = character
        -- Off remains off after a transient failure, but captured values must
        -- still be restored. Retry without requiring another enable/off cycle.
        for _, action in ipairs(IDS) do
            if not self.flags[action] and self.originals[action] then self:_restore(action) end
        end
        self.available.resource_collection_10x = self.hook_ids ~= nil and self.hook_error == nil
        self.available.set_collection_multiplier = self.available.resource_collection_10x
        self.available.unlimited_oxygen = finite(read(character, "OxygenUseMultiplier"))
            and type(read(character, "bFreeOxygenDebug")) == "boolean"
        self.available.unlimited_power = valid(self:_power_component(character))
            and has_function("/Script/Astro.PowerComponent:SetCurrentCharge")
        self.available.invulnerability = type(read(character, "bInvincible")) == "boolean"
        self.available.player_run_speed = valid(self:_movement_component(character))
        self.available.set_run_speed_multiplier = self.available.player_run_speed
        -- Soil is intentionally unavailable until an actual canister source is
        -- verified. DeformTool.TotalSedimentAvailable is only a derived total.
        self.available.add_bytes = valid(self.research_library)
            and has_function("/Script/Astro.ResearchSystem:AuthorityGrantResearchPoints")
        self.message = self.hook_error and "Collection binding failed: " .. self.hook_error
            or "Ready on this host; collection counts measure terrain fragments"
        self:_maintain()
    end
    local values = copy(self.counters)
    values.restoration_pending = self:_restoration_pending()
    if values.restoration_pending then
        self.message = "Restoration pending; some original values could not be restored."
    end
    values.diagnostic_lifecycle = lifecycle
    values.diagnostic_authority = authority
    values.diagnostic_local_control = local_control
    local world_name_ok, world_name = invoke(world, "GetFullName")
    values.diagnostic_world = world_name_ok and world_name or ""
    values.oxygen = read(character, "OxygenLevel")
    local power = self.ready and self:_power_component(character) or nil
    if valid(power) then
        local ok, charge = invoke(power, "GetCurrentCharge")
        if ok then values.power = charge end
        values.power_capacity = read(power, "StorageCapacity")
    end
    if self.ready and valid(self.research_library) then
        local ok, controller = invoke(character, "GetPlayController")
        if ok and valid(controller) then
            local balance_ok, balance = invoke(self.research_library, "GetCurrentResearchPointBalance", controller)
            if balance_ok then values.bytes = balance end
        end
    end
    values.collection_multiplier = self.flags.resource_collection_10x and self.selected_multiplier or 1
    values.selected_collection_multiplier = self.selected_multiplier
    values.selected_run_speed_multiplier = self.selected_run_speed
    values.run_speed_multiplier = self.flags.player_run_speed and self.run_speed_active and self.selected_run_speed or 1
    local pending_speed = not self.flags.player_run_speed and self.originals.player_run_speed
    if pending_speed and pending_speed[1] then
        local record = pending_speed[1]
        local current_movement = read(character, "AstroMovementComponent")
        local observed = read(record.object, "MaxSpeed")
        -- A requested-off state is not proof that a failed restore succeeded.
        -- Expose the remaining observed effect, or omit an unreadable value.
        values.run_speed_multiplier = same(record.object, current_movement)
            and finite(observed) and finite(record.value) and record.value > 0
            and observed / record.value or nil
    end
    -- Report the observed gate even if an off request is waiting to restore it.
    values.invulnerability_active = read(character, "bInvincible")
    values.collection_hook_registered = self.hook_ids ~= nil
    values.collection_scope = "Local terrain resource fragments; backpack capacity still applies"
    values.soil_status = "Canister binding not yet verified"
    return self.ready, self.message, values, copy(self.available)
end

function Instance:apply(action, enabled, multiplier)
    if action == "set_collection_multiplier" then
        if enabled ~= false or not Collection.is_multiplier(multiplier) then
            return false, "Select a collection multiplier of 10, 20 or 100 without enabling the toggle."
        end
    elseif action == "set_run_speed_multiplier" then
        if enabled ~= false or not run_multiplier(multiplier) then
            return false, "Select a run speed multiplier of 3, 5 or 10 without enabling the toggle."
        end
    elseif multiplier ~= nil then
        return false, "Multiplier is only valid for a multiplier selection control."
    end
    if action == "disable_all" then return self:disable_all() end
    -- Off must remain possible after session loss or binding failure.
    if self.flags[action] ~= nil and enabled == false then
        self.flags[action] = false
        if action == "player_run_speed" then self.run_speed_active = false end
        local restored = self:_restore(action)
        self:_changed(action, "Disabled")
        return restored, restored and "Disabled" or "Disabled; restore failed"
    end
    self:refresh()
    if not self.ready then return false, self.message end
    if not self.available[action] then
        return false, action == "unlimited_soil" and "Soil canister binding has not been verified"
            or "Feature unavailable in this game build: " .. tostring(action)
    end
    if action == "set_collection_multiplier" then
        self.selected_multiplier = multiplier
        local message = "Collection multiplier set to " .. tostring(multiplier) .. "x"
        self:_changed(action, message)
        return true, message
    end
    if action == "set_run_speed_multiplier" then
        local previous, was_enabled = self.selected_run_speed, self.flags.player_run_speed
        self.selected_run_speed = multiplier
        self:_maintain_run_speed()
        if was_enabled and not self.flags.player_run_speed then
            self.selected_run_speed = previous
            return false, "Run speed is unavailable"
        end
        local message = "Run speed multiplier set to " .. tostring(multiplier) .. "x"
        self:_changed(action, message)
        return true, message
    end
    if action == "add_bytes" then
        local ok, granted = invoke(self.research_library, "AuthorityGrantResearchPoints", self.character, 10000, false)
        if not ok or granted ~= true then return false, "Game did not confirm the byte grant" end
        self:_changed(action, "Added 10,000 bytes")
        return true, "Added 10,000 bytes"
    end
    if enabled ~= true then return false, "A boolean enabled value is required" end
    if self.flags[action] then return true, "Already enabled" end
    -- A failed off request retains its original values for retry. Finish that
    -- restoration before taking another baseline; otherwise re-enabling would
    -- remember the still-modified value and overwrite the original on next off.
    if not self:_restore(action) then return false, "Disabled; restore failed" end
    if action == "unlimited_oxygen" then
        self:_remember(action, self.character, "OxygenUseMultiplier", read(self.character, "OxygenUseMultiplier"))
        self:_remember(action, self.character, "bFreeOxygenDebug", read(self.character, "bFreeOxygenDebug"))
    elseif action == "invulnerability" then
        local baseline = read(self.character, "bInvincible")
        if type(baseline) ~= "boolean" then return false, "Invulnerability is unavailable" end
        self:_remember(action, self.character, "bInvincible", baseline)
    elseif action == "unlimited_power" then
        local power = self:_power_component(self.character)
        local ok, charge = invoke(power, "GetCurrentCharge")
        if not ok then return false, "Backpack power is unavailable" end
        self:_remember(action, power, "CurrentCharge", charge, "SetCurrentCharge")
    elseif action == "player_run_speed" then
        local movement = self:_movement_component(self.character)
        if not valid(movement) then return false, "Run speed is unavailable" end
        local baseline = read(movement, "MaxSpeed")
        if not finite(baseline) or baseline <= 0 or baseline > MAX_BASE_SPEED then
            return false, "Run speed is unavailable"
        end
        self:_remember(action, movement, "MaxSpeed", baseline)
    end
    self.flags[action] = true
    self:_maintain()
    if not self.flags[action] then return false, "Feature could not be applied" end
    self:_changed(action, "Enabled")
    if action == "resource_collection_10x" then
        return true, tostring(self.selected_multiplier) .. "x terrain collection enabled"
    elseif action == "player_run_speed" then
        return true, tostring(self.selected_run_speed) .. "x on-foot run speed enabled"
    elseif action == "invulnerability" then
        return true, "Invulnerability enabled"
    end
    return true, "Enabled"
end

function Instance:enabled() return copy(self.flags) end
function Instance:session_key() return self.key end

function M.new(on_change)
    -- One module instance owns the hook, preventing duplicate 10x pre-hooks.
    if singleton then
        if on_change then singleton.on_change = on_change end
        return singleton
    end
    singleton = setmetatable({ on_change = on_change, key = "", ready = false,
        message = "Load a save and enter the world", flags = {}, available = {}, originals = {},
        counters = Collection.new_counters(), selected_multiplier = Collection.DEFAULT_MULTIPLIER,
        selected_run_speed = DEFAULT_RUN_SPEED, run_speed_active = false, in_hook = false }, Instance)
    for _, action in ipairs(IDS) do singleton.flags[action] = false end
    singleton:_install_hook()
    return singleton
end

return M
