-- Run from repository root: lua modules/astroneer/tests/collection_test.lua
package.path = "modules/astroneer/Scripts/?.lua;" .. package.path
local Collection = require("collection")
local assertions = 0
local function equal(actual, expected, message)
    assertions = assertions + 1
    assert(actual == expected, (message or "Mismatch") .. ": " .. tostring(actual) .. " ~= " .. tostring(expected))
end
equal(Collection.multiply(1), 10, "One fragment yields ten")
equal(Collection.multiply(123), 1230)
local largest, bonus = Collection.multiply(214748364)
equal(largest, 2147483640)
equal(bonus, 1932735276)
equal(Collection.multiply(1, 20), 20)
local largest_twenty, bonus_twenty = Collection.multiply(107374182, 20)
equal(largest_twenty, 2147483640)
equal(bonus_twenty, 2040109458)
equal(Collection.multiply(107374183, 20), nil, "20x has its own int32 overflow boundary")
equal(Collection.multiply(214748364, 20), nil)
equal(Collection.multiply(1, 100), 100)
local largest_hundred, bonus_hundred = Collection.multiply(21474836, 100)
equal(largest_hundred, 2147483600)
equal(bonus_hundred, 2126008764)
equal(Collection.multiply(21474837, 100), nil, "100x has its own int32 overflow boundary")
equal(Collection.multiply(107374182, 100), nil)
for _, invalid_multiplier in ipairs({ 0, -10, 1, 15, 30, 99, 101, 100.1, "10", "20", "100", true, {}, math.huge }) do
    equal(Collection.multiply(1, invalid_multiplier), nil, "Multiplier must be numeric 10, 20 or 100")
end
equal(Collection.multiply(1, 0 / 0), nil)
for _, invalid in ipairs({ 0, -1, 0.1, 214748365, 2147483647, math.huge, -math.huge, "1", true, {} }) do
    equal(Collection.multiply(invalid), nil, "Invalid or overflowing input is untouched")
end
equal(Collection.multiply(0 / 0), nil)
equal(Collection.multiply(nil), nil)
local counters = Collection.new_counters()
Collection.record(counters, 2, 20, "Resin")
Collection.record(counters, 3, 3, "Compound")
equal(counters.collection_events, 2)
equal(counters.collection_base_fragments, 5)
equal(counters.collection_bonus_fragments, 18)
equal(counters.collection_total_fragments, 23)

-- Mock only the external UE4SS boundary. Verify the hook writes once, limits
-- its owner/network scope, and restores per-character state across respawn.
local next_address = 1
local function object(name, fields)
    local obj = fields or {}
    local address = next_address
    next_address = next_address + 1
    function obj:IsValid() return not self.invalid end
    function obj:GetAddress() return address end
    function obj:GetFullName() return name end
    return obj
end
local world = object("World")
local power = object("BackpackPower", { StorageCapacity = 10, charge = 4 })
function power:GetCurrentCharge() return self.charge end
function power:SetCurrentCharge(value) self.charge = value; return value end
local backpack = object("Backpack")
local character = object("Player", { Health = 100, Dying = false, OxygenUseMultiplier = 1, bFreeOxygenDebug = false, OxygenLevel = 80 })
local standalone, authority, local_control = true, true, true
function character:IsLocallyControlled() return local_control end
function character:HasAuthority() return authority end
function character:GetWorld() return world end
function character:GetBackpack() return backpack end
function character:GetPlayController() return self end
local current_character = character
local lifecycle = 9
local mode = object("GameMode")
function mode:GetCurrentLifecycleState() return lifecycle end
local gameplay = object("GameplayStatics")
function gameplay:GetGameMode() return authority and mode or nil end
local system = object("System")
function system:IsStandalone() return standalone end
local power_library = object("PowerLibrary")
function power_library:ActorPowerComponent() return power end
local research = object("Research", { balance = 0 })
function research:AuthorityGrantResearchPoints(_, amount) self.balance = self.balance + amount; return true end
function research:GetCurrentResearchPointBalance() return self.balance end
local function_object = object("Function")
local hooks, hook_count = {}, 0
function StaticFindObject(path)
    if path == "/Script/Engine.Default__KismetSystemLibrary" then return system end
    if path == "/Script/Engine.Default__GameplayStatics" then return gameplay end
    if path == "/Script/Astro.Default__PowerComponent" then return power_library end
    if path == "/Script/Astro.Default__ResearchSystem" then return research end
    if path:find(":", 1, true) then return function_object end
