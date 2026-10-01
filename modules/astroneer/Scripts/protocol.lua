-- HappyCheats bridge protocol. No command is evaluated as Lua or shell code.
local P = {}
local actions = {resource_collection_10x=true, set_collection_multiplier=true, unlimited_oxygen=true, unlimited_power=true,
    unlimited_soil=true, add_bytes=true, disable_all=true}
function P.parse(text, session, now)
    if type(text) ~= "string" or #text > 2048 then return nil, "Invalid trainer command." end
    local fields = {}
    for line in (text .. "\n"):gmatch("([^\n]*)\n") do
        line = line:gsub("\r$", "")
        if line ~= "" then
            local k,v = line:match("^([a-z_]+)=([a-zA-Z0-9_.%-]+)$")
            if not k or fields[k] then return nil, "Invalid trainer command." end
            fields[k] = v
        end
    end
    local allowed = {schema_version=true,session_id=true,request_id=true,action=true,enabled=true,created_utc=true,multiplier=true}
    for k in pairs(fields) do if not allowed[k] then return nil, "Invalid trainer command." end end
    if fields.schema_version ~= "1" or fields.session_id ~= session then return nil, "Game session changed." end
    if not fields.request_id or not fields.request_id:match("^[a-f0-9]+$") or #fields.request_id ~= 32 then
        return nil, "Invalid trainer command."
    end
    if not actions[fields.action] then return nil, "Unknown trainer control." end
    local created = tonumber(fields.created_utc)
    if not created or created % 1 ~= 0 or now - created > 8 or created > now + 2 then
        return nil, "Trainer command expired."
    end
    if fields.enabled ~= "0" and fields.enabled ~= "1" then return nil, "Invalid trainer toggle state." end
    if fields.action == "set_collection_multiplier" then
        if fields.enabled ~= "0" or (fields.multiplier ~= "10" and fields.multiplier ~= "20") then
            return nil, "Select a collection multiplier of 10 or 20 without enabling the toggle."
        end
        fields.multiplier = tonumber(fields.multiplier)
    elseif fields.multiplier ~= nil then
        return nil, "Multiplier is only valid for the collection multiplier control."
    end
    fields.enabled = fields.enabled == "1"
    return fields
end
local function quote(s)
    return '"' .. tostring(s):gsub('[%z\1-\31\\"]', function(c)
        local escape = {['"']='\\"', ['\\']='\\\\', ['\n']='\\n', ['\r']='\\r', ['\t']='\\t'}
        return escape[c] or string.format('\\u%04x', c:byte())
    end) .. '"'
end
function P.json(value)
    local t = type(value)
    if t == "string" then return quote(value) end
    if t == "boolean" then return value and "true" or "false" end
    if t == "number" then
        if value ~= value or value == math.huge or value == -math.huge then return "null" end
        return tostring(value)
    end
    if t ~= "table" then return "null" end
    local entries = {}
    for k,v in pairs(value) do entries[#entries+1] = quote(k) .. ':' .. P.json(v) end
    table.sort(entries)
    return '{' .. table.concat(entries, ',') .. '}'
end
return P
