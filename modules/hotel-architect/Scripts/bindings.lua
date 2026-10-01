-- Hotel Architect Steam build 25340122. Native binding evidence is recorded in
-- docs/hotel-architect-bindings.md. UObject work runs only on the game thread.
local source=debug.getinfo(1,"S").source:gsub("^@",""):gsub("\\","/")
local scripts=assert(source:match("^(.*)/[^/]+$"))
local P=dofile(scripts.."/protocol.lua")
local M,T={},{}
T.__index=T
local PREFIX="/Script/HotelArchitect."
local function verify_build()
    local file=io.open(scripts.."/../../../../HotelArchitect-Win64-Shipping.exe","rb")
    if not file then return false end
    local ok,verified=pcall(function()
        if file:seek("end")~=164258816 then return false end
        local function read(offset,count) if not file:seek("set",offset) then return nil end; return file:read(count) end
        local dos=read(0,64)
        if not dos or #dos~=64 or dos:sub(1,2)~="MZ" then return false end
        local pe=string.unpack("<I4",dos,61)
        local header=read(pe,84)
        if not header or #header~=84 or header:sub(1,4)~="PE\0\0" then return false end
        if string.unpack("<I2",header,5)~=0x8664 or string.unpack("<I4",header,9)~=2252431335
            or string.unpack("<I2",header,25)~=0x20b or string.unpack("<I4",header,81)~=168026112 then return false end
        local function matches(offset,hex)
            local expected=hex:gsub("..",function(pair) return string.char(tonumber(pair,16)) end)
            return read(offset,#expected)==expected
        end
        return matches(0x48bb240,"4885c9741b8b41080fbae01e72128b816c07000089028b8168070000418900c333c08902418900c3")
            and matches(0x4a139e0,"0f28c1f30f11899c040000f30f5905a5ca6e03f30f1189a0040000f30f118194040000f30f118198040000c3")
    end)
    file:close()
    return ok and verified==true
end
local function finite(v) return type(v)=="number" and v==v and math.abs(v)<math.huge end
local function close(a,b)
    if type(a)~=type(b) then return false end
    if type(a)~="number" then return a==b end
    if not finite(a) or not finite(b) then return false end
    if a%1==0 and b%1==0 then return a==b end
    return math.abs(a-b)<=math.max(0.00001,math.abs(b)*0.000001)
end
local function valid(o) local ok,v=pcall(function() return o:IsValid() end); return ok and v==true end
local function call(o,name,...)
    if not valid(o) then return false,nil end
    local args=table.pack(...)
    return pcall(function() return o[name](o,table.unpack(args,1,args.n)) end)
end
local function get(o,path)
    if not valid(o) then return nil end
    local ok,value=pcall(function()
        local current=o
        for name in path:gmatch("[^.]+") do current=current[name] end
        return current
    end)
    if ok then return value end
    return nil
end
local function put(o,path,value)
    if not valid(o) then return false end
    local ok=pcall(function()
        local names={}; for name in path:gmatch("[^.]+") do names[#names+1]=name end
        local owner=o; for i=1,#names-1 do owner=owner[names[i]] end
        owner[names[#names]]=value
    end)
    return ok and close(get(o,path),value)
end
local function id(o) local ok,v=call(o,"GetAddress"); return ok and tostring(v) or "" end
local function same(a,b) return valid(a) and valid(b) and id(a)~="" and id(a)==id(b) end
local function isa(o,class) local ok,v=call(o,"IsA",PREFIX..class); return ok and v==true end
local function world(o) local ok,v=call(o,"GetWorld"); return ok and valid(v) and v or nil end
local function belongs(o,w) return valid(o) and same(world(o),w) end
local function find(path) local ok,v=pcall(StaticFindObject,path); return ok and valid(v) and v or nil end
local function all(class,w)
    local ok,list=pcall(FindAllOf,class)
    if not ok or type(list)~="table" or #list>20000 then return nil end
    local result={}
    for _,o in ipairs(list) do if isa(o,class) and belongs(o,w) then result[#result+1]=o end end
    return result
end
local function selected(controller,path,class,w)
    local weak=get(controller,path)
    -- SelectedCharacter/SelectedGridItem are native WeakObjectProperty fields.
    local ok,o=pcall(function() return weak:Get() end)
    return ok and isa(o,class) and belongs(o,w) and o or nil
end
local function discover()
    local ok,list=pcall(FindAllOf,"HaPlayerController")
    if not ok or type(list)~="table" or #list>16 then return {} end
    local controller,w
    for _,candidate in ipairs(list) do
        local local_ok,is_local=call(candidate,"IsLocalController")
        local candidate_world=world(candidate)
        if isa(candidate,"HaPlayerController") and local_ok and is_local and valid(candidate_world) then
            if controller then return {} end
            controller,w=candidate,candidate_world
        end
    end
    if not controller then return {} end
    local modes=all("HaStandardGameMode",w)
    if not modes or #modes~=1 then return {} end
    local mode=modes[1]
    local state=get(mode,"HaGameState")
    if not isa(state,"HaGameState") or not belongs(state,w) then return {} end
    return {controller=controller,world=w,mode=mode,state=state,
        person=selected(controller,"SelectedCharacter","HaBaseCharacter",w),
        item=selected(controller,"SelectedGridItem","HaGridItem",w)}
end
local specs={
    ha_days_passed={"state","FinancialData.DaysPassed"},ha_money={"state","Money"},ha_stars={"state","Stars"},
    ha_day_number={"state","DayNumber"},ha_hour={"state","Hour"},ha_minute={"state","Minute"},
    ha_restroom_need={"person","RestroomNeed",100},ha_mood={"person","Mood",100},ha_health={"person","Health",100},
    ha_xp={"staff","HC_CurrentXP"},ha_level={"staff","HC_Level"},ha_retirement_days={"staff","DaysUntilRetirement"},
    ha_energy={"staff","Energy",100},ha_room_value_multiplier={"item","RoomValueMultiplier"},
    ha_cleanliness={"item","Cleanliness",100},ha_attractiveness={"item","AttractivenessValue"},
    ha_negative_attractiveness={"item","NegativeAttractivenessValue"},ha_condition={"item","Condition",100},
    ha_game_speed={"mode","GameSpeed"},
}
function T:enabled() local result={}; for k,v in pairs(self.flags) do result[k]=v end; return result end
function T:session_key() return self.identity end
function T:_restore(action)
    local kept,success={},true
    for _,receipt in ipairs(self.receipts) do
        if action and receipt.action~=action then kept[#kept+1]=receipt
        elseif valid(receipt.object) then
            local current=get(receipt.object,receipt.path)
            if current==nil then kept[#kept+1]=receipt; success=false
            elseif close(current,receipt.applied) and not put(receipt.object,receipt.path,receipt.original) then
                kept[#kept+1]=receipt; success=false
            end
        end
    end
    self.receipts=kept
    return success
end
function T:disable_all()
    for action in pairs(self.flags) do self.flags[action]=false end
    local ok=self:_restore()
    return ok,ok and "All Hotel Architect toggles disabled." or "Hotel Architect restoration is pending."
end
function T:_owned(action,object,path,value)
    local current=get(object,path)
    if current==nil then return false end
    local receipt
    for _,candidate in ipairs(self.receipts) do
        if candidate.action==action and same(candidate.object,object) and candidate.path==path then receipt=candidate; break end
    end
    if receipt then
        if not close(current,receipt.applied) then
            -- A new game value supersedes our previous ownership. Remember it
            -- for cleanup before maintaining the explicitly enabled override.
            receipt.original=current
        end
        receipt.applied=value
    else
        self.receipts[#self.receipts+1]={action=action,object=object,path=path,original=current,applied=value}
    end
    return close(current,value) or put(object,path,value)
end
function T:_value(action)
    if action=="ha_total_hours" then
        local day,time=get(self.context.state,"DayNumber"),get(self.context.state,"GameHoursTime")
        return finite(day) and finite(time) and day*24+time or nil
    end
    local spec=specs[action]
    local value=spec and get(self.context[spec[1]],spec[2]) or nil
    return finite(value) and value*(spec[3] or 1) or nil
end
local function function_exists(class,name) return valid(find(PREFIX..class..":"..name)) end
function T:_availability(available,values)
    local c=self.context
    self.character_functions=find(PREFIX.."Default__HaCharacterFunctions")
    self.sandbox_functions=find(PREFIX.."Default__HaSandboxFunctionLibrary")
    available.ha_money=available.ha_money and function_exists("HaGameState","ChangeMoneyAndAdjustNetWorth")
    available.ha_game_speed=available.ha_game_speed and function_exists("HaStandardGameMode","SetGameSpeed")
    available.ha_stars=available.ha_stars and valid(self.sandbox_functions) and function_exists("HaSandboxFunctionLibrary","ChangeNumberOfStars")
    available.ha_restroom_need=available.ha_restroom_need and valid(self.character_functions) and function_exists("HaCharacterFunctions","ChangeRestroomNeed")
    available.ha_health=available.ha_health and valid(self.character_functions) and function_exists("HaCharacterFunctions","ChangeCharacterHealth")
    available.ha_cleanliness=available.ha_cleanliness and function_exists("HaGridItem","ChangeCleanliness")
    available.ha_condition=available.ha_condition and function_exists("HaGridItem","OnConditionChanged")
    available.ha_no_needs=valid(self.character_functions) and function_exists("HaCharacterFunctions","ChangeRestroomNeed") and function_exists("HaCharacterFunctions","ChangeCharacterHealth")
    local seconds=get(c.state,"HC_TimeOfDaySeconds")
    local previous=get(c.state,"HC_PreviousHours")
    local clock_ready=finite(seconds) and seconds>=0 and seconds<=86400 and finite(previous) and previous>=0 and previous<=24
    available.ha_always_noon=clock_ready
    for _,action in ipairs({"ha_day_number","ha_hour","ha_minute","ha_total_hours"}) do available[action]=available[action] and clock_ready end
    if self.flags.ha_always_noon then
        for _,action in ipairs({"ha_day_number","ha_hour","ha_minute","ha_total_hours"}) do available[action]=false end
    end
    if self.flags.ha_no_needs then
        for _,action in ipairs({"ha_restroom_need","ha_mood","ha_health","ha_energy"}) do available[action]=false end
    end
    if self.flags.ha_furniture_condition then available.ha_cleanliness=false; available.ha_condition=false end
    available.ha_furniture_condition=function_exists("HaGridItem","ChangeCleanliness") and function_exists("HaGridItem","OnConditionChanged")
    -- Aging lives in a native static byte. The client owns its separate,
    -- executable-hash-checked backend and combines that availability in UI.
    available.ha_disable_aging=false
    if not valid(c.person) or not finite(get(c.person,"Health")) or get(c.person,"Health")<=0 then
        for _,action in ipairs({"ha_restroom_need","ha_mood","ha_health","ha_xp","ha_level","ha_retirement_days","ha_energy"}) do available[action]=false end
    end
end
function T:_cleanliness(item,value)
    local current=get(item,"Cleanliness")
    if not finite(current) or current<0 or current>1 then return false end
    if close(current,value) then return true end
    local ok=call(item,"ChangeCleanliness",value-current)
    return ok and close(get(item,"Cleanliness"),value)
end
function T:_condition(item,value)
    local current=get(item,"Condition")
    if not finite(current) or current<0 or current>1 then return false end
    if close(current,value) then return true end
    if not put(item,"Condition",value) then return false end
    -- The native delta helper requires a servicing staff member. The verified
    -- no-argument event refreshes visuals after this explicit exact edit.
    local ok=call(item,"OnConditionChanged")
    return ok and close(get(item,"Condition"),value)
end
function T:_restroom(person,value)
    local current=get(person,"RestroomNeed")
    if not finite(current) or current<0 or current>1 then return false end
    if close(current,value) then return true end
    local ok=call(self.character_functions,"ChangeRestroomNeed",person,value-current)
    return ok and close(get(person,"RestroomNeed"),value)
end
function T:_health(person,value)
    local current=get(person,"Health")
    if not finite(current) or current<=0 or current>1 then return false end
    if close(current,value) then return true end
    local ok=call(self.character_functions,"ChangeCharacterHealth",person,value-current)
    return ok and close(get(person,"Health"),value)
end
function T:_maintain(action)
    if action=="ha_always_noon" then
        return self:_clock(get(self.context.state,"DayNumber"),12,action)
    elseif action=="ha_no_needs" then
        local people=all("HaBaseCharacter",self.context.world)
        if not people then return false end
        local success=true
        for _,person in ipairs(people) do
            local health=get(person,"Health")
            -- Do not revive or alter dead characters.
            if finite(health) and health>0 and health<=1 then
                local mood=get(person,"Mood")
                if not finite(mood) or mood<0 or mood>1 or not self:_restroom(person,0) or not put(person,"Mood",1) or not self:_health(person,1) then success=false end
                if isa(person,"HaStaffCharacter") then
                    local energy=get(person,"Energy")
                    if not finite(energy) or energy<0 or energy>1 or not put(person,"Energy",1) then success=false end
                end
            end
        end
        return success
    elseif action=="ha_furniture_condition" then
        local items=all("HaGridItem",self.context.world)
        if not items then return false end
        local success=true
        for _,item in ipairs(items) do
            if not self:_condition(item,1) or not self:_cleanliness(item,1) then success=false end
        end
        return success
    end
    return false
end
function T:_clock(day,hours,owned_action)
    if not finite(day) or day%1~=0 or day<0 or day>100000 or not finite(hours) or hours<0 or hours>=24 then return false end
    local state=self.context.state
    local hour=math.floor(hours)
    local minute=math.min(59,math.floor((hours-hour)*60+0.000001))
    local fields={{"DayNumber",day},{"HC_TimeOfDaySeconds",hours*3600},{"GameHoursTime",hours},
        {"HC_PreviousHours",hours},{"Hour",hour},{"Minute",minute}}
    -- The independent hourly-event accumulator at 0x494 is intentionally kept
    -- intact. Resetting it to noon would replay hourly finance/events on ticks.
    for _,field in ipairs(fields) do
        if owned_action then
            -- Noon freezes time-of-day only; it never owns or restores the day.
            if field[1]~="DayNumber" and not self:_owned(owned_action,state,field[1],field[2]) then return false end
        elseif not put(state,field[1],field[2]) then return false end
    end
    return true
end
function T:_edit(action,amount)
    local c=self.context
    if action=="ha_game_speed" then
        local ok=call(c.mode,"SetGameSpeed",amount)
        return ok and close(get(c.mode,"GameSpeed"),amount)
    elseif action=="ha_money" then
        local current=get(c.state,"Money")
        if not finite(current) then return false end
        local ok=call(c.state,"ChangeMoneyAndAdjustNetWorth",amount-current)
        return ok and close(get(c.state,"Money"),amount)
    elseif action=="ha_stars" then
        local current=get(c.state,"Stars")
        if not finite(current) then return false end
        local ok=call(self.sandbox_functions,"ChangeNumberOfStars",c.controller,amount-current)
        return ok and close(get(c.state,"Stars"),amount)
    elseif action=="ha_total_hours" then
        return self:_clock(math.floor(amount/24),amount%24)
    elseif action=="ha_day_number" then return self:_clock(amount,get(c.state,"GameHoursTime"))
    elseif action=="ha_hour" then
        return self:_clock(get(c.state,"DayNumber"),amount+get(c.state,"Minute")/60)
    elseif action=="ha_minute" then
        return self:_clock(get(c.state,"DayNumber"),get(c.state,"Hour")+amount/60)
    elseif action=="ha_restroom_need" then return self:_restroom(c.person,amount/100)
    elseif action=="ha_health" then return self:_health(c.person,amount/100)
    elseif action=="ha_cleanliness" then return self:_cleanliness(c.item,amount/100)
    elseif action=="ha_condition" then return self:_condition(c.item,amount/100)
    elseif action=="ha_xp" then
        local level=get(c.staff,"HC_Level")
        if not finite(level) or level<1 or level>10 or level%1~=0 then return false end
        local maximum=level==10 and 0 or 110*level-1
        return amount<=maximum and put(c.staff,"HC_CurrentXP",amount)
    elseif action=="ha_level" then
        local xp=get(c.staff,"HC_CurrentXP")
        if not finite(xp) or xp<0 or xp%1~=0 then return false end
        local maximum=amount==10 and 0 or 110*amount-1
        -- XP is the current level's progress, not lifetime experience. Lowering
        -- level or reaching the cap must retain valid native progression state.
        if xp>maximum and not put(c.staff,"HC_CurrentXP",maximum) then return false end
        return put(c.staff,"HC_Level",amount)
    end
    local spec=specs[action]
    if not spec then return false end
    return put(c[spec[1]],spec[2],amount/(spec[3] or 1))
end
function T:refresh()
    if not self.build_verified then
        self:disable_all(); self.identity=""; self.available={}; self.ready=false
        return false,"Unsupported Hotel Architect executable. Reinstall the supported trainer after an update.",{},{ }
    end
    if not self.custom_registered then
        local ok=pcall(function()
            RegisterCustomProperty({Name="HC_CurrentXP",Type=PropertyTypes.IntProperty,BelongsToClass=PREFIX.."HaStaffCharacter",OffsetInternal=0x768})
            RegisterCustomProperty({Name="HC_Level",Type=PropertyTypes.IntProperty,BelongsToClass=PREFIX.."HaStaffCharacter",OffsetInternal=0x76c})
            RegisterCustomProperty({Name="HC_TimeOfDaySeconds",Type=PropertyTypes.FloatProperty,BelongsToClass=PREFIX.."HaGameState",OffsetInternal=0x498})
            RegisterCustomProperty({Name="HC_PreviousHours",Type=PropertyTypes.FloatProperty,BelongsToClass=PREFIX.."HaGameState",OffsetInternal=0x4a0})
        end)
        if not ok then self.ready=false; return false,"Hotel Architect native property registration failed.",{},{ } end
        self.custom_registered=true
    end
    local context=discover()
    local identity=valid(context.state) and table.concat({id(context.world),id(context.controller),id(context.state),id(context.person),id(context.item)},":") or ""
    if identity~=self.identity then self:disable_all(); self.identity=identity end
    context.staff=isa(context.person,"HaStaffCharacter") and context.person or nil
    self.context=context
    local available,values={},{}
    self.available=available
    self.ready=valid(context.state)
    if not self.ready then self:disable_all(); return false,"Load a Hotel Architect hotel to connect.",values,available end
    for action in pairs(P.numbers) do
        local value=self:_value(action)
        -- Existing debt, unusual modifiers, or a high accumulated total must
        -- not prevent correcting the value into the editor's supported range.
        local spec=P.numbers[action]
        available[action]=value~=nil and math.abs(value)<=9007199254740991 and (not spec[3] or value%1==0)
        if value~=nil then values[action]=value end
    end
    -- Feature-specific write bindings are checked below after native discovery.
    self:_availability(available,values)
    values.ha_validation="Experimental; native startup and gameplay remain unverified."
    values.diagnostic_ha_selected_person=valid(context.person) and id(context.person) or "none"
    values.diagnostic_ha_selected_furniture=valid(context.item) and id(context.item) or "none"
    values.diagnostic_ha_world_id=id(context.world)..":"..id(context.state)
    return true,"Hotel Architect connected. Select a person or furniture for its editors.",values,available
end
function T:apply(action,enabled,amount)
    if action=="disable_all" then return self:disable_all() end
    if self.flags[action]~=nil and enabled==false then
        self.flags[action]=false
        local ok=self:_restore(action)
        return ok,ok and "Hotel Architect control disabled." or "Hotel Architect restoration is pending."
    end
    local identity=self.identity
    self:refresh()
    if self.identity~=identity then return false,"Hotel or selection changed. Reconnect before editing." end
    if not self.ready or self.available[action]~=true then return false,"This control needs its supported game binding and an appropriate selected target." end
    if P.toggles[action] then
        if enabled~=true then return false,"An explicit toggle state is required." end
        if not self:_maintain(action) then self.flags[action]=false; self:_restore(action); return false,"Hotel Architect write could not be verified." end
        self.flags[action]=true
        return true,"Hotel Architect control enabled."
    end
    if enabled==true or not P.valid_amount(action,amount) then return false,"Invalid Hotel Architect editor value." end
    if action=="ha_xp" then
        local level=get(self.context.staff,"HC_Level")
        local maximum=finite(level) and (level==10 and 0 or 110*level-1) or -1
        if amount>maximum then return false,"Current XP must be below 110 times the selected staff level; level 10 uses zero XP." end
    end
    local ok=self:_edit(action,amount)
    return ok,ok and "Hotel Architect value updated once." or "Hotel Architect write could not be verified; it was not retried."
end
function T:tick(reuse_refresh)
    if not reuse_refresh then self:refresh() end
    for action,enabled in pairs(self.flags) do
        if enabled and (not self.ready or not self.available[action] or not self:_maintain(action)) then self.flags[action]=false end
        if not self.flags[action] then self:_restore(action) end
    end
end
-- Native-specific setters and initialization follow; no alternate names or
-- memory scans are used when a verified binding is absent.
function M.new()
    local self=setmetatable({flags={},receipts={},identity="",context={},available={},build_verified=verify_build()},T)
    for action in pairs(P.toggles) do if action~="ha_disable_aging" then self.flags[action]=false end end
    return self
end
return M
