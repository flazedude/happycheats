-- Steam build 25232147, CL-258504. Game-thread calls only. This extension is
-- handed a freshly validated, living, possessed local-player state by bindings.
-- InventoryComponent.WeightLimit (float, +0x158) uses zero as unlimited in
-- GetWeightLimit and all three native capacity/overweight checks.
-- ItemBaseDataAsset.IsSingleUse (bool, +0x90) is the native UseItem consumption
-- condition, after the effect has been applied. Only non-quest consumables in
-- the player's inventory are changed; no quantities or quest flags are edited.
local M, Instance = {}, {}
Instance.__index = Instance
local INV = "/Script/DogwoodInventory.InventoryComponent:"
local SUB = "/Script/DogwoodInventory.InventorySubsystem:"
local XP = "/Script/DogwoodCharacterDevelopment.CharacterDevelopmentSubsystem:"
local CONSUMABLE = "/Script/DogwoodInventory.ItemConsumableDataAsset"
local ITEM_HANDLE = "/Script/DogwoodInventory.ItemHandle"
local HANDLE_INDEX = "HappyCheatsItemHandleIndex"
local MONEY_FLOOR = 999999
local MAX_ITEMS, ITEM_POLL_TICKS, XP_POLL_TICKS = 512, 8, 4
local IDS = { "dawnwalker_carry", "dawnwalker_money", "dawnwalker_items" }

local function field(value, name)
    local ok, result = pcall(function() return value[name] end)
    if ok then return result end
end
local function empty_available()
    return { dawnwalker_carry = false, dawnwalker_money = false,
        dawnwalker_items = false, dawnwalker_xp = false }
end
local function copy(source)
    local result = {}
    for key, value in pairs(source) do result[key] = value end
    return result
end
function Instance:_integer(value, maximum)
    return self.api.finite(value) and value >= 0 and value <= maximum and value % 1 == 0
end
function Instance:_functions(prefix, names)
    for _, name in ipairs(names) do
        if not self.api.has_function(prefix .. name) then return false end
    end
    return true
end
function Instance:_subsystem(state, class_path)
    local a = self.api
    if not a.has_function("/Script/Engine.SubsystemBlueprintLibrary:GetGameInstanceSubsystem") then return nil end
    local library = a.resolve("/Script/Engine.Default__SubsystemBlueprintLibrary")
    local class = a.resolve(class_path)
    if not a.valid(library) or not a.valid(class) then return nil end
    local ok, object = a.invoke(library, "GetGameInstanceSubsystem", state.character, class)
    if ok and a.valid(object) then return object end
end
function Instance:_owns_inventory(state)
    local a = self.api
    if not state or state.key ~= self.key or not a.valid(self.inventory)
        or not a.same(state.character, self.character) then return false end
    local current_ok, current = a.invoke(state.character, "GetInventoryComponent")
    if not current_ok or not a.same(current, self.inventory) then return false end
    local ok, owner = a.invoke(self.inventory, "GetOwner")
    return ok and a.same(owner, state.character)
end
function Instance:_xp_owner(state)
    local a = self.api
    if not state or state.key ~= self.key or not a.valid(self.development) then return false end
    local asc = a.read(self.development, "PlayerASC")
    -- The pinned UE4SS build returns FWeakObjectPtr for this reflected field.
    if not a.valid(asc) then
        local ok, object = pcall(function() return asc:Get() end)
        asc = ok and object or nil
    end
    return a.same(asc, a.read(state.character, "AbilitySystemComponent"))
end
function Instance:_restore(entry)
    local a = self.api
    if not a.valid(entry.object) then return true end -- Object has been destroyed.
    if a.identity(entry.object) ~= entry.identity then return false, "Override object changed" end
    local current = a.read(entry.object, entry.field)
    if current == entry.original then return true end
    if current ~= entry.written then return false, "Property changed outside the trainer; left untouched", true end
    local ok = a.write(entry.object, entry.field, entry.original)
    if not ok or a.read(entry.object, entry.field) ~= entry.original then
        return false, "Could not restore " .. entry.field
    end
    return true
end
function Instance:_restore_carry()
    if not self.carry then return true end
    local ok, message, relinquish = self:_restore(self.carry)
    if ok or relinquish then self.carry = nil end
    return ok, message
