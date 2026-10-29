-- Allowlisted, expiring session-bound data. Never evaluated as code.
local P = {}
local actions = {pbz_health=true, pbz_stamina=true, pbz_damage=true, disable_all=true}
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
    local allowed = {schema_version=true, session_id=true, request_id=true, action=true, enabled=true, created_utc=true, amount=true}
    for key in pairs(fields) do if not allowed[key] then return nil, "Unknown command field" end end
    if fields.schema_version ~= "1" or fields.session_id ~= session then return nil, "Game session changed" end
    if not fields.request_id or #fields.request_id ~= 32 or not fields.request_id:match("^[a-f0-9]+$") then return nil, "Invalid request identity" end
    if not actions[fields.action] then return nil, "Unknown trainer control" end
    if not fields.created_utc or not fields.created_utc:match("^%d+$") then return nil, "Invalid command time" end
    local created = tonumber(fields.created_utc)
    if not created or created <= 0 or created % 1 ~= 0 or now - created > 8 or created > now + 2 then return nil, "Trainer command expired" end
    if fields.enabled ~= "0" and fields.enabled ~= "1" then return nil, "Invalid toggle state" end
    if (fields.action == "pbz_damage" or fields.action == "disable_all") and fields.enabled ~= "0" then return nil, "Action cannot be enabled as a toggle" end
    if fields.action == "pbz_damage" then
        local amount = fields.amount and tonumber(fields.amount) or 2
        if fields.amount and not fields.amount:match("^%d+$") then return nil, "Invalid damage multiplier" end
        if not amount or amount % 1 ~= 0 or amount < 1 or amount > 20 then return nil, "Invalid damage multiplier" end
        fields.amount = amount
    elseif fields.amount then return nil, "Unexpected multiplier" end
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
