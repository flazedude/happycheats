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
    FUObjectArray={UObjectDeleteListeners=0xA8,PrimarySerialNumber=0xE0,bShouldRecycleObjectIndices=0xE4},
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
            seen[section][key]=tonumber(value);count=count+1
        end
    end
end
for name,fields in pairs(expected) do
    for key in pairs(fields) do check(seen[name] and seen[name][key],"Required verified offset "..name.."."..key) end
end
check(count==46,"All verified custom fields must be supplied")

-- Reproduce the failed loader boundary using invented bytes and addresses.
-- AddUObjectDeleteListener runs before the trainer Lua file and consumes a
-- TArray at the configured offset even when the UObject cache is disabled.
-- PBZ's create-listener CRITICAL_SECTION occupies 0x80..0xA7, so stock 0x80
-- interprets lock bytes as a TArray. No process or extracted asset is used here.
local function replace(bytes,offset,value)
    return bytes:sub(1,offset)..value..bytes:sub(offset+#value+1)
end
local function vector(pointer,num,capacity) return string.pack("<i8I4I4",pointer,num,capacity) end
local array=string.rep("\0",0xF0)
array=replace(array,0x70,vector(0x1000,1,4))
array=replace(array,0x80,string.pack("<i8",-1)..string.rep("\xCC",32))
array=replace(array,0xA8,vector(0x2000,2,4))
array=replace(array,0xB8,string.rep("\x55",40))
array=replace(array,0xE0,string.pack("<I4B",77,1))
local heap={
    [0x1000]=string.pack("<i8i8i8i8",0x111,0,0,0),
    [0x2000]=string.pack("<i8i8i8i8",0x222,0x333,0,0),
}
local before_array,before_create=array,heap[0x1000]
local function read_vector(offset)
    local pointer,num,capacity=string.unpack("<i8I4I4",array,offset+1)
    if pointer<=0 or not heap[pointer] or num>capacity or capacity*8>#heap[pointer] then return nil end
    return {pointer=pointer,num=num,capacity=capacity}
end
local function add_listener(offset,listener)
    local v=read_vector(offset)
    if not v then return false,"invalid vector" end
    for i=0,v.num-1 do
        if string.unpack("<i8",heap[v.pointer],i*8+1)==listener then return false,"duplicate" end
    end
    assert(v.num<v.capacity,"Fixture deliberately has spare capacity")
    heap[v.pointer]=replace(heap[v.pointer],v.num*8,string.pack("<i8",listener))
    array=replace(array,offset+8,string.pack("<I4",v.num+1))
    return true
end
local function remove_listener(offset,listener)
    local v=read_vector(offset)
    if not v then return false,"invalid vector" end
    for i=0,v.num-1 do
        if string.unpack("<i8",heap[v.pointer],i*8+1)==listener then
            -- The loader uses RemoveSingleSwap; the vector allocation stays owned.
            local last=heap[v.pointer]:sub((v.num-1)*8+1,v.num*8)
            heap[v.pointer]=replace(heap[v.pointer],i*8,last)
            array=replace(array,offset+8,string.pack("<I4",v.num-1))
            return true
        end
    end
    return false,"not found"
end
local function neighbors_unchanged()
    check(array:sub(1,0xA8)==before_array:sub(1,0xA8),"Create vector and 40-byte create lock are preserved")
    check(array:sub(0xB8+1)==before_array:sub(0xB8+1),"40-byte delete lock, serial and recycle flag are preserved")
    check(heap[0x1000]==before_create,"Create listener storage is preserved")
end
check(string.unpack("<i8",array,0x80+1)==-1,"Stock offset reads the synthetic lock sentinel as a pointer")
check(read_vector(0x80)==nil,"Stock delete-listener offset cannot describe a valid TArray")
check(not add_listener(0x80,0x444),"Malformed stock vector is rejected before unsafe dereference in the fixture")
check(array==before_array,"Stock-offset rejection does not mutate the fixture")
local configured=seen.FUObjectArray.UObjectDeleteListeners
check(add_listener(configured,0x444),"Configured loader offset appends the Lua delete listener")
local current=read_vector(configured)
check(current.num==3 and current.capacity==4,"Append updates only delete-vector count")
check(string.unpack("<i8",heap[current.pointer],17)==0x444,"New listener resides in delete-vector storage")
neighbors_unchanged()
local after_add,array_storage=array,heap[current.pointer]
local ok,reason=add_listener(configured,0x444)
check(not ok and reason=="duplicate","Duplicate registration is refused")
check(array==after_add and heap[current.pointer]==array_storage,"Duplicate detection leaves bytes unchanged")
check(remove_listener(configured,0x222),"Shutdown removal uses the same configured vector")
current=read_vector(configured)
check(current.num==2,"Removal decreases only delete-vector count")
check(string.unpack("<i8",heap[current.pointer])==0x444,"Swap removal preserves the last listener")
check(string.unpack("<i8",heap[current.pointer],9)==0x333,"Unrelated listener is preserved")
neighbors_unchanged()
local after_remove,removed_storage=array,heap[current.pointer]
check(not remove_listener(configured,0x999),"Removing an absent listener is inert")
check(array==after_remove and heap[current.pointer]==removed_storage,"Absent removal preserves bytes")
print("Phantom Blade loader profile: "..checks.." checks passed")
