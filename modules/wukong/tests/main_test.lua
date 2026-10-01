-- Execute the real mailbox and hotkey adapter with engine, time and files mocked.
-- No game process, save, installed file, keyboard or controller is touched.
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
local removal_failure,open_failure,write_failure,close_failure,rename_failure,schedule_failure=false,false,false,false,false,false
local state={ready=true,key="world1/player1",flags={wukong_health=false,wukong_mana=false,wukong_stamina=false,wukong_damage=false,wukong_gourd=false},
    calls={},grants=0,focus=0,disables=0,ticks=0}
local function game_thread() assert(on_thread,"Engine work ran outside game thread") end
local trainer={}
function trainer:refresh()
    game_thread(); refreshes=refreshes+1
    if state.refresh_error then error("fixture refresh failure") end
    return state.ready,state.ready and "Ready" or "Load gameplay",{grants=state.grants,focus=state.focus},
        {wukong_health=true,wukong_mana=true,wukong_stamina=true,wukong_damage=true,wukong_gourd=true,wukong_focus=true,wukong_xp=true,wukong_sparks=true,wukong_will=true}
end
function trainer:session_key() game_thread(); return state.key end
function trainer:enabled() return copy(state.flags) end
function trainer:disable_all()
    state.disables=state.disables+1
    for action in pairs(state.flags) do state.flags[action]=false end
    return true,"All off"
end
function trainer:tick(reuse_refresh)
    game_thread(); state.ticks=state.ticks+1
    equal(reuse_refresh,true,"Adapter reuses only its same-thread validated context")
    if state.tick_error then error("fixture tick failure") end
    if state.tick_disables then state.flags.wukong_health=false end
