-- Real resource extension with synthetic native boundaries, never a game process.
local checks=0
local function eq(a,b,label) checks=checks+1; assert(a==b,(label or "Mismatch")..": "..tostring(a).." ~= "..tostring(b)) end
local HEALTH,STAMINA="dawnwalker_health","dawnwalker_stamina"
local function obj() return {_valid=true} end
local function fixture()
    local f={effects={},calls={},next_id=100,health=0.4,stamina=0.3,sets=0,registrations=0}
    f.character,f.combat,f.asc,f.library,f.controller=obj(),obj(),obj(),obj(),obj()
    f.default=obj();f.default.DurationPolicy=1;f.default.StackingType=0
    f.character.Controller,f.character.CombatComponent,f.character.AbilitySystemComponent=f.controller,f.combat,f.asc
    f.combat.HappyCheatsHealthLockHandle,f.combat.HappyCheatsStaminaLockHandle=-1,-1
    f.state={character=f.character,combat=f.combat,controller=f.controller,key="local-session"}
    function f.controller:K2_GetPawn() return f.character end
    function f.character:IsLocallyControlled() return not f.remote end
    function f.character:HasAuthority() return not f.remote end
    function f.character:IsAlive() return not f.dead end
    function f.combat:GetCharacter() return f.character end
    function f.combat:GetAbilitySystemComponent() return f.combat_asc or f.asc end
    function f.combat:GetHealthPercentage() return f.health end
    function f.combat:GetStaminaPercentage() return f.stamina end
    function f.combat:SetHealthPercent(v) f.sets=f.sets+1; if not f.ignore_set then f.health=v end; if f.after_set then f.after_set() end end
    function f.combat:SetStaminaPercent(v) f.sets=f.sets+1; if not f.ignore_set then f.stamina=v end end
    local function lock(kind)
        f.calls[#f.calls+1]="lock_"..kind
        local key=kind=="health" and "HappyCheatsHealthLockHandle" or "HappyCheatsStaminaLockHandle"
        local old=f.combat[key];if old~=-1 then f.effects[old]=0 end
        f.next_id=f.next_id+1;f.combat[key]=f.next_id;f.effects[f.next_id]=1
        if f.lock_error then error("native outcome unknown") end
    end
    local function unlock(kind)
        f.calls[#f.calls+1]="unlock_"..kind
        if f.unlock_error then error("native outcome unknown") end
        if f.ignore_unlock then return end
        local key=kind=="health" and "HappyCheatsHealthLockHandle" or "HappyCheatsStaminaLockHandle"
        f.effects[f.combat[key]]=0;f.combat[key]=-1
    end
    function f.combat:LockHealth() lock("health") end
    function f.combat:LockStamina() lock("stamina") end
    function f.combat:UnlockHealth() unlock("health") end
    function f.combat:UnlockStamina() unlock("stamina") end
    function f.library:GetActiveGameplayEffectStackCount(handle)
        if f.count_error then error("unreadable") end
        return f.effects[handle.Handle] or 0
    end
    f.api={
        valid=function(o) return type(o)=="table" and o._valid==true end,
        same=function(a,b) return a~=nil and a._valid and a==b end,
        finite=function(n) return type(n)=="number" and n==n and math.abs(n)<math.huge end,
        read=function(o,key) if f.unreadable==key then return nil end;return o and o._valid and o[key] end,
        invoke=function(o,method,...) if not o or not o._valid then return false end;return pcall(o[method],o,...) end,
        has_function=function(path) return path~=f.missing end,
        resolve=function(path) if path=="/Script/GameplayAbilities.Default__AbilitySystemBlueprintLibrary" then return f.library end;return f.default end,
        register_property=function(name,owner,kind,offset)
            eq(owner,"/Script/DogwoodCombat.CombatComponentBase");eq(kind,"IntProperty")
            eq(offset,name=="HappyCheatsHealthLockHandle" and 0x7C8 or 0x7D0)
            f.registrations=f.registrations+1;return not f.registration_error
        end
    }
    f.trainer=dofile("modules/dawnwalker/Scripts/resources.lua").new(f.api)
    return f
end
local function ready(f) local a=f.trainer:refresh(f.state);eq(a[HEALTH],true);eq(a[STAMINA],true) end
for _,action in ipairs({HEALTH,STAMINA}) do
    local f=fixture();for _=1,100 do f.trainer:refresh(nil) end;eq(f.registrations,0);eq(#f.calls,0);eq(f.sets,0)
    ready(f);eq(f.trainer:apply(f.state,action,true),true);eq(f.sets,1);eq(#f.calls,1)
    eq(f.trainer:enabled()[action],true);eq(f.trainer:apply(f.state,action,true),true);eq(#f.calls,1,"Enable is idempotent")
    for _=1,100 do ready(f) end;eq(f.sets,1,"Steady lock does not periodically refill");eq(#f.calls,1)
    f.effects[999]=1
    eq(f.trainer:apply(nil,action,false),true);eq(f.effects[999],1,"Foreign effects survive cleanup")
    eq(f.trainer:enabled()[action],false);eq(f.trainer:disable_all(),true);eq(#f.calls,2,"Disable is idempotent")
end
for _,action in ipairs({HEALTH,STAMINA}) do
    local f=fixture();ready(f)
    local key=action==HEALTH and "HappyCheatsHealthLockHandle" or "HappyCheatsStaminaLockHandle"
    f.combat[key]=900;f.effects[900]=1
    eq(f.trainer:apply(f.state,action,true),false,"Pre-existing game lock refused")
    eq(f.sets,0);eq(#f.calls,0);eq(f.effects[900],1)
    f.combat[key]=-1;eq(f.trainer:apply(f.state,action,true),true)
    local own=f.combat[key];f.effects[own]=0;f.combat[key]=901;f.effects[901]=1
    f.trainer:refresh(f.state);eq(f.trainer:enabled()[action],false);eq(f.effects[901],1);eq(#f.calls,1,"Replacement game lock is never unlocked")
end
do
    local f=fixture();ready(f);eq(f.trainer:apply(f.state,HEALTH,true),true);eq(f.trainer:apply(f.state,STAMINA,true),true)
    eq(f.trainer:apply(nil,HEALTH,false),true);eq(f.trainer:enabled()[STAMINA],true,"Health off preserves stamina lock")
    f.stamina=0.7;ready(f);eq(f.stamina,1,"Max-resource change renews only owned lock")
    eq(f.trainer:enabled()[STAMINA],true)
    eq(f.trainer:refill(f.state,STAMINA),true,"One-shot refill recaptures owned native handle")
    eq(f.trainer:disable_all(),true);eq(f.combat.HappyCheatsStaminaLockHandle,-1)
end
do
    local f=fixture();ready(f);f.ignore_set=true;eq(f.trainer:apply(f.state,HEALTH,true),false);eq(#f.calls,0,"Failed refill never locks partial health")
end
do
    local f=fixture();ready(f);f.after_set=function() f.character.AbilitySystemComponent=obj() end
    eq(f.trainer:apply(f.state,HEALTH,true),false);eq(#f.calls,0,"Reentrant ASC change prevents locking")
end
do
    local f=fixture();ready(f);f.lock_error=true;eq(f.trainer:apply(f.state,HEALTH,true),false)
    eq(f.trainer:apply(f.state,HEALTH,true),false);eq(f.trainer:disable_all(),false);eq(#f.calls,1,"Uncertain application never retries or removes unidentified effect")
    eq(f.trainer:refresh({key="new-session"})[HEALTH],false,"Pending cleanup blocks new-session activation")
    f.asc._valid=false;eq(f.trainer:disable_all(),true)
end
do
    local f=fixture();ready(f);eq(f.trainer:apply(f.state,HEALTH,true),true);f.unlock_error=true
    eq(f.trainer:apply(nil,HEALTH,false),false);eq(f.trainer:apply(nil,HEALTH,false),false);eq(#f.calls,2,"Uncertain removal never blindly retries")
end
do
    local f=fixture();ready(f);eq(f.trainer:apply(f.state,HEALTH,true),true);f.count_error=true
    eq(f.trainer:apply(nil,HEALTH,false),false);eq(#f.calls,1,"Unreadable count never unlocks")
    f.count_error=false;eq(f.trainer:apply(nil,HEALTH,false),true)
end
do
    local f=fixture();ready(f);eq(f.trainer:apply(f.state,HEALTH,true),true)
    f.trainer:refresh({key="next-session"});eq(f.trainer:enabled()[HEALTH],false,"Session change releases original effect")
end
for _,key in ipairs({"HappyCheatsHealthLockHandle","HappyCheatsStaminaLockHandle"}) do
    for _,bad in ipairs({-2,1.5,2147483648}) do
        local f=fixture();ready(f);f.combat[key]=bad;eq(f.trainer:apply(f.state,key:find("Health") and HEALTH or STAMINA,true),false);eq(#f.calls,0)
    end
end
do
    local f=fixture();ready(f);eq(f.trainer:apply(f.state,HEALTH,true),true)
    f.combat_asc=obj()
    eq(f.trainer:apply(nil,HEALTH,false),false,"Changed component ASC never receives native unlock")
    eq(#f.calls,1)
    f.combat_asc=f.asc;eq(f.trainer:apply(nil,HEALTH,false),true)
end

do
    local f=fixture();ready(f);f.after_set=function() f.combat_asc=obj() end
    eq(f.trainer:apply(f.state,HEALTH,true),false,"Reentrant combat ASC change prevents native lock")
    eq(#f.calls,0)
end

for _,field in ipairs({"DurationPolicy","StackingType"}) do
    local f=fixture();ready(f);f.default[field]=2
    eq(f.trainer:apply(f.state,HEALTH,true),false,"Changed effect configuration fails closed");eq(#f.calls,0);eq(f.sets,0)
end
for _,action in ipairs({HEALTH,STAMINA}) do
    local key=action==HEALTH and "HappyCheatsHealthLockHandle" or "HappyCheatsStaminaLockHandle"
    local f=fixture();ready(f);f.combat[key]=action==HEALTH and 228 or 774
    eq(f.trainer:apply(f.state,action,true),true,"A retained ID with zero active stacks is stale, not a game lock")
    eq(f.sets,1);eq(#f.calls,1);eq(f.trainer:enabled()[action],true)
    eq(f.trainer:apply(nil,action,false),true);eq(f.combat[key],-1)
    f=fixture();ready(f);f.combat[key]=900;f.count_error=true
    eq(f.trainer:apply(f.state,action,true),false,"Unreadable existing effect never permits replacement")
    eq(f.sets,0);eq(#f.calls,0)
    for _,active in ipairs({false,true}) do
        f=fixture();ready(f);f.combat[key]=900
        local setter=action==HEALTH and "SetHealthPercent" or "SetStaminaPercent"
        local original=f.combat[setter]
        f.combat[setter]=function(self,v) original(self,v);f.combat[key]=901;f.effects[901]=active and 1 or 0 end
        eq(f.trainer:apply(f.state,action,true),false,"A different callback-created handle is preserved, active or ended")
        eq(#f.calls,0);eq(f.combat[key],901)
    end
    f=fixture();ready(f);f.combat[key]=900
    local setter=action==HEALTH and "SetHealthPercent" or "SetStaminaPercent"
    local original=f.combat[setter]
    f.combat[setter]=function(self,v) original(self,v);f.effects[900]=1 end
    eq(f.trainer:apply(f.state,action,true),false,"Same numeric handle becoming active during refill is refused")
    eq(#f.calls,0);eq(f.effects[900],1)
    f=fixture();ready(f);f.combat[key]=900
    original=f.combat[setter]
    f.combat[setter]=function(self,v) original(self,v);f.combat[key]=-1 end
    eq(f.trainer:apply(f.state,action,true),true,"Native clearing of a stale handle is accepted")
end
print("Dawnwalker resource locks: "..checks.." assertions passed")
