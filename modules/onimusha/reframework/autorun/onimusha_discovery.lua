-- Onimusha: Way of the Sword -- metadata discovery, not a functioning trainer.
-- Only REFramework metadata is read: no instances, hooks, or game method calls.
-- API references: https://refdocs.praydog.com/api/types/RETypeDefinition.html
-- https://refdocs.praydog.com/api/types/REMethodDefinition.html
-- https://refdocs.praydog.com/api/types/REField.html
-- https://refdocs.praydog.com/api/json.html
-- Upstream src/mods/bindings/Sdk.cpp exposes get_types_inheriting_from_this;
-- it does not expose a Lua sdk.get_tdb or sdk.get_type_definitions function.

local OUTPUT_FILE = "onimusha_discovery.json" -- Relative to reframework/data.
local TYPES_PER_FRAME = 30
local START_DELAY_FRAMES = 120
local MAX_ERRORS = 100
local KEYWORDS = {
    "player", "health", "hitpoint", "life", "damage", "parry", "guard",
    "stun", "break", "stamina", "balance", "character", "battle", "issen",
    "chara", "rikido", "deflect",
}

local state = {
    phase = "waiting", status = "Waiting briefly for REFramework initialization.",
    frames = 0, next_type = 1, total = 0, inspected = 0, app_types = 0,
    matched = 0, definitions = nil, errors = {}, error_count = 0,
}

local report = {
    schema_version = 1,
    purpose = "Read-only metadata discovery; requested trainer features are unavailable.",
    scope = "app namespace types whose full names match the configured keywords",
    enumeration = "System.Object:get_types_inheriting_from_this()",
    coverage_note = "Only System.Object descendants exposed by REFramework are enumerated. " ..
        "Native types outside that hierarchy may be absent. get_methods() filters stubs/null methods.",
    performance_note = "Initial descendant enumeration is one native TDB scan; " ..
        "subsequent metadata collection is limited to 30 types per frame.",
    keywords = KEYWORDS,
    requested_features = {
        unlimited_health = "unavailable: game bindings and behavior not verified",
        auto_parry = "unavailable: game bindings and behavior not verified",
        one_hit_stun = "unavailable: enemy stamina/break behavior not verified",
    },
    types = {},
}

local function clean_error(value)
    -- Lua/C++ exception messages can include pointers; exclude them from exports.
    return tostring(value):gsub("0[xX]%x+", "<address omitted>")
        :gsub("userdata:%s*%x+", "<userdata omitted>"):sub(1, 500)
end

