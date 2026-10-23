-- Steam build 25915545 / CL-265636, UE 5.5.4. Native reflected calls only, on the game thread.
-- The engine owns collision, acceleration and horizontal movement during flight.
local M, Instance = {}, {}
Instance.__index = Instance
local FLY = "dawnwalker_fly"
local TELEPORT = "dawnwalker_teleport"
local AUTO_TELEPORT = "dawnwalker_auto_teleport"
local AUTO_WAIT_POLLS = 120 -- Normal 250ms heartbeat, never the flight timer.
local ENGINE = "/Script/Engine."
local MOVE_FLYING = 5 -- Verified in this executable's EMovementMode enumerator table.
local VERTICAL_SPEED, MAX_DELTA = 600, 0.05
local MAP = "/Script/DogwoodMap."
local GAMEPLAY = ENGINE .. "GameplayStatics:"
local SYSTEM = ENGINE .. "KismetSystemLibrary:"
local MAP_LIBRARY = MAP .. "MappinSystemBlueprintLibrary:"
local HIT_FLAGS = "HappyCheatsHitResultFlags"
local FLIGHT_FUNCTIONS = {
    ENGINE .. "CharacterMovementComponent:SetMovementMode",
    ENGINE .. "Actor:K2_AddActorWorldOffset",
    GAMEPLAY .. "GetWorldDeltaSeconds",
    ENGINE .. "PlayerController:IsInputKeyDown",
    ENGINE .. "PlayerController:GetInputAnalogKeyState",
    ENGINE .. "Controller:IsMoveInputIgnored",
}
local TELEPORT_FUNCTIONS = {
    ENGINE .. "SubsystemBlueprintLibrary:GetGameInstanceSubsystem",
    ENGINE .. "SubsystemBlueprintLibrary:GetWorldSubsystem",
    GAMEPLAY .. "GetPlayerController", SYSTEM .. "LineTraceSingle", SYSTEM .. "CapsuleTraceSingle",
    MAP_LIBRARY .. "IsSetUserMappin", MAP .. "FastTravelSystem:FastTravelToLocation",
    ENGINE .. "Actor:K2_GetActorLocation", ENGINE .. "Actor:K2_GetActorRotation",
    ENGINE .. "CapsuleComponent:GetScaledCapsuleRadius", ENGINE .. "CapsuleComponent:GetScaledCapsuleHalfHeight",
    ENGINE .. "Controller:IsMoveInputIgnored",
}

local function copy(source)
    local result = {}
    for key, value in pairs(source) do result[key] = value end
    return result
end

function Instance:_owned(state, movement)
    local a = self.api
    if not state or not a.valid(state.character) or not a.valid(state.controller)
        or not a.valid(state.world) then return false end
    local ok, value = a.invoke(state.character, "IsLocallyControlled")
    if not ok or value ~= true then return false end
    ok, value = a.invoke(state.character, "HasAuthority")
    if not ok or value ~= true then return false end
    if not a.same(a.read(state.character, "Controller"), state.controller) then return false end
    ok, value = a.invoke(state.controller, "K2_GetPawn")
    if not ok or not a.same(value, state.character) then return false end
    ok, value = a.invoke(state.character, "GetWorld")
    if not ok or not a.same(value, state.world) then return false end
    if movement then
        if not a.valid(movement) or not a.same(a.read(state.character, "CharacterMovement"), movement) then return false end
        ok, value = a.invoke(movement, "GetOwner")
        if not ok or not a.same(value, state.character) then return false end
    end
    return true
end

function Instance:_movement(state)
    local a = self.api
    local movement = state and a.read(state.character, "CharacterMovement") or nil
    if not self:_owned(state, movement) or not a.valid(movement) then return nil end
    local mode, custom = a.read(movement, "MovementMode"), a.read(movement, "CustomMovementMode")
    if not a.finite(mode) or mode % 1 ~= 0 or mode < 0 or mode > 6
        or not a.finite(custom) or custom % 1 ~= 0 or custom < 0 or custom > 255 then return nil end
    return movement, mode, custom
end

function Instance:_functions_available(paths)
    for _, path in ipairs(paths or FLIGHT_FUNCTIONS) do
        if not self.api.has_function(path) then return false end
    end
    return true
