#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <MinHook.h>
#include <iostream>
#include <cstring>
#include "runtime.h"
#include "game_api.h"
#include "bindings.h"
using namespace cream;
int checks{};
void check(bool value){if(!value)throw std::runtime_error("Assertion "+std::to_string(checks+1));checks++;}
template<class T>void reject(T action){bool denied=false;try{action();}catch(const std::exception&){denied=true;}check(denied);}
struct Fake final:Game {
 int timers{},moneyWrites{},xpWrites{},skillWrites{},refills{},levelValue=1,points=0,total=0,stocked=2;float amount=0,need=100,moneyValue{};bool noTimer{},missingXP{};
 void prepare(const std::string& feature)override{if(feature=="lc_add_xp" && missingXP)throw std::runtime_error("No XP manager loaded.");}
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
namespace patienceFixture {
 int spot{},observed=-1,calls{};bool fail{};
 struct Method{const char* name;unsigned params;};
 Method orderSpot{"get_OrderSpot",0},hide{"HidePatience",1};
 void* customer=reinterpret_cast<void*>(1);void* ui=reinterpret_cast<void*>(2);
 void configure(Api& api){
  api.objectClass=[](void* object){return object;};
  api.methods=[](void* type,void** iterator)->void*{if(*iterator)return nullptr;*iterator=reinterpret_cast<void*>(1);return type==customer?&orderSpot:&hide;};
  api.methodName=[](void* method){return static_cast<Method*>(method)->name;};
  api.paramCount=[](void* method){return static_cast<Method*>(method)->params;};
  api.isGeneric=[](void*){return false;};api.paramType=[](void*,uint32_t)->void*{return &hide;};
  api.typeName=[](void*){return _strdup("System.Int32");};api.free=[](void* value){std::free(value);};
  api.unbox=[](void* value){return value;};
  api.invoke=[](void* method,void* self,void** args,void** error)->void*{
   if(method==&orderSpot){check(self==customer);return &spot;}
   check(method==&hide && self==ui && args && args[0]);
   observed=*static_cast<int*>(args[0]);calls++;if(fail)*error=ui;return nullptr;
  };
 }
}
int main(){try{
 // Exercise the real IL2CPP adapter: the new UI signature must receive the customer's slot, including slot 1.
 NativeGame native;patienceFixture::configure(native.api);
 for(int spot:{0,1}){patienceFixture::spot=spot;native.hidePatience(patienceFixture::customer,patienceFixture::ui);check(patienceFixture::observed==spot);}
 native.hidePatience(patienceFixture::customer,nullptr);check(patienceFixture::calls==2);
 patienceFixture::fail=true;reject([&]{native.hidePatience(patienceFixture::customer,patienceFixture::ui);});check(patienceFixture::calls==3);
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
 check(runtime.epoch!=previous && runtime.flags==1 && runtime.dirtyPatience && game.moneyWrites==1);
 runtime.flush(game);check(game.noTimer && !runtime.dirtyPatience);
 // A context change does not veto an already submitted command or reset toggles.
 auto nextSession=std::string("0123456789abcdef0123456789abcdef_")+std::to_string(runtime.epoch);
 validate(toggle,nextSession,10002);checks++;
 toggle["session_id"]=nextSession;toggle["request_id"]="2123456789abcdef0123456789abcdef";
 validate(toggle,nextSession,10002);
 runtime.apply(toggle,game,10002);check(runtime.active(0,10002));
 runtime.observe({},10003);check(runtime.ready(10003) && runtime.flags==1);
 runtime.flush(game);check(!runtime.dirtyPatience && game.noTimer);
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
 runtime.context={};runtime.frame=0;runtime.lease=0;
 apply(command("lc_money",678));check(game.moneyWrites==2 && game.moneyValue==678);
 apply(command("lc_add_xp",1));check(game.xpWrites==2);
 game.missingXP=true;reject([&]{apply(command("lc_add_xp",1));});check(game.xpWrites==2);
 apply(command("lc_money",456));check(game.moneyValue==456);game.missingXP=false;
 auto oldSession=command("lc_money",789);validate(oldSession,"0123456789abcdef0123456789abcdef_99",10001);
 reject([&]{validate(oldSession,"f123456789abcdef0123456789abcdef_99",10001);});
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
