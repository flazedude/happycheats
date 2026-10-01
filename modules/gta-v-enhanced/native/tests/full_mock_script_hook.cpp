#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <atomic>
#include <array>
#include <cstdint>
#include <cstring>
#include <vector>

// Inert implementation of the documented SDK ABI. Only used by the test host;
// never copied to the payload. Every native result/mutation is simulated.
using Keyboard = void (*)(DWORD, WORD, BYTE, BOOL, BOOL, BOOL, BOOL);
std::atomic<void(*)()> script{};
std::atomic<Keyboard> keyboard{};
std::atomic<int> calls{}, mutations{}, errors{}, online{}, invincible{}, ammo{}, wanted{}, health{}, armor{}, repairs{}, hour{-1}, weather{};
thread_local std::uint64_t currentHash{};
thread_local std::vector<std::uint64_t> args;
thread_local std::array<std::uint64_t, 4> result{};

void Expect(std::initializer_list<std::uint64_t> expected) {
    if (args != std::vector<std::uint64_t>(expected)) ++errors;
}
extern "C" void MockNativeInit(std::uint64_t hash) { currentHash = hash; args.clear(); ++calls; }
extern "C" void MockNativePush(std::uint64_t argument) { args.push_back(argument); }
extern "C" std::uint64_t* MockNativeCall() {
    result.fill(0);
    switch (currentHash) {
        case 0x10FAB35428CCC9D7ULL: result[0] = online.load(); break;
        case 0xD83C2B94E7508980ULL: case 0x9DE624D2FC4B603FULL: case 0x53FA83401D9C07FEULL: break;
        case 0x4F8644AF03D0E0D6ULL: result[0] = 0; break;
        case 0xD80958FC74E988A6ULL: result[0] = 41; break;
        case 0x7239B21A38F536BAULL: result[0] = args.size() == 1 && (args[0] == 41 || args[0] == 98); break;
        case 0x9F47B058362C84B5ULL: Expect({41}); result[0] = 101; break;
        case 0xB0034A223497FFCBULL: case 0xD9D2CFFF49FAB35FULL: case 0x10D0A8F259E93EC9ULL: case 0x991251AFC3981F84ULL: break;
        case 0x5F9532F3B5CC2551ULL: Expect({41, 0}); break;
        case 0x5E9564D8246B909AULL: case 0x49C32D60007AFA47ULL: Expect({0}); result[0] = 1; break;
        case 0x5A859503B0C08678ULL: result[0] = 1; break;
        case 0x9A9112A0FE9A4713ULL: Expect({41, 0}); result[0] = 98; break;
        case 0xB721981B2B939E07ULL: Expect({0}); result[0] = invincible.load(); break;
        case 0x239528EACDC3E7DEULL:
            if (args.size() != 2 || args[0] != 0 || args[1] > 1) ++errors;
            else invincible = static_cast<int>(args[1]);
            ++mutations; break;
        case 0x183DADC6AA953186ULL:
            if (args.size() != 2 || args[0] != 41 || args[1] > 1) ++errors;
            else ammo = static_cast<int>(args[1]);
            ++mutations; break;
        case 0xB302540597885499ULL: Expect({0}); ++wanted; ++mutations; break;
        case 0x15D757606D170C3CULL: Expect({41}); result[0] = 200; break;
        case 0x6B76DC1F3AE6E6A3ULL: Expect({41, 200, 0, 0}); health = 200; ++mutations; break;
        case 0xCEA04D83135264CCULL: Expect({41, 100}); armor = 100; ++mutations; break;
        case 0x115722B1B9C14C1CULL: case 0x953DA1E1B12C0491ULL: Expect({98}); ++repairs; ++mutations; break;
        case 0x45F6D8EEF34ABEF1ULL: case 0x70DB57649FA8D0D8ULL:
            Expect({98, 0x447a0000}); ++repairs; ++mutations; break; // IEEE-754 float 1000.0
        case 0x47C3B5848C3E45D8ULL: Expect({12, 0, 0}); hour = 12; ++mutations; break;
        case 0x29B487C359E19889ULL:
            if (args.size() != 1 || std::strcmp(reinterpret_cast<const char*>(args[0]), "CLEAR") != 0) ++errors;
            weather = 1; ++mutations; break;
        default: ++errors; break;
    }
    return result.data();
}
extern "C" void MockRegister(HMODULE, void (*callback)()) { script = callback; }
extern "C" void MockUnregister(HMODULE) { script = nullptr; }
extern "C" void MockWait(DWORD time) { Sleep(time ? time : 5); }
extern "C" void MockKeyboardRegister(Keyboard callback) { keyboard = callback; }
extern "C" void MockKeyboardUnregister(Keyboard) { keyboard = nullptr; }
extern "C" __declspec(dllexport) int MockRegistered() { return script.load() && keyboard.load() ? 1 : 0; }
extern "C" __declspec(dllexport) void MockStartScript() { auto callback = script.load(); if (callback) callback(); }
extern "C" __declspec(dllexport) void MockSetOnline(int value) { online = value; }
extern "C" __declspec(dllexport) int MockValue(int index) {
    switch (index) {
        case 0: return calls; case 1: return mutations; case 2: return errors;
        case 3: return invincible; case 4: return ammo; case 5: return wanted;
        case 6: return health; case 7: return armor; case 8: return repairs;
        case 9: return hour; case 10: return weather; default: return -1;
    }
}