end
function FindAllOf() return current_character and { current_character } or {} end
function RegisterHook(path, pre)
    hook_count = hook_count + 1
    hooks[path] = pre
    return hook_count, hook_count + 100
end
local Bindings = require("bindings")
local binding = Bindings.new()
equal(Bindings.new(), binding)
equal(hook_count, 1, "Singleton must never register a second multiplier")
local ready, _, _, available = binding:refresh()
equal(ready, true)
equal(available.resource_collection_10x, true)
equal(available.set_collection_multiplier, true)
equal(available.unlimited_soil, false)
local cache = object("Cache")
function cache:GetCharacter() return current_character end
local cdo = object("ResinDefault", { IsPower = false, IsOxygen = false })
local resource = object("ResinClass")
function resource:GetCDO() return cdo end
local function param(value)
    return { value = value, writes = 0, get = function(self) return self.value end,
        set = function(self, new_value) self.value = new_value; self.writes = self.writes + 1 end }
end
local function collect(amount)
    local amount_param = param(amount)
    hooks["/Script/Astro.ResourceCacheComponent:OnAcceptResource"](param(cache), param(resource), amount_param)
    return amount_param
end
equal(collect(2).value, 2, "Off passes original amount through")
equal(binding:apply("resource_collection_10x", true), true)
local tenfold = collect(2)
equal(tenfold.value, 20)
equal(tenfold.writes, 1)
equal(collect(214748365).writes, 0, "Overflow does not write")
cdo.IsOxygen = true
equal(collect(3).value, 3, "Oxygen is not terrain loot")
cdo.IsOxygen = false
local owner_method = cache.GetCharacter
function cache:GetCharacter() return object("AnotherPlayer") end
equal(collect(3).value, 3, "Other player's collection untouched")
cache.GetCharacter = owner_method
standalone = false
equal(collect(3).value, 30, "Listen host accepts its local collection even when IsStandalone is false")
local host_ready, _, host_values = binding:refresh()
equal(host_ready, true)
equal(host_values.diagnostic_authority, true)
equal(host_values.diagnostic_local_control, true)
local host_key = binding:session_key()
authority = false
equal(collect(3).value, 3, "Authority loss blocks immediate writes")
local client_ready, client_message, client_values = binding:refresh()
equal(client_ready, false)
equal(client_message, "This world is hosted elsewhere. Run the trainer on the host.")
equal(client_values.diagnostic_authority, false)
equal(binding:session_key() ~= host_key, true, "Authority transition invalidates session identity")
equal(binding:enabled().resource_collection_10x, false)
authority = true
equal(binding:refresh(), true)
equal(binding:enabled().resource_collection_10x, false, "Returning to host does not re-enable a toggle")
equal(binding:apply("unlimited_oxygen", true), true)
equal(character.OxygenUseMultiplier, 0)
equal(character.bFreeOxygenDebug, true)
equal(binding:apply("unlimited_power", true), true)
equal(power.charge, 10)
equal(binding:apply("unlimited_oxygen", false), true)
equal(character.OxygenUseMultiplier, 1)
equal(character.bFreeOxygenDebug, false)
equal(binding:apply("add_bytes", true), true)
equal(research.balance, 10000)
local old_key = binding:session_key()
current_character = nil
local spawn_ready, spawn_message = binding:refresh()
equal(spawn_ready, false)
equal(spawn_message, "Waiting for your astronaut to spawn.")
equal(power.charge, 4, "Original charge restored on session loss")
equal(binding:enabled().resource_collection_10x, false)
equal(binding:session_key() ~= old_key, true)
current_character = character
equal(binding:refresh(), true)
equal(binding:enabled().resource_collection_10x, false, "Cheats do not carry to respawn/save switch")
equal(binding:apply("unlimited_soil", true), false)
equal(binding:apply("unknown_action", true), false)

