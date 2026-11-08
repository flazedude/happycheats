#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <MinHook.h>
#include <iostream>
#include <cstring>
#include "runtime.h"
#include "bindings.h"
using namespace cream;
int checks{};
void check(bool value){if(!value)throw std::runtime_error("Assertion "+std::to_string(checks+1));checks++;}
template<class T>void reject(T action){bool denied=false;try{action();}catch(const std::exception&){denied=true;}check(denied);}
struct Fake final:Game {
 int timers{},moneyWrites{},xpWrites{},skillWrites{},refills{},levelValue=1,points=0,total=0,stocked=2;float amount=0,need=100,moneyValue{};bool noTimer{};
 void patience(bool value)override{timers++;noTimer=value;}
 void money(float value)override{moneyWrites++;moneyValue=value;}
 float xp()override{return amount;}int level()override{return levelValue;}float required(int)override{return need;}
 void addXp(float value)override{xpWrites++;amount+=value;while(amount>=need){amount-=need;levelValue++;points+=2;}}
 std::pair<int,int> skillCounts()override{return {points,total};}
 void addSkills(int value)override{skillWrites++;points+=value;total+=value;}
 int refill()override{refills++;return stocked;}
};
Json command(const std::string& id,double value=1){return {{"schema_version",1},{"version",Version},{"session_id","0123456789abcdef0123456789abcdef_1"},{"request_id","1123456789abcdef0123456789abcdef"},{"expires_unix_ms",int64_t(15000)},{"feature",id},{"value",value}};}
void detour(){}
int main(){try{
 Runtime runtime;Fake game;Context first;first.valid=true;first.key[0]=1;runtime.lease=20000;runtime.observe(first,10000);
 auto apply=[&](Json c){validate(c,"0123456789abcdef0123456789abcdef_1",10001);return runtime.apply(c,game,10001);};
 check(runtime.ready(10001));apply(command("lc_money",12345));check(game.moneyWrites==1 && game.moneyValue==12345);
 apply(command("lc_add_xp",250));check(game.xpWrites==1 && game.levelValue==3 && game.amount==50 && game.points==4);
 apply(command("lc_add_skills",10));check(game.skillWrites==1 && game.points==14 && game.total==10);
 for(auto id:{"lc_money","lc_add_xp","lc_add_skills"})for(double value:{-1.0,0.5,100000001.0})reject([&]{apply(command(id,value));});
 game.need=0;reject([&]{apply(command("lc_add_xp",1));});check(game.xpWrites==1);game.need=100;
 game.total=INT_MAX;reject([&]{apply(command("lc_add_skills",1));});check(game.skillWrites==1);
 for(int bit=0;bit<2;bit++){auto c=command(Toggles[bit]);c.erase("value");c["enabled"]=true;apply(c);check(runtime.active(bit,10001));c["enabled"]=false;apply(c);check(!runtime.active(bit,10001));}
 check(game.timers==2 && !game.noTimer);
 auto refill=command("lc_refill_softserve");refill.erase("value");apply(refill);check(game.refills==1);game.stocked=0;reject([&]{apply(refill);});
 auto toggle=command("lc_no_patience");toggle.erase("value");toggle["enabled"]=true;apply(toggle);
 auto previous=runtime.epoch;Context second=first;second.key[7]=2;runtime.observe(second,10002);
 check(runtime.epoch!=previous && runtime.flags==0 && runtime.dirtyPatience && game.moneyWrites==1);
 runtime.flush(game);check(!game.noTimer && !runtime.dirtyPatience);
 // Only a new, explicitly dispatched request can restore an effect after a new context.
 auto nextSession=std::string("0123456789abcdef0123456789abcdef_")+std::to_string(runtime.epoch);
 reject([&]{validate(toggle,nextSession,10002);});
 toggle["session_id"]=nextSession;toggle["request_id"]="2123456789abcdef0123456789abcdef";
 validate(toggle,nextSession,10002);
 runtime.apply(toggle,game,10002);check(runtime.active(0,10002));
 runtime.observe({},10003);check(!runtime.ready(10003) && runtime.flags==0);
 runtime.flush(game);check(runtime.dirtyPatience); // Defer native restoration until a valid shop exists.
 toggle["enabled"]=false;runtime.apply(toggle,game,10003);check(runtime.flags==0);
 auto disable=command("disable_all");disable.erase("value");runtime.apply(disable,game,10003);check(runtime.flags==0);
 runtime.observe(first,10004);runtime.lease=20000;toggle["enabled"]=true;runtime.apply(toggle,game,10004);
 runtime.lease=10005;runtime.observe(first,10006);runtime.flush(game);check(!runtime.active(0,10006) && !game.noTimer);
 check(!runtime.ready(11007));
 auto c=command("lc_money");c["expires_unix_ms"]=9999;reject([&]{apply(c);});c["expires_unix_ms"]=20000;reject([&]{apply(c);});
 c=command("lc_money");c["session_id"]="changed";reject([&]{apply(c);});
 for(auto key:{"schema_version","version","session_id","request_id","expires_unix_ms"}){c=command("lc_money");c.erase(key);reject([&]{apply(c);});}
 c=command("lc_money");c["enabled"]=true;reject([&]{apply(c);});c=command("unknown",1);reject([&]{apply(c);});
 c=command("lc_no_patience");c.erase("value");reject([&]{apply(c);});
 Inbox inbox;check(inbox.claim("same",10000));check(!inbox.claim("same",10001));check(inbox.claim("next",10001));
 check(game.moneyWrites==1 && game.xpWrites==1 && game.skillWrites==1);
 // Relocate the four actual inspected prologues in disposable executable memory; never execute game code.
 check(MH_Initialize()==MH_OK);
 for(auto binding:bindings::sentinels){
  auto page=VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_EXECUTE_READWRITE);check(page!=nullptr);
  std::memset(page,0x90,4096);std::memcpy(page,binding.bytes,24);void* original{};
  check(MH_CreateHook(page,reinterpret_cast<void*>(detour),&original)==MH_OK);check(original!=nullptr);
  check(MH_RemoveHook(page)==MH_OK);check(VirtualFree(page,0,MEM_RELEASE)!=0);
 }
 check(MH_Uninitialize()==MH_OK);
 std::cout<<"PASS Livin Cream native runtime: "<<checks<<" assertions; six controls, context/lease changes, explicit restoration, no grant replay, expiry, overflow and four actual prologue relocations. No game launched.\n";
 return 0;
}catch(const std::exception& error){std::cerr<<error.what()<<"\n";return 1;}}