end
function Instance:_restore_items()
    local success, message = true, nil
    for key, entry in pairs(self.items) do
        local ok, why, relinquish = self:_restore(entry)
        if ok or relinquish then self.items[key] = nil end
        if not ok then success, message = false, why end
    end
    return success, message
end
function Instance:disable_all()
    for _, action in ipairs(IDS) do self.flags[action] = false end
    local carry_ok, carry_message = self:_restore_carry()
    local items_ok, items_message = self:_restore_items()
    return carry_ok and items_ok, carry_message or items_message or "Economy controls disabled"
end
function Instance:enabled() return copy(self.flags) end

-- GetCurrentLevelXPRequirement(int Level) checks the actual native level map
-- and returns zero for a missing entry (thunk 0x5DB1084, guard 0x5DB1180).
-- ForceLevelUp (0x5DB0AEC) is safe only after these immediate, same-game-thread
-- probes. Never call the unguarded quest-reward getter, even during discovery.
-- Native level-map insertion is confined to init callback 0x2051184; the known
-- clears are construction/destruction. See xp-guarded-route-review.md evidence.
function Instance:_xp_plan(state)
    if self.xp_failed or not self:_xp_owner(state) then return nil end
    local a, development = self.api, self.development
    local mode_ok, progression = a.invoke(development, "IsAnyCharacterDevelopmentModeEnabled")
    local level_ok, level = a.invoke(development, "GetCurrentLevel")
    local next_ok, next_level = a.invoke(development, "GetNextLevel")
    local xp_ok, current = a.invoke(development, "GetCurrentXP")
    if not mode_ok or progression ~= true or not level_ok or not self:_integer(level, 126) or level == 0
        or not next_ok or next_level ~= level + 1 or not xp_ok or not self:_integer(current, 10000000) then return nil end
    local threshold_ok, threshold = a.invoke(development, "GetCurrentLevelXPRequirement", next_level)
    local following_ok, following = a.invoke(development, "GetCurrentLevelXPRequirement", next_level + 1)
    -- A zero following threshold could advance twice. The getter also returns
    -- zero when missing, so reject both cases instead of guessing about the cap.
    if not threshold_ok or not self:_integer(threshold, 10000000) or threshold <= current
        or not following_ok or not self:_integer(following, 10000000) or following <= 0 then return nil end
    return { level = level, next_level = next_level, current = current,
        threshold = threshold, following = following, amount = threshold - current }
end
function Instance:_update_xp(state)
    self.xp_plan = self:_xp_plan(state)
    self.available.dawnwalker_xp = self.xp_plan ~= nil
    self.values.dawnwalker_xp_reward = self.xp_plan and self.xp_plan.amount or nil
    self.values.diagnostic_xp = self.xp_failed and "XP result could not be confirmed; disabled for this player session"
        or (self.xp_plan and "Add exactly the XP needed for the next level"
            or "XP unavailable: progression is capped or verified level-cache entries are missing")
    if self.xp_plan then
        self.values.dawnwalker_xp = self.xp_plan.current
        self.values.dawnwalker_level = self.xp_plan.level
    end
