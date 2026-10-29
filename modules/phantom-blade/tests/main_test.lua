-- Execute the real mailbox/hotkey adapter with engine, time and files mocked.
-- No game process, installed file, save, keyboard or controller is touched.
local checks=0
local function equal(actual,expected,label)
    checks=checks+1
    assert(actual==expected,(label or "Mismatch")..": "..tostring(actual).." ~= "..tostring(expected))
end
local function copy(value)
    if type(value)~="table" then return value end
    local result={}; for k,v in pairs(value) do result[k]=copy(v) end; return result
end
local now,on_thread,latest,serialized,loop=1000,false
local files,jobs,keys={},{},{}
local timers,refreshes,publications,read_limit=0,0,0
local removal_failure,open_failure,write_failure,close_failure,rename_failure,schedule_failure,queue_failure=false,false,false,false,false,false,false
local state={ready=true,key="world1/player1",flags={pbz_health=false,pbz_stamina=false,pbz_damage=false},
    available={pbz_health=true,pbz_stamina=true,pbz_damage=true}, calls={},damage=1,damage_calls=0,disables=0,ticks=0}
local function game_thread() assert(on_thread,"Engine work ran outside game thread") end
local trainer={}
function trainer:refresh()
    game_thread(); refreshes=refreshes+1
    if state.refresh_error then error("fixture refresh failure") end
    return state.ready,state.ready and "Ready" or "Load gameplay",{pbz_damage=state.damage},copy(state.available)
end
function trainer:session_key() game_thread(); return state.key end
function trainer:enabled() return copy(state.flags) end
function trainer:disable_all()
    game_thread(); state.disables=state.disables+1
    for action in pairs(state.flags) do state.flags[action]=false end
    state.damage=1
    return true,"All off"
end
function trainer:tick(reuse_refresh)
    game_thread(); state.ticks=state.ticks+1
    equal(reuse_refresh,true,"Adapter reuses only same-thread validated context")
    if state.tick_error then error("fixture tick failure") end
    if state.tick_disables then state.flags.pbz_health=false end
