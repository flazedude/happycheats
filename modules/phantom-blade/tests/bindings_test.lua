-- Execute actual bindings at mocked reflected-UObject boundaries. These fixtures
-- validate trainer decisions, not native loader compatibility or gameplay effects.
local checks, failures, next_address = 0, 0, 100
local function equal(actual, expected, label)
    checks = checks + 1
    assert(actual == expected, (label or "Mismatch") .. ": " .. tostring(actual) .. " ~= " .. tostring(expected))
end
local function object()
    next_address = next_address + 1
    local value = {valid=true,address=next_address}
    function value:IsValid() return self.valid end
    function value:GetAddress() return self.address end
    return value
end
local paths = {
    player="/Script/ProjectPBZ.PBZCharacter_Player",
    local_player="/Script/Engine.LocalPlayer",
    basic="/Script/CharacterBasics.BasicAttributeSet",
}
local function make_player(class, world)
    local f=class.fixture
    local pawn, controller, set, local_player = object(), object(), object(), object()
    pawn.base = {Health=50,Health_Max=100,Shachi=20,Shachi_Max=80,Attack=100}
    pawn.values = {}
    for key,value in pairs(pawn.base) do pawn.values[key] = value end
    pawn.Controller, pawn.world = controller, world
    pawn.AttributeSetBase=set; set.owner=pawn
    controller.Player, controller.Pawn = local_player, pawn
    local_player.PlayerController=controller
    function local_player:IsA(wanted) return wanted==f.local_class and self.correct_class~=false end
    function pawn:IsA(wanted) return wanted == class and self.correct_class ~= false end
    function pawn:GetWorld() return self.world end
    function set:IsA(wanted) return wanted==f.basic and self.correct_class~=false end
    function set:GetOuter() return self.owner end
    setmetatable(set,{__index=function(_,name)
        if pawn.base[name]==nil or f.missing_data[name] then return nil end
        f.data_reads=f.data_reads+1
        -- This models only the demonstrated remote-property LuaUScriptStruct
        -- boundary. It does not fake a native MakeAttribute return or FieldPath.
        local remote={}
        return setmetatable(remote,{
            __index=function(_,field)
                if field=="IsValid" then return function() return set.valid and not f.invalid_data[name] end end
                if field=="IsMappedToObject" then return function() return not f.unmapped_object[name] end end
                if field=="IsMappedToProperty" then return function() return not f.unmapped_property[name] end end
                if field=="CurrentValue" then
                    if f.read_errors[name] then error("fixture current read failed") end
                    return pawn.values[name]
                end
                if field=="BaseValue" then
                    if f.base_errors[name] then error("fixture base read failed") end
                    if f.base_missing then return nil end
                    return pawn.base[name]
                end
                error("Unexpected struct member: "..field)
            end,
            __newindex=function(_,field,value)
                equal(field,"CurrentValue","Only remote CurrentValue may be written")
                if f.setter_error==name then error("fixture setter rejected operation") end
                f.writes[#f.writes+1]={pawn=pawn,set=set,key=name,value=value,base=pawn.base[name]}
                if f.noop~=name then pawn.values[name]=value end
                if f.after_write then f.after_write(pawn,name,value) end
            end,
        })
    end})
    return pawn, controller
end
local function fixture()
    local f = {writes={},missing={},read_errors={},base_errors={},scans=0,finds={},now=1000,
        data_reads=0,missing_data={},invalid_data={},unmapped_object={},unmapped_property={}}
    f.class, f.local_class, f.basic, f.world = object(), object(), object(), object()
    f.class.fixture=f
    f.pawn, f.controller = make_player(f.class, f.world)
    f.controllers, f.attrs = {f.controller}, f.pawn.values
    f.objects = {[paths.player]=f.class,[paths.local_player]=f.local_class,[paths.basic]=f.basic}
    local env = setmetatable({
        os={time=function() return f.now end},
        StaticFindObject=function(path)
            assert(path==paths.player or path==paths.local_player or path==paths.basic,"No native MakeAttribute/helper fallback is supported")
            f.finds[path] = (f.finds[path] or 0) + 1
            if f.find_error then error(f.find_error) end
            if not f.missing[path] then return f.objects[path] end
        end,
        FindAllOf=function(class)
            f.scans = f.scans + 1
            assert(class=="PlayerController", "Unexpected broad object scan")
            if f.scan_error then error(type(f.scan_error)=="string" and f.scan_error or "fixture player discovery failed") end
            return f.controllers
        end,
    },{__index=_G})
    f.trainer = assert(loadfile("modules/phantom-blade/Scripts/bindings.lua","t",env))().new()
    return f
end
local function ready(f)
    local is_ready, message, values, available = f.trainer:refresh()
    equal(is_ready, true, "Ready local player: "..tostring(message))
    return values, available
end
local function test(name, body)
    local ok, reason = xpcall(body, debug.traceback)
    if ok then print("PASS Phantom Blade binding: "..name)
    else failures=failures+1; print("FAIL Phantom Blade binding: "..name.."\n"..reason) end
end

test("read-only startup and idle refresh never enable a cheat", function()
    local f=fixture()
    equal(#f.writes,0,"Constructing bindings is inert")
    local values,available=ready(f)
    equal(tonumber(values.pbz_health),50); equal(tonumber(values.pbz_stamina),20)
    for _,id in ipairs({"pbz_health","pbz_stamina","pbz_damage"}) do equal(available[id],true,id.." availability") end
    for _=1,5 do f.trainer:tick() end
    equal(#f.writes,0)
    local flags=f.trainer:enabled(); flags.pbz_health=true
    equal(f.trainer:enabled().pbz_health,false,"Returned flags cannot mutate trainer")
end)

for _,spec in ipairs({{"pbz_health","Health","Health_Max"},{"pbz_stamina","Shachi","Shachi_Max"}}) do
    test(spec[1].." follows current capacity and explicit off",function()
        local f=fixture(); ready(f)
        equal(f.trainer:apply(spec[1],true),true)
        equal(f.attrs[spec[2]],f.attrs[spec[3]])
        equal(f.pawn.base[spec[2]],spec[2]=="Health" and 50 or 20,"Resource BaseValue is untouched")
        equal(f.trainer:enabled()[spec[1]],true)
        f.attrs[spec[2]]=1; f.attrs[spec[3]]=120
        f.trainer:tick(); equal(f.attrs[spec[2]],120,"Refill follows capacity changes")
        local count=#f.writes; f.trainer:tick()
        equal(#f.writes,count,"No redundant full-resource writes")
        equal(f.trainer:apply(spec[1],false),true)
        f.attrs[spec[2]]=1; f.trainer:tick()
        equal(f.attrs[spec[2]],1); equal(#f.writes,count)
    end)
end

test("unowned, wrong-class, invalid and ambiguous pawns cannot receive writes",function()
    for _,change in ipairs({
        function(f) f.controller.Player=nil end,
        function(f) f.controller.Player.valid=false end,
        function(f) f.controller.Player.correct_class=false end,
        function(f) f.controller.Player.PlayerController=object() end,
        function(f) f.controller.Pawn=nil end,
        function(f) f.pawn.Controller=object() end,
        function(f) f.pawn.correct_class=false end,
        function(f) f.pawn.valid=false end,
        function(f) f.pawn.world=nil end,
        function(f) f.scan_error=true end,
        function(f) local _,other=make_player(f.class,f.world); f.controllers[2]=other end,
    }) do
        local f=fixture(); change(f)
        equal(f.trainer:refresh(),false)
        equal(f.trainer:apply("pbz_health",true),false)
        equal(f.trainer:apply("pbz_damage",false,2),false)
        equal(#f.writes,0)
    end
    local f=fixture(); local foreign,controller=make_player(f.class,f.world)
    controller.Player.correct_class=false; f.controllers={controller,f.controller}
    ready(f); equal(f.trainer:apply("pbz_health",true),true)
    equal(foreign.values.Health,50,"Foreign pawn unchanged"); equal(f.attrs.Health,100)
end)

test("cached ownership revalidates and irrelevant controllers do not reject a local player",function()
    local f=fixture(); ready(f); local scans=f.scans
    for _=1,20 do f.trainer:refresh(); f.trainer:tick(true) end
    equal(f.scans,scans,"Valid cached possession avoids repeated global scans")
    f.controller.Player.correct_class=false; equal(f.trainer:refresh(),false)
    equal(f.trainer:apply("pbz_health",true),false); equal(#f.writes,0)
    f=fixture(); f.controllers={}
    for i=1,32 do
        local _,controller=make_player(f.class,f.world)
        controller.Player.correct_class=false; f.controllers[i]=controller
    end
    f.controllers[33]=f.controller
    ready(f); equal(f.trainer:apply("pbz_health",true),true)
    equal(f.attrs.Health,100)
    for i=1,32 do equal(f.controllers[i].Pawn.values.Health,50,"Remote player is untouched") end
end)

test("reflected ownership works without redundant native getter calls",function()
    local f=fixture(); ready(f)
    local function forbidden() error("Redundant native getter must not be called") end
    f.controller.IsLocalController=forbidden; f.controller.K2_GetPawn=forbidden
    f.pawn.IsPlayerControlled=forbidden; f.pawn.GetController=forbidden
    ready(f); equal(f.trainer:apply("pbz_stamina",true),true)
    equal(f.attrs.Shachi,80)
end)

test("cached reflected possession is checked before every refill",function()
    for _,change in ipairs({
        function(f) f.controller.Player=nil end,
        function(f) f.controller.Player.PlayerController=object() end,
        function(f) f.controller.Pawn=object() end,
        function(f) f.pawn.Controller=object() end,
    }) do
        local f=fixture(); ready(f); equal(f.trainer:apply("pbz_stamina",true),true)
        f.attrs.Shachi=3; local count=#f.writes
        change(f); f.trainer:tick()
        equal(f.trainer.ready,false); equal(f.trainer:enabled().pbz_stamina,false)
        equal(f.attrs.Shachi,3); equal(#f.writes,count,"Changed ownership cannot receive a cached refill")
    end
end)

local function fail_property(object,name)
    object[name]=nil
    setmetatable(object,{__index=function(_,key)
        if key==name then error("fixture "..name.." property read failed") end
    end})
end

test("failed player detection names its stage and preserves it while throttled",function()
    for _,case in ipairs({
        {function(f) f.missing[paths.player]=true end,"Player class is unavailable"},
        {function(f) f.missing[paths.local_player]=true end,"Local-player class is unavailable"},
        {function(f) f.find_error="fixture class lookup failed" end,"Player class is unavailable"},
        {function(f) f.controllers=nil end,"Player controller is unavailable"},
        {function(f) f.controllers={} end,"Player controller is unavailable"},
        {function(f) f.scan_error=true end,"Player-controller discovery failed"},
        {function(f) fail_property(f.controller,"Pawn") end,"Controlled-pawn lookup failed"},
        {function(f) f.controller.Pawn=nil end,"Controlled player is unavailable"},
        {function(f) fail_property(f.controller,"Player") end,"Local-player lookup failed"},
        {function(f) f.controller.Player=nil end,"Local player is unavailable"},
        {function(f) f.controller.Player.correct_class=false end,"Controller does not belong to a local player"},
        {function(f) f.controller.Player.IsA=function() error("fixture local class check") end end,"Local-player class check failed"},
        {function(f) fail_property(f.controller.Player,"PlayerController") end,"Local-player controller lookup failed"},
        {function(f) f.controller.Player.PlayerController=object() end,"Local player does not own this controller"},
        {function(f) fail_property(f.pawn,"Controller") end,"Player-controller lookup failed"},
        {function(f) f.pawn.Controller=object() end,"Player ownership does not match the controller"},
        {function(f) f.pawn.IsA=function() error("fixture class check") end end,"Player-class check failed"},
        {function(f) f.pawn.correct_class=false end,"Controlled pawn is not the Phantom Blade Zero player class"},
        {function(f) f.pawn.GetWorld=function() error("fixture world lookup") end end,"Player-world lookup failed"},
        {function(f) f.pawn.world=nil end,"Player world is unavailable"},
        {function(f) local _,second=make_player(f.class,f.world); f.controllers[2]=second end,"More than one locally controlled"},
    }) do
        local f=fixture(); case[1](f)
        local available,message,values=f.trainer:refresh()
        equal(available,false)
        equal(message:find("Phantom Blade Zero player detection: ",1,true),1)
        equal(message:find(case[2],1,true)~=nil,true,message)
        equal(message:sub(-1),"."); equal(#message<=160,true,"Status remains a short sentence")
        equal(message:find("fixture",1,true),nil,"Runtime errors cannot become status text")
        local scans=f.scans
        local again,repeated,repeated_values=f.trainer:refresh()
        equal(again,false); equal(repeated,message,"Throttled discovery retains the exact failed stage")
        equal(repeated_values.pbz_discovery,values.pbz_discovery,"Throttling retains the detailed diagnostic")
        equal(f.scans,scans); equal(#f.writes,0)
    end
end)

test("cached ownership failure explains the pause and later detection recovers",function()
    local f=fixture(); ready(f)
    f.controller.Player.correct_class=false
    local available,message=f.trainer:refresh()
    equal(available,false); equal(message:find("Controller does not belong to a local player",1,true)~=nil,true)
    f.controller.Player.correct_class=true; f.now=f.now+1
    ready(f)
    equal(f.trainer.discovery_reason,nil,"Successful detection clears stale failure details")
    equal(f.trainer.discovery_detail,nil)
    equal(#f.writes,0)
end)

test("player-detection API errors are bounded printable diagnostics",function()
    local f=fixture(); f.scan_error="failed\n\t\0"..string.rep("x",1000)
    local _,message,values=f.trainer:refresh()
    equal(message,"Phantom Blade Zero player detection: Player-controller discovery failed.")
    equal(values.pbz_discovery:find("[%z\1-\31\127]"),nil,"Diagnostics cannot inject control characters")
    equal(#values.pbz_discovery<=160,true,"Runtime details are bounded separately from status")
    equal(values.pbz_discovery:sub(-3),"...")
    f=fixture(); f.pawn.GetWorld=function() error(setmetatable({},{__tostring=function() error("unprintable") end})) end
    local _,unprintable,details=f.trainer:refresh()
    equal(unprintable,"Phantom Blade Zero player detection: Player-world lookup failed.")
    equal(details.pbz_discovery,"unprintable error")
    equal(#f.writes,0)
end)

test("death cannot be reversed by a previously enabled refill",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_health",true),true)
    f.attrs.Health=0; local count=#f.writes
    f.trainer:tick(); equal(f.trainer:enabled().pbz_health,false)
    equal(f.attrs.Health,0); equal(#f.writes,count,"Dead pawn is never refilled")
    equal(f.trainer:apply("pbz_health",true),false)
    f.attrs.Health=20; f.trainer:tick()
    equal(f.attrs.Health,20); equal(f.trainer:enabled().pbz_health,false,"Recovery needs explicit activation")
end)

test("player transition rejects stale mutation and clears old toggles",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_stamina",true),true)
    local previous,old=f.trainer:session_key(),f.pawn
    old.Controller=nil; f.now=f.now+1
    f.pawn,f.controller=make_player(f.class,f.world); f.controllers={f.controller}; f.attrs=f.pawn.values
    local count=#f.writes
    equal(f.trainer:apply("pbz_stamina",true),false,"Old-session request cannot follow new pawn")
    equal(#f.writes,count); equal(f.trainer:enabled().pbz_stamina,false)
    equal(f.trainer:session_key()~=previous,true)
    equal(old.values.Shachi,80); equal(f.attrs.Shachi,20)
    equal(f.trainer:apply("pbz_stamina",true),true,"Fresh action can target replacement pawn")
end)

test("world transition rotates session even if pawn address is retained",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_health",true),true)
    local previous=f.trainer:session_key(); f.pawn.world=object()
    equal(f.trainer:apply("pbz_health",true),false)
    equal(f.trainer:session_key()~=previous,true); equal(f.trainer:enabled().pbz_health,false)
end)

test("missing essential binding fails closed before any mutation",function()
    for _,path in pairs(paths) do
        local f=fixture(); f.missing[path]=true
        f.trainer:refresh()
        equal(f.trainer:apply("pbz_health",true),false)
        equal(f.trainer:apply("pbz_damage",false,2),false)
        equal(#f.writes,0)
    end
end)

test("temporary attribute binding loss disables toggles instead of silently resuming",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_stamina",true),true)
    f.attrs.Shachi=1; f.basic.valid=false; f.missing[paths.basic]=true
    local count=#f.writes; f.trainer:tick()
    equal(f.trainer:enabled().pbz_stamina,false,"Lost attribute class clears ongoing toggle")
    equal(#f.writes,count)
    f.basic.valid=true; f.missing[paths.basic]=nil; f.trainer:tick()
    equal(f.attrs.Shachi,1,"Recovered attribute class cannot silently resume old toggle")
end)

test("invalid capacities and nonfinite readings never become resource writes",function()
    for _,value in ipairs({0/0,math.huge,-math.huge,-1,0,1000000001}) do
        local f=fixture(); f.attrs.Shachi_Max=value
        local _,available=ready(f); equal(available.pbz_stamina,false)
        equal(f.trainer:apply("pbz_stamina",true),false); equal(#f.writes,0)
    end
    for _,value in ipairs({0/0,math.huge,-math.huge,-1}) do
        local f=fixture(); f.attrs.Shachi=value
        local _,available=ready(f); equal(available.pbz_stamina,false)
        equal(f.trainer:apply("pbz_stamina",true),false); equal(#f.writes,0)
    end
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_stamina",true),true)
    f.read_errors.Shachi=true; local count=#f.writes; f.trainer:tick()
    equal(f.trainer:enabled().pbz_stamina,false); equal(#f.writes,count)
end)

test("silent or throwing setters never report a resource toggle enabled",function()
    for _,mode in ipairs({"noop","setter_error"}) do
        local f=fixture(); ready(f); f[mode]="Shachi"
        equal(f.trainer:apply("pbz_stamina",true),false)
        equal(f.trainer:enabled().pbz_stamina,false); equal(f.attrs.Shachi,20)
    end
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_stamina",true),true)
    f.attrs.Shachi=20; f.noop="Shachi"
    equal(f.trainer:apply("pbz_stamina",true),false)
    equal(f.trainer:enabled().pbz_stamina,false,"Failed repeated enable cannot retain old enabled state")
end)

test("damage scales attack from captured base without compounding and restores",function()
    local f=fixture(); ready(f)
    equal(f.trainer:apply("pbz_damage",false,3),true)
    equal(f.attrs.Attack,300); equal(f.writes[#f.writes].key,"Attack")
    equal(f.pawn.base.Attack,100,"Applying a multiplier leaves BaseValue untouched")
    equal(f.trainer:apply("pbz_damage",false,4),true)
    equal(f.attrs.Attack,400,"Change uses original base, not already scaled value")
    local count=#f.writes
    equal(f.trainer:apply("pbz_damage",false,4),true)
    f.trainer:tick(); equal(f.attrs.Attack,400,"Repeated multiplier does not compound")
    equal(#f.writes,count,"Repeated owned multiplier does not restore and rewrite")
    equal(f.trainer:apply("pbz_damage",false,1),true)
    equal(f.attrs.Attack,100); equal(f.pawn.base.Attack,100)
    equal(f.trainer:apply("pbz_damage",false,20),true); equal(f.attrs.Attack,2000)
    equal(f.trainer:disable_all(),true); equal(f.attrs.Attack,100)
end)

test("damage rejects invalid amounts and unknown actions at binding boundary",function()
    for _,value in ipairs({0,-1,1.5,21,0/0,math.huge,"2",{}}) do
        local f=fixture(); ready(f)
        equal(f.trainer:apply("pbz_damage",false,value),false,"Invalid multiplier rejected")
        equal(#f.writes,0)
    end
    local f=fixture(); ready(f)
    equal(f.trainer:apply("unknown",true),false); equal(#f.writes,0)
end)

test("rejected damage writes and excessive results cannot appear successful",function()
    for _,mode in ipairs({"noop","setter_error"}) do
        local f=fixture(); ready(f); f[mode]="Attack"
        equal(f.trainer:apply("pbz_damage",false,3),false)
        equal(f.pawn.base.Attack,100); equal(f.trainer:enabled().pbz_damage,false)
        equal(f.trainer:disable_all(),true)
    end
    local f=fixture(); f.pawn.base.Attack=10000000; f.attrs.Attack=10000000; ready(f)
    equal(f.trainer:apply("pbz_damage",false,20),false)
    equal(#f.writes,0,"Out-of-range resulting base is rejected before mutation")
end)

test("damage refuses ambiguous effective/base attributes",function()
    local f=fixture(); f.attrs.Attack=125
    local _,available=ready(f); equal(available.pbz_damage,false)
    equal(f.trainer:apply("pbz_damage",false,2),false); equal(#f.writes,0)
    f=fixture(); f.base_missing=true
    local _,_,_,availability=f.trainer:refresh()
    equal(availability.pbz_damage,false); equal(f.trainer:apply("pbz_damage",false,2),false)
    equal(#f.writes,0)
end)

test("foreign base changes survive trainer cleanup",function()
    for _,tick_first in ipairs({false,true}) do
        local f=fixture(); ready(f); equal(f.trainer:apply("pbz_damage",false,3),true)
        f.pawn.base.Attack=125; f.attrs.Attack=125
        local count=#f.writes
        if tick_first then f.trainer:tick() end
        equal(f.trainer:disable_all(),true)
        equal(f.attrs.Attack,125); equal(#f.writes,count,"Foreign mutation is preserved")
    end
end)

test("foreign current recalculation survives cleanup without overwriting game state",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_damage",false,3),true)
    f.attrs.Attack=325
    equal(f.trainer:disable_all(),true)
    equal(f.pawn.base.Attack,100); equal(f.attrs.Attack,325,"Game current recalculation is preserved")
end)

test("write-success readback failure retains restoration lease",function()
    for _,failed_read in ipairs({"base","effective"}) do
        local f=fixture(); ready(f)
        f.after_write=function(_,name)
            if name=="Attack" then
                if failed_read=="base" then f.base_errors.Attack=true else f.read_errors.Attack=true end
            end
        end
        equal(f.trainer:apply("pbz_damage",false,3),false,"Unconfirmed mutation is not reported successful")
        equal(f.writes[1].value,300,"Fixture actually accepted the requested write")
        equal(f.pawn.base.Attack,100,"BaseValue is never written")
        equal(f.attrs.Attack,300,"Unreadable ownership leaves current restoration pending")
        f.after_write=nil; f.base_errors.Attack=nil; f.read_errors.Attack=nil
        f.trainer:tick()
        equal(f.attrs.Attack,100,"Lease survives failed readback and restores later")
        equal(f.pawn.base.Attack,100,"Restoration leaves BaseValue untouched")
    end
end)

test("temporary restoration read failures remain pending",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_damage",false,3),true)
    f.base_errors.Attack=true
    equal(f.trainer:disable_all(),false,"Unreadable ownership cannot be claimed restored")
    equal(f.attrs.Attack,300); equal(f.pawn.base.Attack,100)
    local _,_,pending=f.trainer:refresh()
    equal(pending.pbz_damage,nil,"Pending restoration cannot report a confirmed 1x reset")
    f.base_errors.Attack=nil; f.trainer:tick()
    equal(f.pawn.base.Attack,100); equal(f.attrs.Attack,100,"Pending current restoration completed")
    local _,_,restored=f.trainer:refresh()
    equal(tonumber(restored.pbz_damage),1,"Completed restoration confirms normal multiplier")
end)

test("session cleanup targets original pawn and never replacement pawn",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_damage",false,3),true)
    local old=f.pawn
    old.Controller=nil; f.now=f.now+1
    f.pawn,f.controller=make_player(f.class,object()); f.controllers={f.controller}; f.attrs=f.pawn.values
    f.pawn.base.Attack=75; f.attrs.Attack=75
    local count=#f.writes
    equal(f.trainer:apply("pbz_damage",false,2),false,"Stale action cannot move multiplier to new player")
    equal(f.attrs.Attack,75,"Replacement player stays unchanged")
    equal(old.values.Attack,100,"Still-valid original pawn restored")
    for i=count+1,#f.writes do equal(f.writes[i].pawn,old,"Cleanup writes only leased original pawn") end
end)

test("cleanup remains available after gameplay readiness is lost",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_damage",false,3),true)
    equal(f.trainer:apply("pbz_health",true),true)
    f.controllers={}; f.controller.Player.correct_class=false; f.trainer:tick()
    equal(f.trainer:enabled().pbz_health,false); equal(f.attrs.Attack,100)
    equal(f.trainer:apply("pbz_health",false),true)
    equal(f.trainer:apply("pbz_damage",false,1),true)
    equal(f.trainer:disable_all(),true)
end)

test("unowned sets and detached struct values fail closed",function()
    for _,change in ipairs({
        function(f) f.pawn.AttributeSetBase=nil end,
        function(f) f.pawn.AttributeSetBase.valid=false end,
        function(f) f.pawn.AttributeSetBase.correct_class=false end,
        function(f) f.pawn.AttributeSetBase.owner=object() end,
        function(f) f.missing_data.Health=true end,
        function(f) f.invalid_data.Health=true end,
        function(f) f.unmapped_object.Health=true end,
        function(f) f.unmapped_property.Health=true end,
    }) do
        local f=fixture(); change(f)
        equal(f.trainer:refresh(),false)
        equal(f.trainer:apply("pbz_health",true),false)
        equal(f.trainer:apply("pbz_damage",false,2),false)
        equal(#f.writes,0)
    end
    local f=fixture(); f.unmapped_object.Attack=true
    local _,available=ready(f)
    equal(available.pbz_damage,false); equal(available.pbz_health,true)
    equal(f.trainer:apply("pbz_damage",false,2),false); equal(#f.writes,0)
end)

test("remote property values are reacquired after each change",function()
    local f=fixture(); ready(f)
    equal(f.trainer:apply("pbz_stamina",true),true)
    local reads=f.data_reads; f.attrs.Shachi=4
    f.trainer:tick(); equal(f.attrs.Shachi,80)
    equal(f.data_reads>reads,true,"Refill reacquires remote data instead of retaining raw struct pointers")
    local count=#f.writes; f.unmapped_property.Shachi=true; f.attrs.Shachi=5
    f.trainer:tick()
    equal(f.attrs.Shachi,5); equal(#f.writes,count)
    equal(f.trainer:enabled().pbz_stamina,false,"Detached property cannot retain a running refill")
end)

test("game attack recomputation ends the multiplier without reapplying it",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_damage",false,3),true)
    f.attrs.Attack=100; local count=#f.writes
    f.trainer:tick()
    equal(f.trainer:enabled().pbz_damage,false)
    local values=ready(f); equal(tonumber(values.pbz_damage),1)
    equal(#f.writes,count); equal(f.attrs.Attack,100); equal(f.pawn.base.Attack,100)
end)

test("restoration retries rejected writes and never uses a detached owner",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_damage",false,3),true)
    f.noop="Attack"
    equal(f.trainer:disable_all(),false); equal(f.attrs.Attack,300)
    f.noop=nil; local set=f.pawn.AttributeSetBase; set.owner=object()
    local count=#f.writes
    equal(f.trainer:disable_all(),false); equal(#f.writes,count)
    set.owner=f.pawn; equal(f.trainer:disable_all(),true)
    equal(f.attrs.Attack,100); equal(f.pawn.base.Attack,100)
end)

test("replacement attribute sets never inherit an old multiplier lease",function()
    local f=fixture(); ready(f); equal(f.trainer:apply("pbz_damage",false,3),true)
    local original=f.pawn.AttributeSetBase
    local replacement=make_player(f.class,f.world)
    replacement.values.Attack=300
    replacement.AttributeSetBase.owner=f.pawn
    f.pawn.AttributeSetBase=replacement.AttributeSetBase
    local count=#f.writes
    f.trainer:tick()
    equal(f.trainer:enabled().pbz_damage,false,"Identical values do not confer ownership of another set")
    equal(f.trainer:disable_all(),true)
    equal(#f.writes,count,"Old lease cannot restore through a replacement property")
    equal(replacement.values.Attack,300); equal(replacement.base.Attack,100)
    equal(original.owner,f.pawn)
end)

assert(failures==0,tostring(failures).." Phantom Blade binding fixture(s) failed")
print("Phantom Blade Zero bindings: "..checks.." assertions passed")
