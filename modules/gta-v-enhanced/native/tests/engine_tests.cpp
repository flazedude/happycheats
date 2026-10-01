#include "../engine.h"
#include <cstdio>
#include <stdexcept>
#include <vector>
#include <map>

using namespace happygta;
void Check(bool result, const char* name) { if (!result) throw std::runtime_error(name); }
struct FakeGame final : Game {
    Frame frame{false, true, false, 0, 41, 0, 101, "Fixture Story Mode ready"};
    bool invincible{};
    std::map<int, std::uint32_t> peds{{41, 101}, {42, 101}};
    std::map<int, bool> ammo;
    std::map<int, bool> otherPlayers;
    int reads{};
    std::vector<std::string> writes;
    Frame Read() override { ++reads; return frame; }
    bool IsPed(int ped, std::uint32_t model) override { auto found = peds.find(ped); return found != peds.end() && found->second == model; }
    bool Invincible(int player) override { return player == 0 ? invincible : otherPlayers[player]; }
    void Health(int player, bool value) override { if (player == 0) invincible = value; else otherPlayers[player] = value; writes.push_back(value ? "health:on" : "health:off"); }
    void Ammo(int ped, bool value) override { ammo[ped] = value; writes.push_back(value ? "ammo:on" : "ammo:off"); }
    void Wanted(int) override { writes.push_back("wanted"); }
    void Refill(int) override { writes.push_back("refill"); }
    void Repair(int) override { writes.push_back("repair"); }
    void Noon() override { writes.push_back("noon"); }
    void ClearWeather() override { writes.push_back("weather"); }
};
const std::string Session(32, 'a');
Json Command(int id, const std::string& action, double created = 1000) {
    char buffer[33]; std::snprintf(buffer, sizeof(buffer), "%032x", id);
    return {{"schema_version", 1}, {"session_id", Session}, {"request_id", buffer}, {"action", action}, {"created_utc", created}};
}
Json Toggle(int id, const std::string& action, bool enabled, double created = 1000) {
    Json result = Command(id, action, created); result["enabled"] = enabled; return result;
}
void Protocol() {
    FakeGame game; Engine engine(game, Session, 123); engine.Tick();
    auto status = engine.Status(1000.1234);
    Check(status["updated_utc"].is_number_integer() && status["updated_utc"] == 1000, "integer heartbeat protocol");
    Check(status["process_id"] == 123 && status["session_id"] == Session, "session and PID");
    Check(status["available"].size() == 7 && !status["available"]["gta_repair"], "vehicle availability");
    for (auto& flag : status["enabled"]) Check(!flag.get<bool>(), "defaults off");
    Check(!engine.Submit("[]", 1000), "array rejected");
    Check(!engine.Submit("{", 1000), "malformed JSON rejected");
    Check(!engine.Submit(std::string(4097, 'x'), 1000), "oversize rejected");
    Json command = Command(1, "gta_noon"); command["schema_version"] = "1";
    Check(!engine.Submit(command.dump(), 1000), "string schema rejected");
    command = Command(2, "gta_noon"); command["session_id"] = std::string(32, 'b');
    Check(!engine.Submit(command.dump(), 1000), "wrong session rejected");
    command = Command(3, "gta_noon"); command["request_id"] = std::string(32, 'z');
    Check(!engine.Submit(command.dump(), 1000), "nonhex ID rejected");
    command = Command(4, "gta_noon"); command.erase("created_utc");
    Check(!engine.Submit(command.dump(), 1000), "missing timestamp rejected");
    Check(!engine.Submit(Command(5, "gta_noon", 990).dump(), 1000), "expired rejected");
    Check(!engine.Submit(Command(6, "gta_noon", 1003).dump(), 1000), "future rejected");
    command = Command(7, "gta_noon"); command["enabled"] = false;
    Check(!engine.Submit(command.dump(), 1000), "action boolean rejected");
    command = Toggle(8, "gta_health", true); command["enabled"] = "true";
    Check(!engine.Submit(command.dump(), 1000), "nonboolean toggle rejected");
    Check(!engine.Submit(Command(9, "gta_health").dump(), 1000), "missing toggle boolean rejected");
    command = Command(10, "gta_noon"); command["extra"] = 1;
    Check(!engine.Submit(command.dump(), 1000), "unexpected keys rejected");
    Check(!engine.Submit(Command(11, "unknown").dump(), 1000), "unknown action rejected");
    Check(game.writes.empty(), "rejections produce no mutations");
    command = Command(12, "gta_noon");
    Check(engine.Submit(command.dump(), 1000), "valid noon");
    Check(game.writes == std::vector<std::string>{"noon"}, "real dispatch to backend");
    Check(!engine.Submit(command.dump(), 1000), "duplicate rejected");
    Check(game.writes.size() == 1, "duplicate not replayed");
    Check(!engine.Submit(Command(13, "gta_repair").dump(), 1000), "repair unavailable on foot");
    game.frame.vehicle = 98;
    Check(engine.Submit(Command(14, "gta_repair").dump(), 1000), "repair when in vehicle");
    Check(engine.Submit(Command(15, "gta_refill").dump(), 1000), "refill dispatch");
    Check(engine.Submit(Command(16, "gta_clear_weather").dump(), 1000), "weather dispatch");
    Check(engine.Status(1000)["last_success"] == true && engine.Status(1000)["last_command"] == Command(16, "gta_clear_weather")["request_id"], "ACK identity");
    Engine restarted(game, std::string(32, 'c'), 123);
    Check(!restarted.Submit(Command(17, "gta_noon").dump(), 1000), "script restart invalidates old session commands");
}
void StateAndCleanup() {
    FakeGame game; Engine engine(game, Session, 123); engine.Tick();
    Check(engine.Submit(Toggle(1, "gta_health", true).dump(), 1000), "enable health");
    Check(engine.Submit(Toggle(2, "gta_ammo", true).dump(), 1000), "enable ammo");
    Check(engine.Submit(Toggle(3, "gta_wanted", true).dump(), 1000), "enable wanted");
    game.writes.clear(); engine.Tick();
    Check(game.writes == std::vector<std::string>{"health:on", "ammo:on", "wanted"}, "continuous controls");
    game.frame.paused = true; game.writes.clear(); engine.Tick();
    Check(game.writes == std::vector<std::string>{"health:off", "ammo:off"}, "pause restores owned flags");
    for (int i = 0; i < 3; ++i) Check(!engine.Enabled(i), "pause resets state");
    game.writes.clear();
    Check(engine.Submit(Command(4, "disable_all").dump(), 1000), "offline cleanup allowed when paused");
    Check(engine.Submit(Toggle(5, "gta_health", false).dump(), 1000), "offline toggle cleanup when paused");
    Check(!engine.Submit(Toggle(6, "gta_health", true).dump(), 1000), "paused enable rejected");
    Check(game.writes.empty(), "paused cleanup is no-op after reset");
    game.frame.paused = false; game.invincible = true;
    Check(engine.Submit(Toggle(7, "gta_health", true).dump(), 1000), "existing invincible enable");
    Check(engine.Submit(Toggle(8, "gta_health", false).dump(), 1000), "existing invincible disable");
    Check(game.invincible, "preserve original invincibility");
    Check(engine.Submit(Toggle(9, "gta_ammo", true).dump(), 1000), "ammo for switch");
    game.invincible = false;
    Check(engine.Submit(Toggle(11, "gta_health", true).dump(), 1000), "health for switch");
    game.frame.ped = 42; game.writes.clear(); engine.Tick();
    Check(!engine.Enabled(1) && !engine.Enabled(0) && !game.invincible && !game.ammo[41] && !game.ammo[42], "changed character restores player invincibility and old ped ammo");
    Check(engine.Submit(Toggle(10, "gta_ammo", true).dump(), 1000), "new character enable");
    game.frame.ready = false; game.writes.clear(); engine.Tick();
    Check(!engine.Enabled(1) && game.writes == std::vector<std::string>{"ammo:off"}, "death/loading cleans current character");
    game.writes.clear(); engine.Tick();
    Check(game.writes.empty(), "no unconditional cleanup writes");
    game.frame.ready = true;
    Check(engine.Submit(Toggle(12, "gta_ammo", true).dump(), 1000), "ammo before recycled handle");
    game.peds[42] = 202; game.frame.ped = 41; game.writes.clear(); engine.Tick();
    Check(game.writes.empty() && game.ammo[42], "old ped with changed model never receives cleanup writes");
}
void NetworkLockout() {
    FakeGame game; Engine engine(game, Session, 123); engine.Tick();
    Check(engine.Submit(Toggle(1, "gta_health", true).dump(), 1000), "offline enable before network");
    game.frame.online = true; game.writes.clear();
    Check(!engine.Submit(Command(2, "gta_noon").dump(), 1000), "network checked again at dispatch");
    Check(game.writes.empty(), "zero network mutations including cleanup");
    Check(!engine.Enabled(0) && engine.Online() && !engine.Ready(), "network resets/latches");
    Check(!engine.Submit(Command(3, "disable_all").dump(), 1000), "network disable all rejected");
    Check(!engine.Submit(Toggle(4, "gta_health", false).dump(), 1000), "network toggle off rejected");
    int reads = game.reads; game.frame.online = false;
    engine.Tick();
    Check(!engine.Local("gta_noon") && game.reads == reads, "latched network performs no more natives");
    auto status = engine.Status(1000);
    for (auto& flag : status["available"]) Check(!flag.get<bool>(), "network availability all off");
    Check(status["online"] == true && status["values"]["gta_online"] == true, "network visible in status");
}
void DeferredPlayerCleanup() {
    FakeGame game; Engine engine(game, Session, 123); engine.Tick();
    Check(engine.Submit(Toggle(1, "gta_health", true).dump(), 1000), "deferred setup");
    game.frame.player = 1; game.frame.ped = 42;
    game.writes.clear(); engine.Tick();
    Check(!engine.Enabled(0) && game.invincible && !game.otherPlayers[1] && game.writes.empty(), "defer restoration for missing original player");
    Check(engine.Submit(Toggle(2, "gta_health", false).dump(), 1000), "acknowledge off for absent owner");
    Check(game.invincible && !game.otherPlayers[1] && game.writes.empty(), "off never targets a different player or discards pending restore");
    game.frame.player = 0; game.frame.ped = 41;
    engine.Tick();
    Check(!game.invincible && game.writes == std::vector<std::string>{"health:off"}, "restore original player after return");
}
int main() {
    try { Protocol(); StateAndCleanup(); NetworkLockout(); DeferredPlayerCleanup(); std::puts("PASS GTA command validation, actual feature dispatch, cleanup ownership, integer heartbeat, session restart and permanent network lockout"); return 0; }
    catch (const std::exception& error) { std::fprintf(stderr, "FAIL: %s\n", error.what()); return 1; }
}
