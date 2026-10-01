local P=dofile("modules/hotel-architect/Scripts/protocol.lua")
local checks=0
local function check(value,label) checks=checks+1; assert(value,label) end
local function command(action,enabled,amount,created)
    return "schema_version=1\nsession_id=hotel-session\nrequest_id=0123456789abcdef0123456789abcdef\naction="..
        action.."\nenabled="..(enabled or "0").."\ncreated_utc="..(created or "100").."\n"..
        (amount~=nil and "amount="..tostring(amount).."\n" or "")
end
local function parse(raw,session,now) return P.parse(raw,session or "hotel-session",now or 100) end
local count=0
for action in pairs(P.toggles) do
    count=count+1
    for _,enabled in ipairs({"0","1"}) do check(parse(command(action,enabled))~=nil,"Toggle state") end
    check(parse(command(action,"1",1))==nil,"No amounts for toggles")
end
for action,spec in pairs(P.numbers) do
    count=count+1
    for _,amount in ipairs({spec[1],spec[2]}) do check(assert(parse(command(action,"0",amount))).amount==amount,"Editor boundary") end
    check(parse(command(action,"0"))==nil,"Numeric amount is required")
    check(parse(command(action,"1",spec[1]))==nil,"Numeric edits cannot latch")
    for _,value in ipairs({spec[1]-1,spec[2]+1,"NaN","inf","1e2","+1",".1","1.","1;evil",string.rep("9",100)}) do
        check(parse(command(action,"0",value))==nil,"Reject invalid amount")
    end
    if spec[3] then check(parse(command(action,"0",spec[1]+.25))==nil,"Integers only") end
end
check(count==24,"All 24 screenshot controls included")
check(parse(command("disable_all"))~=nil,"Emergency cleanup")
check(parse(command("ha_health","0",12.5))~=nil,"Decimal percentages accepted")
local valid=command("ha_money","0",500)
check(parse(valid,"old-session")==nil,"Stale selection/world session")
check(parse(valid,nil,108)~=nil,"TTL boundary accepted"); check(parse(valid,nil,109)==nil,"TTL expired")
check(parse(valid,nil,98)~=nil,"Clock skew boundary"); check(parse(valid,nil,97)==nil,"Future request")
for _,field in ipairs({"schema_version","session_id","request_id","action","enabled","created_utc","amount"}) do
    check(parse((valid:gsub(field.."=[^\n]*\n","")))==nil,"Missing field")
    check(parse(valid..field.."=1\n")==nil,"Duplicate field")
end
for _,suffix in ipairs({"evil=1\n","payload=os.execute\n","\0\n"}) do check(parse(valid..suffix)==nil,"Unexpected data") end
check(parse(command("unknown","0",1))==nil,"Unknown action")
check(parse(valid..string.rep("\n",2049))==nil,"Bounded input")
check(parse(nil)==nil and parse({})==nil,"Input type")
check(parse((valid:gsub("\n","\r\n")))~=nil,"CRLF")
check(P.json({message='x\n"\\\0',enabled=false})=='{"enabled":false,"message":"x\\n\\"\\\\\\u0000"}',"JSON escapes")
check(P.json(0/0)=="null" and P.json(math.huge)=="null","Finite JSON")
print("Hotel Architect protocol: "..checks.." assertions passed")
