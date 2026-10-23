-- Offline engine-boundary tests for the reflected player damage and movement
-- controls. These verify trainer behavior, not live game physics or damage.
package.path = "modules/astroneer/Scripts/?.lua;" .. package.path
local checks = 0
local function equal(actual, expected, message)
    checks = checks + 1
    assert(actual == expected, (message or "Mismatch") .. ": " .. tostring(actual) .. " ~= " .. tostring(expected))
end
local serial = 0
local function object(name, data)
    serial = serial + 1
    local address, writes, faults = serial, {}, {}
    local proxy = setmetatable({}, {
        __index = function(_, key) return data[key] end,
        __newindex = function(_, key, value)
            writes[#writes + 1] = { key = key, value = value }
            local fault = faults[key]
            if type(fault) == "function" then fault = fault(value) end
            if fault == "throw" then error("Mock write failure: " .. key) end
            if fault == "ignore" then return end
            if fault == "float32" then value = string.unpack("<f", string.pack("<f", value)) end
            data[key] = value
        end,
    })
    rawset(proxy, "IsValid", function() return not data.invalid end)
    rawset(proxy, "GetAddress", function() return address end)
    rawset(proxy, "GetFullName", function() return name end)
    return proxy, data, faults, writes
end
local world = object("World", {})
local character, character_data, character_faults, character_writes = object("LocalAstro", {
    Health = 100, MaxHealth = 100, Dying = false, bInvincible = false,
    bIsImmuneToDamage = false, Driving = false, bOnHoverboard = false,
    OxygenUseMultiplier = 1, bFreeOxygenDebug = false,
})
local is_local, authority, lifecycle = true, true, 9
rawset(character, "IsLocallyControlled", function() return is_local end)
rawset(character, "HasAuthority", function() return authority end)
rawset(character, "GetWorld", function() return world end)
local current_character = character
local mode = object("GameMode", {})
rawset(mode, "GetCurrentLifecycleState", function() return lifecycle end)
local gameplay = object("GameplayStatics", {})
rawset(gameplay, "GetGameMode", function() return authority and mode or nil end)
local function make_movement(name, speed)
    local movement, data, faults, writes = object(name, {
        MaxSpeed = speed, CurrentMovementMode = 0, SprintMultiplier = 1.8,
        MaxAcceleration = 2048, owner = character,
    })
    rawset(movement, "GetCharacterOwner", function() return data.owner end)
    return movement, data, faults, writes
end
local baseline = 600.25
local movement, movement_data, movement_faults, movement_writes = make_movement("Movement", baseline)
character_data.AstroMovementComponent = movement
local native_function = object("Function", {})
function StaticFindObject(path)
    if path == "/Script/Engine.Default__GameplayStatics" then return gameplay end
    if path:find(":", 1, true) then return native_function end
end
function FindAllOf() return current_character and { current_character } or {} end
function RegisterHook() return 1, 2 end
local trainer = dofile("modules/astroneer/Scripts/bindings.lua").new()
local ready, _, values, available = trainer:refresh()
equal(ready, true)
equal(available.invulnerability, true)
equal(available.player_run_speed, true)
equal(available.set_run_speed_multiplier, true)
equal(values.selected_run_speed_multiplier, 3)
equal(values.run_speed_multiplier, 1)
equal(values.invulnerability_active, false)

-- Selection changes no engine field while off; switching an enabled control
-- always multiplies the saved baseline and preserves the game's sprint factor.
equal(trainer:apply("set_run_speed_multiplier", false, 5), true)
equal(#movement_writes, 0)
equal(trainer:enabled().player_run_speed, false)
equal(trainer:apply("player_run_speed", true), true)
equal(movement.MaxSpeed, baseline * 5)
trainer:refresh(); trainer:refresh()
equal(movement.MaxSpeed, baseline * 5, "Repeated refreshes do not compound speed")
equal(movement.SprintMultiplier, 1.8)
equal(movement.MaxAcceleration, 2048)
for _, multiplier in ipairs({ 3, 10, 5 }) do
    equal(trainer:apply("set_run_speed_multiplier", false, multiplier), true)
    equal(movement.MaxSpeed, baseline * multiplier)
end
equal(trainer:apply("player_run_speed", false), true)
equal(movement.MaxSpeed, baseline)
local _, _, off_values = trainer:refresh()
equal(off_values.selected_run_speed_multiplier, 5)
equal(off_values.run_speed_multiplier, 1)
for _, invalid in ipairs({ 0, 1, 2, 4, 20, 100, 3.1, "3", true, {}, math.huge }) do
    equal(trainer:apply("set_run_speed_multiplier", false, invalid), false)
end
equal(trainer:apply("set_run_speed_multiplier", false, 0 / 0), false)
equal(trainer:apply("set_run_speed_multiplier", false), false)
equal(trainer:apply("set_run_speed_multiplier", true, 3), false)
equal(trainer:apply("set_run_speed_multiplier", nil, 3), false)
equal(trainer:apply("player_run_speed", true, 3), false)
equal(trainer:apply("invulnerability", true, 3), false)

-- Suspend speed outside on-foot walking and leave selection available. Returning
-- to walking resumes the chosen speed without changing vehicle/flight settings.
equal(trainer:apply("player_run_speed", true), true)
for _, movement_mode in ipairs({ 1, 2, 3, 4 }) do
    movement_data.CurrentMovementMode = movement_mode
    local _, _, suspended, suspended_available = trainer:refresh()
    equal(movement.MaxSpeed, baseline)
    equal(trainer:enabled().player_run_speed, true)
    equal(suspended.run_speed_multiplier, 1)
    equal(suspended_available.set_run_speed_multiplier, true)
end
movement_data.CurrentMovementMode = 0
character_data.Driving = true
trainer:refresh()
equal(movement.MaxSpeed, baseline)
equal(trainer:apply("set_run_speed_multiplier", false, 10), true)
equal(movement.MaxSpeed, baseline)
character_data.Driving = false
character_data.bOnHoverboard = true
trainer:refresh()
equal(movement.MaxSpeed, baseline)
character_data.bOnHoverboard = false
local _, _, walking_values = trainer:refresh()
equal(movement.MaxSpeed, baseline * 10)
equal(walking_values.run_speed_multiplier, 10)

-- Invulnerability sets the verified Blueprint damage gate, with no health
-- refill or edits to the game's separate immunity-initiator state.
equal(trainer:apply("invulnerability", true), true)
equal(character.bInvincible, true)
trainer:refresh()
equal(character.Health, 100)
equal(character.MaxHealth, 100)
equal(character.bIsImmuneToDamage, false)
for _, write in ipairs(character_writes) do
    equal(write.key == "Health" or write.key == "MaxHealth" or write.key == "bIsImmuneToDamage", false)
end
equal(trainer:apply("invulnerability", false), true)
equal(character.bInvincible, false)
character_data.bInvincible = true
equal(trainer:apply("invulnerability", true), true)
equal(trainer:apply("invulnerability", false), true)
equal(character.bInvincible, true, "A pre-existing invincibility flag is preserved")
character_data.bInvincible = false
equal(trainer:apply("invulnerability", true), true)
equal(trainer:disable_all(), true)
equal(character.bInvincible, false)
equal(movement.MaxSpeed, baseline)
local _, _, all_off = trainer:refresh()
equal(all_off.selected_run_speed_multiplier, 10)
equal(all_off.run_speed_multiplier, 1)

-- Remote ownership, menu transitions, lost possession and death remove the
-- effects and invalidate selections. None silently re-enable on recovery.
equal(trainer:apply("invulnerability", true), true)
equal(trainer:apply("player_run_speed", true), true)
authority = false
equal(trainer:refresh(), false)
equal(character.bInvincible, false)
equal(movement.MaxSpeed, baseline)
equal(trainer:apply("invulnerability", true), false)
equal(trainer:apply("player_run_speed", true), false)
authority = true
equal(trainer:refresh(), true)
equal(trainer:enabled().invulnerability, false)
equal(trainer:enabled().player_run_speed, false)
for _, lose_session in ipairs({ "menu", "possession", "death" }) do
    equal(trainer:apply("invulnerability", true), true)
    equal(trainer:apply("player_run_speed", true), true)
    if lose_session == "menu" then lifecycle = 8
    elseif lose_session == "possession" then is_local = false
    else character_data.Dying = true end
    equal(trainer:refresh(), false)
    equal(character.bInvincible, false)
    equal(movement.MaxSpeed, baseline)
    lifecycle, is_local, character_data.Dying = 9, true, false
    equal(trainer:refresh(), true)
    equal(trainer:enabled().player_run_speed, false)
end

-- Unknown fields, foreign components and unsupported movement modes fail
-- closed, instead of exposing a control backed by a fabricated binding.
character_data.bInvincible = nil
local _, _, _, missing_flag = trainer:refresh()
equal(missing_flag.invulnerability, false)
equal(trainer:apply("invulnerability", true), false)
character_data.bInvincible = false
movement_data.owner = object("RemoteAstro", {})
local _, _, _, foreign = trainer:refresh()
equal(foreign.player_run_speed, false)
equal(foreign.set_run_speed_multiplier, false)
equal(trainer:apply("set_run_speed_multiplier", false, 5), false)
equal(trainer:apply("player_run_speed", true), false)
movement_data.owner = character
for _, invalid_mode in ipairs({ 5, -1, 0.5, "0" }) do
    movement_data.CurrentMovementMode = invalid_mode
    local _, _, _, unsupported = trainer:refresh()
    equal(unsupported.player_run_speed, false)
end
movement_data.CurrentMovementMode = 0
character_data.Driving = nil
local _, _, _, unknown_driving = trainer:refresh()
equal(unknown_driving.player_run_speed, false)
character_data.Driving = false

-- Silent failed writes must never report enabled. A failed off keeps its
-- baseline queued; re-enable cannot recapture the currently modified value.
character_faults.bInvincible = function(value) return value and "ignore" end
equal(trainer:apply("invulnerability", true), false)
equal(trainer:enabled().invulnerability, false)
equal(character.bInvincible, false)
character_faults.bInvincible = nil
equal(trainer:apply("invulnerability", true), true)
character_faults.bInvincible = function(value) return not value and "ignore" end
equal(trainer:apply("invulnerability", false), false)
equal(character.bInvincible, true)
local _, pending_message, pending_invulnerability = trainer:refresh()
equal(pending_message, "Restoration pending; some original values could not be restored.")
equal(pending_invulnerability.restoration_pending, true)
equal(pending_invulnerability.invulnerability_active, true, "A failed off reports the observed flag truthfully")
equal(trainer:apply("invulnerability", true), false)
character_faults.bInvincible = nil
local _, _, restored_invulnerability = trainer:refresh()
equal(restored_invulnerability.restoration_pending, false)
equal(restored_invulnerability.invulnerability_active, false)
equal(trainer:enabled().invulnerability, false, "Automatic restoration does not re-enable")
equal(trainer:apply("invulnerability", true), true)
equal(trainer:apply("invulnerability", false), true)
equal(character.bInvincible, false)
movement_faults.MaxSpeed = function(value) return value ~= baseline and "ignore" end
equal(trainer:apply("player_run_speed", true), false)
equal(trainer:enabled().player_run_speed, false)
equal(movement.MaxSpeed, baseline)
movement_faults.MaxSpeed = nil
equal(trainer:apply("player_run_speed", true), true)
movement_faults.MaxSpeed = function(value) return value == baseline and "ignore" end
equal(trainer:apply("player_run_speed", false), false)
local _, _, pending_speed = trainer:refresh()
equal(pending_speed.restoration_pending, true)
equal(pending_speed.run_speed_multiplier, movement.MaxSpeed / baseline, "Failed off reports the remaining speed effect")
equal(pending_speed.run_speed_multiplier > 1, true)
equal(trainer:apply("player_run_speed", true), false)
movement_faults.MaxSpeed = nil
local _, _, restored_speed = trainer:refresh()
equal(restored_speed.restoration_pending, false)
equal(movement.MaxSpeed, baseline)
equal(trainer:apply("player_run_speed", true), true)
equal(trainer:apply("player_run_speed", false), true)
equal(movement.MaxSpeed, baseline)

-- Automatic suspension failure is visible even without an apply callback.
equal(trainer:apply("player_run_speed", true), true)
movement_data.CurrentMovementMode = 1
movement_faults.MaxSpeed = function(value) return value == baseline and "ignore" end
local _, suspension_message, failed_suspension = trainer:refresh()
equal(trainer:enabled().player_run_speed, false)
equal(failed_suspension.restoration_pending, true)
equal(failed_suspension.run_speed_multiplier, movement.MaxSpeed / baseline)
equal(suspension_message, "Restoration pending; some original values could not be restored.")
movement_faults.MaxSpeed = nil
trainer:refresh()
equal(movement.MaxSpeed, baseline)
movement_data.CurrentMovementMode = 0

-- A field can disappear between availability checking and baseline capture.
-- Reject that second read before any write, including non-finite float values.
local movement_mt = getmetatable(movement)
local movement_index = movement_mt.__index
for _, invalid_read in ipairs({ function() return nil end, function() return 0 / 0 end,
        function() return 0 end, function() return -1 end, function() return math.huge end }) do
    local reads = 0
    movement_mt.__index = function(obj, key)
        if key == "MaxSpeed" then
            reads = reads + 1
            if reads >= 3 then return invalid_read() end
        end
        return movement_index(obj, key)
    end
    local ok, applied = pcall(function() return trainer:apply("player_run_speed", true) end)
    equal(ok, true, "Failed baseline capture does not throw")
    equal(applied, false)
    equal(movement_data.MaxSpeed, baseline)
    movement_mt.__index = movement_index
end
local character_mt = getmetatable(character)
local character_index = character_mt.__index
local invulnerability_reads = 0
character_mt.__index = function(obj, key)
    if key == "bInvincible" then
        invulnerability_reads = invulnerability_reads + 1
        -- Availability and observed-status reads succeed; capture then fails.
        if invulnerability_reads >= 3 then return nil end
    end
    return character_index(obj, key)
end
equal(trainer:apply("invulnerability", true), false)
equal(character_data.bInvincible, false)
character_mt.__index = character_index

-- UE fields store float32; successful rounded writes are accepted without
-- mistaking normal representation changes for failed writes.
baseline = string.unpack("<f", string.pack("<f", 123.4567))
movement_data.MaxSpeed = baseline
movement_faults.MaxSpeed = "float32"
equal(trainer:apply("set_run_speed_multiplier", false, 5), true)
equal(trainer:apply("player_run_speed", true), true)
equal(movement.MaxSpeed ~= baseline * 5, true)
equal(math.abs(movement.MaxSpeed - baseline * 5) < baseline * 5 * 0.000001, true)
equal(trainer:apply("player_run_speed", false), true)
equal(movement.MaxSpeed, baseline)
movement_faults.MaxSpeed = nil

-- Overflow limits apply to the saved baseline, not an already boosted field.
baseline = 3.0e37
movement_data.MaxSpeed = baseline
equal(trainer:apply("set_run_speed_multiplier", false, 10), true)
equal(trainer:apply("player_run_speed", true), true)
equal(trainer:refresh(), true)
equal(trainer:enabled().player_run_speed, true)
equal(movement.MaxSpeed, baseline * 10)
equal(trainer:apply("player_run_speed", false), true)
movement_data.MaxSpeed = 3.5e37
equal(trainer:apply("player_run_speed", true), false)
equal(movement.MaxSpeed, 3.5e37)

-- Component replacement restores the old baseline and requires explicit
-- re-enable on the new component; an invalid old object never blocks a respawn.
baseline = 600.25
movement_data.MaxSpeed = baseline
equal(trainer:apply("player_run_speed", true), true)
local old_movement = movement
movement, movement_data, movement_faults = make_movement("NewMovement", 450.5)
character_data.AstroMovementComponent = movement
trainer:refresh()
equal(old_movement.MaxSpeed, baseline)
equal(trainer:enabled().player_run_speed, false)
equal(movement.MaxSpeed, 450.5)
equal(trainer:apply("player_run_speed", true), true)
movement_faults.MaxSpeed = "throw"
equal(trainer:apply("player_run_speed", false), false)
movement_data.invalid = true
movement, movement_data, movement_faults = make_movement("RespawnMovement", 500.5)
character_data.AstroMovementComponent = movement
world = object("NewWorld", {})
local _, _, respawn = trainer:refresh()
equal(respawn.selected_run_speed_multiplier, 3)
equal(respawn.run_speed_multiplier, 1)
equal(trainer:apply("player_run_speed", true), true)
equal(movement.MaxSpeed, 1501.5)
equal(trainer:disable_all(), true)
equal(movement.MaxSpeed, 500.5)

print("Astroneer player controls: " .. checks .. " assertions passed")
