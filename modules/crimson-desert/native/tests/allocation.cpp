#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <MinHook.h>
extern "C" {
#include "trampoline.h"
#include "buffer.h"
}
#include <algorithm>
#include <array>
#include <cassert>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>

// Every reservation and instruction is owned by this fixture process. No game
// process or game executable is opened. Only safe fixture functions execute.
constexpr std::size_t GiB=0x40000000,Granularity=0x10000;
static_assert(GiB==MH_MIN_BUFFER_RANGE && MH_MAX_BUFFER_RANGE==2*GiB-Granularity);
struct Envelope {
    unsigned char* base{};
    std::size_t size=4*GiB+Granularity;
    std::vector<void*> segments;
    explicit Envelope(std::vector<std::size_t> holes) {
        base=static_cast<unsigned char*>(VirtualAlloc(nullptr,size,MEM_RESERVE,PAGE_NOACCESS));
        assert(base && VirtualFree(base,0,MEM_RELEASE));
        std::sort(holes.begin(),holes.end());
        std::size_t start{};
        for(auto hole:holes) {
            assert(hole>=start && hole+Granularity<=size);
            Reserve(start,hole-start); start=hole+Granularity;
        }
        Reserve(start,size-start);
    }
    void Reserve(std::size_t offset,std::size_t length) {
        if(!length) return;
        void* result=VirtualAlloc(base+offset,length,MEM_RESERVE,PAGE_NOACCESS);
        assert(result==base+offset); segments.push_back(result);
    }
    unsigned char* Commit(std::size_t offset) {
        auto page=base+(offset&~std::size_t{4095});
        assert(VirtualAlloc(page,4096,MEM_COMMIT,PAGE_EXECUTE_READWRITE)==page);
        return base+offset;
    }
    ~Envelope() { for(void* segment:segments) assert(VirtualFree(segment,0,MEM_RELEASE)); }
};
__attribute__((noinline)) int Detour() { return 99; }
enum class Code { Constant,LocalRip,PositiveOverflowRip,NegativeOverflowRip };
void Case(const char* name,std::vector<std::size_t> holes,Code code,bool nearAvailable,bool expectAllocation) {
    Envelope memory(holes);
    auto target=memory.Commit(2*GiB);
    std::array<unsigned char,16> bytes{{0xB8,7,0,0,0,0xC3,0x90,0x90}};
    const bool overflow=code==Code::PositiveOverflowRip || code==Code::NegativeOverflowRip;
    if(code!=Code::Constant) {
        bytes={0x8B,0x05,0,0,0,0,0xC3,0x90};
        const std::int32_t displacement=code==Code::PositiveOverflowRip ? 0x70000000 :
            code==Code::NegativeOverflowRip ? -0x70000000 : 0x40-6;
        std::memcpy(bytes.data()+2,&displacement,4);
        const int value=42; std::memcpy(target+0x40,&value,4);
    }
    std::memcpy(target,bytes.data(),bytes.size()); FlushInstructionCache(GetCurrentProcess(),target,bytes.size());
    assert(MH_Initialize()==MH_OK);
    void* original{};
    const auto created=MH_CreateHook(target,reinterpret_cast<void*>(&Detour),&original);
    const bool allocated=nearAvailable || expectAllocation;
    const auto expected=!allocated ? MH_ERROR_MEMORY_ALLOC : overflow ? MH_ERROR_UNSUPPORTED_FUNCTION : MH_OK;
    assert(created==expected && std::memcmp(target,bytes.data(),bytes.size())==0);
    std::intptr_t distance{};
    if(created==MH_OK) {
        distance=reinterpret_cast<std::intptr_t>(original)-reinterpret_cast<std::intptr_t>(target);
        if(nearAvailable) assert(distance>-static_cast<std::intptr_t>(GiB) && distance<static_cast<std::intptr_t>(GiB));
        else assert(distance<-static_cast<std::intptr_t>(GiB) || distance>static_cast<std::intptr_t>(GiB));
        if(!overflow) {
            using Function=int(*)();
            Function volatile trampoline=reinterpret_cast<Function>(original);
            Function volatile invoke=reinterpret_cast<Function>(target);
            assert(trampoline()==(code==Code::Constant ? 7 : 42));
            assert(MH_EnableHook(target)==MH_OK && invoke()==99);
            assert(MH_DisableHook(target)==MH_OK && invoke()==(code==Code::Constant ? 7 : 42));
            assert(std::memcmp(target,bytes.data(),bytes.size())==0);
        }
        assert(MH_RemoveHook(target)==MH_OK);
    } else assert(original==nullptr);
    assert(MH_Uninitialize()==MH_OK);
    std::printf("{\"case\":\"%s\",\"status\":\"%s\",\"trampoline_delta\":%lld,\"target_unchanged\":true,\"pass\":true}\n",
        name,MH_StatusToString(created),static_cast<long long>(distance));
}
void ExistingBlock(bool nearHole) {
    std::vector<std::size_t> holes{GiB/2};
    if(nearHole) holes.push_back(3*GiB/2);
    Envelope memory(holes);
    auto first=memory.Commit(3*GiB/4);
    auto second=memory.Commit(2*GiB);
    const unsigned char code[]{0xB8,7,0,0,0,0xC3};
    std::memcpy(first,code,sizeof(code)); std::memcpy(second,code,sizeof(code));
    FlushInstructionCache(GetCurrentProcess(),first,sizeof(code));
    FlushInstructionCache(GetCurrentProcess(),second,sizeof(code));
    assert(MH_Initialize()==MH_OK);
    void* originalFirst{}; void* originalSecond{};
    assert(MH_CreateHook(first,reinterpret_cast<void*>(&Detour),&originalFirst)==MH_OK);
    const auto firstPage=reinterpret_cast<std::uintptr_t>(originalFirst)&~std::uintptr_t{4095};
    assert(firstPage==reinterpret_cast<std::uintptr_t>(memory.base+GiB/2));
    assert(MH_CreateHook(second,reinterpret_cast<void*>(&Detour),&originalSecond)==MH_OK);
    const auto secondPage=reinterpret_cast<std::uintptr_t>(originalSecond)&~std::uintptr_t{4095};
    if(nearHole) {
        assert(secondPage!=firstPage);
        assert(secondPage==reinterpret_cast<std::uintptr_t>(memory.base+3*GiB/2));
    } else assert(secondPage==firstPage); // Reuses far block even though its page is no longer free.
    assert(MH_EnableHook(first)==MH_OK && MH_EnableHook(second)==MH_OK);
    using Function=int(*)();
    Function volatile callFirst=reinterpret_cast<Function>(first),callSecond=reinterpret_cast<Function>(second);
    Function volatile trampolineFirst=reinterpret_cast<Function>(originalFirst),trampolineSecond=reinterpret_cast<Function>(originalSecond);
    assert(callFirst()==99 && callSecond()==99 && trampolineFirst()==7 && trampolineSecond()==7);
    assert(MH_DisableHook(second)==MH_OK && MH_RemoveHook(second)==MH_OK);
    assert(MH_DisableHook(first)==MH_OK && MH_RemoveHook(first)==MH_OK);
    assert(std::memcmp(first,code,sizeof(code))==0 && std::memcmp(second,code,sizeof(code))==0);
    assert(MH_Uninitialize()==MH_OK);
    std::printf("{\"case\":\"%s\",\"same_buffer_page\":%s,\"original_and_detour_executed\":true,\"pass\":true}\n",
        nearHole?"near_allocation_before_far_block_reuse":"existing_far_block_reuse",secondPage==firstPage?"true":"false");
}
void FarRelayGuard(bool below,bool hotPatch) {
    Envelope memory({});
    auto target=memory.Commit(below ? 0x90000000+16 : Granularity+16);
    auto trampoline=memory.Commit(below ? Granularity : 0x90000000);
    std::array<unsigned char,8> code{{0xB8,7,0,0,0,0xC3}};
    if(hotPatch) { code={0xC3,0x90,0x11,0x22,0x33,0x44,0x55,0x66}; std::memset(target-5,0x90,5); }
    std::memcpy(target,code.data(),code.size());
    TRAMPOLINE ct{}; ct.pTarget=target; ct.pDetour=reinterpret_cast<void*>(&Detour); ct.pTrampoline=trampoline;
    const bool created=CreateTrampolineFunction(&ct)!=FALSE;
    assert(!created && ct.patchAbove==hotPatch);
    assert(std::memcmp(target,code.data(),code.size())==0);
    std::printf("{\"case\":\"relay_beyond_signed32_rejected\",\"below\":%s,\"hot_patch\":%s,\"created\":false,\"target_unchanged\":true,\"pass\":true}\n",
        below?"true":"false",hotPatch?"true":"false");
}
int main() {
    // Target at2GiB into envelope. One far hole is1.5GiB below/above target.
    const std::size_t farBelow=GiB/2,farAbove=7*GiB/2,nearBelow=3*GiB/2;
    Case("old_1gib_blocked_outer_reachable",{farBelow},Code::Constant,false,true);
    Case("outer_reachable_above",{farAbove},Code::Constant,false,true);
    Case("near_band_preferred",{farBelow,nearBelow},Code::Constant,true,true);
    Case("true_2gib_exhaustion",{},Code::Constant,false,false);
    Case("far_trampoline_local_rip_valid",{farBelow},Code::LocalRip,false,true);
    Case("near_trampoline_local_rip_valid",{nearBelow},Code::LocalRip,true,true);
    Case("positive_rip_overflow_rejected",{farBelow},Code::PositiveOverflowRip,false,true);
    Case("negative_rip_overflow_rejected",{farAbove},Code::NegativeOverflowRip,false,true);
    ExistingBlock(false); ExistingBlock(true);
    FarRelayGuard(false,false); FarRelayGuard(true,false);
    FarRelayGuard(false,true); FarRelayGuard(true,true);
}
