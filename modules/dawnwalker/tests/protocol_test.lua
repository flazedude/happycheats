local P = dofile("modules/dawnwalker/Scripts/protocol.lua")
local checks = 0
local function check(value, message) checks = checks + 1; assert(value, message) end
local function command(action, enabled, created)
    return "schema_version=1\nsession_id=test-session\nrequest_id=0123456789abcdef0123456789abcdef\naction=" .. (action or "dawnwalker_refill") ..
        "\nenabled=" .. (enabled or "0") .. "\ncreated_utc=" .. (created or "100") .. "\n"
end
for _, action in ipairs({"dawnwalker_health", "dawnwalker_stamina", "dawnwalker_blood", "dawnwalker_invulnerable", "dawnwalker_damage", "dawnwalker_invisible", "dawnwalker_fly", "dawnwalker_carry", "dawnwalker_money", "dawnwalker_items", "dawnwalker_auto_teleport"}) do
    for _, enabled in ipairs({"0", "1"}) do
        local parsed = assert(P.parse(command(action, enabled), "test-session", 100))
        check(parsed.action == action and parsed.enabled == (enabled == "1"), "Toggle contract")
    end
end
for _, action in ipairs({"dawnwalker_refill", "dawnwalker_xp", "dawnwalker_teleport", "disable_all"}) do
    check(P.parse(command(action), "test-session", 100) ~= nil, "One-shot accepted")
    check(P.parse(command(action, "1"), "test-session", 100) == nil, "One-shot cannot become enabled")
end
check(P.parse(command(), "other-session", 100) == nil, "Old session rejected")
check(P.parse(command(), "test-session", 108) ~= nil, "Age eight seconds accepted")
check(P.parse(command(), "test-session", 109) == nil, "Age nine seconds rejected")
check(P.parse(command(), "test-session", 98) ~= nil, "Two seconds future accepted")
check(P.parse(command(), "test-session", 97) == nil, "Three seconds future rejected")
for _, created in ipairs({"0", "-1", "100.0", "1e2", "NaN", "99999999999999999999999999999999999999999999"}) do
    check(P.parse(command(nil, nil, created), "test-session", 100) == nil, "Invalid timestamp " .. created)
end
for _, enabled in ipairs({"true", "false", "2", "-1", "01"}) do
    check(P.parse(command("dawnwalker_health", enabled), "test-session", 100) == nil, "Invalid toggle " .. enabled)
    check(P.parse(command("dawnwalker_auto_teleport", enabled), "test-session", 100) == nil, "Invalid automatic-travel toggle " .. enabled)
end
for _, key in ipairs({"schema_version", "session_id", "request_id", "action", "enabled", "created_utc"}) do
    local missing = command():gsub(key .. "=[^\n]*\n", "")
    check(P.parse(missing, "test-session", 100) == nil, "Missing field " .. key)
    local duplicate = command() .. key .. "=1\n"
    check(P.parse(duplicate, "test-session", 100) == nil, "Duplicate field " .. key)
end
for _, action in ipairs({"execute", "dawnwalker_unknown", "set_health", "console"}) do
    check(P.parse(command(action), "test-session", 100) == nil, "Unsupported control " .. action)
end
for _, injected in ipairs({"payload=1\n", "action=os.execute('x')\n", "bad key=1\n", "action=disable_all\0\n"}) do
    check(P.parse(command() .. injected, "test-session", 100) == nil, "No unknown/executable command data")
end
check(P.parse(command():gsub("\n", "\r\n"), "test-session", 100) ~= nil, "CRLF accepted")
check(P.parse(command() .. string.rep("\n", 2049), "test-session", 100) == nil, "Oversize input rejected")
check(P.parse(nil, "test-session", 100) == nil, "Non-string rejected")
check(P.json({enabled=false, message='x\n"\\\0'}) == '{"enabled":false,"message":"x\\n\\"\\\\\\u0000"}', "JSON escaping")
check(P.json({}) == '{}', "Empty object")
check(P.json(0/0) == 'null' and P.json(math.huge) == 'null' and P.json(-math.huge) == 'null', "JSON rejects nonfinite numbers")
print("Dawnwalker protocol: " .. checks .. " assertions passed")
