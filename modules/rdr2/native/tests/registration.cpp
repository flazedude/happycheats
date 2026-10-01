#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
int wmain(int argc, wchar_t** argv) {
    if(argc != 2 || GetModuleHandleW(L"ScriptHookRDR2.dll")) return 2;
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
    HMODULE trainer = LoadLibraryW(argv[1]);
    if(!trainer) { std::printf("FAIL trainer loader error: %lu\n",GetLastError()); return 3; }
    HMODULE hook = GetModuleHandleW(L"ScriptHookRDR2.dll");
    if(!hook) return 4;
    auto registered = reinterpret_cast<int(*)()>(GetProcAddress(hook,"testRegistrations"));
    auto natives = reinterpret_cast<int(*)()>(GetProcAddress(hook,"testNativeCalls"));
    if(!registered || !natives || registered() != 1 || natives() != 0) return 5;
    // Retain the fake dependency while checking unregister on ASI unload.
    LoadLibraryW(L"ScriptHookRDR2.dll");
    FreeLibrary(trainer);
    if(registered() != 0 || natives() != 0) return 6;
    std::puts("PASS ASI loader dependency, decorated ABI registration/unregister; fake hook only, no game natives");
    return 0;
}
