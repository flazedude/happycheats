#include "../trainer_core.h"
#include <cstdlib>
#include <iostream>
#include <stdexcept>
#include <vector>

int assertions = 0;
void check(bool passed, const char* text) { ++assertions; if(!passed) { std::cerr << "FAIL " << text << '\n'; std::exit(1); } }
struct Mock final : rdr2::Backend {
    std::vector<std::string> calls;
    bool present = true;
    bool failHealth = false;
    bool exists(int) override { return present; }
    void health(int ped) override { if(failHealth) throw std::runtime_error("fixture backend failure"); calls.push_back("health:" + std::to_string(ped)); }
    void stamina(int player, int ped) override { calls.push_back("stamina:" + std::to_string(player) + ":" + std::to_string(ped)); }
    void deadeye(int player, int ped) override { calls.push_back("deadeye:" + std::to_string(player) + ":" + std::to_string(ped)); }
    void ammo(int ped, bool on) override { calls.push_back("ammo:" + std::to_string(ped) + (on ? ":1" : ":0")); }
    void horse_stamina(int ped) override { calls.push_back("horse:" + std::to_string(ped)); }
    void clear_wanted(int player) override { calls.push_back("wanted:" + std::to_string(player)); }
};
std::string id(unsigned value) { std::ostringstream result; result << std::hex << std::setw(32) << std::setfill('0') << value; return result.str(); }
rdr2::Json data(const rdr2::Trainer& trainer, const std::string& action, bool enabled = true, unsigned request = 1, int time = 100) {
    return {{"schema_version",1},{"session_id",trainer.session_id()},{"request_id",id(request)},{"action",action},{"enabled",enabled},{"created_utc",time}};
}
bool all_off(const rdr2::Json& status) { for(const auto& flag : status.at("enabled")) if(flag.get<bool>()) return false; return true; }
int main() {
    Mock native;
    rdr2::Trainer trainer(native, 0x12345);
    check(rdr2::token(trainer.session_id()), "bounded hexadecimal session identity");
    check(all_off(trainer.status(100,99)), "startup flags disabled");
    check(!trainer.status(100,99)["ready"].get<bool>(), "startup not ready");
    trainer.observe({2,101,201,true,false});
    const auto session = trainer.session_id();
    unsigned sequence = 1;
    for(int i = 0; i < 6; ++i) {
        check(trainer.consume(data(trainer,rdr2::Actions[i],true,sequence++).dump(),100), "valid toggle acknowledged");
        check(trainer.status(100,99)["last_success"].get<bool>(), "valid toggle succeeds");
    }
    trainer.tick();
    check(native.calls == std::vector<std::string>{"health:101","stamina:2:101","deadeye:2:101","ammo:101:1","health:201","horse:201"}, "each native binding receives correct player or horse");
    auto wanted = data(trainer,"rdr2_clear_wanted",false,sequence++).dump();
    check(trainer.consume(wanted,100), "one-shot accepted");
    check(native.calls.back() == "wanted:2", "wanted applied to player");
    const auto callCount = native.calls.size();
    check(!trainer.consume(wanted,100), "duplicate request ignored");
    check(native.calls.size() == callCount, "duplicate never dispatches");
    check(trainer.status(100,99)["last_command"] == id(sequence-1), "acknowledgement echoes request identity");
    check(trainer.status(100,99)["process_id"] == 99, "status includes producing process");
    check(trainer.status(100,99)["updated_utc"] == 100, "status heartbeat uses supplied clock");
    check(trainer.consume(data(trainer,"rdr2_ammo",false,sequence++).dump(),100), "ammo off accepted");
    check(native.calls.back() == "ammo:101:0", "ammo off restores native flag");
    trainer.consume(data(trainer,"rdr2_ammo",true,sequence++).dump(),100);
    trainer.observe({2,101,202,true,false});
    check(!trainer.status(100,99)["enabled"]["rdr2_horse_health"].get<bool>() && !trainer.status(100,99)["enabled"]["rdr2_horse_stamina"].get<bool>(), "mount changes reset horse controls");
    check(trainer.session_id() == session, "mount transition preserves player session");
    trainer.observe({2,101,0,false,false});
    check(trainer.session_id() != session && all_off(trainer.status(100,99)), "death resets all flags and rotates session");
    check(native.calls.back() == "ammo:101:0", "death restores ammo flag");
    check(!trainer.consume(wanted,100), "previous session command rejected");
    check(trainer.status(100,99)["last_command"] == "", "transition clears acknowledgement");
    trainer.consume(data(trainer,"rdr2_health",true,sequence++).dump(),100);
    check(!trainer.status(100,99)["last_success"].get<bool>(), "dead player cannot enable");
    native.calls.clear(); trainer.tick(); check(native.calls.empty(), "dead player never mutated");
    trainer.observe({2,102,0,true,false});
    trainer.consume(data(trainer,"rdr2_horse_health",true,sequence++).dump(),100);
    check(!trainer.status(100,99)["last_success"].get<bool>(), "unmounted horse request rejected");
    trainer.consume(data(trainer,"rdr2_ammo",true,sequence++).dump(),100); trainer.tick();
    native.calls.clear(); trainer.observe({2,102,0,true,true});
    trainer.consume(data(trainer,"rdr2_health",true,sequence++).dump(),100);
    trainer.consume(data(trainer,"rdr2_clear_wanted",false,sequence++).dump(),100); trainer.tick();
    check(native.calls.empty(), "Online gate performs no native mutations including cleanup");
    check(all_off(trainer.status(100,99)) && !trainer.status(100,99)["ready"].get<bool>(), "Online clears flags and readiness");
    check(!trainer.status(100,99)["last_success"].get<bool>(), "Online request receives negative acknowledgement");
    trainer.observe({3,105,205,true,false});
    check(all_off(trainer.status(100,99)), "return to Story does not reenable controls");
    check(native.calls.back() == "ammo:102:0", "Online transition defers ammo cleanup until Story is available");
    trainer.consume(data(trainer,"rdr2_ammo",true,sequence++).dump(),100);
    trainer.consume(data(trainer,"rdr2_health",true,sequence++).dump(),100);
    trainer.consume(data(trainer,"disable_all",false,sequence++).dump(),100);
    check(all_off(trainer.status(100,99)) && native.calls.back() == "ammo:105:0", "disable all cleans persistent ammo and all toggles");
    trainer.consume(data(trainer,"rdr2_ammo",true,sequence++).dump(),100);
    native.present = false; native.calls.clear(); trainer.observe({4,106,0,true,false});
    check(native.calls.empty(), "missing old entity never mutated during cleanup");
    native.present = true; trainer.consume(data(trainer,"rdr2_ammo",true,sequence++).dump(),100); trainer.shutdown();
    check(all_off(trainer.status(100,99)) && native.calls.back() == "ammo:106:0", "shutdown cleans native flags");
    trainer.observe({4,106,0,true,false});
    trainer.consume(data(trainer,"rdr2_ammo",true,sequence++).dump(),100); trainer.tick();
    trainer.consume(data(trainer,"rdr2_health",true,sequence++).dump(),100); native.failHealth = true;
    bool caught = false;
    try { trainer.tick(); } catch(...) { caught = true; trainer.stop_after_error({4,106,0,true,false}); }
    check(caught && all_off(trainer.status(100,99)) && native.calls.back() == "ammo:106:0", "script-fiber error path cleans active ammo before parking");
    check(!trainer.status(100,99)["ready"].get<bool>(), "runtime error resets readiness");
    native.failHealth = false;

    rdr2::Command parsed;
    auto valid = data(trainer,"rdr2_health",true,1000);
    check(rdr2::parse(valid.dump(),trainer.session_id(),108,parsed), "eight-second age boundary accepted");
    check(!rdr2::parse(valid.dump(),trainer.session_id(),109,parsed), "expired command rejected");
    check(rdr2::parse(valid.dump(),trainer.session_id(),98,parsed), "two-second future boundary accepted");
    check(!rdr2::parse(valid.dump(),trainer.session_id(),97,parsed), "future command rejected");
    for(const char* name : {"schema_version","session_id","request_id","action","enabled","created_utc"}) {
        auto missing = valid; missing.erase(name);
        check(!rdr2::parse(missing.dump(),trainer.session_id(),100,parsed), "required field rejected if missing");
        auto wrongType = valid; wrongType[name] = nullptr;
        check(!rdr2::parse(wrongType.dump(),trainer.session_id(),100,parsed), "required field rejects invalid type");
    }
    for(const auto& invalid : {"RDR2_HEALTH","disable_all()","unknown",""}) {
        auto command = valid; command["action"] = invalid;
        check(!rdr2::parse(command.dump(),trainer.session_id(),100,parsed), "unknown action rejected");
    }
    auto extra = valid; extra["script"] = "ignored";
    check(!rdr2::parse(extra.dump(),trainer.session_id(),100,parsed), "unknown field rejected");
    auto wrongId = valid; wrongId["request_id"] = std::string(32,'F');
    check(!rdr2::parse(wrongId.dump(),trainer.session_id(),100,parsed), "noncanonical identity rejected");
    auto floatTime = valid; floatTime["created_utc"] = 100.0;
    check(!rdr2::parse(floatTime.dump(),trainer.session_id(),100,parsed), "floating timestamp rejected");
    check(!rdr2::parse(std::string(2049,' '),trainer.session_id(),100,parsed), "oversized input rejected");
    auto duplicate = valid.dump(); duplicate.insert(1,"\"enabled\":true,");
    check(!rdr2::parse(duplicate,trainer.session_id(),100,parsed), "duplicate JSON key rejected");
    auto once = valid; once["action"] = "rdr2_clear_wanted";
    check(!rdr2::parse(once.dump(),trainer.session_id(),100,parsed), "one-shot cannot latch on");
    check(!rdr2::parse("{",trainer.session_id(),100,parsed), "malformed JSON rejected");
    check(!rdr2::parse(valid.dump()+"{}",trainer.session_id(),100,parsed), "trailing data rejected");
    std::cout << "PASS " << assertions << " RDR2 offline logic/protocol checks (no game launch)\n";
}
