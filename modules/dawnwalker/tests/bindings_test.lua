-- Execute real bindings against UObject-shaped boundary fixtures. Methods below
-- are the observed engine contract; no game is loaded and no native call occurs.
local checks, next_address, native_calls, discovery_calls = 0, 100, 0, 0
local function equal(actual, expected, label)
    checks=checks+1
    assert(actual==expected,(label or "Mismatch")..": "..tostring(actual).." ~= "..tostring(expected))
end
local function object(name)
    next_address=next_address+1
    local result={_valid=true,_name=name,_address=next_address}
    function result:IsValid() native_calls=native_calls+1; return self._valid end
    function result:GetAddress() native_calls=native_calls+1; return self._address end
    function result:GetFullName() native_calls=native_calls+1; return self._name.."_"..self._address end
    return result
end
local function fixture(observe, factories)
    local f={health=0.4,stamina=0.3,blood_amount=20,blood_capacity=100,local_player=true,authority=true,
        alive=true,combat_alive=true,writes={},attempts={},functions={},changes={},function_lookups=0}
    f.world,f.controller,f.pawn,f.combat,f.blood=object("World"),object("Controller"),object("Player"),object("Combat"),object("Blood")
    f.pawn.Controller,f.pawn.CombatComponent,f.pawn.BloodBar=f.controller,f.combat,f.blood
    f.owner,f.possessed=f.pawn,f.pawn
    function f.pawn:IsLocallyControlled() return f.local_player end
    function f.pawn:HasAuthority() return f.authority end
    function f.pawn:IsAlive() return f.alive end
    function f.pawn:GetWorld() return f.world end -- UE4SS AActor wrapper method.
    -- This name is present in the inspected game's reflection snapshot.
    -- AController's native GetPawn helper is not a reflected Lua-callable method.
    function f.controller:K2_GetPawn() return f.possessed end
    function f.combat:GetCharacter() return f.owner end
    function f.combat:IsAlive() return f.combat_alive end
    function f.combat:GetHealthPercentage() if f.read_error then error("getter failure") end; return f.health end
    function f.combat:GetStaminaPercentage() return f.stamina end
    function f.blood:GetBlood() return f.blood_amount end
    function f.blood:GetBloodBarLength() return f.blood_capacity end
    local function write(resource,value)
        assert(value==1.0,"Native setters receive percent 1.0 only")
        f.attempts[#f.attempts+1]=resource
        if f.setter_error==resource then error("setter failure") end
        if f.noop~=resource then
            f.writes[#f.writes+1]=resource
            if resource=="health" then f.health=value
            elseif resource=="stamina" then f.stamina=value
            else f.blood_amount=f.blood_capacity*value end
        end
        if f.after_write then f.after_write(resource) end
    end
    function f.combat:SetHealthPercent(value) write("health",value) end
    function f.combat:SetStaminaPercent(value) write("stamina",value) end
    function f.blood:SetBloodPercent(value) write("blood",value) end
    -- These are gameplay-owned lock handles: any attempted use is a test failure.
    local function forbidden() error("Trainer must not modify native gameplay locks") end
    f.combat.LockHealth,f.combat.UnlockHealth,f.combat.LockStamina,f.combat.UnlockStamina=forbidden,forbidden,forbidden,forbidden
    f.blood.LockBlood,f.blood.UnlockBlood=forbidden,forbidden
    for _, path in ipairs({"/Script/DogwoodCombat.CombatComponentBase:SetHealthPercent",
        "/Script/DogwoodCombat.CombatComponentBase:SetStaminaPercent", "/Script/DogwoodStats.BloodBarComponent:SetBloodPercent"}) do
        f.functions[path]=object("Function")
    end
    local function forbidden_scan()
        discovery_calls=discovery_calls+1
        error("Global object discovery must not be used")
    end
    f.registered={}
    local env=setmetatable({
        PropertyTypes={IntProperty="int32 fixture",ByteProperty="byte fixture"},
        RegisterCustomProperty=function(info)
            if f.registration_failure then error("fixture registration failed") end
            f.registered[#f.registered+1]=info
        end,
        FindAllOf=forbidden_scan, FindFirstOf=forbidden_scan,
        StaticFindObject=function(path)
            native_calls=native_calls+1; f.function_lookups=f.function_lookups+1
            return f.functions[path]
        end
    },{__index=_G})
    local module=assert(loadfile("modules/dawnwalker/Scripts/bindings.lua","t",env))()
    f.trainer=module.new(function(action,enabled,message) f.changes[#f.changes+1]={action,enabled,message} end, factories or {})
    if observe ~= false then f.trainer:observe(f.pawn) end
    return f
end
local function ready(f)
    local is_ready,_,values,available=f.trainer:refresh()
    equal(is_ready,true,"Fixture ready")
    return values,available
end

do
    local f=fixture(false)
    local before=native_calls
    for _=1,10000 do assert(f.trainer:refresh()==false,"Startup must wait for a notification") end
    equal(native_calls,before,"Empty startup/menu polling makes no engine calls")
    equal(discovery_calls,0,"Empty startup/menu polling never searches the object array")
    equal(#f.writes,0)
    -- A real pawn may already exist, but it is unavailable until a construction
    -- notification is received. Late mod loading requires restarting the game.
    equal(f.trainer:apply("dawnwalker_refill",false),false,"No late-load scan fallback")
    f.trainer:observe(f.pawn)
    equal(native_calls,before,"Observation makes no engine calls")
    ready(f)
end
do
    local f=fixture(false)
    local accesses=0
    local meta={__index=function() accesses=accesses+1; error("Object is still constructing") end,
        __eq=function() accesses=accesses+1; error("No native wrapper equality in notification") end}
    local first,second=setmetatable({},meta),setmetatable({},meta)
    f.trainer:observe(first); f.trainer:observe(first); f.trainer:observe(second); f.trainer:observe(nil)
    equal(accesses,0,"Construction callbacks only store raw wrappers")
    equal(#f.trainer.candidates,2,"Repeated raw wrapper notifications are deduplicated")
end
do
    local f=fixture(false); f.local_player=false; f.possessed=nil
    f.trainer:observe(f.pawn)
    equal(f.trainer:refresh(),false,"Constructed pawn may not yet be possessed")
    equal(#f.trainer.candidates,1,"Unpossessed candidate is retained")
    f.local_player=true; f.possessed=f.pawn; ready(f)
    equal(f.trainer:apply("dawnwalker_health",true),true)
    f.alive=false; f.health=0.3; local writes=#f.writes
    equal(f.trainer:refresh(),false,"Death removes readiness")
    equal(f.trainer:enabled().dawnwalker_health,false,"Death stops top-ups")
    equal(#f.trainer.candidates,1,"Death does not require a new construction notification")
    f.alive=true; ready(f)
    equal(#f.writes,writes,"Revival leaves top-ups off")
    f.pawn._valid=false
    equal(f.trainer:refresh(),false); equal(#f.trainer.candidates,0,"Invalid wrappers are pruned")
end
do
    local f=fixture(); ready(f)
    for _=1,15 do
        local candidate=object("DestroyedCandidate"); candidate._valid=false
        f.trainer:observe(candidate)
    end
    equal(#f.trainer.candidates,16)
    ready(f); equal(#f.trainer.candidates,1,"Polling prunes expired wrappers and frees bounded capacity")
    local candidate=object("NextDestroyedCandidate"); candidate._valid=false
    f.trainer:observe(candidate); ready(f)
    equal(#f.trainer.candidates,1,"A later notification fits capacity reclaimed by pruning")
end
do
    local f=fixture(); ready(f)
    equal(f.trainer:apply("dawnwalker_health",true),true)
    f.health=0.2; local writes=#f.writes
    local before=native_calls
    local old=object("OldCandidate")
    f.trainer:observe(old)
    for _=1,100 do f.trainer:observe(object("NewCandidate")) end
    equal(native_calls,before,"Candidate overflow makes no native calls")
    equal(#f.trainer.candidates,16,"Notification bursts cannot create unbounded work")
    local selected_present,old_present=false,false
    for _,candidate in ipairs(f.trainer.candidates) do
        selected_present=selected_present or rawequal(candidate,f.pawn)
        old_present=old_present or rawequal(candidate,old)
        if not rawequal(candidate,f.pawn) then candidate._valid=false end
    end
    equal(selected_present,true,"Overflow preserves the selected pawn")
    equal(old_present,true,"Overflow does not discard a potentially ambiguous pawn")
    local is_ready,message=f.trainer:refresh()
    equal(is_ready,false,"Incomplete candidate discovery fails closed")
    equal(message,"Trainer candidate limit reached; restart the game")
    equal(f.trainer:enabled().dawnwalker_health,false,"Overflow clears previously enabled controls")
    equal(#f.writes,writes,"Overflow prevents writes even to the previously selected pawn")
    equal(f.trainer:apply("dawnwalker_health",true),false,"Overflow cannot be bypassed with a command")
    equal(f.trainer:refresh(),false,"Pruning tracked invalids cannot account for omitted candidates")
    equal(native_calls,before,"Overflow remains paused without touching tracked game objects")
end
do
    local f=fixture(); local values,available=ready(f)
    equal(#f.writes,0,"Polling while disabled never writes resources")
    equal(values.health_percent,40); equal(values.stamina_percent,30); equal(values.blood,20)
    equal(available.dawnwalker_health,true); equal(available.dawnwalker_stamina,true); equal(available.dawnwalker_blood,true)
    equal(available.dawnwalker_refill,true); equal(available.dawnwalker_teleport,false)
    equal(f.trainer:enabled().dawnwalker_health,false)
    equal(f.trainer:apply("dawnwalker_health",true),true); equal(f.health,1); equal(f.trainer:enabled().dawnwalker_health,true)
    f.health=0.2; ready(f); equal(f.health,1); equal(#f.writes,2)
    ready(f); equal(#f.writes,2,"Full health skips setter")
    f.health=0.5; equal(f.trainer:apply("dawnwalker_health",false),true); ready(f)
    equal(f.health,0.5); equal(#f.writes,2,"Disabling neither refills nor releases game locks")
    local flags=f.trainer:enabled(); flags.dawnwalker_health=true
    equal(f.trainer:enabled().dawnwalker_health,false,"Returned state is a copy")
end
do
    local f=fixture(); local values=ready(f)
    equal(values.diagnostic_player_candidates,1)
    equal(values.diagnostic_player_state,"ready")
    f.controller.K2_GetPawn=nil
    local is_ready,message,diagnostics=f.trainer:refresh()
    equal(is_ready,false,"A missing reflected possession getter fails closed")
    equal(message,"Controlled-pawn binding unavailable","Readiness failures identify the failing stage")
    equal(diagnostics.diagnostic_player_state,message)
    equal(#f.writes,0)
end
do
    local f=fixture(); ready(f)
    equal(f.function_lookups,3,"Setter availability resolves once per binding")
    for _=1,1000 do assert(f.trainer:refresh()) end
    equal(f.function_lookups,3,"Repeated status polls reuse cached native functions")
    equal(f.trainer:apply("dawnwalker_health",true),true)
    f.health=0.2; ready(f)
    equal(f.function_lookups,3,"Command and active top-up polls do not repeat global lookups")
    f.world=object("NewWorld"); ready(f)
    equal(f.function_lookups,6,"A new player session rebuilds binding availability")
    local path="/Script/DogwoodCombat.CombatComponentBase:SetHealthPercent"
    f.functions[path]._valid=false
    local _,available=ready(f)
    equal(available.dawnwalker_health,false,"Cached wrappers are revalidated before availability")
    for _=1,100 do assert(f.trainer:refresh()) end
    equal(f.function_lookups,6,"Invalid cached functions do not trigger per-frame rediscovery")
end
do
    local f=fixture()
    local path="/Script/DogwoodCombat.CombatComponentBase:SetHealthPercent"
    f.functions[path]=nil
    local _,available=ready(f); equal(available.dawnwalker_health,false)
    f.functions[path]=object("LateFunction")
    ready(f); equal(f.function_lookups,3,"Missing binding is cached for the existing session")
    equal(f.trainer:apply("dawnwalker_health",true),false,"Missing binding remains fail-closed in its session")
    f.world=object("NewWorld"); _,available=ready(f)
    equal(available.dawnwalker_health,true,"New session can resolve a previously missing function")
    equal(f.function_lookups,6)
end
do
    local f=fixture(); ready(f)
    equal(f.trainer:apply("dawnwalker_stamina",true),true); equal(f.stamina,1)
    equal(f.trainer:apply("dawnwalker_blood",true),true); equal(f.blood_amount,100)
    local count=#f.writes; ready(f); equal(#f.writes,count,"Full stamina and blood skip setters")
    f.stamina,f.blood_amount=0,0; ready(f); equal(f.stamina,1); equal(f.blood_amount,100)
    equal(f.trainer:disable_all(),true); f.health,f.stamina,f.blood_amount=0.2,0.1,1; count=#f.writes; ready(f)
    equal(#f.writes,count,"Disable all performs no engine writes")
    equal(f.trainer:apply("dawnwalker_refill",false),true); equal(f.health,1); equal(f.stamina,1); equal(f.blood_amount,1,"Refill preserves blood")
    equal(f.trainer:enabled().dawnwalker_health,false); equal(f.trainer:enabled().dawnwalker_stamina,false)
    equal(f.trainer:enabled().dawnwalker_refill,nil,"Refill is never persistent")
end
do
    local f=fixture(); f.functions["/Script/DogwoodCombat.CombatComponentBase:SetHealthPercent"]=nil
    local _,available=ready(f); equal(available.dawnwalker_health,false); equal(available.dawnwalker_refill,false)
    equal(available.dawnwalker_stamina,true); equal(f.trainer:apply("dawnwalker_health",true),false); equal(#f.writes,0)
    equal(f.trainer:apply("dawnwalker_stamina",true),true)
    f.functions["/Script/DogwoodCombat.CombatComponentBase:SetStaminaPercent"]._valid=false
    f.functions["/Script/DogwoodCombat.CombatComponentBase:SetStaminaPercent"]=nil; f.stamina=0.2
    local count=#f.writes; ready(f); equal(f.trainer:enabled().dawnwalker_stamina,false); equal(#f.writes,count)
end
for _, unavailable in ipairs({"absent","negative","bad_capacity","nonfinite"}) do
    local f=fixture()
    if unavailable=="absent" then f.pawn.BloodBar=nil
    elseif unavailable=="negative" then f.blood_amount=-1
    elseif unavailable=="bad_capacity" then f.blood_capacity=0
    else f.blood_amount=0/0 end
    local _,available=ready(f)
    equal(available.dawnwalker_blood,false,"Unavailable blood "..unavailable)
    equal(available.dawnwalker_refill,true,"Missing blood does not disable health/stamina")
    equal(f.trainer:apply("dawnwalker_blood",true),false); equal(#f.writes,0)
end
local invalid={
    remote=function(f) f.local_player=false end,
    non_authority=function(f) f.authority=false end,
    dead_pawn=function(f) f.alive=false end,
    dead_combat=function(f) f.combat_alive=false end,
    enemy_component=function(f) f.owner=object("Enemy") end,
    unpossessed=function(f) f.possessed=object("OtherPawn") end,
    no_world=function(f) f.world._valid=false end,
    dead_health=function(f) f.health=0 end,
    nan_health=function(f) f.health=0/0 end,
    infinite_health=function(f) f.health=math.huge end,
    negative_stamina=function(f) f.stamina=-1 end,
    nan_stamina=function(f) f.stamina=0/0 end,
    invalid_pawn=function(f) f.pawn._valid=false end,
    throwing_getter=function(f) f.read_error=true end,
    no_player=function() end
}
for label,invalidate in pairs(invalid) do
    local f=fixture(label~="no_player"); invalidate(f)
    equal(f.trainer:refresh(),false,"Reject "..label)
    equal(f.trainer:apply("dawnwalker_health",true),false,"No enable for "..label)
    equal(#f.writes,0,"No write for "..label)
end
do
    local f,other=fixture(),fixture(); f.trainer:observe(other.pawn)
    equal(f.trainer:refresh(),false,"Ambiguous controlled candidates rejected"); equal(#f.writes,0)
    f.alive=false; local _,available=ready(f)
    equal(available.dawnwalker_health,true,"Dead stale pawn does not hide living candidate")
    equal(f.trainer:apply("dawnwalker_health",true),true)
    equal(#f.writes,0,"No stale-pawn writes"); equal(other.health,1,"Living pawn receives operation")
end
do
    local f,other=fixture(),fixture(); ready(f)
    equal(f.trainer:apply("dawnwalker_health",true),true)
    f.health=0.2; local count=#f.writes
    f.trainer:observe(other.pawn)
    equal(f.trainer:refresh(),false,"A cached live pawn cannot hide a newly observed local pawn")
    equal(f.trainer:enabled().dawnwalker_health,false,"Ambiguous ownership disables top-ups")
    equal(#f.writes,count,"Ambiguity is detected before any top-up")
    other.pawn._valid=false; ready(f)
    equal(f.trainer:enabled().dawnwalker_health,false,"Resolved ambiguity does not re-enable controls")
end
do
    local f=fixture(); ready(f)
    equal(f.trainer:apply("dawnwalker_health",true),true)
    -- UE4SS may return distinct wrappers for the same native object. They are
    -- raw-distinct during observation, but cannot count as two possessed pawns.
    local alias=setmetatable({},{__index=f.pawn})
    f.trainer:observe(alias); ready(f)
    equal(f.trainer:enabled().dawnwalker_health,true,"Aliases of one native pawn are not ambiguous")
end
do
    local f=fixture(); ready(f); equal(f.trainer:apply("dawnwalker_health",true),true)
    local previous=f.trainer:session_key(); f.world=object("NextWorld"); f.health=0.4
    local count=#f.writes; ready(f)
    equal(f.trainer:session_key()~=previous,true); equal(f.trainer:enabled().dawnwalker_health,false)
    equal(#f.writes,count,"World transition clears toggles before top-ups")
    -- Simulate lifecycle change after mailbox session validation, before apply.
    f.world=object("WorldDuringCommand"); count=#f.writes
    equal(f.trainer:apply("dawnwalker_health",true),false,"Stale command cannot attach to a replacement world")
    equal(#f.writes,count); equal(f.trainer:enabled().dawnwalker_health,false)
    equal(f.trainer:apply("dawnwalker_health",true),true,"Fresh action may target current session")
end
for _,component in ipairs({"combat","blood"}) do
    local f=fixture(); ready(f)
    equal(f.trainer:apply("dawnwalker_health",true),true)
    f.health=0.2; local count=#f.writes
    local replacement=object("Replacement"..component)
    for key,value in pairs(f[component]) do
        if type(value)=="function" then replacement[key]=value end
    end
    f[component]=replacement
    if component=="combat" then f.pawn.CombatComponent=replacement else f.pawn.BloodBar=replacement end
    ready(f)
    equal(f.trainer:enabled().dawnwalker_health,false,"Component replacement clears enabled controls")
    equal(#f.writes,count,"Changed component cannot receive an old session's top-up")
end
for _, resource in ipairs({"health","stamina","blood"}) do
    local f=fixture(); ready(f); f.noop=resource
    equal(f.trainer:apply("dawnwalker_"..resource,true),false,"Silent setter no-op fails readback: "..resource)
    equal(f.trainer:enabled()["dawnwalker_"..resource],false)
    f.noop=nil; equal(f.trainer:apply("dawnwalker_"..resource,true),true)
    if resource=="blood" then f.blood_amount=0 else f[resource]=0.2 end
    f.setter_error=resource; ready(f)
    equal(f.trainer:enabled()["dawnwalker_"..resource],false,"Thrown setter disables feature: "..resource)
end
do
    local f=fixture(); ready(f)
    f.after_write=function(resource) if resource=="health" then f.authority=false end end
    equal(f.trainer:apply("dawnwalker_refill",false),false,"Partial refill reports failure")
    equal(#f.writes,1,"Possession/authority rechecked before second resource")
    equal(f.health,1); equal(f.stamina,0.3)
    equal(f.trainer:enabled().dawnwalker_health,false); equal(f.trainer:enabled().dawnwalker_stamina,false)
end
do
    local f=fixture(); ready(f)
    equal(f.trainer:apply("dawnwalker_teleport",false),false)
    equal(f.trainer:apply("execute",true),false)
    equal(f.trainer:apply("dawnwalker_health","true"),false)
    equal(f.trainer:apply("dawnwalker_health",nil),false)
    equal(#f.writes,0,"Unknown controls and malformed toggles make no writes")
    equal(f.trainer:apply("dawnwalker_health",true),true)
    f.alive=false; local count=#f.writes
    equal(f.trainer:apply("dawnwalker_health",false),true,"Off works after player becomes unavailable")
    equal(f.trainer:disable_all(),true); equal(#f.writes,count)
end
do
    local state_seen, disabled, applied, fly_ticks = nil, 0, 0, 0
    local extension = { flag=false }
    function extension:refresh(state)
        state_seen=state
        return {dawnwalker_money=true, dawnwalker_xp=true}, {xp_reward=50}
    end
    function extension:apply(state, action, enabled)
        if enabled==false and action=="dawnwalker_money" then self.flag=false; return true,"Off" end
        assert(state and state.controller and state.world,"Extensions receive a validated complete state")
        applied=applied+1
        if action=="dawnwalker_money" then self.flag=true end
        return true,"Applied"
    end
    function extension:disable_all() disabled=disabled+1; self.flag=false; return true,"Off" end
    function extension:enabled() return {dawnwalker_money=self.flag} end
    local f=fixture(true,{economy={new=function(api)
        assert(type(api.resolve)=="function" and type(api.write)=="function" and type(api.fname)=="function")
        return extension
    end}})
    local values,available=ready(f)
    equal(state_seen.controller,f.controller); equal(state_seen.world,f.world)
    equal(available.dawnwalker_money,true); equal(values.xp_reward,50)
    equal(f.trainer:apply("dawnwalker_xp",true),false,"XP cannot be enabled as a toggle")
    equal(f.trainer:apply("dawnwalker_xp",false),true); equal(applied,1)
    equal(f.trainer:apply("dawnwalker_money",true),true); equal(f.trainer:enabled().dawnwalker_money,true)
    f.world=object("ChangedExtensionWorld"); ready(f)
    equal(f.trainer:enabled().dawnwalker_money,false,"Session transition cleans extension overrides")
    equal(disabled,2,"Cleanup occurs before either session is used")
    equal(f.trainer:apply("dawnwalker_money",true),true)
    f.alive=false
    equal(f.trainer:apply("dawnwalker_money",false),true,"Extension off does not depend on readiness")
    equal(f.trainer:enabled().dawnwalker_money,false)
    f.alive=true; f.world=object("ChangedDuringExtensionCommand")
    equal(f.trainer:apply("dawnwalker_money",true),false,"Stale extension command rejected")
    equal(applied,3,"Stale extension command performs no writes")
end
do
    local movement={flag=false, polls=0, applied=0}
    function movement:refresh(state)
        self.state=state
        return {dawnwalker_auto_teleport=true}, {}
    end
    function movement:apply(state, action, enabled)
        equal(action,"dawnwalker_auto_teleport","Auto toggle delegates to movement")
        if enabled==false then self.flag=false; self.pending=nil; return true,"Off" end
        assert(state and state.character and state.controller and state.world,"Auto enable receives validated state")
        self.flag=true; self.applied=self.applied+1; return true,"Enabled"
    end
    function movement:disable_all() self.flag=false; self.pending=nil; return true,"Off" end
    function movement:enabled() return {dawnwalker_auto_teleport=self.flag} end
    function movement:set_marker_observation_ready(ready)
        self.observer_ready=ready==true
        if not self.observer_ready then self.flag=false; self.pending=nil end
    end
    function movement:observe_marker(map,location) self.pending={map=map,location=location} end
    function movement:process_auto_teleport()
        self.polls=self.polls+1
        if not self.flag or not self.pending then return nil end
        self.pending=nil; return self.result,"Automatic result"
    end
    local f=fixture(true,{movement={new=function() return movement end}})
    local _,available=ready(f)
    equal(available.dawnwalker_auto_teleport,true)
    equal(f.trainer:enabled().dawnwalker_auto_teleport,false,"Automatic travel starts off")
    local before=native_calls
    f.trainer:set_marker_observation_ready(true)
    equal(movement.observer_ready,true,"Hook readiness delegates to movement")
    equal(native_calls,before,"Observer readiness is pure Lua, including within POST hook failure cleanup")
    equal(f.trainer:apply("dawnwalker_auto_teleport",nil),false,"Auto is a boolean toggle, not a one-shot")
    equal(f.trainer:apply("dawnwalker_auto_teleport","true"),false)
    equal(f.trainer:apply("dawnwalker_auto_teleport",true),true)
    equal(f.trainer:enabled().dawnwalker_auto_teleport,true)
    equal(f.trainer:process_auto_teleport(),nil,"Idle return is preserved")
    local map,location={}, {X=1,Y=2,Z=3}
    f.trainer:observe_marker(map,location)
    equal(movement.polls,1,"Observation only queues; it cannot process travel")
    for _,result in ipairs({true,false}) do
        movement.result=result; f.trainer:observe_marker(map,location)
        local success,message=f.trainer:process_auto_teleport()
        equal(success,result,"Consumed automatic result is preserved")
        equal(message,"Automatic result")
    end
    f.alive=false
    equal(f.trainer:apply("dawnwalker_auto_teleport",false),true,"Off delegates before readiness checks")
    equal(f.trainer:enabled().dawnwalker_auto_teleport,false)
    f.alive=true; equal(f.trainer:apply("dawnwalker_auto_teleport",true),true)
    f.world=object("NewAutoWorld"); ready(f)
    equal(f.trainer:enabled().dawnwalker_auto_teleport,false,"Session changes clear auto toggle")
    equal(f.trainer:process_auto_teleport(),nil)
    movement.flag=true; movement.pending={}
    f.trainer:set_marker_observation_ready(false)
    equal(movement.observer_ready,false); equal(movement.flag,false); equal(movement.pending,nil)
    local bare=fixture(false)
    equal(bare.trainer:process_auto_teleport(),nil,"Optional movement module absence is idle")
    equal(bare.trainer:set_marker_observation_ready(false),nil,"Missing optional module tolerates failed observation cleanup")
end

do
    local api_seen
    local movement={}
    function movement:observe_marker(map,location) self.map=map; self.location=location end
    function movement:forget_marker(map) self.forgotten=map end
    local f=fixture(false,{movement={new=function(api) api_seen=api; return movement end}})
    equal(api_seen.register_property("Other", "/Script/Engine.Actor", "IntProperty", 0),false,"Arbitrary layouts rejected")
    equal(#f.registered,0)
    for _=1,3 do equal(api_seen.register_property("HappyCheatsItemHandleIndex", "/Script/DogwoodInventory.ItemHandle", "IntProperty", 0),true) end
    equal(#f.registered,1,"Repeated binds cannot append duplicate custom properties")
    equal(f.registered[1].Type,"int32 fixture"); equal(f.registered[1].OffsetInternal,0)
    equal(api_seen.register_property("HappyCheatsPlayerStubVisibilityFlags", "/Script/DogwoodAI.DogwoodPlayerAIStub", "ByteProperty", 0x30),true)
    equal(#f.registered,2); equal(f.registered[2].Type,"byte fixture")
    equal(api_seen.register_property("HappyCheatsHitResultFlags", "/Script/Engine.HitResult", "ByteProperty", 0xAD),true)
    equal(#f.registered,3); equal(f.registered[3].Type,"byte fixture"); equal(f.registered[3].OffsetInternal,0xAD)
    equal(api_seen.register_property("HappyCheatsHitResultFlags", "/Script/Engine.HitResult", "ByteProperty", 0xAD),true)
    equal(#f.registered,3,"HitResult field registration is cached")
    equal(api_seen.register_property("HappyCheatsHitResultFlags", "/Script/Engine.HitResult", "ByteProperty", 0xAC),false,"Wrong packed-flag layout rejected")
    equal(api_seen.register_property("HappyCheatsHitResultFlags", "/Script/Engine.HitResult", "IntProperty", 0xAD),false,"Wrong packed-flag size rejected")
    local map,location={}, {X=1,Y=2,Z=3}
    f.trainer:observe_marker(map,location); equal(movement.map,map); equal(movement.location,location)
    f.trainer:forget_marker(map); equal(movement.forgotten,map)
    local rejected_api
    local rejected=fixture(false,{movement={new=function(api) rejected_api=api; return {} end}})
    rejected.registration_failure=true
    equal(rejected_api.register_property("HappyCheatsItemHandleIndex", "/Script/DogwoodInventory.ItemHandle", "IntProperty", 0),false)
    rejected.registration_failure=false
    equal(rejected_api.register_property("HappyCheatsItemHandleIndex", "/Script/DogwoodInventory.ItemHandle", "IntProperty", 0),false,"Uncertain registration is not retried")
    equal(#rejected.registered,0)
end
do
    local factories={}
    for _,name in ipairs({"combat","movement","economy"}) do factories[name]=dofile("modules/dawnwalker/Scripts/"..name..".lua") end
    local f=fixture(false,factories)
    local before=native_calls
    for _=1,1000 do assert(f.trainer:refresh()==false) end
    equal(native_calls,before,"All real extensions remain idle before gameplay")
    f.trainer:observe(f.pawn)
    local _,available=ready(f)
    equal(available.dawnwalker_health,true,"Missing optional bindings do not hide base controls")
    for _,action in ipairs({"invulnerable","damage","invisible","fly","teleport","auto_teleport","carry","money","items","xp"}) do
        equal(available["dawnwalker_"..action],false,"Unavailable optional binding fails closed: "..action)
    end
    for _,enabled in pairs(f.trainer:enabled()) do equal(enabled,false,"Every extension starts off") end
    equal(f.trainer:disable_all(),true)
end
equal(discovery_calls,0,"No binding path falls back to global discovery")
print("Dawnwalker native binding boundary: "..checks.." assertions passed")
