-- Exercise the real extension against reflected-API boundary fixtures only.
-- No game process, files, native handles, or gameplay functions are accessed.
local checks = 0
local function equal(actual, expected, label)
    checks = checks + 1
    assert(actual == expected, (label or "Mismatch") .. ": " .. tostring(actual) .. " ~= " .. tostring(expected))
end
local INV = "dawnwalker_invulnerable"
local INVISIBLE, VISIBILITY_FLAGS = "dawnwalker_invisible", "HappyCheatsPlayerStubVisibilityFlags"
local function object() return {_valid = true} end
local function fixture()
    local f = { calls = {}, effects = {}, next_handle = 10, resolves = 0, functions = 0 }
    f.asc, f.character, f.combat = object(), object(), object()
    f.stub, f.stub_class, f.ai_library = object(), object(), object()
    f.stub[VISIBILITY_FLAGS], f.character.bHidden = 0, false
    f.visibility_writes = {}
    f.character.AbilitySystemComponent = f.asc
    f.combat_asc = f.asc
    f.default, f.class, f.library, f.combat_library = object(), object(), object(), object()
    f.default.DurationPolicy, f.default.StackingType = 1, 0
    f.state = {character = f.character, combat = f.combat, key = "validated-local-world-pawn"}
    f.attribute_names = {"MeleeDamageMultiplier", "ClawsDamageMultiplier", "MagicDamageMultiplier", "UnarmedDamageMultiplier"}
    f.attributes, f.bases, f.attribute_writes = {}, {}, {}
    for i, name in ipairs(f.attribute_names) do f.attributes[i] = {name = name}; f.bases[name] = 1 + i * 0.25 end
    function f.asc:GetAllAttributes(output)
        f.attribute_scans = (f.attribute_scans or 0) + 1
        for i, descriptor in ipairs(f.attributes) do
            output[i] = {get = function() return descriptor end}
        end
    end
    function f.library:GetDebugStringFromGameplayAttribute(attribute) return "CharacterBaseAttributeSet." .. attribute.name end
    function f.library:GetFloatAttributeBaseFromAbilitySystemComponent(asc, attribute, found)
        equal(asc, f.asc, "Attribute read uses original ASC")
        found.bSuccessfullyFoundAttribute = f.bases[attribute.name] ~= nil and f.base_unreadable ~= attribute.name
        if f.base_unreadable_once == attribute.name then found.bSuccessfullyFoundAttribute = false; f.base_unreadable_once = nil end
        return f.bases[attribute.name]
    end
    function f.combat_library:SetAttributeValue(asc, attribute, value)
        equal(asc, f.asc, "Native attribute write uses original ASC")
        f.attribute_writes[#f.attribute_writes + 1] = {name = attribute.name, value = value}
        if f.ignore_write == #f.attribute_writes then return end
        if f.setter_fails then error("setter failure") end
        f.bases[attribute.name] = string.unpack("<f", string.pack("<f", value))
        if f.after_attribute_write then f.after_attribute_write(attribute.name, value) end
    end
    function f.combat:GetAbilitySystemComponent() return f.combat_asc end
    function f.asc:MakeEffectContext() error("Opaque context must be constructed by native fallback") end
    function f.stub:GetClass() return f.actual_stub_class or f.stub_class end
    function f.stub:IsA(path) return path == "/Script/DogwoodAI.DogwoodPlayerAIStub" and not f.wrong_stub_type end
    function f.stub:IsPlayer() return f.is_player ~= false end
    function f.stub:GetActor() return f.stub_owner or f.character end
    function f.ai_library:GetAIStubFromActor(character)
        equal(character, f.character, "Look up only the validated player")
        f.stub_lookups = (f.stub_lookups or 0) + 1
        return f.mapped_stub or f.stub
    end
    function f.asc:BP_ApplyGameplayEffectToSelf(class, level, context)
        equal(class, f.class, "Only native verified effect class")
        equal(level, 1.0, "Fixed effect level")
        equal(type(context), "table", "Default native context is a table")
        equal(next(context), nil, "Native fallback creates its own opaque context")
        f.calls[#f.calls + 1] = {"apply", self}
        f.next_handle = f.next_handle + 1
        local handle = {Handle = f.next_handle, bPassedFiltersAndWasExecuted = true}
        f.effects[handle] = f.apply_count == nil and 1 or f.apply_count
        f.last_handle = handle
        if f.apply_throw then error("failure after native apply") end
        if f.apply_nil then return nil end
        if f.return_handle then return f.return_handle end
        return handle
    end
    function f.asc:RemoveActiveGameplayEffect(handle, stacks)
        f.calls[#f.calls + 1] = {"remove", self, handle, stacks}
        equal(stacks, 1, "Only one owned stack is removed")
        if f.remove_throw then error("unknown native failure") end
        if f.remove_false then return false end
        f.effects[handle] = math.max(0, (f.effects[handle] or 0) - stacks)
        return true
    end
    function f.library:GetActiveGameplayEffectStackCount(handle)
        f.count_reads = (f.count_reads or 0) + 1
        if f.count_error then error("count unavailable") end
        return f.effects[handle] or 0
    end
    local function forbidden() error("A gameplay-owned lock or unscoped removal was called") end
    f.combat.LockHealth, f.combat.UnlockHealth = forbidden, forbidden
    f.asc.RemoveActiveGameplayEffectBySourceEffect = forbidden
    local api = {
        valid = function(value) return type(value) == "table" and value._valid == true end,
        read = function(value, key)
            if f.unreadable_visibility and value == f.stub and key == VISIBILITY_FLAGS then return nil end
            if f.unreadable_hidden and value == f.character and key == "bHidden" then return nil end
            return value and value._valid and value[key]
        end,
        register_property = function(name, owner, kind, offset)
            equal(name, VISIBILITY_FLAGS); equal(owner, "/Script/DogwoodAI.DogwoodPlayerAIStub")
            equal(kind, "ByteProperty"); equal(offset, 0x30)
            f.registrations = (f.registrations or 0) + 1
            return not f.registration_failed
        end,
        write = function(value, key, number)
            equal(value, f.stub, "Write only the original player stub")
            equal(key, VISIBILITY_FLAGS, "Write only the registered player visibility byte")
            f.visibility_writes[#f.visibility_writes + 1] = number
            if f.visibility_write_failed then return false end
            value[key] = number
            if f.after_visibility_write then f.after_visibility_write(value, number) end
            return not f.visibility_write_report_failure
        end,
        invoke = function(value, method, ...) return pcall(value[method], value, ...) end,
        same = function(a, b) return a ~= nil and a._valid and a == b end,
        finite = function(value) return type(value) == "number" and value == value and math.abs(value) < math.huge end,
        has_function = function(path) f.functions = f.functions + 1; return path ~= f.missing_function end,
        resolve = function(path)
            f.resolves = f.resolves + 1
            if f.missing_object then return nil end
            if path == "/Script/DogwoodAI.DogwoodAIGameplayEffect_Invulnerability" then return f.class end
            if path == "/Script/DogwoodAI.Default__DogwoodAIGameplayEffect_Invulnerability" then return f.default end
            if path == "/Script/GameplayAbilities.Default__AbilitySystemBlueprintLibrary" then return f.library end
            if path == "/Script/DogwoodCombat.Default__CombatBlueprintFunctionLibrary" then return f.combat_library end
            if path == "/Script/DogwoodAI.DogwoodPlayerAIStub" then return f.stub_class end
            if path == "/Script/RebelAI.Default__RebelAISubsystem" then return f.ai_library end
            error("Unexpected global lookup: " .. path)
        end
    }
    f.trainer = dofile("modules/dawnwalker/Scripts/combat.lua").new(api)
    return f
end
local function enable(f)
    local available = f.trainer:refresh(f.state)
    equal(available[INV], true)
    equal(f.trainer:apply(f.state, INV, true), true)
    equal(f.trainer:enabled()[INV], true)
end

do
    local f = fixture()
    for _ = 1, 1000 do f.trainer:refresh(nil) end
    equal(f.resolves, 0, "Startup makes no global lookups")
    equal(#f.calls, 0, "Startup makes no writes")
    local available = f.trainer:refresh(f.state)
    equal(available[INV], true)
    equal(available.dawnwalker_damage, true, "Native damage setter is available")
    equal(available.dawnwalker_invisible, true, "Verified target-side AI gate is available")
    for _ = 1, 1000 do f.trainer:refresh(f.state) end
    equal(f.resolves, 7, "Off-state resolution is session cached")
    equal(f.functions, 12, "Off-state function lookups are session cached")
    equal(f.count_reads, nil, "Off-state does not poll effect functions")
    equal(f.attribute_scans, nil, "Off-state never enumerates attributes")
    equal(f.stub_lookups, nil, "Off-state never looks up the player stub")
    equal(f.registrations, 1, "Custom property registration is session cached")
    equal(#f.calls, 0, "Availability never applies an effect")
end
do
    local f = fixture(); enable(f)
    local own = f.last_handle
    local foreign = {id = 999}; f.effects[foreign] = 3
    equal(f.trainer:apply(f.state, INV, true), true, "Enable is idempotent")
    equal(#f.calls, 1, "Repeated enable never applies a second effect")
    f.trainer:refresh(f.state)
    equal(f.trainer:disable_all(), true)
    equal(f.effects[own], 0)
    equal(f.effects[foreign], 3, "Different handles are preserved")
    equal(f.calls[2][3], own, "Only returned handle is removed")
    equal(f.trainer:enabled()[INV], false)
    equal(f.trainer:disable_all(), true)
    equal(#f.calls, 2, "Repeated disable performs no write")
end
do
    local f = fixture(); enable(f)
    f.effects[f.last_handle] = 3
    equal(f.trainer:disable_all(), true)
    equal(f.effects[f.last_handle], 2, "Later game-added stacks on that handle are retained")
end
do
    local f = fixture(); enable(f)
    f.effects[f.last_handle] = 0
    f.trainer:refresh(f.state)
    equal(f.trainer:enabled()[INV], false, "Expired effects are not re-applied")
    equal(#f.calls, 1)
    equal(f.trainer:disable_all(), true)
    equal(#f.calls, 1, "Expired effect is not removed again")
end
do
    local f = fixture(); enable(f)
    local owner, handle = f.asc, f.last_handle
    f.state = {character = object(), combat = object(), key = "new-session"}
    f.trainer:refresh(f.state)
    equal(f.calls[2][2], owner, "Session cleanup uses original ASC")
    equal(f.calls[2][3], handle)
    equal(f.trainer:enabled()[INV], false, "Session changes never carry the toggle")
end
do
    local f = fixture(); enable(f)
    f.character.AbilitySystemComponent = object()
    equal(f.trainer:apply(f.state, INV, true), false, "Changed ASC is not acknowledged before refresh")
    f.trainer:refresh(f.state)
    equal(f.trainer:enabled()[INV], false, "ASC changes end prior ownership")
    equal(f.effects[f.last_handle], 0)
end
do
    local f = fixture(); enable(f)
    f.asc._valid = false
    equal(f.trainer:disable_all(), true)
    equal(#f.calls, 1, "Destroyed ASC receives no cleanup call")
end
do
    local f = fixture(); enable(f)
    f.remove_false = true
    equal(f.trainer:disable_all(), false)
    equal(f.trainer:enabled()[INV], true, "Rejected removal keeps ownership visible")
    local available = f.trainer:refresh({key = "new-session"})
    equal(available[INV], false, "Failed cleanup blocks new session")
    f.remove_false = false
    equal(f.trainer:disable_all(), true)
end
do
    local f = fixture(); enable(f)
    f.remove_throw = true
    equal(f.trainer:disable_all(), false)
    local count = #f.calls
    f.remove_throw = false
    equal(f.trainer:disable_all(), false)
    equal(#f.calls, count, "Uncertain removal is never replayed")
    equal(f.trainer:apply(f.state, INV, true), false, "Uncertain cleanup blocks another apply")
end
do
    local f = fixture(); enable(f)
    f.count_error = true
    equal(f.trainer:disable_all(), false)
    equal(#f.calls, 1, "Unreadable handles do not authorize cleanup")
    local available = f.trainer:refresh(f.state)
    equal(available[INV], false)
    f.count_error = false
    equal(f.trainer:disable_all(), true)
end
for _, configuration in ipairs({{0,0}, {2,0}, {1,1}, {1,2}}) do
    local f = fixture(); f.trainer:refresh(f.state)
    f.default.DurationPolicy, f.default.StackingType = table.unpack(configuration)
    equal(f.trainer:apply(f.state, INV, true), false, "Unexpected effect semantics fail closed")
    equal(#f.calls, 0)
end
do
    local f = fixture(); f.trainer:refresh(f.state)
    f.combat_asc = object()
    equal(f.trainer:apply(f.state, INV, true), false, "Mismatched component ownership fails closed")
    equal(#f.calls, 0)
    equal(f.trainer:apply(f.state, INV, nil), false)
    equal(f.trainer:apply(f.state, INV, 1), false)
    equal(f.trainer:apply({key="other-session"}, INV, true), false)
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), false)
    f.is_player = false
    equal(f.trainer:apply(f.state, "dawnwalker_invisible", true), false)
end
do
    local f = fixture(); f.missing_object = true
    equal(f.trainer:refresh(f.state)[INV], false)
    f.missing_object = false
    equal(f.trainer:refresh(f.state)[INV], false, "Missing binding stays failed for this session")
    equal(f.trainer:apply(f.state, INV, true), false)
    equal(#f.calls, 0)
end
for _, count in ipairs({0,2}) do
    local f = fixture(); f.apply_count = count; f.trainer:refresh(f.state)
    equal(f.trainer:apply(f.state, INV, true), false, "Unexpected application is not acknowledged")
    equal(f.effects[f.last_handle], count == 0 and 0 or 1)
    equal(f.trainer:enabled()[INV], false)
end

for _, failure in ipairs({"apply_throw", "apply_nil"}) do
    local f = fixture(); f.trainer:refresh(f.state); f[failure] = true
    equal(f.trainer:apply(f.state, INV, true), false, "Lost application handle is not acknowledged")
    equal(f.effects[f.last_handle], 1, "Native mutation may precede the failure")
    equal(f.trainer:enabled()[INV], false)
    equal(f.trainer:refresh(f.state)[INV], false, "Unknown application disables further activation")
    equal(f.trainer:disable_all(), false, "No handle means no safe cleanup")
    equal(f.trainer:apply(f.state, INV, true), false, "Unknown application never retries")
    equal(f.trainer:refresh({key = "new-session"})[INV], false, "Living uncertain ASC blocks new sessions")
    equal(#f.calls, 1, "No extra applications or unscoped removals")
    equal(f.count_reads, nil, "Missing handle is never sent to native stack lookup")
    f.asc._valid = false
    equal(f.trainer:disable_all(), true, "Destroyed ASC releases uncertain application latch")
    equal(#f.calls, 1, "Destroyed ASC receives no cleanup")
end

local function damage_enable(f)
    equal(f.trainer:refresh(f.state).dawnwalker_damage, true)
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), true)
    equal(f.trainer:enabled().dawnwalker_damage, true)
end
local function originals(f)
    for i, name in ipairs(f.attribute_names) do equal(f.bases[name], 1 + i * 0.25, "Original base restored: " .. name) end
end
do
    local f = fixture(); damage_enable(f)
    equal(f.attribute_scans, 1, "Descriptors enumerated only on enable")
    equal(#f.attribute_writes, 4)
    for i, name in ipairs(f.attribute_names) do equal(f.bases[name], (1 + i * 0.25) * 10) end
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), true)
    equal(#f.attribute_writes, 4, "Repeated enable does not multiply again")
    f.trainer:refresh(f.state)
    equal(f.attribute_scans, 1)
    equal(#f.attribute_writes, 4, "Steady refresh never reapplies damage")
    equal(f.trainer:apply(f.state, "dawnwalker_damage", false), true)
    originals(f)
    equal(f.trainer:enabled().dawnwalker_damage, false)
    equal(f.trainer:disable_all(), true)
    equal(#f.attribute_writes, 8, "Repeated disable does not restore twice")
end
do
    local f = fixture(); damage_enable(f); enable(f)
    equal(f.trainer:apply(f.state, INV, false), true)
    equal(f.trainer:enabled().dawnwalker_damage, true, "Invulnerability off preserves damage toggle")
    equal(#f.attribute_writes, 4)
    enable(f)
    equal(f.trainer:apply(f.state, "dawnwalker_damage", false), true)
    equal(f.trainer:enabled()[INV], true, "Damage off preserves invulnerability")
    equal(f.trainer:disable_all(), true)
    equal(f.effects[f.last_handle], 0)
    originals(f)
end
do
    local f = fixture(); damage_enable(f)
    f.bases.MeleeDamageMultiplier = 7.5
    f.trainer:refresh(f.state)
    equal(f.trainer:enabled().dawnwalker_damage, false)
    equal(f.bases.MeleeDamageMultiplier, 7.5, "External base change preserved")
    equal(f.bases.ClawsDamageMultiplier, 1.5, "Other owned fields restored")
    equal(f.bases.MagicDamageMultiplier, 1.75)
    equal(f.bases.UnarmedDamageMultiplier, 2)
end
do
    local f = fixture(); f.trainer:refresh(f.state); f.ignore_write = 2
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), false, "Partial setter failure is rejected")
    originals(f)
    equal(f.trainer:enabled().dawnwalker_damage, false)
    equal(f.trainer:disable_all(), true)
end
do
    local f = fixture(); f.trainer:refresh(f.state)
    f.after_attribute_write = function(name, value) if name == "MeleeDamageMultiplier" and value > 10 then error("failure after native write") end end
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), false)
    originals(f)
    equal(f.trainer:enabled().dawnwalker_damage, false, "Post-write failure rolls back")
end
do
    local f = fixture(); f.trainer:refresh(f.state)
    f.after_attribute_write = function(name, value)
        if name == "MeleeDamageMultiplier" and value > 10 then f.bases.ClawsDamageMultiplier = 3.25 end
    end
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), false)
    equal(f.bases.MeleeDamageMultiplier, 1.25)
    equal(f.bases.ClawsDamageMultiplier, 3.25, "Concurrent changes during apply are not overwritten")
    equal(f.bases.MagicDamageMultiplier, 1.75)
    equal(f.trainer:enabled().dawnwalker_damage, false)
end
do
    local f = fixture(); damage_enable(f)
    f.base_unreadable = "MeleeDamageMultiplier"
    equal(f.trainer:disable_all(), false)
    equal(f.bases.ClawsDamageMultiplier, 1.5, "Independent restore continues after read failure")
    equal(f.bases.MeleeDamageMultiplier, 12.5, "Unreadable owned value is retained")
    equal(f.trainer:refresh({key="new-session"}).dawnwalker_damage, false, "Pending cleanup blocks new session")
    f.base_unreadable = nil
    equal(f.trainer:disable_all(), true)
    originals(f)
end
do
    local f = fixture(); damage_enable(f)
    f.character.AbilitySystemComponent = object()
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), false, "Changed ASC cannot inherit a prior damage acknowledgement")
    f.trainer:refresh(f.state)
    originals(f)
    equal(f.trainer:enabled().dawnwalker_damage, false, "Replacement ASC never inherits damage override")
end
do
    local f = fixture(); damage_enable(f)
    f.asc._valid = false
    equal(f.trainer:disable_all(), true)
    equal(#f.attribute_writes, 4, "Destroyed ASC receives no attribute restore")
end
for _, value in ipairs({0, -1, math.huge, 100001}) do
    local f = fixture(); f.trainer:refresh(f.state); f.bases.MagicDamageMultiplier = value
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), false, "Unsupported base values fail before writing")
    equal(#f.attribute_writes, 0)
end
do
    local f = fixture(); f.trainer:refresh(f.state); f.bases.MagicDamageMultiplier = 0/0
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), false)
    equal(#f.attribute_writes, 0)
end
do
    local f = fixture(); f.trainer:refresh(f.state); f.attributes[5] = f.attributes[1]
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), false, "Duplicate descriptors are ambiguous")
    equal(#f.attribute_writes, 0)
end
do
    local f = fixture(); f.trainer:refresh(f.state)
    function f.library:GetDebugStringFromGameplayAttribute(attribute) return "OtherAttributeSet." .. attribute.name end
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), false, "Same field name from foreign set is rejected")
    equal(#f.attribute_writes, 0)
end
do
    local f = fixture(); f.trainer:refresh(f.state)
    local original = string.unpack("<f", string.pack("<f", 1.3))
    f.bases.MeleeDamageMultiplier = original
    f.after_attribute_write = function(name, value)
        if name == "MeleeDamageMultiplier" and value > 10 then f.base_unreadable_once = name end
    end
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), false)
    equal(f.bases.MeleeDamageMultiplier, original, "Lost readback restores exact native float32 ownership")
    equal(f.trainer:enabled().dawnwalker_damage, false)
end
do
    local f = fixture(); f.trainer:refresh(f.state)
    function f.asc:GetAllAttributes(output) output[1] = f.attributes[1] end
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), false, "Raw OUT-array wrapper mismatch fails closed")
    equal(#f.attribute_writes, 0)
end
for _, unwrapped in ipairs({false, "missing"}) do
    local f = fixture(); f.trainer:refresh(f.state)
    function f.asc:GetAllAttributes(output)
        output[1] = {get = function() if unwrapped == false then error("unreadable wrapper") end end}
    end
    equal(f.trainer:apply(f.state, "dawnwalker_damage", true), false, "Unreadable OUT-array descriptor fails before writes")
    equal(#f.attribute_writes, 0)
end
for _, handle in ipairs({{}, {Handle=0/0, bPassedFiltersAndWasExecuted=true},
    {Handle=12.5, bPassedFiltersAndWasExecuted=true}, {Handle=2147483648, bPassedFiltersAndWasExecuted=true},
    {Handle=12}, {Handle=12, bPassedFiltersAndWasExecuted="true"}}) do
    local f = fixture(); f.trainer:refresh(f.state); f.return_handle = handle
    equal(f.trainer:apply(f.state, INV, true), false, "Malformed active-effect identity is not accepted")
    equal(f.trainer:enabled()[INV], false)
    equal(f.trainer:disable_all(), false, "Unknown applied handle is never removed by a guessed ID")
    equal(#f.calls, 1)
end
do
    local f = fixture(); f.trainer:refresh(f.state)
    function f.asc:BP_ApplyGameplayEffectToSelf() return {Handle=-1, bPassedFiltersAndWasExecuted=false} end
    equal(f.trainer:apply(f.state, INV, true), false, "Native invalid handle is rejected")
    equal(f.trainer:disable_all(), true, "Native rejection creates no cleanup lease")
    equal(f.count_reads, nil, "No count lookup for an invalid native handle")
end

local function invisible_enable(f)
    equal(f.trainer:refresh(f.state)[INVISIBLE], true)
    equal(f.trainer:apply(f.state, INVISIBLE, true), true)
    equal(f.trainer:enabled()[INVISIBLE], true)
end
do
    local f = fixture(); f.stub[VISIBILITY_FLAGS] = 0xa3; invisible_enable(f)
    equal(f.stub[VISIBILITY_FLAGS], 0xb3, "Only hidden-for-AI mask is set")
    equal(f.character.bHidden, false, "Player model stays visible")
    equal(f.trainer:apply(f.state, INVISIBLE, true), true)
    equal(#f.visibility_writes, 1, "Repeated enable never rewrites flags")
    f.stub[VISIBILITY_FLAGS] = f.stub[VISIBILITY_FLAGS] | 0x04
    f.trainer:refresh(f.state)
    equal(#f.visibility_writes, 1, "Active refresh does not enforce or rewrite flags")
    equal(f.trainer:apply(f.state, INVISIBLE, false), true)
    equal(f.stub[VISIBILITY_FLAGS], 0xa7, "Cleanup preserves unrelated game-changed bits")
    equal(f.trainer:enabled()[INVISIBLE], false)
    equal(f.trainer:disable_all(), true)
    equal(#f.visibility_writes, 2, "Repeated cleanup is idempotent")
end
for _, setup in ipairs({
    function(f) f.stub[VISIBILITY_FLAGS] = 0x10 end,
    function(f) f.character.bHidden = true end,
    function(f) f.character.bHidden = nil end,
    function(f) f.actual_stub_class = object() end,
    function(f) f.wrong_stub_type = true end,
    function(f) f.is_player = false end,
    function(f) f.stub_owner = object() end,
    function(f) f.stub._valid = false end,
}) do
    local f = fixture(); f.trainer:refresh(f.state); setup(f)
    equal(f.trainer:apply(f.state, INVISIBLE, true), false, "Unverified or game-owned player visibility fails closed")
    equal(#f.visibility_writes, 0)
end
for _, byte in ipairs({-1, 256, 1.5, math.huge, 0/0}) do
    local f = fixture(); f.trainer:refresh(f.state); f.stub[VISIBILITY_FLAGS] = byte
    equal(f.trainer:apply(f.state, INVISIBLE, true), false, "Visibility must be a bounded integer byte")
    equal(#f.visibility_writes, 0)
end
do
    local f = fixture(); f.registration_failed = true
    equal(f.trainer:refresh(f.state)[INVISIBLE], false)
    equal(f.trainer:apply(f.state, INVISIBLE, true), false)
    equal(#f.visibility_writes, 0, "Failed property binding performs no write")
end
do
    local f = fixture(); invisible_enable(f)
    f.stub[VISIBILITY_FLAGS] = 0x04
    f.trainer:refresh(f.state)
    equal(f.trainer:enabled()[INVISIBLE], false, "Game-cleared invisibility is not reapplied")
    equal(#f.visibility_writes, 1)
end
do
    local f = fixture(); invisible_enable(f)
    f.mapped_stub = object(); f.mapped_stub[VISIBILITY_FLAGS] = 0x01
    equal(f.trainer:apply(f.state, INVISIBLE, true), false, "Old valid stub cannot acknowledge a new mapped stub")
    f.trainer:refresh(f.state)
    equal(f.stub[VISIBILITY_FLAGS], 0, "Original still-owned stub is restored")
    equal(f.mapped_stub[VISIBILITY_FLAGS], 0x01, "Replacement stub receives no inherited override")
    equal(f.trainer:enabled()[INVISIBLE], false)
end
do
    local f = fixture(); invisible_enable(f)
    f.stub_owner = object()
    equal(f.trainer:disable_all(), false, "Reassigned stub is not written during cleanup")
    equal(#f.visibility_writes, 1)
    equal(f.trainer:refresh({key="new-session"})[INVISIBLE], false, "Unresolved old owner blocks a new session")
    f.stub_owner = nil
    equal(f.trainer:disable_all(), true)
    equal(f.stub[VISIBILITY_FLAGS], 0)
end
do
    local f = fixture(); invisible_enable(f)
    f.trainer:refresh({key="new-session", character=object(), combat=object()})
    equal(f.stub[VISIBILITY_FLAGS], 0, "Session transition restores the original owner")
    equal(f.trainer:enabled()[INVISIBLE], false)
end
do
    local f = fixture(); invisible_enable(f); f.stub._valid = false
    equal(f.trainer:disable_all(), true)
    equal(#f.visibility_writes, 1, "Destroyed stubs are never written")
end
do
    local f = fixture(); invisible_enable(f); f.character._valid = false
    equal(f.trainer:disable_all(), false, "Living stub with destroyed actor remains pending")
    equal(#f.visibility_writes, 1)
    f.stub._valid = false
    equal(f.trainer:disable_all(), true)
end
do
    local f = fixture(); invisible_enable(f); f.character.bHidden = true
    equal(f.trainer:apply(f.state, INVISIBLE, false), false, "Actor hiding defers cleanup")
    equal(#f.visibility_writes, 1, "Game-controlled hide is not cleared")
    equal(f.trainer:apply(f.state, INVISIBLE, true), false, "Pending visibility cleanup cannot enable again")
    equal(f.trainer:refresh(f.state)[INVISIBLE], false)
    f.character.bHidden = false
    f.trainer:refresh(f.state)
    equal(f.stub[VISIBILITY_FLAGS], 0, "Unhide automatically completes pending cleanup")
    equal(f.trainer:enabled()[INVISIBLE], false)
end
do
    local f = fixture(); invisible_enable(f); f.character.bHidden = true
    equal(f.trainer:refresh(f.state)[INVISIBLE], false, "Automatic game hide defers cleanup")
    f.stub[VISIBILITY_FLAGS] = 0
    f.trainer:refresh(f.state)
    equal(f.trainer:enabled()[INVISIBLE], false)
    equal(#f.visibility_writes, 1, "Game-cleared bit retires pending lease without writing")
end
for _, failed_read in ipairs({"unreadable_visibility", "unreadable_hidden"}) do
    local f = fixture(); invisible_enable(f); f[failed_read] = true
    equal(f.trainer:disable_all(), false)
    equal(#f.visibility_writes, 1, "Unreadable visibility never authorizes a restore")
    f[failed_read] = nil
    f.trainer:refresh(f.state)
    equal(f.stub[VISIBILITY_FLAGS], 0, "Recovered reads finish pending cleanup")
    equal(f.trainer:enabled()[INVISIBLE], false)
end
do
    local f = fixture(); f.trainer:refresh(f.state); f.visibility_write_failed = true
    equal(f.trainer:apply(f.state, INVISIBLE, true), false)
    equal(f.trainer:enabled()[INVISIBLE], false)
    equal(f.stub[VISIBILITY_FLAGS], 0)
    equal(f.trainer:disable_all(), true)
end
do
    local f = fixture(); f.trainer:refresh(f.state); f.visibility_write_report_failure = true
    equal(f.trainer:apply(f.state, INVISIBLE, true), false, "Post-write failure is rolled back")
    equal(f.stub[VISIBILITY_FLAGS], 0)
    equal(f.trainer:enabled()[INVISIBLE], false)
end
do
    local f = fixture(); f.trainer:refresh(f.state)
    f.after_visibility_write = function(_, value) if value & 0x10 ~= 0 then f.unreadable_visibility = true end end
    equal(f.trainer:apply(f.state, INVISIBLE, true), false, "Lost enable readback retains only a cleanup lease")
    equal(f.trainer:enabled()[INVISIBLE], false, "Unconfirmed application is never reported active")
    equal(f.trainer:apply(f.state, INVISIBLE, true), false)
    f.unreadable_visibility = false
    f.trainer:refresh(f.state)
    equal(f.stub[VISIBILITY_FLAGS], 0)
    equal(#f.visibility_writes, 2)
end
do
    local f = fixture(); invisible_enable(f); enable(f); damage_enable(f)
    equal(f.trainer:apply(f.state, INVISIBLE, false), true)
    equal(f.trainer:enabled()[INV], true, "Invisibility cleanup preserves owned invulnerability")
    equal(f.trainer:enabled().dawnwalker_damage, true, "Invisibility cleanup preserves damage")
    invisible_enable(f)
    equal(f.trainer:disable_all(), true)
    equal(f.stub[VISIBILITY_FLAGS], 0)
    equal(f.effects[f.last_handle], 0)
    originals(f)
end
print("Dawnwalker combat: " .. checks .. " assertions passed")
