#pragma once
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <type_traits>

// Original declarations of the public x64 ABI, verified against the official
// ScriptHookV SDK 1.0.617.1a import library. No SDK header or library is shipped.
// ScriptHookV's implementation remains an external, offline-only prerequisite.
namespace happygta {
using Keyboard = void (*)(DWORD, WORD, BYTE, BOOL, BOOL, BOOL, BOOL);
struct ScriptHook {
    void (*Register)(HMODULE, void (*)()){};
    void (*Unregister)(HMODULE){};
    void (*Wait)(DWORD){};
    void (*KeyboardRegister)(Keyboard){};
    void (*KeyboardUnregister)(Keyboard){};
    void (*Init)(std::uint64_t){};
    void (*Push)(std::uint64_t){};
    std::uint64_t* (*Call)(){};
    template<class T> static T Resolve(HMODULE module, const char* name) { return reinterpret_cast<T>(GetProcAddress(module, name)); }
    bool Bind(HMODULE module) {
        if (!module) return false;
        Register = Resolve<decltype(Register)>(module, "?scriptRegister@@YAXPEAUHINSTANCE__@@P6AXXZ@Z");
        Unregister = Resolve<decltype(Unregister)>(module, "?scriptUnregister@@YAXPEAUHINSTANCE__@@@Z");
        Wait = Resolve<decltype(Wait)>(module, "?scriptWait@@YAXK@Z");
        KeyboardRegister = Resolve<decltype(KeyboardRegister)>(module, "?keyboardHandlerRegister@@YAXP6AXKGEHHHH@Z@Z");
        KeyboardUnregister = Resolve<decltype(KeyboardUnregister)>(module, "?keyboardHandlerUnregister@@YAXP6AXKGEHHHH@Z@Z");
        Init = Resolve<decltype(Init)>(module, "?nativeInit@@YAX_K@Z");
        Push = Resolve<decltype(Push)>(module, "?nativePush64@@YAX_K@Z");
        Call = Resolve<decltype(Call)>(module, "?nativeCall@@YAPEA_KXZ");
        return Register && Unregister && Wait && KeyboardRegister && KeyboardUnregister && Init && Push && Call;
    }
    template<class T> static std::uint64_t Bits(T value) {
        if constexpr (std::is_pointer_v<T>) return reinterpret_cast<std::uint64_t>(value);
        else if constexpr (std::is_floating_point_v<T>) { static_assert(sizeof(T) == 4); std::uint32_t bits{}; std::memcpy(&bits, &value, 4); return bits; }
        else return static_cast<std::uint64_t>(value);
    }
    template<class R = void, class... A> R Native(std::uint64_t hash, A... args) {
        Init(hash); (Push(Bits(args)), ...); std::uint64_t* result = Call();
        if constexpr (!std::is_void_v<R>) { R value{}; if (result) std::memcpy(&value, result, sizeof(R)); return value; }
    }
};
}
