#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <cstdio>
#include <nlohmann/json.hpp>

// A tiny, disposable target for verifying our loader's Windows API calls. This
// is compiled from this source, not a game executable, and has no game data.
int main() {
    wchar_t name[32768]{};
    if(!GetModuleFileNameW(nullptr,name,32768)) return 2;
    const auto statusPath=std::filesystem::path(name).parent_path()/"HappyCheatsCrimsonDesert/status.json";
    for(int attempt=0;attempt<300;++attempt) {
        Sleep(50);
        try {
            std::ifstream file(statusPath);
            if(!file) continue;
            nlohmann::json status; file>>status;
            if(status.value("process_id",0u)!=GetCurrentProcessId()) continue;
            if(status.value("ready",true) || status.value("player_ready",true)) return 3;
            Sleep(1500); // Allow injector module enumeration to finish.
            return 0;
        } catch(...) {}
    }
    return 4;
}
