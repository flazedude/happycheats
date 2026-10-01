#include "native_api.h"
#include <bcrypt.h>
#include <array>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <vector>

extern "C" void** hcScriptHookImportAnchor;

namespace {
HMODULE self = nullptr;
rdr2::ScriptHook hook;
bool registered = false;
std::int64_t unix_time() {
    return std::chrono::duration_cast<std::chrono::seconds>(std::chrono::system_clock::now().time_since_epoch()).count();
}
std::filesystem::path module_path(HMODULE module) {
    std::array<wchar_t, 32768> buffer{};
    const DWORD size = GetModuleFileNameW(module, buffer.data(), static_cast<DWORD>(buffer.size()));
    if(!size || size == buffer.size()) return {};
    return std::filesystem::path(std::wstring(buffer.data(), size));
}
bool supported_version(const std::filesystem::path& path) {
    DWORD ignored = 0;
    const DWORD length = GetFileVersionInfoSizeW(path.c_str(), &ignored);
    if(!length || length > 1024 * 1024) return false;
    std::vector<unsigned char> bytes(length);
    if(!GetFileVersionInfoW(path.c_str(), 0, length, bytes.data())) return false;
    VS_FIXEDFILEINFO* info = nullptr; UINT infoSize = 0;
    if(!VerQueryValueW(bytes.data(), L"\\", reinterpret_cast<void**>(&info), &infoSize) || infoSize < sizeof(VS_FIXEDFILEINFO)) return false;
    return info->dwSignature == 0xFEEF04BD && info->dwFileVersionMS == ((1UL << 16) | 0UL) && info->dwFileVersionLS == ((1491UL << 16) | 50UL);
}
void publish(const std::filesystem::path& bridge, const rdr2::Json& status) {
    const auto temporary = bridge / L"status.tmp";
    { std::ofstream file(temporary, std::ios::binary | std::ios::trunc); file << status.dump(); file.flush(); if(!file.good()) return; }
    MoveFileExW(temporary.c_str(), (bridge / L"status.json").c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
}
std::string mailbox(const std::filesystem::path& bridge) {
    const auto command = bridge / L"command.json";
    const auto consumed = bridge / L"command.consumed";
    // The rename claims one whole mailbox. The client publishes atomically too.
    if(!MoveFileExW(command.c_str(), consumed.c_str(), MOVEFILE_REPLACE_EXISTING)) return {};
    std::array<char, 2049> bytes{};
    std::streamsize length = 0;
    { std::ifstream file(consumed, std::ios::binary); if(file) { file.read(bytes.data(), bytes.size()); length = file.gcount(); } }
    // Never act unless consumption succeeded. A retry can only see a new mailbox.
    if(!DeleteFileW(consumed.c_str()) || length <= 0 || length > 2048) return {};
    return std::string(bytes.data(), static_cast<std::size_t>(length));
}
void script_main() {
    try {
        const auto root = module_path(self).parent_path();
        const auto bridge = root / L"HappyCheatsRdr2" / L"bridge";
        std::filesystem::create_directories(bridge);
        std::uint64_t nonce = 0;
        const bool randomOkay = BCryptGenRandom(nullptr, reinterpret_cast<PUCHAR>(&nonce), sizeof(nonce), BCRYPT_USE_SYSTEM_PREFERRED_RNG) == 0;
        rdr2::NativeBackend backend(hook);
        rdr2::Trainer trainer(backend, nonce);
        if(!randomOkay || !supported_version(module_path(nullptr))) {
            for(;;) {
                auto status = trainer.status(unix_time(), GetCurrentProcessId());
                status["message"] = !randomOkay ? "Secure session generation failed. Trainer is disabled." : "Unsupported RDR2 executable version. This trainer profile requires 1.0.1491.50; gameplay compatibility remains unverified.";
                publish(bridge, status); hook.wait(1000);
            }
        }
        try {
            std::int64_t lastPublication = 0;
            for(;;) {
                trainer.observe(backend.sample());
                const auto now = unix_time();
                const auto command = mailbox(bridge);
                bool acknowledged = !command.empty() && trainer.consume(command, now);
                trainer.tick();
                if(now != lastPublication || acknowledged) { publish(bridge, trainer.status(now, GetCurrentProcessId())); lastPublication = now; }
                hook.wait(0);
            }
        } catch(...) {
            // Cleanup is still on the script fiber while the controller exists.
            // Recheck Online first: never perform even cleanup writes in Online.
            trainer.stop_after_error(backend.sample());
            for(;;) {
                auto status = trainer.status(unix_time(), GetCurrentProcessId());
                status["message"] = "Trainer stopped after a runtime error. Controls were reset; restart Story Mode.";
                publish(bridge, status); hook.wait(1000);
            }
        }
    } catch(...) {
        // No worker thread, native hook, or automatic restart on failures.
        // A stale heartbeat makes the client fail closed until the game restarts.
        for(;;) hook.wait(1000);
    }
}
}

extern "C" BOOL APIENTRY DllMain(HMODULE instance, DWORD reason, LPVOID) {
    if(reason == DLL_PROCESS_ATTACH) {
        const auto host = module_path(nullptr).filename().wstring();
        if(_wcsicmp(host.c_str(), L"RDR2.exe") != 0) return FALSE;
        if(!hcScriptHookImportAnchor || !*hcScriptHookImportAnchor) return FALSE;
        if(!hook.bind(GetModuleHandleW(L"ScriptHookRDR2.dll"))) return FALSE;
        self = instance;
        DisableThreadLibraryCalls(instance);
        hook.registerScript(instance, &script_main); registered = true;
    } else if(reason == DLL_PROCESS_DETACH && registered) {
        // Native calls are not safe in DllMain. Normal runtime control cleanup
        // occurs on the script fiber; live unloading is intentionally unsupported.
        hook.unregisterScript(instance); registered = false;
    }
    return TRUE;
}
