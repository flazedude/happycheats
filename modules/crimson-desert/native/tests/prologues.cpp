#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <MinHook.h>
#include "../vendor/minhook/src/buffer.h"
#include <cstdio>
#include <cstring>
#include <array>
#include <cstdint>

// All reservations, commits and hooks belong to this disposable process.
// No game process is located, opened, loaded or modified. Captured game
// instructions are never executed: the only invocation is AFTER enabling a
// detour which returns a fixture constant. The trampoline is never called.
__attribute__((noinline)) int FixtureDetour() { return 8675309; }
struct Fixture { const char* name; std::uintptr_t rva; std::size_t count; std::array<unsigned char,33> bytes; };
constexpr Fixture fixtures[]{
    {"commit",0xb8e0190,17,{0x66,0x44,0x89,0x4C,0x24,0x20,0x48,0x89,0x54,0x24,0x10,0x53,0x55,0x56,0x57,0x41,0x56}},
    {"damage",0x18e1930,15,{0x48,0x89,0x5C,0x24,0x08,0x48,0x89,0x6C,0x24,0x10,0x48,0x89,0x74,0x24,0x18}},
    {"loco",0x396dcd0,15,{0x48,0x8B,0xC4,0x48,0x89,0x58,0x10,0x44,0x88,0x48,0x20,0x48,0x89,0x48,0x08}},
    {"marker",0xc97330,14,{0x48,0x89,0x5C,0x24,0x08,0x48,0x89,0x74,0x24,0x18,0x89,0x54,0x24,0x10}},
    {"outer_movement",0x3977060,29,{0x48,0x8B,0xC4,0x55,0x53,0x56,0x57,0x41,0x54,0x41,0x55,0x41,0x56,0x41,0x57,0x48,0x8D,0xA8,0x08,0xF9,0xFF,0xFF,0x48,0x81,0xEC,0xB8,0x07,0x00,0x00}}
};
bool Run(const char* mode,std::size_t range,bool expectExhaustion) {
    // RESERVE consumes virtual address space, not physical RAM. Test the exact
    // game prologues normally, with the old 1 GiB search occupied (fallback
    // must succeed), and with the entire conservative rel32 reach occupied.
    constexpr std::size_t imageSize=341544960,padding=0x10000;
    const auto size=imageSize+(range ? 2*range+padding : 0);
    auto reservation=static_cast<unsigned char*>(VirtualAlloc(nullptr,size,MEM_RESERVE,PAGE_NOACCESS));
    if(!reservation) {
        std::printf("{\"mode\":\"%s\",\"reserve_error\":%lu}\n",mode,GetLastError());
        return false;
    }
    auto image=reservation+range;
    const auto initialized=MH_Initialize();
    std::printf("{\"mode\":\"%s\",\"initialize\":\"%s\",\"reserved_bytes\":%zu,\"image_base\":\"%p\"}\n",
        mode,MH_StatusToString(initialized),size,static_cast<void*>(image));
    if(initialized!=MH_OK) { VirtualFree(reservation,0,MEM_RELEASE); return false; }
    bool success=true;
    for(const auto& f:fixtures) {
        auto target=image+f.rva;
        auto page=reinterpret_cast<void*>(reinterpret_cast<std::uintptr_t>(target)&~std::uintptr_t(0xFFF));
        if(!VirtualAlloc(page,4096,MEM_COMMIT,PAGE_READWRITE)) { success=false; break; }
        std::memset(page,0xCC,4096);
        std::memcpy(target,f.bytes.data(),f.count);
        DWORD previous{};
        if(!VirtualProtect(page,4096,PAGE_EXECUTE_READ,&previous)) { success=false; break; }
        FlushInstructionCache(GetCurrentProcess(),page,4096);
        void* original{};
        SetLastError(0);
        const auto created=MH_CreateHook(target,reinterpret_cast<void*>(&FixtureDetour),&original);
        const DWORD createLastError=GetLastError();
        const bool unchanged=std::memcmp(target,f.bytes.data(),f.count)==0;
        MH_STATUS enabled=MH_UNKNOWN,disabled=MH_UNKNOWN,removed=MH_UNKNOWN;
        bool redirected=false,restored=false;
        if(created==MH_OK) {
            enabled=MH_EnableHook(target);
            if(enabled==MH_OK) {
                using Call=int(*)();
                Call volatile invoke=reinterpret_cast<Call>(target);
                redirected=invoke()==8675309;
                disabled=MH_DisableHook(target);
                restored=std::memcmp(target,f.bytes.data(),f.count)==0;
            }
            removed=MH_RemoveHook(target);
        }
        std::printf("{\"mode\":\"%s\",\"hook\":\"%s\",\"target\":\"%p\",\"create\":\"%s\",\"create_code\":%d,\"last_error\":%lu,\"trampoline\":\"%p\",\"unchanged_on_create\":%s,\"enable\":\"%s\",\"redirected\":%s,\"disable\":\"%s\",\"restored\":%s,\"remove\":\"%s\"}\n",
            mode,f.name,static_cast<void*>(target),MH_StatusToString(created),static_cast<int>(created),createLastError,
            original,unchanged?"true":"false",MH_StatusToString(enabled),redirected?"true":"false",MH_StatusToString(disabled),
            restored?"true":"false",MH_StatusToString(removed));
        success=success && unchanged && (expectExhaustion ? created==MH_ERROR_MEMORY_ALLOC && !original :
            created==MH_OK && original && enabled==MH_OK && redirected && disabled==MH_OK && restored && removed==MH_OK);
    }
    const auto uninitialized=MH_Uninitialize();
    const bool freed=VirtualFree(reservation,0,MEM_RELEASE)!=0;
    return success && uninitialized==MH_OK && freed;
}
int main() {
    const bool normal=Run("normal",0,false);
    const bool fallback=Run("near_exhausted",MH_MIN_BUFFER_RANGE,false);
    const bool exhausted=Run("fully_exhausted",MH_MAX_BUFFER_RANGE,true);
    std::printf("{\"normal_pass\":%s,\"fallback_pass\":%s,\"full_exhaustion_pass\":%s}\n",normal?"true":"false",fallback?"true":"false",exhausted?"true":"false");
    return normal && fallback && exhausted ? 0 : 1;
}