end
function Instance:_bind(state)
    local a = self.api
    self.character, self.key = state.character, state.key
    self.inventory, self.inventory_subsystem, self.development = nil, nil, nil
    self.xp_failed, self.xp_supported, self.xp_ticks, self.xp_plan = false, false, 0, nil
    self.available = empty_available()
    if a.has_function("/Script/Dawnwalker.DawnwalkerCharacterBase:GetInventoryComponent") then
        local ok, inventory = a.invoke(state.character, "GetInventoryComponent")
        if ok and a.valid(inventory) then self.inventory = inventory end
    end
    if self:_owns_inventory(state) then
        local limit = a.read(self.inventory, "WeightLimit")
        self.available.dawnwalker_carry = a.finite(limit) and limit >= 0
            and a.has_function(INV .. "GetWeightLimit")
        if self:_functions(INV, { "GetCurrencyAmount", "AddCurrency" }) then
            local ok, money = a.invoke(self.inventory, "GetCurrencyAmount", 0)
            self.available.dawnwalker_money = ok and self:_integer(money, 2147483647)
            if self.available.dawnwalker_money then self.values.dawnwalker_money = money end
        end
        self.inventory_subsystem = self:_subsystem(state, "/Script/DogwoodInventory.InventorySubsystem")
        if a.valid(self.inventory_subsystem)
            and self:_functions(INV, { "GetCurrentItems", "GetHandleForAssetInInventory" })
            and self:_functions(SUB, { "GetPlayerInventoryComponent", "GetAssetForItem", "IsQuestItem" }) then
            local ok, inventory = a.invoke(self.inventory_subsystem, "GetPlayerInventoryComponent")
            -- ItemHandle is an opaque four-byte native index. UE4SS 2bfa839f
            -- converts returned structs into Lua tables; without this verified
            -- field its returned handle is {}, losing the index on the next call.
            local registered = type(a.register_property) == "function"
                and a.register_property(HANDLE_INDEX, ITEM_HANDLE, "IntProperty", 0)
            self.available.dawnwalker_items = ok and a.same(inventory, self.inventory) and registered == true
            if registered ~= true then self.values.diagnostic_economy = "Consumable handle marshaling unavailable" end
        end
    end
    self.development = self:_subsystem(state, "/Script/DogwoodCharacterDevelopment.CharacterDevelopmentSubsystem")
    self.xp_supported = self:_functions(XP, { "ForceLevelUp", "GetCurrentXP", "GetCurrentLevel", "GetNextLevel",
        "GetCurrentLevelXPRequirement", "IsAnyCharacterDevelopmentModeEnabled" })
    if self.xp_supported then self:_update_xp(state) end
end
function Instance:_carry_on()
    local a, inventory = self.api, self.inventory
    if self.carry then return a.read(inventory, "WeightLimit") == 0, "Carry limit changed" end
    local original = a.read(inventory, "WeightLimit")
    if not a.finite(original) or original < 0 then return false, "Carry limit unavailable" end
    if original ~= 0 then
        self.carry = { object = inventory, identity = a.identity(inventory), field = "WeightLimit", original = original, written = 0 }
        if not a.write(inventory, "WeightLimit", 0) then
            self:_restore_carry()
            return false, "Carry limit write failed"
        end
    end
    local ok, limit = a.invoke(inventory, "GetWeightLimit")
    if not ok or limit ~= 0 or a.read(inventory, "WeightLimit") ~= 0 then
        self:_restore_carry()
        return false, "Unlimited carry limit was not confirmed"
    end
    return true, "Carry limit disabled"
end
function Instance:_money_top_up()
    local a = self.api
    local ok, amount = a.invoke(self.inventory, "GetCurrencyAmount", 0)
    if not ok or not self:_integer(amount, 2147483647) then return false, "Coin balance unavailable" end
    self.values.dawnwalker_money = amount
    if amount >= MONEY_FLOOR then return true, "Coin balance maintained at 999,999" end
    local added = a.invoke(self.inventory, "AddCurrency", 0, MONEY_FLOOR - amount)
    local read_ok, current = a.invoke(self.inventory, "GetCurrencyAmount", 0)
    if not added or not read_ok or current ~= MONEY_FLOOR then return false, "Coin top-up was not confirmed" end
    self.values.dawnwalker_money = current
    return true, "Coin balance maintained at 999,999; disabling stops top-ups"
