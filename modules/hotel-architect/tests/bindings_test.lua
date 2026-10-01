-- Offline reflected-UObject fixtures. No game process/save or installed file.
local checks=0
local function eq(a,b,label) checks=checks+1; assert(a==b,(label or "Mismatch")..": "..tostring(a).." ~= "..tostring(b)) end
local function near(a,b,label) checks=checks+1; assert(type(a)=="number" and math.abs(a-b)<0.00001,label or "Numeric mismatch") end
local P=dofile("modules/hotel-architect/Scripts/protocol.lua")
local PREFIX="/Script/HotelArchitect."
local function fixture(options)
    options=options or {}
    local f={writes={},calls={},registered={},missing={},scans=0,objects={},next_id=100}
    local function object(class,fields,w)
        f.next_id=f.next_id+1
        local o={_class=class,_fields=fields or {},_world=w,_id=f.next_id,_valid=true}
        function o:IsValid() return self._valid end
        function o:GetAddress() return self._id end
        function o:GetWorld() return self._world end
        function o:IsA(path)
            return path==PREFIX..self._class or self._class=="HaStaffCharacter" and path==PREFIX.."HaBaseCharacter"
        end
        setmetatable(o,{
            __index=function(self,key)
                if f.read_error==key then error("fixture read failed") end
                local result=self._fields[key]
                if result==nil then error("Unknown reflected property: "..key) end
                return result
            end,
            __newindex=function(self,key,value)
                if key:sub(1,1)=="_" then rawset(self,key,value); return end
                if self._fields[key]==nil then error("Unknown reflected write: "..key) end
                if f.write_error==key then error("fixture write failed") end
                f.writes[#f.writes+1]={object=self,key=key,value=value}
                if f.noop~=key then self._fields[key]=value end
                if f.after_write then f.after_write(self,key,value) end
            end,
        })
        f.objects[#f.objects+1]=o
        return o
    end
    f.world=object("World"); f.world._world=f.world
    f.state=object("HaGameState",{FinancialData={DaysPassed=4},Money=100,Stars=2,DayNumber=3,Hour=6,Minute=30,
        GameHoursTime=6.5,HC_TimeOfDaySeconds=23400,HC_PreviousHours=6.5},f.world)
    rawset(f.state,"ChangeMoneyAndAdjustNetWorth",function(self,delta)
        f.calls[#f.calls+1]={"money",delta}; self.Money=self.Money+delta
        if f.money_error then error("Uncertain money result") end
    end)
    f.mode=object("HaStandardGameMode",{HaGameState=f.state,GameSpeed=1},f.world)
    rawset(f.mode,"SetGameSpeed",function(self,value) f.calls[#f.calls+1]={"speed",value}; self.GameSpeed=value end)
    f.person=object("HaStaffCharacter",{RestroomNeed=.7,Mood=.3,Health=.5,Energy=.2,HC_CurrentXP=20,HC_Level=2,DaysUntilRetirement=50},f.world)
    local function item(w)
        local result=object("HaGridItem",{RoomValueMultiplier=1,Cleanliness=.3,AttractivenessValue=20,NegativeAttractivenessValue=3,Condition=.4},w or f.world)
        rawset(result,"ChangeCleanliness",function(self,delta) f.calls[#f.calls+1]={"cleanliness",delta}; self.Cleanliness=math.max(0,math.min(1,self.Cleanliness+delta)) end)
        rawset(result,"OnConditionChanged",function(self) f.calls[#f.calls+1]={"condition_event",self} end)
        return result
    end
    f.item=item()
    local function weak(o) return {Get=function() return o end} end
    f.controller=object("HaPlayerController",{SelectedCharacter=weak(f.person),SelectedGridItem=weak(f.item)},f.world)
    rawset(f.controller,"IsLocalController",function() return not f.foreign_controller end)
    f.cf=object("HaCharacterFunctions")
    rawset(f.cf,"ChangeRestroomNeed",function(_,person,delta) f.calls[#f.calls+1]={"restroom",delta}; person.RestroomNeed=math.max(0,math.min(1,person.RestroomNeed+delta)) end)
    rawset(f.cf,"ChangeCharacterHealth",function(_,person,delta) f.calls[#f.calls+1]={"health",delta}; if person.Health>0 then person.Health=math.max(0,math.min(1,person.Health+delta)) end end)
    f.sf=object("HaSandboxFunctionLibrary")
    rawset(f.sf,"ChangeNumberOfStars",function(_,controller,delta) eq(controller,f.controller); f.calls[#f.calls+1]={"stars",delta}; f.state.Stars=math.max(0,math.min(5,f.state.Stars+delta)) end)
    local function meta() return {IsValid=function() return true end} end
    local functions={
        ["HaGameState:ChangeMoneyAndAdjustNetWorth"]=true,["HaStandardGameMode:SetGameSpeed"]=true,
        ["HaSandboxFunctionLibrary:ChangeNumberOfStars"]=true,["HaCharacterFunctions:ChangeRestroomNeed"]=true,
        ["HaCharacterFunctions:ChangeCharacterHealth"]=true,
        ["HaGridItem:ChangeCleanliness"]=true,["HaGridItem:OnConditionChanged"]=true,
    }
    local function hex(value) return value:gsub("..",function(pair) return string.char(tonumber(pair,16)) end) end
    local dos="MZ"..string.rep("\0",58)..string.pack("<I4",128)
    local header="PE\0\0"..string.pack("<I2I2I4",0x8664,6,2252431335)..string.rep("\0",12)..string.pack("<I2",0x20b)..string.rep("\0",54)..string.pack("<I4",168026112)
    local data={
        [0]=dos,[128]=header,
        [0x48bb240]=hex("4885c9741b8b41080fbae01e72128b816c07000089028b8168070000418900c333c08902418900c3"),
        [0x4a139e0]=hex("0f28c1f30f11899c040000f30f5905a5ca6e03f30f1189a0040000f30f118194040000f30f118198040000c3"),
    }
    local fake_io={open=function(path,mode)
        eq(mode,"rb"); eq(path:find("HotelArchitect%-Win64%-Shipping.exe$")~=nil,true)
        if options.absent then return nil end
        local file={at=0}
        function file:seek(whence,offset) if whence=="end" then return options.bad_size and 123 or 164258816 end; self.at=offset; return offset end
        function file:read(count) local result=data[self.at]; if options.bad_sentinel and self.at==0x4a139e0 then result=string.rep("\0",count) end; return result and result:sub(1,count) end
        function file:close() return true end
        return file
    end}
    local env=setmetatable({io=fake_io,PropertyTypes={IntProperty="Int",FloatProperty="Float"},
        RegisterCustomProperty=function(info) f.registered[#f.registered+1]=info; if options.registration_error then error("Registration refused") end end,
        StaticFindObject=function(path)
            if f.missing[path] then return nil end
            if path==PREFIX.."Default__HaCharacterFunctions" then return f.cf end
            if path==PREFIX.."Default__HaSandboxFunctionLibrary" then return f.sf end
            if functions[path:gsub("^/Script/HotelArchitect%.","")] then return meta() end
        end,
        FindAllOf=function(class)
            f.scans=f.scans+1
            if f.scan_error then error("fixture scan failed") end
            local result={}
            for _,o in ipairs(f.objects) do if o:IsA(PREFIX..class) then result[#result+1]=o end end
            return result
        end,
    },{__index=_G})
    f.trainer=assert(loadfile("modules/hotel-architect/Scripts/bindings.lua","t",env))().new()
    f.object,f.make_item,f.weak=object,item,weak
    return f
end
local function ready(f) local ok,_,values,available=f.trainer:refresh(); eq(ok,true,"Hotel ready"); return values,available end
local function test(name,body) body(); print("PASS Hotel Architect: "..name) end

test("exact executable fingerprint gates custom registration and all discovery",function()
    for _,options in ipairs({{absent=true},{bad_size=true},{bad_sentinel=true}}) do
        local f=fixture(options); eq(f.trainer:refresh(),false); eq(#f.registered,0); eq(f.scans,0)
        eq(f.trainer:apply("ha_money",false,1000),false); eq(#f.writes,0)
    end
    local f=fixture(); local values,available=ready(f)
    eq(#f.registered,4); eq(f.registered[1].OffsetInternal,0x768); eq(f.registered[2].OffsetInternal,0x76c)
    eq(f.registered[3].OffsetInternal,0x498); eq(f.registered[4].OffsetInternal,0x4a0)
    for action in pairs(P.numbers) do eq(available[action],true,action.." available") end
    eq(available.ha_no_needs,true); eq(available.ha_always_noon,true); eq(available.ha_furniture_condition,true)
    eq(available.ha_disable_aging,false,"Native aging is owned by the client")
    eq(values.diagnostic_ha_world_id,tostring(f.world._id)..":"..f.state._id)
    eq(values.ha_restroom_need,70); eq(values.ha_total_hours,78.5)
    for _=1,5 do f.trainer:tick() end
    eq(#f.writes,0,"Default-off polling is read-only"); eq(#f.calls,0); eq(#f.registered,4)
end)

test("all numeric editors use precise targets, percentages, setters and bounded values",function()
    local f=fixture(); ready(f)
    local edits={ha_game_speed=3,ha_days_passed=12,ha_money=2345,ha_stars=5,ha_day_number=5,ha_hour=10,ha_minute=45,
        ha_total_hours=120.5,ha_restroom_need=20,ha_mood=70,ha_health=80,ha_xp=100,ha_level=8,ha_retirement_days=777,
        ha_energy=90,ha_room_value_multiplier=5.5,ha_cleanliness=80,ha_attractiveness=500,ha_negative_attractiveness=0,ha_condition=90}
    for action,amount in pairs(edits) do
        eq(f.trainer:apply(action,false,amount),true,action.." applied")
        local values=ready(f); near(values[action],amount,action.." readback")
    end
    eq(f.person.RestroomNeed,.2); eq(f.person.HC_Level,8); eq(f.person.DaysUntilRetirement,777)
    eq(f.item.AttractivenessValue,500); eq(f.state.FinancialData.DaysPassed,12)
    local count=#f.writes
    for action,spec in pairs(P.numbers) do
        for _,bad in ipairs({spec[1]-1,spec[2]+1,0/0,math.huge}) do eq(f.trainer:apply(action,false,bad),false) end
        eq(f.trainer:apply(action,true,spec[1]),false)
    end
    eq(#f.writes,count,"Invalid requests cannot write")
end)

test("clock edits update native seconds without replaying hourly events",function()
    local f=fixture(); ready(f)
    eq(f.trainer:apply("ha_total_hours",false,246.75),true)
    eq(f.state.DayNumber,10); eq(f.state.Hour,6); eq(f.state.Minute,45)
    eq(f.state.HC_TimeOfDaySeconds,24300); eq(f.state.GameHoursTime,6.75); eq(f.state.HC_PreviousHours,6.75)
    for _,write in ipairs(f.writes) do eq(write.key=="HC_HourlyAccumulator",false) end
    eq(f.trainer:apply("ha_always_noon",true),true)
    eq(f.state.Hour,12); eq(f.state.Minute,0); eq(f.state.HC_TimeOfDaySeconds,43200)
    eq(f.trainer:apply("ha_always_noon",false),true)
    eq(f.state.Hour,6); eq(f.state.Minute,45); eq(f.state.HC_TimeOfDaySeconds,24300)
    eq(f.state.DayNumber,10,"Day number is not owned by noon")
    eq(f.trainer:apply("ha_always_noon",true),true)
    f.state.Hour=19; f.state.HC_TimeOfDaySeconds=68400
    eq(f.trainer:disable_all(),true)
    eq(f.state.Hour,19,"External clock changes survive cleanup"); eq(f.state.HC_TimeOfDaySeconds,68400)
end)

test("global refills restrict writes to living characters and furniture in this world",function()
    local f=fixture(); ready(f)
    local foreign_world=f.object("World")
    local foreign=f.object("HaStaffCharacter",{RestroomNeed=.9,Mood=.1,Health=.4,Energy=.2},foreign_world)
    local dead=f.object("HaBaseCharacter",{RestroomNeed=.8,Mood=.1,Health=0},f.world)
    local other_item=f.make_item(foreign_world)
    eq(f.trainer:apply("ha_no_needs",true),true)
    eq(f.person.RestroomNeed,0); eq(f.person.Mood,1); eq(f.person.Health,1); eq(f.person.Energy,1)
    eq(foreign.RestroomNeed,.9); eq(foreign.Health,.4); eq(dead.Health,0); eq(dead.RestroomNeed,.8)
    eq(f.trainer:apply("ha_furniture_condition",true),true)
    eq(f.item.Condition,1); eq(f.item.Cleanliness,1); eq(other_item.Condition,.4)
    f.person.RestroomNeed=.5; f.item.Condition=.2; f.trainer:tick()
    eq(f.person.RestroomNeed,0); eq(f.item.Condition,1)
    eq(f.trainer:disable_all(),true)
    f.person.RestroomNeed=.6; f.item.Condition=.3; f.trainer:tick()
    eq(f.person.RestroomNeed,.6); eq(f.item.Condition,.3)
end)

test("selection changes invalidate queued edits and restore owned noon fields",function()
    local f=fixture(); local initial=ready(f); local identity=f.trainer:session_key()
    eq(f.trainer:apply("ha_always_noon",true),true)
    local other=f.object("HaBaseCharacter",{RestroomNeed=.1,Mood=.2,Health=.7},f.world)
    f.controller.SelectedCharacter=f.weak(other)
    eq(f.trainer:apply("ha_health",false,100),false)
    eq(f.trainer:session_key()~=identity,true); eq(other.Health,.7)
    eq(f.trainer:enabled().ha_always_noon,false); eq(f.state.Hour,6)
    local values,available=ready(f)
    eq(values.diagnostic_ha_world_id,initial.diagnostic_ha_world_id,"Selection cannot look like another hotel")
    eq(available.ha_health,true); eq(available.ha_xp,false); eq(available.ha_energy,false)
    f.controller.SelectedCharacter=f.weak(nil); f.controller.SelectedGridItem=f.weak(nil)
    values,available=ready(f); eq(available.ha_health,false); eq(available.ha_condition,false); eq(available.ha_money,true)
end)

test("missing bindings, dead targets and incompatible values fail per control",function()
    local f=fixture(); f.missing[PREFIX.."HaGridItem:ChangeCleanliness"]=true
    local _,a=ready(f); eq(a.ha_cleanliness,false); eq(a.ha_furniture_condition,false); eq(a.ha_money,true)
    f.person.Health=0; _,a=ready(f); eq(a.ha_health,false); eq(a.ha_xp,false)
    f.state.Money=-100; _,a=ready(f); eq(a.ha_money,true,"Debt can be corrected"); eq(f.trainer:apply("ha_money",false,200),true)
    f.person.Health=.5; f.person.Mood=0/0; _,a=ready(f); eq(a.ha_mood,false)
    f.foreign_controller=true; eq(f.trainer:refresh(),false); eq(f.trainer:apply("ha_money",false,100),false)
end)

test("throwing and silent setters report failure and never retry one-shot mutations",function()
    local f=fixture(); ready(f)
    f.noop="HC_CurrentXP"; eq(f.trainer:apply("ha_xp",false,100),false); eq(f.person.HC_CurrentXP,20)
    f.noop=nil; f.money_error=true
    eq(f.trainer:apply("ha_money",false,900),false); eq(f.state.Money,900)
    local calls=#f.calls
    for _=1,5 do f.trainer:tick() end
    eq(#f.calls,calls,"Uncertain one-shot is never automatically retried")
    f.money_error=false; f.write_error="Mood"
    eq(f.trainer:apply("ha_no_needs",true),false); eq(f.trainer:enabled().ha_no_needs,false)
end)

test("XP and level edits retain the native per-level progression invariant",function()
    local f=fixture(); ready(f)
    eq(f.trainer:apply("ha_xp",false,219),true)
    eq(f.trainer:apply("ha_xp",false,220),false,"Level 2 threshold is 220")
    eq(f.trainer:apply("ha_level",false,1),true); eq(f.person.HC_CurrentXP,109)
    eq(f.trainer:apply("ha_level",false,9),true)
    eq(f.trainer:apply("ha_xp",false,989),true)
    eq(f.trainer:apply("ha_level",false,10),true); eq(f.person.HC_CurrentXP,0)
    eq(f.trainer:apply("ha_xp",false,1),false,"Maximum-level staff have zero current XP")
    eq(f.trainer:apply("ha_xp",false,0),true)
    f.person.HC_CurrentXP=1000000
    eq(f.trainer:apply("ha_level",false,5),true); eq(f.person.HC_CurrentXP,549)
end)

test("active maintenance disables conflicting editors",function()
    local f=fixture(); ready(f)
    for toggle,actions in pairs({ha_no_needs={"ha_restroom_need","ha_mood","ha_health","ha_energy"},
        ha_always_noon={"ha_hour","ha_minute","ha_day_number","ha_total_hours"},
        ha_furniture_condition={"ha_cleanliness","ha_condition"}}) do
        eq(f.trainer:apply(toggle,true),true)
        local _,available=ready(f)
        for _,action in ipairs(actions) do eq(available[action],false); eq(f.trainer:apply(action,false,0),false) end
        eq(f.trainer:apply(toggle,false),true)
        _,available=ready(f)
        for _,action in ipairs(actions) do eq(available[action],true) end
    end
end)

test("integer readback cannot hide a one-unit write error at large balances",function()
    local f=fixture(); ready(f)
    f.after_write=function(object,key,value) if key=="Money" then object._fields.Money=value-1 end end
    eq(f.trainer:apply("ha_money",false,1000000000),false)
    eq(f.state.Money,999999999)
end)

test("restoration receipts survive temporary failures",function()
    local f=fixture(); ready(f); eq(f.trainer:apply("ha_always_noon",true),true)
    f.read_error="HC_TimeOfDaySeconds"; eq(f.trainer:disable_all(),false)
    f.read_error=nil; f.trainer:tick(); eq(f.state.HC_TimeOfDaySeconds,23400)
    eq(f.trainer:enabled().ha_always_noon,false)
end)
print("Hotel Architect bindings: "..checks.." assertions passed")
