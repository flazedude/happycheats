local P=dofile("modules/phantom-blade/Scripts/protocol.lua")
local checks=0
local function check(value,label) checks=checks+1; assert(value,label) end
local function command(action,enabled,created,amount)
    return "schema_version=1\nsession_id=test-session\nrequest_id=0123456789abcdef0123456789abcdef\naction=" ..
        (action or "disable_all") .. "\nenabled=" .. (enabled or "0") .. "\ncreated_utc=" .. (created or "100") .. "\n" ..
        (amount~=nil and "amount="..tostring(amount).."\n" or "")
end
local function parse(text,session,now) return P.parse(text,session or "test-session",now or 100) end
for _,action in ipairs({"pbz_health","pbz_stamina"}) do
    for _,enabled in ipairs({"0","1"}) do
        local result=assert(parse(command(action,enabled)))
        check(result.action==action and result.enabled==(enabled=="1"),"Explicit toggle state")
    end
    check(parse(command(action,"0",nil,1))==nil,"Amounts cannot affect toggles")
end
for _,action in ipairs({"pbz_damage","disable_all"}) do
    check(parse(command(action))~=nil,"Action accepted")
    check(parse(command(action,"1"))==nil,"Action cannot be latched on")
end
check(assert(parse(command("pbz_damage"))).amount==2,"Default damage multiplier")
for _,amount in ipairs({1,2,20}) do
    check(assert(parse(command("pbz_damage","0",nil,amount))).amount==amount,"Bounded multiplier forwarded")
end
for _,amount in ipairs({"0","-1","21","1.0","1e1","NaN","inf","+1"," 1","1 ","1;print","99999999999999999999999999999999999999999999999999999999"}) do
    check(parse(command("pbz_damage","0",nil,amount))==nil,"Invalid damage multiplier: "..amount)
end
check(parse(command("pbz_damage","0",nil,1).."amount=2\n")==nil,"Duplicate amount rejected")
check(parse(command("disable_all","0",nil,1))==nil,"Disable all rejects amount")
check(parse(command(),"previous-session")==nil,"Previous session rejected")
check(parse(command(),nil,108)~=nil,"Eight-second age boundary accepted")
check(parse(command(),nil,109)==nil,"Expired command rejected")
check(parse(command(),nil,98)~=nil,"Two-second clock skew accepted")
check(parse(command(),nil,97)==nil,"Future command rejected")
for _,created in ipairs({"0","-1","100.0","1e2","NaN","99999999999999999999999999999999999999999999999"}) do
    check(parse(command(nil,nil,created))==nil,"Invalid command timestamp")
end
for _,enabled in ipairs({"true","false","2","-1","01"}) do
    check(parse(command("pbz_health",enabled))==nil,"Malformed toggle state")
end
for _,field in ipairs({"schema_version","session_id","request_id","action","enabled","created_utc"}) do
    check(parse((command():gsub(field.."=[^\n]*\n","")))==nil,"Missing required field: "..field)
    check(parse(command()..field.."=1\n")==nil,"Duplicate required field: "..field)
end
for _,identity in ipairs({"short",string.rep("0",31),string.rep("0",33),string.rep("A",32),string.rep("g",32)}) do
    check(parse((command():gsub("request_id=[^\n]+","request_id="..identity)))==nil,"Malformed request identity")
end
for _,action in ipairs({"console","pbz_unknown","execute","set_health","wukong_health"}) do
    check(parse(command(action))==nil,"Unknown action cannot reach a binding")
end
for _,data in ipairs({"payload=1\n","unknown=1\n","bad key=1\n","action=os.execute('x')\n","action=disable_all\0\n"}) do
    check(parse(command()..data)==nil,"Reject unknown or executable data")
end
check(parse((command():gsub("\n","\r\n")))~=nil,"Windows newlines accepted")
check(parse(command()..string.rep("\n",2049))==nil,"Oversized mailbox rejected")
check(parse(nil)==nil and parse({})==nil,"Non-string input rejected")
check(P.json({enabled=false,message='x\n"\\\0'})=='{"enabled":false,"message":"x\\n\\"\\\\\\u0000"}',"Status JSON escaping")
check(P.json({})=='{}',"Empty dictionary remains an object")
check(P.json(0/0)=='null' and P.json(math.huge)=='null' and P.json(-math.huge)=='null',"Nonfinite values cannot corrupt JSON")
print("Phantom Blade Zero protocol: "..checks.." assertions passed")
