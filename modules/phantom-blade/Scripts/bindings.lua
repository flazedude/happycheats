-- Reflected CurrentValue fields inspected in Steam build 26266538.
-- See docs/phantom-blade-bindings.md. All calls run on the UE4SS game thread.
local M, T = {}, {}
T.__index = T
local PLAYER = "/Script/ProjectPBZ.PBZCharacter_Player"
local LOCAL_PLAYER = "/Script/Engine.LocalPlayer"
local BASIC = "/Script/CharacterBasics.BasicAttributeSet"
local resources = {pbz_health={"Health","Health_Max"}, pbz_stamina={"Shachi","Shachi_Max"}}
local function finite(v) return type(v)=="number" and v==v and math.abs(v)<math.huge end
local function close(a,b) return finite(a) and finite(b) and math.abs(a-b)<=math.max(0.01,math.abs(b)*0.00001) end
local function valid(o)
    local ok,v=pcall(function() return o:IsValid() end)
    return ok and v==true
end
local function call(o,method,...)
    if not valid(o) then return false,nil end
    local args=table.pack(...)
    return pcall(function() return o[method](o,table.unpack(args,1,args.n)) end)
end
local function read(o,property)
    if not valid(o) then return false,nil end
    return pcall(function() return o[property] end)
end
local function find(path)
    local ok,o=pcall(StaticFindObject,path)
    if not ok then return nil,o end
    if not valid(o) then return nil,"object not found or invalid" end
    return o
end
local function diagnostic(value)
    local ok,text=pcall(tostring,value)
    text=ok and text or "unprintable error"
    text=text:gsub("[%z\1-\31\127]"," "):gsub("%s+"," ")
    return #text>160 and text:sub(1,157).."..." or text
end
local function address(o)
    local ok,v=call(o,"GetAddress")
    return ok and v~=nil and tostring(v) or ""
end
local function same(a,b) local key=address(a); return key~="" and key==address(b) end
local function owned(pawn,controller,class,local_class)
    if not valid(pawn) then return false,"Controlled player is unavailable" end
    if not valid(controller) then return false,"Player controller is unavailable" end
    if not valid(class) then return false,"Player class is unavailable" end
    if not valid(local_class) then return false,"Local-player class is unavailable" end
    -- These reflected links are verified in this game's live metadata. Reading
    -- possession directly avoids redundant native getter calls through ProcessEvent.
    local a,local_player=read(controller,"Player")
    if not a then return false,"Local-player lookup failed",local_player end
    if not valid(local_player) then return false,"Local player is unavailable" end
    local local_ok,is_local=call(local_player,"IsA",local_class)
    if not local_ok then return false,"Local-player class check failed",is_local end
    if is_local~=true then return false,"Controller does not belong to a local player" end
    local link_ok,local_controller=read(local_player,"PlayerController")
    if not link_ok then return false,"Local-player controller lookup failed",local_controller end
    if not same(local_controller,controller) then return false,"Local player does not own this controller" end
    local b,current=read(controller,"Pawn")
    if not b then return false,"Controlled-pawn lookup failed",current end
    if not same(current,pawn) then return false,"Controller possession does not match the player" end
    local d,owner=read(pawn,"Controller")
    if not d then return false,"Player-controller lookup failed",owner end
    if not same(owner,controller) then return false,"Player ownership does not match the controller" end
    local e,matching_class=call(pawn,"IsA",class)
    if not e then return false,"Player-class check failed",matching_class end
    if matching_class~=true then return false,"Controlled pawn is not the Phantom Blade Zero player class" end
    return true
