#pragma once
#include <windows.h>
#include <vector>
#include <set>
#include <cstring>
#include <initializer_list>
#include "runtime.h"
namespace cream {
struct Api {
 void*(*domainGet)(){};void*(*threadAttach)(void*){};void(*threadDetach)(void*){};
 void**(*assemblies)(void*,size_t*){};void*(*image)(void*){};const char*(*imageName)(void*){};
 void*(*classFromName)(void*,const char*,const char*){};void*(*classType)(void*){};void*(*typeObject)(void*){};
 void*(*methods)(void*,void**){};const char*(*methodName)(void*){};uint32_t(*paramCount)(void*){};
 bool(*isGeneric)(void*){};void*(*paramType)(void*,uint32_t){};char*(*typeName)(void*){};void(*free)(void*){};
 void*(*invoke)(void*,void*,void**,void**){};void*(*unbox)(void*){};void*(*objectClass)(void*){};
 void*(*field)(void*,const char*){};void(*fieldValue)(void*,void*,void*){};
 void*(*stringNew)(const char*){};size_t(*arrayLength)(void*){};
 template<class T>void load(HMODULE module,T& target,const char* name){auto address=GetProcAddress(module,name);static_assert(sizeof(target)==sizeof(address));std::memcpy(&target,&address,sizeof(target));if(!target)throw std::runtime_error(std::string("Missing IL2CPP export: ")+name);}
 void initialize(HMODULE module){
#define LC_API(member,name) load(module,member,"il2cpp_" name)
 LC_API(domainGet,"domain_get");LC_API(threadAttach,"thread_attach");LC_API(threadDetach,"thread_detach");
 LC_API(assemblies,"domain_get_assemblies");LC_API(image,"assembly_get_image");LC_API(imageName,"image_get_name");
 LC_API(classFromName,"class_from_name");LC_API(classType,"class_get_type");LC_API(typeObject,"type_get_object");
 LC_API(methods,"class_get_methods");LC_API(methodName,"method_get_name");LC_API(paramCount,"method_get_param_count");
 LC_API(isGeneric,"method_is_generic");LC_API(paramType,"method_get_param");LC_API(typeName,"type_get_name");LC_API(free,"free");
 LC_API(invoke,"runtime_invoke");LC_API(unbox,"object_unbox");LC_API(objectClass,"object_get_class");
 LC_API(field,"class_get_field_from_name");LC_API(fieldValue,"field_get_value");LC_API(stringNew,"string_new");LC_API(arrayLength,"array_length");
#undef LC_API
 }
 void* klass(const char* imageId,const char* space,const char* name){
  size_t count{};auto list=assemblies(domainGet(),&count);if(count>1000)throw std::runtime_error("Invalid IL2CPP assembly list.");
  for(size_t i=0;i<count;i++){auto img=image(list[i]);if(std::strcmp(imageName(img),imageId)==0){auto c=classFromName(img,space,name);if(c)return c;}}
  throw std::runtime_error(std::string("Game class unavailable: ")+space+"."+name);
 }
 void* method(void* c,const char* name,std::initializer_list<const char*> signature={}){
  void* iterator{};void* found{};
  while(auto m=methods(c,&iterator)){
   if(std::strcmp(methodName(m),name) || paramCount(m)!=signature.size() || isGeneric(m))continue;
   unsigned i=0;bool match=true;for(auto wanted:signature){char* actual=typeName(paramType(m,i++));bool same=actual && std::strcmp(actual,wanted)==0;if(actual)free(actual);if(!same){match=false;break;}}
   if(match){if(found)throw std::runtime_error(std::string("Ambiguous game method: ")+name);found=m;}
  }
  if(!found)throw std::runtime_error(std::string("Game method unavailable: ")+name);
  return found;
 }
 void* call(void* method,void* self=nullptr,std::initializer_list<void*> args={}){
  std::vector<void*> values(args);void* exception{};void* result=invoke(method,self,values.data(),&exception);
  if(exception)throw std::runtime_error(std::string("Native game method rejected the operation: ")+methodName(method)+". Check the in-game value before repeating a grant.");
  return result;
 }
 template<class T>T scalar(void* m,void* self=nullptr,std::initializer_list<void*> args={}){auto boxed=call(m,self,args);auto data=boxed?unbox(boxed):nullptr;if(!data)throw std::runtime_error("Game returned no value.");T value{};std::memcpy(&value,data,sizeof(T));return value;}
 template<class T>T get(void* self,const char* name){if(!self)throw std::runtime_error("Game object unavailable.");auto f=field(objectClass(self),name);if(!f)throw std::runtime_error(std::string("Game field unavailable: ")+name);T value{};fieldValue(self,f,&value);return value;}
};
struct NativeGame final:Game {
 Api api;
 void *playerType{},*shopType{},*levelType{},*skillType{},*stockType{},*find{},*getShop{},*getMoney{},*setMoney{},*addXP{},*getXP{},*getLevel{},*getRequired{},*earnSkills{},*currentStyle{},*refresh{},*stockAmount{},*stockMax{},*stockRestore{};
 void *shop{},*levels{},*skills{},*skillData{};
 std::set<void*> ownedStocks;
 void initialize(HMODULE module){
  api.initialize(module);auto thread=api.threadAttach(api.domainGet());if(!thread)throw std::runtime_error("IL2CPP is not ready. Connect after loading your shop.");
  try {
   auto game=[&](const char* name){return api.klass("Assembly-CSharp.dll","",name);};
   playerType=game("Player");shopType=game("Shop");levelType=game("LevelManager");skillType=game("SkillTreeManager");stockType=game("SoftServeStock");
   auto unity=[&](const char* name){return api.klass("UnityEngine.CoreModule.dll","UnityEngine",name);};
   find=api.method(unity("Object"),"FindObjectsByType",{"System.Type","UnityEngine.FindObjectsSortMode"});
   getShop=api.method(playerType,"GetCurrentShop");
   auto money=api.klass("Assembly-CSharp.dll","makif.MoneyKit","Money");getMoney=api.method(money,"GetMoney");setMoney=api.method(money,"SetMoney",{"System.Single","System.String","System.Boolean"});
   addXP=api.method(levelType,"AddXP",{"System.Single"});getXP=api.method(levelType,"GetCurrentXP");getLevel=api.method(levelType,"GetCurrentLevel");getRequired=api.method(levelType,"GetXPRequiredForLevel",{"System.Int32"});
   earnSkills=api.method(skillType,"EarnSkillPoint",{"System.Int32"});currentStyle=api.method(game("PlayStyleRules"),"get_Current");refresh=api.method(game("Shop"),"OnPlayStyleChanged",{"PlayStyle"});
   stockAmount=api.method(stockType,"GetStockAmount");stockMax=api.method(stockType,"get_MaxStock");stockRestore=api.method(stockType,"RestoreStock",{"System.Single"});
  } catch(...){api.threadDetach(thread);throw;}api.threadDetach(thread);
 }
 std::vector<void*> objects(void* type){
  int sort=0;auto reflection=api.typeObject(api.classType(type));auto array=api.call(find,nullptr,{reflection,&sort});
  std::vector<void*> result;if(!array)return result;size_t count=api.arrayLength(array);if(count>100000)throw std::runtime_error("Unexpected game object count.");
  auto data=reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(array)+4*sizeof(void*));
  for(size_t i=0;i<count;i++)if(data[i])result.push_back(data[i]);
  return result;
 }
 void* first(void* type){auto found=objects(type);return found.empty()?nullptr:found.front();}
 void* currentShop(){
  for(auto value:objects(playerType)){auto current=api.call(getShop,value);if(current)return current;}
  return first(shopType);
 }
 void stocks(){ownedStocks.clear();for(auto stock:objects(stockType))ownedStocks.insert(stock);}
 Context probe(){
  // Diagnostic observation only: unrelated managers, restore flags and database identities never veto controls.
  Context context{};context.valid=true;
  stocks();return context;
 }
 void prepare(const std::string& feature)override{
  if(feature=="lc_add_xp"){
   levels=first(levelType);if(!levels)throw std::runtime_error("The game has no loaded LevelManager for the XP grant.");
  }else if(feature=="lc_add_skills"){
   skills=first(skillType);if(!skills)throw std::runtime_error("The game has no loaded SkillTreeManager for the skill-point grant.");
   skillData=api.get<void*>(skills,"DB");if(!skillData)throw std::runtime_error("The skill-point manager has no loaded point database.");
  }else if(feature=="lc_refill_softserve")stocks();
 }
 void* customer(){if(!shop)return nullptr;auto list=api.get<void*>(shop,"currentCustomers");if(!list)return nullptr;auto type=api.objectClass(list);if(api.scalar<int>(api.method(type,"get_Count"),list)<=0)return nullptr;int zero=0;return api.call(api.method(type,"get_Item",{"System.Int32"}),list,{&zero});}
 void patience(bool disabled)override{
  shop=currentShop();if(!shop)return;
  if(!disabled){auto style=api.scalar<int>(currentStyle);api.call(refresh,shop,{&style});return;}
  auto front=customer();if(!front)return;
  auto type=api.objectClass(front);
  if(!api.scalar<bool>(api.method(type,"get_HasActiveOrder"),front))return;
  api.call(api.method(type,"StopPatience"),front);
  auto ui=api.get<void*>(shop,"orderUI");if(ui)api.call(api.method(api.objectClass(ui),"HidePatience"),ui);
 }
 void money(float value)override{auto source=api.stringNew("HappyCheats");bool silent=true;api.call(setMoney,nullptr,{&value,source,&silent});}
 float xp()override{return api.scalar<float>(getXP,levels);}
 int level()override{return api.scalar<int>(getLevel,levels);}
 float required(int value)override{return api.scalar<float>(getRequired,levels,{&value});}
 void addXp(float value)override{api.call(addXP,levels,{&value});}
 std::pair<int,int> skillCounts()override{return {api.get<int>(skillData,"SkillPoint"),api.get<int>(skillData,"TotalEarned")};}
 void addSkills(int value)override{api.call(earnSkills,skills,{&value});}
 int refill()override{int count=0;for(auto stock:ownedStocks)if(api.scalar<float>(stockAmount,stock)>0){float maximum=api.scalar<float>(stockMax,stock);if(!std::isfinite(maximum) || maximum<=0 || maximum>1000000)throw std::runtime_error("Invalid reservoir capacity.");api.call(stockRestore,stock,{&maximum});count++;}return count;}
 Json values(){Json result=Json::object();try{result["lc_money"]=std::to_string(api.scalar<float>(getMoney));}catch(const std::exception&){}return result;}
};
}