end

function Instance:_hit_flags_available()
    if self.hit_flags_ready ~= nil then return self.hit_flags_ready end
    self.hit_flags_ready = false
    if type(self.api.register_property) ~= "function" then return false end
    -- Builds25232147/25915545 HitResult: both native bool properties occupy byte0xAD,
    -- masks1/2. UE4SS2bfa839f's GetNonTrivialLocal reads the whole byte as bool,
    -- so an ordinary blocking hit incorrectly yields bStartPenetrating=true.
    -- Custom ByteProperty fields retain that byte in OUT-struct Lua tables.
    local ok, registered = pcall(self.api.register_property, HIT_FLAGS, ENGINE .. "HitResult", "ByteProperty", 0xAD)
    self.hit_flags_ready = ok and registered == true
    return self.hit_flags_ready
end

local function member(value, name)
    local ok, result = pcall(function() return value[name] end)
    if ok then return result end
end
function Instance:_collision_flags(hit)
    local flags = member(hit, HIT_FLAGS)
    if not self.api.finite(flags) or flags < 0 or flags > 255 or flags % 1 ~= 0 then return nil end
    return (flags & 1) ~= 0, (flags & 2) ~= 0
end
function Instance:_vector(value, limit)
    local result = {}
    for _, name in ipairs({ "X", "Y", "Z" }) do
        local number = member(value, name)
        if not self.api.finite(number) or math.abs(number) > (limit or 10000000) then return nil end
        result[name] = number
    end
    return result
end
function Instance:_rotation(value)
    if type(value) ~= "table" then return nil end
    local result, seen = {}, {}
    -- Both UFunction signatures use CoreUObject.Rotator. The pinned loader
    -- copies its exact reflected field names in each direction (the capture
    -- has lowercase `pitch`, then `Yaw` and `Roll`). Preserve the returned key
    -- when sending the validated angle back; canonicalizing only the output
    -- would silently leave a differently cased native field at zero.
    for field, number in next, value do
        local axis = type(field) == "string" and field:lower() or nil
        if axis == "pitch" or axis == "yaw" or axis == "roll" then
            if seen[axis] or not self.api.finite(number) or math.abs(number) > 360000 then return nil end
            seen[axis], result[field] = true, number
        end
    end
    if not seen.pitch or not seen.yaw or not seen.roll then return nil end
    return result
end
function Instance:_library(name)
    if type(self.api.resolve) ~= "function" then return nil end
    local object = self.api.resolve(name)
    if self.api.valid(object) then return object end
end

-- Native SetUserMappin/UnsetUserMappin POST hooks provide these observations.
-- MappinInstanceId is opaque: UE4SS converts its UFunction return into an empty
-- table, so it cannot safely be passed to another function. Keep only copied
-- coordinates and ordinary UObject wrappers, never a hook parameter wrapper.
function Instance:observe_marker(map, location)
    local state, vector = self.state, self:_vector(location)
    self.marker, self.auto_queue = nil, nil
    if not state or not state.key or state.key == "" or map == nil or not vector then
        self:_disable_auto("Auto teleport disabled: the new marker could not be captured")
        return false
    end
    self.marker = { map = map, location = vector, key = state.key, world = state.world }
    self.teleport_message = "Custom marker captured; teleport checks ground and collision"
    if self.auto_enabled then
        if self.auto_owner.key ~= state.key then
            self:_disable_auto("Auto teleport disabled: player session changed")
        elseif self.pending then
            -- Native travel can itself set a marker. Never turn that callback
            -- into another travel request, including after arrival.
            self.auto_message = "Auto teleport: drop a new marker after current travel finishes"
        else
            self.auto_queue = { marker = self.marker, polls = 0 }
            self.auto_message = "Auto teleport queued; close the map and return to player control"
        end
    end
    return true
end
function Instance:forget_marker()
    self.marker, self.auto_queue = nil, nil
    self.teleport_message = "Place a custom map marker to enable teleport"
    if self.auto_enabled then self.auto_message = "Auto teleport enabled; drop a new custom map marker" end
end

