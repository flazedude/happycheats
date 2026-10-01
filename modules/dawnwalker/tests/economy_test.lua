-- Run the real extension against the inspected reflected contracts. No game or
-- process-memory access is used by these ownership/restoration regression tests.
local checks, serial = 0, 0
local function equal(actual, expected, label)
    checks = checks + 1
    assert(actual == expected, (label or "Mismatch") .. ": " .. tostring(actual) .. " ~= " .. tostring(expected))
end
local function obj(name)
    serial = serial + 1
    return { valid = true, id = name .. serial }
end
local function array(values, size)
    -- Pinned UE4SS UFunction-return arrays are ordinary 1-based Lua tables.
    -- The entries expose get(), which returns a field-readable UScriptStruct.
    local result = {}
    for index, item in ipairs(values) do
        result[index] = setmetatable({ get = function()
            if item.decode_error then error("Native struct cannot be read") end
            return setmetatable({}, { __index = function(_, key)
                if key == "ItemDataAsset" or key == "Quantity" or key == "Level" then return item[key] end
                error("No reflected struct field " .. key)
            end })
        end }, { __index = function() error("LocalUnrealParam must be unwrapped") end })
    end
    if size then setmetatable(result, { __len = function() return size end }) end
    return result
end
local function fixture()
    local f = { writes = {}, calls = {}, missing = {}, money = 575, xp = 500, reward = 200, level = 2, rows = {}, lookup = 0,
        handles = {}, next_handle = 0, registrations = 0 }
    f.character, f.inventory, f.invsub, f.dev, f.asc, f.library = obj("Player"), obj("Inventory"), obj("InvSubsystem"), obj("Development"), obj("ASC"), obj("Library")
    f.owner, f.current_inventory, f.sub_inventory = f.character, f.inventory, f.inventory
    f.inventory.WeightLimit = 75
    f.character.AbilitySystemComponent = f.asc
    f.dev.PlayerASC = { Get = function() return f.asc end }
    f.requirements, f.level_cap = { [2] = 200, [3] = 600, [4] = 800 }, 4
    function f.library:GetGameInstanceSubsystem(character, class)
        assert(character == f.character, "Subsystem requires the validated player context")
        if class.path == "/Script/DogwoodInventory.InventorySubsystem" then return f.invsub end
        if class.path == "/Script/DogwoodCharacterDevelopment.CharacterDevelopmentSubsystem" then return f.dev end
        error("Unexpected subsystem")
    end
    function f.character:GetInventoryComponent() return f.current_inventory end
    function f.inventory:GetOwner() return f.owner end
    function f.inventory:GetWeightLimit() return f.limit_readback or self.WeightLimit end
    function f.inventory:GetCurrencyAmount(currency)
        assert(currency == 0, "Coin is the only verified currency")
        return f.money
    end
    function f.inventory:AddCurrency(currency, amount)
        assert(currency == 0 and amount > 0 and amount <= 999999 and amount % 1 == 0, "Bounded positive coin delta")
        f.writes[#f.writes + 1] = { "money", amount }
        if not f.money_noop then f.money = f.money + amount end
        return true
    end
    function f.invsub:GetPlayerInventoryComponent() return f.sub_inventory end
    function f.inventory:GetCurrentItems() return f.array_override or array(f.entries, f.array_size) end
    function f.inventory:GetHandleForAssetInInventory(asset)
        if f.empty_handle then return {} end -- Old behavior: no reflected fields.
        return { HappyCheatsItemHandleIndex = f.handle_index or asset.handle_index }
    end
    function f.invsub:GetAssetForItem(handle, out_level)
        assert(type(out_level) == "table" and next(out_level) == nil, "Byte OUT parameter requires a fresh table")
        out_level.OutLevel = 1
        return f.roundtrip_asset or f.handles[handle.HappyCheatsItemHandleIndex]
    end
    function f.invsub:IsQuestItem(handle)
        local asset = assert(f.handles[handle.HappyCheatsItemHandleIndex], "Must preserve opaque handle index")
        if f.quest_error == true or f.quest_error == asset then error("quest classifier unavailable") end
        return asset.quest == true
    end
    function f.dev:GetCurrentLevel() return f.level end
    function f.dev:GetCurrentXP() return f.xp end
    function f.dev:GetNextLevel() return math.min(f.level + 1, f.level_cap) end
    function f.dev:IsAnyCharacterDevelopmentModeEnabled() return f.progression ~= false end
    function f.dev:GetCurrentLevelXPRequirement(level)
        assert(type(level) == "number" and level % 1 == 0, "Explicit integer level required")
        return f.requirements[level] or 0 -- Proven native missing-map guard.
    end
    function f.dev:ForceLevelUp()
        local threshold = assert(f.requirements[f.level + 1], "Must never use a missing level cache entry")
        f.writes[#f.writes + 1] = { "xp", threshold - f.xp }
        if not f.xp_capped then
            f.xp = threshold
            while f.level < f.level_cap and f.requirements[f.level + 1] and f.xp >= f.requirements[f.level + 1] do
                f.xp = f.xp - f.requirements[f.level + 1]
                f.level = f.level + 1
            end
        end
        if f.after_xp then f.after_xp() end
        if f.force_throw then error("Native result uncertain") end
    end
    function f.dev:GetXPAmountByRewardType() error("Unsafe reward-map getter must never be called") end
    function f.dev:AddQuestXP() error("Unsafe quest reward-map mutation must never be called") end
    function f.asset(name, single, consumable, quest)
        local value = obj(name)
        value.handle_index = f.next_handle
        f.handles[f.next_handle], f.next_handle = value, f.next_handle + 1
        value.IsSingleUse, value.quest, value.consumable = single, quest == true, consumable ~= false
        function value:IsA(class)
            assert(class == "/Script/DogwoodInventory.ItemConsumableDataAsset")
            return self.consumable
        end
        return value
    end
    f.potion, f.food, f.quest, f.recipe = f.asset("Potion", true), f.asset("ReusableFood", false), f.asset("Quest", true, true, true), f.asset("Recipe", true, false)
    f.entries = { { ItemDataAsset = f.potion, Quantity = 5 }, { ItemDataAsset = f.food, Quantity = 3 },
        { ItemDataAsset = f.quest, Quantity = 1 }, { ItemDataAsset = f.recipe, Quantity = 1 } }
    local api = {}
    function api.valid(value) return type(value) == "table" and value.valid == true end
    function api.identity(value) return api.valid(value) and value.id or "" end
    function api.same(a, b) return api.valid(a) and api.valid(b) and a.id == b.id end
    function api.read(value, name) if api.valid(value) then return value[name] end end
    function api.finite(value) return type(value) == "number" and value == value and math.abs(value) < math.huge end
    function api.invoke(value, name, ...)
        f.calls[name] = (f.calls[name] or 0) + 1
        if not api.valid(value) then return false, "Invalid fixture object" end
        return pcall(value[name], value, ...)
    end
    function api.has_function(path) return not f.missing[path] end
    function api.register_property(name, owner, kind, offset)
        assert(name == "HappyCheatsItemHandleIndex" and owner == "/Script/DogwoodInventory.ItemHandle"
            and kind == "IntProperty" and offset == 0, "Only the verified four-byte opaque handle schema")
        f.registrations = f.registrations + 1
        return not f.registration_error
    end
    function api.resolve(path)
        f.lookup = f.lookup + 1
        if path == "/Script/Engine.Default__SubsystemBlueprintLibrary" then return f.library end
        local value = obj("Class"); value.path = path; return value
    end
    function api.write(value, name, new_value)
        f.writes[#f.writes + 1] = { name, new_value, value }
        if f.write_error == value or (f.restore_error == value and new_value ~= false and new_value ~= 0) then return false, "Write failed" end
        if f.write_noop ~= value then value[name] = new_value end
        return true
    end
    f.api = api
    f.state = { character = f.character, key = "first", world = obj("World"), controller = obj("Controller") }
    f.module = assert(loadfile("modules/dawnwalker/Scripts/economy.lua"))().new(api)
    return f
end
local function refresh(f, count)
    local available, values
    for _ = 1, count or 1 do available, values = f.module:refresh(f.state) end
    return available, values
end
local function apply(f, action, enabled) return f.module:apply(f.state, "dawnwalker_" .. action, enabled) end

do
    local f = fixture()
    for _, action in ipairs({ "carry", "money", "items" }) do equal(f.module:enabled()["dawnwalker_" .. action], false, "First hotkey starts disabled") end
    local available, values = refresh(f)
    for _, action in ipairs({ "carry", "money", "items", "xp" }) do equal(available["dawnwalker_" .. action], true, "Verified binding available") end
    equal(values.dawnwalker_xp_reward, 100, "Display exact XP to next level")
    equal(#f.writes, 0, "Discovery is read only")
    local lookups = f.lookup
    refresh(f, 64)
    equal(f.lookup, lookups, "No repeated subsystem discovery")
    equal(f.calls.GetCurrentItems, nil, "No inventory scans while off")
    equal(f.calls.GetXPAmountByRewardType, nil, "No unsafe getter during readiness")
    equal(f.calls.ForceLevelUp, nil, "Idle probing never grants XP")
    equal(f.calls.GetCurrencyAmount, 1, "Disabled money does not poll currency")
    equal(#f.writes, 0, "Idle polling is read only")
end
do
    local f = fixture(); refresh(f)
    equal(apply(f, "carry", true), true)
    equal(f.inventory.WeightLimit, 0, "Use native unlimited sentinel")
    equal(apply(f, "carry", true), true)
    equal(#f.writes, 1, "Repeated enable does not replace backup")
    equal(apply(f, "carry", false), true)
    equal(f.inventory.WeightLimit, 75, "Restore original carry limit")
    equal(f.module:enabled().dawnwalker_carry, false)
    f.inventory.WeightLimit = 0
    equal(apply(f, "carry", true), true)
    equal(apply(f, "carry", false), true)
    equal(f.inventory.WeightLimit, 0, "Preserve preexisting unlimited value")
end
do
    local f = fixture(); refresh(f); f.write_noop = f.inventory
    equal(apply(f, "carry", true), false, "No-op property write is rejected")
    equal(f.module:enabled().dawnwalker_carry, false)
    equal(f.inventory.WeightLimit, 75)
    f.write_noop = nil; equal(apply(f, "carry", true), true)
    f.inventory.WeightLimit = 90
    equal(apply(f, "carry", false), false, "Foreign change is reported")
    equal(f.inventory.WeightLimit, 90, "Do not overwrite another owner")
    equal(f.module:disable_all(), true, "Relinquish conflicting ownership")
end
do
    local f = fixture(); refresh(f); apply(f, "carry", true); f.restore_error = f.inventory
    equal(apply(f, "carry", false), false, "Restoration failure is reported")
    f.state.key = "second"
    local available = refresh(f)
    equal(available.dawnwalker_carry, false, "Pending restoration blocks the next session")
    f.restore_error = nil
    equal(refresh(f).dawnwalker_carry, true)
    equal(f.inventory.WeightLimit, 75, "Retry preserves the original backup")
    equal(f.module:enabled().dawnwalker_carry, false, "New sessions start disabled")
end
do
    local f = fixture(); refresh(f); equal(apply(f, "carry", true), true)
    local replacement = obj("ReplacementInventory"); replacement.WeightLimit = 50
    f.current_inventory = replacement
    equal(apply(f, "money", true), false, "Reject replaced component with the same living owner")
    equal(f.calls.AddCurrency, nil)
    local available = refresh(f)
    equal(f.inventory.WeightLimit, 75, "Restore the old component override")
    equal(replacement.WeightLimit, 50, "Never write into unbound replacement")
    equal(available.dawnwalker_carry, false)
    equal(f.module:enabled().dawnwalker_carry, false)
end
do
    local f = fixture(); refresh(f)
    equal(apply(f, "money", true), true)
    equal(f.money, 999999)
    equal(f.writes[1][2], 999424, "Add only the bounded difference")
    refresh(f, 5); equal(f.calls.AddCurrency, 1, "Do not write at the floor")
    f.money = f.money - 50; refresh(f)
    equal(f.money, 999999); equal(f.writes[2][2], 50)
    equal(apply(f, "money", false), true)
    equal(f.money, 999999, "Disable retains current balance")
    f.money = 2000000; equal(apply(f, "money", true), true)
    equal(f.money, 2000000, "Do not reduce a larger earned balance")
    equal(f.calls.AddCurrency, 2)
end
do
    local f = fixture(); f.money = -1
    equal(refresh(f).dawnwalker_money, false)
    equal(apply(f, "money", true), false); equal(#f.writes, 0)
    f = fixture(); refresh(f); f.money_noop = true
    equal(apply(f, "money", true), false, "Native success requires balance readback")
    equal(f.module:enabled().dawnwalker_money, false)
    f = fixture(); refresh(f); apply(f, "money", true); f.money = 0/0
    refresh(f); equal(f.module:enabled().dawnwalker_money, false, "Invalid live balance stops writes")
    equal(f.calls.AddCurrency, 1)
end
do
    local f = fixture(); refresh(f)
    equal(f.inventory:GetCurrentItems().ForEach, nil, "Returned array has no property TArray methods")
    equal(apply(f, "items", true), true)
    equal(f.potion.IsSingleUse, false, "Reusable ordinary potion")
    equal(f.food.IsSingleUse, false, "Preserve preexisting reuse")
    equal(f.quest.IsSingleUse, true, "Quest consumable excluded")
    equal(f.recipe.IsSingleUse, true, "Recipe is not a consumable")
    equal(#f.writes, 1)
    refresh(f, 7); equal(f.calls.GetCurrentItems, 1, "Throttled inventory refresh")
    refresh(f); equal(f.calls.GetCurrentItems, 2)
    equal(#f.writes, 1, "No repeat writes for unchanged inventory")
    equal(apply(f, "items", false), true)
    equal(f.potion.IsSingleUse, true)
    equal(f.food.IsSingleUse, false)
    equal(f.calls.AddCurrency, nil, "Item reuse never creates currency")
end
do
    local f = fixture(); f.registration_error = true
    local available, values = refresh(f)
    equal(available.dawnwalker_items, false, "Opaque handle support must be registered before availability")
    equal(available.dawnwalker_money, true, "Marshaling failure is limited to item reuse")
    equal(values.diagnostic_economy, "Consumable handle marshaling unavailable")
    equal(apply(f, "items", true), false)
    equal(f.calls.GetCurrentItems, nil)
    equal(#f.writes, 0)
    f = fixture(); f.api.register_property = nil
    equal(refresh(f).dawnwalker_items, false, "Missing marshaling helper fails closed")
    f = fixture(); f.missing["/Script/DogwoodInventory.InventorySubsystem:GetAssetForItem"] = true
    equal(refresh(f).dawnwalker_items, false, "Native identity verification is required")
end
do
    for _, failure in ipairs({ "empty_handle", "negative_handle", "wrong_asset", "decode", "quest", "bad_format" }) do
        local f = fixture(); refresh(f)
        local stage
        if failure == "empty_handle" then f.empty_handle, stage = true, "native handle decode"
        elseif failure == "negative_handle" then f.handle_index, stage = -1, "native handle decode"
        elseif failure == "wrong_asset" then f.roundtrip_asset, stage = f.quest, "native handle round-trip"
        elseif failure == "decode" then f.entries[1].decode_error, stage = true, "struct decode"
        elseif failure == "quest" then f.quest_error, stage = true, "quest classification"
        else f.array_override = "Unsupported return" end
        local ok, message = apply(f, "items", true)
        equal(ok, false, failure .. " must stop reuse")
        equal(#f.writes, 0, "Complete trustworthy classification before any override")
        equal(f.module:enabled().dawnwalker_items, false)
        if stage then equal(message, "Consumable inventory check failed (item 1: " .. stage .. ")")
        else equal(message, "Inventory return format unavailable") end
        if failure ~= "quest" then equal(f.calls.IsQuestItem, nil, "Never classify a damaged or mismatched native handle") end
    end
end
do
    local f = fixture(); refresh(f); equal(apply(f, "items", true), true)
    f.empty_handle = true
    local _, values = refresh(f, 8)
    equal(f.module:enabled().dawnwalker_items, false, "A later marshaling failure stops periodic writes")
    equal(f.potion.IsSingleUse, true, "Restore prior ownership when a later classification fails")
    equal(f.quest.IsSingleUse, true, "Quest item remains untouched throughout cleanup")
    equal(values.diagnostic_economy, "Consumable inventory check failed (item 1: native handle decode)")
    equal(f.calls.GetCurrentItems, 2)
    refresh(f, 16); equal(f.calls.GetCurrentItems, 2, "No automatic retry after malformed inventory")
end
do
    local f = fixture(); refresh(f); f.quest_error = f.quest
    local ok, message = apply(f, "items", true)
    equal(ok, false)
    equal(message, "Consumable inventory check failed (item 3: quest classification)", "Retain the failing stage and index")
    equal(#f.writes, 0, "A later quest check must finish before any earlier eligible item is changed")
    f = fixture(); refresh(f); f.array_size = #f.entries + 1
    ok, message = apply(f, "items", true)
    equal(ok, false)
    equal(message, "Consumable inventory check failed (item 5: struct decode)", "Incomplete returned arrays fail closed")
    equal(#f.writes, 0)
end
do
    local f = fixture(); refresh(f); apply(f, "items", true)
    f.potion.quest = true; refresh(f, 8)
    equal(f.potion.IsSingleUse, true, "Restore item promoted to quest item")
    local new = f.asset("NewPotion", true)
    f.entries[#f.entries + 1] = { ItemDataAsset = new, Quantity = 2 }
    refresh(f, 8); equal(new.IsSingleUse, false, "Newly carried consumable joins the override")
    table.remove(f.entries); refresh(f, 8)
    equal(new.IsSingleUse, true, "Restore asset after it leaves the inventory")
    equal(f.module:disable_all(), true)
end
do
    local f = fixture(); refresh(f); f.array_size = 513
    equal(apply(f, "items", true), false, "Bounded inventory scanning")
    equal(#f.writes, 0)
    f = fixture(); refresh(f); f.quest_error = true
    equal(apply(f, "items", true), false)
    equal(#f.writes, 0, "Classification finishes before any write")
    f = fixture(); refresh(f); f.write_error = f.potion
    equal(apply(f, "items", true), false)
    equal(f.module:enabled().dawnwalker_items, false)
    equal(f.potion.IsSingleUse, true)
end
do
    local f = fixture(); refresh(f); apply(f, "items", true)
    f.potion.IsSingleUse = true; refresh(f, 8)
    equal(f.module:enabled().dawnwalker_items, false, "External edits stop reuse")
    equal(f.potion.IsSingleUse, true, "External edit survives cleanup")
    f = fixture(); refresh(f); apply(f, "items", true)
    f.state.key = "new save"; refresh(f)
    equal(f.potion.IsSingleUse, true, "Session transition restores consumables")
    equal(f.module:enabled().dawnwalker_items, false)
    f = fixture(); refresh(f); apply(f, "items", true); f.potion.valid = false
    equal(f.module:disable_all(), true, "Destroyed assets need no restore write")
end
do
    local f = fixture(); refresh(f)
    equal(apply(f, "xp"), true)
    equal(f.xp, 0, "Exact threshold resets per-level XP")
    equal(f.level, 3, "Exactly one level gained")
    equal(f.writes[1][2], 100, "Add only remaining XP to threshold")
    equal(f.calls.ForceLevelUp, 1, "Exactly one native mutation per button press")
    equal(f.module:enabled().dawnwalker_xp, nil, "XP never becomes a toggle")
    refresh(f, 20); equal(f.calls.ForceLevelUp, 1, "No automatic retries or grants")
    equal(f.calls.GetXPAmountByRewardType, nil)
    equal(f.calls.AddQuestXP, nil)
end
do
    local f = fixture(); f.requirements = {}
    equal(refresh(f).dawnwalker_xp, false, "Constructor-level 1/2 does not prove cache readiness")
    equal(apply(f, "xp"), false); equal(f.calls.ForceLevelUp, nil)
    f = fixture(); f.requirements[3] = 0
    equal(refresh(f).dawnwalker_xp, false, "Zero next threshold rejected")
    equal(f.calls.ForceLevelUp, nil)
    f = fixture(); f.requirements[4] = 0
    equal(refresh(f).dawnwalker_xp, false, "Zero following threshold cannot grant multiple levels")
    equal(f.calls.ForceLevelUp, nil)
    f = fixture(); f.requirements[4] = nil
    equal(refresh(f).dawnwalker_xp, false, "Missing following row rejected conservatively")
    equal(f.calls.ForceLevelUp, nil)
    f = fixture(); f.level_cap = f.level
    equal(refresh(f).dawnwalker_xp, false, "No XP at progression cap")
    f = fixture(); f.progression = false
    equal(refresh(f).dawnwalker_xp, false, "Disabled native progression rejected")
    f = fixture(); f.xp = 600
    equal(refresh(f).dawnwalker_xp, false, "Do not apply zero or negative XP delta")
    f = fixture(); f.xp = -1
    equal(refresh(f).dawnwalker_xp, false)
    f = fixture(); f.level = 127
    equal(refresh(f).dawnwalker_xp, false, "Signed-byte level bound respected")
end
do
    local f = fixture(); refresh(f); f.requirements[3] = nil
    equal(apply(f, "xp"), false, "Actual cache is rechecked at the button press")
    equal(f.calls.ForceLevelUp, nil)
    f = fixture(); refresh(f); f.character.AbilitySystemComponent = obj("ForeignASC")
    equal(apply(f, "xp"), false, "Recheck current player ownership")
    equal(f.calls.ForceLevelUp, nil)
    f = fixture(); f.dev.PlayerASC = { Get = function() return obj("ForeignASC") end }
    equal(refresh(f).dawnwalker_xp, false, "Unwrap native weak pointer before identity check")
    equal(f.calls.ForceLevelUp, nil)
    f = fixture(); refresh(f); f.xp_capped = true
    equal(apply(f, "xp"), false, "Native no-op is not reported as a grant")
    refresh(f, 8)
    equal(f.module.available.dawnwalker_xp, false, "Uncertain result latches unavailable")
    equal(apply(f, "xp"), false, "Same-session retry is refused")
    equal(f.calls.ForceLevelUp, 1)
end
do
    local f = fixture(); refresh(f); f.force_throw = true
    equal(apply(f, "xp"), false, "Uncertain native exception is not retried")
    equal(f.level, 3, "Fixture can mutate before reporting an error")
    refresh(f, 20); equal(apply(f, "xp"), false)
    equal(f.calls.ForceLevelUp, 1)
    f = fixture(); refresh(f); f.after_xp = function() f.level = 4 end
    equal(apply(f, "xp"), false, "Unexpected multiple-level readback is rejected")
    equal(f.module.available.dawnwalker_xp, false)
    f = fixture(); f.missing["/Script/DogwoodCharacterDevelopment.CharacterDevelopmentSubsystem:ForceLevelUp"] = true
    equal(refresh(f).dawnwalker_xp, false, "Missing mutation binding fails closed")
    equal(f.calls.GetCurrentLevelXPRequirement, nil)
    equal(f.calls.ForceLevelUp, nil)
end
do
    local f = fixture(); f.missing["/Script/DogwoodInventory.InventoryComponent:AddCurrency"] = true
    local available = refresh(f)
    equal(available.dawnwalker_money, false); equal(available.dawnwalker_carry, true)
    equal(apply(f, "unknown", true), false)
    equal(apply(f, "carry", nil), false, "No implicit toggle at native boundary")
    equal(f.module:disable_all(), true)
    equal(#f.writes, 0)
end
print("Dawnwalker economy: " .. checks .. " assertions passed")