-- A write failure must never count a bonus as delivered or keep a failed hook on.
equal(binding:apply("resource_collection_10x", true), true)
local _, _, before_failure = binding:refresh()
local failing_amount = param(2)
function failing_amount:set() error("Mock memory write failure") end
hooks["/Script/Astro.ResourceCacheComponent:OnAcceptResource"](param(cache), param(resource), failing_amount)
equal(binding:enabled().resource_collection_10x, false)
local _, _, after_failure, failed_available = binding:refresh()
equal(after_failure.collection_total_fragments, before_failure.collection_total_fragments)
equal(failed_available.resource_collection_10x, false)

-- An unloaded/refactored function is unsupported, not a fabricated ready hook.
local original_find = StaticFindObject
function StaticFindObject(path)
    if path == "/Script/Astro.ResourceCacheComponent:OnAcceptResource" then return nil end
    return original_find(path)
end
local missing_binding = dofile("modules/astroneer/Scripts/bindings.lua").new()
local _, _, _, missing_available = missing_binding:refresh()
equal(missing_available.resource_collection_10x, false)
equal(missing_available.set_collection_multiplier, false)
equal(missing_binding:apply("resource_collection_10x", true), false)
equal(missing_binding:apply("set_collection_multiplier", false, 20), false)
StaticFindObject = original_find
local _, _, _, loaded_available = missing_binding:refresh()
equal(loaded_available.resource_collection_10x, true, "Deferred function load can install hook")

-- A nested event fired while Amount:set runs is ignored, never multiplied twice.
equal(missing_binding:apply("resource_collection_10x", true), true)
local nested_param
local recursive_amount = param(2)
function recursive_amount:set(value)
    self.value, self.writes = value, self.writes + 1
    nested_param = collect(2)
end
hooks["/Script/Astro.ResourceCacheComponent:OnAcceptResource"](param(cache), param(resource), recursive_amount)
equal(recursive_amount.value, 20)
equal(recursive_amount.writes, 1)
equal(nested_param.value, 2)
equal(nested_param.writes, 0)

-- Failed restoration stays queued and can be retried after the failure clears.
equal(missing_binding:apply("unlimited_power", true), true)
local original_set = power.SetCurrentCharge
local restore_fails = true
function power:SetCurrentCharge(value)
    if restore_fails then error("Mock transient restore failure") end
    return original_set(self, value)
end
equal(missing_binding:apply("unlimited_power", false), false)
equal(power.charge, 10)
equal(missing_binding:apply("unlimited_power", true), false, "Unrestored baseline blocks re-enable")
equal(missing_binding:enabled().unlimited_power, false)
equal(power.charge, 10)
restore_fails = false
equal(missing_binding:apply("unlimited_power", true), true, "Recovery restores the true baseline before enabling")
equal(power.charge, 10)
equal(missing_binding:apply("unlimited_power", false), true)
equal(power.charge, 4, "Re-enable after failed off must preserve original charge")
equal(missing_binding:apply("unlimited_power", true), true)
restore_fails = true
equal(missing_binding:apply("unlimited_power", false), false)
restore_fails = false
equal(missing_binding:disable_all(), true)
equal(power.charge, 4)

