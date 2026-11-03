-- Native contracts, hostile input/cancellation and cleanup are simulated here.
-- These checks do not assert that any attack was parried in the real game.
local checks=0
local function equal(x,y,label) checks=checks+1;assert(x==y,(label or "Mismatch")..": "..tostring(x).." ~= "..tostring(y)) end
local function object(name) return {name=name,valid=true} end
local function fixture()
    local f={now=100,hit=100.04,point=99.99,incoming=true,wanted=false,writes={},held={},mapping={"RemappedGuard","Gamepad_LeftShoulder"}}
    local a={}
    function a.valid(o) return type(o)=="table" and o.valid==true end
    function a.fname(text) return {ToString=function() return text end} end
    function a.read(o,k)
        if o==f.input and k=="EnhancedActionMappings" then
            if f.raw_mappings then return f.raw_mappings end
            if f.bad_mapping then return f.bad_mapping end
            local result={}
            for _,name in ipairs(f.mapping) do
                result[#result+1]={Action=a.resolve("/Game/_Dawnwalker/Player/Input/Actions/Combat/IA_Combat_Block.IA_Combat_Block"),Key={KeyName=a.fname(name)}}
            end
            return result
        end
        return a.valid(o) and o[k] or nil
    end
    function a.same(x,y) return a.valid(x) and x==y end
    function a.identity(o) return a.valid(o) and o.name or "" end
    function a.finite(n) return type(n)=="number" and n==n and math.abs(n)<math.huge end
    function a.has_function(path) return path~=f.missing end
    f.pawn,f.combat,f.controller,f.world=object("Player"),object("PlayerCombat"),object("Controller"),object("World")
    f.enemy,f.actor,f.enemy_combat=object("EnemyStub"),object("Enemy"),object("EnemyCombat")
    f.stub,f.board,f.config,f.input=object("PlayerStub"),object("Board"),object("Config"),object("InputSubsystem")
    f.controller.PlayerInput=f.input
    function f.input:GetOuter() return f.wrong_input_owner and f.pawn or f.controller end
    function f.input:IsA(path) assert(path=="/Script/EnhancedInput.EnhancedPlayerInput");return not f.wrong_input_type end
    f.pawn.CombatComponent=f.combat;f.actor.CombatComponent=f.enemy_combat
    f.config.ParryInputCooldown=0.5
    f.combat.BlockingDirection,f.combat.DefenseAngleDot=0,0.5
    f.pawn.position={X=0,Y=0,Z=0};f.actor.position={X=100,Y=0,Z=0}
    f.combat.forward={X=1,Y=0,Z=0}
    f.attack={Direction=1,bIsUnblockable=false,Target=f.combat,Instigator=f.enemy_combat,TimePerformed=99.8,
        PlayedAttackMontageInstanceID=3,PlayedAttackMontage=object("Montage"),Action=object("AttackAction")}
    f.enemy_combat.CurrentAttack=f.attack
    f.state={character=f.pawn,combat=f.combat,controller=f.controller,world=f.world,key="world/player"}
    function a.current_state() return not f.session_lost and f.state or nil end
    f.objects={}
    function a.resolve(path)
        if f.unresolved==path then return nil end
        if not f.objects[path] then f.objects[path]=object(path) end
        return f.objects[path]
    end
    function f.pawn:GetWorld() return f.world end
    function f.actor:GetWorld() return f.enemy_world or f.world end
    function f.pawn:K2_GetActorLocation() return self.position end
    function f.actor:K2_GetActorLocation() return self.position end
    function f.combat:GetCharacter() return f.replaced_owner or f.pawn end
    function f.enemy_combat:GetCharacter() return f.actor end
    function f.enemy_combat:IsAlive() return not f.enemy_dead end
    function f.combat:IsA(path) return path=="/Script/DogwoodCombat.PlayerCombatComponent" end
    function f.combat:GetConfig() return f.config end
    function f.combat:GetCombatForwardDirection() return self.forward end
    function f.combat:WantsToBlock() return f.wanted==true end
    function f.combat:IsBlocking() return f.blocking==true or f.wanted==true end
    function f.combat:CanDefend() return not f.cannot_defend end
    function f.combat:CanPerformBlockInDirection(direction) f.checked_direction=direction;return not f.direction_blocked end
    function f.combat:SetDesiredBlockState(value)
        f.writes[#f.writes+1]={"guard",value}
        if value==false and f.release_ignored then return end
        if value==true and f.press_ignored then return end
        f.wanted=value
        if value and f.lose_during_press then f.session_lost=true end
        if value and f.press_throws then error("native wrapper lost its result") end
    end
    function f.combat:SetBlockingDirection(value)
        f.writes[#f.writes+1]={"direction",value}
        if f.direction_ignored then return end
        self.BlockingDirection=value
        if f.direction_throws then error("native wrapper lost direction result") end
    end
    function f.controller:K2_GetPawn() return f.other_pawn or f.pawn end
    function f.controller:IsMoveInputIgnored() return f.ignored==true end
    function f.controller:IsInputKeyDown(key)
        if f.input_error then error("input unavailable") end
        assert(key.key==nil and key.KeyName,"Native FKey argument has a copied KeyName, no borrowed wrapper")
        return f.held[key.KeyName:ToString()]==true
    end
    function f.stub:GetActor() return f.pawn end
    function f.stub:GetAIBoard() return not f.no_board and f.board or nil end
    function f.stub:IsPlayer() return true end
    function f.stub:IsInitializedAndHasPawn() return not f.stub_unready end
    function f.stub:IsInCinematicMode() return f.cinematic==true end
    function f.enemy:GetActor() return f.actor end
    function f.enemy:IsPlayer() return f.enemy_player==true end
    function f.enemy:IsInitializedAndHasPawn() return not f.enemy_unready end
    function f.enemy:IsHostileTowardsPlayer() return not f.friendly end
    function a.invoke(o,method,...)
        if not a.valid(o) then return false,"Invalid object" end
        local args=table.pack(...)
        return pcall(function()
            if method=="GetAIStubFromActor" then assert(args[1]==f.pawn);return f.stub end
            if method=="GetTimeSeconds" then return f.now end
            if method=="IsGamePaused" then return f.paused==true end
            if method=="GetNextIncomingAttacker" then
                assert(args[1]==f.stub and args[2]~=args[3],"Separate native OUT tables required")
                if not f.missing_out then args[2].bIsHitIncoming=f.incoming;args[3].TimeToHitWindow=f.hit-f.now end
                return f.enemy
            end
            if method=="GetParryPointNotificationTime" then return f.point end
            return o[method](o,table.unpack(args,1,args.n))
        end)
    end
    function a.invoke_function(path,o,...)
        error("Interface exec thunks must not be called with UObject context")
    end
    f.api=a;f.trainer=dofile("modules/dawnwalker/Scripts/parry.lua").new(a)
    function f:enable() self.trainer:refresh(self.state);return self.trainer:apply(self.state,"dawnwalker_auto_parry",true) end
    function f:tick() equal(self.trainer:tick(self.state),true) end
    return f
end

do
    local f=fixture();equal(f.trainer:enabled().dawnwalker_auto_parry,false)
    f.trainer:refresh(f.state);equal(#f.writes,0,"Readiness is read-only")
    equal(f:enable(),true);equal(#f.writes,0,"Enable does not press guard")
    f:tick();equal(#f.writes,2);equal(f.writes[1][1],"guard");equal(f.writes[1][2],true)
    equal(f.writes[2][1],"direction");equal(f.writes[2][2],8,"Enemy left swing requires defender right")
    for _=1,100 do f:tick() end
    equal(#f.writes,2,"Repeated samples cannot mash or compound input")
    f.now=100.2;f:tick();equal(f.wanted,false);equal(f.combat.BlockingDirection,0);equal(#f.writes,4)
    f.now=100.3;f:tick();equal(#f.writes,4,"Same attack is never retried")
    equal(f.trainer.attempts,1)
    equal(f.trainer:disable_all(),true);equal(f.trainer:enabled().dawnwalker_auto_parry,false)
end
for direction,expected in pairs({[1]=8,[2]=4,[3]=1,[4]=2}) do
    local f=fixture();f.attack.Direction=direction;equal(f:enable(),true);f:tick()
    equal(f.checked_direction,expected);equal(f.combat.BlockingDirection,expected)
end
for _,bad in ipairs({"unblockable","wrong_target","wrong_instigator","friendly","other_world","behind","above","far",
    "enemy_dead","enemy_player","enemy_unready","invalid_direction","invalid_stamp","future_stamp","old_stamp","invalid_montage",
    "invalid_action","invalid_id","cannot_defend","direction_blocked","already_blocking","already_wanted","manual","no_incoming",
    "too_early","too_late","missing_point","point_future","point_wait","paused","ignored","cinematic"}) do
    local f=fixture()
    if bad=="unblockable" then f.attack.bIsUnblockable=true
    elseif bad=="wrong_target" then f.attack.Target=object("OtherCombat")
    elseif bad=="wrong_instigator" then f.attack.Instigator=object("OtherCombat")
    elseif bad=="other_world" then f.enemy_world=object("OtherWorld")
    elseif bad=="behind" then f.actor.position.X=-100
    elseif bad=="above" then f.actor.position.Z=301
    elseif bad=="far" then f.actor.position.X=1201
    elseif bad=="invalid_direction" then f.attack.Direction=0
    elseif bad=="invalid_stamp" then f.attack.TimePerformed=0/0
    elseif bad=="future_stamp" then f.attack.TimePerformed=101
    elseif bad=="old_stamp" then f.attack.TimePerformed=80
    elseif bad=="invalid_montage" then f.attack.PlayedAttackMontage=nil
    elseif bad=="invalid_action" then f.attack.Action=nil
    elseif bad=="invalid_id" then f.attack.PlayedAttackMontageInstanceID=-1
    elseif bad=="already_blocking" then f.blocking=true
    elseif bad=="already_wanted" then f.wanted=true
    elseif bad=="manual" then f.held.RemappedGuard=true
    elseif bad=="no_incoming" then f.incoming=false
    elseif bad=="too_early" then f.hit=101
    elseif bad=="too_late" then f.hit=99
    elseif bad=="missing_point" then f.point=3.4e38
    elseif bad=="point_future" then f.point=103
    elseif bad=="point_wait" then f.point=100.1;f.hit=100.2
    else f[bad]=true end
    equal(f:enable(),true,bad);f:tick();equal(#f.writes,0,"No mutation for "..bad)
end
do
    local f=fixture();f.point=100.025;f.hit=100.1;f:enable();f:tick();equal(#f.writes,0)
    f.now=100.032;f:tick();equal(f.wanted,true,"Guard starts when native animation timing permits")
    f.now=100.23;f:tick();equal(f.wanted,false)
    f.attack.TimePerformed=100.25;f.hit=100.33;f.point=100.3;f.now=100.3;f:tick()
    equal(f.wanted,false,"Native cooldown is respected")
    f.attack.TimePerformed=100.6;f.hit=100.68;f.point=100.63;f.now=100.64;f:tick();equal(f.wanted,true)
    equal(f.trainer.attempts,2)
end
for _,reason in ipairs({"off","disable_all","cancel","target_switch","unblockable","pause","ignored","cinematic","session","clock_backwards"}) do
    local f=fixture();f:enable();f:tick();equal(f.wanted,true)
    if reason=="off" then equal(f.trainer:apply(nil,"dawnwalker_auto_parry",false),true)
    elseif reason=="disable_all" then equal(f.trainer:disable_all(),true)
    else
        if reason=="cancel" then f.incoming=false
        elseif reason=="target_switch" then f.attack.Target=object("OtherTarget")
        elseif reason=="unblockable" then f.attack.bIsUnblockable=true
        elseif reason=="pause" then f.paused=true
        elseif reason=="session" then f.session_lost=true
        elseif reason=="clock_backwards" then f.now=99
        else f[reason]=true end
        f:tick()
    end
    equal(f.wanted,false,"Owned guard released on "..reason);equal(f.combat.BlockingDirection,0)
end
for _,kind in ipairs({"manual","direction_changed","game_released","component_changed","destroyed"}) do
    local f=fixture();f:enable();f:tick();local count=#f.writes
    if kind=="manual" then f.held.Gamepad_LeftShoulder=true
    elseif kind=="direction_changed" then f.combat.BlockingDirection=4
    elseif kind=="game_released" then f.wanted=false
    elseif kind=="component_changed" then f.pawn.CombatComponent=object("Replacement")
    else f.combat.valid=false end
    equal(f.trainer:disable_all(),true);equal(#f.writes,count,"Preserve later game/player state: "..kind)
    equal(f.trainer.lease,nil)
end
do
    local f=fixture();f:enable();f:tick();f.mapping={};f.held.RemappedGuard=true
    equal(f.trainer:disable_all(),true);equal(f.wanted,true,"Old verified keys preserve manual input when contexts disappear")
end
do
    local f=fixture();f:enable();f:tick();f.mapping={};equal(f.trainer:disable_all(),true);equal(f.wanted,false)
end
do
    local f=fixture();f:enable();f:tick();f.release_ignored=true
    equal(f.trainer:disable_all(),false);equal(f.trainer:enabled().dawnwalker_auto_parry,false)
    equal(f.trainer:apply(f.state,"dawnwalker_auto_parry",true),false,"Pending cleanup prevents overlap")
    f.release_ignored=false;local _,values=f.trainer:refresh(f.state)
    equal(f.wanted,false);equal(f.trainer.lease,nil);equal(values.dawnwalker_auto_parry_status,"Auto parry disabled")
end
for _,failure in ipairs({"press_ignored","press_throws","direction_ignored","direction_throws","lose_during_press"}) do
    local f=fixture();f[failure]=true;f:enable();f:tick()
    equal(f.trainer:enabled().dawnwalker_auto_parry,false,failure)
    equal(f.wanted,false,"Partial requests are cleaned: "..failure)
end
for _,kind in ipairs({"input_error","malformed","too_many","wrong_input_owner","wrong_input_type"}) do
    local f=fixture()
    if kind=="malformed" then f.bad_mapping={{Action=f.api.resolve("/Game/_Dawnwalker/Player/Input/Actions/Combat/IA_Combat_Block.IA_Combat_Block"),Key={}}}
    elseif kind=="too_many" then for i=1,33 do f.mapping[i]=tostring(i) end
    else f[kind]=true end
    equal(f:enable(),false,kind);equal(#f.writes,0)
end
do
    local f=fixture();f.mapping={}
    equal(f:enable(),true,"Out-of-combat context can arm without pretending a block mapping is broken")
    f:tick();equal(#f.writes,0,"No guard request without current block keys")
    f.mapping={"RemappedGuard"};f:tick();equal(f.wanted,true,"Effective combat mappings arm guard when they appear")
end
do
    local f=fixture();f.mapping={"None","RemappedGuard","RemappedGuard"}
    equal(f:enable(),true);f.held.RemappedGuard=true;f:tick();equal(#f.writes,0,"Unbound and duplicate rows cannot conceal held manual guard")
end
do
    local f=fixture();local a=f.api
    f.raw_mappings={{Action=object("IA_DrawToggleSword"),Key={KeyName=a.fname("Gamepad_LeftShoulder")}}}
    equal(f:enable(),true);f:tick();equal(#f.writes,0,"Exploration L1 mapping is not assumed to be the block binding")
    f.raw_mappings={{Action=a.resolve("/Game/_Dawnwalker/Player/Input/Actions/Combat/IA_Combat_Block.IA_Combat_Block"),Key={KeyName=a.fname("Gamepad_LeftShoulder")}}}
    f.held.Gamepad_LeftShoulder=true;f:tick();equal(#f.writes,0,"Active combat L1 mapping preserves manual guard")
    f.held.Gamepad_LeftShoulder=false;f:tick();equal(f.wanted,true,"Combat mapping transition enables automatic guard without rearming")
end
for _,kind in ipairs({"no_board","stub_unready","missing_out","bad_clock"}) do
    local f=fixture()
    if kind=="no_board" or kind=="stub_unready" then f[kind]=true;equal(f:enable(),false)
    else
        f:enable();if kind=="missing_out" then f.missing_out=true else f.now=0/0 end
        f:tick();equal(f.trainer:enabled().dawnwalker_auto_parry,false)
    end
    equal(#f.writes,0,"No mutation on unavailable data")
end
do
    local f=fixture();f.missing="/Script/DogwoodCombat.CombatComponentBase:SetDesiredBlockState"
    local availability=f.trainer:refresh(f.state);equal(availability.dawnwalker_auto_parry,false);equal(f:enable(),false)
end
do
    local f=fixture();f:enable()
    for i=1,80 do
        f.now=100+i;f.hit=f.now+0.04;f.point=f.now-0.01;f.attack.TimePerformed=f.now-0.2
        f:tick();f.now=f.now+0.2;f:tick()
    end
    equal(#f.trainer.order,64,"Attack history is bounded");equal(f.trainer.attempts,80)
    local count=0;for _ in pairs(f.trainer.seen) do count=count+1 end;equal(count,64)
end
print("Dawnwalker auto parry: "..checks.." assertions passed")
