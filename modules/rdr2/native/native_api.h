#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <type_traits>
#include "trainer_core.h"

namespace rdr2 {
struct ScriptHook {
    void (*init)(std::uint64_t) = nullptr;
    void (*push)(std::uint64_t) = nullptr;
    std::uint64_t* (*call)() = nullptr;
    void (*registerScript)(HMODULE, void(*)()) = nullptr;
    void (*unregisterScript)(HMODULE) = nullptr;
    void (*wait)(DWORD) = nullptr;
    bool bind(HMODULE module) {
        if(!module) return false;
        init = reinterpret_cast<decltype(init)>(GetProcAddress(module,"?nativeInit@@YAX_K@Z"));
        push = reinterpret_cast<decltype(push)>(GetProcAddress(module,"?nativePush64@@YAX_K@Z"));
        call = reinterpret_cast<decltype(call)>(GetProcAddress(module,"?nativeCall@@YAPEA_KXZ"));
        registerScript = reinterpret_cast<decltype(registerScript)>(GetProcAddress(module,"?scriptRegister@@YAXPEAUHINSTANCE__@@P6AXXZ@Z"));
        unregisterScript = reinterpret_cast<decltype(unregisterScript)>(GetProcAddress(module,"?scriptUnregister@@YAXPEAUHINSTANCE__@@@Z"));
        wait = reinterpret_cast<decltype(wait)>(GetProcAddress(module,"?scriptWait@@YAXK@Z"));
        return init && push && call && registerScript && unregisterScript && wait;
    }
    template<class T> static std::uint64_t argument(T value) {
        static_assert(sizeof(T) <= 8);
        std::uint64_t result = 0;
        std::memcpy(&result, &value, sizeof(value));
        return result;
    }
    template<class Result = void, class... Args> Result invoke(std::uint64_t hash, Args... args) {
        init(hash); (push(argument(args)), ...);
        std::uint64_t* result = call();
        if constexpr(!std::is_void_v<Result>) {
            Result value{};
            if(result) std::memcpy(&value, result, sizeof(value));
            return value;
        }
    }
};

// Hashes/signatures: alloc8or/rdr3-nativedb-data. See modules/rdr2/README.md.
// All native calls occur in ScriptHook's game script fiber, never a worker thread.
class NativeBackend final : public Backend {
    ScriptHook& hook;
    void core(int ped, int index) { hook.invoke(0xC6258F41D86676E0ULL, ped, index, 100); }
public:
    explicit NativeBackend(ScriptHook& binding) : hook(binding) {}
    bool exists(int entity) override { return entity != 0 && hook.invoke<int>(0xD42BD6EB2E0F1677ULL, entity) != 0; }
    Snapshot sample() {
        Snapshot state;
        // This additional guard complements ScriptHookRDR2's own Online block.
        // Check before requesting or mutating any player/horse state.
        state.online = hook.invoke<int>(0x9DE624D2FC4B603FULL) != 0 || hook.invoke<int>(0x10FAB35428CCC9D7ULL) != 0;
        if(state.online) return state;
        state.player = hook.invoke<int>(0x217E9DC48139933DULL);
        state.ped = hook.invoke<int>(0x096275889B8E0EE0ULL);
        state.ready = exists(state.ped) && hook.invoke<int>(0xBFFB35986CAAE58CULL, state.player) != 0 &&
            hook.invoke<int>(0x7D5B1F88E7504BBAULL, state.ped) == 0 && hook.invoke<int>(0x3317DEDB88C95038ULL, state.ped, 1) == 0;
        if(state.ready && hook.invoke<int>(0x460BC76A0E10655EULL, state.ped) != 0) {
            const int mount = hook.invoke<int>(0xE7E11B8DCBED1058ULL, state.ped);
            if(exists(mount) && hook.invoke<int>(0x7D5B1F88E7504BBAULL, mount) == 0) state.mount = mount;
        }
        return state;
    }
    void health(int ped) override {
        const int maximum = hook.invoke<int>(0x15D757606D170C3CULL, ped, 0);
        if(maximum > 0 && maximum < 100000) hook.invoke(0xAC2767ED8BDFAB15ULL, ped, maximum, 0);
        core(ped, 0);
    }
    void stamina(int, int ped) override { hook.invoke(0x675680D089BFA21FULL, ped, 100.0f); core(ped, 1); }
    void deadeye(int player, int ped) override {
        const float maximum = hook.invoke<float>(0x592F58BC4D2A2CF3ULL, player, 0);
        if(maximum > 0.0f && maximum < 100000.0f) hook.invoke(0x51345AE20F22C261ULL, player, maximum, 0, 0, 1);
        core(ped, 2);
    }
    void ammo(int ped, bool enabled) override { hook.invoke(0xFBAA1E06B6BCA741ULL, ped, static_cast<int>(enabled)); }
    void horse_stamina(int ped) override { hook.invoke(0x675680D089BFA21FULL, ped, 100.0f); core(ped, 1); }
    void clear_wanted(int player) override { hook.invoke(0x062B4A4A3396351DULL, player); hook.invoke(0x55F37F5F3F2475E1ULL); }
};
}
