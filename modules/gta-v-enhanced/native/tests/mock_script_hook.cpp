#include <windows.h>
// Deliberately incomplete inert fixture. Loading the ASI must load this dependency
// without calling even its one native export when host/version validation fails.
static int calls{};
extern "C" void MockNativeInit(unsigned long long) { ++calls; }
extern "C" __declspec(dllexport) int MockNativeCalls() { return calls; }
