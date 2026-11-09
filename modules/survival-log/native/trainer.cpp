#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <bcrypt.h>
#include <MinHook.h>
#include <nlohmann/json.hpp>
#include <atomic>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <optional>
#include <map>
#include "bindings.h"
#include "policy.h"

// Original HappyCheats IL2CPP bindings. All game calls run on BattleLogicWorld's update thread.
namespace {
using Json = nlohmann::json;
constexpr const char* version = "0.1.1";
constexpr const char* expectedHash = "15ffde4e555fad6afe8c3aefd78d06a161023c6757a62223e2523b78198b062c";
const char* toggles[] = {"sl_satiety","sl_morale","sl_stamina","sl_health","sl_vitality","sl_freeze_clock","sl_carry"};
const char* numbers[] = {"sl_money","sl_survival_points","sl_xp"};
HMODULE self{};
uintptr_t game{};
std::filesystem::path bridge;
std::recursive_mutex state;
std::string session, lastRequest, lastMessage = "Load a living player to use the trainer.", initError;
bool lastSuccess = true, initialized{};
uint64_t processStart{};
std::atomic<int> mask{}, xp{1};
std::atomic<int64_t> player{};
std::atomic<ULONGLONG> leaseDeadline{}, frameTime{};
uintptr_t world{}, role{}, timeManager{};
int64_t roleId{}, clockId{};
bool clockOwned{}, oldClock{}, clockConflict{};
std::optional<Json> pending;
std::map<std::string,int64_t> requests;
int64_t pendingPlayer{};
using UpdateFn = void(*)(void*,float,float,const void*);
using EndFn = void(*)(void*,const void*);
using FloatFn = void(*)(void*,int,float,bool,const void*);
using IntFn = void(*)(void*,int,int,const void*);
using BurdenFn = int(*)(void*,const void*);
using ExpFn = void(*)(void*,int,void*,int,int,int,int,int*,const void*);
UpdateFn originalUpdate{};
EndFn originalEnd{};
FloatFn originalFloat{};
IntFn originalInt{};
BurdenFn originalBurden{};
ExpFn originalExp{};
float (*getTotalFloat)(void*,int,const void*){};
int (*getTotalInt)(void*,int,const void*){};
void* (*getLeading)(void*,const void*){};
void (*setGold)(void*,int,const void*){};
void (*addSurvival)(void*,int,const void*){};

int64_t unixMs() { FILETIME f; GetSystemTimeAsFileTime(&f); ULARGE_INTEGER u; u.LowPart=f.dwLowDateTime;u.HighPart=f.dwHighDateTime;return static_cast<int64_t>((u.QuadPart-116444736000000000ULL)/10000); }
bool memory(uintptr_t at, size_t size, bool write=false) {
    if (at < 0x10000 || at + size < at) return false;
    MEMORY_BASIC_INFORMATION m{};
    if (!VirtualQuery(reinterpret_cast<void*>(at), &m, sizeof(m)) || m.State != MEM_COMMIT || (m.Protect & (PAGE_GUARD|PAGE_NOACCESS))) return false;
    auto protection=m.Protect & 0xff;
    bool readable=protection==PAGE_READONLY || protection==PAGE_READWRITE || protection==PAGE_WRITECOPY || protection==PAGE_EXECUTE_READ || protection==PAGE_EXECUTE_READWRITE || protection==PAGE_EXECUTE_WRITECOPY;
    return readable && (!write || protection==PAGE_READWRITE || protection==PAGE_WRITECOPY || protection==PAGE_EXECUTE_READWRITE || protection==PAGE_EXECUTE_WRITECOPY)
        && at+size <= reinterpret_cast<uintptr_t>(m.BaseAddress)+m.RegionSize;
}
template<class T> T read(uintptr_t at) { T v{}; if(memory(at,sizeof(T))) memcpy(&v,reinterpret_cast<void*>(at),sizeof(T));return v; }
bool live() { auto now=GetTickCount64();return player.load()!=0 && now<leaseDeadline.load() && now-frameTime.load()<1000; }
bool owns(void* component) { return live() && read<int64_t>(reinterpret_cast<uintptr_t>(component)+0x30)==player.load(); }
void clear() { mask=0;xp=1; }
void restoreClock() {
    // Restore only the exact live object and value we wrote. Foreign changes win.
    if(clockOwned && memory(timeManager+0x44,1,true) && read<int64_t>(timeManager+0x10)==clockId && read<bool>(timeManager+0x44))
        *reinterpret_cast<bool*>(timeManager+0x44)=oldClock;
    clockOwned=false;timeManager=0;clockId=0;clockConflict=false;
}
void clock(uintptr_t current, bool enable) {
    if (timeManager && (current!=timeManager || read<int64_t>(current+0x10)!=clockId)) { clockOwned=false;timeManager=0;clockConflict=false; }
    if (!enable) { restoreClock();return; }
    if (!memory(current+0x44,1,true)) return;
    if (clockOwned && !read<bool>(current+0x44)) { clockOwned=false;clockConflict=true;mask.fetch_and(~(1<<5));return; }
    if (!clockOwned && !clockConflict) {
        timeManager=current;clockId=read<int64_t>(current+0x10);oldClock=read<bool>(current+0x44);
        *reinterpret_cast<bool*>(current+0x44)=true;clockOwned=true;
    }
}

void setFloat(void* component,int attr,float value,bool bypass,const void* method) {
    if(policy::stat(attr,mask.load(),read<int64_t>(reinterpret_cast<uintptr_t>(component)+0x30),player.load(),live())) {
        auto maximum=getTotalFloat(component,attr+100,nullptr);
        if(std::isfinite(maximum) && maximum>0 && maximum<10000000) value=maximum;
    }
    originalFloat(component,attr,value,bypass,method);
}
void setInt(void* component,int attr,int value,const void* method) {
    if(policy::stat(attr,mask.load(),read<int64_t>(reinterpret_cast<uintptr_t>(component)+0x30),player.load(),live())) {
        auto maximum=getTotalInt(component,attr+100,nullptr);
        if(maximum>0 && maximum<1000000000) value=maximum;
    }
    originalInt(component,attr,value,method);
}
int burden(void* component,const void* method) { return owns(component) && (mask.load()&(1<<6)) ? 1000000 : originalBurden(component,method); }
void addExp(void* manager,int type,void* key,int base,int level,int discovery,int source,int* actual,const void* method) {
    // The world-owned manager is checked before multiplying; unrelated contexts keep their original XP.
    int multiplier=1;
    { std::lock_guard lock(state);if(live() && read<uintptr_t>(world+0x158)==reinterpret_cast<uintptr_t>(manager)) multiplier=xp.load(); }
    originalExp(manager,type,key,policy::experience(base,multiplier),level,policy::experience(discovery,multiplier),source,actual,method);
}
void acknowledge(const Json& command,bool success,const std::string& message) {
    lastRequest=command.value("request_id","");lastSuccess=success;lastMessage=message;
}
void execute(const Json& command) {
    if(!policy::fresh(command.value("expires_unix_ms",int64_t{}),unixMs())) throw std::runtime_error("Command expired; click again.");
    const auto feature=command.value("feature","");
    if(feature=="disable_all") {
        if(command.contains("value") || command.contains("enabled"))throw std::runtime_error("Disable all does not accept a value.");
        clear();restoreClock();return;
    }
    for(int i=0;i<7;i++) if(feature==toggles[i]) {
        if(!command.contains("enabled") || !command["enabled"].is_boolean() || command.contains("value")) throw std::runtime_error("Explicit toggle state required.");
        bool enable=command["enabled"];
        if(enable && (!live() || pendingPlayer!=roleId)) throw std::runtime_error("Player changed or is not ready; try again after loading.");
        if(i==5 && enable && !memory(read<uintptr_t>(world+0x80)+0x44,1,true)) throw std::runtime_error("The game clock is not ready.");
        if(enable) mask.fetch_or(1<<i);else mask.fetch_and(~(1<<i));
        if(i==5) { if(!enable)restoreClock();else clockConflict=false; }
        return;
    }
    if(!live() || pendingPlayer!=roleId) throw std::runtime_error("Player changed or is not ready; one-shot action was not applied.");
    if(command.contains("enabled") || !command.contains("value") || !command["value"].is_number()) throw std::runtime_error("Numeric value required.");
    double number=command["value"].get<double>();
    if(!policy::numeric(feature,number)) throw std::runtime_error("Value is outside this control's range.");
    int value=static_cast<int>(number);
    if(feature=="sl_money") setGold(reinterpret_cast<void*>(role),value,nullptr);
    else if(feature=="sl_survival_points") {
        auto manager=read<uintptr_t>(world+0x160);
        if(!memory(manager,0x40)) throw std::runtime_error("Survival points manager is unavailable.");
        if(read<int>(manager+0x30)>INT_MAX-value || read<int>(manager+0x34)>INT_MAX-value)
            throw std::runtime_error("Current or lifetime survival points would overflow.");
        addSurvival(reinterpret_cast<void*>(manager),value,nullptr);
    } else if(feature=="sl_xp") xp=value;
    else throw std::runtime_error("Unknown control.");
}
void update(void* instance,float delta,float unscaled,const void* method) {
    {
        std::lock_guard lock(state);
        auto current=reinterpret_cast<uintptr_t>(instance);
        auto manager=read<uintptr_t>(current+0x40);
        uintptr_t lead{};
        if(read<bool>(current+0x18) && memory(manager,0x48))
            lead=reinterpret_cast<uintptr_t>(getLeading(reinterpret_cast<void*>(manager),nullptr));
        int64_t id=memory(lead,0x70) && !read<bool>(lead+0x40) ? read<int64_t>(lead+0x10) : 0;
        if(world!=current || roleId!=id || role!=lead) {
            // Do not carry confirmed effects across recycled entities. The client retains requests and restores via commands.
            if(world==current)restoreClock();else {clockOwned=false;timeManager=0;clockConflict=false;}
            clear();
        }
        world=current;role=lead;roleId=id;player=id;frameTime=GetTickCount64();
        if(!live()) {clear();restoreClock();}
        if(pending) {
            auto command=std::move(*pending);pending.reset();
            try { execute(command);acknowledge(command,true,"Applied."); }
            catch(const std::exception& error) {acknowledge(command,false,error.what());}
        }
        clock(read<uintptr_t>(current+0x80),live() && (mask.load()&(1<<5)));
    }
    originalUpdate(instance,delta,unscaled,method);
}
void endBattle(void* instance,const void* method) {
    { std::lock_guard lock(state); if(world==reinterpret_cast<uintptr_t>(instance)){restoreClock();clear();player=0;role=0;roleId=0;world=0;} }
    originalEnd(instance,method);
}

std::string hashFile(const std::filesystem::path& path) {
    std::ifstream input(path,std::ios::binary);if(!input)throw std::runtime_error("Cannot read game assembly.");
    BCRYPT_ALG_HANDLE algorithm{};BCRYPT_HASH_HANDLE hash{};
    if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)throw std::runtime_error("SHA256 provider unavailable.");
    DWORD size{},count{};BCryptGetProperty(algorithm,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&size),sizeof(size),&count,0);
    std::vector<unsigned char> object(size),digest(32);bool ok=BCryptCreateHash(algorithm,&hash,object.data(),size,nullptr,0,0)>=0;
    char bytes[65536];while(ok && input){input.read(bytes,sizeof(bytes));auto n=input.gcount();if(n)ok=BCryptHashData(hash,reinterpret_cast<PUCHAR>(bytes),static_cast<ULONG>(n),0)>=0;}
    ok=ok && input.eof() && BCryptFinishHash(hash,digest.data(),32,0)>=0;
    if(hash)BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm,0);
    if(!ok)throw std::runtime_error("Game hash verification failed.");
    const char* hex="0123456789abcdef";std::string result;for(auto v:digest){result+=hex[v>>4];result+=hex[v&15];}return result;
}
std::string readFile(const std::filesystem::path& path,size_t limit) {
    std::ifstream input(path,std::ios::binary);if(!input)return "";
    std::string data(limit+1,'\0');input.read(data.data(),data.size());data.resize(static_cast<size_t>(input.gcount()));
    if(data.size()>limit)throw std::runtime_error("Mailbox exceeds its size limit.");
    return data;
}
void status() {
    Json enabled=Json::object(),available=Json::object(),values=Json::object();
    bool ready=initialized && live();int flags=ready?mask.load():0;
    for(int i=0;i<7;i++){enabled[toggles[i]]=(flags&(1<<i))!=0;available[toggles[i]]=ready;}
    for(auto id:numbers)available[id]=ready;
    values["sl_xp"]=std::to_string(ready?xp.load():1);
    if(!initError.empty())values["initialization_error"]=initError;
    Json result={{"schema_version",1},{"version",version},{"process_id",GetCurrentProcessId()},{"process_start_unix_ms",processStart},
        {"heartbeat_unix_ms",unixMs()},{"session_id",session},{"ready",ready},{"message",!initError.empty()?initError:ready?"Experimental trainer connected. Gameplay validation pending.":"Load a living player; trainer effects are waiting."},
        {"enabled",enabled},{"available",available},{"values",values},{"last_command",lastRequest},
        {"command_result",{{"request_id",lastRequest},{"success",lastSuccess},{"message",lastMessage}}}};
    auto temp=bridge/L"status.tmp";{std::ofstream output(temp,std::ios::binary|std::ios::trunc);output<<result.dump();if(!output)return;}
    MoveFileExW(temp.c_str(),(bridge/L"status.json").c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH);
}
bool requestId(const std::string& id) {return id.size()==32 && id.find_first_not_of("0123456789abcdef")==std::string::npos;}
void mailbox() {
    auto path=bridge/L"command.json";
    auto data=readFile(path,16384);if(data.empty())return;
    // Capture first; never delete a concurrent client's new request.
    auto held=bridge/(L"command.processing-"+std::wstring(session.begin(),session.end())+L".json");
    if(!MoveFileExW(path.c_str(),held.c_str(),0))return;
    try {
        auto command=Json::parse(readFile(held,16384));
        auto id=command.value("request_id","");
        if(command.value("schema_version",0)!=1 || command.value("version","")!=version || command.value("session_id","")!=session || !requestId(id))throw std::runtime_error("Invalid command identity.");
        for(auto it=requests.begin();it!=requests.end();)if(it->second<unixMs())it=requests.erase(it);else ++it;
        if(requests.contains(id)) {DeleteFileW(held.c_str());return;}
        requests.emplace(id,unixMs()+10000);
        if(pending)acknowledge(command,false,"Trainer is busy; wait for the previous command.");
        else if(!policy::fresh(command.value("expires_unix_ms",int64_t{}),unixMs()))acknowledge(command,false,"Command expired.");
        else if(command.value("feature","")=="disable_all") {
            if(command.contains("value") || command.contains("enabled"))acknowledge(command,false,"Disable all does not accept a value.");
            else {clear();acknowledge(command,true,"Effects disabled; clock restoration occurs on the next frame.");}
        }
        else if(!live() && command.contains("enabled") && command["enabled"]==false) {
            auto feature=command.value("feature","");bool found=false;
            if(command.contains("value"))throw std::runtime_error("A toggle does not accept a numeric value.");
            for(int i=0;i<7;i++)if(feature==toggles[i]){mask.fetch_and(~(1<<i));found=true;}
            acknowledge(command,found,found?"Disabled; clock restoration occurs on the next frame.":"Unknown control.");
        } else {pendingPlayer=player.load();pending=command;}
    } catch(const std::exception& error) {lastMessage=error.what();}
    DeleteFileW(held.c_str());
}
void initialize(const std::filesystem::path& root) {
    wchar_t executable[32768];GetModuleFileNameW(nullptr,executable,32768);
    if(std::filesystem::path(executable).filename()!=L"SurvivalLog.exe")throw std::runtime_error("Unsupported host; SurvivalLog.exe is required.");
    if(hashFile(root/L"SurvivalLog_Data/il2cpp_data/Metadata/global-metadata.dat")!="dd37a8f1d9fcd5c10c026797fc76b7d9faaafa5a0b0df87e3dee4c7d39cba75b")throw std::runtime_error("Unsupported Survival Log metadata.");
    if(hashFile(root/L"GameAssembly.dll")!=expectedHash)throw std::runtime_error("Unsupported Survival Log build. Update the trainer before connecting.");
    auto module=GetModuleHandleW(L"GameAssembly.dll");if(!module)throw std::runtime_error("GameAssembly.dll is not loaded.");game=reinterpret_cast<uintptr_t>(module);
    for(auto& sentinel:bindings::sentinels)if(!memory(game+sentinel.rva,sizeof(sentinel.bytes)) || memcmp(reinterpret_cast<void*>(game+sentinel.rva),sentinel.bytes,sizeof(sentinel.bytes)))throw std::runtime_error("Game code differs from inspected bindings. Restart without conflicting mods.");
    getTotalFloat=reinterpret_cast<decltype(getTotalFloat)>(game+bindings::GetTotalFloat);
    getTotalInt=reinterpret_cast<decltype(getTotalInt)>(game+bindings::GetTotalInt);
    getLeading=reinterpret_cast<decltype(getLeading)>(game+bindings::GetLeadingRole);
    setGold=reinterpret_cast<decltype(setGold)>(game+bindings::SetGold);
    addSurvival=reinterpret_cast<decltype(addSurvival)>(game+bindings::AddSurvival);
    if(MH_Initialize()!=MH_OK)throw std::runtime_error("Native hook initialization failed.");
    struct Hook {uintptr_t rva;void* callback;void** original;};
    Hook hooks[]={{bindings::WorldUpdate,reinterpret_cast<void*>(update),reinterpret_cast<void**>(&originalUpdate)},
        {bindings::EndBattle,reinterpret_cast<void*>(endBattle),reinterpret_cast<void**>(&originalEnd)},
        {bindings::SetFloat,reinterpret_cast<void*>(setFloat),reinterpret_cast<void**>(&originalFloat)},
        {bindings::SetInt,reinterpret_cast<void*>(setInt),reinterpret_cast<void**>(&originalInt)},
        {bindings::GetBurden,reinterpret_cast<void*>(burden),reinterpret_cast<void**>(&originalBurden)},
        {bindings::AddExp,reinterpret_cast<void*>(addExp),reinterpret_cast<void**>(&originalExp)}};
    for(auto& hook:hooks)if(MH_CreateHook(reinterpret_cast<void*>(game+hook.rva),hook.callback,hook.original)!=MH_OK)throw std::runtime_error("Could not prepare all native hooks; no effects enabled.");
    // Queue only our hooks. Do not enable or disable another mod's hooks.
    for(auto& hook:hooks)if(MH_QueueEnableHook(reinterpret_cast<void*>(game+hook.rva))!=MH_OK)throw std::runtime_error("Could not queue native hooks.");
    if(MH_ApplyQueued()!=MH_OK)throw std::runtime_error("Native hook activation failed; restart the game.");
    initialized=true;
}
DWORD WINAPI run(void*) {
    wchar_t dll[32768];GetModuleFileNameW(self,dll,32768);bridge=std::filesystem::path(dll).parent_path();
    FILETIME created,exited,kernel,user;GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user);ULARGE_INTEGER start;start.LowPart=created.dwLowDateTime;start.HighPart=created.dwHighDateTime;processStart=(start.QuadPart-116444736000000000ULL)/10000;
    unsigned char nonce[16];if(BCryptGenRandom(nullptr,nonce,sizeof(nonce),BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0)return 1;
    const char* hex="0123456789abcdef";for(auto v:nonce){session+=hex[v>>4];session+=hex[v&15];}
    try{initialize(bridge.parent_path());}catch(const std::exception& error){initError=error.what();clear();}
    for(;;){
        try{
            std::lock_guard lock(state);
            if(readFile(bridge/L"lease.txt",256)==session){WIN32_FILE_ATTRIBUTE_DATA a{};if(GetFileAttributesExW((bridge/L"lease.txt").c_str(),GetFileExInfoStandard,&a)){ULARGE_INTEGER t;t.LowPart=a.ftLastWriteTime.dwLowDateTime;t.HighPart=a.ftLastWriteTime.dwHighDateTime;auto stamp=static_cast<int64_t>((t.QuadPart-116444736000000000ULL)/10000);auto age=unixMs()-stamp;if(age>=0 && age<5000)leaseDeadline=GetTickCount64()+(5000-age);}}
            if(!live())clear();
            if(pending && !policy::fresh(pending->value("expires_unix_ms",int64_t{}),unixMs())){acknowledge(*pending,false,"No ready game update before command expiry.");pending.reset();}
            mailbox();status();
        }catch(const std::exception&){/* transient file sharing failure; next heartbeat retries */}
        Sleep(100);
    }
}
}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH){self=module;DisableThreadLibraryCalls(module);HANDLE worker=CreateThread(nullptr,0,run,nullptr,0,nullptr);if(worker)CloseHandle(worker);}
    return TRUE;
}
