// Exercise the actual production diagnostic helpers. DllMain is merely an unused
// function in this executable; neither Worker nor InstallHooks is called.
// All hooks below belong to this isolated fixture, never to a game process.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <MinHook.h>
MH_STATUS WINAPI FixtureEnableHook(LPVOID target);
#define MH_EnableHook FixtureEnableHook
#undef NOMINMAX
#include "../trainer.cpp"
#undef MH_EnableHook
#include <cassert>

bool injectEnableFailure{};
MH_STATUS WINAPI FixtureEnableHook(LPVOID target) {
    if(injectEnableFailure) { SetLastError(ERROR_ACCESS_DENIED); return MH_ERROR_MEMORY_PROTECT; }
    const auto result=MH_EnableHook(target);
    if(result==MH_OK) SetLastError(ERROR_NO_MORE_FILES); // Deliberately benign raw context; success must stay success.
    return result;
}

__attribute__((noinline)) int DiagnosticDetour() { return 321; }
int main() {
    SetLastError(1234);
    const auto cleared=CallHookStage([] { assert(GetLastError()==ERROR_SUCCESS); return MH_OK; });
    assert(cleared.attempted && cleared.status==MH_OK && cleared.win32Error==0);
    const auto failed=CallHookStage([] { SetLastError(ERROR_ACCESS_DENIED); return MH_ERROR_MEMORY_PROTECT; });
    assert(failed.status==MH_ERROR_MEMORY_PROTECT && failed.win32Error==ERROR_ACCESS_DENIED);
    const auto skipped=HookCallJson({});
    assert(!skipped["attempted"].get<bool>() && skipped["status"].is_null() && skipped["code"].is_null());
    const auto failedJson=HookCallJson(failed);
    assert(failedJson["status"]=="MH_ERROR_MEMORY_PROTECT" && failedJson["code"]==10 && failedJson["raw_win32_last_error"]==5);

    assert(MH_Initialize()==MH_OK);
    auto target=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
    assert(target);
    const unsigned char prologue[]{0x48,0x89,0x5C,0x24,0x08,0x48,0x89,0x6C,0x24,0x10,0x48,0x89,0x74,0x24,0x18};
    std::copy(std::begin(prologue),std::end(prologue),target);
    void* trampoline{};
    InstallOneHook(g_hookResults[0],target,reinterpret_cast<void*>(&DiagnosticDetour),&trampoline,g_commitReady);
    assert(!g_commitReady.load() && !g_hookResults[0].ready && !trampoline);
    assert(g_hookResults[0].create.attempted && g_hookResults[0].create.status==MH_ERROR_NOT_EXECUTABLE);
    assert(!g_hookResults[0].enable.attempted && std::string(g_hookResults[0].skippedReason)=="create_failed");
    assert(g_hookResults[0].failure.target.queried && g_hookResults[0].failure.target.protect==PAGE_READWRITE);
    assert(g_hookResults[0].failure.target.state==MEM_COMMIT && g_hookResults[0].failure.target.type==MEM_PRIVATE);
    assert(g_hookResults[0].failure.detour.queried && g_hookResults[0].failure.detour.type==MEM_IMAGE);
    assert(!g_hookResults[0].failure.nearby.attempted);

    DWORD previous{};
    assert(VirtualProtect(target,4096,PAGE_EXECUTE_READ,&previous));
    InstallOneHook(g_hookResults[1],target,reinterpret_cast<void*>(&DiagnosticDetour),&trampoline,g_damageReady);
    assert(g_damageReady.load() && g_hookResults[1].ready && trampoline);
    assert(g_hookResults[1].create.status==MH_OK && g_hookResults[1].enable.status==MH_OK);
    assert(g_hookResults[1].enable.win32Error==ERROR_NO_MORE_FILES && !g_hookResults[1].failure.captured);
    using Target=int(*)();
    Target volatile call=reinterpret_cast<Target>(target);
    assert(call()==321); // Executes only the installed diagnostic detour.
    assert(MH_DisableHook(target)==MH_OK && MH_RemoveHook(target)==MH_OK);
    // Fixture-local wrapper injects the API result through the real helper.
    // This is not a real OS denial and changes no mitigation policy.
    injectEnableFailure=true;
    trampoline=nullptr;
    InstallOneHook(g_hookResults[2],target,reinterpret_cast<void*>(&DiagnosticDetour),&trampoline,g_locoReady);
    assert(g_hookResults[2].create.status==MH_OK && g_hookResults[2].enable.status==MH_ERROR_MEMORY_PROTECT);
    assert(g_hookResults[2].enable.win32Error==ERROR_ACCESS_DENIED && !g_locoReady.load());
    assert(g_hookResults[2].failure.captured && g_hookResults[2].failure.target.protect==PAGE_EXECUTE_READ);
    assert(g_hookResults[2].failure.policyQueried && !g_hookResults[2].failure.nearby.attempted);
    assert(MH_RemoveHook(target)==MH_OK);
    injectEnableFailure=false;
    assert(VirtualFree(target,0,MEM_RELEASE));
    g_hookResults[3].skippedReason="movement_hook_not_ready";
    g_hookResults[4].skippedReason="outer_binding_unavailable";
    const auto results=HookResultsJson();
    assert(!results["stats"]["ready"].get<bool>() && results["damage"]["ready"].get<bool>());
    assert(!results["movement"]["ready"].get<bool>() && !results["marker"]["ready"].get<bool>());
    assert(results["stats"]["create"]["status"]=="MH_ERROR_NOT_EXECUTABLE");
    assert(results["stats"]["enable"]["status"].is_null());
    assert(results["movement"]["enable"]["raw_win32_last_error"]==ERROR_ACCESS_DENIED);
    assert(results["marker"]["skipped_reason"]=="movement_hook_not_ready");
    assert(!results["outer_movement"]["ready"].get<bool>() && results["outer_movement"]["skipped_reason"]=="outer_binding_unavailable");
    const auto summary=HookFailureSummary();
    assert(summary.find("stats create=MH_ERROR_NOT_EXECUTABLE (7)")!=std::string::npos);
    assert(summary.find("movement enable=MH_ERROR_MEMORY_PROTECT (10)")!=std::string::npos);
    assert(summary.find("marker skipped=movement_hook_not_ready")!=std::string::npos);
    assert(summary.find("outer_movement skipped=outer_binding_unavailable")!=std::string::npos);
    assert(summary.find("damage")==std::string::npos);
    assert(summary.find("Win32")==std::string::npos && summary.find("LastError")==std::string::npos);

    constexpr std::size_t range=0x40000000,size=2*range+0x10000;
    auto blocked=static_cast<unsigned char*>(VirtualAlloc(nullptr,size,MEM_RESERVE,PAGE_NOACCESS));
    assert(blocked);
    auto blockedTarget=blocked+range;
    assert(VirtualAlloc(blockedTarget,4096,MEM_COMMIT,PAGE_READWRITE));
    std::copy(std::begin(prologue),std::end(prologue),blockedTarget);
    assert(VirtualProtect(blockedTarget,4096,PAGE_EXECUTE_READ,&previous));
    HookResult exhausted{"allocation_fixture",0}; std::atomic<bool> exhaustedReady{};
    trampoline=nullptr;
    InstallOneHook(exhausted,blockedTarget,reinterpret_cast<void*>(&DiagnosticDetour),&trampoline,exhaustedReady);
    assert(exhausted.create.status==MH_ERROR_MEMORY_ALLOC && !exhaustedReady.load() && !trampoline);
    const auto space=exhausted.failure.nearby;
    assert(space.attempted && space.complete && space.freeCandidates==0 && space.freeRegions==0);
    assert(!space.nearestBelow && !space.nearestAbove);
    const auto limited=CaptureNearbySpace(reinterpret_cast<uintptr_t>(blockedTarget),1,50);
    assert(limited.attempted && !limited.complete && std::string(limited.stopReason)=="region_limit");
    const auto timed=CaptureNearbySpace(reinterpret_cast<uintptr_t>(blockedTarget),4096,0);
    assert(!timed.complete && timed.regionsScanned==0 && std::string(timed.stopReason)=="time_limit");
    assert(VirtualFree(blocked,0,MEM_RELEASE) && MH_Uninitialize()==MH_OK);
    std::printf("%s\n%s\nPASS native hook diagnostics: real create failure/success, cleared Win32 error, skipped stages and enable failure formatting.\n",
        results.dump(2).c_str(),summary.c_str());
    std::printf("%s\nPASS injected enable failure through InstallOneHook; read-only memory/policy evidence; actual allocation exhaustion and bounded scan truncation.\n",
        FailureEvidenceJson(exhausted.failure).dump(2).c_str());
}
