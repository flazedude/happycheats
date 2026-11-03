-- Build 25915545 / CL-265636. One native guard pulse per incoming attack.
-- No synthetic input, forced defense result, damage hook or global actor scan.
local M, Instance = {}, {}
Instance.__index = Instance
local ACTION = "dawnwalker_auto_parry"
local COMBAT = "/Script/DogwoodCombat.CombatComponentBase:"
local STUB = "/Script/RebelAI.RebelAIStub:"
local GAMEPLAY = "/Script/Engine.GameplayStatics:"
local BLOCK_ACTION = "/Game/_Dawnwalker/Player/Input/Actions/Combat/IA_Combat_Block.IA_Combat_Block"
-- Native conversion at RVA 0x60391D0 mirrors attack left/right for the defender.
local DIRECTIONS = {[1]=8, [2]=4, [3]=1, [4]=2}
local VALID_DIRECTIONS = {[0]=true,[1]=true,[2]=true,[4]=true,[8]=true,[5]=true,[9]=true,[6]=true,[10]=true}
local FUNCTIONS = {
    COMBAT.."SetDesiredBlockState", COMBAT.."WantsToBlock", COMBAT.."IsBlocking", COMBAT.."CanDefend",
    COMBAT.."CanPerformBlockInDirection", COMBAT.."GetConfig", COMBAT.."GetCombatForwardDirection",
    "/Script/DogwoodCombat.PlayerCombatComponent:SetBlockingDirection",
    "/Script/RebelAI.RebelAISubsystem:GetAIStubFromActor",
    STUB.."GetActor", STUB.."GetAIBoard", STUB.."IsPlayer", STUB.."IsInitializedAndHasPawn",
    STUB.."IsHostileTowardsPlayer", STUB.."IsInCinematicMode",
    "/Script/DogwoodAI.DogwoodAICombatBlueprintFunctionLibrary:GetNextIncomingAttacker",
    "/Script/DogwoodCombat.CombatBlueprintFunctionLibrary:GetParryPointNotificationTime",
    GAMEPLAY.."GetTimeSeconds", GAMEPLAY.."IsGamePaused",
    "/Script/Engine.Controller:IsMoveInputIgnored", "/Script/Engine.PlayerController:IsInputKeyDown",
    "/Script/Engine.Actor:K2_GetActorLocation",
}
local function field(value, key)
    local ok, result = pcall(function() return value[key] end)
    if ok then return result end
end
local function bool(a, object, method, ...)
    local ok, value = a.invoke(object, method, ...)
    if ok and type(value) == "boolean" then return value end
end

function Instance:_bindings()
    if self.bindings ~= nil then return self.bindings ~= false and self.bindings or nil end
    local a = self.api
    self.bindings = false
    for _, path in ipairs(FUNCTIONS) do if not a.has_function(path) then return nil end end
    local b = {}
    for key, path in pairs({ai="/Script/RebelAI.Default__RebelAISubsystem",
        incoming="/Script/DogwoodAI.Default__DogwoodAICombatBlueprintFunctionLibrary",
        combat="/Script/DogwoodCombat.Default__CombatBlueprintFunctionLibrary",
        gameplay="/Script/Engine.Default__GameplayStatics", action=BLOCK_ACTION}) do
        b[key] = a.resolve(path)
        if not a.valid(b[key]) then return nil end
    end
    self.bindings = b
    return b
end

function Instance:_component_owned(s)
    local a = self.api
    if not s or not a.valid(s.character) or not a.valid(s.combat) then return false end
    local ok, owner = a.invoke(s.combat, "GetCharacter")
    return ok and a.same(owner, s.character) and a.same(a.read(s.character, "CombatComponent"), s.combat)
end

function Instance:_current(s)
    local fresh = self.api.current_state()
    return fresh and fresh.key == s.key and self.api.same(fresh.combat,s.combat)
end

