#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <cstdio>
#include <nlohmann/json.hpp>
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 1;
    const auto asi = std::filesystem::absolute(argv[1]);
    const auto statusPath = asi.parent_path() / L"HappyCheatsGtaV" / L"status.json";
    if (GetModuleHandleW(L"ScriptHookV.dll") || !LoadLibraryW(asi.c_str())) return 2;
    const auto deadline = GetTickCount64() + 5000;
    while (GetTickCount64() < deadline) {
        try {
            HANDLE file = CreateFileW(statusPath.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (file == INVALID_HANDLE_VALUE) { Sleep(20); continue; }
            char buffer[16384]; DWORD count{};
            const bool read = ReadFile(file, buffer, sizeof(buffer), &count, nullptr) != FALSE;
            CloseHandle(file);
            if (!read) { Sleep(20); continue; }
            auto status = nlohmann::json::parse(buffer, buffer + count);
            if (status.value("process_id", 0UL) == GetCurrentProcessId() &&
                !status.value("initialization_error", std::string()).empty()) {
                if (status["game_version"] != "1.0.1158.16" || status["runtime_backend"] != "enhanced-source" ||
                    status["ready"] != false || status["online"] != false || GetModuleHandleW(L"ScriptHookV.dll")) return 3;
                for (auto& value : status["available"]) if (value.get<bool>()) return 4;
                std::puts("PASS production ASI routes 1158.16 to source backend, rejects missing interfaces, and never loads ScriptHookV");
                return 0;
            }
        } catch (...) { }
        Sleep(20);
    }
    return 5;
}
