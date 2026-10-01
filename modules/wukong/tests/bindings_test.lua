-- Execute the actual Lua bindings against reflected-UObject boundary fixtures.
-- No game is opened and no native engine, save, or installed mod file is used.
local checks, failures, next_address = 0, 0, 100
local function equal(actual, expected, label)
    checks=checks+1
    assert(actual==expected,(label or "Mismatch")..": "..tostring(actual).." ~= "..tostring(expected))
end
local function object()
    next_address=next_address+1
    local o={valid=true,address=next_address}
    function o:IsValid() return self.valid end
    function o:GetAddress() return self.address end
    return o
end
local function make_player(class)
    local pawn,controller=object(),object()
    pawn.values={[151]=50,[1]=100,[152]=20,[2]=70,[158]=30,[8]=100,[186]=1,[36]=10,[191]=0,[39]=3,[53]=0}
    pawn.player,pawn.dead,pawn.controller,pawn.base_attack=true,false,controller,100
    controller.local_player,controller.pawn=true,pawn
    function controller:IsLocalController() return self.local_player end
    function controller:K2_GetPawn() return self.pawn end
    function pawn:IsPlayerControlled() return self.player end
    function pawn:GetController() return self.controller end
    function pawn:IsA(wanted) return wanted==class and self.correct_class~=false end
    return pawn,controller
