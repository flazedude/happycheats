#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <MinHook.h>
#include <cstdio>

// Exercise the actual compiled hook library inside this disposable fixture.
// This executable never locates, opens, loads or modifies a game process.
static volatile int baseValue = 17;
__attribute__((noinline)) int FixtureFunction(int input) {
    int value = baseValue;
    value += input;
    value *= 2;
    return value + 3;
}
using Function = int(*)(int);
static Function original{};
__attribute__((noinline)) int Hook(int input) { return original(input) + 100; }

int main() {
    auto target = reinterpret_cast<void*>(&FixtureFunction);
    Function volatile call = &FixtureFunction;
    if (call(4) != 45 || MH_Initialize() != MH_OK) return 1;
    if (MH_CreateHook(target,reinterpret_cast<void*>(&Hook),reinterpret_cast<void**>(&original)) != MH_OK) return 2;
    if (MH_EnableHook(target) != MH_OK || call(4) != 145 || original(4) != 45) return 3;
    if (MH_DisableHook(target) != MH_OK || call(4) != 45) return 4;
    if (MH_RemoveHook(target) != MH_OK || MH_Uninitialize() != MH_OK) return 5;
    std::puts("PASS native hook install, trampoline, disable and removal in fixture process");
    return 0;
}