function Instance:_disable_auto(message)
    self.auto_enabled, self.auto_owner, self.auto_queue = false, nil, nil
    self.auto_message = message or "Auto teleport disabled"
end

-- Registration/capture state only. This may be called from a marker hook, so
-- it must never inspect native objects, invoke game functions, or undo travel.
function Instance:set_marker_observation_ready(ready)
    self.marker_observation_ready = ready == true
    if not self.marker_observation_ready then
        self:forget_marker()
        self:_disable_auto("Auto teleport disabled: map marker observation is unavailable")
    end
end

function Instance:_auto_owned(state, movement)
    local owner, a = self.auto_owner, self.api
    return owner ~= nil and state ~= nil and owner.key == state.key
        and a.same(owner.character, state.character) and a.same(owner.controller, state.controller)
        and a.same(owner.world, state.world) and a.same(owner.movement, movement)
end

function Instance:_stop(reason)
    local restored, message = self:_disable_flight()
    self.flight_message = reason .. (restored and "" or "; " .. tostring(message))
    return restored, self.flight_message
end

function Instance:_keys()
    if self.keys then return true end
    local names = { up = "SpaceBar", down = "LeftControl",
        trigger_up = "Gamepad_RightTriggerAxis", trigger_down = "Gamepad_LeftTriggerAxis" }
    local keys = {}
    for role, name in pairs(names) do
        -- UE4SS's FName property pusher requires FName userdata, not a Lua string.
        local ok, value = pcall(self.api.fname, name)
        if not ok or value == nil then return false end
        keys[role] = { KeyName = value }
    end
    self.keys = keys
    return true
end

function Instance:_disable_flight()
    local flight = self.flight
    if not flight then return true, "Flight disabled" end
    local a = self.api
    if not self:_owned(flight.state, flight.movement) then
        self.flight = nil
        self.flight_message = "Flight stopped because player ownership changed"
        return true, self.flight_message
    end
    -- A mode selected by the game after our override belongs to the game. Never
    -- overwrite a cinematic, climb, swim, death or other subsequent mode change.
    if a.read(flight.movement, "MovementMode") ~= MOVE_FLYING then
        self.flight = nil
        self.flight_message = "Flight stopped after the game changed movement mode"
        return true, self.flight_message
    end
    local ok = a.invoke(flight.movement, "SetMovementMode", flight.mode, flight.custom)
    if not ok or a.read(flight.movement, "MovementMode") ~= flight.mode
        or a.read(flight.movement, "CustomMovementMode") ~= flight.custom then
        self.flight_message = "Could not restore movement mode; disable flight again"
        return false, self.flight_message
    end
    self.flight = nil
    self.flight_message = "Flight disabled; previous movement mode restored"
    return true, self.flight_message
end

function Instance:disable_all()
    self:_disable_auto()
    return self:_disable_flight()
end

function Instance:refresh(state)
    if self.flight and (not state or self.flight.state.key ~= state.key) then self:disable_all() end
    self.state = state
    if self.marker and (not state or state.key ~= self.marker.key) then self:forget_marker() end
    if self.pending and (not state or state.key ~= self.pending.key) then self.pending = nil end
    local movement, mode = self:_movement(state)
    if self.auto_enabled and (not movement or not self:_auto_owned(state, movement)) then
        self:_disable_auto("Auto teleport disabled: player session or ownership changed")
    end
    if self.flight and (not movement or not self.api.same(movement, self.flight.movement)
        or mode ~= MOVE_FLYING) then self:_disable_flight() end
    local supported = movement ~= nil and self:_functions_available()
    if self.pending and movement then
        self.pending.polls = self.pending.polls + 1
        if self.pending.polls >= 4 and mode >= 1 and mode <= 4 then
            local ignored_ok, ignored = self.api.invoke(state.controller, "IsMoveInputIgnored")
            local location_ok, position = self.api.invoke(state.character, "K2_GetActorLocation")
            position = location_ok and self:_vector(position) or nil
            local destination = self.pending.destination
            if ignored_ok and ignored == false and position
                and math.abs(position.X - destination.X) < 500 and math.abs(position.Y - destination.Y) < 500
                and math.abs(position.Z - destination.Z) < 1000 then
                self.pending = nil
                self.teleport_message = "Map-marker travel completed"
                if self.auto_enabled then self.auto_message = "Auto teleport complete; drop a new custom map marker" end
            end
        end
    end
    local teleport_supported = movement ~= nil and self:_functions_available(TELEPORT_FUNCTIONS)
        and self:_hit_flags_available()
    local travel_mode = movement ~= nil and (self.flight ~= nil or (mode >= 1 and mode <= 4))
    self.available = {
        [FLY] = supported and (self.flight ~= nil or (mode >= 1 and mode <= 4)),
        [TELEPORT] = movement ~= nil and self.marker ~= nil and self.pending == nil
            and travel_mode and teleport_supported,
        [AUTO_TELEPORT] = self.marker_observation_ready == true and teleport_supported
            and (self.auto_enabled == true or (travel_mode and self.pending == nil)),
    }
    self.values = {
        diagnostic_fly_status = self.flight_message or "Space / Left Ctrl or RT / LT to ascend / descend",
        diagnostic_teleport_status = self.teleport_message or "Place a custom map marker to enable teleport",
        dawnwalker_auto_teleport_status = self.auto_message or "Auto teleport disabled",
    }
    return copy(self.available), copy(self.values)
