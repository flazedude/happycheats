// Experimental Enhanced Story Mode runtime. GPL-2.0.
// Interface layouts/hash data derived from YimMenuV2 enhanced commit
// 39a0f227782304d7fb3a5f7d3cdaec36a4d348f3. See PROVENANCE.md.
#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <array>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <functional>
#include <string>
#include <type_traits>
#include <vector>

namespace happygta::source {
struct Mapping { std::uint64_t original, mapped; const char* name; };
extern const std::array<Mapping, 41> Mappings;

struct alignas(16) CallContext {
    void* result{};                       // 0x00
    std::uint32_t count{}, pad0{};        // 0x08
    void* arguments{};                    // 0x10
    std::uint32_t vectorCount{}, pad1{};  // 0x18
    std::uint64_t vectorTargets[4]{};     // 0x20
    std::uint8_t vectorSources[0x40]{};   // 0x40
};
static_assert(sizeof(CallContext) == 0x80);
static_assert(alignof(CallContext) == 16);
static_assert(offsetof(CallContext, arguments) == 0x10);
static_assert(offsetof(CallContext, vectorCount) == 0x18);
static_assert(offsetof(CallContext, vectorTargets) == 0x20);
static_assert(offsetof(CallContext, vectorSources) == 0x40);
struct Program {
    std::uint8_t before[0x2C]{};
    std::uint32_t nativeCount{};
    std::uint8_t between[0x10]{};
    void** nativeEntries{};
    std::uint8_t after[0x38]{};
};
static_assert(sizeof(Program) == 0x80);
static_assert(offsetof(Program, nativeEntries) == 0x40);

// Validates a mapped PE image, not raw on-disk sections. No mutation, injection,
// unpacking or process opening is performed by this resolver.
class Image {
public:
    struct Section { std::uint32_t rva{}, size{}, flags{}; };
    bool Parse(void* base, std::size_t suppliedSize, std::string& error);
    std::vector<std::uintptr_t> Find(const char* signature) const;
    bool Contains(std::uintptr_t address, std::size_t length = 1) const;
    bool Executable(std::uintptr_t address) const;
    bool Writable(std::uintptr_t address, std::size_t length = 1) const;
    bool FunctionStart(std::uintptr_t address) const;
    std::uintptr_t Rip(std::uintptr_t displacement) const;
    std::uintptr_t Base() const { return base_; }
private:
    std::uintptr_t base_{};
    std::size_t size_{};
    std::vector<Section> sections_;
    std::uint32_t exceptionRva_{}, exceptionSize_{};
};
struct Resolved {
    std::uintptr_t version{}, threads{}, initializer{}, scheduler{}, sessionA{}, sessionB{};
};
bool Resolve(const Image& image, Resolved& result, std::string& error);
bool MemoryAccessible(std::uintptr_t address, std::size_t size, bool writable = false);

class Runtime {
public:
    using Tick = void (*)(void*);
    using Diagnostic = void (*)(void*, const char*);
    // Caller runs this outside DllMain after module load, and keeps this object
    // and its ASI alive for the process lifetime. A false result installs no hook.
    bool Initialize(HMODULE game, Tick tick, Diagnostic diagnostic, void* context);
    bool Ready() const { return ready_.load() && !threadViolation_.load() && !failed_.load() && !online_.load(); }
    bool OnlineLatched() const { return online_.load(); }
    const std::string& Error() const { return error_; }
    template<class T> static std::uint64_t Bits(T value) {
        if constexpr (std::is_pointer_v<T>) return reinterpret_cast<std::uint64_t>(value);
        else if constexpr (std::is_floating_point_v<T>) { static_assert(sizeof(T) == 4); std::uint32_t bits{}; std::memcpy(&bits, &value, 4); return bits; }
        else return static_cast<std::uint64_t>(value);
    }
    template<class R = void, class... A> R Native(std::uint64_t hash, A... args) {
        static_assert(sizeof...(A) <= 40);
        std::array<std::uint64_t, sizeof...(A)> arguments{Bits(args)...};
        auto result = Call(hash, arguments.data(), arguments.size());
        if constexpr (!std::is_void_v<R>) { R value{}; std::memcpy(&value, &result, sizeof(R)); return value; }
    }
private:
    static bool Scheduler(int budget);
    void Frame();
    bool SessionSafe();
    bool CacheNatives();
    void Fail(const char* reason);
    void Inactive(const char* reason);
    void Report(const char* reason) noexcept;
    std::uint64_t Call(std::uint64_t hash, const std::uint64_t* args, std::size_t count);
    Image image_;
    Resolved resolved_;
    std::array<void*, 41> handlers_{};
    bool (*original_)(int){};
    Tick tick_{};
    Diagnostic diagnostic_{};
    void* context_{};
    std::string error_;
    std::atomic<bool> active_{false}, ready_{false}, online_{false}, failed_{false};
    std::atomic<bool> threadViolation_{false};
    std::atomic<DWORD> callbackThread_{};
    bool inCallback_{};
    bool nativesCached_{};
    ULONGLONG lastInactive_{};
    static Runtime* instance_;
};
Runtime& CurrentRuntime();
bool Initialize(HMODULE, void (*)(), void (*)(const std::string&, bool));
bool OnlineLatched();
bool Ready();
const std::string& LastError();
#ifdef HC_SOURCE_TESTING
void SetFixtureTls(void* tls);
#endif
template<class R = void, class... A> R Native(std::uint64_t hash, A... args) {
    return CurrentRuntime().Native<R>(hash, args...);
}
}