local function record_error(context, detail)
    state.error_count = state.error_count + 1
    if #state.errors < MAX_ERRORS then
        state.errors[#state.errors + 1] = {context = context, detail = clean_error(detail)}
    end
end

local function read_metadata(object, member, context)
    local ok, value = pcall(function() return object[member](object) end)
    if not ok then
        record_error(context .. "." .. member, value)
        return nil
    end
    return value
end

local function type_name(definition, context)
    if definition == nil then return nil end
    return read_metadata(definition, "get_full_name", context)
end

local function collect_method(method, owner)
    local name = read_metadata(method, "get_name", owner .. ".method")
    if name == nil then return nil end
    local context = owner .. "." .. name
    local param_types = read_metadata(method, "get_param_types", context) or {}
    local param_names = read_metadata(method, "get_param_names", context) or {}
    local num_params = read_metadata(method, "get_num_params", context)
    local return_type = type_name(read_metadata(method, "get_return_type", context), context)
    local parameters, signature_types = {}, {}
    local complete = num_params ~= nil and num_params == #param_types and return_type ~= nil
    for index = 1, math.max(num_params or 0, #param_types) do
        local parameter_type = type_name(param_types[index], context .. ".parameter")
        parameters[#parameters + 1] = {
            index = index, name = param_names[index], type = parameter_type,
        }
        signature_types[#signature_types + 1] = parameter_type or "<unknown>"
        if parameter_type == nil then complete = false end
    end
    return {
        name = name,
        -- Exact overload lookup format from metadata, without parameter names.
        signature = name .. "(" .. table.concat(signature_types, ", ") .. ")",
        signature_complete = complete,
        return_type = return_type,
        is_static = read_metadata(method, "is_static", context),
        declaring_type = type_name(read_metadata(method, "get_declaring_type", context), context),
        parameter_count = num_params, parameters = parameters,
    }
end

local function collect_field(field, owner)
    local name = read_metadata(field, "get_name", owner .. ".field")
    if name == nil then return nil end
    local context = owner .. "." .. name
    return {
        name = name,
        type = type_name(read_metadata(field, "get_type", context), context),
        declaring_type = type_name(read_metadata(field, "get_declaring_type", context), context),
        is_static = read_metadata(field, "is_static", context),
        is_literal = read_metadata(field, "is_literal", context),
        -- Relative metadata offsets, never process memory addresses or values.
        offset_from_base = read_metadata(field, "get_offset_from_base", context),
        offset_from_fieldptr = read_metadata(field, "get_offset_from_fieldptr", context),
    }
end

local function collect_type(definition, name, matched_keyword)
    local before_errors = state.error_count
    local parent = read_metadata(definition, "get_parent_type", name)
    local parents, seen = {}, {}
    while parent ~= nil and #parents < 64 do
        local parent_name = type_name(parent, name .. ".parent")
        if parent_name == nil then break end
        if seen[parent_name] then
            record_error(name, "Cycle detected in parent metadata")
            break
        end
        seen[parent_name] = true
        parents[#parents + 1] = parent_name
        parent = read_metadata(parent, "get_parent_type", name .. ".parent")
    end
    if parent ~= nil and #parents >= 64 then
        record_error(name, "Parent chain exceeds 64 metadata entries")
    end
    local result = {
        full_name = name, matched_keyword = matched_keyword,
        parent = parents[1], parents = parents, methods = {}, fields = {},
        is_value_type = read_metadata(definition, "is_value_type", name),
        is_enum = read_metadata(definition, "is_enum", name),
    }
    local methods = read_metadata(definition, "get_methods", name) or {}
    for _, method in ipairs(methods) do
        local ok, metadata = pcall(collect_method, method, name)
        if ok and metadata ~= nil then
            result.methods[#result.methods + 1] = metadata
        elseif not ok then
            record_error(name .. ".method", metadata)
        end
    end
    local fields = read_metadata(definition, "get_fields", name) or {}
    for _, field in ipairs(fields) do
        local ok, metadata = pcall(collect_field, field, name)
        if ok and metadata ~= nil then
            result.fields[#result.fields + 1] = metadata
        elseif not ok then
            record_error(name .. ".field", metadata)
        end
    end
    result.metadata_complete = before_errors == state.error_count
    return result
end

local function export_report()
    state.phase = "exporting"
    report.counts = {
        enumerated_types = state.total, inspected_types = state.inspected,
        app_types = state.app_types, matched_types = state.matched,
        metadata_errors = state.error_count, recorded_errors = #state.errors,
    }
    report.errors = state.errors
    report.collection_complete = state.inspected == state.total and state.error_count == 0
    local ok, result = pcall(json.dump_file, OUTPUT_FILE, report, 2)
    if ok and result == true then
        state.phase = "complete"
        state.status = "Exported " .. state.matched .. " matching types."
        if state.error_count > 0 then
            state.status = state.status .. " Partial metadata: " .. state.error_count .. " errors."
        elseif state.matched == 0 then
            state.status = state.status .. " No matching app types; inspect framework compatibility."
        end
        state.definitions = nil
    else
        state.phase = "export_failed"
        state.status = "Export failed: " .. (ok and "json.dump_file returned false." or clean_error(result))
    end
end

local function initialize()
    local ok, game = pcall(function() return reframework:get_game_name() end)
    report.game_identity = ok and game or "unavailable"
    if not ok or game ~= "onimusha_wots" then
        state.phase = "failed"
        state.status = "Expected REFramework game identity onimusha_wots; got " .. tostring(report.game_identity)
        return
    end
    report.reframework_version = read_metadata(reframework, "get_version_string", "reframework")
    report.reframework_commit = read_metadata(reframework, "get_commit_hash", "reframework")
    report.tdb_version = sdk.get_tdb_version()
    local root = sdk.find_type_definition("System.Object")
    if root == nil then error("System.Object metadata is unavailable; update REFramework and retry.") end
    -- This returns all descendants in one native operation, not an incremental TDB API.
    state.definitions = root:get_types_inheriting_from_this()
    if state.definitions == nil then error("REFramework returned no type enumeration.") end
    state.total = #state.definitions
    state.phase = "collecting"
    state.status = "Reading type metadata over successive frames."
end

local function process_frame()
    if state.phase == "waiting" then
        state.frames = state.frames + 1
        if state.frames >= START_DELAY_FRAMES then initialize() end
        return
    end
    if state.phase ~= "collecting" then return end
    local last = math.min(state.total, state.next_type + TYPES_PER_FRAME - 1)
    for index = state.next_type, last do
        local definition = state.definitions[index]
        local name = type_name(definition, "type[" .. index .. "]")
        if type(name) == "string" and name:sub(1, 4) == "app." then
            state.app_types = state.app_types + 1
            local lower_name = name:lower()
            for _, keyword in ipairs(KEYWORDS) do
                if lower_name:find(keyword, 1, true) then
                    local ok, metadata = pcall(collect_type, definition, name, keyword)
                    if ok then
                        report.types[#report.types + 1] = metadata
                        state.matched = state.matched + 1
                    else
                        record_error(name, metadata)
                    end
                    break
                end
            end
        end
        state.inspected = index
    end
    state.next_type = last + 1
    if state.next_type > state.total then export_report() end
end

re.on_frame(function()
    local ok, detail = pcall(process_frame)
    if not ok then
        state.phase = "failed"
        state.status = "Discovery stopped: " .. clean_error(detail)
        record_error("discovery", detail)
    end
end)

re.on_draw_ui(function()
    if not imgui.tree_node("Onimusha - Metadata Discovery") then return end
    imgui.text("Metadata exporter. Gameplay controls are in Onimusha Trainer.")
    imgui.separator()
    imgui.text(state.status)
    imgui.text("Types inspected: " .. state.inspected .. " / " .. state.total)
    imgui.text("Matching types: " .. state.matched .. " | Metadata errors: " .. state.error_count)
    imgui.text("Output: reframework/data/" .. OUTPUT_FILE)
    imgui.text("Metadata only. No gameplay changes or object values are collected.")
    if state.phase == "export_failed" and imgui.button("Retry JSON export") then
        export_report()
    end
    imgui.tree_pop()
end)
