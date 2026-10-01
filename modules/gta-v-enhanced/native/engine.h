#pragma once
#include <nlohmann/json.hpp>
#include <array>
#include <cmath>
#include <cstdint>
#include <deque>
#include <string>

// Original HappyCheats policy and dispatch layer. The same engine runs in the ASI
// and in the fake-game regression fixture; no GTA/ScriptHook code is included.
namespace happygta {
using Json = nlohmann::json;
inline constexpr std::array<const char*, 7> Features{
    "gta_health", "gta_ammo", "gta_wanted", "gta_refill", "gta_repair", "gta_noon", "gta_clear_weather"};
inline bool HexId(const std::string& value) {
    if (value.size() != 32) return false;
    for (char c : value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
    return true;
}
inline int Feature(const std::string& action) {
    for (int i = 0; i < static_cast<int>(Features.size()); ++i) if (action == Features[i]) return i;
    return -1;
}
struct Frame {
    bool online{}, ready{}, paused{};
    int player{-1}, ped{}, vehicle{};
    std::uint32_t model{};
    std::string message{"Waiting for a loaded Story Mode character."};
};
struct Game {
    virtual ~Game() = default;
    virtual Frame Read() = 0;
    virtual bool IsPed(int ped, std::uint32_t model) = 0;
    virtual bool Invincible(int player) = 0;
    virtual void Health(int player, bool enabled) = 0;
    virtual void Ammo(int ped, bool enabled) = 0;
    virtual void Wanted(int player) = 0;
    virtual void Refill(int ped) = 0;
    virtual void Repair(int vehicle) = 0;
    virtual void Noon() = 0;
    virtual void ClearWeather() = 0;
};
class Engine {
    Game& game_;
    std::string session_;
    std::uint32_t pid_;
    Frame frame_{};
    std::array<bool, 3> enabled_{};
    bool originalInvincible_{};
    bool healthOwned_{}, ammoOwned_{};
    int healthPlayer_{-1}, ammoPed_{};
    std::uint32_t ammoModel_{};
    bool onlineLatched_{};
    std::deque<std::pair<std::string, double>> seen_;
    std::string lastCommand_, lastMessage_{"No commands received."};
    bool lastSuccess_{true};

    void ClearInternal() { enabled_.fill(false); originalInvincible_ = false; healthOwned_ = ammoOwned_ = false; }
    bool SameCharacter(const Frame& a, const Frame& b) const {
        return a.ped != 0 && a.ped == b.ped && a.player == b.player && a.model == b.model;
    }
    void Disable(const Frame& current) {
        if (current.online) { ClearInternal(); return; }
        // Invincibility belongs to the player index and must be restored across a
        // character swap. Defer restoration if that player is temporarily absent.
        if (healthOwned_ && current.player == healthPlayer_) {
            game_.Health(healthPlayer_, originalInvincible_); healthOwned_ = false;
        }
        // Ammo belongs to the old ped. Verify its existence and original model
        // before touching it; never redirect cleanup to the new/reused character.
        if (ammoOwned_) {
            if (game_.IsPed(ammoPed_, ammoModel_)) game_.Ammo(ammoPed_, false);
            ammoOwned_ = false;
        }
        enabled_.fill(false);
    }
    bool Available(int index) const {
        return index >= 0 && frame_.ready && !frame_.online && !frame_.paused && (index != 4 || frame_.vehicle != 0);
    }
    bool Dispatch(const std::string& action, bool enabled) {
        // Refresh immediately before every command, not only once per frame.
        Refresh();
        int index = Feature(action);
        if (frame_.online) {
            lastMessage_ = "GTA Online is locked out, including invite-only sessions.";
            return false;
        }
        if (!frame_.ready || frame_.paused) {
            if (action == "disable_all" || (index >= 0 && index < 3 && !enabled)) {
                // Refresh has already removed owned toggles. Acknowledge cleanup
                // without writing game state while the character is unavailable.
                lastMessage_ = "Toggles already disabled while Story Mode is unavailable."; return true;
            }
            lastMessage_ = "Load a living Story Mode character and close the pause menu."; return false;
        }
        if (action == "disable_all") { Disable(frame_); lastMessage_ = "All HappyCheats toggles disabled."; return true; }
        if (!Available(index)) { lastMessage_ = index == 4 ? "Enter a vehicle first." : "This feature is unavailable."; return false; }
        switch (index) {
            case 0:
                if (enabled && !enabled_[0]) {
                    if (healthOwned_ && healthPlayer_ != frame_.player) { lastMessage_ = "Waiting to restore the previous player state."; return false; }
                    originalInvincible_ = game_.Invincible(frame_.player); healthPlayer_ = frame_.player; healthOwned_ = true;
                }
                if (enabled) game_.Health(frame_.player, true);
                else if (healthOwned_ && frame_.player == healthPlayer_) {
                    game_.Health(healthPlayer_, originalInvincible_); healthOwned_ = false;
                }
                enabled_[0] = enabled;
                break;
            case 1:
                if (enabled) { ammoPed_ = frame_.ped; ammoModel_ = frame_.model; ammoOwned_ = true; }
                if (enabled || ammoOwned_) game_.Ammo(frame_.ped, enabled);
                if (!enabled) ammoOwned_ = false;
                enabled_[1] = enabled;
                break;
            case 2: enabled_[2] = enabled; if (enabled) game_.Wanted(frame_.player); break;
            case 3: game_.Refill(frame_.ped); break;
            case 4: game_.Repair(frame_.vehicle); break;
            case 5: game_.Noon(); break;
            case 6: game_.ClearWeather(); break;
            default: return false;
        }
        lastMessage_ = index < 3 ? (enabled ? "Toggle enabled." : "Toggle disabled.") : "Story Mode action applied.";
        return true;
    }
public:
    Engine(Game& game, std::string session, std::uint32_t pid) : game_(game), session_(std::move(session)), pid_(pid) {}
    ~Engine() { try { Refresh(); Disable(frame_); } catch (...) {} }
    void Refresh() {
        if (onlineLatched_) return;
        Frame next = game_.Read();
        if (next.online) {
            onlineLatched_ = true;
            next.ready = false;
            next.message = "GTA Online detected. Trainer locked until the game restarts, including invite-only sessions.";
        }
        if (next.online || !next.ready || next.paused || !SameCharacter(frame_, next)) Disable(next);
        else if (!enabled_[0] && healthOwned_ && next.player == healthPlayer_) {
            game_.Health(healthPlayer_, originalInvincible_); healthOwned_ = false;
        }
        frame_ = std::move(next);
    }
    void Tick() {
        Refresh();
        if (!frame_.ready || frame_.online || frame_.paused) return;
        if (enabled_[0]) game_.Health(frame_.player, true);
        if (enabled_[1]) game_.Ammo(frame_.ped, true);
        if (enabled_[2]) game_.Wanted(frame_.player);
    }
    bool Local(const std::string& action) {
        int index = Feature(action);
        return Dispatch(action, index >= 0 && index < 3 ? !enabled_[index] : false);
    }
    bool Submit(const std::string& text, double now) {
        // A bounded read is also enforced before this parser by the Win32 bridge.
        if (text.size() > 4096) return false;
        try {
            const Json command = Json::parse(text);
            if (!command.is_object()) return false;
            if (!command.contains("request_id") || !command["request_id"].is_string()) return false;
            std::string id = command["request_id"].get<std::string>();
            if (!HexId(id)) return false;
            lastCommand_ = id; lastSuccess_ = false;
            const auto fail = [&](const char* message) { lastMessage_ = message; return false; };
            if (!command.contains("schema_version") || !command["schema_version"].is_number_integer() || command["schema_version"] != 1)
                return fail("Unsupported command schema.");
            if (!command.contains("session_id") || !command["session_id"].is_string() || !HexId(command["session_id"].get<std::string>()) || command["session_id"] != session_)
                return fail("The command belongs to another game session.");
            if (!command.contains("created_utc") || !command["created_utc"].is_number()) return fail("Missing command timestamp.");
            double created = command["created_utc"].get<double>();
            if (!std::isfinite(created) || !std::isfinite(now) || now - created > 8 || created - now > 2)
                return fail("The command expired or has a future timestamp.");
            while (!seen_.empty() && now - seen_.front().second > 12) seen_.pop_front();
            for (const auto& prior : seen_) if (prior.first == id) return fail("Duplicate request rejected.");
            if (seen_.size() >= 1024) return fail("Too many recent requests.");
            seen_.emplace_back(id, now);
            if (!command.contains("action") || !command["action"].is_string()) return fail("Missing action.");
            std::string action = command["action"].get<std::string>();
            int index = Feature(action);
            if (index < 0 && action != "disable_all") return fail("Unknown action.");
            bool toggle = index >= 0 && index < 3;
            if (toggle && (!command.contains("enabled") || !command["enabled"].is_boolean())) return fail("Toggle requires a boolean enabled value.");
            if (!toggle && command.contains("enabled")) return fail("Actions do not accept an enabled value.");
            if (command.size() != (toggle ? 6u : 5u)) return fail("Unexpected command fields.");
            lastSuccess_ = Dispatch(action, toggle && command["enabled"].get<bool>());
            return lastSuccess_;
        } catch (...) { lastSuccess_ = false; lastMessage_ = "Malformed command rejected."; return false; }
    }
    Json Status(double now) const {
        Json enabled = Json::object(), available = Json::object();
        for (int i = 0; i < static_cast<int>(Features.size()); ++i) {
            enabled[Features[i]] = i < 3 && enabled_[i]; available[Features[i]] = Available(i);
        }
        return {{"schema_version", 1}, {"version", "0.2.0"}, {"session_id", session_}, {"process_id", pid_},
            {"updated_utc", static_cast<std::int64_t>(now)}, {"ready", frame_.ready && !frame_.online && !frame_.paused}, {"online", frame_.online},
            {"enabled", enabled}, {"available", available}, {"values", {{"gta_online", frame_.online}}},
            {"last_command", lastCommand_}, {"last_success", lastSuccess_}, {"last_message", lastMessage_}, {"message", frame_.message}};
    }
    bool Online() const { return frame_.online; }
    bool Ready() const { return frame_.ready && !frame_.paused && !frame_.online; }
    bool Enabled(int i) const { return i >= 0 && i < 3 && enabled_[i]; }
};
}
