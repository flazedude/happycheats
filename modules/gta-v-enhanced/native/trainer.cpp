// HappyCheats GTA V Enhanced Story Mode ASI, GPL-2.0.
// Original trainer plus the attributed Enhanced source runtime. See LICENSE and PROVENANCE.md.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <bcrypt.h>
#include <array>
#include <atomic>
#include <chrono>
#include <filesystem>
#include <memory>
#include <mutex>
#include <string>
#include <vector>
#include "engine.h"
#include "script_hook_abi.h"
#include "source_runtime.h"

namespace {
using namespace happygta;
HMODULE g_module{};
ScriptHook g_scriptHook;
std::atomic<bool> g_registered{};
bool g_sourceMode{};
struct NativeBridge {
    template<class R = void, class... A> R Native(std::uint64_t hash, A... args) {
        if (g_sourceMode) return source::Native<R>(hash, args...);
        return g_scriptHook.Native<R>(hash, args...);
    }
} g_hook;
bool g_onlineLatched{};
std::wstring g_bridge;
std::string g_session;
std::string g_initializationError;
std::string g_gameVersion;
std::array<std::atomic<bool>, 256> g_keys{};
bool g_menu{};
int g_selection{};
std::mutex g_statusMutex;
double Now() { return std::chrono::duration<double>(std::chrono::system_clock::now().time_since_epoch()).count(); }

std::wstring ModulePath(HMODULE module) {
    std::vector<wchar_t> buffer(32768);
    DWORD length = GetModuleFileNameW(module, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (!length || length >= buffer.size()) return {};
    return {buffer.data(), length};
}
std::string NewSession() {
    std::array<UCHAR, 16> bytes{};
    if (BCryptGenRandom(nullptr, bytes.data(), static_cast<ULONG>(bytes.size()), BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0) return {};
    static const char digits[] = "0123456789abcdef";
    std::string value; value.reserve(32);
    for (auto b : bytes) { value += digits[b >> 4]; value += digits[b & 15]; }
    return value;
}
bool PlainDirectory(const std::wstring& path) {
    DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) && !(attributes & FILE_ATTRIBUTE_REPARSE_POINT);
}
bool InitBridge() {
    std::filesystem::path module = ModulePath(g_module);
    if (module.empty() || !PlainDirectory(module.parent_path().wstring())) return false;
    g_bridge = (module.parent_path() / L"HappyCheatsGtaV").wstring();
    if (!CreateDirectoryW(g_bridge.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS) return false;
    return PlainDirectory(g_bridge);
}
bool SafeExistingFile(const std::wstring& path) {
    DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes == INVALID_FILE_ATTRIBUTES || !(attributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY));
}
bool WriteStatus(const Json& status) {
    std::lock_guard<std::mutex> lock(g_statusMutex);
    if (g_bridge.empty() || !PlainDirectory(g_bridge)) return false;
    const auto target = g_bridge + L"\\status.json";
    const auto temporary = g_bridge + L"\\status-" + std::to_wstring(GetCurrentProcessId()) + L".tmp";
    if (!SafeExistingFile(target) || !SafeExistingFile(temporary)) return false;
    std::string text = status.dump();
    HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    BY_HANDLE_FILE_INFORMATION info{};
    bool safe = GetFileInformationByHandle(file, &info) && !(info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT);
    DWORD written{};
    bool result = safe && WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr) && written == text.size();
    if (result) result = FlushFileBuffers(file) != FALSE;
    CloseHandle(file);
    if (result && PlainDirectory(g_bridge) && SafeExistingFile(target))
        result = MoveFileExW(temporary.c_str(), target.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE;
    return result;
}
std::string ReadCommand() {
    if (g_bridge.empty() || !PlainDirectory(g_bridge)) return {};
    HANDLE file = CreateFileW((g_bridge + L"\\command.json").c_str(), GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
    if (file == INVALID_HANDLE_VALUE) return {};
    BY_HANDLE_FILE_INFORMATION info{};
    std::string text;
    if (GetFileInformationByHandle(file, &info) && !(info.dwFileAttributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY)) &&
        info.nFileSizeHigh == 0 && info.nFileSizeLow > 0 && info.nFileSizeLow <= 4096) {
        text.resize(info.nFileSizeLow);
        DWORD bytes{};
        if (!ReadFile(file, text.data(), static_cast<DWORD>(text.size()), &bytes, nullptr) || bytes != text.size()) text.clear();
    }
    CloseHandle(file);
    return text;
}
bool SupportedHost() {
    std::filesystem::path image = ModulePath(nullptr);
    if (image.empty() || _wcsicmp(image.filename().c_str(), L"GTA5_Enhanced.exe") != 0) {
        g_initializationError = "Unsupported host: only GTA5_Enhanced.exe is accepted."; return false;
    }
    DWORD ignored{};
    DWORD size = GetFileVersionInfoSizeW(image.c_str(), &ignored);
    if (!size || size > 1024 * 1024) { g_initializationError = "Cannot verify the game executable version."; return false; }
    std::vector<unsigned char> bytes(size);
    VS_FIXEDFILEINFO* info{}; UINT length{};
    if (!GetFileVersionInfoW(image.c_str(), 0, size, bytes.data()) ||
        !VerQueryValueW(bytes.data(), L"\\", reinterpret_cast<void**>(&info), &length) ||
        length < sizeof(VS_FIXEDFILEINFO) || info->dwSignature != 0xFEEF04BD) {
        g_initializationError = "Cannot verify the game executable version."; return false;
    }
    auto major = HIWORD(info->dwFileVersionMS), minor = LOWORD(info->dwFileVersionMS);
    auto build = HIWORD(info->dwFileVersionLS), revision = LOWORD(info->dwFileVersionLS);
    g_gameVersion = std::to_string(major) + "." + std::to_string(minor) + "." + std::to_string(build) + "." + std::to_string(revision);
    if (major != 1 || minor != 0 || build != 1158 || (revision != 13 && revision != 16)) {
        g_initializationError = "Unsupported GTA V Enhanced build " + g_gameVersion + "; targets are 1.0.1158.16 and 1.0.1158.13."; return false;
    }
    g_sourceMode = revision == 16;
    return true;
}

class NativeGame final : public Game {
public:
    bool IsPed(int ped, std::uint32_t model) override {
        return ped != 0 && g_hook.Native<int>(0x7239B21A38F536BAULL, ped) != 0 &&
            g_hook.Native<std::uint32_t>(0x9F47B058362C84B5ULL, ped) == model;
    }
    Frame Read() override {
        Frame frame;
        if (g_sourceMode && source::OnlineLatched()) g_onlineLatched = true;
        if (g_onlineLatched) {
            frame.online = true;
            frame.message = "GTA Online was detected. Restart the game to use Story Mode controls.";
            return frame;
        }
        // Every dispatch and frame checks networking before any gameplay mutation.
        frame.online = g_hook.Native<int>(0x10FAB35428CCC9D7ULL) != 0 || // NETWORK_IS_GAME_IN_PROGRESS
            g_hook.Native<int>(0xD83C2B94E7508980ULL) != 0 || // NETWORK_IS_SESSION_ACTIVE
            g_hook.Native<int>(0x9DE624D2FC4B603FULL) != 0 || // NETWORK_IS_SESSION_STARTED
            g_hook.Native<int>(0x53FA83401D9C07FEULL) != 0;   // NETWORK_IS_TRANSITION_STARTED
        if (g_sourceMode && source::OnlineLatched()) frame.online = true;
        if (frame.online) { g_onlineLatched = true; frame.message = "GTA Online is locked out, including invite-only sessions."; return frame; }
        frame.player = g_hook.Native<int>(0x4F8644AF03D0E0D6ULL); // PLAYER_ID
        frame.ped = g_hook.Native<int>(0xD80958FC74E988A6ULL); // PLAYER_PED_ID
        if (frame.player < 0 || !frame.ped || !g_hook.Native<int>(0x7239B21A38F536BAULL, frame.ped)) return frame;
        frame.model = g_hook.Native<std::uint32_t>(0x9F47B058362C84B5ULL, frame.ped);
        frame.paused = g_hook.Native<int>(0xB0034A223497FFCBULL) != 0; // IS_PAUSE_MENU_ACTIVE
        frame.ready = g_hook.Native<int>(0x5E9564D8246B909AULL, frame.player) && // IS_PLAYER_PLAYING
            !g_hook.Native<int>(0x5F9532F3B5CC2551ULL, frame.ped, 0) && // IS_ENTITY_DEAD
            !g_hook.Native<int>(0xD9D2CFFF49FAB35FULL) && // IS_PLAYER_SWITCH_IN_PROGRESS
            !g_hook.Native<int>(0x10D0A8F259E93EC9ULL) && // GET_IS_LOADING_SCREEN_ACTIVE
            g_hook.Native<int>(0x5A859503B0C08678ULL) && // IS_SCREEN_FADED_IN
            !g_hook.Native<int>(0x991251AFC3981F84ULL) && // IS_CUTSCENE_ACTIVE
            g_hook.Native<int>(0x49C32D60007AFA47ULL, frame.player); // IS_PLAYER_CONTROL_ON
        if (frame.ready && !frame.paused) {
            frame.vehicle = g_hook.Native<int>(0x9A9112A0FE9A4713ULL, frame.ped, 0);
            if (frame.vehicle && !g_hook.Native<int>(0x7239B21A38F536BAULL, frame.vehicle)) frame.vehicle = 0;
            frame.message = "Story Mode ready. F11 menu; F1/F2/F3 toggles; F4/F6/F7/F8 actions; F10 disable all.";
        } else frame.message = "Paused, loading, dead, switching character, or player control unavailable. Toggles reset.";
        return frame;
    }
    bool Invincible(int player) override { return g_hook.Native<int>(0xB721981B2B939E07ULL, player) != 0; }
    void Health(int player, bool enabled) override { g_hook.Native(0x239528EACDC3E7DEULL, player, enabled); }
    void Ammo(int ped, bool enabled) override { g_hook.Native(0x183DADC6AA953186ULL, ped, enabled); }
    void Wanted(int player) override { g_hook.Native(0xB302540597885499ULL, player); }
    void Refill(int ped) override {
        int maximum = g_hook.Native<int>(0x15D757606D170C3CULL, ped);
        if (maximum > 0 && maximum < 100000) g_hook.Native(0x6B76DC1F3AE6E6A3ULL, ped, maximum, 0, 0);
        g_hook.Native(0xCEA04D83135264CCULL, ped, 100);
    }
    void Repair(int vehicle) override {
        g_hook.Native(0x115722B1B9C14C1CULL, vehicle);
        g_hook.Native(0x953DA1E1B12C0491ULL, vehicle);
        g_hook.Native(0x45F6D8EEF34ABEF1ULL, vehicle, 1000.0f);
        g_hook.Native(0x70DB57649FA8D0D8ULL, vehicle, 1000.0f);
    }
    void Noon() override { g_hook.Native(0x47C3B5848C3E45D8ULL, 12, 0, 0); }
    void ClearWeather() override { g_hook.Native(0x29B487C359E19889ULL, "CLEAR"); }
};

void KeyboardEvent(DWORD key, WORD, BYTE, BOOL, BOOL withAlt, BOOL previouslyDown, BOOL released) {
    if (key >= g_keys.size() || released || previouslyDown || withAlt) return;
    DWORD process{}; GetWindowThreadProcessId(GetForegroundWindow(), &process);
    if (process == GetCurrentProcessId()) g_keys[key].store(true, std::memory_order_relaxed);
}
bool Take(int key) { return g_keys[key].exchange(false, std::memory_order_relaxed); }
void DrainKeys() { for (auto& key : g_keys) key.store(false, std::memory_order_relaxed); }
bool Foreground() { DWORD process{}; GetWindowThreadProcessId(GetForegroundWindow(), &process); return process == GetCurrentProcessId(); }
void PollKeyboard() {
    static std::array<bool, 256> down{};
    const bool foreground = Foreground();
    const bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
    for (int key : {VK_F1, VK_F2, VK_F3, VK_F4, VK_F6, VK_F7, VK_F8, VK_F10, VK_F11,
                    VK_BACK, VK_ESCAPE, VK_UP, VK_DOWN, VK_RETURN}) {
        const bool pressed = (GetAsyncKeyState(key) & 0x8000) != 0;
        if (foreground && pressed && !down[key] && !alt) g_keys[key].store(true, std::memory_order_relaxed);
        down[key] = pressed;
    }
    if (!foreground) DrainKeys();
}

void Text(float x, float y, const std::string& value, float scale = 0.34f, int red = 239, int green = 243, int blue = 250) {
    g_hook.Native(0x66E0276CC5F6B9DAULL, 0); // SET_TEXT_FONT
    g_hook.Native(0x07C837F9A01C34C9ULL, scale, scale);
    g_hook.Native(0xBE6B23FFA53FB442ULL, red, green, blue, 255);
    g_hook.Native(0xC02F4DBFB51D988BULL, false);
    g_hook.Native(0x6B3C4650BC8BEE47ULL, false);
    g_hook.Native(0x63145D9C883A1A70ULL, 0.0f, 1.0f);
    g_hook.Native(0x25FBB336DF1804CBULL, "STRING");
    g_hook.Native(0x6C188BE134E074AAULL, value.c_str());
    g_hook.Native(0xCD015E5BB0D96A57ULL, x, y, 0);
}
void Rect(float x, float y, float width, float height, int r, int g, int b, int a) {
    g_hook.Native(0x3A618A217E5154F0ULL, x, y, width, height, r, g, b, a, false);
}
void DrawMenu(const Engine& engine) {
    static constexpr std::array<const char*, 8> labels{"Invincibility", "Unlimited ammo / no reload", "Never wanted", "Refill health + armor", "Repair current vehicle", "Set time to noon", "Clear weather", "Disable all toggles"};
    static constexpr std::array<const char*, 8> shortcuts{"F1", "F2", "F3", "F4", "F6", "F7", "F8", "F10"};
    Rect(0.19f, 0.335f, 0.32f, 0.51f, 15, 21, 32, 235);
    Rect(0.19f, 0.12f, 0.32f, 0.08f, 26, 131, 162, 255);
    Text(0.045f, 0.087f, "HappyCheats  /  GTA V", 0.43f);
    Text(0.045f, 0.135f, "ENHANCED  -  STORY MODE", 0.27f, 176, 231, 238);
    for (int i = 0; i < 8; ++i) {
        float y = 0.19f + i * 0.04f;
        if (i == g_selection) Rect(0.19f, y + 0.015f, 0.308f, 0.039f, 39, 74, 92, 255);
        std::string label = std::string(shortcuts[i]) + "  " + labels[i];
        if (i < 3) label += engine.Enabled(i) ? "  [ON]" : "  [OFF]";
        Text(0.043f, y, label, 0.30f);
    }
    Text(0.045f, 0.53f, "Up/Down + Enter   |   F11 / Backspace close", 0.26f, 168, 185, 202);
    Text(0.045f, 0.565f, "GTA Online is unavailable.", 0.26f, 168, 185, 202);
    // Prevent menu arrows/Enter from controlling the player while this menu is open.
    for (int control : {172, 173, 174, 175, 176, 177, 18, 27, 199, 200, 201, 202})
        g_hook.Native(0xFE99B66D079CF6BCULL, 0, control, true);
}
void Input(Engine& engine) {
    if (!Foreground() || !engine.Ready()) { g_menu = false; DrainKeys(); return; }
    if (Take(VK_F11)) g_menu = !g_menu;
    if (g_menu) {
        if (Take(VK_BACK) || Take(VK_ESCAPE)) g_menu = false;
        if (Take(VK_UP)) g_selection = (g_selection + 7) % 8;
        if (Take(VK_DOWN)) g_selection = (g_selection + 1) % 8;
        if (Take(VK_RETURN)) engine.Local(g_selection < 7 ? Features[g_selection] : "disable_all");
    }
    const std::array<int, 7> hotkeys{VK_F1, VK_F2, VK_F3, VK_F4, VK_F6, VK_F7, VK_F8};
    for (int i = 0; i < 7; ++i) if (Take(hotkeys[i])) engine.Local(Features[i]);
    if (Take(VK_F10)) engine.Local("disable_all");
    DrainKeys();
    if (g_menu && engine.Ready()) DrawMenu(engine);
}

Json InactiveStatus(const std::string& message) {
    Json enabled = Json::object(), available = Json::object();
    for (const auto* feature : Features) { enabled[feature] = false; available[feature] = false; }
    return {{"schema_version", 1}, {"version", "0.2.0"}, {"session_id", g_session},
        {"process_id", GetCurrentProcessId()}, {"updated_utc", static_cast<std::int64_t>(Now())}, {"ready", false}, {"online", false},
        {"enabled", enabled}, {"available", available}, {"values", Json::object()},
        {"last_command", ""}, {"last_success", false}, {"last_message", message}, {"message", message},
        {"initialization_error", g_initializationError}, {"game_version", g_gameVersion},
        {"runtime_backend", g_sourceMode ? "enhanced-source" : "scripthookv"}, {"gameplay_verified", false}};
}
struct RuntimeFrame {
    NativeGame game;
    Engine engine{game, g_session, GetCurrentProcessId()};
    std::string previousCommand;
    ULONGLONG lastPoll{}, lastStatus{};
};
std::atomic<bool> g_sourceReset{true};
bool SourceInterrupted() { return g_sourceMode && (g_sourceReset.load() || source::OnlineLatched() || !source::Ready()); }
void PublishFrame(RuntimeFrame& frame) {
    // An inactive callback can occur inside a native dispatch. Its inactive /
    // Online status must win over a cached Engine state or successful ACK.
    if (SourceInterrupted()) return;
    auto status = frame.engine.Status(Now());
    status["game_version"] = g_gameVersion;
    status["runtime_backend"] = g_sourceMode ? "enhanced-source" : "scripthookv";
    status["gameplay_verified"] = false;
    WriteStatus(status);
}
void TickFrame(RuntimeFrame& frame) {
    frame.engine.Tick();
    if (SourceInterrupted()) return;
    const ULONGLONG tick = GetTickCount64();
    if (tick - frame.lastPoll >= 50) {
        frame.lastPoll = tick;
        auto command = ReadCommand();
        if (!command.empty() && command != frame.previousCommand) {
            frame.previousCommand = command;
            frame.engine.Submit(command, Now());
            if (SourceInterrupted()) return;
            PublishFrame(frame); frame.lastStatus = tick;
        }
    }
    if (g_sourceMode) PollKeyboard();
    Input(frame.engine);
    if (SourceInterrupted()) return;
    if (tick - frame.lastStatus >= 200) { PublishFrame(frame); frame.lastStatus = tick; }
}
// Process-lifetime storage avoids issuing native cleanup from the Windows loader
// lock during process detach. Reset only on an eligible game script callback.
RuntimeFrame* g_sourceFrame{};
void SourceTick() {
    if (g_sourceReset.exchange(false)) {
        delete g_sourceFrame; g_sourceFrame = nullptr;
        g_session = NewSession();
        if (g_session.empty()) throw std::runtime_error("Session entropy unavailable");
        g_sourceFrame = new RuntimeFrame();
    }
    g_registered.store(true);
    TickFrame(*g_sourceFrame);
}
void SourceInactive(const std::string& message, bool online) {
    g_sourceReset.store(true);
    g_registered.store(false);
    g_menu = false; DrainKeys();
    g_initializationError = message;
    auto status = InactiveStatus(message);
    status["online"] = online;
    WriteStatus(status);
}
void ScriptMain() {
    try {
        // ScriptHook can restart the script without recreating this DLL. Every
        // entry gets a new command session so an old on-disk command cannot replay.
        g_session = NewSession();
        if (g_session.empty()) throw std::runtime_error("Session entropy unavailable");
        WriteStatus(InactiveStatus("Starting a new Story Mode command session."));
        RuntimeFrame frame;
        for (;;) {
            TickFrame(frame);
            g_scriptHook.Wait(0);
        }
    } catch (...) {
        g_initializationError = "Trainer stopped after a runtime error. Restart Story Mode before trying again.";
        try { WriteStatus(InactiveStatus(g_initializationError)); } catch (...) {}
        // A C++ error stops further commands. Never run natives from another thread.
        for (;;) g_scriptHook.Wait(1000);
    }
}
DWORD WINAPI InitializeWorker(LPVOID) {
    try {
        // Runtime loading, scanning and hook installation occur on this worker,
        // after DllMain returns. DllMain already pinned its queued entry point.
        g_session = NewSession();
        if (g_session.empty() || !InitBridge()) return 0;
        if (!SupportedHost()) { WriteStatus(InactiveStatus(g_initializationError)); return 0; }
        if (g_sourceMode) {
            WriteStatus(InactiveStatus("Validating the experimental Enhanced 1.0.1158.16 source runtime."));
            // ASI loading may precede the game's unpacking/initialization. Retry
            // read-only resolution for a bounded interval; a successful call owns
            // the scheduler hook and subsequently reports from the script thread.
            const auto deadline = GetTickCount64() + 30000;
            do {
                if (source::Initialize(GetModuleHandleW(nullptr), SourceTick, SourceInactive)) return 0;
                SourceInactive(source::LastError(), source::OnlineLatched());
                if (source::OnlineLatched()) break;
                Sleep(500);
            } while (GetTickCount64() < deadline);
            // Keep the diagnostic heartbeat fresh without touching game memory.
            const std::string failure = "Enhanced source runtime inactive: " + source::LastError();
            for (;;) { SourceInactive(failure, source::OnlineLatched()); Sleep(1000); }
        } else {
            const auto path = std::filesystem::path(ModulePath(g_module)).parent_path() / L"ScriptHookV.dll";
            HMODULE hook = LoadLibraryExW(path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
            if (!g_scriptHook.Bind(hook)) {
                g_initializationError = "Matching ScriptHookV runtime is missing or lacks a required public export.";
                WriteStatus(InactiveStatus(g_initializationError)); return 0;
            }
            WriteStatus(InactiveStatus("Waiting for ScriptHookV and a loaded Story Mode character."));
            g_scriptHook.Register(g_module, ScriptMain);
            g_scriptHook.KeyboardRegister(KeyboardEvent);
            g_registered.store(true);
        }
    } catch (...) {
        try { g_initializationError = "GTA V runtime initialization failed. Restart Story Mode before trying again.";
              WriteStatus(InactiveStatus(g_initializationError)); } catch (...) {}
    }
    return 0;
}
}

extern "C" __declspec(dllexport) int HappyCheatsGtaVRegistered() { return g_registered.load() ? 1 : 0; }
BOOL WINAPI DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_module = module;
        DisableThreadLibraryCalls(module);
        // Pin before queuing work: a caller may immediately FreeLibrary after
        // LoadLibrary returns, otherwise unloading this worker's entry point.
        HMODULE pinned{};
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&InitializeWorker), &pinned)) return FALSE;
        HANDLE worker = CreateThread(nullptr, 0, InitializeWorker, nullptr, 0, nullptr);
        if (worker) CloseHandle(worker);
    }
    return TRUE;
}