end

function Instance:_subsystem(state, class_name, world)
    local library = self:_library(ENGINE .. "Default__SubsystemBlueprintLibrary")
    local class = self:_library(class_name:sub(1, 1) == "/" and class_name or MAP .. class_name)
    if not library or not class then return nil end
    local ok, object = self.api.invoke(library, world and "GetWorldSubsystem" or "GetGameInstanceSubsystem", state.character, class)
    if ok and self.api.valid(object) then return object end
end

function Instance:_teleport(state)
    self:refresh(state)
    if self.pending then return false, "Map-marker travel is still in progress" end
    local marker, a = self.marker, self.api
    if not marker then return false, "Place a custom map marker again after the trainer loads" end
    if not self.available[TELEPORT] or not state or marker.key ~= state.key
        or not a.same(marker.world, state.world) then return false, "Map-marker player session unavailable" end
    local ignored_ok, ignored = a.invoke(state.controller, "IsMoveInputIgnored")
    if not ignored_ok or ignored ~= false then return false, "Close the map or menu and wait for player control" end
    local library = self:_library(MAP .. "Default__MappinSystemBlueprintLibrary")
    local system = self:_library(ENGINE .. "Default__KismetSystemLibrary")
    local gameplay = self:_library(ENGINE .. "Default__GameplayStatics")
    local map = self:_subsystem(state, "MappinSystemImpl", false)
    local travel = self:_subsystem(state, "FastTravelSystem", true)
    if not library or not system or not gameplay or not map or not travel or not a.same(map, marker.map) then
        return false, "Map or travel system changed; place a new marker"
    end
    -- The native phase callback dereferences these two services without null
    -- checks. A living pawn and a FastTravelSystem alone do not prove readiness.
    if not a.valid(a.read(state.world, "OwningGameInstance"))
        or not self:_subsystem(state, "/Script/DogwoodSystem.WorldStateSystemInterface", false)
        or not self:_subsystem(state, ENGINE .. "WorldPartitionSubsystem", true) then
        return false, "World streaming services are not ready for map-marker travel"
    end
    local pin_ok, pin_set = a.invoke(library, "IsSetUserMappin", map)
    local player_ok, player = a.invoke(gameplay, "GetPlayerController", state.character, 0)
    if not pin_ok or pin_set ~= true or not player_ok or not a.same(player, state.controller) then
        return false, "Custom marker or travel player unavailable"
    end
    -- Exact ground query from WBP_Map_C.SetCustomMappin: XY, +1,000,000 to
    -- -1,000,000, trace channel0, simple collision. Its Z=0 fallback is unsafe
    -- for travel; require a fresh blocking hit instead, including at distant pins.
    local point, hit = marker.location, {}
    local color = { R = 0, G = 0, B = 0, A = 0 }
    local start = { X = point.X, Y = point.Y, Z = 1000000 }
    local finish = { X = point.X, Y = point.Y, Z = -1000000 }
    local traced, blocked = a.invoke(system, "LineTraceSingle", state.character, start, finish,
        0, false, { state.character }, 0, hit, true, color, color, 0.0)
    if not traced then return false, "Ground collision check failed; wait for the game to finish loading and try again" end
    if blocked ~= true then return false, "Ground at the marker is not loaded; choose a nearby marker" end
    local hit_blocking, penetrating = self:_collision_flags(hit)
    if hit_blocking == nil then return false, "Ground collision flags unavailable; restart the game after updating the trainer" end
    if hit_blocking ~= true then return false, "Ground collision result could not be confirmed" end
    if penetrating then return false, "Ground trace starts inside collision; choose another marker" end
    local ground, normal = self:_vector(hit.Location), self:_vector(hit.ImpactNormal, 1.001)
    local movement = self:_movement(state)
    local slope = movement and a.read(movement, "WalkableFloorZ") or nil
    if not ground or not normal then return false, "Ground collision coordinates unavailable" end
    if not a.finite(slope) or slope < 0 or slope > 1 then return false, "Player walkable-slope setting unavailable" end
    if normal.Z < math.max(0.5, slope) then return false, "Ground at the marker is too steep; choose a flatter spot" end
    if math.abs(ground.X - point.X) > 1 or math.abs(ground.Y - point.Y) > 1 or math.abs(ground.Z) >= 1000000 then
        return false, "Ground hit does not match a supported marker position"
    end
    local capsule = a.read(state.character, "CapsuleComponent")
    local capsule_owner_ok, capsule_owner = a.invoke(capsule, "GetOwner")
    local radius_ok, radius = a.invoke(capsule, "GetScaledCapsuleRadius")
    local height_ok, height = a.invoke(capsule, "GetScaledCapsuleHalfHeight")
    if not capsule_owner_ok or not a.same(capsule_owner, state.character)
        or not radius_ok or not height_ok or not a.finite(radius) or not a.finite(height)
        or radius <= 0 or radius > 1000 or height < radius or height > 1000 then
        return false, "Player collision capsule unavailable"
    end
    local center = { X = ground.X, Y = ground.Y, Z = ground.Z + height + 5 }
    local clearance = {}
    local clearance_ok, obstructed = a.invoke(system, "CapsuleTraceSingle", state.character, center,
        { X = center.X, Y = center.Y, Z = center.Z + 1 }, radius, height, 0, false,
        { state.character }, 0, clearance, true, color, color, 0.0)
    if not clearance_ok then return false, "Player collision clearance check failed" end
    local clearance_blocking, clearance_penetrating = self:_collision_flags(clearance)
    if clearance_blocking == nil then return false, "Player collision clearance flags unavailable" end
    if obstructed ~= false or clearance_blocking or clearance_penetrating then return false, "Not enough clear space above the marker" end
    local rotation_ok, rotation = a.invoke(state.character, "K2_GetActorRotation")
    local orientation = rotation_ok and self:_rotation(rotation) or nil
    if not orientation then return false, "Player rotation unavailable" end
    local alive_ok, alive = a.invoke(state.character, "IsAlive")
    if self.marker ~= marker or not self:_owned(state, movement) or not alive_ok or alive ~= true then
        return false, "Marker or player changed during checks"
    end
    if self.flight then
        local restored, message = self:_disable_flight()
        if not restored then return false, message end
    end
    -- The game's native route owns unloading/loading, its safety platform,
    -- capsule-floor placement and restoring movement. Never teleport directly.
    self.pending = { key = state.key, destination = ground, polls = 0 }
    local ok, result = a.invoke(travel, "FastTravelToLocation", ground, orientation)
    if ok and (result == 0 or result == 1) then
        self.pending = nil
        return false, "The game refused map-marker travel"
    end
    if not ok or result ~= 2 then
        self.teleport_message = "Travel result uncertain; waiting for the game's travel sequence"
        return false, self.teleport_message
    end
    self.teleport_message = "Travelling to the custom marker; the game is loading the destination"
    return true, self.teleport_message
