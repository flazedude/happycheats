-- HappyCheats Wukong 0.1.1. All UObject access stays on the game thread.
local source=debug.getinfo(1,"S").source:gsub("^@",""):gsub("\\","/")
local scripts=assert(source:match("^(.*)/[^/]+$"))
local bridge=scripts.."/../bridge/"
local P=dofile(scripts.."/protocol.lua")
local trainer=dofile(scripts.."/bindings.lua").new()
local boot=tostring(os.time()).."-"..tostring({}):gsub("[^%w]","")
local serial,identity,session=0,nil,boot.."-0"
local seen={}
local last_command,last_success,last_message="",false,""
local status={schema_version=1,version="0.1.1",session_id=session,ready=false,
    message="Wukong trainer starting; waiting for the game thread.",enabled={},values={},available={}}
local published_at,published_controls,reported_connection
local function publish()
    status.session_id,status.enabled=session,trainer:enabled()
    status.last_command,status.last_success,status.last_message=last_command,last_success,last_message
    local now=os.time()
    local controls=P.json({session=session,ready=status.ready,message=status.message,enabled=status.enabled,
        available=status.available,last_command=last_command,last_success=last_success,last_message=last_message})
    if now==published_at and controls==published_controls then return end
    local file=io.open(bridge.."status.tmp","wb")
    if not file then return end
    local wrote=file:write(P.json(status)); local closed=file:close()
    if not wrote or not closed then return end
    published_at,published_controls=nil,nil
    os.remove(bridge.."status.json")
    if os.rename(bridge.."status.tmp",bridge.."status.json") then published_at,published_controls=now,controls end
end
local function refresh()
    status.ready,status.message,status.values,status.available=trainer:refresh()
    local connection=tostring(status.ready)..":"..tostring(status.message)
    if connection~=reported_connection then
        reported_connection=connection
        print("[HappyCheats] "..tostring(status.message).."\n")
    end
    local current=trainer:session_key()
    if current~=identity then
        identity=current; serial=serial+1; session=boot.."-"..serial
        seen={}; last_command,last_success,last_message="",false,""
        trainer:disable_all()
    end
end
local function tick()
    refresh()
    local now=os.time()
    for id,created in pairs(seen) do if now-created>10 then seen[id]=nil end end
    local file=io.open(bridge.."command.txt","rb")
    if file then
        local raw=file:read(2049); file:close()
        -- Consume before dispatch. Never retry progression after uncertain results.
        local removed=os.remove(bridge.."command.txt")
        local command,reason=P.parse(raw,session,now)
        if removed and command and not seen[command.request_id] then
            seen[command.request_id]=tonumber(command.created_utc)
            last_command=command.request_id
            local disabling=command.enabled==false and trainer:enabled()[command.action]~=nil
            if not status.ready and command.action~="disable_all" and not disabling then
                last_success,last_message=false,status.message
            else
                local ok,success,message=pcall(function() return trainer:apply(command.action,command.enabled,command.amount) end)
                last_success=ok and success==true
                last_message=ok and tostring(message) or "Wukong command failed: "..tostring(success)
                if not ok then trainer:disable_all() end
                refresh()
            end
        elseif reason then print("[HappyCheats] Ignored command: "..reason.."\n") end
    end
    -- The same game-thread turn already resolved the current player above (and
    -- again after a command). Reuse it instead of scanning all UObjects twice.
    trainer:tick(true)
    publish()
end
local function guarded(work)
    local ok,reason=pcall(work)
    if not ok then
        pcall(function() trainer:disable_all() end)
        status.ready=false; status.available={}
        status.message="Wukong trainer paused: "..tostring(reason)
        pcall(publish)
        print("[HappyCheats] "..status.message.."\n")
    end
end
pcall(publish)
guarded(function() LoopInGameThreadWithDelay(100,function() guarded(tick) end) end)
local keys={{Key.F1,"wukong_health"},{Key.F2,"wukong_mana"},{Key.F3,"wukong_stamina"},
    {Key.F4,"wukong_damage"},{Key.F5,"wukong_gourd"},{Key.F6,"wukong_focus"},
    {Key.F7,"wukong_xp"},{Key.F8,"wukong_sparks"},{Key.F9,"wukong_will"},{Key.F10,"disable_all"}}
for _,shortcut in ipairs(keys) do
    local action=shortcut[2]
    RegisterKeyBind(shortcut[1],function()
        ExecuteInGameThread(function()
            guarded(function()
                refresh()
                local flags=trainer:enabled()
                local enable=flags[action]==false
                local ok,message=trainer:apply(action,enable)
                refresh(); status.message=tostring(message); publish()
                if not ok then print("[HappyCheats] "..tostring(message).."\n") end
            end)
        end)
    end)
end
print("[HappyCheats] Wukong 0.1.1 loaded. All toggles start off.\n")
