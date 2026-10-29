-- Build 26266538 only; native caller at RVA 0x164c416 resolves GUObjectArray
-- RVA 0xddb3c40 and calls AllocateUObjectIndex at RVA 0x164dba0.
-- All 31 bytes occur exactly once in the inspected executable. RIP-relative
-- displacements are unchanged by ASLR. A different build must be re-inspected.
function Register()
    return "48 8D 0D 23 78 76 0C 48 89 44 24 28 44 8B CE 8B 44 24 60 48 8B D7 89 44 24 20 E8 6B 17 00 00"
end

function OnMatchFound(MatchAddress)
    if math.type(MatchAddress) ~= "integer" or MatchAddress < 0x10000
        or MatchAddress > 0x7FFFFFFFFFFF - 0x0C76782A then return nil end
    local ok_lea, displacement = pcall(DerefToInt32, MatchAddress + 3)
    local ok_call, call_displacement = pcall(DerefToInt32, MatchAddress + 0x1B)
    if not ok_lea or displacement ~= 0x0C767823
        or not ok_call or call_displacement ~= 0x176B then return nil end
    return MatchAddress + 7 + displacement
end