end
function trainer:apply(action,enabled,amount)
    game_thread()
    equal(files["command.txt"],nil,"Mailbox is consumed before mutation")
    state.calls[#state.calls+1]={action=action,enabled=enabled,amount=amount}
    if state.apply_error then error("fixture dispatch failure") end
    if action=="disable_all" then return self:disable_all() end
    if enabled==false and (action=="pbz_health" or action=="pbz_stamina") then state.flags[action]=false; return true,"Off" end
    if action=="pbz_damage" and amount==1 then state.damage=1; state.flags.pbz_damage=false; return true,"Damage restored" end
    if not state.ready then return false,"Load gameplay" end
    if state.reject then return false,"Binding refused action" end
    if action=="pbz_damage" then
        state.damage=amount; state.damage_calls=state.damage_calls+1; state.flags.pbz_damage=true
        if state.after_damage_error then error("fixture exception after dispatch") end
        return true,"Damage multiplier applied"
    end
    if state.flags[action]==nil then return false,"Unknown" end
    state.flags[action]=enabled==true; return true,"Toggle changed"
end
local P=dofile("modules/phantom-blade/Scripts/protocol.lua")
local json=P.json
P.json=function(value)
    if type(value)=="table" and value.schema_version then serialized=copy(value) end
    return json(value)
end
local function basename(path) return path:gsub("\\","/"):match("([^/]+)$") end
local fake_io={}
function fake_io.open(path,mode)
    local name=basename(path)
    assert(name=="command.txt" or name=="status.tmp","Unexpected bridge file")
    if mode=="rb" and not files[name] or mode=="wb" and open_failure then return nil end
    local file={data=mode=="wb" and "" or files[name]}
    function file:read(limit) read_limit=limit; return self.data:sub(1,limit) end
    function file:write(value)
        if write_failure then return nil,"fixture write failure" end
        self.data=self.data..value; return self
    end
    function file:close()
        if mode=="wb" then files[name]=self.data; if close_failure then return nil,"fixture close failure" end end
        return true
    end
    return file
end
local fake_os={time=function() return now end}
function fake_os.remove(path)
    if basename(path)=="command.txt" and removal_failure then return nil,"fixture removal failed" end
    files[basename(path)]=nil; return true
end
function fake_os.rename(from,to)
    if rename_failure then return nil,"fixture rename failed" end
    files[basename(to)],files[basename(from)]=files[basename(from)],nil
    latest=copy(serialized); publications=publications+1; return true
end
local env=setmetatable({io=fake_io,os=fake_os,print=function() end,Key={F1=1,F2=2,F10=10}},{__index=_G})
function env.dofile(path)
    if basename(path)=="protocol.lua" then return P end
    if basename(path)=="bindings.lua" then return {new=function() return trainer end} end
    error("Unexpected dependency")
end
function env.LoopAsync() error("Must not create background UObject work") end
function env.LoopInGameThreadWithDelay(delay,callback)
    equal(delay,100,"One 100ms game-thread timer"); timers=timers+1
    if schedule_failure then error("fixture timer failure") end
    loop=callback
end
function env.ExecuteInGameThread(callback)
    if queue_failure then error("fixture queue failure") end
    jobs[#jobs+1]=callback
end
function env.RegisterKeyBind(key,callback) assert(not keys[key],"Duplicate hotkey"); keys[key]=callback end
local function load_main() assert(loadfile("modules/phantom-blade/Scripts/main.lua","t",env))() end
local function flush()
    local count=0
    while #jobs>0 do
        count=count+1; assert(count<20,"Unbounded queued work")
        on_thread=true; table.remove(jobs,1)(); on_thread=false
    end
end
local function tick() on_thread=true; loop(); on_thread=false; flush() end
local function press(key) assert(keys[key],"Missing hotkey")(); flush() end
local function queue(action,id,enabled,amount,session,created)
    files["command.txt"]=table.concat({"schema_version=1","session_id="..(session or latest.session_id),
        "request_id="..string.format("%032x",id),"action="..action,"enabled="..(enabled and "1" or "0"),
        "created_utc="..tostring(created or now),amount and "amount="..tostring(amount) or "",""},"\n")
end
load_main()
equal(refreshes,0,"Startup does not touch game objects")
equal(state.disables,0,"Startup does not call cleanup off-thread")
equal(#state.calls,0,"Startup cannot change gameplay")
equal(latest.ready,false,"Startup publishes a waiting heartbeat")
equal(timers,1); equal(#jobs,0)
for _,key in ipairs({1,2,10}) do equal(type(keys[key]),"function","Game-focus hotkeys registered") end
local key_count=0; for _ in pairs(keys) do key_count=key_count+1 end
equal(key_count,3,"No native menu or numeric-action hotkey")
tick(); equal(latest.ready,true); equal(latest.version,"0.1.1"); equal(state.disables,1)
equal(refreshes,1,"One idle tick resolves native context only once")
local published=publications
tick(); tick(); tick(); equal(publications,published,"Stable polls coalesce status writes")
equal(refreshes,4,"Idle polling does not multiply native discovery work")
now=now+1; tick(); equal(publications,published+1,"Fresh heartbeat each second")
queue("pbz_damage",1,false,8); tick()
equal(read_limit,2049,"Read is bounded before parsing")
equal(state.damage,8); equal(state.calls[#state.calls].amount,8,"Multiplier reaches binding unchanged")
equal(latest.last_success,true); equal(latest.last_message,"Damage multiplier applied")
local acknowledged=latest.last_command
queue("pbz_damage",1,false,8); tick(); equal(state.damage_calls,1,"Same request never repeats mutation")
equal(latest.last_command,acknowledged)
queue("pbz_damage",2,false); removal_failure=true; tick(); tick()
equal(state.damage_calls,1,"Failed consumption cannot dispatch")
equal(type(files["command.txt"]),"string","Failed removal leaves intent unconsumed")
removal_failure=false; tick(); equal(state.damage_calls,2,"Successful consumption dispatches once")
equal(state.calls[#state.calls].amount,2,"Default multiplier forwarded")
tick(); equal(state.damage_calls,2)
queue("pbz_damage",3,false,3); open_failure=true; tick(); equal(state.damage_calls,3)
open_failure=false; queue("pbz_damage",3,false,3); tick(); equal(state.damage_calls,3,"Lost acknowledgement cannot replay")
state.after_damage_error=true; queue("pbz_damage",4,false,4); tick()
equal(state.damage_calls,4); equal(latest.last_success,false,"Ambiguous dispatch exception is not success")
equal(state.damage,1,"Dispatch exception requests cleanup")
state.after_damage_error=false; queue("pbz_damage",4,false,4); tick(); equal(state.damage_calls,4,"Exception cannot replay")
queue("pbz_damage",5,false,5,nil,now-9); tick(); equal(state.damage_calls,4,"Expired commands never dispatch")
queue("pbz_damage",6,false,6); removal_failure=true; tick(); now=now+9; removal_failure=false; tick()
equal(state.damage_calls,4,"A blocked mailbox expires before eventual consumption")
equal(files["command.txt"],nil)
queue("pbz_damage",1,false,8,nil,now-12); tick(); equal(state.damage_calls,4,"Expired original cannot replay after dedup pruning")
local before=#state.calls
files["command.txt"]=string.rep("x",2049); tick(); equal(#state.calls,before,"Oversized input never dispatches")
queue("pbz_unknown",7,false); tick(); equal(#state.calls,before,"Unknown action never dispatches")
queue("pbz_damage",8,false,21); tick(); equal(#state.calls,before,"Out-of-range multiplier never dispatches")
queue("pbz_health",9,true); tick(); equal(state.flags.pbz_health,true)
local previous=latest.session_id
queue("pbz_damage",10,false,10); state.key="world2/player2"; tick()
equal(latest.session_id~=previous,true,"Pawn transition rotates session")
equal(latest.last_command,""); equal(state.damage_calls,4,"Queued old-pawn command cannot target new pawn")
equal(state.flags.pbz_health,false,"Toggles cleared on session transition")
queue("pbz_stamina",11,true); tick(); equal(state.flags.pbz_stamina,true)
queue("disable_all",12,false); tick(); equal(state.flags.pbz_stamina,false)
before=#state.calls; keys[1](); equal(#state.calls,before,"Hotkey queues onto game thread")
for _=1,50 do keys[1](); keys[2]() end
equal(#jobs,1,"Repeated hotkeys cannot build an unbounded game-thread backlog")
flush(); equal(state.flags.pbz_health,true); press(1); equal(state.flags.pbz_health,false)
press(2); equal(state.flags.pbz_stamina,true)
press(10); for _,flag in pairs(state.flags) do equal(flag,false,"Emergency off clears all toggles") end
queue_failure=true; keys[1](); equal(#jobs,0); queue_failure=false
press(1); equal(state.flags.pbz_health,true,"Failed scheduling releases hotkey queue gate")
previous=latest.session_id; state.ready=false; state.key="world2/menu"; tick()
equal(latest.session_id~=previous,true)
before=#state.calls; queue("pbz_damage",13,false,3); tick()
equal(#state.calls,before,"Mailbox refuses mutation outside gameplay"); equal(latest.last_success,false)
state.flags.pbz_health=true; queue("pbz_health",14,false); tick()
equal(state.flags.pbz_health,false,"Toggle cleanup works outside gameplay")
state.damage=6; queue("pbz_damage",15,false,1); tick()
equal(state.damage,1,"Numeric reset works outside gameplay"); equal(latest.last_success,true)
queue("disable_all",16,false); tick(); equal(latest.last_success,true)
before=#state.calls; press(1); equal(#state.calls,before,"Hotkey readiness guard before binding")
state.ready=true; state.key="world3/player3"; tick()
state.available.pbz_damage=false; before=#state.calls; queue("pbz_damage",17,false,3); tick()
equal(#state.calls,before,"Unavailable control cannot dispatch"); equal(latest.last_success,false)
state.available.pbz_health=false; press(1); equal(#state.calls,before,"Unavailable hotkey cannot dispatch")
state.flags.pbz_health=true; press(1); equal(state.flags.pbz_health,false,"Unavailable toggle can still be disabled")
state.available.pbz_damage=true; state.available.pbz_health=true
press(1); state.refresh_error=true; tick(); equal(latest.ready,false); equal(state.flags.pbz_health,false)
state.refresh_error=false; tick(); equal(latest.ready,true,"Recovery retains the same timer")
state.reject=true; queue("pbz_damage",18,false,3); tick(); equal(latest.last_success,false); equal(state.damage_calls,4)
state.reject=false; state.apply_error=true; queue("pbz_damage",19,false,3); tick(); equal(latest.last_success,false)
state.apply_error=false; queue("pbz_damage",19,false,3); tick(); equal(state.damage_calls,4,"Rejected identity consumed")
press(1); state.tick_disables=true; tick()
equal(latest.enabled.pbz_health,false,"Tick cleanup published without second discovery")
state.tick_disables=false
press(1); state.tick_error=true; tick(); equal(latest.ready,false); equal(state.flags.pbz_health,false)
state.tick_error=false; tick(); equal(latest.ready,true)
for _,failure in ipairs({"write","close"}) do
    local committed,count=files["status.json"],publications
    state.ready=false; write_failure=failure=="write"; close_failure=failure=="close"; tick()
    equal(files["status.json"],committed,"Partial publication preserves committed heartbeat")
    equal(publications,count)
    write_failure=false; close_failure=false; tick(); equal(latest.ready,false,"Failed publication retried")
    state.ready=true; tick()
end
local count=publications
state.ready=false; rename_failure=true; tick(); equal(publications,count)
equal(files["status.json"],nil,"Rename failure creates recoverable heartbeat gap")
state.ready=true; rename_failure=false; tick()
equal(publications,count+1,"Returning to old state republishes after gap")
equal(latest.ready,true); equal(files["status.tmp"],nil)
equal(timers,1,"Polling and toggles never allocate extra timers")
equal(#jobs,0,"No asynchronous backlog")
local disables=state.disables
schedule_failure=true; keys={}; loop=nil; load_main()
equal(loop,nil); equal(latest.ready,false)
equal(latest.message:find("fixture timer failure",1,true)~=nil,true,"Scheduler failure visible without timer")
equal(state.disables,disables,"Failed scheduler cannot run engine cleanup off-thread")
equal(#jobs,0,"Scheduler failure cannot create fallback loop")
equal(next(keys),nil,"Scheduler failure cannot expose hotkeys without command polling")
print("Phantom Blade Zero mailbox/hotkeys: "..checks.." assertions passed")
