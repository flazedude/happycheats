-- Reflected Wukong APIs; see docs/wukong.md for inspected contracts/provenance.
-- All entry points are called on UE4SS's game thread. No save files are edited.
local M, T = {}, {}
T.__index = T
local CS = "/Script/b1-Managed.Default__BGUFunctionLibraryCS"
local GM = "/Script/b1-Managed.Default__BGUFunctionLibraryManaged"
local resources = {
    wukong_health={151,1,"health"}, wukong_mana={152,2,"mana"},
    wukong_stamina={158,8,"stamina"}, wukong_gourd={186,36,"gourd"},
    wukong_focus={191,39,"focus"},
}
local progression = {wukong_xp={"AddExp",1000,1000000},
    wukong_sparks={"AddTalentPoint",1,100}, wukong_will={"AddItem 1002",1000,1000000}}
local toggles = {"wukong_health","wukong_mana","wukong_stamina","wukong_damage","wukong_gourd"}
local function finite(n) return type(n)=="number" and n==n and math.abs(n)<math.huge end
local function valid(o)
    local ok,v=pcall(function() return o:IsValid() end)
    return ok and v==true
end
local function call(o,method,...)
    if not valid(o) then return false,nil end
    local args=table.pack(...)
    return pcall(function() return o[method](o,table.unpack(args,1,args.n)) end)
end
local function find(path)
    local ok,o=pcall(StaticFindObject,path)
    if not ok then return nil,o end
    if not valid(o) then return nil,"object not found or invalid" end
    return o
end
local function diagnostic(value,limit)
    local ok,text=pcall(tostring,value)
    text=ok and text or "unprintable error"
    text=text:gsub("[%z\1-\31\127]"," "):gsub("%s+"," ")
    limit=limit or 180
    return #text>limit and text:sub(1,limit-3).."..." or text
end
local function id(o)
    local ok,v=call(o,"GetAddress")
    return ok and tostring(v) or ""
end
local function same(a,b) return id(a)~="" and id(a)==id(b) end
local function attr(lib,pawn,key)
    local ok,v=call(lib,"GetAttrValue",pawn,key)
    return ok and finite(v) and v or nil
end
local function set(lib,pawn,key,value)
    if not finite(value) or value>100000000 or (key==53 and value<=-10000) or (key~=53 and value<0) then return false end
    local ok=call(lib,"BGUSetAttrValue",pawn,key,value)
    if not ok then return false end
    local observed=attr(lib,pawn,key)
    return observed~=nil and math.abs(observed-value)<=math.max(0.01,math.abs(value)*0.00001)
