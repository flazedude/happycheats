#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <cstdio>
#include <nlohmann/json.hpp>
int wmain(int argc, wchar_t** argv) {
    if (argc != 3) return 2;
    auto path = std::filesystem::absolute(argv[1]);
    if (GetModuleHandleW(L"ScriptHookV.dll")) return 3;
    HMODULE module = LoadLibraryW(path.c_str());
    if (!module) { std::printf("LoadLibrary failed: %lu\n", GetLastError()); return 4; }
    auto registered = reinterpret_cast<int(*)()>(GetProcAddress(module, "HappyCheatsGtaVRegistered"));
    if (!registered) return 6;
    bool checked = false;
    const auto deadline = GetTickCount64() + 3000;
    while (GetTickCount64() < deadline) try {
        const auto statusPath = path.parent_path() / "HappyCheatsGtaV" / "status.json";
        HANDLE file = CreateFileW(statusPath.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) { Sleep(10); continue; }
        char buffer[16384]; DWORD count{};
        const bool read = ReadFile(file, buffer, sizeof(buffer), &count, nullptr) != FALSE;
        CloseHandle(file);
        if (!read) { Sleep(10); continue; }
        auto status = nlohmann::json::parse(buffer, buffer + count);
        if (status["process_id"] != GetCurrentProcessId()) { Sleep(10); continue; }
        if (status["ready"] != false || status["online"] != false) return 7;
        if (!status["updated_utc"].is_number_integer() || status["session_id"].get<std::string>().size() != 32) return 8;
        std::wstring expected = argv[2]; std::string narrow(expected.begin(), expected.end());
        if (status["initialization_error"].get<std::string>().find(narrow) == std::string::npos) { Sleep(10); continue; }
        for (auto& value : status["available"]) if (value.get<bool>()) return 10;
        for (auto& value : status["enabled"]) if (value.get<bool>()) return 11;
        checked = true; break;
    } catch (...) { Sleep(10); }
    if (!checked || registered()) return 12;
    auto dependency = GetModuleHandleW(L"ScriptHookV.dll");
    if (dependency) {
        auto calls = reinterpret_cast<int(*)()>(GetProcAddress(dependency, "MockNativeCalls"));
        if (!calls || calls()) return 13;
    }
    FreeLibrary(module);
    std::puts("PASS deferred ASI host/build/export gate and zero native mutations");
    return 0;
}
