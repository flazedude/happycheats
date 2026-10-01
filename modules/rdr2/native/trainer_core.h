#pragma once
#include <array>
#include <cstdint>
#include <iomanip>
#include <sstream>
#include <string>
#include <unordered_map>
#include <nlohmann/json.hpp>

namespace rdr2 {
using Json = nlohmann::json;
inline constexpr std::array<const char*, 7> Actions = {"rdr2_health", "rdr2_stamina", "rdr2_deadeye", "rdr2_ammo", "rdr2_horse_health", "rdr2_horse_stamina", "rdr2_clear_wanted"};
struct Snapshot {
    int player = 0, ped = 0, mount = 0;
    bool ready = false, online = false;
};
struct Backend {
    virtual ~Backend() = default;
    virtual bool exists(int entity) = 0;
    virtual void health(int ped) = 0;
    virtual void stamina(int player, int ped) = 0;
    virtual void deadeye(int player, int ped) = 0;
    virtual void ammo(int ped, bool enabled) = 0;
    virtual void horse_stamina(int ped) = 0;
    virtual void clear_wanted(int player) = 0;
};
struct Command { std::string id, action; bool enabled = false; std::int64_t created = 0; };
inline bool token(const std::string& value) {
    if(value.size() != 32) return false;
    for(char c : value) if(!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
    return true;
}
inline bool parse(const std::string& raw, const std::string& session, std::int64_t now, Command& command) {
    if(raw.empty() || raw.size() > 2048) return false;
    try {
        // The bridge is flat. Duplicate fields and every unknown field are rejected.
        std::unordered_map<std::string, bool> keys;
        bool duplicate = false;
        Json data = Json::parse(raw, [&](int, Json::parse_event_t event, Json& value) {
            if(event == Json::parse_event_t::key && !keys.emplace(value.get<std::string>(), true).second) duplicate = true;
            return true;
        });
        if(duplicate || !data.is_object() || data.size() != 6) return false;
        for(const auto& entry : data.items())
            if(entry.key() != "schema_version" && entry.key() != "session_id" && entry.key() != "request_id" && entry.key() != "action" && entry.key() != "enabled" && entry.key() != "created_utc") return false;
        if(!data.at("schema_version").is_number_integer() || data.at("schema_version") != 1 ||
           !data.at("session_id").is_string() || data.at("session_id") != session ||
           !data.at("request_id").is_string() || !data.at("action").is_string() ||
           !data.at("enabled").is_boolean() || !data.at("created_utc").is_number_integer()) return false;
        command.id = data.at("request_id").get<std::string>();
        command.action = data.at("action").get<std::string>();
        command.enabled = data.at("enabled").get<bool>();
        command.created = data.at("created_utc").get<std::int64_t>();
        if(!token(command.id) || command.created <= 0 || command.created < now - 8 || command.created > now + 2) return false;
        int action = -1;
        for(int i = 0; i < 7; ++i) if(command.action == Actions[i]) action = i;
        return (action >= 0 || command.action == "disable_all") && (!(action == 6 || command.action == "disable_all") || !command.enabled);
    } catch(...) { return false; }
}

class Trainer {
    Backend& backend;
    Snapshot current{};
    bool initialized = false;
    std::array<bool, 6> flags{};
    std::unordered_map<std::string, std::int64_t> seen;
    std::uint64_t boot, generation = 0;
    std::string session, lastCommand, lastMessage;
    bool lastSuccess = false;
    int deferredAmmoCleanup = 0;
    void rotate() {
        std::ostringstream value;
        value << std::hex << std::setfill('0') << std::setw(16) << boot << std::setw(16) << ++generation;
        session = value.str(); seen.clear(); lastCommand.clear(); lastMessage.clear(); lastSuccess = false;
    }
    void cleanup(bool permitWrites) {
        if(permitWrites && deferredAmmoCleanup) {
            if(backend.exists(deferredAmmoCleanup)) backend.ammo(deferredAmmoCleanup, false);
            deferredAmmoCleanup = 0;
        }
        if(permitWrites && flags[3] && current.ped && backend.exists(current.ped)) backend.ammo(current.ped, false);
        else if(!permitWrites && flags[3]) deferredAmmoCleanup = current.ped;
        flags.fill(false);
    }
public:
    Trainer(Backend& binding, std::uint64_t bootNonce) : backend(binding), boot(bootNonce) { rotate(); }
    const std::string& session_id() const { return session; }
    void observe(Snapshot next) {
        next.ready = next.ready && !next.online && next.ped != 0;
        const bool transition = !initialized || next.player != current.player || next.ped != current.ped || next.ready != current.ready || next.online != current.online;
        if(transition) { cleanup(!next.online); rotate(); }
        else if(next.mount != current.mount) { flags[4] = flags[5] = false; }
        current = next; initialized = true;
    }
    bool available(int index) const { return current.ready && !current.online && (index < 4 || index == 6 || current.mount != 0); }
    bool consume(const std::string& raw, std::int64_t now) {
        Command command;
        if(!parse(raw, session, now, command)) return false;
        for(auto item = seen.begin(); item != seen.end();) {
            if(item->second < now - 8) item = seen.erase(item); else ++item;
        }
        if(seen.count(command.id) || seen.size() >= 2048) return false;
        seen.emplace(command.id, command.created);
        lastCommand = command.id; lastSuccess = false;
        if(command.action == "disable_all") { cleanup(!current.online); lastSuccess = true; lastMessage = "All trainer controls disabled."; return true; }
        int index = 0; while(command.action != Actions[index]) ++index;
        if(index < 6 && !command.enabled) {
            if(index == 3 && flags[index] && !current.online && current.ped && backend.exists(current.ped)) backend.ammo(current.ped, false);
            flags[index] = false; lastSuccess = true; lastMessage = "Control disabled."; return true;
        }
        if(!available(index)) { lastMessage = current.online ? "Red Dead Online is not supported. Use Story Mode." : "Control unavailable: load a living Story Mode player or mount a horse."; return true; }
        if(index == 6) { backend.clear_wanted(current.player); lastMessage = "Wanted score and current pursuit cleared; bounty is unchanged."; }
        else { flags[index] = true; lastMessage = "Control enabled for this Story Mode session."; }
        lastSuccess = true; return true;
    }
    void tick() {
        if(!current.ready || current.online) return;
        if(flags[0]) backend.health(current.ped);
        if(flags[1]) backend.stamina(current.player, current.ped);
        if(flags[2]) backend.deadeye(current.player, current.ped);
        if(flags[3]) backend.ammo(current.ped, true);
        if(current.mount) {
            if(flags[4]) backend.health(current.mount);
            if(flags[5]) backend.horse_stamina(current.mount);
        }
    }
    void shutdown() { cleanup(!current.online); current.ready = false; rotate(); }
    void stop_after_error(Snapshot freshState) { observe(freshState); shutdown(); }
    Json status(std::int64_t now, unsigned long pid) const {
        Json enabled = Json::object(), availability = Json::object();
        for(int i = 0; i < 7; ++i) { availability[Actions[i]] = available(i); if(i < 6) enabled[Actions[i]] = flags[i]; }
        return {{"schema_version",1},{"version","0.1.0"},{"process_id",pid},{"session_id",session},{"updated_utc",now},
            {"ready",current.ready},{"online",current.online},{"enabled",enabled},{"available",availability},
            {"message",current.online ? "Red Dead Online is not supported. Use Story Mode." : current.ready ? "Story Mode trainer ready. Controls reset on death or player changes." : "Load a living player in Story Mode. ScriptHookRDR2 must support this game build."},
            {"last_command",lastCommand},{"last_success",lastSuccess},{"last_message",lastMessage}};
    }
};
}