end
local function player()
    local ok,list=pcall(FindAllOf,"PlayerController")
    if not ok then return nil,nil,"Wukong discovery failed: FindAllOf(PlayerController): "..diagnostic(list) end
    if type(list)~="table" then return nil,nil,"Wukong discovery: no PlayerController list (returned "..type(list).."). Load a save." end
    if #list>16 then return nil,nil,"Wukong discovery refused: "..#list.." controllers exceeds the limit of 16." end
    local counts={controllers=#list,locals=0,pawns=0,controlled=0,owners=0,classes=0,matches=0}
    local problem="No local player controller; load a save"
    local function failed(stage,detail) problem=stage..": "..diagnostic(detail) end
    local function summary()
        return string.format("controllers=%d local=%d pawns=%d controlled=%d owners=%d classes=%d matches=%d",
            counts.controllers,counts.locals,counts.pawns,counts.controlled,counts.owners,counts.classes,counts.matches)
    end
    local selected,owner
    for _,controller in ipairs(list) do
        local local_ok,is_local=call(controller,"IsLocalController")
        if not local_ok then failed("IsLocalController call failed",is_local) end
        if local_ok and is_local then
            counts.locals=counts.locals+1
            local pawn_ok,pawn=call(controller,"K2_GetPawn")
            if not pawn_ok then failed("K2_GetPawn call failed",pawn)
            elseif not valid(pawn) then failed("Local controller pawn unavailable","K2_GetPawn returned no valid pawn") end
            if pawn_ok and valid(pawn) then
                counts.pawns=counts.pawns+1
                local owned_ok,owned=call(pawn,"IsPlayerControlled")
                if not owned_ok then failed("IsPlayerControlled call failed",owned)
                elseif not owned then failed("Pawn ownership rejected","IsPlayerControlled returned false")
                else counts.controlled=counts.controlled+1 end
                local get_ok,actual=call(pawn,"GetController")
                local reciprocal=get_ok and same(actual,controller)
                if not get_ok then failed("GetController call failed",actual)
                elseif not reciprocal then
                    local owner_ok,owner_address=call(actual,"GetAddress")
                    local controller_ok,controller_address=call(controller,"GetAddress")
                    if not owner_ok then failed("Owner GetAddress call failed",owner_address)
                    elseif not controller_ok then failed("Controller GetAddress call failed",controller_address)
                    else failed("Reciprocal owner rejected","pawn.GetController differs from the local controller") end
                else counts.owners=counts.owners+1 end
                local class,class_error=find("/Script/b1-Managed.BGUPlayerCharacterCS")
                local class_ok,is_player=call(pawn,"IsA",class)
                if not class then failed("Player class unavailable /Script/b1-Managed.BGUPlayerCharacterCS",class_error)
                elseif not class_ok then failed("Player IsA call failed",is_player)
                elseif not is_player then failed("Player class rejected","pawn is not BGUPlayerCharacterCS")
                else counts.classes=counts.classes+1 end
                if owned_ok and owned and reciprocal and class_ok and is_player then
                    counts.matches=counts.matches+1
                    if selected then return nil,nil,"Wukong discovery refused: multiple matching local players; "..summary() end
                    selected,owner=pawn,controller
                end
            end
        end
    end
    return selected,owner,selected and summary() or "Wukong discovery: "..problem.."; "..summary()
end
function T:enabled()
    local copy={}; for key,value in pairs(self.flags) do copy[key]=value end; return copy
end
function T:session_key() return self.identity end
function T:_restore_damage()
    local owned=self.damage
    if not owned then return true end
    if not valid(owned.pawn) then self.damage=nil; return true end
    local current=attr(owned.lib,owned.pawn,53)
    if current==nil then return false end
    -- Preserve changes made by equipment, gameplay, or another mod after us.
    if math.abs(current-owned.applied)>math.max(0.01,owned.applied*0.00001) then self.damage=nil; return true end
    if not set(owned.lib,owned.pawn,53,owned.original) then return false end
    self.damage=nil; return true
end
function T:disable_all()
    for _,key in ipairs(toggles) do self.flags[key]=false end
    local restored=self:_restore_damage()
    return restored,restored and "All Wukong toggles disabled." or "Wukong damage restoration is pending."
end
function T:refresh()
    local pawn,controller,discovery=player()
    local identity=valid(pawn) and (id(controller)..":"..id(pawn)) or ""
    if identity~=self.identity then
        self:disable_all()
        self.identity=identity
        self.pawn,self.controller=pawn,controller
    end
    local library_error,gm_error
    self.lib,library_error=find(CS)
    self.gm,gm_error=find(GM)
    local available,values={},{wukong_discovery=diagnostic(discovery,700)}
    self.available=available
    self.ready=false
    if not valid(pawn) then
        self:disable_all()
        return false,diagnostic(discovery,700),values,available
    end
    if not valid(self.lib) then
        self:disable_all()
        return false,"Wukong attribute library unavailable (Default__BGUFunctionLibraryCS): "..diagnostic(library_error),values,available
    end
    local dead_ok,dead=call(self.lib,"BGUIsUnitDead",pawn)
    local hp_ok,hp=call(self.lib,"GetAttrValue",pawn,151)
    if not dead_ok or dead or not hp_ok or not finite(hp) or hp<=0 then
        self:disable_all()
        local reason
        if not dead_ok then reason="BGUIsUnitDead call failed: "..diagnostic(dead)
        elseif dead then reason="BGUIsUnitDead reports that the player is dead"
        elseif not hp_ok then reason="GetAttrValue(Hp=151) call failed: "..diagnostic(hp)
        elseif not finite(hp) then reason="GetAttrValue(Hp=151) returned invalid health: "..diagnostic(hp)
        else reason="GetAttrValue(Hp=151) returned non-positive health: "..diagnostic(hp) end
        return false,"Wukong waiting for a living player: "..reason..".",values,available
    end
    self.ready=true
    for action,spec in pairs(resources) do
        local current,maximum=attr(self.lib,pawn,spec[1]),attr(self.lib,pawn,spec[2])
        available[action]=current~=nil and current>=0 and maximum~=nil and maximum>0 and maximum<=10000000
        if available[action] then values["wukong_"..spec[3]]=current; values["wukong_"..spec[3].."_max"]=maximum end
    end
    local attack=attr(self.lib,pawn,153)
    local multiplier=attr(self.lib,pawn,53)
    available.wukong_damage=attack~=nil and attack>0 and attack<=1000000 and multiplier~=nil and multiplier > -10000 and multiplier<=1000000
    if attack then values.wukong_attack=attack end
    local gm_function,gm_function_error=find("/Script/b1-Managed.BGUFunctionLibraryManaged:RunScriptGM")
    local gm_ready=valid(self.gm) and valid(gm_function)
    for action in pairs(progression) do available[action]=gm_ready end
    values.wukong_validation="Experimental; gameplay validation is pending."
    if not gm_ready then
        local reason=not valid(self.gm) and "Default__BGUFunctionLibraryManaged unavailable: "..diagnostic(gm_error)
            or "RunScriptGM function unavailable: "..diagnostic(gm_function_error)
        values.wukong_progression_diagnostic=reason
        return true,"Black Myth: Wukong trainer ready; progression unavailable: "..reason,values,available
    end
    return true,"Black Myth: Wukong trainer ready.",values,available
end
function T:_refill(action)
    local spec=resources[action]
    local current,maximum=attr(self.lib,self.pawn,spec[1]),attr(self.lib,self.pawn,spec[2])
    if current==nil or not finite(maximum) or maximum<=0 or maximum>10000000 then return false end
    if action=="wukong_health" and current<=0 then return false end
    return current>=maximum or set(self.lib,self.pawn,spec[1],maximum)
end
function T:apply(action,enabled,amount)
    if action=="disable_all" then return self:disable_all() end
    if self.flags[action]~=nil and enabled==false then
        self.flags[action]=false
        if action=="wukong_damage" and not self:_restore_damage() then return false,"Wukong damage restoration is pending." end
        return true,"Wukong control disabled."
    end
    local requested_identity=self.identity
    self:refresh()
    if self.identity~=requested_identity then return false,"Black Myth: Wukong session changed. Reconnect before trying again." end
    if not self.ready or not self.available[action] then return false,"This Wukong control is unavailable in the current game session." end
    if self.flags[action]~=nil then
        if enabled~=true then return false,"An explicit toggle state is required." end
        if action=="wukong_damage" then
            if self.flags[action] then return true,"Wukong control enabled." end
            if not self:_restore_damage() then return false,"Wukong damage restoration is pending." end
            local original=attr(self.lib,self.pawn,53)
            -- The game computes Atk153 = AtkBase103 * (10000+AtkMul53)/10000.
            local applied=(original+10000)*10-10000
            -- Retain the receipt even if verification fails after a write.
            self.damage={pawn=self.pawn,lib=self.lib,original=original,applied=applied}
            if not set(self.lib,self.pawn,53,applied) then self:_restore_damage(); return false,"Wukong attribute write could not be verified." end
        elseif not self:_refill(action) then return false,"Wukong attribute write could not be verified." end
        self.flags[action]=true
        return true,"Wukong control enabled."
    elseif action=="wukong_focus" then
        local ok=self:_refill(action)
        return ok,ok and "Focus filled to the current maximum." or "Wukong attribute write could not be verified."
    elseif progression[action] then
        local spec=progression[action]; amount=amount or spec[2]
        if not finite(amount) or amount%1~=0 or amount<1 or amount>spec[3] then return false,"Invalid Wukong progression amount." end
        -- These exact verbs/arguments are present in the game's own GM menu.
        -- No fallback or automatic retry: they are irreversible one-shot grants.
        local ok=call(self.gm,"RunScriptGM",spec[1].." "..string.format("%d",amount),self.controller)
        return ok,ok and "Progression command sent once; check the in-game total." or "Wukong progression command failed."
    end
    return false,"Unknown Wukong trainer control."
end
function T:tick(reuse_refresh)
    if not reuse_refresh then self:refresh() end
    if self.damage and not self.flags.wukong_damage then self:_restore_damage() end
    if not self.ready then return end
    for _,action in ipairs(toggles) do
        if self.flags[action] and action~="wukong_damage" then
            if not self.available[action] or not self:_refill(action) then self.flags[action]=false end
        end
    end
    -- A game-driven attack recalculation replaces our override; don't multiply
    -- it again (or accidentally compound the multiplier).
    if self.flags.wukong_damage and self.damage then
        local current=attr(self.lib,self.pawn,53)
        if current==nil then
            self.flags.wukong_damage=false
        elseif math.abs(current-self.damage.applied)>math.max(0.01,self.damage.applied*0.00001) then
            self.flags.wukong_damage=false; self.damage=nil
        end
    end
end
function M.new()
    local self=setmetatable({flags={},identity="",available={}},T)
    for _,key in ipairs(toggles) do self.flags[key]=false end
    return self
end
return M
