#include "../trainer.cpp"
#include <array>
#include <iostream>
#include <stdexcept>
namespace {
int checks{},goldWrites{},survivalWrites{},observedInt{};
float observedFloat{};
void* fakeRole{};
template<class T> void put(uintptr_t at,T v){memcpy(reinterpret_cast<void*>(at),&v,sizeof(v));}
void check(bool b,const char* message){checks++;if(!b)throw std::runtime_error(message);}
template<class F> void reject(F f){bool rejected=false;try{f();}catch(const std::exception&){rejected=true;}check(rejected,"expected rejection");}
void floatSink(void*,int,float v,bool,const void*){observedFloat=v;}
void intSink(void*,int,int v,const void*){observedInt=v;}
void updateSink(void*,float,float,const void*){}
void endSink(void*,const void*){}
int burdenSink(void*,const void*){return 125;}
int observedBase{},observedDiscovery{};
void xpSink(void*,int,void*,int base,int,int discovery,int,int*,const void*){observedBase=base;observedDiscovery=discovery;}
Json command(const char* feature) {return {{"feature",feature},{"expires_unix_ms",unixMs()+4000},{"request_id","0123456789abcdef0123456789abcdef"}};}
}
int main(){try{
    for(int attr=1;attr<=5;attr++){
        check(policy::stat(attr,31,42,42,true),"owned stat missing");
        check(!policy::stat(attr,31,99,42,true),"NPC affected");
        check(!policy::stat(attr,31,42,42,false),"expired lease affected");
    }
    check(!policy::stat(80001,127,42,42,true),"NPC enum affected");
    check(policy::experience(INT_MAX,100)==INT_MAX,"XP overflow");
    check(policy::experience(-5,100)==-5 && policy::experience(12,3)==36,"XP sign");
    for(auto id:numbers){check(!policy::numeric(id,0.5),"fraction accepted");check(!policy::numeric(id,NAN),"NaN accepted");check(!policy::numeric(id,INFINITY),"infinity accepted");}
    check(policy::numeric("sl_money",0) && !policy::numeric("sl_money",100000001),"money bounds");
    check(!policy::numeric("sl_survival_points",0) && policy::numeric("sl_survival_points",1000000),"points bounds");
    check(!policy::fresh(10,10) && !policy::fresh(5011,10) && policy::fresh(5010,10),"expiry bounds");
    std::array<unsigned char,0x400> w{},agentManager{},a{},component{},timer{},results{},proficiency{};
    uintptr_t W=reinterpret_cast<uintptr_t>(w.data()),M=reinterpret_cast<uintptr_t>(agentManager.data()),A=reinterpret_cast<uintptr_t>(a.data());
    uintptr_t C=reinterpret_cast<uintptr_t>(component.data()),T=reinterpret_cast<uintptr_t>(timer.data()),R=reinterpret_cast<uintptr_t>(results.data()),P=reinterpret_cast<uintptr_t>(proficiency.data());
    put(W+0x18,true);put(W+0x40,M);put(W+0x80,T);put(W+0x160,R);put(W+0x158,P);put(A+0x10,int64_t{42});put(C+0x30,int64_t{42});put(T+0x10,int64_t{9});
    fakeRole=a.data();getLeading=[](void*,const void*){return fakeRole;};getTotalFloat=[](void*,int attr,const void*){return attr>=101 && attr<=105 ? 120.f:0.f;};getTotalInt=[](void*,int,const void*){return 120000;};
    setGold=[](void*,int,const void*){goldWrites++;};addSurvival=[](void*,int,const void*){survivalWrites++;};
    originalUpdate=updateSink;originalEnd=endSink;originalFloat=floatSink;originalInt=intSink;originalBurden=burdenSink;originalExp=xpSink;
    leaseDeadline=GetTickCount64()+5000;update(w.data(),0.1f,0.1f,nullptr);
    check(live() && player==42,"world did not identify live player");
    mask=31;setFloat(component.data(),1,1.f,false,nullptr);check(observedFloat==120.f,"owned float was not refilled");
    setInt(component.data(),5,1,nullptr);check(observedInt==120000,"owned integer was not refilled");
    put(C+0x30,int64_t{43});setFloat(component.data(),1,1.f,false,nullptr);check(observedFloat==1.f,"foreign float changed");put(C+0x30,int64_t{42});
    mask=1<<6;check(burden(component.data(),nullptr)==1000000,"player burden unchanged");put(C+0x30,int64_t{43});check(burden(component.data(),nullptr)==125,"foreign burden changed");put(C+0x30,int64_t{42});
    xp=3;addExp(proficiency.data(),1,nullptr,10,1,4,0,nullptr,nullptr);check(observedBase==30 && observedDiscovery==12,"XP multiplication failed");
    addExp(results.data(),1,nullptr,10,1,4,0,nullptr,nullptr);check(observedBase==10 && observedDiscovery==4,"foreign manager XP changed");
    pendingPlayer=42;auto money=command("sl_money");money["value"]=5000;execute(money);check(goldWrites==1,"one-shot money not applied");
    money["expires_unix_ms"]=unixMs()-1;reject([&]{execute(money);});check(goldWrites==1,"expired grant replayed");
    auto points=command("sl_survival_points");points["value"]=100;execute(points);check(survivalWrites==1,"points not applied");put(R+0x30,INT_MAX);reject([&]{execute(points);});check(survivalWrites==1,"points overflow");
    put(R+0x30,0);put(R+0x34,INT_MAX);reject([&]{execute(points);});check(survivalWrites==1,"lifetime points overflow");
    auto on=command("sl_freeze_clock");on["enabled"]=true;execute(on);update(w.data(),.1f,.1f,nullptr);check(read<bool>(T+0x44),"clock was not frozen");
    auto off=command("sl_freeze_clock");off["enabled"]=false;execute(off);check(!read<bool>(T+0x44),"clock prior value not restored");
    put(T+0x44,true);execute(on);update(w.data(),.1f,.1f,nullptr);execute(off);check(read<bool>(T+0x44),"originally frozen clock not preserved");
    put(T+0x44,false);execute(on);update(w.data(),.1f,.1f,nullptr);put(T+0x44,false);update(w.data(),.1f,.1f,nullptr);check(!read<bool>(T+0x44) && !(mask.load()&(1<<5)),"foreign clock update overwritten");
    mask=127;xp=10;pending=command("sl_money");(*pending)["value"]=9000;pendingPlayer=42;put(A+0x10,int64_t{44});update(w.data(),.1f,.1f,nullptr);
    check(mask==0 && xp==1 && player==44 && !lastSuccess && goldWrites==1,"player replacement retained effects or replayed money");
    mask=127;leaseDeadline=0;update(w.data(),.1f,.1f,nullptr);check(mask==0 && !live(),"lease expiry kept effects");
    auto disable=command("disable_all");execute(disable);check(mask==0 && xp==1,"disable all failed");
    endBattle(w.data(),nullptr);check(player==0 && world==0,"end battle retained pointers");
    auto temporary=std::filesystem::temp_directory_path()/("happycheats-survival-mailbox-"+std::to_string(GetCurrentProcessId())+"-"+std::to_string(GetTickCount64()));
    check(std::filesystem::create_directory(temporary),"fixture directory already exists");bridge=temporary;session="fixture-native-session";
    leaseDeadline=GetTickCount64()+5000;update(w.data(),.1f,.1f,nullptr);initialized=true;
    auto publish=[&](Json c,const std::string& id){c["schema_version"]=1;c["version"]=version;c["session_id"]=session;c["request_id"]=id;std::ofstream output(bridge/L"command.json");output<<c.dump();};
    auto grant=command("sl_money");grant["value"]=700;
    publish(grant,std::string(32,'a'));mailbox();check(pending.has_value(),"valid mailbox not queued");update(w.data(),.1f,.1f,nullptr);check(goldWrites==2 && lastSuccess,"queued gold not executed");
    auto multiplier=command("sl_xp");multiplier["value"]=2;publish(multiplier,std::string(32,'b'));mailbox();update(w.data(),.1f,.1f,nullptr);check(xp==2,"second command failed");
    publish(grant,std::string(32,'a'));mailbox();check(!pending && goldWrites==2,"old ID replayed after another command");
    auto stale=grant;stale["expires_unix_ms"]=unixMs()-1;publish(stale,std::string(32,'c'));mailbox();check(!pending && !lastSuccess && goldWrites==2,"expired mailbox applied");
    auto bad=grant;bad["session_id"]="other-session";bad["schema_version"]=1;bad["version"]=version;bad["request_id"]=std::string(32,'d');{std::ofstream output(bridge/L"command.json");output<<bad.dump();}mailbox();check(!pending && goldWrites==2,"foreign session applied");
    status();{std::ifstream input(bridge/L"status.json");Json json;input>>json;check(json["process_id"]==GetCurrentProcessId() && json["ready"]==true,"atomic native status invalid");}
    endBattle(w.data(),nullptr);initialized=false;std::filesystem::remove_all(temporary);
    reject([&]{initialize(std::filesystem::temp_directory_path());});check(!initialized,"unsupported host initialized");
    // Exercise the actual relocation engine against every pinned hook prologue in synthetic executable pages.
    check(MH_Initialize()==MH_OK,"fixture MinHook init failed");
    for(auto rva:{bindings::WorldUpdate,bindings::EndBattle,bindings::SetFloat,bindings::SetInt,bindings::GetBurden,bindings::AddExp}) {
        auto page=VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE);check(page!=nullptr,"fixture page failed");memset(page,0x90,4096);
        for(auto& sentinel:bindings::sentinels)if(sentinel.rva==rva)memcpy(page,sentinel.bytes,24);
        void* original{};check(MH_CreateHook(page,reinterpret_cast<void*>(updateSink),&original)==MH_OK,"pinned prologue cannot relocate");
        check(MH_EnableHook(page)==MH_OK && MH_DisableHook(page)==MH_OK && MH_RemoveHook(page)==MH_OK,"synthetic hook activation failed");VirtualFree(page,0,MEM_RELEASE);
    }
    MH_Uninitialize();
    std::cout<<"PASS Survival Log native fixture: "<<checks<<" assertions; player ownership, lease, stat refill, XP, one-shot expiry, clock restoration, player replacement and six exact prologue relocations. No game launched.\n";
    return 0;
}catch(const std::exception& e){std::cerr<<"FAIL "<<e.what()<<"\n";return 1;}}