end
function Instance:_scan_items()
    local a = self.api
    local ok, inventory = a.invoke(self.inventory_subsystem, "GetPlayerInventoryComponent")
    if not ok or not a.same(inventory, self.inventory) then return false, "Player inventory changed" end
    local read_ok, array = a.invoke(self.inventory, "GetCurrentItems")
    if not read_ok or array == nil then return false, "Inventory listing unavailable" end
    -- UFunction returns use GetNonTrivialLocal in pinned UE4SS 2bfa839f:
    -- this is a 1-based Lua table of LocalUnrealParam<ScriptStructWrapper>,
    -- not a property TArray with ForEach. Each element:get() is a UScriptStruct.
    if type(array) ~= "table" then return false, "Inventory return format unavailable" end
    local size_ok, size = pcall(function() return #array end)
    if not size_ok or not self:_integer(size, MAX_ITEMS) then return false, "Inventory listing exceeds safe limit" end
    local eligible, error_message = {}, nil
    local stage = "inventory iteration"
    local walked = pcall(function()
        for index = 1, size do
            local prefix = "item " .. tostring(index) .. ": "
            stage = prefix .. "struct decode"
            local element = rawget(array, index)
            local item = element:get()
            local asset, quantity = field(item, "ItemDataAsset"), field(item, "Quantity")
            if not a.valid(asset) or not self:_integer(quantity, 2147483647) then error("Invalid inventory entry") end
            if quantity == 0 then goto continue end
            stage = prefix .. "consumable class check"
            local class_ok, consumable = a.invoke(asset, "IsA", CONSUMABLE)
            if not class_ok or type(consumable) ~= "boolean" then error("Consumable class check failed") end
            if consumable ~= true then goto continue end
            local key = a.identity(asset)
            if key == "" then error("Consumable identity unavailable") end
            if eligible[key] then goto continue end
            stage = prefix .. "native handle decode"
            local handle_ok, handle = a.invoke(self.inventory, "GetHandleForAssetInInventory", asset)
            local handle_index = field(handle, HANDLE_INDEX)
            if not handle_ok or not self:_integer(handle_index, 2147483647) then error("Consumable handle unavailable") end
            -- A valid-looking index alone cannot prove that the round-trip
            -- through Lua preserved this item's identity. Check the native
            -- asset lookup before allowing its quest classification to pass.
            stage = prefix .. "native handle round-trip"
            local asset_ok, handle_asset = a.invoke(self.inventory_subsystem, "GetAssetForItem", handle, {})
            if not asset_ok or not a.same(handle_asset, asset) then error("Consumable handle changed") end
            stage = prefix .. "quest classification"
            local quest_ok, quest = a.invoke(self.inventory_subsystem, "IsQuestItem", handle)
            if not quest_ok or type(quest) ~= "boolean" then error("Quest item check failed") end
            if quest then goto continue end
            stage = prefix .. "single-use flag read"
            local single = a.read(asset, "IsSingleUse")
            if type(single) ~= "boolean" then error("Consumable flag unavailable") end
            eligible[key] = { object = asset, identity = key, field = "IsSingleUse", original = single, written = false }
            ::continue::
        end
    end)
    if not walked then return false, "Consumable inventory check failed (" .. stage .. ")" end
    -- Restore assets no longer carried, or newly marked as quest items, before
    -- changing any newly discovered assets. Never duplicate or re-add items.
    for key, entry in pairs(self.items) do
        if not eligible[key] then
            local restored, why, relinquish = self:_restore(entry)
            if restored or relinquish then self.items[key] = nil end
            if not restored then error_message = why end
        elseif a.read(entry.object, entry.field) ~= entry.written then
            self.items[key] = nil
            error_message = "Consumable flag changed outside the trainer"
        end
    end
    if error_message then return false, error_message end
    local active = 0
    for key, entry in pairs(eligible) do
        active = active + 1
        if not self.items[key] and entry.original == true then
            self.items[key] = entry -- Record ownership before attempting the write.
            if not a.write(entry.object, entry.field, false) or a.read(entry.object, entry.field) ~= false then
                return false, "Consumable reuse write was not confirmed"
            end
        end
    end
    self.values.dawnwalker_reusable_consumables = active
    return true, "Ordinary carried consumables can be reused; quest items excluded"
end
function Instance:_add_xp(state)
    local plan = self:_xp_plan(state)
    if not plan then return false, "XP requires verified current and following level-cache entries" end
    -- All checks are read-only. Repeat them immediately before the one mutation;
    -- no timer yield, native callback, or other action occurs between this final
    -- probe and ForceLevelUp. Never use a guessed XP amount or quest reward map.
    local fresh = self:_xp_plan(state)
    if not fresh or fresh.level ~= plan.level or fresh.current ~= plan.current
        or fresh.threshold ~= plan.threshold or fresh.following ~= plan.following then
        return false, "XP progression changed; try again"
    end
    self.xp_failed = true -- An uncertain write is never retried automatically.
    self.available.dawnwalker_xp = false
    local granted = self.api.invoke(self.development, "ForceLevelUp")
    local level_ok, level = self.api.invoke(self.development, "GetCurrentLevel")
    local xp_ok, xp = self.api.invoke(self.development, "GetCurrentXP")
    if not granted or not level_ok or level ~= plan.next_level or not xp_ok or xp ~= 0 then
        self.values.diagnostic_xp = "XP result could not be confirmed; disabled for this player session"
        return false, self.values.diagnostic_xp
    end
    self.xp_failed = false
    self.values.dawnwalker_xp, self.values.dawnwalker_level = xp, level
    self:_update_xp(state)
    return true, "Added " .. tostring(plan.amount) .. " XP; reached level " .. tostring(level)
end
function Instance:refresh(state)
    local key = state and state.key or ""
    if key ~= self.key then
        local restored, why = self:disable_all()
        self.available, self.values = empty_available(), {}
        if not restored and (self.carry or next(self.items)) then
            self.values.diagnostic_economy = why
            return copy(self.available), copy(self.values)
        end
        self.key = key
        if state then self:_bind(state) end
    end
    if not state or key == "" then return copy(self.available), copy(self.values) end
    if not self:_owns_inventory(state) then
        self:disable_all()
        self.available.dawnwalker_carry, self.available.dawnwalker_money, self.available.dawnwalker_items = false, false, false
    else
        if self.flags.dawnwalker_carry and self.api.read(self.inventory, "WeightLimit") ~= 0 then
            self.flags.dawnwalker_carry = false
            self:_restore_carry()
            self.values.diagnostic_economy = "Carry limit changed outside the trainer"
        end
        if self.flags.dawnwalker_money then
            local ok, why = self:_money_top_up()
            if not ok then self.flags.dawnwalker_money = false; self.values.diagnostic_economy = why end
        end
        if self.flags.dawnwalker_items then
            self.item_ticks = self.item_ticks + 1
            if self.item_ticks >= ITEM_POLL_TICKS then
                self.item_ticks = 0
                local ok, why = self:_scan_items()
                if not ok then
                    self.flags.dawnwalker_items = false
                    self:_restore_items()
                    self.values.diagnostic_economy = why
                end
            end
        end
    end
    if self.xp_supported then
        self.xp_ticks = self.xp_ticks + 1
        if self.xp_ticks >= XP_POLL_TICKS then
            self.xp_ticks = 0
            self:_update_xp(state)
        elseif not self:_xp_owner(state) then
            self.available.dawnwalker_xp = false
        end
    end
    return copy(self.available), copy(self.values)
end
function Instance:apply(state, action, enabled)
    if action == "dawnwalker_carry" and enabled == false then
        self.flags[action] = false
        local ok, why = self:_restore_carry()
        return ok, why or "Original carry limit restored"
    elseif action == "dawnwalker_items" and enabled == false then
        self.flags[action] = false
        local ok, why = self:_restore_items()
        return ok, why or "Original consumable behavior restored"
    elseif action == "dawnwalker_money" and enabled == false then
        self.flags[action] = false
        return true, "Coin top-ups disabled; current balance retained"
    end
    if not state or state.key ~= self.key then return false, "Player session changed; enable again" end
    if self.available[action] ~= true then return false, "Economy binding unavailable" end
    if action == "dawnwalker_xp" then return self:_add_xp(state) end
    if enabled ~= true then return false, "Expected an explicit enable or disable value" end
    if not self:_owns_inventory(state) then return false, "Player inventory ownership changed" end
    local ok, message
    if action == "dawnwalker_carry" then ok, message = self:_carry_on()
    elseif action == "dawnwalker_money" then ok, message = self:_money_top_up()
    elseif action == "dawnwalker_items" then
        self.item_ticks = 0
        ok, message = self:_scan_items()
        if not ok then self:_restore_items() end
    else return false, "Unknown economy action" end
    self.flags[action] = ok == true
    return ok, message
end
function M.new(api)
    local flags = {}
    for _, action in ipairs(IDS) do flags[action] = false end
    return setmetatable({ api = api, key = "", flags = flags, available = empty_available(), values = {},
        items = {}, item_ticks = 0 }, Instance)
end
return M