end

-- Called once after commands on the normal heartbeat. Hook callbacks only copy
-- and queue; the actual request uses the same ground/ownership/streaming checks
-- as manual travel. A consumed request is never replayed after any result.
-- nil means idle/waiting; bool,message means a request was consumed or canceled.
function Instance:process_auto_teleport()
    local request = self.auto_queue
    if not self.auto_enabled or not request then return nil end
    local state, a = self.state, self.api
    local movement, mode = self:_movement(state)
    if not movement or not self:_auto_owned(state, movement) then
        self:_disable_auto("Auto teleport disabled: player session or ownership changed")
        return false, self.auto_message
    end
    if not self.marker_observation_ready or self.marker ~= request.marker then
        self.auto_queue = nil
        self.auto_message = "Auto teleport canceled: the custom marker changed or was removed"
        return false, self.auto_message
    end
    if self.pending then
        self.auto_queue = nil
        self.auto_message = "Auto teleport: drop a new marker after current travel finishes"
        return false, self.auto_message
    end
    request.polls = request.polls + 1
    if request.polls >= AUTO_WAIT_POLLS then
        self.auto_queue = nil
        self.auto_message = "Auto teleport expired; drop a new custom map marker"
        return false, self.auto_message
    end
    local alive_ok, alive = a.invoke(state.character, "IsAlive")
    if not alive_ok or alive ~= true then
        self:_disable_auto("Auto teleport disabled: player liveness check failed")
        return false, self.auto_message
    end
    local ignored_ok, ignored = a.invoke(state.controller, "IsMoveInputIgnored")
    if not ignored_ok or type(ignored) ~= "boolean" then
        self.auto_queue = nil
        self.auto_message = "Auto teleport canceled: player control could not be confirmed"
        return false, self.auto_message
    end
    if ignored or not ((mode >= 1 and mode <= 4) or (mode == MOVE_FLYING and self.flight ~= nil)) then
        return nil
    end
    self.auto_queue = nil -- Before any native call; errors/refusals cannot replay.
    local ok, message = self:_teleport(state)
    self.auto_message = ok and "Auto teleport started; waiting for the game to finish travel"
        or ("Auto teleport skipped: " .. tostring(message))
    if not ok then self.teleport_message = message end
    return ok, self.auto_message
