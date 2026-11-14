#pragma once
#include <array>
#include <cstdint>
#include <cmath>
#include <climits>
#include <map>
#include <stdexcept>
#include <string>
#include <nlohmann/json.hpp>
namespace cream {
using Json=nlohmann::json;
inline constexpr const char* Version="1.0.4";
inline constexpr const char* Toggles[]={"lc_no_patience","lc_unlimited_softserve"};
inline constexpr const char* Actions[]={"lc_money","lc_add_xp","lc_add_skills","lc_refill_softserve"};
struct Context {std::array<uintptr_t,12> key{}; bool valid{}; bool operator==(const Context&)const=default;};
struct Game {
 virtual ~Game()=default;
 virtual void prepare(const std::string&){}
 virtual void patience(bool)=0;
 virtual void money(float)=0;
 virtual float xp()=0;
 virtual int level()=0;
 virtual float required(int)=0;
 virtual void addXp(float)=0;
 virtual std::pair<int,int> skillCounts()=0;
 virtual void addSkills(int)=0;
 virtual int refill()=0;
};
inline bool sameProcess(const std::string& left,const std::string& right){return left.size()>33 && right.size()>33 && left.substr(0,33)==right.substr(0,33) && left[32]=='_' && left.substr(0,32).find_first_not_of("0123456789abcdef")==std::string::npos && left.substr(33).find_first_not_of("0123456789")==std::string::npos;}
inline void validate(const Json& c,const std::string& session,int64_t now) {
 auto id=c.value("request_id",""); auto end=c.value("expires_unix_ms",int64_t{});
 if(c.value("schema_version",0)!=1 || c.value("version","")!=Version || !sameProcess(c.value("session_id",""),session)
   || id.size()!=32 || id.find_first_not_of("0123456789abcdef")!=std::string::npos)
   throw std::runtime_error("Trainer session/request changed. Refresh and retry.");
 if(end<=now || end>now+5000)throw std::runtime_error("Command expired or is dated in the future. Check the value before retrying.");
 auto action=c.value("feature","");
 for(auto id:Toggles)if(action==id){
  if(!c.contains("enabled") || !c["enabled"].is_boolean() || c.contains("value"))throw std::runtime_error("An explicit On/Off state is required.");
  return;
 }
 if(c.contains("enabled"))throw std::runtime_error("This action does not accept a toggle state.");
 if(action=="disable_all" || action=="lc_refill_softserve") {if(c.contains("value"))throw std::runtime_error("Unexpected numeric value.");return;}
 if(!c.contains("value") || !c["value"].is_number())throw std::runtime_error("Numeric value required.");
 double v=c["value"].get<double>();
 double minimum=action=="lc_money"?0:1,maximum=action=="lc_money"?100000000:action=="lc_add_xp"?10000:action=="lc_add_skills"?1000:-1;
 if(!std::isfinite(v) || std::floor(v)!=v || v<minimum || v>maximum)throw std::runtime_error("Choose a whole number within the control's range.");
}
struct Inbox {
 std::map<std::string,int64_t> seen;
 bool claim(const std::string& id,int64_t now) {
  for(auto i=seen.begin();i!=seen.end();)if(i->second<now)i=seen.erase(i);else ++i;
  return seen.emplace(id,now+10000).second;
 }
};
struct Runtime {
 Context context; uint64_t epoch{},frame{},lease{}; unsigned flags{}; bool dirtyPatience{};
 bool ready(uint64_t now)const{return now<lease;}
 bool active(int bit,uint64_t now)const{return now<lease && (flags&(1u<<bit));}
 void clear(){dirtyPatience|=(flags&1)!=0;flags=0;}
 bool observe(Context next,uint64_t now) {
  bool changed=!(next==context);
  if(changed){dirtyPatience|=(flags&1)!=0;epoch++;context=next;}
  frame=now;
  if(now>=lease)clear();
  return changed;
 }
 void flush(Game& game){if(dirtyPatience){game.patience((flags&1)!=0);dirtyPatience=false;}}
 std::string apply(const Json& command,Game& game,uint64_t) {
  auto action=command.value("feature","");
  if(action=="disable_all"){clear();flush(game);return "All continuous cheats are off.";}
  for(int i=0;i<2;i++)if(action==Toggles[i]) {
   bool on=command.at("enabled").get<bool>();
   auto old=flags;
   if(on)flags|=1u<<i;else flags&=~(1u<<i);
   if(i==0 && old!=flags){dirtyPatience=true;try{flush(game);}catch(...){if(on)flags=old;throw;}}
   return on?"Control turned on.":"Control turned off.";
  }
  game.prepare(action);
  if(action=="lc_refill_softserve"){
   int count=game.refill();if(!count)throw std::runtime_error("Fill at least one soft-serve machine with a flavor first. Empty machines are preserved.");
   return "Refilled "+std::to_string(count)+" soft-serve reservoirs; flavors preserved.";
  }
  double value=command.at("value").get<double>();
  if(action=="lc_money"){game.money(static_cast<float>(value));return "Money set.";}
  if(action=="lc_add_skills"){
   auto [available,total]=game.skillCounts();int amount=static_cast<int>(value);
   if(available<0 || total<0 || available>INT_MAX-amount || total>INT_MAX-amount)throw std::runtime_error("Skill-point counters are outside the supported range.");
   game.addSkills(amount);return "Skill points added through the native reward method.";
  }
  if(action=="lc_add_xp"){
   float xp=game.xp()+static_cast<float>(value);int level=game.level();bool valid=false;
   if(level<1 || level>100000 || !std::isfinite(xp) || xp<0)throw std::runtime_error("Invalid current XP/level.");
   for(int i=0;i<512;i++,level++){
    float required=game.required(level);
    if(!std::isfinite(required) || required<=0)throw std::runtime_error("Invalid level XP requirement.");
    if(xp<required){valid=true;break;}xp-=required;
   }
   if(!valid)throw std::runtime_error("Too many level-ups in one request. Use a smaller XP amount.");
   game.addXp(static_cast<float>(value));return "Shop XP added through the native level-up method.";
  }
  throw std::runtime_error("Unknown control.");
 }
};
}
