#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
static int registrations = 0, nativeCalls = 0;
extern "C" void fakeInit(std::uint64_t) { ++nativeCalls; }
extern "C" void fakePush(std::uint64_t) { ++nativeCalls; }
extern "C" std::uint64_t* fakeCall() { ++nativeCalls; static std::uint64_t result = 0; return &result; }
extern "C" void fakeRegister(HMODULE, void(*)()) { ++registrations; }
extern "C" void fakeUnregister(HMODULE) { --registrations; }
extern "C" void fakeWait(DWORD) {}
extern "C" __declspec(dllexport) int testRegistrations() { return registrations; }
extern "C" __declspec(dllexport) int testNativeCalls() { return nativeCalls; }