-- Losing authority invalidates every toggle and restores oxygen. A listen
-- server itself remains allowed; only joining somebody else's host is blocked.
equal(missing_binding:apply("unlimited_oxygen", true), true)
authority = false
equal(missing_binding:refresh(), false)
equal(character.OxygenUseMultiplier, 1)
equal(character.bFreeOxygenDebug, false)
equal(missing_binding:enabled().unlimited_oxygen, false)
equal(missing_binding:apply("add_bytes", true), false)
equal(research.balance, 10000, "No additional grant on a remote client")
authority = true
lifecycle = 8
equal(missing_binding:refresh(), false, "Possessed main menu astronaut must not become ready")
equal(missing_binding:apply("add_bytes", true), false)
local menu_key = missing_binding:session_key()
lifecycle = 9
equal(missing_binding:refresh(), true, "GameStarted permits gameplay")
equal(missing_binding:session_key() ~= menu_key, true, "Lifecycle transitions invalidate IPC session")
equal(missing_binding:apply("resource_collection_10x", true), true)
lifecycle = 8
equal(collect(2).value, 2, "Main menu transition blocks writes before next polling tick")
equal(missing_binding:refresh(), false)
equal(missing_binding:enabled().resource_collection_10x, false)

-- Possession is rechecked inside the hook, not only by the polling discovery.
lifecycle = 9
equal(missing_binding:refresh(), true)
equal(missing_binding:apply("resource_collection_10x", true), true)
local possession_key = missing_binding:session_key()
local_control = false
equal(collect(4).writes, 0, "Pawn losing local control cannot receive a bonus between polls")
equal(missing_binding:refresh(), false)
equal(missing_binding:session_key() ~= possession_key, true)
equal(missing_binding:enabled().resource_collection_10x, false)
local_control = true
equal(missing_binding:refresh(), true)

-- Missing authority/game-mode methods cannot accidentally grant access.
local authority_method = character.HasAuthority
character.HasAuthority = nil
equal(missing_binding:refresh(), false)
equal(missing_binding:apply("add_bytes", true), false)
character.HasAuthority = authority_method
local mode_method = gameplay.GetGameMode
gameplay.GetGameMode = nil
equal(missing_binding:refresh(), false)
equal(missing_binding:apply("add_bytes", true), false)
gameplay.GetGameMode = mode_method
equal(missing_binding:refresh(), true)
equal(standalone, false, "Regression suite ends on an accepted non-standalone local host")

-- Selecting a multiplier changes the next event without implicitly switching
-- collection on. Off and emergency-off preserve the user's current selection.
local _, _, default_values = missing_binding:refresh()
equal(default_values.selected_collection_multiplier, 10)
equal(default_values.collection_multiplier, 1)
equal(missing_binding:apply("set_collection_multiplier", false, 20), true)
equal(missing_binding:enabled().resource_collection_10x, false)
local _, _, selected_values = missing_binding:refresh()
equal(selected_values.selected_collection_multiplier, 20)
equal(selected_values.collection_multiplier, 1)
equal(collect(3).value, 3)
equal(missing_binding:apply("resource_collection_10x", true), true)
equal(collect(3).value, 60)
equal(collect(107374183).writes, 0, "Runtime rejects 20x overflowing events")
equal(missing_binding:apply("set_collection_multiplier", false, 10), true)
equal(missing_binding:enabled().resource_collection_10x, true)
equal(collect(3).value, 30, "Selection while on applies to the next fragment")
for _, invalid_multiplier in ipairs({ 0, 15, 30, 99, 101, "20", "100", true, {} }) do
    equal(missing_binding:apply("set_collection_multiplier", false, invalid_multiplier), false)
end
equal(missing_binding:apply("set_collection_multiplier", false), false)
equal(missing_binding:apply("set_collection_multiplier", true, 20), false)
equal(missing_binding:apply("set_collection_multiplier", nil, 20), false)
equal(missing_binding:apply("resource_collection_10x", false, 20), false,
    "Multiplier parameter is rejected on a legacy toggle")
equal(missing_binding:enabled().resource_collection_10x, true)
equal(missing_binding:apply("set_collection_multiplier", false, 20), true)
equal(missing_binding:apply("resource_collection_10x", false), true)
local _, _, off_values = missing_binding:refresh()
equal(off_values.selected_collection_multiplier, 20)
equal(off_values.collection_multiplier, 1)
equal(missing_binding:apply("resource_collection_10x", true), true)
equal(collect(2).value, 40, "Legacy/F1 toggle retains the selection")
equal(missing_binding:disable_all(), true)
local _, _, disabled_values = missing_binding:refresh()
equal(disabled_values.selected_collection_multiplier, 20)
equal(disabled_values.collection_multiplier, 1)

