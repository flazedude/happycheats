-- Terrain fragments are signed int32 inputs, not inventory stack counts.
-- Keep this arithmetic independent of UE4SS so bounds can be tested directly.
local M = { DEFAULT_MULTIPLIER = 10, INT32_MAX = 2147483647 }

function M.is_multiplier(value)
    return type(value) == "number" and (value == 10 or value == 20 or value == 100)
end

function M.is_positive_int32(value)
    return type(value) == "number" and value == value and value > 0
        and value <= 2147483647 and value == math.floor(value)
end

function M.multiply(amount, multiplier)
    if multiplier == nil then multiplier = M.DEFAULT_MULTIPLIER end
    if not M.is_multiplier(multiplier) then return nil, "invalid_multiplier" end
    if not M.is_positive_int32(amount) then return nil, "invalid_amount" end
    if amount > math.floor(M.INT32_MAX / multiplier) then return nil, "int32_overflow" end
    return amount * multiplier, amount * (multiplier - 1)
end

function M.new_counters()
    return { collection_events = 0, collection_base_fragments = 0,
        collection_bonus_fragments = 0, collection_total_fragments = 0,
        collection_rejected_events = 0, collection_last_original = 0,
        collection_last_total = 0, collection_last_resource = "" }
end

-- Record only after Amount:set has succeeded. These measure the values passed
-- to the game's collection handler; they do not assert nugget output/capacity.
function M.record(counters, original, total, resource)
    counters.collection_events = counters.collection_events + 1
    counters.collection_base_fragments = counters.collection_base_fragments + original
    counters.collection_bonus_fragments = counters.collection_bonus_fragments + total - original
    counters.collection_total_fragments = counters.collection_total_fragments + total
    counters.collection_last_original = original
    counters.collection_last_total = total
    counters.collection_last_resource = resource or ""
end

return M