end
local function fixture()
    local f={writes={},grants={},read_errors={},missing={},scans=0}
    f.class,f.lib,f.gm,f.gm_function=object(),object(),object(),object()
    f.pawn,f.controller=make_player(f.class)
    f.controllers={f.controller}; f.attrs=f.pawn.values
    function f.lib:GetAttrValue(pawn,key)
        if f.read_errors[key] then error("fixture getter failed") end
        if key==153 then return pawn.base_attack*(1+pawn.values[53]/10000) end
        return pawn.values[key]
    end
    function f.lib:BGUSetAttrValue(pawn,key,value)
        -- Atk (153) is a computed attribute. The shipped trainer must modify
        -- AtkMul (53), otherwise the game's recomputation overwrites the value.
        assert(key~=153,"Computed attack must never be written")
        if f.setter_error==key then error("fixture setter failed") end
        f.writes[#f.writes+1]={pawn=pawn,key=key,value=value}
        if f.noop~=key then pawn.values[key]=value end
        if f.after_write then f.after_write(pawn,key,value) end
    end
    function f.lib:BGUIsUnitDead(pawn) return pawn.dead end
    function f.gm:RunScriptGM(command,controller)
        f.grants[#f.grants+1]={command=command,controller=controller}
        if f.gm_error then error("fixture GM failed after dispatch") end
    end
    local objects={
        ["/Script/b1-Managed.Default__BGUFunctionLibraryCS"]=f.lib,
        ["/Script/b1-Managed.Default__BGUFunctionLibraryManaged"]=f.gm,
        ["/Script/b1-Managed.BGUPlayerCharacterCS"]=f.class,
        ["/Script/b1-Managed.BGUFunctionLibraryManaged:RunScriptGM"]=f.gm_function,
    }
    local env=setmetatable({
        StaticFindObject=function(path) if not f.missing[path] then return objects[path] end end,
        FindAllOf=function(class)
            f.scans=f.scans+1
            assert(class=="PlayerController","Unexpected broad object scan")
            if f.scan_error then error("fixture scan failed") end
            return f.controllers
        end,
    },{__index=_G})
    f.trainer=assert(loadfile("modules/wukong/Scripts/bindings.lua","t",env))().new()
    return f
end
local function ready(f)
    local is_ready,_,values,available=f.trainer:refresh()
    equal(is_ready,true,"Ready local player")
    return values,available
end
local function test(name,body)
    local ok,error=xpcall(body,debug.traceback)
    if ok then print("PASS Wukong binding: "..name)
    else failures=failures+1; print("FAIL Wukong binding: "..name.."\n"..error) end
end

test("idle inspection reports individual resource availability without writes",function()
    local f=fixture(); local values,available=ready(f)
    equal(values.wukong_health,50); equal(values.wukong_health_max,100)
    for _,action in ipairs({"health","mana","stamina","damage","gourd","focus","xp","sparks","will"}) do
        equal(available["wukong_"..action],true,action.." availability")
    end
    for _=1,5 do f.trainer:tick() end
    equal(#f.writes,0); equal(#f.grants,0)
    local copy=f.trainer:enabled(); copy.wukong_health=true
    equal(f.trainer:enabled().wukong_health,false,"Returned flags cannot mutate trainer")
end)

for _,spec in ipairs({{"health",151,1},{"mana",152,2},{"stamina",158,8},{"gourd",186,36}}) do
    test(spec[1].." refill follows current capacity and stops on explicit off",function()
        local f=fixture(); ready(f)
        local action="wukong_"..spec[1]
        equal(f.trainer:apply(action,true),true)
        equal(f.attrs[spec[2]],f.attrs[spec[3]])
        equal(f.trainer:enabled()[action],true)
        f.attrs[spec[2]]=1; f.attrs[spec[3]]=123
        f.trainer:tick(); equal(f.attrs[spec[2]],123,"Refresh uses changed capacity")
        local count=#f.writes
        f.trainer:tick(); equal(#f.writes,count,"Full resource does not cause unnecessary writes")
        equal(f.trainer:apply(action,false),true)
        f.attrs[spec[2]]=1; f.trainer:tick()
        equal(f.attrs[spec[2]],1); equal(#f.writes,count)
    end)
end

test("focus is a one-shot and does not become a refill toggle",function()
    local f=fixture(); ready(f)
    equal(f.trainer:apply("wukong_focus",nil),true)
    equal(f.attrs[191],3); equal(f.trainer:enabled().wukong_focus,nil)
    f.attrs[191]=0; local count=#f.writes; f.trainer:tick()
    equal(f.attrs[191],0); equal(#f.writes,count)
end)

test("same-thread tick reuses validation while standalone ticks still revalidate",function()
    local f=fixture(); ready(f)
    equal(f.trainer:apply("wukong_mana",true),true)
    f.attrs[152]=1
    ready(f); local scans=f.scans
    f.trainer:tick(true)
    equal(f.scans,scans,"Already-validated tick avoids another global scan")
    equal(f.attrs[152],70,"Reuse still performs guarded attribute readback")
    f.attrs[152]=1; f.pawn.dead=true
    f.trainer:tick()
    equal(f.scans,scans+1,"Standalone tick discovers current native state")
    equal(f.trainer:enabled().wukong_mana,false)
    equal(f.attrs[152],1,"Direct tick cannot apply an old live-player snapshot")
end)

test("foreign, unpossessed, wrong-class and ambiguous players cannot receive writes",function()
    for _,change in ipairs({
        function(f) f.controller.local_player=false end,
        function(f) f.controller.pawn=nil end,
        function(f) f.pawn.player=false end,
        function(f) f.pawn.controller=object() end,
        function(f) f.pawn.correct_class=false end,
        function(f) f.pawn.valid=false end,
        function(f) f.scan_error=true end,
        function(f) local _,other=make_player(f.class); f.controllers[2]=other end,
    }) do
        local f=fixture(); change(f)
        equal(f.trainer:refresh(),false)
        equal(f.trainer:apply("wukong_health",true),false)
        equal(f.trainer:apply("wukong_xp",nil,1000),false)
        equal(#f.writes,0); equal(#f.grants,0)
    end
    local f=fixture(); local foreign,controller=make_player(f.class)
    controller.local_player=false; f.controllers={controller,f.controller}
    ready(f); equal(f.trainer:apply("wukong_health",true),true)
    equal(foreign.values[151],50,"Remote player unchanged")
    equal(f.attrs[151],100,"Local player refilled")
end)

test("death disables refills without resurrecting and requires fresh activation",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("wukong_health",true),true)
    f.attrs[151]=0; f.pawn.dead=true
    local count=#f.writes; f.trainer:tick()
    equal(f.trainer:enabled().wukong_health,false); equal(#f.writes,count)
    equal(f.trainer:apply("wukong_health",true),false)
    f.attrs[151]=20; f.pawn.dead=false; f.trainer:tick()
    equal(f.attrs[151],20); equal(f.trainer:enabled().wukong_health,false)
end)

test("player transition cleans old flags and rejects an in-flight action",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("wukong_mana",true),true)
    local previous=f.trainer:session_key(); local old=f.pawn
    f.pawn,f.controller=make_player(f.class); f.controllers={f.controller}; f.attrs=f.pawn.values
    local count=#f.writes
    equal(f.trainer:apply("wukong_mana",true),false,"Previous-session action cannot follow a new player")
    equal(#f.writes,count); equal(f.trainer:enabled().wukong_mana,false)
    equal(f.trainer:session_key()~=previous,true)
    equal(old.values[152],70); equal(f.attrs[152],20)
    equal(f.trainer:apply("wukong_mana",true),true,"Fresh action may use new player")
end)

test("missing core binding clears toggles instead of silently resuming",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("wukong_mana",true),true)
    f.attrs[152]=1; f.missing["/Script/b1-Managed.Default__BGUFunctionLibraryCS"]=true
    f.trainer:tick(); equal(f.trainer:enabled().wukong_mana,false)
    f.missing={}; f.trainer:tick(); equal(f.attrs[152],1)
end)

test("NaN infinity invalid capacities and getter failures cannot become writes",function()
    for _,value in ipairs({0/0,math.huge,-math.huge,-1,0,10000001}) do
        local f=fixture(); f.attrs[2]=value
        local _,available=ready(f); equal(available.wukong_mana,false)
        equal(f.trainer:apply("wukong_mana",true),false); equal(#f.writes,0)
    end
    for _,value in ipairs({0/0,math.huge,-math.huge,-1}) do
        local f=fixture(); f.attrs[152]=value
        local _,available=ready(f); equal(available.wukong_mana,false)
        equal(f.trainer:apply("wukong_mana",true),false); equal(#f.writes,0)
    end
    local f=fixture(); ready(f); equal(f.trainer:apply("wukong_stamina",true),true)
    f.read_errors[158]=true; local count=#f.writes; f.trainer:tick()
    equal(f.trainer:enabled().wukong_stamina,false); equal(#f.writes,count)
end)

test("a silent or throwing resource setter cannot report enabled",function()
    for _,mode in ipairs({"noop","setter_error"}) do
        local f=fixture(); ready(f); f[mode]=152
        equal(f.trainer:apply("wukong_mana",true),false)
        equal(f.trainer:enabled().wukong_mana,false); equal(f.attrs[152],20)
    end
end)

test("damage uses basis-point multiplier and restores negative modifiers without compounding",function()
    for _,original in ipairs({0,2500,-2500,-9999,-9999.5}) do
        local f=fixture(); f.attrs[53]=original; ready(f)
        local expected=(original+10000)*10-10000
        equal(f.trainer:apply("wukong_damage",true),true)
        equal(f.attrs[53],expected); equal(f.writes[1].key,53)
        local count=#f.writes
        for _=1,5 do f.trainer:tick(); equal(f.trainer:apply("wukong_damage",true),true) end
        equal(f.attrs[53],expected); equal(#f.writes,count,"Repeated enable and ticks do not multiply again")
        equal(f.trainer:apply("wukong_damage",false),true)
        equal(f.attrs[53],original); equal(f.trainer:enabled().wukong_damage,false)
    end
end)

test("game mutations replace the trainer multiplier and survive off",function()
    for _,tick_first in ipairs({false,true}) do
        local f=fixture(); f.attrs[53]=-500; ready(f)
        equal(f.trainer:apply("wukong_damage",true),true)
        f.attrs[53]=1750; local count=#f.writes
        if tick_first then f.trainer:tick(); equal(f.trainer:enabled().wukong_damage,false) end
        equal(f.trainer:apply("wukong_damage",false),true)
        equal(f.attrs[53],1750); equal(#f.writes,count,"Game mutation is not overwritten")
    end
end)

test("damage cleanup retries uncertain reads and restores after temporary failure",function()
    local f=fixture(); f.attrs[53]=-1000; ready(f)
    equal(f.trainer:apply("wukong_damage",true),true)
    f.read_errors[53]=true; f.trainer:tick()
    equal(f.trainer:enabled().wukong_damage,false)
    equal(f.trainer:disable_all(),false,"Unreadable ownership cannot be claimed restored")
    f.read_errors[53]=nil; f.trainer:tick()
    equal(f.attrs[53],-1000,"Pending receipt survives the read failure")
end)

test("failed damage readback retains cleanup until the original value is recoverable",function()
    local f=fixture(); f.attrs[53]=-2000; ready(f)
    f.after_write=function(_,key) if key==53 then f.read_errors[53]=true end end
    equal(f.trainer:apply("wukong_damage",true),false)
    equal(f.trainer:enabled().wukong_damage,false)
    f.after_write=nil; f.read_errors[53]=nil; f.trainer:tick()
    equal(f.attrs[53],-2000)
end)

test("damage is cleaned on player loss and off remains available without readiness",function()
    local f=fixture(); f.attrs[53]=-2500; ready(f)
    equal(f.trainer:apply("wukong_damage",true),true)
    equal(f.trainer:apply("wukong_health",true),true)
    f.controllers={}; f.trainer:tick()
    equal(f.attrs[53],-2500); equal(f.trainer:enabled().wukong_damage,false)
    equal(f.trainer:enabled().wukong_health,false)
    equal(f.trainer:apply("wukong_health",false),true)
    equal(f.trainer:disable_all(),true)
end)

test("progression dispatches exact one-shot verbs and bounded integer amounts",function()
    local f=fixture(); ready(f)
    for _,spec in ipairs({{"wukong_xp","AddExp 1000"},{"wukong_sparks","AddTalentPoint 1"},{"wukong_will","AddItem 1002 1000"}}) do
        equal(f.trainer:apply(spec[1],nil),true)
        equal(f.grants[#f.grants].command,spec[2]); equal(f.grants[#f.grants].controller,f.controller)
    end
    equal(f.trainer:apply("wukong_will",nil,123),true)
    equal(f.grants[#f.grants].command,"AddItem 1002 123")
    local count=#f.grants
    for _,amount in ipairs({0,-1,1.5,0/0,math.huge,1000001}) do
        equal(f.trainer:apply("wukong_xp",nil,amount),false)
    end
    equal(f.trainer:apply("wukong_sparks",nil,101),false)
    equal(#f.grants,count)
    f.gm_error=true
    equal(f.trainer:apply("wukong_xp",nil,5),false)
    equal(#f.grants,count+1,"An uncertain native call is never retried")
    for _=1,5 do f.trainer:tick() end
    equal(#f.grants,count+1)
end)

test("discovery diagnostics identify failed guards without permitting writes",function()
    local cases={
        {function(f) f.scan_error=true end,"FindAllOf(PlayerController)"},
        {function(f) f.controllers=nil end,"no PlayerController list"},
        {function(f) for i=2,17 do f.controllers[i]=f.controller end end,"17 controllers exceeds"},
        {function(f) f.controller.local_player=false end,"local=0"},
        {function(f) f.controller.K2_GetPawn=nil end,"K2_GetPawn call failed"},
        {function(f) f.controller.pawn=nil end,"Local controller pawn unavailable"},
        {function(f) f.controller.IsLocalController=nil end,"IsLocalController call failed"},
        {function(f) f.pawn.IsPlayerControlled=nil end,"IsPlayerControlled call failed"},
        {function(f) f.pawn.player=false end,"Pawn ownership rejected"},
        {function(f) f.pawn.GetController=nil end,"GetController call failed"},
        {function(f) f.pawn.controller=object() end,"Reciprocal owner rejected"},
        {function(f) f.controller.GetAddress=nil end,"Owner GetAddress call failed"},
        {function(f) f.missing["/Script/b1-Managed.BGUPlayerCharacterCS"]=true end,"Player class unavailable"},
        {function(f) f.pawn.IsA=nil end,"Player IsA call failed"},
        {function(f) f.pawn.correct_class=false end,"Player class rejected"},
        {function(f) local _,other=make_player(f.class); f.controllers[2]=other end,"multiple matching local players"},
    }
    for _,case in ipairs(cases) do
        local f=fixture(); case[1](f)
        local is_ready,message,values=f.trainer:refresh()
        equal(is_ready,false)
        equal(message:find(case[2],1,true)~=nil,true,"Diagnostic identifies "..case[2]..": "..message)
        equal(type(values.wukong_discovery),"string","Discovery explanation survives in status values")
        equal(#f.writes,0); equal(#f.grants,0)
    end
    local f=fixture(); local values=ready(f)
    equal(values.wukong_discovery,"controllers=1 local=1 pawns=1 controlled=1 owners=1 classes=1 matches=1")
end)

test("library and player-state diagnostics distinguish missing calls from game state",function()
    local cases={
        {function(f) f.missing["/Script/b1-Managed.Default__BGUFunctionLibraryCS"]=true end,"attribute library unavailable"},
        {function(f) f.lib.BGUIsUnitDead=nil end,"BGUIsUnitDead call failed"},
        {function(f) f.pawn.dead=true end,"reports that the player is dead"},
        {function(f) f.lib.GetAttrValue=nil end,"GetAttrValue(Hp=151) call failed"},
        {function(f) f.read_errors[151]=true end,"GetAttrValue(Hp=151) call failed"},
        {function(f) f.attrs[151]=0/0 end,"returned invalid health"},
        {function(f) f.attrs[151]=0 end,"returned non-positive health"},
    }
    for _,case in ipairs(cases) do
        local f=fixture(); case[1](f)
        local is_ready,message=f.trainer:refresh()
        equal(is_ready,false); equal(message:find(case[2],1,true)~=nil,true,"Diagnostic identifies "..case[2])
        equal(#f.writes,0); equal(#f.grants,0)
    end
    local f=fixture()
    function f.controller:IsLocalController() error("line1\nline2\0"..string.rep("x",5000)) end
    local _,message=f.trainer:refresh()
    equal(#message<=700,true,"Native errors are bounded")
    equal(message:find("[%z\1-\31\127]")==nil,true,"Native errors cannot inject status control characters")
end)

test("missing progression lookup stays visible while base controls remain available",function()
    for _,spec in ipairs({
        {"/Script/b1-Managed.Default__BGUFunctionLibraryManaged","Default__BGUFunctionLibraryManaged unavailable"},
        {"/Script/b1-Managed.BGUFunctionLibraryManaged:RunScriptGM","RunScriptGM function unavailable"},
    }) do
        local f=fixture(); f.missing[spec[1]]=true
        local is_ready,message,values,available=f.trainer:refresh()
        equal(is_ready,true); equal(available.wukong_health,true)
        equal(available.wukong_xp,false); equal(available.wukong_sparks,false); equal(available.wukong_will,false)
        equal(message:find(spec[2],1,true)~=nil,true)
        equal(values.wukong_progression_diagnostic:find(spec[2],1,true)~=nil,true)
        equal(#f.writes,0); equal(#f.grants,0)
    end
end)

assert(failures==0,tostring(failures).." Wukong binding cases failed")
print("Wukong native binding boundary: "..checks.." assertions passed")
