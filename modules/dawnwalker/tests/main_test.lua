-- Actual main/protocol with only the engine and bridge I/O boundaries mocked.
-- No game process, installed files, keyboard input, or controller input is used.
local checks = 0
local function equal(actual, expected, label)
    checks = checks + 1
    assert(actual == expected, (label or "Mismatch") .. ": " .. tostring(actual) .. " ~= " .. tostring(expected))
end
local function copy(value)
    if type(value) ~= "table" then return value end
    local result = {}; for k, v in pairs(value) do result[k] = copy(v) end; return result
end
local files, jobs, keys = {}, {}, {}
local flight_loop, flight_ticks, flight_handle
local delayed, next_handle, canceled, hooks = {}, 0, 0, {}
local now, on_thread, latest, serialized_status, loop, notification = 1000, false
local timers, refreshes, publications = 0, 0, 0
local remove_failure, publish_failure, schedule_failure = false, false, false
local write_failure, close_failure, rename_failure = false, false, false
local state = {ready=true, key="world1/player1", flags={dawnwalker_health=false, dawnwalker_stamina=false, dawnwalker_blood=false,
    dawnwalker_auto_teleport=false}, calls={}, refills=0, disables=0, auto_polls=0, auto_attempts=0}
local function game_thread() assert(on_thread, "Engine boundary called off game thread") end
local trainer = {}
function trainer:refresh()
    game_thread()
    refreshes=refreshes+1
    if state.refresh_error then error("fixture lifecycle failure") end
    return state.ready, state.ready and "Ready" or "Load gameplay", {refills=state.refills},
        {dawnwalker_health=true, dawnwalker_stamina=true, dawnwalker_refill=true, dawnwalker_blood=true}
end
function trainer:session_key() game_thread(); return state.key end
function trainer:enabled() return copy(state.flags) end
function trainer:disable_all()
    state.disables=state.disables+1
    for action in pairs(state.flags) do state.flags[action]=false end
    state.auto_pending=nil
    return true, "All off"
end
function trainer:observe(character) state.observed=character end
function trainer:tick() game_thread(); flight_ticks=(flight_ticks or 0)+1; return not state.flight_tick_failure end
function trainer:set_marker_observation_ready(ready)
    state.observation_ready=ready==true
    if not state.observation_ready then
        state.flags.dawnwalker_auto_teleport=false; state.auto_pending=nil; state.marker=nil
    end
end
function trainer:observe_marker(map, location)
    state.marker={map=map,location=location}
    if state.flags.dawnwalker_auto_teleport then state.auto_pending=state.marker end
end
function trainer:forget_marker(map) state.forgotten_map=map; state.marker=nil; state.auto_pending=nil end
function trainer:process_auto_teleport()
    game_thread(); state.auto_polls=state.auto_polls+1
    if state.auto_error then error("fixture automatic travel failure") end
    if not state.flags.dawnwalker_auto_teleport or not state.auto_pending then return nil end
    state.auto_pending=nil; state.auto_attempts=state.auto_attempts+1
    return not state.auto_reject, state.auto_reject and "Automatic travel refused" or "Automatic travel started"
