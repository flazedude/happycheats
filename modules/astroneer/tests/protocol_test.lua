local P = dofile("modules/astroneer/Scripts/protocol.lua")
local function command(extra)
    return "schema_version=1\nsession_id=test-session\nrequest_id=0123456789abcdef0123456789abcdef\naction=add_bytes\nenabled=0\ncreated_utc=100\n" .. (extra or "")
end
assert(P.parse(command(), "test-session", 100).action == "add_bytes")
assert(not P.parse(command(), "other-session", 100))
assert(not P.parse(command(), "test-session", 109))
assert(not P.parse(command(), "test-session", 97))
assert(not P.parse(command("action=add_bytes\n"), "test-session", 100))
assert(not P.parse(command("surprise=1\n"), "test-session", 100))
assert(not P.parse(command():gsub("enabled=0", "enabled=false"), "test-session", 100))
assert(not P.parse(command():gsub("action=add_bytes", "action=execute"), "test-session", 100))
assert(not P.parse(string.rep("x", 2049), "test-session", 100))
assert(P.json({enabled=false, message='x\n"\\'}) == '{"enabled":false,"message":"x\\n\\"\\\\"}')
assert(P.json({}) == '{}')
assert(P.json(0/0) == 'null')
local function selection(multiplier, enabled, action)
    local text = command():gsub("action=add_bytes", "action=" .. (action or "set_collection_multiplier"))
        :gsub("enabled=0", "enabled=" .. (enabled or "0"))
    return text .. (multiplier and "multiplier=" .. multiplier .. "\n" or "")
end
for _, multiplier in ipairs({ "10", "20", "100" }) do
    local parsed = assert(P.parse(selection(multiplier), "test-session", 100))
    assert(parsed.multiplier == tonumber(multiplier))
    assert(parsed.enabled == false)
    assert(not P.parse(selection(multiplier, "1"), "test-session", 100))
end
assert(not P.parse(selection(nil), "test-session", 100))
assert(not P.parse(selection("20", "1"), "test-session", 100))
for _, invalid in ipairs({ "0", "1", "15", "30", "99", "101", "-10", "10.0", "020", "2e1", "100.0", "0100", "1e2", "true", "NaN" }) do
    assert(not P.parse(selection(invalid), "test-session", 100))
end
assert(not P.parse(selection("20") .. "multiplier=10\n", "test-session", 100))
assert(not P.parse(selection("100") .. "multiplier=100\n", "test-session", 100))
assert(not P.parse(command("multiplier=20\n"), "test-session", 100))
assert(not P.parse(command("multiplier=100\n"), "test-session", 100))
assert(not P.parse(command("multiplier=10\n"):gsub("action=add_bytes", "action=resource_collection_10x"), "test-session", 100))
assert(not P.parse(command("multiplier=100\n"):gsub("action=add_bytes", "action=resource_collection_10x"), "test-session", 100))
for _, multiplier in ipairs({ "3", "5", "10" }) do
    local text = selection(multiplier, "0", "set_run_speed_multiplier")
    local parsed = assert(P.parse(text, "test-session", 100))
    assert(parsed.multiplier == tonumber(multiplier) and parsed.enabled == false)
    assert(not P.parse(selection(multiplier, "1", "set_run_speed_multiplier"), "test-session", 100))
    assert(not P.parse(text, "changed-session", 100))
    assert(not P.parse(text, "test-session", 109))
end
for _, invalid in ipairs({ "0", "1", "2", "4", "6", "9", "11", "20", "100", "03", "3.0", "+3", "3 ", " 3", "1e1", "NaN" }) do
    assert(not P.parse(selection(invalid, "0", "set_run_speed_multiplier"), "test-session", 100))
end
assert(not P.parse(selection(nil, "0", "set_run_speed_multiplier"), "test-session", 100))
assert(not P.parse(selection("3", "0", "set_run_speed_multiplier") .. "multiplier=5\n", "test-session", 100))
assert(not P.parse(selection("3"), "test-session", 100))
assert(not P.parse(selection("5"), "test-session", 100))
for _, action in ipairs({ "invulnerability", "player_run_speed" }) do
    for _, enabled in ipairs({ "0", "1" }) do
        local text = command():gsub("action=add_bytes", "action=" .. action):gsub("enabled=0", "enabled=" .. enabled)
        local parsed = assert(P.parse(text, "test-session", 100))
        assert(parsed.action == action and parsed.enabled == (enabled == "1") and parsed.multiplier == nil)
        assert(not P.parse(text .. "multiplier=3\n", "test-session", 100))
        assert(not P.parse(text, "previous-session", 100))
    end
end
print("PASS Astroneer protocol: session, expiry, malformed/duplicate fields, scoped collection/run-speed choices, new toggles, escaping")
