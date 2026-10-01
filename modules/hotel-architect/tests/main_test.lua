-- Actual mailbox/timer/hotkeys with synthetic files, engine and clock only.
local checks=0
local function eq(a,b,label) checks=checks+1; assert(a==b,(label or "Mismatch")..": "..tostring(a).." ~= "..tostring(b)) end
local function copy(x) if type(x)~="table" then return x end; local r={}; for k,v in pairs(x) do r[k]=copy(v) end; return r end
local P=dofile("modules/hotel-architect/Scripts/protocol.lua")
local json=P.json
local serialized,latest,callback
P.json=function(value) if type(value)=="table" and value.schema_version then serialized=copy(value) end; return json(value) end
local now,on_thread=1000,false
local files,keys,jobs={},{},{}
local state={key="world1:person1:item1",ready=true,flags={ha_no_needs=false,ha_always_noon=false,ha_furniture_condition=false},calls={},edits=0}
local failures={}
local publications,timers,read_limit=0,0,0
local function thread() assert(on_thread,"UObject adapter work must run on game thread") end
local trainer={}
function trainer:enabled() return copy(state.flags) end
function trainer:session_key() thread(); return state.key end
function trainer:disable_all() for action in pairs(state.flags) do state.flags[action]=false end; return true,"Off" end
function trainer:refresh()
    thread(); if failures.refresh then error("Refresh failed") end
    local available={}; for action in pairs(P.numbers) do available[action]=state.ready end; for action in pairs(P.toggles) do available[action]=state.ready end
    return state.ready,"Load hotel",{diagnostic_ha_world_id="world1",ha_money=state.edits},available
end
function trainer:apply(action,enabled,amount)
    thread(); eq(files["command.txt"],nil,"Consume before mutation")
    state.calls[#state.calls+1]={action=action,enabled=enabled,amount=amount}
    if action=="disable_all" then return self:disable_all() end
    if state.flags[action]~=nil and enabled==false then state.flags[action]=false; return true,"Off" end
    if not state.ready then return false,"Load hotel" end
    if failures.apply then error("Apply failed") end
    if P.numbers[action] then state.edits=state.edits+1; if failures.after_edit then error("Ambiguous mutation") end; return true,"Edited once" end
    state.flags[action]=enabled; return true,"Toggled"
end
function trainer:tick(reuse) thread(); eq(reuse,true); if failures.tick then error("Tick failed") end end
local function basename(path) return path:gsub("\\","/"):match("([^/]+)$") end
local fake_io={open=function(path,mode)
    local name=basename(path)
    if mode=="rb" and not files[name] or mode=="wb" and failures.open then return nil end
    local file={data=mode=="wb" and "" or files[name]}
    function file:read(limit) read_limit=limit; return self.data:sub(1,limit) end
    function file:write(data) if failures.write then return nil end; self.data=self.data..data; return self end
    function file:close() if mode=="wb" then files[name]=self.data; if failures.close then return nil end end; return true end
    return file
end}
local fake_os={time=function() return now end}
function fake_os.remove(path) if basename(path)=="command.txt" and failures.remove then return nil end; files[basename(path)]=nil; return true end
function fake_os.rename(from,to) if failures.rename then return nil end; files[basename(to)]=files[basename(from)]; files[basename(from)]=nil; latest=copy(serialized); publications=publications+1; return true end
local env=setmetatable({io=fake_io,os=fake_os,print=function() end,Key={F1=1,F2=2,F3=3,F4=4,F10=10}}, {__index=_G})
function env.dofile(path) if basename(path)=="protocol.lua" then return P end; if basename(path)=="bindings.lua" then return {new=function() return trainer end} end; error("Unexpected file") end
function env.LoopInGameThreadWithDelay(delay,work) eq(delay,100); callback=work; timers=timers+1 end
function env.ExecuteInGameThread(work) jobs[#jobs+1]=work end
function env.RegisterKeyBind(key,work) assert(not keys[key]); keys[key]=work end
local function flush() while #jobs>0 do on_thread=true; table.remove(jobs,1)(); on_thread=false end end
local function tick() on_thread=true; callback(); on_thread=false; flush() end
local function press(key) keys[key](); flush() end
local function queue(action,number,amount,enabled,session,created)
    files["command.txt"]=table.concat({"schema_version=1","session_id="..(session or latest.session_id),"request_id="..string.format("%032x",number),
        "action="..action,"enabled="..(enabled and "1" or "0"),"created_utc="..(created or now),amount and "amount="..amount or "",""},"\n")
end
assert(loadfile("modules/hotel-architect/Scripts/main.lua","t",env))()
eq(latest.ready,false); eq(latest.updated_utc,now); eq(#state.calls,0); eq(timers,1); eq(keys[4],nil,"No Lua aging hotkey"); eq(keys[10],nil,"Disable All uses the combined client backend")
tick(); eq(latest.ready,true)
local count=publications; tick(); tick(); eq(publications,count,"Stable status writes coalesce")
now=now+1; tick(); eq(publications,count+1); eq(latest.updated_utc,now)
queue("ha_money",1,1234); tick(); eq(read_limit,2049); eq(state.edits,1); eq(state.calls[#state.calls].amount,1234); eq(latest.last_success,true)
queue("ha_money",1,1234); tick(); eq(state.edits,1,"Duplicate never replays")
queue("ha_stars",2,5); failures.remove=true; tick(); eq(state.edits,1); failures.remove=false; tick(); eq(state.edits,2)
failures.after_edit=true; queue("ha_money",3,900); tick(); eq(state.edits,3); eq(latest.last_success,false)
failures.after_edit=false; queue("ha_money",3,900); tick(); eq(state.edits,3,"Ambiguous mutation is consumed once")
queue("ha_money",4,1000,false,nil,now-9); tick(); eq(state.edits,3,"TTL rejects expired request")
queue("ha_money",5,1000,false,nil,now+3); tick(); eq(state.edits,3,"Future request rejected")
queue("ha_no_needs",6,nil,true); tick(); eq(state.flags.ha_no_needs,true)
local previous=latest.session_id
queue("ha_money",7,2000); state.key="world1:person2:item1"; tick()
eq(latest.session_id~=previous,true); eq(state.edits,3); eq(state.flags.ha_no_needs,false)
press(1); eq(state.flags.ha_no_needs,true)
press(2); eq(state.flags.ha_always_noon,true)
press(3); eq(state.flags.ha_furniture_condition,true)
queue("disable_all",8); tick(); for _,flag in pairs(state.flags) do eq(flag,false) end
state.ready=false; state.key="menu"; tick(); local calls=#state.calls
queue("ha_money",9,100); tick(); eq(#state.calls,calls,"No mutation outside hotel")
queue("disable_all",10); tick(); eq(latest.last_success,true)
state.ready=true; state.key="world2"; tick(); press(1)
failures.refresh=true; tick(); eq(latest.ready,false); eq(state.flags.ha_no_needs,false)
failures.refresh=false; tick(); eq(latest.ready,true)
for _,failure in ipairs({"write","close"}) do
    local committed=files["status.json"]
    failures[failure]=true; state.ready=false; tick(); eq(files["status.json"],committed,"Failed publication preserves committed heartbeat")
    failures[failure]=nil; tick(); eq(latest.ready,false)
    state.ready=true; tick()
end
state.ready=false; failures.rename=true; tick(); eq(files["status.json"],nil)
failures.rename=false; tick(); eq(latest.ready,false,"Publication gap repaired")
eq(timers,1); eq(#jobs,0)
print("Hotel Architect mailbox/hotkeys: "..checks.." assertions passed")
