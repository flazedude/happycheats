-- Allowlisted, expiring session-bound data. Never evaluated as code.
local P = {}
local actions={wukong_health=true,wukong_mana=true,wukong_stamina=true,wukong_damage=true,wukong_gourd=true,wukong_focus=true,wukong_xp=true,wukong_sparks=true,wukong_will=true,disable_all=true}
local oneshot={wukong_focus=true,wukong_xp=true,wukong_sparks=true,wukong_will=true,disable_all=true}
local grants={wukong_xp={1000,1000000},wukong_sparks={1,100},wukong_will={1000,1000000}}
function P.parse(text, session, now)
    if type(text) ~= "string" or #text > 2048 then return nil, "Invalid trainer command" end
    local fields = {}
    for line in (text .. "\n"):gmatch("([^\n]*)\n") do
        line = line:gsub("\r$", "")
        if line ~= "" then
            local key, value = line:match("^([a-z_]+)=([a-zA-Z0-9_.%-]+)$")
            if not key or fields[key] then return nil, "Invalid trainer command" end
            fields[key] = value
        end
    end
    local allowed = {schema_version=true, session_id=true, request_id=true, action=true, enabled=true, created_utc=true,amount=true}
    for key in pairs(fields) do if not allowed[key] then return nil, "Unknown command field" end end
    if fields.schema_version ~= "1" or fields.session_id ~= session then return nil, "Game session changed" end
    if not fields.request_id or #fields.request_id ~= 32 or not fields.request_id:match("^[a-f0-9]+$") then return nil, "Invalid request identity" end
    if not actions[fields.action] then return nil, "Unknown trainer control" end
    if not fields.created_utc or not fields.created_utc:match("^%d+$") then return nil, "Invalid command time" end
    local created = tonumber(fields.created_utc)
    if not created or created <= 0 or created % 1 ~= 0 or now - created > 8 or created > now + 2 then return nil, "Trainer command expired" end
    if fields.enabled ~= "0" and fields.enabled ~= "1" then return nil, "Invalid toggle state" end
    if oneshot[fields.action] and fields.enabled ~= "0" then return nil, "One-shot action cannot be enabled" end
    local grant=grants[fields.action]
    if grant then
        local amount=fields.amount and tonumber(fields.amount) or grant[1]
        if fields.amount and not fields.amount:match("^%d+$") then return nil,"Invalid Wukong progression amount" end
        if not amount or amount%1~=0 or amount<1 or amount>grant[2] then return nil,"Invalid Wukong progression amount" end
        fields.amount=amount
    elseif fields.amount then return nil,"Unexpected progression amount" end
    fields.enabled = fields.enabled == "1"
    return fields
end
local function quote(value)
    return '"' .. tostring(value):gsub('[%z\1-\31\\"]', function(c)
        local escapes = {['"']='\\"', ['\\']='\\\\', ['\n']='\\n', ['\r']='\\r', ['\t']='\\t'}
        return escapes[c] or string.format('\\u%04x', c:byte())
    end) .. '"'
end
function P.json(value)
    local kind = type(value)
    if kind == "string" then return quote(value) end
    if kind == "boolean" then return value and "true" or "false" end
    if kind == "number" then return value == value and math.abs(value) < math.huge and tostring(value) or "null" end
    if kind ~= "table" then return "null" end
    local entries = {}
    for key, item in pairs(value) do entries[#entries+1] = quote(key) .. ":" .. P.json(item) end
    table.sort(entries)
    return "{" .. table.concat(entries, ",") .. "}"
end
return P
