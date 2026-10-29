-- Offline ABI fixture for the inspected build; no process is opened.
local checks=0
local function check(value,label) checks=checks+1; assert(value,label) end
dofile("modules/phantom-blade/UE4SS_Signatures/GUObjectArray.lua")
check(Register()=="48 8D 0D 23 78 76 0C 48 89 44 24 28 44 8B CE 8B 44 24 60 48 8B D7 89 44 24 20 E8 6B 17 00 00","Exact native caller signature")
for _,base in ipairs({0x140000000,0x7FF600000000}) do
    local match=base+0x164c416
    DerefToInt32=function(address)
        if address==match+3 then return 0x0C767823 end
        if address==match+0x1B then return 0x176B end
        error("Unexpected read")
    end
    check(OnMatchFound(match)==base+0xddb3c40,"ASLR-relative global address")
    DerefToInt32=function(address) return address==match+3 and 0x0C767823 or 0 end
    check(OnMatchFound(match)==nil,"Reject changed native call")
    DerefToInt32=function(address) return address==match+3 and 0 or 0x176B end
    check(OnMatchFound(match)==nil,"Reject changed global displacement")
    DerefToInt32=function() return nil end
    check(OnMatchFound(match)==nil,"Reject unreadable displacement")
    DerefToInt32=function() error("Unavailable") end
    check(OnMatchFound(match)==nil,"Reject failed read")
end
for _,value in ipairs({0,-1,0x800000000000,1.5,"address",false}) do
    check(OnMatchFound(value)==nil,"Reject invalid candidate address")
end
check(OnMatchFound(nil)==nil,"Reject missing candidate address")

local expected={
    TUObjectArray={MaxChunks=0,NumChunks=4,Objects=8,NumElements=16,MaxElements=20,PreAllocatedObjects=24,UEP_TotalSize=32},
    FField={Next=0x10,Owner=0x18},
    FFieldClass={ConstructFn=0,CastFlags=0x10,Id=0x18,Name=0x30},
    UStruct={PropertiesSize=0xA8,MinAlignment=0xAC,StructStateFlags=0xAE,Script=0xB0,PropertyLink=0xC0,
        RefLink=0xC8,DestructorLink=0xD0,PostConstructLink=0xD8,UEP_TotalSize=0x100},
    UFunction={FunctionFlags=0x100,NumParms=0x104,ParmsSize=0x106,ReturnValueOffset=0x108,FirstPropertyToInit=0x110,
        EventGraphFunction=0x118,EventGraphCallOffset=0x120,Func=0x128,UEP_TotalSize=0x130},
    UClass={ClassFlags=0x124,ClassCastFlags=0x128,ClassDefaultObject=0x160,FuncMap=0x180,FuncMapLock=0x1D0,
        AllFunctionsCache=0x1D8,AllFunctionsCacheLock=0x228,UEP_TotalSize=0x258},
    UScriptStruct={StructFlags=0x100,bPrepareCppStructOpsCompleted=0x104,CppStructOps=0x108,UEP_TotalSize=0x110}
}
local input=assert(io.open("modules/phantom-blade/MemberVariableLayout.ini","rb"))
local contents=input:read("a");input:close()
local section,seen,count=nil,{},0
for line in contents:gmatch("[^\r\n]+") do
    if not line:match("^%s*;") and not line:match("^%s*$") then
        local header=line:match("^%s*%[([^%]]+)%]%s*$")
        if header then
            check(expected[header]~=nil and seen[header]==nil,"Only verified layout sections, with no duplicates")
            section=header;seen[header]={}
        else
            local key,value=line:match("^%s*([%w_]+)%s*=%s*(0x%x+)%s*$")
            check(section~=nil and key~=nil and not seen[section][key] and expected[section][key]==tonumber(value),"Known offset, no duplicate or unsupported override")
            seen[section][key]=true;count=count+1
        end
    end
end
for name,fields in pairs(expected) do
    for key in pairs(fields) do check(seen[name] and seen[name][key],"Required verified offset "..name.."."..key) end
end
check(count==43,"All verified custom fields must be supplied")
print("Phantom Blade loader profile: "..checks.." checks passed")
