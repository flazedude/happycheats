#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <cstdio>
#include <nlohmann/json.hpp>
#include "../build_guard.h"

// Load our freshly built DLL into this unsupported fixture host only. The
// compatibility gate must publish diagnostics without installing game hooks.
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    const std::filesystem::path dll=std::filesystem::absolute(argv[1]);
    if (!LoadLibraryW(dll.c_str())) { std::printf("LoadLibrary failed: %lu\n",GetLastError()); return 3; }
    for(int attempt=0;attempt<100;++attempt) {
        Sleep(50);
        try {
            std::ifstream file(dll.parent_path()/"status.json");
            if(!file) continue;
            nlohmann::json status; file>>status;
            if(status.value("process_id",0u)!=GetCurrentProcessId()) continue;
            if(status.value("ready",true) || status.value("player_ready",true)) return 4;
            if(status.value("session_id",std::string()).empty()) return 5;
            for(auto& value:status.at("enabled")) if(value.get<bool>()) return 6;
            for(auto& value:status.at("available")) if(value.get<bool>()) return 7;
            if(status.value("message",std::string()).empty()) return 8;
            if(status.value("initialization_error",std::string()).empty()) return 10;
            if(status.value("observed_image_size",0u)==0 ||
                status.value("expected_image_size",0u)!=crimson_build::ExpectedImageSize) return 11;
            if(status.value("version",std::string())!="0.2.8") return 12;
            if(status.at("hook_library").value("attempted",true)) return 13;
            const auto& hooks=status.at("hook_results");
            if(hooks.size()!=5) return 14;
            for(const auto* name:{"stats","damage","movement","marker","outer_movement"}) {
                const auto& hook=hooks.at(name);
                if(hook.value("ready",true) || hook.at("create").value("attempted",true) ||
                    hook.at("enable").value("attempted",true) || hook.value("skipped_reason",std::string())!="build_guard_failed") return 15;
                if(!hook.at("create").at("status").is_null() || !hook.at("enable").at("status").is_null() ||
                    !hook.at("failure_snapshot").is_null()) return 16;
            }
            std::puts("PASS packaged native DLL startup and unsupported-host rejection; no game opened");
            return 0;
        } catch(...) {}
    }
    std::puts("No fresh native status from fixture host");
    return 9;
}
