#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <bcrypt.h>
#include <MinHook.h>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <optional>
#include <atomic>
#include "game_api.h"
#include "bindings.h"
namespace {
using cream::Json;
HMODULE self{};
std::filesystem::path bridge;
std::recursive_mutex state;
cream::Runtime runtime;
cream::NativeGame game;
cream::Inbox inbox;
std::optional<Json> pending;
std::string processSession,lastRequest,lastMessage="Controls dispatch directly. Return to the game to execute queued actions.",initError;
std::atomic<bool> initialized{};bool lastSuccess=true;
uint64_t processStart{},nextProbe{},nextPatienceRefresh{};
Json values=Json::object();
using Tick=void(*)(const void*);
using Consume=void(*)(void*,float,const void*);
Tick originalTick{},originalRestore{};
Consume originalPatience{};
Consume originalConsume{};
int64_t unixMs(){FILETIME f;GetSystemTimeAsFileTime(&f);ULARGE_INTEGER n;n.LowPart=f.dwLowDateTime;n.HighPart=f.dwHighDateTime;return static_cast<int64_t>((n.QuadPart-116444736000000000ULL)/10000);}
std::string session(){return processSession+"_"+std::to_string(runtime.epoch);}
void acknowledge(const Json& c,bool success,const std::string& message){lastRequest=c.value("request_id","");lastSuccess=success;lastMessage=message;}
void patience(void* customer,float duration,const void* method){
 {std::lock_guard lock(state);if(runtime.active(0,GetTickCount64()))return;}
 originalPatience(customer,duration,method);
}
void consume(void* stock,float amount,const void* method){
 {std::lock_guard lock(state);if(runtime.active(1,GetTickCount64()))return;}
 originalConsume(stock,amount,method);
}
void restorePending(const void* method){
 {std::lock_guard lock(state);runtime.context={};runtime.epoch++;game.ownedStocks.clear();values=Json::object();nextProbe=0;}
 originalRestore(method);
}
void tick(const void* method){
 originalTick(method);
 std::lock_guard lock(state);
 if(!initialized)return;
 auto now=GetTickCount64();
 try {
  if(now>=nextProbe || pending || runtime.dirtyPatience){
   nextProbe=now+250;
   try{runtime.observe(game.probe(),now);}catch(const std::exception& error){lastMessage=error.what();runtime.frame=now;}
  }else runtime.frame=now;
  if(now>=runtime.lease)runtime.clear();
  try{runtime.flush(game);if(runtime.active(0,now) && now>=nextPatienceRefresh){game.patience(true);nextPatienceRefresh=now+250;}}
  catch(const std::exception& error){lastMessage=error.what();}
  if(pending){
   auto command=std::move(*pending);pending.reset();
   try{cream::validate(command,session(),unixMs());acknowledge(command,true,runtime.apply(command,game,now));}
   catch(const std::exception& error){acknowledge(command,false,error.what());}
  }
  try{values=game.values();}catch(const std::exception& error){values=Json::object();lastMessage=error.what();}
 }catch(const std::exception& error){
  runtime.clear();runtime.context={};game.ownedStocks.clear();values=Json::object();
  lastMessage=std::string("Trainer paused: ")+error.what();
  if(pending){acknowledge(*pending,false,lastMessage);pending.reset();}
 }
}
// SHA-256 and bounded mailbox helpers are the repository's already-reviewed native I/O path.
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

void status(){
 bool ready=initialized && runtime.ready(GetTickCount64());Json enabled=Json::object(),available=Json::object();
 for(int i=0;i<2;i++){enabled[cream::Toggles[i]]=ready && (runtime.flags&(1u<<i))!=0;available[cream::Toggles[i]]=ready;}
 for(auto action:cream::Actions)available[action]=ready;
 Json reported=values;if(!initError.empty())reported["initialization_error"]=initError;
 Json result={{"schema_version",1},{"version",cream::Version},{"process_id",GetCurrentProcessId()},{"process_start_unix_ms",processStart},
  {"heartbeat_unix_ms",unixMs()},{"process_session",processSession},{"session_id",session()},{"ready",ready},
  {"message",!initError.empty()?initError:ready?"Native trainer connected. Gameplay effects remain unverified.":lastMessage},
  {"enabled",enabled},{"available",available},{"values",reported},{"last_command",lastRequest},
  {"command_result",{{"request_id",lastRequest},{"success",lastSuccess},{"message",lastMessage}}}};
 auto temp=bridge/L"status.tmp";{std::ofstream output(temp,std::ios::binary|std::ios::trunc);output<<result.dump();if(!output)return;}
 MoveFileExW(temp.c_str(),(bridge/L"status.json").c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH);
}
void mailbox(){
 auto path=bridge/L"command.json";
 if(readFile(path,16384).empty())return;
 auto held=bridge/(L"command.processing-"+std::wstring(processSession.begin(),processSession.end())+L".json");
 if(!MoveFileExW(path.c_str(),held.c_str(),0))return;
 Json command;
 try {
  command=Json::parse(readFile(held,16384));auto now=unixMs();cream::validate(command,session(),now);
  if(!inbox.claim(command.value("request_id",""),now)){DeleteFileW(held.c_str());return;}
  if(pending)acknowledge(command,false,"Trainer is busy. Wait for the previous command.");
  else if(!initialized)acknowledge(command,false,initError.empty()?"Trainer initialization is not ready.":initError);
  else pending=command;
 }catch(const std::exception& error){if(command.is_object())acknowledge(command,false,error.what());else lastMessage=error.what();}
 DeleteFileW(held.c_str());
}
void initialize(const std::filesystem::path& root){
 wchar_t path[32768];GetModuleFileNameW(nullptr,path,32768);
 if(std::filesystem::path(path).filename()!=L"Livin' the Cream Ice Cream Simulator.exe")throw std::runtime_error("Unsupported host. Connect only to Livin' the Cream.");
 if(hashFile(root/L"GameAssembly.dll")!=bindings::AssemblyHash || hashFile(root/L"Livin' the Cream Ice Cream Simulator_Data/il2cpp_data/Metadata/global-metadata.dat")!=bindings::MetadataHash)
  throw std::runtime_error("Unsupported native game build. Check this game's trainer update.");
 auto module=GetModuleHandleW(L"GameAssembly.dll");if(!module)throw std::runtime_error("Load the game before connecting.");
 for(auto binding:bindings::sentinels)if(std::memcmp(reinterpret_cast<void*>(reinterpret_cast<uintptr_t>(module)+binding.rva),binding.bytes,24))throw std::runtime_error("Native binding bytes differ from the inspected build.");
 game.initialize(module);
 auto thread=game.api.threadAttach(game.api.domainGet());if(!thread)throw std::runtime_error("IL2CPP is not ready. Connect after loading your shop.");
 struct Hook{const char* type;const char* method;std::initializer_list<const char*> parameters;uintptr_t rva;void* callback;void** original;};
 Hook hooks[]={
  {"GameInput","Tick",{},bindings::Tick,reinterpret_cast<void*>(tick),reinterpret_cast<void**>(&originalTick)},
  {"Customer","StartPatienceDuration",{"System.Single"},bindings::Patience,reinterpret_cast<void*>(patience),reinterpret_cast<void**>(&originalPatience)},
  {"SoftServeStock","DecreaseStock",{"System.Single"},bindings::Consume,reinterpret_cast<void*>(consume),reinterpret_cast<void**>(&originalConsume)},
  {"ShopSaveManager","MarkRestorePending",{},bindings::Restore,reinterpret_cast<void*>(restorePending),reinterpret_cast<void**>(&originalRestore)}};
 std::vector<void*> prepared;
 try {
  if(MH_Initialize()!=MH_OK)throw std::runtime_error("Native hook initialization failed.");
  for(auto hook:hooks){
   auto klass=game.api.klass("Assembly-CSharp.dll","",hook.type);auto method=game.api.method(klass,hook.method,hook.parameters);
   auto target=reinterpret_cast<void*>(reinterpret_cast<uintptr_t>(module)+hook.rva);
   if(*reinterpret_cast<void**>(method)!=target)throw std::runtime_error("Runtime method identity differs from the inspected build.");
   if(MH_CreateHook(target,hook.callback,hook.original)!=MH_OK)throw std::runtime_error("Could not prepare all native hooks. No cheat was enabled.");
   prepared.push_back(target);
  }
  for(auto target:prepared)if(MH_QueueEnableHook(target)!=MH_OK)throw std::runtime_error("Could not queue native hooks.");
  if(MH_ApplyQueued()!=MH_OK)throw std::runtime_error("Native hook activation failed. Restart the game before reconnecting.");
  initialized=true;
 }catch(...){for(auto target:prepared){MH_DisableHook(target);MH_RemoveHook(target);}game.api.threadDetach(thread);throw;}
 game.api.threadDetach(thread);
}
DWORD WINAPI run(void*){
 wchar_t dll[32768];GetModuleFileNameW(self,dll,32768);bridge=std::filesystem::path(dll).parent_path();
 FILETIME created,exited,kernel,user;GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user);ULARGE_INTEGER start;start.LowPart=created.dwLowDateTime;start.HighPart=created.dwHighDateTime;processStart=(start.QuadPart-116444736000000000ULL)/10000;
 unsigned char nonce[16];if(BCryptGenRandom(nullptr,nonce,sizeof(nonce),BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0)return 1;
 const char* hex="0123456789abcdef";for(auto value:nonce){processSession+=hex[value>>4];processSession+=hex[value&15];}
 try{initialize(bridge.parent_path());}catch(const std::exception& error){initError=error.what();runtime.clear();}
 for(;;){
  try{
   std::lock_guard lock(state);
   if(readFile(bridge/L"lease.txt",256)==processSession){WIN32_FILE_ATTRIBUTE_DATA a{};if(GetFileAttributesExW((bridge/L"lease.txt").c_str(),GetFileExInfoStandard,&a)){
    ULARGE_INTEGER t;t.LowPart=a.ftLastWriteTime.dwLowDateTime;t.HighPart=a.ftLastWriteTime.dwHighDateTime;auto age=unixMs()-static_cast<int64_t>((t.QuadPart-116444736000000000ULL)/10000);if(age>=0 && age<5000)runtime.lease=GetTickCount64()+5000-age;
   }}
   if(GetTickCount64()>=runtime.lease)runtime.clear();
   if(pending && pending->value("expires_unix_ms",int64_t{})<=unixMs()){acknowledge(*pending,false,"No game input tick before command expiry. Return to the game and retry; actions are never replayed.");pending.reset();}
   mailbox();status();
  }catch(const std::exception&){/* Sharing gaps are retried without replaying mutations. */}
  Sleep(100);
 }
}
}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID){
 if(reason==DLL_PROCESS_ATTACH){self=module;DisableThreadLibraryCalls(module);HANDLE thread=CreateThread(nullptr,0,run,nullptr,0,nullptr);if(thread)CloseHandle(thread);}
 return TRUE;
}
