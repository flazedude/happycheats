-- Read-only request/response probe. Files are relative to reframework/data.
-- SDK APIs verified in refdocs.praydog.com and upstream bindings/Sdk.cpp.
-- The sole game method called is the verified zero-argument player getter.
local REQUEST, RESPONSE = "onimusha_probe_request.json", "onimusha_probe_response.json"
local frames, last_id, automatic_done = 0, nil, false
local SELECT = {"context", "health", "character", "entity", "owner", "param",
    "guard", "justframe", "rikido", "damage", "iscontrol"}
local function clean(value)
    return tostring(value):gsub("0[xX]%x+", "<address omitted>")
        :gsub("userdata:%s*%x+", "<userdata omitted>"):sub(1, 500)
end
local function attempt(report, context, fn)
    local ok, result = pcall(fn)
    if ok then return result end
    if #report.errors < 100 then
        report.errors[#report.errors + 1] = {context = context, detail = clean(result)}
    end
end
local function name(def) return def and def:get_full_name() or "<unknown>" end
local function scalar(value)
    local kind = type(value)
    if kind == "nil" then return {is_null = true} end
    if kind == "boolean" then return value end
    if kind == "number" then
        if value == value and value ~= math.huge and value ~= -math.huge then return value end
        return {omitted = "non-finite number"}
    end
    if kind == "string" then return clean(value) end
    return {omitted = "non-primitive value"}
end
local function metadata(report, typename)
    local def = sdk.find_type_definition(typename)
    if not def then error("Type not found: " .. typename) end
    local out = {full_name = name(def), parent = name(def:get_parent_type()),
        is_enum = def:is_enum(), methods = {}, fields = {}}
    for _, method in ipairs(def:get_methods() or {}) do
        attempt(report, typename .. ".method", function()
            local types, params, names = {}, {}, method:get_param_names() or {}
            for i, param in ipairs(method:get_param_types() or {}) do
                types[i] = name(param); params[i] = {type = types[i], name = names[i]}
            end
            out.methods[#out.methods + 1] = {name = method:get_name(),
                signature = method:get_name() .. "(" .. table.concat(types, ", ") .. ")",
                return_type = name(method:get_return_type()), parameters = params,
                parameter_count = method:get_num_params(), is_static = method:is_static(),
                declaring_type = name(method:get_declaring_type())}
        end)
    end
    for _, field in ipairs(def:get_fields() or {}) do
        attempt(report, typename .. ".field", function()
            local entry = {name = field:get_name(), type = name(field:get_type()),
                is_static = field:is_static(), is_literal = field:is_literal(),
                declaring_type = name(field:get_declaring_type())}
            if out.is_enum == true and entry.is_literal == true then
                entry.value = scalar(field:get_data(nil))
            end
            out.fields[#out.fields + 1] = entry
        end)
    end
    return out
end
local function selected(fieldname)
    local lower = fieldname:lower()
    for _, word in ipairs(SELECT) do if lower:find(word, 1, true) then return true end end
    return false
end
local function excluded(def)
    if def:is_array() then return "array: metadata only" end
    if def:is_pointer() or name(def) == "System.IntPtr" or name(def) == "System.UIntPtr" then
        return "pointer omitted"
    end
end
local function instance_fields(def)
    local fields, seen, levels = {}, {}, 0
    while def and levels < 32 do
        for _, field in ipairs(def:get_fields() or {}) do
            if not seen[field:get_name()] then
                fields[#fields + 1] = field
                seen[field:get_name()] = true
            end
        end
        def = def:get_parent_type(); levels = levels + 1
    end
    return fields
end
local function snapshot(report, object, depth, seen)
    if type(object) ~= "userdata" then return scalar(object) end
    if not sdk.is_managed_object(object) then return {omitted = "not a managed reference"} end
    local def = object:get_type_definition()
    local out = {type = name(def)}
    local skip = excluded(def)
    if skip then out.omitted = skip; return out end
    if seen[object] then out.omitted = "already visited"; return out end
    if depth >= 4 or report.node_count >= 120 then out.omitted = "snapshot limit"; return out end
    seen[object] = true; report.node_count = report.node_count + 1; out.fields = {}
    for _, field in ipairs(instance_fields(def)) do
        attempt(report, out.type .. ".field", function()
            if field:is_static() then return end
            local fieldname, fieldtype = field:get_name(), field:get_type()
            local entry = {name = fieldname, type = name(fieldtype)}
            entry.omitted = excluded(fieldtype)
            if not entry.omitted then
                if fieldtype:is_primitive() or fieldtype:is_enum() or name(fieldtype) == "System.String" then
                    entry.value = scalar(object:get_field(fieldname))
                elseif selected(fieldname) then
                    entry.value = snapshot(report, object:get_field(fieldname), depth + 1, seen)
                else entry.omitted = "reference outside selection" end
            end
            out.fields[#out.fields + 1] = entry
        end)
    end
    return out
end
local function player_snapshot(report, paths)
    local manager = sdk.get_managed_singleton("app.PlayerManager")
    if not manager then return {ready = false, reason = "PlayerManager unavailable"} end
    local method = manager:get_type_definition():get_method("getControllingPlayer()")
    if not method or method:is_static() or method:get_num_params() ~= 0 or
        name(method:get_return_type()) ~= "app.cPlayerManageInfo" then
        error("Controlling-player getter metadata did not match the verified signature")
    end
    local player = manager:call("getControllingPlayer()")
    if not player then return {ready = false, reason = "No controlling player"} end
    local character = player:get_field("<Character>k__BackingField")
    local out = {ready = character ~= nil, paths = {}}
    if not out.ready then out.reason = "Controlling-player info exists, but no character is loaded" end
    -- Focus requests are evaluated first so the automatic graph cannot consume their budget.
    for i, path in ipairs(paths or {}) do
        if i > 16 then error("At most 16 field paths are allowed") end
        local focused = attempt(report, "player.paths[" .. i .. "]", function()
            if type(path) ~= "table" or #path > 4 then error("Field path must contain at most 4 names") end
            local current = player
            for _, fieldname in ipairs(path) do
                if type(fieldname) ~= "string" then error("Field path entries must be strings") end
                if not current or type(current) ~= "userdata" or not sdk.is_managed_object(current) then
                    error("Field path reached a null or non-managed value")
                end
                local field = current:get_type_definition():get_field(fieldname)
                if not field or field:is_static() then error("Field is missing or static: " .. fieldname) end
                if excluded(field:get_type()) then error("Field path reaches an array or pointer") end
                current = current:get_field(fieldname)
            end
            return {path = path, snapshot = snapshot(report, current, 0, {})}
        end)
        if focused then out.paths[#out.paths + 1] = focused end
    end
    out.manage_info = snapshot(report, player, 0, {})
    return out
end
local function respond(request)
    local report = {schema_version = 1, request_id = request.request_id,
        read_only = true, types = {}, errors = {}, node_count = 0}
    attempt(report, "request", function()
        if reframework:get_game_name() ~= "onimusha_wots" then error("Unexpected game identity") end
        if request.types ~= nil and type(request.types) ~= "table" then error("types must be an array") end
        if request.paths ~= nil and type(request.paths) ~= "table" then error("paths must be an array") end
        for i, typename in ipairs(request.types or {}) do
            if i > 64 then error("At most 64 explicit metadata types are allowed") end
            if type(typename) ~= "string" then error("Type names must be strings") end
            local result = attempt(report, clean(typename), function() return metadata(report, typename) end)
            if result then report.types[#report.types + 1] = result end
        end
        if request.snapshot ~= false then
            report.player = attempt(report, "player", function() return player_snapshot(report, request.paths) end)
        end
    end)
    report.complete = #report.errors == 0
    local ok, result = pcall(json.dump_file, RESPONSE, report, 2)
    if not ok or result ~= true then log.error("Onimusha probe export failed: " .. clean(result)); return false end
    return true
end
-- Execute the verified game getter and instance reads on the behavior thread.
re.on_application_entry("UpdateBehavior", function()
    frames = frames + 1
    if frames % 60 ~= 0 then return end
    local ok, request = pcall(json.load_file, REQUEST)
    if ok and type(request) == "table" and
        (type(request.request_id) == "string" or type(request.request_id) == "number") then
        if request.request_id ~= last_id and respond(request) then last_id = request.request_id end
        automatic_done = true
    elseif not automatic_done and frames >= 120 then
        automatic_done = respond({request_id = "automatic-ready", snapshot = true})
    end
end)