end

function Instance:apply(state, action, enabled)
    if action == "disable_all" then return self:disable_all() end
    if action == TELEPORT then
        self.auto_queue = nil
        if self.auto_enabled then self.auto_message = "Auto teleport enabled; drop a new custom map marker" end
        local ok, message = self:_teleport(state)
        if not ok then self.teleport_message = message end
        return ok, message
    end
    if action == AUTO_TELEPORT then
        if enabled == false then self:_disable_auto(); return true, self.auto_message end
        if enabled ~= true then return false, "A boolean enabled value is required" end
        self:refresh(state)
        if self.auto_enabled then return true, self.auto_message end
        if not self.available[AUTO_TELEPORT] then return false, "Auto teleport requires player movement and map marker observation" end
        local movement = self:_movement(state)
        if not movement then return false, "Auto teleport player ownership changed" end
        self.auto_owner = { key = state.key, character = state.character, controller = state.controller,
            world = state.world, movement = movement }
        self.auto_enabled, self.auto_queue = true, nil
        self.auto_message = "Auto teleport enabled; drop a new custom map marker"
        return true, self.auto_message
    end
    if action ~= FLY then return false, "Unknown movement action" end
    if enabled == false then return self:_disable_flight() end
    if enabled ~= true then return false, "A boolean enabled value is required" end
    self:refresh(state)
    if self.flight then
        if not state or self.flight.state.key ~= state.key then return false, "Previous flight restore is still pending" end
        return true, "Flight already enabled"
    end
    if not self.available[FLY] then return false, "Flight requires normal player movement" end
    if not self:_keys() then return false, "Flight input keys are unavailable" end
    -- Swept motion remains safe without diagnostics, but never read UE4SS's
    -- unmasked HitResult booleans to decide whether a collision occurred.
    self:_hit_flags_available()
    self.gameplay = self:_library(ENGINE .. "Default__GameplayStatics")
    if not self.gameplay then return false, "Flight frame timing is unavailable" end
    local movement, mode, custom = self:_movement(state)
    if not movement or mode < 1 or mode > 4 then return false, "Player movement changed; try again" end
    self.flight = { state = state, movement = movement, mode = mode, custom = custom }
    local ok = self.api.invoke(movement, "SetMovementMode", MOVE_FLYING, 0)
    if not ok or self.api.read(movement, "MovementMode") ~= MOVE_FLYING
        or self.api.read(movement, "CustomMovementMode") ~= 0 then
        self:_disable_flight()
        return false, "The game did not enable flight"
    end
    self.flight_message = "Flight enabled; Space / Left Ctrl or RT / LT to ascend / descend"
    return true, self.flight_message
