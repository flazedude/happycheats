#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <nlohmann/json.hpp>
#include <chrono>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <functional>
#include <string>

using Json = nlohmann::json;
std::filesystem::path bridge;
int (*Value)(int){};
void (*SetOnline)(int){};
void (*StartScript)(){};
int request{};
[[noreturn]] void Finish(int code, const char* text) {
    std::printf("%s\n", text); std::fflush(stdout);
    // The test ScriptMain stays alive in the production DLL. Terminate this
    // bounded fixture process; never FreeLibrary an ASI with active callbacks.
    ExitProcess(code);
}
void Check(bool value, const char* text) { if (!value) Finish(1, text); }
Json Status() {
    try { std::ifstream stream(bridge / "status.json"); Json result; stream >> result; return result; }
    catch (...) { return Json::object(); }
}
Json Await(const std::function<bool(const Json&)>& predicate) {
    ULONGLONG deadline = GetTickCount64() + 3000;
    while (GetTickCount64() < deadline) {
        auto status = Status();
        try { if (predicate(status)) return status; } catch (...) {}
        Sleep(10);
    }
    Finish(2, "FAIL accepted-host fixture timed out waiting for bridge state");
}
std::int64_t Now() { return std::chrono::duration_cast<std::chrono::seconds>(std::chrono::system_clock::now().time_since_epoch()).count(); }
Json Send(const std::string& session, const char* action, bool success, int enabled = -1) {
    char id[33]; std::snprintf(id, sizeof(id), "%032x", ++request);
    Json command{{"schema_version", 1}, {"session_id", session}, {"request_id", id}, {"action", action}, {"created_utc", Now()}};
    if (enabled >= 0) command["enabled"] = enabled != 0;
    auto temporary = bridge / "test-command.tmp";
    { std::ofstream stream(temporary); stream << command.dump(); }
    Check(MoveFileExW(temporary.c_str(), (bridge / "command.json").c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH), "FAIL command file replacement");
    auto status = Await([&](const Json& value) { return value.value("last_command", std::string()) == id; });
    Check(status["last_success"] == success, "FAIL unexpected real ASI command ACK");
    return status;
}
DWORD WINAPI Run(LPVOID) { StartScript(); return 0; }
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 3;
    auto path = std::filesystem::absolute(argv[1]); bridge = path.parent_path() / "HappyCheatsGtaV";
    Check(!GetModuleHandleW(L"ScriptHookV.dll"), "FAIL dependency unexpectedly preloaded");
    HMODULE asi = LoadLibraryW(path.c_str());
    Check(asi != nullptr, "FAIL accepted-host ASI load");
    auto asiRegistered = reinterpret_cast<int(*)()>(GetProcAddress(asi, "HappyCheatsGtaVRegistered"));
    const auto initializationDeadline = GetTickCount64() + 3000;
    while (asiRegistered && !asiRegistered() && GetTickCount64() < initializationDeadline) Sleep(10);
    Check(asiRegistered && asiRegistered(), "FAIL deferred ASI initialization");
    HMODULE hook = GetModuleHandleW(L"ScriptHookV.dll");
    Check(hook != nullptr, "FAIL accepted-host independent dependency loading");
    auto registered = reinterpret_cast<int(*)()>(GetProcAddress(hook, "MockRegistered"));
    Value = reinterpret_cast<int(*)(int)>(GetProcAddress(hook, "MockValue"));
    SetOnline = reinterpret_cast<void(*)(int)>(GetProcAddress(hook, "MockSetOnline"));
    StartScript = reinterpret_cast<void(*)()>(GetProcAddress(hook, "MockStartScript"));
    Check(registered && Value && SetOnline && StartScript && registered() == 1, "FAIL real ASI script/keyboard registration");
    Check(Value(0) == 0 && Value(1) == 0, "FAIL natives invoked before the game script callback");
    auto initialSession = Status().value("session_id", std::string());
    HANDLE thread = CreateThread(nullptr, 0, Run, nullptr, 0, nullptr);
    Check(thread != nullptr, "FAIL fixture script thread"); CloseHandle(thread);
    auto status = Await([](const Json& value) { return value.value("ready", false); });
    auto session = status["session_id"].get<std::string>();
    Check(session.size() == 32 && session != initialSession && status["updated_utc"].is_number_integer(), "FAIL script-entry session/heartbeat");
    Send(session, "gta_health", true, 1); Check(Value(3) == 1, "FAIL native invincibility dispatch");
    Send(session, "gta_ammo", true, 1); Check(Value(4) == 1, "FAIL native ammo dispatch");
    Send(session, "gta_wanted", true, 1); Check(Value(5) > 0, "FAIL native wanted dispatch");
    Send(session, "gta_refill", true); Check(Value(6) == 200 && Value(7) == 100, "FAIL native refill dispatch");
    Send(session, "gta_repair", true); Check(Value(8) == 4, "FAIL native repair/float dispatch");
    Send(session, "gta_noon", true); Check(Value(9) == 12, "FAIL native noon dispatch");
    Send(session, "gta_clear_weather", true); Check(Value(10) == 1, "FAIL native weather/string dispatch");
    Send(session, "disable_all", true); Check(Value(3) == 0 && Value(4) == 0, "FAIL owned native cleanup");
    Check(Value(2) == 0, "FAIL native ABI hash/argument contract");
    SetOnline(1);
    status = Await([](const Json& value) { return value.value("online", false) && !value.value("ready", true); });
    int lockedCalls = Value(0), lockedMutations = Value(1);
    Send(session, "gta_noon", false);
    Send(session, "disable_all", false);
    SetOnline(0); Sleep(250);
    Check(Status().value("online", false), "FAIL online latch after network clears");
    Check(Value(0) == lockedCalls && Value(1) == lockedMutations, "FAIL native call after online lockout");
    Check(Value(2) == 0, "FAIL unexpected native ABI call");
    Finish(0, "PASS accepted-host deferred ASI registration, NativeGame ABI for seven features, JSON mailbox ACK, cleanup and permanent network lockout (fake game only)");
}
