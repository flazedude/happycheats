#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <nlohmann/json.hpp>
int wmain(int count,wchar_t** arguments) {
    if(count!=2)return 2;
    auto dll=std::filesystem::absolute(arguments[1]);
    if(!LoadLibraryW(dll.c_str()))return 3;
    for(int i=0;i<100;i++){
        Sleep(50);
        try {
            std::ifstream input(dll.parent_path()/"status.json");nlohmann::json value;input>>value;
            if(value.value("process_id",0u)!=GetCurrentProcessId())continue;
            if(value.value("ready",true) || value.at("values").value("initialization_error","").empty())return 4;
            for(auto& enabled:value.at("enabled"))if(enabled.get<bool>())return 5;
            for(auto& enabled:value.at("available"))if(enabled.get<bool>())return 6;
            if(value.value("version","")!="0.1.2" || value.value("session_id","").size()!=32)return 7;
            std::cout<<"PASS Survival Log packaged native DLL startup and unsupported-host diagnostics; no game accessed.\n";return 0;
        }catch(const std::exception&){}
    }
    return 8;
}