end
function trainer:apply(action,enabled,amount)
    game_thread()
    equal(files["command.txt"],nil,"Mailbox is consumed before mutation")
    state.calls[#state.calls+1]={action=action,enabled=enabled,amount=amount}
    if state.apply_error then error("fixture dispatch failure") end
    if action=="disable_all" then return self:disable_all() end
    if enabled==false and state.flags[action]~=nil then state.flags[action]=false; return true,"Off" end
    if not state.ready then return false,"Load gameplay" end
    if state.reject then return false,"Binding refused action" end
    if action=="wukong_focus" then state.focus=state.focus+1; return true,"Focus filled" end
    if action=="wukong_xp" or action=="wukong_sparks" or action=="wukong_will" then
        state.grants=state.grants+1
        if state.after_grant_error then error("fixture exception after dispatch") end
        return true,"Progression command sent once; check the in-game total."
    end
    if state.flags[action]==nil then return false,"Unknown" end
    state.flags[action]=enabled==true; return true,"Toggle changed"
end
local P=dofile("modules/wukong/Scripts/protocol.lua")
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
local env=setmetatable({io=fake_io,os=fake_os,print=function() end,Key={F1=1,F2=2,F3=3,F4=4,F5=5,F6=6,F7=7,F8=8,F9=9,F10=10}},{__index=_G})
function env.dofile(path)
    if basename(path)=="protocol.lua" then return P end
    if basename(path)=="bindings.lua" then return {new=function() return trainer end} end
    error("Unexpected dependency")
end
function env.LoopAsync() error("Must not create background UObject work") end
function env.LoopInGameThreadWithDelay(delay,callback)
    equal(delay,100,"One 100ms game-thread timer")
    timers=timers+1
    if schedule_failure then error("fixture timer failure") end
    loop=callback
end
function env.ExecuteInGameThread(callback) jobs[#jobs+1]=callback end
function env.RegisterKeyBind(key,callback) assert(not keys[key],"Duplicate hotkey"); keys[key]=callback end
local function load_main() assert(loadfile("modules/wukong/Scripts/main.lua","t",env))() end
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
equal(#state.calls,0,"Startup cannot grant progression")
equal(latest.ready,false,"Startup publishes a disconnected heartbeat")
equal(timers,1); equal(#jobs,0)
for key=1,10 do equal(type(keys[key]),"function","All game-focus hotkeys registered") end
tick(); equal(latest.ready,true); equal(latest.version,"0.1.1"); equal(state.disables,1)
equal(refreshes,1,"One idle tick resolves native context only once")
local published=publications
tick(); tick(); tick(); equal(publications,published,"Stable polls coalesce status writes")
equal(refreshes,4,"Idle polling does not multiply native discovery work")
now=now+1; tick(); equal(publications,published+1,"Unchanged controls receive a fresh heartbeat each second")
queue("wukong_xp",1,false,4321); tick()
equal(read_limit,2049,"Read is bounded before parsing")
equal(state.grants,1); equal(state.calls[#state.calls].amount,4321,"Amount reaches binding unchanged")
equal(latest.last_success,true); equal(latest.last_message,"Progression command sent once; check the in-game total.")
local acknowledged=latest.last_command
queue("wukong_xp",1,false,4321); tick(); equal(state.grants,1,"Same request never repeats a grant")
equal(latest.last_command,acknowledged)
queue("wukong_sparks",2,false); removal_failure=true; tick(); tick()
equal(state.grants,1,"Failed consumption cannot dispatch a grant")
equal(type(files["command.txt"]),"string","Failed removal leaves intent unconsumed")
removal_failure=false; tick(); equal(state.grants,2,"Successful consumption dispatches once")
equal(state.calls[#state.calls].amount,1,"Default spark amount forwarded")
tick(); equal(state.grants,2)
queue("wukong_will",3,false); open_failure=true; tick(); equal(state.grants,3)
open_failure=false; queue("wukong_will",3,false); tick(); equal(state.grants,3,"Lost acknowledgement cannot replay progression")
equal(state.calls[#state.calls].amount,1000)
state.after_grant_error=true; queue("wukong_xp",4,false); tick()
equal(state.grants,4); equal(latest.last_success,false,"Ambiguous dispatch exception is not success")
state.after_grant_error=false; queue("wukong_xp",4,false); tick(); equal(state.grants,4,"Exception after mutation cannot repeat a grant")
queue("wukong_xp",5,false,nil,nil,now-9); tick(); equal(state.grants,4,"Expired commands never reach binding")
queue("wukong_xp",6,false); removal_failure=true; tick(); now=now+9; removal_failure=false; tick()
equal(state.grants,4,"A blocked mailbox must still expire before eventual consumption")
equal(files["command.txt"],nil)
queue("wukong_xp",1,false,nil,nil,now-12); tick(); equal(state.grants,4,"Expired original cannot replay after dedup pruning")
local before=#state.calls
files["command.txt"]=string.rep("x",2049); tick(); equal(#state.calls,before,"Oversized input never dispatches")
queue("wukong_unknown",7,false); tick(); equal(#state.calls,before,"Unknown action never dispatches")
queue("wukong_xp",8,false,1000001); tick(); equal(#state.calls,before,"Out-of-range grant never dispatches")
queue("wukong_health",9,true); tick(); equal(state.flags.wukong_health,true)
local previous=latest.session_id
queue("wukong_xp",10,false); state.key="world2/player2"; tick()
equal(latest.session_id~=previous,true,"Pawn transition rotates session")
equal(latest.last_command,""); equal(state.grants,4,"Queued old-pawn grant cannot target new pawn")
equal(state.flags.wukong_health,false,"Toggles are cleared on session transition")
queue("wukong_mana",11,true); tick(); equal(state.flags.wukong_mana,true)
queue("disable_all",12,false); tick(); equal(state.flags.wukong_mana,false)
before=#state.calls; keys[1](); equal(#state.calls,before,"Hotkey queues onto the game thread"); flush()
equal(state.flags.wukong_health,true); press(1); equal(state.flags.wukong_health,false)
press(2); press(3); press(4); press(5)
press(6); equal(state.focus,1); equal(state.calls[#state.calls].enabled,false,"One-shot hotkey does not enable a toggle")
press(7); press(8); press(9); equal(state.grants,7,"Each progression hotkey dispatches one request")
press(10); for _,flag in pairs(state.flags) do equal(flag,false,"Emergency off clears all toggles") end
previous=latest.session_id; state.ready=false; state.key="world2/menu"; tick()
equal(latest.session_id~=previous,true)
before=#state.calls; queue("wukong_xp",13,false); tick()
equal(#state.calls,before,"Mailbox refuses progression outside gameplay"); equal(latest.last_success,false)
state.flags.wukong_health=true; queue("wukong_health",14,false); tick()
equal(state.flags.wukong_health,false,"Cleanup still works outside gameplay")
queue("disable_all",15,false); tick(); equal(latest.last_success,true)
local grants=state.grants; press(7); equal(state.grants,grants,"Binding readiness guards hotkey progression")
state.ready=true; state.key="world3/player3"; tick(); press(1)
state.refresh_error=true; tick(); equal(latest.ready,false); equal(state.flags.wukong_health,false)
state.refresh_error=false; tick(); equal(latest.ready,true,"Recovery retains the same timer")
state.reject=true; queue("wukong_xp",16,false); tick(); equal(latest.last_success,false); equal(state.grants,grants)
state.reject=false; state.apply_error=true; queue("wukong_xp",17,false); tick(); equal(latest.last_success,false)
state.apply_error=false; queue("wukong_xp",17,false); tick(); equal(state.grants,grants,"Rejected dispatch identity is consumed")
press(1); state.tick_disables=true; tick()
equal(latest.enabled.wukong_health,false,"Tick cleanup is published immediately without a second discovery")
state.tick_disables=false
press(1); state.tick_error=true; tick(); equal(latest.ready,false); equal(state.flags.wukong_health,false)
state.tick_error=false; tick(); equal(latest.ready,true)
for _,failure in ipairs({"write","close"}) do
    local committed,count=files["status.json"],publications
    state.ready=false; write_failure=failure=="write"; close_failure=failure=="close"; tick()
    equal(files["status.json"],committed,"Partial publication preserves committed heartbeat")
    equal(publications,count)
    write_failure=false; close_failure=false; tick(); equal(latest.ready,false,"Failed publication is retried")
    state.ready=true; tick()
end
local count=publications
state.ready=false; rename_failure=true; tick(); equal(publications,count)
equal(files["status.json"],nil,"Rename failure creates a recoverable heartbeat gap")
state.ready=true; rename_failure=false; tick()
equal(publications,count+1,"Returning to old state still republishes after a gap")
equal(latest.ready,true); equal(files["status.tmp"],nil)
equal(timers,1,"Polling and toggles never allocate additional timers")
equal(#jobs,0,"No outstanding asynchronous backlog")
schedule_failure=true; keys={}; loop=nil; load_main()
equal(loop,nil); equal(latest.ready,false)
equal(latest.message:find("fixture timer failure",1,true)~=nil,true,"Scheduler failure is visible without a running timer")
equal(#jobs,0,"Scheduler failure cannot create a fallback loop")
print("Wukong mailbox/hotkeys: "..checks.." assertions passed")
