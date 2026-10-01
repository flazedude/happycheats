// Isolated ASI lifetime test. Never opens or launches the game.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
int wmain(int argc,wchar_t** argv){
    if(argc!=2)return 2;
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);
    DWORD_PTR processMask{},systemMask{};
    if(!GetProcessAffinityMask(GetCurrentProcess(),&processMask,&systemMask))return 3;
    auto single=processMask&(~processMask+1);
    if(!SetProcessAffinityMask(GetCurrentProcess(),single))return 4;
    SetThreadPriority(GetCurrentThread(),THREAD_PRIORITY_HIGHEST);
    auto module=LoadLibraryW(argv[1]);
    if(!module)return 5;
    if(!FreeLibrary(module))return 6;
    Sleep(250);
    std::puts("PASS immediate host unload leaves ASI worker code loaded");
    return 0;
}