end

-- Called by a persistent game-thread timer at approximately 16ms. Disabled
-- flight returns before any game call, allocation of FNames, global lookup or IO.
function Instance:tick()
    local flight = self.flight
    if not flight then return true end
    local a, state = self.api, flight.state
    if not self:_owned(state, flight.movement) then
        self:_disable_auto("Auto teleport disabled: player session or ownership changed")
        return self:_stop("Flight stopped: player ownership changed")
    end
    local alive_ok, alive = a.invoke(state.character, "IsAlive")
    if not alive_ok or alive ~= true then
        self:_disable_auto("Auto teleport disabled: player liveness check failed")
        return self:_stop("Flight stopped: player liveness check failed")
    end
    if a.read(flight.movement, "MovementMode") ~= MOVE_FLYING then return self:_stop("Flight stopped: the game changed movement mode") end
    local ignored_ok, ignored = a.invoke(state.controller, "IsMoveInputIgnored")
    if not ignored_ok or type(ignored) ~= "boolean" then return self:_stop("Flight stopped: input suppression query failed") end
    if ignored then self.flight_message = "Flight paused while the game blocks movement input"; return true end
    local up_ok, up = a.invoke(state.controller, "IsInputKeyDown", self.keys.up)
    local down_ok, down = a.invoke(state.controller, "IsInputKeyDown", self.keys.down)
    local rt_ok, rt = a.invoke(state.controller, "GetInputAnalogKeyState", self.keys.trigger_up)
    local lt_ok, lt = a.invoke(state.controller, "GetInputAnalogKeyState", self.keys.trigger_down)
    if not up_ok or not down_ok or not rt_ok or not lt_ok or type(up) ~= "boolean"
        or type(down) ~= "boolean" or not a.finite(rt) or not a.finite(lt) then
        return self:_stop("Flight stopped: keyboard or controller input query failed")
    end
    rt, lt = math.max(0, math.min(1, rt)), math.max(0, math.min(1, lt))
    if rt < 0.1 then rt = 0 end
    if lt < 0.1 then lt = 0 end
    local axis = math.max(-1, math.min(1, (up and 1 or 0) - (down and 1 or 0) + rt - lt))
    self.flight_message = "Flight enabled; Space / Left Ctrl or RT / LT to ascend / descend"
    if axis ~= 0 then
        local delta_ok, delta = a.invoke(self.gameplay, "GetWorldDeltaSeconds", state.character)
        if not delta_ok or not a.finite(delta) or delta < 0 then return self:_stop("Flight stopped: frame timing query failed") end
        delta = math.min(delta, MAX_DELTA)
        if delta == 0 then return true end
        -- RebelCharacterMovement's asynchronous locomotion can discard ordinary
        -- Pawn vertical input. Move the possessed actor's root with a collision
        -- sweep; preserve native horizontal movement and never teleport physics.
        local hit = {}
        if not a.invoke(state.character, "K2_AddActorWorldOffset",
            { X = 0, Y = 0, Z = axis * VERTICAL_SPEED * delta }, true, hit, false) then
            return self:_stop("Flight stopped: swept vertical movement failed")
        end
        local blocking, penetrating = self:_collision_flags(hit)
        if blocking or penetrating then self.flight_message = "Flight vertical movement blocked by collision" end
    end
    return true
end

function Instance:enabled() return { [FLY] = self.flight ~= nil, [AUTO_TELEPORT] = self.auto_enabled == true } end
function M.new(api)
    return setmetatable({ api = api, available = {}, values = {} }, Instance)
end
return M