end
function T:_player()
    if self.pawn or self.controller then
        local is_owned,reason,detail=owned(self.pawn,self.controller,self.player_class,self.local_player_class)
        if is_owned then
            local ok,world=call(self.pawn,"GetWorld")
            if ok and valid(world) then
                self.discovery_reason,self.discovery_detail=nil,nil
                return self.pawn,self.controller,world
            end
            reason=ok and "Player world is unavailable" or "Player-world lookup failed"
            detail=not ok and world or nil
        end
        self.discovery_reason,self.discovery_detail=reason,detail
    end
    -- Failed discovery is throttled; stable sessions never scan the object array.
    -- Retain the failed stage between scans instead of replacing it with a load-save hint.
    local function failed(reason,detail)
        if reason then self.discovery_reason,self.discovery_detail=reason,detail end
        self.discovery_reason=self.discovery_reason or "Player controller is unavailable"
        return nil,nil,nil,self.discovery_reason,self.discovery_detail
    end
    local now=os.time()
    if now<self.next_discovery then return failed() end
    self.next_discovery=now+1
    local class,reason=find(PLAYER)
    self.player_class=class
    if not class then return failed("Player class is unavailable",reason) end
    self.local_player_class,reason=find(LOCAL_PLAYER)
    if not self.local_player_class then return failed("Local-player class is unavailable",reason) end
    local ok,list=pcall(FindAllOf,"PlayerController")
    if not ok then return failed("Player-controller discovery failed",list) end
    if type(list)~="table" or #list==0 then return failed("Player controller is unavailable") end
    local pawn,controller,world,detail
    reason="Player controller is unavailable"
    for _,candidate in ipairs(list) do
        local got,p=read(candidate,"Pawn")
        if not got then reason,detail="Controlled-pawn lookup failed",p
        else
            local is_owned,unavailable,error_detail=owned(p,candidate,self.player_class,self.local_player_class)
            if not is_owned then reason,detail=unavailable,error_detail
            else
                local w,context=call(p,"GetWorld")
                if not w then reason,detail="Player-world lookup failed",context
                elseif not valid(context) then reason,detail="Player world is unavailable",nil
                else
                    if pawn then return failed("More than one locally controlled Phantom Blade Zero player is available") end
                    pawn,controller,world=p,candidate,context
                end
            end
        end
    end
    if not pawn then return failed(reason,detail) end
    self.discovery_reason,self.discovery_detail=nil,nil
    return pawn,controller,world
end
function T:_attribute_set(pawn)
    if not valid(pawn) then return nil end
    if not valid(self.basic_class) then self.basic_class=find(BASIC) end
    if not valid(self.basic_class) then return nil end
    local ok,set=pcall(function() return pawn.AttributeSetBase end)
    if not ok or not valid(set) then return nil end
    local a,class=call(set,"IsA",self.basic_class)
    local b,owner=call(set,"GetOuter")
    return a and class==true and b and same(owner,pawn) and set or nil
end
function T:_data(name,pawn,expected_set)
    local set=self:_attribute_set(pawn or self.pawn)
    if not set or expected_set and not same(set,expected_set) then return nil end
    local ok,data=pcall(function() return set[name] end)
    if not ok or not valid(data) then return nil end
    local a,mapped=call(data,"IsMappedToObject")
    local b,property=call(data,"IsMappedToProperty")
    return a and mapped==true and b and property==true and data or nil
end
function T:_value(name,field,pawn,set)
    local data=self:_data(name,pawn,set)
    if not data then return nil end
    local ok,value=pcall(function() return data[field] end)
    return ok and finite(value) and value>=0 and value<=100000000 and value or nil
end
function T:_read(name,pawn,set) return self:_value(name,"CurrentValue",pawn,set) end
function T:_base(pawn,set) return self:_value("Attack","BaseValue",pawn,set) end
function T:_write(pawn,name,value,set)
    if not finite(value) or value<0 or value>100000000 then return false end
    local data=self:_data(name,pawn,set)
    if not data then return false end
    -- UE4SS's remote UScriptStruct __newindex writes this one reflected float.
    -- Never copy a struct or write BaseValue; normal game recalculation remains authoritative.
    return pcall(function() data.CurrentValue=value end)