-- 100x remains a selection until enabled, takes effect on the next event, and
-- rejects the first overflowing integer without reporting a delivered bonus.
equal(missing_binding:apply("set_collection_multiplier", false, 100), true)
local _, _, hundred_off = missing_binding:refresh()
equal(hundred_off.selected_collection_multiplier, 100)
equal(hundred_off.collection_multiplier, 1)
equal(missing_binding:enabled().resource_collection_10x, false)
equal(collect(3).value, 3)
equal(missing_binding:apply("resource_collection_10x", true), true)
local hundred_event = collect(3)
equal(hundred_event.value, 300)
equal(hundred_event.writes, 1)
local boundary_event = collect(21474836)
equal(boundary_event.value, 2147483600)
equal(boundary_event.writes, 1)
local _, _, before_hundred_overflow = missing_binding:refresh()
local overflow_event = collect(21474837)
equal(overflow_event.value, 21474837)
equal(overflow_event.writes, 0)
local _, _, after_hundred_overflow = missing_binding:refresh()
equal(after_hundred_overflow.collection_rejected_events, before_hundred_overflow.collection_rejected_events + 1)
equal(after_hundred_overflow.collection_total_fragments, before_hundred_overflow.collection_total_fragments)
equal(after_hundred_overflow.collection_bonus_fragments, before_hundred_overflow.collection_bonus_fragments)
equal(missing_binding:enabled().resource_collection_10x, true)
equal(missing_binding:apply("set_collection_multiplier", false, 20), true)
equal(collect(3).value, 60, "Changing away from 100x takes effect immediately")
equal(missing_binding:apply("set_collection_multiplier", false, 100), true)
equal(collect(3).value, 300, "Selecting 100x while on changes the next event")
equal(missing_binding:apply("resource_collection_10x", false), true)
local _, _, hundred_disabled = missing_binding:refresh()
equal(hundred_disabled.selected_collection_multiplier, 100)
equal(hundred_disabled.collection_multiplier, 1)
equal(missing_binding:apply("resource_collection_10x", true), true)
equal(collect(2).value, 200, "Re-enabling retains 100x")
equal(missing_binding:disable_all(), true)
local _, _, hundred_emergency_off = missing_binding:refresh()
equal(hundred_emergency_off.selected_collection_multiplier, 100)
equal(hundred_emergency_off.collection_multiplier, 1)
world = object("AnotherWorld")
local _, _, new_session_values = missing_binding:refresh()
equal(new_session_values.selected_collection_multiplier, 10, "A new gameplay session restores default selection")
equal(new_session_values.collection_multiplier, 1)
equal(missing_binding:enabled().resource_collection_10x, false)

-- An unloaded object's pending restoration cannot block the next astronaut.
equal(missing_binding:apply("unlimited_power", true), true)
function power:SetCurrentCharge() error("Old backpack unloaded during restoration") end
equal(missing_binding:apply("unlimited_power", false), false)
power.invalid = true
power = object("ReplacementBackpackPower", { StorageCapacity = 15, charge = 6 })
function power:GetCurrentCharge() return self.charge end
function power:SetCurrentCharge(value) self.charge = value; return value end
world = object("AfterRespawnWorld")
equal(missing_binding:refresh(), true)
equal(missing_binding:enabled().unlimited_power, false)
equal(missing_binding:apply("unlimited_power", true), true, "A dead object's pending restore is discarded")
equal(power.charge, 15)
equal(missing_binding:apply("unlimited_power", false), true)
equal(power.charge, 6, "The new backpack retains its own baseline")
print("Astroneer collection/bindings: " .. assertions .. " assertions passed")