function Instance:_manual(s, fallback)
    local a, b = self.api, self:_bindings()
    if not b or not a.valid(s.controller) then return nil,nil,"Player controller unavailable" end
    local input = a.read(s.controller,"PlayerInput")
    local owner_ok, owner = a.invoke(input,"GetOuter")
    local keys, reason
    if owner_ok and a.same(owner,s.controller) and bool(a,input,"IsA","/Script/EnhancedInput.EnhancedPlayerInput") == true then
        local array = a.read(input,"EnhancedActionMappings")
        local decoded, result = pcall(function()
            local count = #array
            if not a.finite(count) or count < 0 or count > 512 or count % 1 ~= 0 then error("Input mapping count outside supported range") end
            local found, seen = {}, {}
            for i=1,count do
                local mapping = array[i] -- Reflected TArray indexing returns a struct directly.
                if a.same(field(mapping,"Action"),b.action) then
                    local name = field(field(mapping,"Key"),"KeyName")
                    local text = type(name)=="string" and name or name:ToString()
                    if type(text)~="string" or #text==0 or #text>128 then error("Block key name unavailable") end
                    if text~="None" and not seen[text] then
                        -- Keep an owned FName, not a borrowed mapping/FKey wrapper
                        -- or its shared KeyDetails allocation, across map rebuilds.
                        found[#found+1]={KeyName=a.fname(text)};seen[text]=true
                        if #found>32 then error("Too many block keys") end
                    end
                end
            end
            return found
        end)
        if decoded then keys=result else reason="Block key mappings could not be read" end
    else
        reason="Player input ownership unavailable"
    end
    -- Contexts may disappear while opening a menu. Previously verified keys are
    -- used only to release/hand off an existing pulse, never to start a new one.
    if not keys or #keys==0 then
        if fallback and #fallback>0 then keys=fallback
        else return nil,keys,reason or "Auto parry waiting for combat block controls" end
    end
    local held = false
    for _, key in ipairs(keys) do
        local down = bool(a, s.controller, "IsInputKeyDown", key)
        if down == nil then return nil,nil,"Block key state unavailable" end
        held = held or down
    end
    return held, keys
end

function Instance:_release()
    local lease, a = self.lease, self.api
    if not lease then return true end
    local s = lease.state
    if not self:_component_owned(s) then
        self.lease = nil -- Destroyed/replaced component is never a cleanup target.
        return true
    end
    local direction = a.read(s.combat, "BlockingDirection")
    if direction ~= lease.direction then self.lease = nil; return true end
    local wanted = bool(a, s.combat, "WantsToBlock")
    if wanted == nil then return false end
    if lease.guard and wanted then
        local possessed, pawn = a.invoke(s.controller, "K2_GetPawn")
        if possessed and a.same(pawn, s.character) then
            local manual = self:_manual(s, lease.keys)
            if manual == nil then return false end
            if manual then self.lease = nil; return true end -- Hand guard to the player.
        end
        -- This setter may have completed even if the wrapper reports an error.
        a.invoke(s.combat, "SetDesiredBlockState", false)
        if bool(a, s.combat, "WantsToBlock") ~= false then return false end
    elseif lease.guard and not wanted then
        self.lease = nil -- Game/player already ended this request; preserve it.
        return true
    end
    lease.guard = false
    if a.read(s.combat, "BlockingDirection") ~= lease.direction then self.lease = nil; return true end
    a.invoke(s.combat, "SetBlockingDirection", lease.previous_direction)
    if a.read(s.combat, "BlockingDirection") ~= lease.previous_direction then return false end
    self.lease = nil
    return true
end

function Instance:_stop(message)
    self.active = false
    local cleaned = self:_release()
    self.message = cleaned and message or "Auto parry cleanup pending; release guard and try disabling again"
    return cleaned, self.message
end

function Instance:_context(s)
    local a, b = self.api, self:_bindings()
    if not b or not self:_component_owned(s) then return nil end
    if bool(a, s.combat, "IsA", "/Script/DogwoodCombat.PlayerCombatComponent") ~= true then return nil end
    local ok, stub = a.invoke(b.ai, "GetAIStubFromActor", s.character)
    if not ok or not a.valid(stub) or bool(a, stub, "IsPlayer") ~= true
        or bool(a, stub, "IsInitializedAndHasPawn") ~= true then return nil end
    local actor_ok, actor = a.invoke(stub, "GetActor")
    local board_ok, board = a.invoke(stub, "GetAIBoard")
    if not actor_ok or not a.same(actor, s.character) or not board_ok or not a.valid(board) then return nil end
    local config_ok, config = a.invoke(s.combat, "GetConfig")
    local cooldown = config_ok and a.read(config, "ParryInputCooldown")
    if not a.finite(cooldown) or cooldown < 0 or cooldown > 5 then return nil end
    return {stub=stub, cooldown=math.max(0.05, cooldown)+0.032}
end

function Instance:refresh(s)
    if self.state and self.state.key ~= s.key then
        self:_stop("Auto parry disabled after player session changed")
        self.bindings, self.seen, self.order, self.last_attempt, self.last_clock = nil, {}, {}, nil, nil
    end
    self.state = s
    if not self.active and self.lease and self:_release() then self.message = "Auto parry disabled" end
    local supported = self:_context(s) ~= nil
    if self.active and not supported then self:_stop("Auto parry bindings became unavailable") end
    return {[ACTION]=supported}, {dawnwalker_auto_parry_status=self.message, dawnwalker_auto_parry_attempts=self.attempts}
end

local function vector(a, value)
    local x, y, z = field(value,"X"), field(value,"Y"), field(value,"Z")
    if a.finite(x) and a.finite(y) and a.finite(z) then return x,y,z end
end

function Instance:_attack(s, enemy, now)
    local a = self.api
    if not a.valid(enemy) or bool(a, enemy, "IsPlayer") ~= false
        or bool(a, enemy, "IsInitializedAndHasPawn") ~= true
        or bool(a, enemy, "IsHostileTowardsPlayer") ~= true then return nil end
    local ok, actor = a.invoke(enemy, "GetActor")
    if not ok or not a.valid(actor) or a.same(actor, s.character) then return nil end
    local world_ok, world = a.invoke(actor, "GetWorld")
    if not world_ok or not a.same(world, s.world) then return nil end
    local combat = a.read(actor, "CombatComponent")
    local owner_ok, owner = a.invoke(combat, "GetCharacter")
    if not owner_ok or not a.same(owner, actor) or bool(a, combat, "IsAlive") ~= true then return nil end
    local attack = a.read(combat, "CurrentAttack")
    local direction = DIRECTIONS[field(attack, "Direction")]
    local stamp, montage_id = field(attack,"TimePerformed"), field(attack,"PlayedAttackMontageInstanceID")
    local montage, action = field(attack,"PlayedAttackMontage"), field(attack,"Action")
    if not direction or field(attack,"bIsUnblockable") ~= false
        or not a.same(field(attack,"Target"), s.combat) or not a.same(field(attack,"Instigator"), combat)
        or not a.finite(stamp) or stamp < 0 or stamp > now+0.05 or now-stamp > 15
        or not a.finite(montage_id) or montage_id < 0 or montage_id % 1 ~= 0
        or not a.valid(montage) or not a.valid(action) then return nil end
    local p_ok, p = a.invoke(s.character, "K2_GetActorLocation")
    local e_ok, e = a.invoke(actor, "K2_GetActorLocation")
    local f_ok, forward = a.invoke(s.combat, "GetCombatForwardDirection")
    local px,py,pz = vector(a,p); local ex,ey,ez = vector(a,e); local fx,fy = vector(a,forward)
    local angle = a.read(s.combat, "DefenseAngleDot")
    if not p_ok or not e_ok or not f_ok or not px or not ex or not fx or not a.finite(angle)
        or angle < -1 or angle > 1 then return nil end
    local dx,dy = ex-px,ey-py
    local distance, length = math.sqrt(dx*dx+dy*dy), math.sqrt(fx*fx+fy*fy)
    if distance < 1 or distance > 1200 or length < 0.01 or math.abs(ez-pz) > 300
        or (dx*fx+dy*fy)/(distance*length) < angle then return nil end
    return {key=a.identity(enemy)..":"..a.identity(action)..":"..a.identity(montage)..":"..montage_id..":"..string.format("%.17g",stamp),
        direction=direction, combat=combat, actor=actor, stamp=stamp, montage_id=montage_id}
end

function Instance:tick(s)
    if not self.active then return true end
    if not s or not self.state or s.key ~= self.state.key or not self:_current(s) then
        self:_stop("Auto parry disabled after player session changed"); return true
    end
    local a, b = self.api, self:_bindings()
    local context = self:_context(s)
    if not context or not b then self:_stop("Auto parry bindings became unavailable"); return true end
    local clock_ok, now = a.invoke(b.gameplay, "GetTimeSeconds", s.character)
    if not clock_ok or not a.finite(now) or now < 0 or (self.last_clock and now < self.last_clock) then
        self:_stop("Auto parry clock unavailable"); return true
    end
    self.last_clock = now
    local paused = bool(a, b.gameplay, "IsGamePaused", s.character)
    local ignored = bool(a, s.controller, "IsMoveInputIgnored")
    local cinematic = bool(a, context.stub, "IsInCinematicMode")
    if paused ~= false or ignored ~= false or cinematic ~= false then
        if not self:_release() then self:_stop("Auto parry cleanup pending; release guard and try disabling again") end
        self.message = self.lease and self.message or "Auto parry waiting for player control"
        return true
    end
    local hit, timing = {}, {}
    local incoming_ok, enemy = a.invoke(b.incoming, "GetNextIncomingAttacker", context.stub, hit, timing)
    if not incoming_ok or type(hit.bIsHitIncoming) ~= "boolean" then
        self:_stop("Auto parry incoming-attack data unavailable"); return true
    end
    local attack = hit.bIsHitIncoming and self:_attack(s,enemy,now) or nil
    local time_to_hit = timing.TimeToHitWindow
    if self.lease then
        if now >= self.lease.until_time or not attack or attack.key ~= self.lease.attack then
            if not self:_release() then self:_stop("Auto parry cleanup pending; release guard and try disabling again") end
        end
        if self.lease then return true end
    end
    self.message = "Auto parry watching for a parryable attack"
    if not attack or self.seen[attack.key] then return true end
    if not a.finite(time_to_hit) or time_to_hit < -0.02 or time_to_hit > 0.25 then return true end
    if self.last_attempt and now-self.last_attempt < context.cooldown then return true end
    local point_ok, point = a.invoke(b.combat, "GetParryPointNotificationTime", enemy)
    -- Native timestamps use world game time. Missing notifies return a sentinel.
    -- Never queue a wall-clock callback that can fire after pause/loading.
    if not point_ok or not a.finite(point) or point < 0 or point > now+2 then return true end
    local press_at = math.min(point, now+time_to_hit-0.05)
    if now < press_at then return true end
    local manual, keys = self:_manual(s)
    if manual ~= false then self.message = manual and "Auto parry waiting for manual guard to release" or "Auto parry waiting for combat block controls"; return true end
    if bool(a,s.combat,"WantsToBlock") ~= false or bool(a,s.combat,"IsBlocking") ~= false
        or bool(a,s.combat,"CanDefend") ~= true
        or bool(a,s.combat,"CanPerformBlockInDirection",attack.direction) ~= true then return true end
    local previous = a.read(s.combat,"BlockingDirection")
    if not VALID_DIRECTIONS[previous] then self:_stop("Auto parry guard direction unavailable"); return true end
    -- Recheck the attack immediately before mutation, including cancellation,
    -- target switches and unblockable flags. Do not hold borrowed struct wrappers.
    local fresh = self:_attack(s,enemy,now)
    if not fresh or fresh.key ~= attack.key or not self:_current(s) then return true end
    self.seen[attack.key] = true; self.order[#self.order+1] = attack.key
    if #self.order > 64 then self.seen[table.remove(self.order,1)] = nil end
    self.last_attempt, self.attempts = now, self.attempts+1
    -- Press before changing direction. Native guard-press and direction-change
    -- both update cooldown timestamps; reversing this order flags a double tap.
    self.lease = {state=s, attack=attack.key, previous_direction=previous, direction=previous,
        keys=keys, guard=true, until_time=now+math.max(0.08,math.min(0.35,time_to_hit+0.12))}
    local pressed = a.invoke(s.combat,"SetDesiredBlockState",true)
    if not pressed or bool(a,s.combat,"WantsToBlock") ~= true then
        self:_stop("Auto parry guard request was not confirmed"); return true
    end
    if not self:_current(s) then self:_stop("Auto parry disabled after player session changed"); return true end
    self.lease.direction = attack.direction -- Covers a setter that mutates then throws.
    local directed = a.invoke(s.combat,"SetBlockingDirection",attack.direction)
    if not directed or a.read(s.combat,"BlockingDirection") ~= attack.direction then
        -- If no direction write happened, the original guard request is still ours.
        if a.read(s.combat,"BlockingDirection") == previous then self.lease.direction = previous end
        self:_stop("Auto parry guard direction was not confirmed"); return true
    end
    self.message = "Auto parry guard requested; native combat rules apply"
    return true
end

function Instance:apply(s, action, enabled)
    if action ~= ACTION then return false,"Unknown action" end
    if enabled == false then return self:_stop("Auto parry disabled") end
    if enabled ~= true then return false,"A boolean enabled value is required" end
    if self.active then return true,self.message end
    if self.lease then return false,"Auto parry cleanup pending; release guard and try disabling again" end
    if not s or not self:_context(s) then return false,"Auto parry bindings unavailable in this build" end
    local manual, keys, reason = self:_manual(s)
    if manual == nil and keys == nil then return false,reason end
    self.state, self.active, self.message = s, true, manual==nil and reason or "Auto parry watching for a parryable attack"
    return true,self.message
end
function Instance:enabled() return {[ACTION]=self.active == true} end
function Instance:disable_all() return self:_stop("Auto parry disabled") end
function M.new(api)
    return setmetatable({api=api, active=false, attempts=0, seen={}, order={}, message="Auto parry disabled"},Instance)
end
return M
