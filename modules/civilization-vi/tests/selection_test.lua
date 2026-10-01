-- Run the actual C#-embedded InGame request extracted by run.py.
local select = assert(load("return " .. selection_expression))
local count = 0
local function eq(expected, label)
    count = count + 1
    local actual = select()
    assert(actual == expected, label .. ": " .. tostring(actual) .. " ~= " .. expected)
end
local function item(owner, id)
    return {GetOwner = function() return owner end, GetID = function() return id end}
end
local unit, city = item(0, 11), item(0, 7)
local function reset()
    Game = {GetLocalPlayer = function() return 0 end}
    GameConfiguration = {IsAnyMultiplayer = function() return false end}
    UI = {GetHeadSelectedUnit = function() return unit end, GetHeadSelectedCity = function() return city end}
end
reset()
eq("0|11|7|true", "Positive single-player evidence")
UI.GetHeadSelectedUnit = nil
eq("0|-1|7|true", "Missing unit selection leaves city and local player intact")
UI.GetHeadSelectedUnit = function() error("Unit selection unavailable") end
eq("0|-1|7|true", "Throwing unit selection leaves city and player intact")
reset()
UI.GetHeadSelectedCity = nil
eq("0|11|-1|true", "Missing city selection leaves unit and player intact")
UI.GetHeadSelectedCity = function() error("City selection unavailable") end
eq("0|11|-1|true", "Throwing city selection leaves unit and player intact")
UI = nil
eq("0|-1|-1|true", "Missing UI selections do not block player-level features")
reset()
UI.GetHeadSelectedUnit = function() return item(1, 11) end
eq("0|-1|7|true", "Foreign unit rejected independently")
UI.GetHeadSelectedCity = function() return item(1, 7) end
eq("0|-1|-1|true", "Foreign city rejected independently")
reset()
UI.GetHeadSelectedUnit = function() return {GetID = function() return 11 end} end
eq("0|-1|7|true", "UI object without ownership evidence is not trusted")
reset()
UI.GetHeadSelectedCity = function() return {GetOwner = function() return 0 end} end
eq("0|11|-1|true", "Selected object without an ID only removes that selection")
reset()
GameConfiguration = nil
eq("0|11|7|false", "Missing mode API retains player without asserting single-player")
GameConfiguration = {IsAnyMultiplayer = function() return nil end}
eq("0|11|7|false", "Nil mode result does not assert single-player")
GameConfiguration.IsAnyMultiplayer = function() error("Not exposed") end
eq("0|11|7|false", "Throwing mode API does not assert single-player")
reset()
GameConfiguration.IsAnyMultiplayer = function() return true end
eq("-1|-1|-1|false", "Explicit multiplayer excludes all identities")
reset()
GameConfiguration.IsHotseat = function() return true end
eq("-1|-1|-1|false", "Explicit hotseat overrides the general mode flag")
reset()
GameConfiguration.IsHotseat = function() error("Not exposed") end
eq("0|11|7|true", "Absent optional hotseat metadata does not erase positive single-player evidence")
reset()
Game.GetLocalPlayer = nil
eq("-1|-1|-1|false", "Missing local player never creates an identity")
Game.GetLocalPlayer = function() error("Not exposed") end
eq("-1|-1|-1|false", "Throwing local player API never creates an identity")
Game.GetLocalPlayer = function() return -1 end
eq("-1|-1|-1|false", "No local player discards all selected identities")
Game.GetLocalPlayer = function() return "0" end
eq("-1|-1|-1|false", "Non-numeric player ID rejected")
Game = nil
eq("-1|-1|-1|false", "Absent Game table rejected")
print("Civilization VI InGame selection: " .. count .. " offline assertions passed.")