end
function T:_restore_damage()
    local lease=self.damage
    self.flags.pbz_damage=false
    if not lease then return true end
    if not valid(lease.pawn) then self.damage=nil; return true end
    if not valid(lease.set) then self.damage=nil; return true end
    local ok,world=call(lease.pawn,"GetWorld")
    if not ok or not valid(world) then return false end
    if not same(world,lease.world) then self.damage=nil; return true end
    local set=self:_attribute_set(lease.pawn)
    if not set then return false end
    if not same(set,lease.set) then self.damage=nil; return true end
    local base=self:_base(lease.pawn,lease.set)
    local current=self:_read("Attack",lease.pawn,lease.set)
    if base==nil or current==nil then return false end
    if not close(base,lease.base) or close(current,lease.original) or not close(current,lease.applied) then
        -- The game owns any later base/current change. Preserve its recalculation.
        self.damage=nil; return true
    end
    self:_write(lease.pawn,"Attack",lease.original,lease.set)
    local restored=self:_read("Attack",lease.pawn,lease.set)
    if restored==nil or close(restored,lease.applied) then return false end
    self.damage=nil
    return close(restored,lease.original)
end
function T:disable_all()
    self.flags.pbz_health=false; self.flags.pbz_stamina=false
    local restored=self:_restore_damage()
    return restored,restored and "All Phantom Blade Zero controls reset." or "Attack restoration is pending; keep HappyCheats connected."
end
function T:enabled()
    local copy={}; for name,value in pairs(self.flags) do copy[name]=value end; return copy
end
function T:session_key() return self.identity end
function T:refresh()
    local pawn,controller,world,discovery,discovery_detail=self:_player()
    local identity=valid(pawn) and (address(world)..":"..address(controller)..":"..address(pawn)) or ""
    if identity~=self.identity then
        self:disable_all()
        self.identity=identity
    end
    self.pawn,self.controller,self.world=pawn,controller,world
    self.ready=false; self.available={pbz_health=false,pbz_stamina=false,pbz_damage=false}
    -- An unverified restoration must not publish 1 as a confirmed reset.
    local values=self.damage and {} or {pbz_damage="1"}
    if not valid(pawn) then
        -- Keep the UI sentence stable and translatable; runtime details stay in telemetry.
        values.pbz_discovery=diagnostic(discovery_detail or discovery)
        return false,"Phantom Blade Zero player detection: "..discovery..".",values,self.available
    end
    if not self:_attribute_set(pawn) then
        self:disable_all()
        return false,"Phantom Blade Zero owned attribute set is unavailable.",values,self.available
    end
    local health=self:_read("Health")
    if health==nil or health<=0 then
        self:disable_all()
        return false,"Waiting for a living Phantom Blade Zero player.",values,self.available
    end
    for feature,spec in pairs(resources) do
        local current,maximum=self:_read(spec[1]),self:_read(spec[2])
        self.available[feature]=current~=nil and maximum~=nil and maximum>0 and current<=maximum+0.01
        if current~=nil then values[feature]=string.format("%.3f",current) end
        if maximum~=nil then values[feature.."_max"]=string.format("%.3f",maximum) end
    end
    local attack=self:_read("Attack")
    local base=self:_base(pawn)
    if self.damage and self.flags.pbz_damage then
        if not same(self:_attribute_set(pawn),self.damage.set) then
            self.flags.pbz_damage=false; self.damage=nil
        elseif not close(base,self.damage.base) or not close(attack,self.damage.applied) then
            self.flags.pbz_damage=false
            if base~=nil and attack~=nil then self.damage=nil end
        end
    end
    self.available.pbz_damage=attack~=nil and base~=nil and base>0 and (close(attack,base) or self.flags.pbz_damage)
        and (self.damage==nil or self.flags.pbz_damage)
    if attack~=nil then values.pbz_attack=string.format("%.3f",attack) end
    if self.flags.pbz_damage and self.damage then values.pbz_damage=tostring(self.damage.multiplier)
    elseif not self.damage then values.pbz_damage="1" end
    self.ready=true
    return true,self.damage and not self.flags.pbz_damage and "Attack restoration is pending; keep HappyCheats connected."
        or "Experimental Phantom Blade Zero trainer connected. Gameplay effects are unverified.",values,self.available
