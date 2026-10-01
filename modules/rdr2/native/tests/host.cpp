#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
int wmain(int argc, wchar_t** argv) {
    if(argc != 2) return 2;
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
    HMODULE module = LoadLibraryW(argv[1]);
    if(module) { FreeLibrary(module); std::puts("FAIL ASI accepted unsupported host"); return 3; }
    if(GetLastError() != ERROR_DLL_INIT_FAILED) { std::printf("FAIL unexpected loader error: %lu\n", GetLastError()); return 4; }
    if(GetModuleHandleW(L"ScriptHookRDR2.dll")) { std::puts("FAIL test loaded ScriptHook runtime"); return 5; }
    std::puts("PASS real ASI refuses unsupported host; no game or ScriptHook runtime opened");
    return 0;
}
