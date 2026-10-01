-- Real movement module against reflected engine boundary fixtures. No game calls.
local checks = 0
local function equal(actual, expected, message)
    checks = checks + 1
    assert(actual == expected, (message or "Mismatch") .. ": " .. tostring(actual) .. " ~= " .. tostring(expected))
end
local function object(name) return { name = name, valid = true } end
local function fixture()
    local f = { calls = 0, lookups = 0, fnames = 0, setters = {}, inputs = {}, keys = {},
        local_player = true, authority = true, alive = true, ignored = false, analog = {}, delta = 1 / 60,
        traces = {}, travels = {}, marker_present = true, ground_z = 250, normal_z = 1, property_registrations = 0 }
    f.pawn, f.controller, f.world, f.movement = object("pawn"), object("controller"), object("world"), object("movement")
    f.pawn.Controller, f.pawn.CharacterMovement = f.controller, f.movement
    f.world.OwningGameInstance = object("game instance")
    f.world_state, f.partition = object("world state"), object("partition")
    f.possessed, f.owner = f.pawn, f.pawn
    f.movement.MovementMode, f.movement.CustomMovementMode = 1, 0
    f.movement.WalkableFloorZ = 0.7
    f.position = { X = 0, Y = 0, Z = 0 }
    -- Captured CoreUObject.Rotator has lowercase pitch. The pinned loader uses
    -- exact reflected spellings both for return tables and incoming arguments.
    f.rotator_fields = { "pitch", "Yaw", "Roll" }
    f.rotation = { pitch = -12.5, Yaw = 45, Roll = 7.25 }
    f.gameplay, f.system, f.subsystems, f.map_library, f.map, f.travel, f.capsule =
        object("gameplay"), object("system"), object("subsystems"), object("map_library"), object("map"), object("travel"), object("capsule")
    f.pawn.CapsuleComponent = f.capsule
    f.radius, f.height = 40, 90
    f.first_player = f.controller
    f.state = { character = f.pawn, controller = f.controller, world = f.world, key = "world/controller/pawn" }
    function f.pawn:IsLocallyControlled() return f.local_player end
    function f.pawn:HasAuthority() return f.authority end
    function f.pawn:IsAlive() return f.alive end
    function f.pawn:GetWorld() return f.world end
    function f.pawn:K2_GetActorLocation() return f.position end
    function f.pawn:K2_GetActorRotation()
        if f.rotation_error then error("rotation unavailable") end
        return f.rotation
    end
    function f.controller:K2_GetPawn() return f.possessed end
    function f.controller:IsMoveInputIgnored()
        if f.ignore_error then error("input suppression unavailable") end
        return f.ignored
    end
    function f.movement:GetOwner() return f.owner end
    function f.movement:SetMovementMode(mode, custom)
        f.setters[#f.setters + 1] = { mode = mode, custom = custom }
        if f.setter_error then error("native setter error") end
        if not f.noop then self.MovementMode, self.CustomMovementMode = mode, custom end
    end
    local function key_name(key)
        assert(type(key) == "table" and type(key.KeyName) == "table" and key.KeyName.is_fname,
            "FKey requires native FName userdata, represented by a tagged fixture")
        return key.KeyName.value
    end
    function f.controller:IsInputKeyDown(key)
        local name = key_name(key)
        if f.input_error then error("input unavailable") end
        return f.keys[name] == true
    end
    function f.controller:GetInputAnalogKeyState(key) return f.analog[key_name(key)] or 0 end
    function f.pawn:K2_AddActorWorldOffset(direction, sweep, hit, teleport)
        assert(direction.X == 0 and direction.Y == 0 and sweep == true and teleport == false and type(hit) == "table",
            "Flight sweeps only vertical displacement and never teleports physics")
        if f.input_write_error then error("input failed") end
        f.inputs[#f.inputs + 1] = direction.Z
        local flags = f.flight_flags or (f.flight_blocked and 1 or 0)
        if not f.register_failed and not f.register_error then hit.HappyCheatsHitResultFlags = flags end
        hit.bBlockingHit, hit.bStartPenetrating = flags ~= 0, flags ~= 0
    end
    function f.gameplay:GetWorldDeltaSeconds(character) assert(character == f.pawn); return f.delta end
    function f.gameplay:GetPlayerController(character, index) assert(character == f.pawn and index == 0); return f.first_player end
    function f.map_library:IsSetUserMappin(map) assert(map == f.map); return f.marker_present end
    function f.subsystems:GetGameInstanceSubsystem(character, class)
        if class.path == "/Script/DogwoodSystem.WorldStateSystemInterface" then return f.world_state end
        assert(character == f.pawn and class.path == "/Script/DogwoodMap.MappinSystemImpl")
        return f.map
    end
    function f.subsystems:GetWorldSubsystem(character, class)
        if class.path == "/Script/Engine.WorldPartitionSubsystem" then return f.partition end
        assert(character == f.pawn and class.path == "/Script/DogwoodMap.FastTravelSystem")
        return f.travel
    end
    function f.capsule:GetScaledCapsuleRadius() return f.radius end
    function f.capsule:GetScaledCapsuleHalfHeight() return f.height end
    function f.capsule:GetOwner() return f.capsule_owner or f.pawn end
    function f.system:LineTraceSingle(character, start, finish, channel, complex, ignore, debug, hit, ignore_self, color, hit_color, duration)
        assert(character == f.pawn and start.Z == 1000000 and finish.Z == -1000000 and start.X == finish.X and start.Y == finish.Y,
            "Ground query matches the captured map Blueprint")
        assert(channel == 0 and complex == false and ignore[1] == f.pawn and debug == 0 and ignore_self == true and duration == 0)
        assert(type(color) == "table" and type(hit_color) == "table" and type(hit) == "table")
        f.traces[#f.traces + 1] = "ground"
        if f.trace_error then error("trace unavailable") end
        local flags = (not f.no_ground and 1 or 0) | (f.ground_penetrating and 2 or 0)
        if f.ground_flags ~= nil then flags = f.ground_flags end
        if not f.missing_ground_flags then hit.HappyCheatsHitResultFlags = flags end
        -- Reproduce the pinned UE4SS OUT converter: both packed bool fields
        -- are incorrectly read as the whole byte, even for ordinary ground.
        hit.bBlockingHit, hit.bStartPenetrating = flags ~= 0, flags ~= 0
        hit.Location = { X = start.X, Y = start.Y, Z = f.ground_z }
        hit.ImpactNormal = { X = 0, Y = 0, Z = f.normal_z }
        if f.change_marker_during_trace then f.trainer:forget_marker() end
        if f.change_owner_during_trace then f.possessed = object("other") end
        return not f.no_ground
    end
    function f.system:CapsuleTraceSingle(character, start, finish, radius, height, channel, complex, ignore, debug, hit, ignore_self, color, hit_color, duration)
        assert(character == f.pawn and start.Z == f.ground_z + f.height + 5 and finish.Z == start.Z + 1)
        assert(radius == f.radius and height == f.height and channel == 0 and complex == false and ignore[1] == f.pawn and debug == 0 and ignore_self)
        assert(type(color) == "table" and type(hit_color) == "table" and duration == 0)
        f.traces[#f.traces + 1] = "capsule"
        if f.clearance_error then error("capsule unavailable") end
        local flags = (f.obstructed and 1 or 0) | (f.clearance_penetrating and 2 or 0)
        if f.clearance_flags ~= nil then flags = f.clearance_flags end
        if not f.missing_clearance_flags then hit.HappyCheatsHitResultFlags = flags end
        hit.bBlockingHit, hit.bStartPenetrating = flags ~= 0, flags ~= 0
        return f.obstructed == true
    end
    function f.travel:FastTravelToLocation(location, rotation)
        -- Reproduce convert_lua_table_to_struct: each reflected field is read
        -- with lua_rawget; a mismatched key silently leaves its default zero.
        local native_rotation = {}
        for _, field in ipairs(f.rotator_fields) do native_rotation[field] = rawget(rotation, field) or 0 end
        f.travels[#f.travels + 1] = { location = location, rotation = native_rotation }
        if f.marker_during_travel then f.trainer:observe_marker(f.map, f.marker_during_travel) end
        if f.travel_error then error("travel uncertain") end
        if f.travel_result == 0 or f.travel_result == 1 then return f.travel_result end
        f.movement.MovementMode = 0
        return f.travel_result or 2
    end
    local api = {
        valid = function(value) f.calls = f.calls + 1; return type(value) == "table" and value.valid == true end,
        read = function(value, field) f.calls = f.calls + 1; return value and value[field] end,
        invoke = function(value, method, ...)
            f.calls = f.calls + 1
            if not value or value.valid ~= true then return false end
            return pcall(value[method], value, ...)
        end,
        same = function(a, b) f.calls = f.calls + 1; return a ~= nil and a == b end,
        finite = function(value) return type(value) == "number" and value == value and math.abs(value) < math.huge end,
        has_function = function(path) f.lookups = f.lookups + 1; return f.missing ~= path end,
        register_property = function(name, owner, kind, offset)
            f.property_registrations = f.property_registrations + 1
            assert(name == "HappyCheatsHitResultFlags" and owner == "/Script/Engine.HitResult"
                and kind == "ByteProperty" and offset == 0xAD, "Only the verified HitResult flag byte is registered")
            if f.register_error then error("property unavailable") end
            return not f.register_failed
        end,
        resolve = function(path)
            if f.missing_object == path then return nil end
            local libraries = {
                ["/Script/Engine.Default__GameplayStatics"] = f.gameplay,
                ["/Script/Engine.Default__KismetSystemLibrary"] = f.system,
                ["/Script/Engine.Default__SubsystemBlueprintLibrary"] = f.subsystems,
                ["/Script/DogwoodMap.Default__MappinSystemBlueprintLibrary"] = f.map_library,
            }
            if libraries[path] then return libraries[path] end
            local class = object(path); class.path = path; return class
        end,
        fname = function(name)
            f.fnames = f.fnames + 1
            if f.fname_error then error("name unavailable") end
            return { is_fname = true, value = name }
        end,
    }
    f.trainer = assert(loadfile("modules/dawnwalker/Scripts/movement.lua"))().new(api)
    return f
end
local function enable(f)
    equal(f.trainer:apply(f.state, "dawnwalker_fly", true), true, "Enable native flight")
    equal(f.movement.MovementMode, 5)
    equal(f.trainer:enabled().dawnwalker_fly, true)
end

do
    local f = fixture()
    for _ = 1, 10000 do f.trainer:tick() end
    equal(f.calls, 0, "Disabled flight tick performs no engine calls")
    equal(f.lookups, 0, "Disabled flight does not resolve functions")
    equal(f.fnames, 0, "Disabled flight creates no names")
    local available = f.trainer:refresh(f.state)
    equal(available.dawnwalker_fly, true)
    equal(f.fnames, 0, "Read-only availability does not create input keys")
    equal(#f.setters, 0, "Availability does not change movement")
    enable(f)
    equal(f.fnames, 4, "Input keys are created once")
    f.keys.SpaceBar = true
    local lookups = f.lookups
    for _ = 1, 1000 do f.trainer:tick() end
    equal(#f.inputs, 1000, "Held ascent uses a collision sweep each frame instead of discarded pawn input")
    equal(f.inputs[1], 10)
    equal(f.lookups, lookups, "Active tick never runs a global function lookup")
    equal(f.fnames, 4, "Active tick reuses FKey/FName values")
    equal(f.trainer:disable_all(), true)
    equal(f.movement.MovementMode, 1, "Original mode restored")
    equal(f.movement.CustomMovementMode, 0)
    equal(#f.setters, 2, "Only enabling and disabling change movement mode")
    equal(f.trainer:enabled().dawnwalker_fly, false)
    local calls = f.calls
    f.trainer:tick()
    equal(f.calls, calls)
end

do
    local f = fixture(); enable(f)
    f.keys.SpaceBar, f.keys.LeftControl = true, true
    f.trainer:tick(); equal(#f.inputs, 0, "Opposing keys cancel")
    f.keys.SpaceBar = false
    f.trainer:tick(); equal(f.inputs[#f.inputs], -10)
    f.keys.LeftControl = false
    f.analog.Gamepad_RightTriggerAxis = 0.6
    f.analog.Gamepad_LeftTriggerAxis = 0.2
    f.trainer:tick(); equal(math.abs(f.inputs[#f.inputs] - 4) < 0.0001, true)
    f.analog.Gamepad_RightTriggerAxis, f.analog.Gamepad_LeftTriggerAxis = 0.05, 0.05
    local inputs = #f.inputs
    f.trainer:tick(); equal(#f.inputs, inputs, "Trigger noise stays in dead zone")
    f.analog.Gamepad_RightTriggerAxis, f.keys.SpaceBar = 2, true
    f.trainer:tick(); equal(f.inputs[#f.inputs], 10, "Combined input is bounded")
    f.ignored = true
    inputs = #f.inputs
    f.trainer:tick(); equal(#f.inputs, inputs, "Menus/cinematics can suppress movement input")
end

for _, condition in ipairs({ "possession", "controller", "world", "component", "owner", "authority", "local" }) do
    local f = fixture(); enable(f); f.keys.SpaceBar = true
    if condition == "possession" then f.possessed = object("other")
    elseif condition == "controller" then f.pawn.Controller = object("other")
    elseif condition == "world" then f.world = object("other")
    elseif condition == "component" then f.pawn.CharacterMovement = object("other")
    elseif condition == "owner" then f.owner = object("other")
    elseif condition == "authority" then f.authority = false
    else f.local_player = false end
    f.trainer:tick()
    equal(f.trainer:enabled().dawnwalker_fly, false, "Ownership loss stops flight: " .. condition)
    equal(#f.inputs, 0, "Ownership loss prevents input: " .. condition)
    equal(#f.setters, 1, "Ownership loss never writes another/currently unowned pawn: " .. condition)
end

do
    local f = fixture(); f.movement.MovementMode = 3; enable(f)
    equal(f.trainer:disable_all(), true)
    equal(f.movement.MovementMode, 3, "Falling mode restored rather than hardcoded walking")
    enable(f)
    f.movement.MovementMode, f.movement.CustomMovementMode = 6, 7
    f.trainer:tick()
    equal(f.trainer:enabled().dawnwalker_fly, false)
    equal(f.movement.MovementMode, 6, "A later game-owned custom mode is preserved")
    equal(f.movement.CustomMovementMode, 7)
end

do
    local f = fixture(); enable(f)
    f.noop = true
    equal(f.trainer:disable_all(), false, "Failed restoration is reported")
    equal(f.trainer:enabled().dawnwalker_fly, true, "Failed restore does not falsely report disabled")
    f.noop = false
    equal(f.trainer:disable_all(), true, "Restoration can be retried")
    equal(f.movement.MovementMode, 1)
end

for _, failure in ipairs({ "input_error", "input_write_error", "alive", "nan" }) do
    local f = fixture(); enable(f); f.keys.SpaceBar = true
    if failure == "alive" then f.alive = false
    elseif failure == "nan" then f.analog.Gamepad_RightTriggerAxis = 0/0
    else f[failure] = true end
    f.trainer:tick()
    equal(f.trainer:enabled().dawnwalker_fly, false, "Input/liveness failure stops flight: " .. failure)
    equal(f.movement.MovementMode, 1, "Owned mode restored on failure: " .. failure)
end

for _, mode in ipairs({ 0, 5, 6, 999 }) do
    local f = fixture(); f.movement.MovementMode = mode
    equal(f.trainer:apply(f.state, "dawnwalker_fly", true), false, "Do not replace disabled/custom/external flight mode")
    equal(#f.setters, 0)
end
do
    local f = fixture(); f.missing = "/Script/Engine.Actor:K2_AddActorWorldOffset"
    equal(f.trainer:apply(f.state, "dawnwalker_fly", true), false)
    equal(#f.setters, 0)
    f = fixture(); f.fname_error = true
    equal(f.trainer:apply(f.state, "dawnwalker_fly", true), false)
    equal(#f.setters, 0)
    f = fixture(); f.noop = true
    equal(f.trainer:apply(f.state, "dawnwalker_fly", true), false, "Native enable must actually change mode")
    equal(f.trainer:enabled().dawnwalker_fly, false)
end

do
    local f = fixture(); enable(f)
    local next_state = { character = f.pawn, controller = f.controller, world = f.world, key = "new-session" }
    f.trainer:refresh(next_state)
    equal(f.trainer:enabled().dawnwalker_fly, false, "Session change restores an old still-owned component")
    equal(f.movement.MovementMode, 1)
    f = fixture(); enable(f); f.noop = true
    next_state = { character = f.pawn, controller = f.controller, world = f.world, key = "new-session" }
    equal(f.trainer:apply(next_state, "dawnwalker_fly", true), false, "A failed old-session restore cannot become a successful new-session enable")
    equal(f.trainer:enabled().dawnwalker_fly, true, "Pending original restore stays visible")
    f.noop = false
    equal(f.trainer:disable_all(), true)
end

do
    local f = fixture()
    equal(f.trainer:apply(f.state, "dawnwalker_teleport", false), false, "No observed marker never moves the player")
    equal(#f.setters, 0)
    equal(#f.inputs, 0)
end

do
    local f = fixture(); enable(f); f.keys.SpaceBar = true
    f.delta = 1
    f.trainer:tick(); equal(f.inputs[1], 30, "Long frames cannot create a large vertical jump")
    f.delta = 0
    f.trainer:tick(); equal(#f.inputs, 1, "Paused world does not move")
    f.delta = -1
    f.trainer:tick(); equal(f.trainer:enabled().dawnwalker_fly, false)
    local _, values = f.trainer:refresh(f.state)
    equal(values.diagnostic_fly_status:find("frame timing", 1, true) ~= nil, true, "Timing failure survives mode cleanup")
    f = fixture(); enable(f); f.keys.SpaceBar = true; f.flight_blocked = true
    f.trainer:tick()
    equal(f.trainer:enabled().dawnwalker_fly, true, "A wall or ceiling blocks motion without disabling flight")
    _, values = f.trainer:refresh(f.state)
    equal(values.diagnostic_fly_status:find("blocked by collision", 1, true) ~= nil, true)
    f.flight_flags = 0x80
    f.trainer:tick(); _, values = f.trainer:refresh(f.state)
    equal(values.diagnostic_fly_status:find("blocked by collision", 1, true), nil, "Upper hit-result bits do not cause a false flight collision report")
    equal(f.property_registrations, 1, "Flight registers packed flags outside the high-frequency timer")
    f = fixture(); f.register_failed = true; enable(f); f.keys.SpaceBar = true
    f.trainer:tick()
    equal(#f.inputs, 1, "Optional collision diagnostics do not prevent safely swept flight")
end

local function marker(f, point)
    f.trainer:refresh(f.state)
    equal(f.trainer:observe_marker(f.map, point or { X = 1000, Y = 2000, Z = 0 }), true)
end

local function auto_enable(f)
    f.trainer:set_marker_observation_ready(true)
    equal(f.trainer:apply(f.state, "dawnwalker_auto_teleport", true), true, "Enable opt-in marker travel")
    equal(f.trainer:enabled().dawnwalker_auto_teleport, true)
end

local function complete_travel(f)
    local last = f.travels[#f.travels].location
    f.movement.MovementMode = 1
    f.position = { X = last.X, Y = last.Y, Z = last.Z + f.height }
    for _ = 1, 4 do f.trainer:refresh(f.state) end
end

-- Exercise the real return/outbound conversion shape through both dispatch
-- paths. A read-only alias fix that rewrites `pitch` to `Pitch` must fail here.
for _, automatic in ipairs({ false, true }) do
    for _, spelling in ipairs({ { "pitch", "Yaw", "Roll" }, { "Pitch", "Yaw", "Roll" }, { "PITCH", "yaw", "rOlL" } }) do
        local f = fixture()
        f.rotator_fields, f.rotation = spelling, {}
        local angles = { -23.75, 137.125, 8.5 }
        for i, field in ipairs(spelling) do f.rotation[field] = angles[i] end
        if automatic then auto_enable(f) end
        marker(f)
        local ok
        if automatic then ok = f.trainer:process_auto_teleport()
        else ok = f.trainer:apply(f.state, "dawnwalker_teleport") end
        equal(ok, true, "Accept validated reflected Rotator spelling")
        equal(#f.travels, 1)
        for i, field in ipairs(spelling) do
            equal(f.travels[1].rotation[field], angles[i], "Preserve native " .. field .. " through exact outbound lookup")
        end
        equal(#f.traces, 2, "Rotation handling does not bypass collision checks")
    end
end

for _, malformed in ipairs({ "missing_pitch", "missing_yaw", "missing_roll", "nan_pitch", "infinite_yaw",
    "negative_infinite_roll", "excess_pitch", "negative_excess_roll", "string_yaw", "boolean_roll",
    "duplicate_pitch", "duplicate_equal_pitch", "duplicate_yaw", "duplicate_roll", "not_table", "nil", "getter_error" }) do
    local f = fixture(); auto_enable(f); marker(f)
    if malformed == "missing_pitch" then f.rotation.pitch = nil
    elseif malformed == "missing_yaw" then f.rotation.Yaw = nil
    elseif malformed == "missing_roll" then f.rotation.Roll = nil
    elseif malformed == "nan_pitch" then f.rotation.pitch = 0/0
    elseif malformed == "infinite_yaw" then f.rotation.Yaw = math.huge
    elseif malformed == "negative_infinite_roll" then f.rotation.Roll = -math.huge
    elseif malformed == "excess_pitch" then f.rotation.pitch = 360001
    elseif malformed == "negative_excess_roll" then f.rotation.Roll = -360001
    elseif malformed == "string_yaw" then f.rotation.Yaw = "45"
    elseif malformed == "boolean_roll" then f.rotation.Roll = false
    elseif malformed == "duplicate_pitch" then f.rotation.Pitch = 90
    elseif malformed == "duplicate_equal_pitch" then f.rotation.Pitch = f.rotation.pitch
    elseif malformed == "duplicate_yaw" then f.rotation.yaw = f.rotation.Yaw
    elseif malformed == "duplicate_roll" then f.rotation.ROLL = f.rotation.Roll
    elseif malformed == "not_table" then f.rotation = "unavailable"
    elseif malformed == "nil" then f.rotation = nil
    else f.rotation_error = true end
    local ok, message = f.trainer:process_auto_teleport()
    equal(ok, false, "Malformed or ambiguous rotation fails closed: " .. malformed)
    equal(message:find("Player rotation unavailable", 1, true) ~= nil, true)
    equal(#f.travels, 0)
    equal(#f.setters, 0, "Invalid rotation cannot change movement")
    local traces = #f.traces
    f.rotation_error, f.rotation = false, { pitch = -12.5, Yaw = 45, Roll = 7.25 }
    equal(f.trainer:process_auto_teleport(), nil, "Failed Auto rotation check cannot silently retry")
    equal(#f.traces, traces)
    equal(#f.travels, 0)
end

do
    local f = fixture(); enable(f); marker(f); f.rotation.pitch = nil
    local ok, message = f.trainer:apply(f.state, "dawnwalker_teleport")
    equal(ok, false)
    equal(message, "Player rotation unavailable", "Manual rotation validation has no invented zero fallback")
    equal(#f.travels, 0)
    equal(f.trainer:enabled().dawnwalker_fly, true, "Rotation failure occurs before releasing flight")
end

do
    local f = fixture()
    local available = f.trainer:refresh(f.state)
    equal(available.dawnwalker_auto_teleport, false, "Both marker hooks must be installed before auto travel is offered")
    equal(f.trainer:apply(f.state, "dawnwalker_auto_teleport", true), false)
    f.trainer:set_marker_observation_ready(true)
    available = f.trainer:refresh(f.state)
    equal(available.dawnwalker_auto_teleport, true, "Auto may be enabled before any marker exists")
    equal(available.dawnwalker_teleport, false, "Manual travel still needs an observed marker")
    equal(f.trainer:enabled().dawnwalker_auto_teleport, false, "Automatic travel starts off")
    auto_enable(f)
    local calls, lookups = f.calls, f.lookups
    for _ = 1, 10000 do f.trainer:process_auto_teleport() end
    equal(f.calls, calls, "Watching without a fresh marker makes no engine calls")
    equal(f.lookups, lookups, "Watching performs no reflection lookups")
    equal(#f.travels, 0)
    equal(#f.traces, 0)
    equal(#f.setters, 0)
end

do
    local f = fixture(); marker(f); auto_enable(f)
    equal(f.trainer:process_auto_teleport(), nil, "Enabling never travels to an already captured marker")
    equal(#f.travels, 0)
    local point = { X = 1234, Y = 5678, Z = 0 }
    local calls, lookups = f.calls, f.lookups
    equal(f.trainer:observe_marker(f.map, point), true)
    point.X = 9999
    equal(f.calls, calls, "Marker callback only copies and queues, without native inspection")
    equal(f.lookups, lookups)
    equal(#f.travels, 0, "Marker hook never dispatches travel")
    equal(f.trainer:process_auto_teleport(), true)
    equal(#f.travels, 1)
    equal(f.travels[1].location.X, 1234, "Automatic request owns a copied destination")
    equal(f.travels[1].location.Z, f.ground_z, "Auto uses verified ground, never the marker fallback Z")
    equal(#f.traces, 2, "Auto uses both manual ground and capsule checks")
    equal(f.trainer:enabled().dawnwalker_auto_teleport, true, "Accepted travel preserves the opt-in")
    for _ = 1, 10 do f.trainer:refresh(f.state); f.trainer:process_auto_teleport() end
    equal(#f.travels, 1, "Accepted request cannot replay")
    complete_travel(f)
    local _, values = f.trainer:refresh(f.state)
    equal(values.dawnwalker_auto_teleport_status, "Auto teleport complete; drop a new custom map marker")
    equal(f.trainer:process_auto_teleport(), nil, "Arrival never reuses the previous marker")
end

do
    local f = fixture(); auto_enable(f); f.ignored = true; marker(f)
    for _ = 1, 10 do equal(f.trainer:process_auto_teleport(), nil, "Wait while the map/menu blocks movement") end
    equal(#f.traces, 0, "Waiting never repeats collision traces")
    equal(#f.travels, 0)
    marker(f, { X = 3000, Y = 4000, Z = 123 })
    equal(f.trainer:apply(f.state, "dawnwalker_auto_teleport", true), true, "Idempotent enable preserves the new request")
    f.ignored = false
    equal(f.trainer:process_auto_teleport(), true)
    equal(#f.travels, 1)
    equal(f.travels[1].location.X, 3000, "Only the latest marker is dispatched after map close")
end

for _, mode in ipairs({ 0, 5, 6 }) do
    local f = fixture(); auto_enable(f); marker(f); f.movement.MovementMode = mode
    equal(f.trainer:process_auto_teleport(), nil, "Wait for normal player mode instead of overriding a game-owned mode")
    equal(#f.traces, 0)
    equal(#f.travels, 0)
    f.movement.MovementMode = 1
    equal(f.trainer:process_auto_teleport(), true, "One request runs when normal control returns")
end

do
    local f = fixture(); auto_enable(f); f.ignored = true; marker(f)
    for _ = 1, 119 do equal(f.trainer:process_auto_teleport(), nil) end
    equal(f.trainer:process_auto_teleport(), false, "A queued marker expires after at most120 normal polls")
    f.ignored = false
    for _ = 1, 10 do f.trainer:process_auto_teleport() end
    equal(#f.traces, 0, "Expired destination is never traced after later control return")
    equal(#f.travels, 0)
    equal(f.trainer:enabled().dawnwalker_auto_teleport, true, "Expiry only consumes that request")
    local _, values = f.trainer:refresh(f.state)
    equal(values.dawnwalker_auto_teleport_status, "Auto teleport expired; drop a new custom map marker")
    marker(f)
    equal(f.trainer:process_auto_teleport(), true, "A later fresh marker can request a new trip")
end

for _, failure in ipairs({ "no_ground", "ground_penetrating", "missing_ground_flags", "obstructed",
    "clearance_penetrating", "missing_clearance_flags", "trace_error", "clearance_error", "steep",
    "world_state", "partition", "ignore_error" }) do
    local f = fixture(); auto_enable(f); marker(f)
    if failure == "steep" then f.normal_z = 0.2
    elseif failure == "world_state" then f.world_state = nil
    elseif failure == "partition" then f.partition = nil
    else f[failure] = true end
    equal(f.trainer:process_auto_teleport(), false, "Auto rejects unsafe request: " .. failure)
    local traces = #f.traces
    f[failure], f.no_ground, f.ground_penetrating, f.missing_ground_flags, f.obstructed = nil, false, false, false, false
    f.clearance_penetrating, f.missing_clearance_flags, f.normal_z = false, false, 1
    f.world_state, f.partition = object("world state"), object("partition")
    for _ = 1, 10 do f.trainer:process_auto_teleport() end
    equal(#f.traces, traces, "Failed request is consumed, not retried: " .. failure)
    equal(#f.travels, 0)
end

for _, failure in ipairs({ "refused", "error", "unexpected_result" }) do
    local f = fixture(); auto_enable(f); marker(f)
    if failure == "refused" then f.travel_result = 0
    elseif failure == "error" then f.travel_error = true
    else f.travel_result = 7 end
    equal(f.trainer:process_auto_teleport(), false, "Native outcome is not presumed successful: " .. failure)
    for _ = 1, 10 do f.trainer:refresh(f.state); f.trainer:process_auto_teleport() end
    equal(#f.travels, 1, "No retries after a refused or uncertain native call: " .. failure)
    equal(#f.traces, 2)
    if failure ~= "refused" then
        marker(f)
        equal(f.trainer:process_auto_teleport(), nil, "A fresh marker cannot overlap uncertain native travel")
        equal(#f.travels, 1)
    end
end

do
    local f = fixture(); auto_enable(f); marker(f)
    f.marker_during_travel = { X = 3000, Y = 4000, Z = 0 }
    equal(f.trainer:process_auto_teleport(), true)
    equal(f.trainer.auto_queue, nil, "A native marker callback inside travel cannot recursively enqueue")
    marker(f, { X = 5000, Y = 6000, Z = 0 })
    equal(f.trainer.auto_queue, nil, "Markers dropped while travel is pending are discarded")
    complete_travel(f)
    equal(f.trainer:process_auto_teleport(), nil, "Arrival does not run any in-transit marker")
    equal(#f.travels, 1)
    marker(f, { X = 7000, Y = 8000, Z = 0 })
    equal(f.trainer:process_auto_teleport(), true, "A fresh post-arrival marker is allowed")
    equal(f.travels[2].location.X, 7000)
end

for _, change in ipairs({ "off", "removal", "disable_all", "session", "ownership", "component", "dead", "observer", "bad_capture" }) do
    local f = fixture(); auto_enable(f); marker(f)
    if change == "off" then f.trainer:apply(f.state, "dawnwalker_auto_teleport", false)
    elseif change == "removal" then f.trainer:forget_marker()
    elseif change == "disable_all" then f.trainer:disable_all()
    elseif change == "session" then f.state.key = "new-session"; f.trainer:refresh(f.state)
    elseif change == "ownership" then f.possessed = object("another pawn")
    elseif change == "component" then f.pawn.CharacterMovement = object("another movement")
    elseif change == "dead" then f.alive = false
    elseif change == "observer" then f.trainer:set_marker_observation_ready(false)
    else equal(f.trainer:observe_marker(f.map, { X = 0/0, Y = 0, Z = 0 }), false) end
    f.trainer:process_auto_teleport()
    equal(f.trainer.auto_queue, nil, "Invalidated intent is cleared: " .. change)
    equal(#f.traces, 0)
    equal(#f.travels, 0)
    equal(f.trainer:enabled().dawnwalker_auto_teleport, change == "removal", "Ownership/session/failure disarms opt-in; removal only cancels request: " .. change)
end

do
    local f = fixture(); auto_enable(f)
    f.possessed = object("another pawn"); f.trainer:refresh(f.state)
    equal(f.trainer:enabled().dawnwalker_auto_teleport, false, "Ownership loss also disarms an idle watcher")
    f = fixture(); auto_enable(f); marker(f)
    local calls, lookups = f.calls, f.lookups
    f.trainer:set_marker_observation_ready(false)
    equal(f.calls, calls, "Observer failure performs no engine calls from a hook")
    equal(f.lookups, lookups)
    equal(f.trainer.marker, nil)
    equal(f.trainer.auto_queue, nil)
    equal(f.trainer:apply(f.state, "dawnwalker_auto_teleport", true), false, "Observation failure prevents rearming")
    calls, lookups = f.calls, f.lookups
    f.trainer:set_marker_observation_ready(true)
    equal(f.calls, calls, "Successful hook registration only changes Lua readiness")
    equal(f.lookups, lookups)
    equal(f.trainer:apply(f.state, "dawnwalker_auto_teleport", true), true)
    equal(f.trainer:process_auto_teleport(), nil, "Rearming after recovery never resurrects a canceled request")
    f.trainer:disable_all()
    equal(f.trainer:apply(f.state, "dawnwalker_auto_teleport", true), true, "Disable all preserves the successfully installed hook pair")
    f.state.key = "new-session"
    f.trainer:refresh(f.state)
    equal(f.trainer:enabled().dawnwalker_auto_teleport, false)
    equal(f.trainer:apply(f.state, "dawnwalker_auto_teleport", true), true, "New session can rearm without registering another pair of hooks")
end

for _, fail_manual in ipairs({ false, true }) do
    local f = fixture(); auto_enable(f); marker(f); f.no_ground = fail_manual
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), not fail_manual)
    equal(f.trainer.auto_queue, nil, "Manual request consumes any queued auto request even when preflight fails")
    equal(f.trainer:enabled().dawnwalker_auto_teleport, true)
    f.no_ground = false
    equal(f.trainer:process_auto_teleport(), nil)
    equal(#f.travels, fail_manual and 0 or 1, "Manual and automatic requests cannot duplicate one marker")
end

do
    local f = fixture(); auto_enable(f); enable(f); marker(f)
    equal(f.trainer:apply(f.state, "dawnwalker_fly", false), true)
    equal(f.trainer:enabled().dawnwalker_auto_teleport, true, "Turning flight off preserves auto opt-in")
    equal(f.trainer:process_auto_teleport(), true, "Flight-only cleanup preserves the queued marker")
    equal(f.trainer:enabled().dawnwalker_auto_teleport, true)
    f = fixture(); auto_enable(f); enable(f); marker(f)
    equal(f.trainer:process_auto_teleport(), true, "Automatic travel safely releases flight first")
    equal(f.trainer:enabled().dawnwalker_fly, false)
    equal(f.trainer:enabled().dawnwalker_auto_teleport, true, "Travel flight cleanup does not turn auto off")
    f = fixture(); auto_enable(f); enable(f); marker(f); f.noop = true
    equal(f.trainer:disable_all(), false, "A flight restore failure remains visible")
    equal(f.trainer:enabled().dawnwalker_auto_teleport, false, "Disable all still clears auto when flight cannot restore")
    equal(f.trainer.auto_queue, nil)
    f = fixture(); auto_enable(f); enable(f); marker(f); f.input_error = true
    f.trainer:tick()
    equal(f.trainer:enabled().dawnwalker_fly, false)
    equal(f.trainer:enabled().dawnwalker_auto_teleport, true, "Flight input failure does not disable an independently owned watcher")
end

for _, missing in ipairs({ "flags", "function" }) do
    local f = fixture(); f.trainer:set_marker_observation_ready(true)
    if missing == "flags" then f.register_failed = true
    else f.missing = "/Script/Engine.KismetSystemLibrary:LineTraceSingle" end
    equal(f.trainer:apply(f.state, "dawnwalker_auto_teleport", true), false, "Missing safety capability blocks auto enable")
    equal(f.trainer:enabled().dawnwalker_auto_teleport, false)
    equal(#f.travels, 0)
end

do
    local f = fixture()
    equal(f.trainer:observe_marker(f.map, { X = 1, Y = 2, Z = 3 }), false, "Ignore markers before a player session exists")
    local point = { X = 1000, Y = 2000, Z = 0 }
    marker(f, point); point.X = 9000
    local available = f.trainer:refresh(f.state)
    equal(available.dawnwalker_teleport, true)
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), true, "Normal byte1 hit is accepted despite both marshaled bools being true")
    equal(f.property_registrations, 1, "Packed hit byte is registered only once")
    equal(#f.travels, 1)
    equal(f.travels[1].location.X, 1000, "Hook coordinates are copied, not retained by reference")
    equal(f.travels[1].location.Z, f.ground_z, "Never pass the marker's Z=0 fallback to travel")
    equal(f.travels[1].rotation.Yaw, f.rotation.Yaw)
    equal(#f.inputs, 0, "Teleport does not use actor movement or raw teleport")
    equal(#f.setters, 0, "Native travel owns movement changes")
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), false, "No overlapping travel sequence")
    equal(#f.travels, 1)
    f.movement.MovementMode = 1
    f.position = { X = 1000, Y = 2000, Z = f.ground_z + f.height }
    for _ = 1, 4 do f.trainer:refresh(f.state) end
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), true, "Completed native travel permits another explicit request")
end

for _, flags in ipairs({ 0, 2, 3, -1, 256, 1.5, math.huge, 0/0 }) do
    local f = fixture(); marker(f); f.ground_flags = flags
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), false, "Reject missing blocking bit, actual penetration, and malformed packed flags")
    equal(#f.travels, 0, "Packed-flag failure cannot launch travel")
end
for _, flags in ipairs({ 1, 2, 3, -1, 256, 0.5, math.huge, 0/0 }) do
    local f = fixture(); marker(f); f.clearance_flags = flags
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), false, "Clearance requires neither blocking nor penetration bits")
    equal(#f.travels, 0)
end
for _, condition in ipairs({ "missing_ground_flags", "missing_clearance_flags", "register_failed", "register_error" }) do
    local f = fixture(); f[condition] = true; marker(f)
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), false, "Never fall back to broken bool marshalling: " .. condition)
    equal(#f.travels, 0)
end
do
    local f = fixture(); marker(f); f.ground_flags, f.clearance_flags = 0x81, 0x80
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), true, "Unrelated upper bits do not masquerade as blocking or penetration")
    f = fixture(); marker(f); f.ground_penetrating = true
    local ok, message = f.trainer:apply(f.state, "dawnwalker_teleport")
    equal(ok, false)
    equal(message:find("inside collision", 1, true) ~= nil, true, "Actual initial penetration has its own diagnosis")
    f = fixture(); marker(f); f.normal_z = 0.2
    ok, message = f.trainer:apply(f.state, "dawnwalker_teleport")
    equal(ok, false)
    equal(message:find("too steep", 1, true) ~= nil, true, "Steep ground is distinguished from unloaded ground")
    f = fixture(); marker(f); f.missing_ground_flags = true
    ok, message = f.trainer:apply(f.state, "dawnwalker_teleport")
    equal(ok, false)
    equal(message:find("flags unavailable", 1, true) ~= nil, true, "Marshalling errors no longer claim the ground is unloaded")
    local _, values = f.trainer:refresh(f.state)
    equal(values.diagnostic_teleport_status, message, "Diagnostic remains visible on the next heartbeat")
end

for _, failure in ipairs({ "no_ground", "trace_error", "ground_penetrating", "slope", "nan_ground", "obstructed",
    "clearance_penetrating", "clearance_error", "radius", "height", "marker_removed", "other_map", "other_player",
    "ignored", "change_marker_during_trace", "change_owner_during_trace", "rotation", "world_state", "partition",
    "game_instance", "capsule_owner", "dead" }) do
    local f = fixture(); marker(f)
    if failure == "slope" then f.normal_z = 0.2
    elseif failure == "nan_ground" then f.ground_z = 0/0
    elseif failure == "radius" then f.radius = 0
    elseif failure == "height" then f.height = 2000
    elseif failure == "marker_removed" then f.marker_present = false
    elseif failure == "other_map" then f.map = object("other map")
    elseif failure == "other_player" then f.first_player = object("other controller")
    elseif failure == "rotation" then f.rotation.Yaw = 0/0
    elseif failure == "world_state" then f.world_state = nil
    elseif failure == "partition" then f.partition = nil
    elseif failure == "game_instance" then f.world.OwningGameInstance = nil
    elseif failure == "capsule_owner" then f.capsule_owner = object("other character")
    elseif failure == "dead" then f.alive = false
    else f[failure] = true end
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), false, "Reject unsafe teleport: " .. failure)
    equal(#f.travels, 0, "No native travel on failed preflight: " .. failure)
    equal(#f.setters, 0, "No movement mode writes on failed preflight: " .. failure)
end

do
    local f = fixture(); marker(f); f.trainer:forget_marker(f.map)
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), false, "Unset hook invalidates cached marker")
    marker(f)
    f.state.key = "new-world"
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), false, "New session cannot reuse coordinates from old world")
    equal(#f.travels, 0)
    marker(f)
    equal(f.trainer:observe_marker(f.map, { X = 0/0, Y = 0, Z = 0 }), false)
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), false, "Bad hook coordinates invalidate prior coordinates")
end
do
    local f = fixture(); enable(f); marker(f)
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), true)
    equal(f.trainer:enabled().dawnwalker_fly, false, "Flight releases its mode before native travel owns movement")
    equal(f.setters[#f.setters].mode, 1)
    f = fixture(); enable(f); marker(f); f.noop = true
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), false, "Failed flight restoration blocks travel")
    equal(#f.travels, 0)
    equal(f.trainer:enabled().dawnwalker_fly, true)
end
for _, result in ipairs({ 0, 1 }) do
    local f = fixture(); marker(f); f.travel_result = result
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), false, "Native refusal is not reported as successful travel")
    equal(f.trainer.pending, nil, "Explicit native refusal releases the request guard")
end
do
    local f = fixture(); marker(f); f.travel_error = true
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), false)
    equal(f.trainer:apply(f.state, "dawnwalker_teleport"), false)
    equal(#f.travels, 1, "An uncertain native result cannot replay the travel request")
end

print("movement: " .. checks .. " checks passed")