end
function T:_refill(feature)
    local spec=resources[feature]
    local health=self:_read("Health")
    local current,maximum=self:_read(spec[1]),self:_read(spec[2])
    if health==nil or health<=0 or current==nil or maximum==nil or maximum<=0 or current>maximum+0.01 then return false end
    if close(current,maximum) then return true end
    local ok=self:_write(self.pawn,spec[1],maximum)
    return ok and close(self:_read(spec[1]),maximum)
end
function T:apply(action,enabled,amount)
    if action=="disable_all" then return self:disable_all() end
    if resources[action] and enabled==false then self.flags[action]=false; return true,"Phantom Blade Zero control disabled." end
    if action=="pbz_damage" and amount==1 then
        local ok=self:_restore_damage()
        return ok,ok and "Attack multiplier reset." or "Attack restoration is pending; keep HappyCheats connected."
    end
    local identity=self.identity
    self:refresh()
    if identity=="" or self.identity~=identity then return false,"Phantom Blade Zero session changed; reconnect before trying again." end
    if not self.ready or not self.available[action] then return false,"This Phantom Blade Zero control is unavailable in the current session." end
    if resources[action] then
        if enabled~=true then return false,"An explicit toggle state is required." end
        if not self:_refill(action) then self.flags[action]=false; return false,"Resource refill could not be verified." end
        self.flags[action]=true; return true,"Phantom Blade Zero control enabled."
    end
    if action~="pbz_damage" then return false,"Unknown Phantom Blade Zero control." end
    if not finite(amount) or amount%1~=0 or amount<1 or amount>20 then return false,"Choose a whole-number attack multiplier from 1 to 20." end
    if self.flags.pbz_damage and self.damage and self.damage.multiplier==amount then return true,"Attack power multiplier is already applied." end
    if not self:_restore_damage() then return false,"Previous attack restoration is pending." end
    local set=self:_attribute_set(self.pawn)
    if not set then return false,"The player's owned attribute set is unavailable." end
    local original=self:_base(self.pawn,set)
    local current=self:_read("Attack",self.pawn,set)
    if original==nil or original<=0 or not close(current,original) then return false,"Attack modifiers are active or unavailable; multiplier was not applied." end
    local applied=original*amount
    if applied>100000000 then return false,"The requested attack value exceeds the supported range." end
    -- Keep the restoration lease before dispatch: a failed call/readback can still have written.
    self.damage={pawn=self.pawn,world=self.world,set=set,base=original,
        original=current,applied=applied,multiplier=amount}
    local ok=self:_write(self.pawn,"Attack",applied,set)
    if not ok or not close(self:_base(self.pawn,set),original) or not close(self:_read("Attack",self.pawn,set),applied) then
        self:_restore_damage()
        return false,"Attack multiplier could not be verified; restoration will retry while connected."
    end
    self.flags.pbz_damage=true
    return true,"Attack power multiplier applied. Final damage still follows the game's combat rules."
end
function T:tick(reuse_refresh)
    if not reuse_refresh then self:refresh() end
    if self.damage and not self.flags.pbz_damage then self:_restore_damage() end
    if not self.ready then return end
    for feature in pairs(resources) do
        if self.flags[feature] and (not self.available[feature] or not self:_refill(feature)) then self.flags[feature]=false end
    end
end
function M.new()
    return setmetatable({identity="",next_discovery=0,available={},ready=false,
        flags={pbz_health=false,pbz_stamina=false,pbz_damage=false}},T)
end
return M
