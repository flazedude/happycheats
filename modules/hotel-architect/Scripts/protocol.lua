-- Session-bound, allowlisted data; never evaluated as code.
local P={}
P.toggles={ha_no_needs=true,ha_always_noon=true,ha_furniture_condition=true,ha_disable_aging=true}
-- Numeric editor limits are part of the client/runtime contract. Normalized
-- needs and condition are displayed as percentages; bindings divide by 100.
P.numbers={
    ha_game_speed={0,3,true},ha_days_passed={0,100000,true},ha_money={0,1000000000,true},
    ha_stars={0,5,true},ha_day_number={0,100000,true},ha_hour={0,23,true},ha_minute={0,59,true},
    ha_total_hours={0,1000000,false},ha_restroom_need={0,100,false},ha_mood={0,100,false},
    ha_health={0,100,false},ha_xp={0,989,true},ha_level={1,10,true},
    ha_retirement_days={0,100000,true},ha_energy={0,100,false},ha_room_value_multiplier={0,100,false},
    ha_cleanliness={0,100,false},ha_attractiveness={0,1000000,true},
    ha_negative_attractiveness={0,1000000,true},ha_condition={0,100,false},
}
function P.valid_amount(action,amount)
    local spec=P.numbers[action]
    return spec~=nil and type(amount)=="number" and amount==amount and amount>=spec[1] and amount<=spec[2]
        and (not spec[3] or amount%1==0)
end
function P.parse(text,session,now)
    if type(text)~="string" or #text>2048 then return nil,"Invalid trainer command" end
    local fields={}
    for line in (text.."\n"):gmatch("([^\n]*)\n") do
        line=line:gsub("\r$","")
        if line~="" then
            local key,value=line:match("^([a-z_]+)=([a-zA-Z0-9_.%-]+)$")
            if not key or fields[key] then return nil,"Invalid trainer command" end
            fields[key]=value
        end
    end
    local allowed={schema_version=true,session_id=true,request_id=true,action=true,enabled=true,created_utc=true,amount=true}
    for key in pairs(fields) do if not allowed[key] then return nil,"Unknown command field" end end
    if fields.schema_version~="1" or fields.session_id~=session then return nil,"Game session changed" end
    if not fields.request_id or #fields.request_id~=32 or not fields.request_id:match("^[a-f0-9]+$") then return nil,"Invalid request identity" end
    local action=fields.action
    if not P.toggles[action] and not P.numbers[action] and action~="disable_all" then return nil,"Unknown trainer control" end
    if not fields.created_utc or not fields.created_utc:match("^%d+$") then return nil,"Invalid command time" end
    local created=tonumber(fields.created_utc)
    if not created or created<=0 or created%1~=0 or now-created>8 or created>now+2 then return nil,"Trainer command expired" end
    if fields.enabled~="0" and fields.enabled~="1" then return nil,"Invalid toggle state" end
    if not P.toggles[action] and fields.enabled~="0" then return nil,"One-shot edit cannot be enabled" end
    if P.numbers[action] then
        if not fields.amount or not (fields.amount:match("^%d+$") or fields.amount:match("^%d+%.%d+$")) then return nil,"An explicit numeric value is required" end
        local amount=tonumber(fields.amount)
        if not P.valid_amount(action,amount) then return nil,"Hotel Architect value is outside its supported range" end
        fields.amount=amount
    elseif fields.amount then return nil,"Unexpected numeric value" end
    fields.enabled=fields.enabled=="1"
    return fields
end
local function quote(value)
    return '"'..tostring(value):gsub('[%z\1-\31\\"]',function(c)
        local escapes={['"']='\\"',['\\']='\\\\',['\n']='\\n',['\r']='\\r',['\t']='\\t'}
        return escapes[c] or string.format('\\u%04x',c:byte())
    end)..'"'
end
function P.json(value)
    local kind=type(value)
    if kind=="string" then return quote(value) end
    if kind=="boolean" then return value and "true" or "false" end
    if kind=="number" then return value==value and math.abs(value)<math.huge and tostring(value) or "null" end
    if kind~="table" then return "null" end
    local entries={}
    for key,item in pairs(value) do entries[#entries+1]=quote(key)..":"..P.json(item) end
    table.sort(entries)
    return "{"..table.concat(entries,",").."}"
end
return P