end
function trainer:apply(action, enabled)
    game_thread(); state.calls[#state.calls+1]={action=action, enabled=enabled}
    if state.apply_error then error("fixture write failure") end
    if action == "disable_all" then return self:disable_all() end
    if enabled==false and state.flags[action]~=nil then
        state.flags[action]=false
        if action=="dawnwalker_auto_teleport" then state.auto_pending=nil end
        return true,"Off"
    end
    if not state.ready then return false, "Load gameplay" end
    if state.reject then return false, "Binding refused action" end
    if action=="dawnwalker_auto_teleport" and not state.observation_ready then return false,"Marker observation unavailable" end
    if action == "dawnwalker_refill" then state.refills=state.refills+1; return true, "Refilled" end
    if state.flags[action] == nil then return false, "Unknown" end
    state.flags[action]=enabled == true
    return true, enabled and "Enabled" or "Disabled"
end
local P = dofile("modules/dawnwalker/Scripts/protocol.lua")
local json = P.json
P.json = function(value)
    if type(value) == "table" and value.schema_version then serialized_status=copy(value) end
    return json(value)
end
local function name(path) return path:gsub("\\", "/"):match("([^/]+)$") end
local fake_io = {}
function fake_io.open(path, mode)
    local file_name = name(path)
    assert(file_name == "command.txt" or file_name == "status.tmp", "Unexpected bridge file")
    if mode == "rb" and files[file_name] == nil or mode == "wb" and publish_failure then return nil end
    local file = {data=mode == "wb" and "" or files[file_name]}
    function file:read(limit) return self.data:sub(1, limit) end
    function file:write(value)
        if write_failure then self.data=self.data..value:sub(1,12); return nil,"fixture write failure" end
        self.data=self.data..value; return self
    end
    function file:close()
        if mode == "wb" then
            files[file_name]=self.data
            if close_failure then return nil,"fixture close failure" end
        end
        return true
    end
    return file
end
local fake_os = {time=function() return now end}
function fake_os.remove(path)
    if name(path) == "command.txt" and remove_failure then return nil, "fixture removal failed" end
    files[name(path)]=nil; return true
end
function fake_os.rename(from, to)
    if rename_failure then return nil,"fixture rename failure" end
    files[name(to)], files[name(from)]=files[name(from)], nil
    -- Observed status must represent a committed file, not an attempted JSON
    -- serialization whose write, close or rename might subsequently fail.
    latest=copy(serialized_status)
    publications=publications+1
    return true
end
local env = setmetatable({io=fake_io, os=fake_os, print=function() end,
    Key={F1=1,F2=2,F3=3,F4=4,F5=5,F7=7,F8=8,F9=9,F10=10,F11=11,F12=12,
        NUM_ONE=97,NUM_TWO=98,NUM_THREE=99,NUM_FOUR=100}}, {__index=_G})
function env.dofile(path)
    if name(path) == "protocol.lua" then return P end
    if name(path) == "bindings.lua" then return {new=function() return trainer end} end
    error("Unexpected dependency")
end
function env.LoopAsync() error("Startup must not create an async trainer loop") end
function env.LoopInGameThreadWithDelay(interval, callback)
    if interval==16 then
        if state.flight_schedule_error then error("fixture flight timer failure") end
        next_handle=next_handle+1; flight_handle=next_handle
        flight_loop=callback; delayed[flight_handle]=callback; return flight_handle
    end
    equal(interval,250); timers=timers+1
    if schedule_failure then error("fixture timer failure") end
    loop=callback
end
function env.CancelDelayedAction(handle)
    if state.cancel_failure then return false end
    if not delayed[handle] then return false end
    delayed[handle]=nil; canceled=canceled+1; return true
end
function env.RegisterHook(path, before, after)
    equal(type(before),"function"); equal(type(after),"function")
    if state.marker_hook_failure and path:match("UnsetUserMappin$") then error("fixture marker hook failure") end
    assert(not hooks[path],"Marker hooks must not be registered twice")
    hooks[path]={before=before,after=after}; return 1,2
end
function env.NotifyOnNewObject(class, callback)
    equal(class,"/Script/Dawnwalker.DawnwalkerPlayerCharacter")
    notification=callback
end
function env.ExecuteInGameThread(callback)
    if schedule_failure then error("fixture queue failure") end
    jobs[#jobs+1]=callback
end
function env.RegisterKeyBind(key, callback) assert(not keys[key]); keys[key]=callback end
assert(loadfile("modules/dawnwalker/Scripts/main.lua", "t", env))()
local function flush()
    local count=0
    while #jobs>0 do
        count=count+1; assert(count<20, "Unbounded queue")
        on_thread=true; table.remove(jobs,1)(); on_thread=false
    end
end
local function tick() on_thread=true; equal(loop(),false,"Persistent timer continues"); on_thread=false; flush() end
local function press(key) assert(keys[key], "Missing key "..key)(); flush() end
local function queue(action, id, enabled, session, created)
    files["command.txt"]=table.concat({"schema_version=1", "session_id="..(session or latest.session_id),
        "request_id="..string.format("%032x",id), "action="..action, "enabled="..(enabled and "1" or "0"),
        "created_utc="..tostring(created or now), ""}, "\n")
end
equal(#state.calls,0,"Loading makes no engine mutations")
equal(state.auto_polls,0,"Startup cannot dispatch automatic travel")
equal(refreshes,0,"Loading never queries game objects")
equal(latest.ready,false,"Startup heartbeat exists before game thread runs")
equal(latest.message,"Dawnwalker trainer starting; waiting for the game thread")
equal(timers,1,"One persistent timer")
equal(#jobs,0,"No startup handoff or queued backlog")
local constructed={}
equal(notification(constructed),nil,"Notification remains registered")
equal(state.observed,constructed,"Constructor callback only retains object")
equal(refreshes,0,"Constructor callback never refreshes")
tick()
equal(latest.version,"0.3.4"); equal(latest.ready,true); equal(state.disables,1)
equal(state.auto_polls,1,"Normal timer checks automatic travel once")
do
    local prefix="/Script/DogwoodMap.MappinSystemBlueprintLibrary:"
    local map, point={}, {X=100,Y=200,Z=300}
    local function param(value) return {get=function() return value end} end
    local set=hooks[prefix.."SetUserMappin"]
    equal(type(set),"table","Map observation registered after readiness")
    equal(state.observation_ready,true,"Both successful hooks make marker observation ready")
    set.before(nil,param(map),param(point)); equal(state.marker,nil,"Pre-hook never captures an uncommitted marker")
    set.after(nil,param(map),param(point))
    equal(state.marker.map,map); equal(state.marker.location.X,100)
    point.X=999; equal(state.marker.location.X,100,"Hook copies expired parameter data")
    hooks[prefix.."UnsetUserMappin"].after(nil,param(map))
    equal(state.marker,nil,"Removing a marker invalidates capture"); equal(state.forgotten_map,map)
end
local published=publications
local refreshed=refreshes
tick(); tick(); tick()
equal(publications,published,"Stable polling does not rewrite the heartbeat four times per second")
equal(refreshes,refreshed+3,"Resource refresh still runs every game-thread poll")
now=now+1; tick(); equal(publications,published+1,"Unchanged status still has a fresh 1 Hz heartbeat")
published=publications; state.key="world1/player2"; tick()
equal(publications,published+1,"Session change publishes inside the same second")
equal(latest.session_id:sub(-2),"-2")
equal(files["status.tmp"],nil,"Status rename completed")
do
    local committed=files["status.json"]
    local count=publications
    state.ready=false; rename_failure=true; tick()
    equal(files["status.json"],nil,"Failed rename after removal leaves a publication gap")
    equal(latest.ready,true,"Failed rename never counts as a committed status")
    equal(publications,count)
    state.ready=true; rename_failure=false; tick()
    equal(files["status.json"],committed,"A to B failure to A retries within the same second")
    equal(publications,count+1,"Returning to the old controls still recreates the missing file")
    equal(latest.ready,true)
end
for _,failure in ipairs({"write","close"}) do
    local committed=files["status.json"]
    local count=publications
    state.ready=false
    write_failure=failure=="write"; close_failure=failure=="close"
    tick()
    equal(files["status.json"],committed,failure.." failure preserves the last good status file")
    equal(publications,count,failure.." failure is not committed")
    equal(latest.ready,true,failure.." failure cannot expose an uncommitted status")
    write_failure=false; close_failure=false; tick()
    equal(publications,count+1,failure.." failure retries on the next poll")
    equal(latest.ready,false)
    equal(files["status.json"]~=committed,true,"The retried state reaches the committed file")
    state.ready=true; tick()
end
for _, key in ipairs({1,2,3,4,10,100}) do equal(type(keys[key]),"function") end
equal(keys[6],nil,"Overlay owns F6")
queue("dawnwalker_refill",1,false,nil,now-9); tick(); equal(#state.calls,0)
queue("dawnwalker_refill",1,false); tick(); equal(state.refills,1); equal(latest.last_success,true)
local accepted=latest.last_command
queue("dawnwalker_refill",1,false); tick(); equal(state.refills,1,"Replay never repeats one-shot"); equal(latest.last_command,accepted)
queue("dawnwalker_refill",2,false); remove_failure=true; tick()
equal(state.refills,1,"Failed mailbox removal forbids execution"); equal(type(files["command.txt"]),"string")
remove_failure=false; tick(); equal(state.refills,2)
queue("dawnwalker_refill",3,false); publish_failure=true; tick(); equal(state.refills,3)
publish_failure=false; queue("dawnwalker_refill",3,false); tick(); equal(state.refills,3,"Lost status publication cannot replay action")
local old_session=latest.session_id
queue("dawnwalker_health",4,true); state.key="world2/player2"; tick()
equal(latest.session_id~=old_session,true); equal(latest.last_command,""); equal(state.flags.dawnwalker_health,false)
equal(#state.calls,3,"Old-session command cannot affect new pawn")
queue("dawnwalker_health",5,true); tick(); equal(state.flags.dawnwalker_health,true)
queue("disable_all",6,false); tick(); equal(state.flags.dawnwalker_health,false)
local before=#state.calls
keys[1](); equal(#state.calls,before,"Hotkey waits for game thread"); flush()
equal(state.flags.dawnwalker_health,true); press(1); equal(state.flags.dawnwalker_health,false)
press(2); press(4); equal(state.flags.dawnwalker_stamina,true); equal(state.flags.dawnwalker_blood,true)
press(3); equal(state.refills,4); equal(state.calls[#state.calls].enabled,false,"One-shot hotkey forwards false")
press(10); equal(state.flags.dawnwalker_stamina,false); equal(state.flags.dawnwalker_blood,false)
old_session=latest.session_id; state.ready=false; state.key="world2/menu"; tick()
equal(latest.session_id~=old_session,true); before=#state.calls
queue("dawnwalker_refill",7,false); tick(); equal(#state.calls,before); equal(latest.last_success,false)
press(1); press(2); press(3); press(4); equal(#state.calls,before,"Gameplay shortcuts ignored in menu")
press(10); equal(#state.calls,before+1,"Emergency off remains allowed")
state.flags.dawnwalker_health=true
queue("dawnwalker_health",70,false); tick()
equal(state.flags.dawnwalker_health,false,"Mailbox cleanup allowed while gameplay unavailable")
state.flags.dawnwalker_stamina=true; press(2)
equal(state.flags.dawnwalker_stamina,false,"Hotkey cleanup allowed while gameplay unavailable")
state.ready=true; state.key="world3/player3"; tick(); press(1)
state.refresh_error=true; tick(); equal(latest.ready,false); equal(state.flags.dawnwalker_health,false)
state.refresh_error=false; tick(); equal(latest.ready,true); equal(#jobs,0)
state.reject=true; queue("dawnwalker_refill",8,false); tick(); equal(latest.last_success,false); equal(state.refills,4)
state.reject=false; state.apply_error=true; queue("dawnwalker_refill",9,false); tick()
equal(latest.last_success,false); equal(state.flags.dawnwalker_health,false); equal(state.refills,4)
state.apply_error=false; queue("dawnwalker_refill",9,false); tick(); equal(state.refills,4,"Failure identity also deduplicated")
equal(timers,1,"Polling never allocates new timers"); equal(#jobs,0)
before=#state.calls; files["command.txt"]=string.rep("x",2049); tick(); equal(#state.calls,before,"Bounded command read rejects oversized data")
queue("dawnwalker_unknown",10,false); tick(); equal(#state.calls,before,"Unknown action never reaches binding")
do
    local map, point={}, {X=10,Y=20,Z=30}
    local set=hooks["/Script/DogwoodMap.MappinSystemBlueprintLibrary:SetUserMappin"]
    local function mark()
        set.after(nil,{get=function() return map end},{get=function() return point end})
    end
    mark()
    local polls, attempts=state.auto_polls,state.auto_attempts
    press(100)
    equal(state.flags.dawnwalker_auto_teleport,true,"NumPad4 enables the opt-in toggle")
    equal(state.auto_polls,polls,"Hotkey handler does not dispatch travel")
    tick()
    equal(state.auto_attempts,attempts,"Enabling does not travel to an existing marker")
    mark()
    equal(state.auto_attempts,attempts,"Native marker POST hook never dispatches travel")
    equal(state.auto_polls,polls+1,"Hook never enters the automatic travel processor")
    queue("dawnwalker_auto_teleport",1000,false)
    tick()
    equal(state.auto_attempts,attempts,"Off command cancels queued travel before the automatic check")
    equal(state.auto_polls,polls+2,"A command and its extra refresh still dispatch only once per tick")
    equal(latest.last_success,true); equal(latest.enabled.dawnwalker_auto_teleport,false)
    press(100); mark(); queue("disable_all",1001,false); tick()
    equal(state.auto_attempts,attempts,"Disable-all command takes precedence over queued travel")
    press(100); mark()
    local ack=latest.last_command
    polls=state.auto_polls; tick()
    equal(state.auto_attempts,attempts+1,"Queued marker runs during the next regular timer tick")
    equal(state.auto_polls,polls+1,"Post-travel refresh never dispatches recursively")
    equal(latest.last_command,ack,"Automatic result does not overwrite command acknowledgement")
    tick(); equal(state.auto_attempts,attempts+1,"An already consumed marker cannot replay")
    state.auto_reject=true; mark(); tick()
    equal(state.auto_attempts,attempts+2)
    equal(latest.ready,true,"A safe refused travel request does not pause the trainer")
    tick(); equal(state.auto_attempts,attempts+2,"Refused automatic travel does not retry")
    state.auto_reject=false; mark(); state.auto_error=true; tick()
    equal(latest.ready,false,"Automatic processor exception enters the existing guarded pause")
    equal(state.flags.dawnwalker_auto_teleport,false,"Exception cancels automatic travel")
    equal(state.auto_pending,nil)
    state.auto_error=false; tick(); equal(latest.ready,true)
    equal(state.auto_attempts,attempts+2,"Recovery never replays canceled automatic travel")
    press(100); mark(); state.key="auto-next-session"; tick()
    equal(state.flags.dawnwalker_auto_teleport,false,"Session transition turns automatic travel off")
    equal(state.auto_attempts,attempts+2,"Session cleanup precedes queued marker dispatch")
    state.ready=false; state.flags.dawnwalker_auto_teleport=true
    queue("dawnwalker_auto_teleport",1002,false); tick()
    equal(state.flags.dawnwalker_auto_teleport,false,"Mailbox off remains allowed when gameplay is unavailable")
    state.flags.dawnwalker_auto_teleport=true; press(100)
    equal(state.flags.dawnwalker_auto_teleport,false,"NumPad4 off remains allowed when gameplay is unavailable")
    state.ready=true; tick()
    press(100); mark()
    set.after(nil,{get=function() return map end},{get=function() error("expired struct") end})
    equal(state.marker,nil,"Invalid hook data fails closed")
    equal(state.observation_ready,false,"Capture error invalidates observation readiness")
    equal(state.flags.dawnwalker_auto_teleport,false,"Capture error disarms automatic travel immediately")
    equal(state.auto_pending,nil,"Capture error cancels queued intent without engine calls")
    mark(); equal(state.marker,nil,"Subsequent POST hooks remain inert after capture failure")
    press(100); equal(state.flags.dawnwalker_auto_teleport,false,"Broken marker observation cannot be enabled")
    tick(); equal(state.auto_attempts,attempts+2,"Capture recovery never retries a queued request")
end
equal(flight_loop,nil,"Disabled flight creates no high-frequency timer")
state.flags.dawnwalker_fly=true; tick()
local active_flight_loop=flight_loop
equal(type(active_flight_loop),"function","Enabling flight creates its game-thread timer")
tick(); equal(flight_loop,active_flight_loop,"Active flight does not allocate duplicate timers")
on_thread=true; flight_loop(); on_thread=false
equal(flight_ticks,1)
state.flags.dawnwalker_fly=false
on_thread=true; flight_loop(); on_thread=false
equal(delayed[flight_handle],nil,"Disabling flight explicitly cancels native handle")
equal(canceled,1)
equal(flight_ticks,1,"Disabled timer does not enter engine work")
state.flags.dawnwalker_fly=true; tick()
equal(flight_loop~=active_flight_loop,true,"Re-enabling flight starts a fresh timer")
local new_handle=flight_handle
on_thread=true; active_flight_loop(); on_thread=false
equal(flight_handle,new_handle); equal(flight_ticks,1,"Old callbacks cannot affect a new flight timer")
state.flags.dawnwalker_fly=false
tick(); equal(delayed[flight_handle],nil,"Status refresh also cancels an inactive flight timer")
state.flight_schedule_error=true; state.flags.dawnwalker_fly=true; tick()
equal(latest.ready,false,"Flight scheduling failure pauses trainer")
equal(state.flags.dawnwalker_fly,false,"Flight scheduling failure disables controls")
state.flight_schedule_error=false; tick(); state.flags.dawnwalker_fly=true; tick()
on_thread=true; flight_loop(); on_thread=false
equal(flight_ticks,2,"Flight can restart after failed scheduling")
state.cancel_failure=true; state.flags.dawnwalker_fly=false; tick()
local retained_handle=flight_handle
equal(type(delayed[retained_handle]),"function","Failed cancellation retains its handle")
state.flags.dawnwalker_fly=true; tick()
equal(flight_handle,retained_handle,"Cancellation failure cannot allocate duplicate loops")
state.cancel_failure=false; state.flight_tick_failure=true
on_thread=true; flight_loop(); on_thread=false
equal(delayed[retained_handle],nil,"Flight failure cancels fast loop")
equal(state.flags.dawnwalker_fly,false,"Flight failure releases controls")
state.flight_tick_failure=false
for _=1,10 do
    state.flags.dawnwalker_fly=true; tick()
    state.flags.dawnwalker_fly=false; tick()
end
equal(next(delayed),nil,"Repeated flight toggles leave no native timers behind")
state.flags.dawnwalker_fly=false
-- A partial hook registration cannot leave a usable setter observer without
-- an unset observer. Base controls still work and registration never repeats.
keys={}; hooks={}; loop=nil; state.marker_hook_failure=true
assert(loadfile("modules/dawnwalker/Scripts/main.lua", "t", env))()
tick(); equal(latest.ready,true,"Optional map hook failure preserves base readiness")
equal(state.marker,nil)
equal(state.observation_ready,false,"Partial hook pair never enables automatic observation")
press(100); equal(state.flags.dawnwalker_auto_teleport,false,"Partial hook registration cannot arm automatic travel")
local setter=hooks["/Script/DogwoodMap.MappinSystemBlueprintLibrary:SetUserMappin"]
setter.after(nil,{get=function() return {} end},{get=function() return {X=1,Y=2,Z=3} end})
equal(state.marker,nil,"Partially registered hooks cannot capture a stale marker")
state.marker_hook_failure=false; tick()
equal(hooks["/Script/DogwoodMap.MappinSystemBlueprintLibrary:UnsetUserMappin"],nil,"Failed hook registration is not duplicated")
-- A scheduler initialization failure must remain visible in the bridge even
-- though no game-thread callback can publish status.
schedule_failure=true; keys={}; loop=nil; hooks={}
assert(loadfile("modules/dawnwalker/Scripts/main.lua", "t", env))()
equal(loop,nil); equal(latest.ready,false)
equal(latest.message:find("fixture timer failure",1,true)~=nil,true)
equal(#jobs,0,"Failed initialization does not spawn another scheduling path")
print("Dawnwalker main bridge/hotkeys: "..checks.." assertions passed")
