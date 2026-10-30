// HappyCheats Crimson Desert bridge. Game bindings derived from Trinity:
// Copyright (c) 2026 XeTrinityz, MIT; gugi97 commit e5b3303854e7383cfeb3489e7830995772c52ea9.
// See TRINITY-LICENSE.txt. No third-party trainer binary is used.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <bcrypt.h>
#include <MinHook.h>
#include "vendor/minhook/src/buffer.h"
#include <nlohmann/json.hpp>
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <limits>
#include <mutex>
#include <string>
#include <vector>
#include "trainer_logic.h"
#include "marker_policy.h"
#include "build_guard.h"

namespace {
using Json = nlohmann::json;
constexpr uintptr_t kManagerRva = 0x71AEF78;
constexpr uintptr_t kCommitRva = 0xB8E0190;
constexpr uintptr_t kDamageRva = 0x18E1930;
constexpr uintptr_t kLocoRva = 0x396DCD0;
constexpr uintptr_t kOuterMoveRva = 0x3977060;
constexpr uintptr_t kMarkerSenderRva = 0xC97330;
constexpr uintptr_t kMarkerUiReturnRva = 0xF6449E;
constexpr uintptr_t kOriginRva = 0x719ACB0;
constexpr uintptr_t kSyncProxyRva = 0x3C62A10;
constexpr uintptr_t kPointerFloor = 0x10000;
constexpr std::uint64_t kLeaseTicks = 5ULL * 10000000ULL;
const std::array<const char*, 8> kFeatures{{"crimson_health", "crimson_stamina", "crimson_one_hit",
    "crimson_spirit", "crimson_teleport", "crimson_speed", "crimson_items", "crimson_auto_teleport"}};
HMODULE g_module{};
uintptr_t g_game{};
std::filesystem::path g_bridge;
std::string g_session;
std::atomic<bool> g_health{}, g_stamina{}, g_spirit{}, g_oneHit{};
std::atomic<bool> g_speedEnabled{};
std::atomic<float> g_speed{1.0f};
std::atomic<bool> g_commitReady{}, g_damageReady{}, g_locoReady{}, g_playerReady{}, g_leaseValid{};
std::atomic<bool> g_markerReady{}, g_warpPending{};
std::atomic<bool> g_outerReady{};
std::atomic<bool> g_clientBindingReady{},g_clientReady{};
std::atomic<ULONGLONG> g_leaseDeadline{};
std::atomic<uintptr_t> g_owner{};
std::atomic<uintptr_t> g_actor{},g_marker{},g_root{},g_stats{};
std::atomic<uintptr_t> g_clientRoot{},g_clientService{},g_clientOwner{},g_clientSelfUser{},g_clientActor{},g_clientMarker{},g_clientVitalRoot{},g_clientStats{};
std::uint64_t g_processStartUnixMs{};
std::string g_lastCommand;
bool g_lastSuccess = true;
std::string g_lastMessage = "Waiting for a loaded player and the HappyCheats connection.";
std::string g_initMessage;
std::string g_initError;
std::uint32_t g_observedImageSize{};
struct MemoryEvidence {
    bool queried{};
    DWORD rawLastError{},state{},protect{},allocationProtect{},type{};
    uintptr_t address{},base{},allocationBase{};
    std::size_t regionSize{};
};
struct NearbyEvidence {
    bool attempted{},complete{};
    const char* stopReason="not_requested";
    DWORD rawLastError{};
    uintptr_t lower{},upper{},nearestBelow{},nearestAbove{};
    std::size_t regionsScanned{},freeRegions{},freeCandidates{};
};
struct FailureEvidence {
    bool captured{},policyQueried{};
    MemoryEvidence target{},detour{};
    DWORD policyFlags{},policyRawLastError{};
    NearbyEvidence nearby{};
};
MemoryEvidence CaptureMemory(uintptr_t address) noexcept {
    MemoryEvidence result{};
    result.address=address;
    MEMORY_BASIC_INFORMATION memory{};
    SetLastError(ERROR_SUCCESS);
    result.queried=VirtualQuery(reinterpret_cast<void*>(address),&memory,sizeof(memory))==sizeof(memory);
    result.rawLastError=GetLastError();
    if(result.queried) {
        result.base=reinterpret_cast<uintptr_t>(memory.BaseAddress);
        result.allocationBase=reinterpret_cast<uintptr_t>(memory.AllocationBase);
        result.regionSize=memory.RegionSize;
        result.state=memory.State; result.protect=memory.Protect;
        result.allocationProtect=memory.AllocationProtect; result.type=memory.Type;
    }
    return result;
}
NearbyEvidence CaptureNearbySpace(uintptr_t target,std::size_t maximumRegions=4096,ULONGLONG timeBudgetMs=50) noexcept {
    NearbyEvidence result{};
    result.attempted=true;
    SYSTEM_INFO system{}; GetSystemInfo(&system);
    const auto minimum=reinterpret_cast<uintptr_t>(system.lpMinimumApplicationAddress);
    const auto maximum=reinterpret_cast<uintptr_t>(system.lpMaximumApplicationAddress);
    constexpr uintptr_t range=MH_MAX_BUFFER_RANGE; // Match the allocator's full fallback reach; read-only evidence.
    result.lower=(std::max)(minimum,target>range ? target-range : minimum);
    result.upper=(std::min)(maximum,target>(std::numeric_limits<uintptr_t>::max)()-range ? maximum : target+range);
    const auto granularity=static_cast<uintptr_t>(system.dwAllocationGranularity);
    const auto page=static_cast<uintptr_t>(system.dwPageSize);
    auto current=result.lower;
    const auto started=GetTickCount64();
    while(current<=result.upper) {
        if(result.regionsScanned>=maximumRegions) { result.stopReason="region_limit"; return result; }
        if(GetTickCount64()-started>=timeBudgetMs) { result.stopReason="time_limit"; return result; }
        const auto memory=CaptureMemory(current);
        ++result.regionsScanned;
        if(!memory.queried) { result.stopReason="query_failed"; result.rawLastError=memory.rawLastError; return result; }
        if(memory.regionSize==0 || memory.base>(std::numeric_limits<uintptr_t>::max)()-memory.regionSize) {
            result.stopReason="invalid_region"; return result;
        }
        const auto next=memory.base+memory.regionSize;
        if(next<=current) { result.stopReason="nonadvancing_region"; return result; }
        if(memory.state==MEM_FREE) {
            const auto start=(std::max)(result.lower,memory.base);
            const auto end=(std::min)(result.upper+1,next);
            const auto first=((start+granularity-1)/granularity)*granularity;
            if(end>=page) {
                const auto last=((end-page)/granularity)*granularity;
                if(first<=last) {
                    ++result.freeRegions;
                    result.freeCandidates+=(last-first)/granularity+1;
                    if(first<target) {
                        const auto below=(std::min)(last,((target-1)/granularity)*granularity);
                        if(below>=first) result.nearestBelow=(std::max)(result.nearestBelow,below);
                    }
                    if(last>target) {
                        const auto above=(std::max)(first,((target+granularity)/granularity)*granularity);
                        if(above<=last && (!result.nearestAbove || above<result.nearestAbove)) result.nearestAbove=above;
                    }
                }
            }
        }
        current=next;
    }
    result.complete=true; result.stopReason="completed";
    return result;
}
FailureEvidence CaptureHookFailure(void* target,void* detour,bool inspectNearby) noexcept {
    FailureEvidence result{};
    result.captured=true;
    result.target=CaptureMemory(reinterpret_cast<uintptr_t>(target));
    result.detour=CaptureMemory(reinterpret_cast<uintptr_t>(detour));
    PROCESS_MITIGATION_DYNAMIC_CODE_POLICY policy{};
    using QueryPolicy=BOOL(WINAPI*)(HANDLE,PROCESS_MITIGATION_POLICY,PVOID,SIZE_T);
    const auto address=GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"GetProcessMitigationPolicy");
    QueryPolicy query{};
    static_assert(sizeof(query)==sizeof(address));
    std::memcpy(&query,&address,sizeof(query));
    if(query) {
        SetLastError(ERROR_SUCCESS);
        result.policyQueried=query(GetCurrentProcess(),ProcessDynamicCodePolicy,&policy,sizeof(policy))!=FALSE;
        result.policyRawLastError=GetLastError();
    } else result.policyRawLastError=ERROR_PROC_NOT_FOUND;
    if(result.policyQueried) result.policyFlags=policy.Flags;
    if(inspectNearby) result.nearby=CaptureNearbySpace(reinterpret_cast<uintptr_t>(target));
    return result;
}
struct HookCallResult {
    bool attempted{};
    MH_STATUS status=MH_UNKNOWN;
    DWORD win32Error{};
};
struct HookResult {
    const char* name;
    uintptr_t rva;
    HookCallResult create{},enable{};
    FailureEvidence failure{};
    bool ready{};
    const char* skippedReason="initialization_not_reached";
};
std::array<HookResult,5> g_hookResults{{{"stats",kCommitRva},{"damage",kDamageRva},
    {"movement",kLocoRva},{"marker",kMarkerSenderRva},{"outer_movement",kOuterMoveRva}}};
HookCallResult g_hookLibraryResult;

template<class Action> HookCallResult CallHookStage(Action action) noexcept {
    SetLastError(ERROR_SUCCESS);
    const auto status=action();
    const auto error=GetLastError();
    return {true,status,error};
}
void SkipAllHooks(const char* reason) {
    for(auto& hook:g_hookResults) hook.skippedReason=reason;
}
void InstallOneHook(HookResult& result,void* target,void* detour,void** original,std::atomic<bool>& ready) noexcept {
    result.create=CallHookStage([&] { return MH_CreateHook(target,detour,original); });
    if(result.create.status!=MH_OK) {
        result.skippedReason="create_failed";
        result.failure=CaptureHookFailure(target,detour,result.create.status==MH_ERROR_MEMORY_ALLOC);
        return;
    }
    result.enable=CallHookStage([&] { return MH_EnableHook(target); });
    if(result.enable.status!=MH_OK) {
        result.skippedReason="enable_failed";
        result.failure=CaptureHookFailure(target,detour,false);
        return;
    }
    result.ready=true;
    result.skippedReason="";
    ready.store(true);
}
std::string HookStageSummary(const HookCallResult& stage) {
    return std::string(MH_StatusToString(stage.status))+" ("+std::to_string(static_cast<int>(stage.status))+
        ")";
}
std::string HookFailureSummary() {
    std::string failures;
    for(const auto& hook:g_hookResults) {
        if(hook.ready) continue;
        if(!failures.empty()) failures+="; ";
        failures+=hook.name;
        if(hook.create.attempted && hook.create.status!=MH_OK) failures+=" create="+HookStageSummary(hook.create);
        else if(hook.enable.attempted && hook.enable.status!=MH_OK) failures+=" enable="+HookStageSummary(hook.enable);
        else failures+=" skipped="+std::string(hook.skippedReason);
    }
    return failures;
}
Json HookCallJson(const HookCallResult& call) {
    if(!call.attempted) return {{"attempted",false},{"status",nullptr},{"code",nullptr},{"raw_win32_last_error",nullptr}};
    return {{"attempted",true},{"status",MH_StatusToString(call.status)},
        {"code",static_cast<int>(call.status)},{"raw_win32_last_error",call.win32Error}};
}
std::string HexAddress(uintptr_t address) {
    char text[24]{};
    std::snprintf(text,sizeof(text),"0x%llX",static_cast<unsigned long long>(address));
    return text;
}
Json MemoryEvidenceJson(const MemoryEvidence& memory) {
    return {{"queried",memory.queried},{"raw_win32_last_error",memory.rawLastError},{"address",HexAddress(memory.address)},
        {"base_address",HexAddress(memory.base)},{"allocation_base",HexAddress(memory.allocationBase)},
        {"region_size",memory.regionSize},{"state",memory.state},{"protect",memory.protect},
        {"allocation_protect",memory.allocationProtect},{"type",memory.type}};
}
Json FailureEvidenceJson(const FailureEvidence& evidence) {
    if(!evidence.captured) return nullptr;
    const auto& space=evidence.nearby;
    return {{"target",MemoryEvidenceJson(evidence.target)},{"detour",MemoryEvidenceJson(evidence.detour)},
        {"dynamic_code_policy",{{"queried",evidence.policyQueried},{"flags",evidence.policyFlags},
            {"raw_win32_last_error",evidence.policyRawLastError}}},
        {"nearby_space",{{"attempted",space.attempted},{"scan_complete",space.complete},{"stop_reason",space.stopReason},
            {"lower_address",HexAddress(space.lower)},{"upper_address_inclusive",HexAddress(space.upper)},
            {"regions_scanned",space.regionsScanned},{"free_region_count",space.freeRegions},{"free_candidate_count",space.freeCandidates},
            {"nearest_below",space.nearestBelow ? Json(HexAddress(space.nearestBelow)) : Json(nullptr)},
            {"nearest_above",space.nearestAbove ? Json(HexAddress(space.nearestAbove)) : Json(nullptr)},
            {"raw_win32_last_error",space.rawLastError}}}};
}
Json HookResultsJson() {
    Json results=Json::object();
    const std::array<bool,5> ready{{g_commitReady.load(),g_damageReady.load(),g_locoReady.load(),g_markerReady.load(),g_outerReady.load()}};
    for(std::size_t index=0;index<g_hookResults.size();++index) {
        const auto& hook=g_hookResults[index];
        results[hook.name]={{"target_rva",hook.rva},{"ready",ready[index]},
            {"create",HookCallJson(hook.create)},{"enable",HookCallJson(hook.enable)},
            {"skipped_reason",hook.skippedReason},{"failure_snapshot",FailureEvidenceJson(hook.failure)}};
    }
    return results;
}
using Player = crimson_logic::Identity;
using Client = crimson_logic::ClientIdentity;
using Position = crimson_logic::Position;
struct WarpContext {
    Player player{};
    Client client{};
    uintptr_t component{},proxy{},nav{},sender{};
    Position origin{},local{};
};
std::mutex g_warpMutex;
crimson_logic::NewMarkerPolicy g_markerPolicy;
crimson_logic::MarkerWarpQueue g_warpQueue;
crimson_logic::MarkerAcknowledgement g_markerAck;
WarpContext g_warpContext;
std::uint64_t g_markerRevision{};
std::uint64_t g_warpGeneration{};
bool g_warpInFlight{};
enum class WarpObservation { Idle,Queued,Writing,AwaitingUpdate,ObservedNearMarker,NotRetained,Canceled };
WarpObservation g_warpObservation=WarpObservation::Idle;
Position g_warpRequested{},g_warpObserved{};
bool g_hasWarpRequested{},g_hasWarpObserved{};
unsigned g_warpWrites{};
const char* ObservationName(WarpObservation state) {
    switch(state) {
    case WarpObservation::Queued: return "queued";
    case WarpObservation::Writing: return "writing";
    case WarpObservation::AwaitingUpdate: return "awaiting_outer_update";
    case WarpObservation::ObservedNearMarker: return "observed_near_marker";
    case WarpObservation::NotRetained: return "not_retained";
    case WarpObservation::Canceled: return "canceled";
    default: return "idle";
    }
}
std::string g_warpMessage="Automatic marker teleport is off. Terrain height is not checked.";
void CancelWarpLocked(const char* message) {
    ++g_warpGeneration;
    g_warpQueue.Cancel();
    g_warpObservation=WarpObservation::Canceled;
    g_warpPending.store(false,std::memory_order_release);
    g_warpMessage=message;
}
void AbortWarpNoThrow(bool clearInFlight=false) noexcept {
    g_warpPending.store(false,std::memory_order_release);
    try {
        std::lock_guard<std::mutex> lock(g_warpMutex);
        ++g_warpGeneration; g_warpObservation=WarpObservation::Canceled;
        g_warpQueue.Cancel(); g_markerPolicy.Disable(g_markerRevision);
        if(clearInFlight) g_warpInFlight=false;
    } catch(...) { /* A failed diagnostic allocation must never escape a game hook. */ }
}

bool ReadBytes(uintptr_t address, void* out, std::size_t count) {
    SIZE_T read{};
    return address >= kPointerFloor && count != 0 &&
        ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(address), out, count, &read) && read == count;
}
template<class T> bool Read(uintptr_t address, T& value) { return ReadBytes(address, &value, sizeof value); }
template<class T> bool Write(uintptr_t address, const T& value) {
    SIZE_T written{};
    return address >= kPointerFloor && WriteProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(address),
        &value, sizeof value, &written) && written == sizeof value;
}
bool Pointer(uintptr_t address, uintptr_t& value) {
    return Read(address, value) && value >= kPointerFloor && value <= 0x00007FFFFFFFFFFFULL;
}
bool StatusKey(std::uint16_t reference,std::uint32_t& key) {
    return crimson_logic::ResolveStatusKey(g_game+crimson_logic::StatusRegistryRva,reference,Pointer,Read<std::uint32_t>,key);
}
void DisableAll() {
    g_health.store(false); g_stamina.store(false); g_spirit.store(false); g_oneHit.store(false);
    g_speedEnabled.store(false); g_speed.store(1.0f);
    std::lock_guard<std::mutex> lock(g_warpMutex);
    g_markerPolicy.InvalidateContext(g_markerRevision);
    CancelWarpLocked("Marker teleport disabled; any pending request was canceled.");
}

// A cached owner is never enough to authorize a write: recheck the engine's
// possession roundtrip and the entire vital chain at each hook invocation.
bool ValidatePlayer(uintptr_t owner, Player& player) {
    uintptr_t descriptor{}, possessor{}, pawn{}, actor{}, marker{}, root{}, stats{}, backlink{};
    std::uint8_t tag{};
    std::uint16_t type{};
    std::uint32_t key{},statCount{};
    if (!owner || !Pointer(owner + 0x88, descriptor) || !Read(descriptor + 1, tag) ||
        !Pointer(owner + 0xA0, possessor) ||
        !Pointer(possessor + 0xD0, pawn) || !crimson_logic::IsControlled(owner,pawn,tag) ||
        !Pointer(owner + 0x68, actor) || !Pointer(actor + 0x20, marker) ||
        !Pointer(marker + 0x18, root) || !Pointer(root, backlink) || backlink != marker ||
        !Pointer(root + 0x58, stats) || !Read(root+0x60,statCount) || !statCount || statCount>0xFFFF ||
        !Read(stats, type) || !StatusKey(type,key) || key!=crimson_logic::HealthKey) return false;
    player = {owner, actor, marker, root, stats};
    return true;
}
bool ResolvePlayer(Player& player) {
    uintptr_t holder{}, manager{}, data{}, candidate{}, found{};
    std::uint32_t count{};
    if (!Pointer(g_game + kManagerRva, holder) || !Pointer(holder, manager) ||
        !Pointer(manager + 0xB8, data) || !Read(manager + 0xC0, count) || count == 0 || count > 8192) return false;
    for (std::uint32_t i = 0; i < count; ++i) {
        Player current{};
        if (!Pointer(data + sizeof(uintptr_t) * i, candidate) || !ValidatePlayer(candidate, current)) continue;
        if (found && found != candidate) return false; // Ambiguity is never permission to choose the first NPC.
        found = candidate;
        player = current;
    }
    return found != 0;
}
bool CurrentPlayer(Player& player) {
    if (!g_leaseValid.load(std::memory_order_acquire) || GetTickCount64() >= g_leaseDeadline.load()) return false;
    const auto owner=g_owner.load(std::memory_order_acquire);
    if (!ValidatePlayer(owner,player)) return false;
    // A reused owner address with a rebuilt vital chain must not carry toggles
    // into a new body before the next worker refresh observes the transition.
    const Player expected{owner,g_actor.load(),g_marker.load(),g_root.load(),g_stats.load()};
    return g_owner.load(std::memory_order_acquire)==owner && crimson_logic::SameIdentity(expected,player);
}
bool ResolveClientPlayer(Client& client) {
    // This is the world-map's current client-owner accessor, not either server
    // character-manager global. Normal/ChildOnly and the engine local-self
    // predicate are pinned to PE3424; a different class or self-user is rejected.
    return g_clientBindingReady.load() && crimson_logic::ResolveControlledClientAnchor(g_game+crimson_logic::ClientRootRva,
        g_game+crimson_logic::ClientOwnerVtableRva,Pointer,Read<std::uint8_t>,client.anchor,
        g_game+crimson_logic::ClientChildOwnerVtableRva) &&
        ValidatePlayer(client.anchor.owner,client.player);
}
bool CurrentClientPlayer(Client& client) {
    if(!g_leaseValid.load(std::memory_order_acquire) || GetTickCount64()>=g_leaseDeadline.load()) return false;
    const auto owner=g_clientOwner.load(std::memory_order_acquire);
    const Client expected{{g_clientRoot.load(),g_clientService.load(),owner,g_clientSelfUser.load()},
        {owner,g_clientActor.load(),g_clientMarker.load(),g_clientVitalRoot.load(),g_clientStats.load()}};
    return ResolveClientPlayer(client) && g_clientOwner.load(std::memory_order_acquire)==owner &&
        crimson_logic::SameClientIdentity(expected,client);
}
struct IdentityPublication {
    bool player{},client{};
};
IdentityPublication RefreshIdentityCache(bool lease,bool playerReady,const Player& player,
                                        bool clientReady,const Client& client) {
    // Only the worker publishes these caches. A stable identity must not pass
    // through owner==0: a concurrent commit would otherwise miss its refill.
    const Player cached{g_owner.load(),g_actor.load(),g_marker.load(),g_root.load(),g_stats.load()};
    const Client cachedClient{{g_clientRoot.load(),g_clientService.load(),g_clientOwner.load(),g_clientSelfUser.load()},
        {g_clientOwner.load(),g_clientActor.load(),g_clientMarker.load(),g_clientVitalRoot.load(),g_clientStats.load()}};
    const bool publishPlayer=playerReady ? !crimson_logic::SameIdentity(cached,player) :
        cached.owner || cached.actor || cached.marker || cached.root || cached.stats;
    const bool publishClient=clientReady ? !crimson_logic::SameClientIdentity(cachedClient,client) :
        cachedClient.anchor.root || cachedClient.anchor.service || cachedClient.anchor.owner || cachedClient.anchor.selfUser ||
        cachedClient.player.actor || cachedClient.player.marker || cachedClient.player.root || cachedClient.player.stats;
    const bool playerChanged=publishPlayer || g_playerReady.load()!=playerReady;
    const bool clientChanged=publishClient || g_clientReady.load()!=clientReady;
    if(!lease || !playerReady || playerChanged || clientChanged) DisableAll();
    if(publishPlayer) {
        // Genuine transitions still invalidate the cache before any field changes.
        g_owner.store(0,std::memory_order_release);
        g_actor.store(playerReady ? player.actor : 0);
        g_marker.store(playerReady ? player.marker : 0);
        g_root.store(playerReady ? player.root : 0);
        g_stats.store(playerReady ? player.stats : 0);
        g_owner.store(playerReady ? player.owner : 0,std::memory_order_release);
    }
    if(publishClient) {
        g_clientOwner.store(0,std::memory_order_release);
        g_clientRoot.store(clientReady ? client.anchor.root : 0);
        g_clientService.store(clientReady ? client.anchor.service : 0);
        g_clientSelfUser.store(clientReady ? client.anchor.selfUser : 0);
        g_clientActor.store(clientReady ? client.player.actor : 0);
        g_clientMarker.store(clientReady ? client.player.marker : 0);
        g_clientVitalRoot.store(clientReady ? client.player.root : 0);
        g_clientStats.store(clientReady ? client.player.stats : 0);
        g_clientOwner.store(clientReady ? client.player.owner : 0,std::memory_order_release);
    }
    // Readiness may recover after an exception without republishing stable pointers.
    g_clientReady.store(clientReady,std::memory_order_release);
    g_playerReady.store(playerReady,std::memory_order_release);
    return {publishPlayer,publishClient};
}
bool MovementComponent(const Player& player, uintptr_t& component) {
    // Inspected PE3424 CLIENT caller paths establish owner+68 -> actor+40 -> object+140 -> component.
    // A server owner has a different control component at actor+40 and must never enter this chain.
    // The component's proxy is +2C0 on this build; no arbitrary pointer scans are used.
    uintptr_t proxy{};
    return crimson_logic::ResolveMovementComponent(player.actor,Pointer,component,proxy);
}
bool WarpSnapshot(WarpContext& context) {
    uintptr_t handle{};
    Position originAgain{};
    Player fresh{};
    Client freshClient{};
    uintptr_t componentAgain{},proxyAgain{},navAgain{},senderAgain{};
    return CurrentPlayer(context.player) && CurrentClientPlayer(context.client) && MovementComponent(context.client.player,context.component) &&
        Pointer(context.component+0x2C0,context.proxy) && Pointer(context.client.player.actor+0x168,context.nav) &&
        Pointer(context.nav+8,context.sender) && context.sender==context.client.player.owner &&
        Read(context.proxy+0x38,handle) && handle!=~uintptr_t{} &&
        Read(g_game+kOriginRva,context.origin) && Read(context.proxy+0x90,context.local) &&
        crimson_logic::FinitePosition(context.origin) && crimson_logic::FinitePosition(context.local) &&
        Read(g_game+kOriginRva,originAgain) && crimson_logic::SamePosition(context.origin,originAgain) &&
        CurrentPlayer(fresh) && crimson_logic::SameIdentity(context.player,fresh) &&
        CurrentClientPlayer(freshClient) && crimson_logic::SameClientIdentity(context.client,freshClient) &&
        MovementComponent(freshClient.player,componentAgain) && componentAgain==context.component &&
        Pointer(componentAgain+0x2C0,proxyAgain) && proxyAgain==context.proxy &&
        Pointer(freshClient.player.actor+0x168,navAgain) && navAgain==context.nav &&
        Pointer(navAgain+8,senderAgain) && senderAgain==context.sender;
}
bool SameWarpContext(const WarpContext& a,const WarpContext& b) {
    return crimson_logic::SameIdentity(a.player,b.player) && a.component==b.component &&
        crimson_logic::SameClientIdentity(a.client,b.client) &&
        a.proxy==b.proxy && a.nav==b.nav && a.sender==b.sender;
}
bool QueueWarpLocked(crimson_logic::MarkerWarpRequest request,const WarpContext& context,
                     const Position& markerBaseline,std::uint64_t now) {
    // A free-map pin has Y=0, not terrain elevation. Freeze current WORLD Y
    // once per event; repeated hold ticks must never accumulate a height offset.
    const Position marker=request.world;
    Position frozen{};
    if(!crimson_logic::FreezeMarkerWorld(marker,context.origin,context.local,frozen)) {
        CancelWarpLocked("Marker teleport rejected: invalid world elevation."); return false;
    }
    request.world=frozen;
    if(!g_warpQueue.Enqueue(request,now)) {
        CancelWarpLocked("Marker teleport rejected: invalid or expired destination."); return false;
    }
    g_markerAck.Begin(markerBaseline,marker,now);
    ++g_warpGeneration;
    g_warpRequested=frozen; g_hasWarpRequested=true; g_hasWarpObserved=false;
    g_warpWrites=0; g_warpObservation=WarpObservation::Queued;
    g_warpContext=context;
    g_warpPending.store(true,std::memory_order_release);
    g_warpMessage="Waiting for the new map marker to be accepted. Terrain height is not checked.";
    return true;
}
bool SetAutoTeleport(bool enabled,std::string& message) {
    WarpContext context{};
    if(enabled && (!g_markerReady.load() || !g_outerReady.load() || !WarpSnapshot(context))) {
        message="Automatic marker teleport is unavailable for the current player."; return false;
    }
    std::lock_guard<std::mutex> lock(g_warpMutex);
    CancelWarpLocked("Any pending marker teleport was canceled.");
    if(enabled) {
        g_markerPolicy.Enable(context.player,g_markerRevision);
        g_warpContext=context;
        g_warpMessage="Armed for a NEW map marker. Existing markers will not teleport; terrain height is not checked.";
    } else {
        g_markerPolicy.Disable(g_markerRevision);
        g_warpMessage="Automatic marker teleport is off.";
    }
    message=g_warpMessage; return true;
}
void RefreshWarpState() {
    {
        std::lock_guard<std::mutex> lock(g_warpMutex);
        if(!g_markerPolicy.Enabled() && !g_warpQueue.Pending()) return;
    }
    WarpContext context{};
    const bool valid=WarpSnapshot(context);
    std::lock_guard<std::mutex> lock(g_warpMutex);
    if(!valid || !SameWarpContext(g_warpContext,context)) {
        g_markerPolicy.InvalidateContext(g_markerRevision);
        CancelWarpLocked("Automatic marker teleport disarmed: world or movement context changed.");
    } else if(g_warpQueue.Pending()) {
        crimson_logic::MarkerWarpRequest request{};
        if(!g_warpQueue.BeginTick(context.player,g_markerRevision,GetTickCount64(),request))
            CancelWarpLocked("Marker teleport expired before movement observation completed; no deferred retry.");
        else {
            // The map may pause locomotion. Observe the asynchronous navigation
            // acknowledgement here so its deadline is independent of closing the map.
            Position accepted{};
            if(!Read(context.nav+0x1E8,accepted) ||
                g_markerAck.Check(accepted,GetTickCount64())==crimson_logic::MarkerAckDecision::Cancel)
                CancelWarpLocked("Marker teleport canceled: destination changed, cleared, or was not accepted in time.");
        }
    }
}
bool ManualTeleport(std::string& message) {
    WarpContext context{};
    Position marker{};
    if(!g_markerReady.load() || !g_outerReady.load() || !WarpSnapshot(context) ||
        !Read(context.nav+0x1E8,marker) || !crimson_logic::MarkerPosition(marker)) {
        message="Place a map marker with a supported player loaded before teleporting."; return false;
    }
    std::lock_guard<std::mutex> lock(g_warpMutex);
    const auto now=GetTickCount64();
    crimson_logic::MarkerWarpRequest request{++g_markerRevision,context.player,marker,now,
        now+crimson_logic::NewMarkerPolicy::RequestLifetimeMs};
    const bool queued=QueueWarpLocked(request,context,marker,now);
    message=queued ? "Experimental marker teleport queued; terrain height and landing are unverified." : g_warpMessage;
    return queued;
}
bool IsTrackedStat(const Player& player, uintptr_t entry, std::uint32_t& key) {
    std::uint32_t count{};
    if (!Read(player.root+0x60,count) || !crimson_logic::StatAddress(player.stats,entry,count)) return false;
    std::uint16_t reference{};
    return Read(entry, reference) && StatusKey(reference,key) && crimson_logic::StatEnabled(key,true,true,true);
}
void PinStat(const Player& player, uintptr_t entry,std::uint32_t expectedKey) {
    std::uint64_t base{}, cap{};
    if (!Read(entry + 0x18, base) || !Read(entry + 0x30, cap)) return;
    std::uint64_t full{}, normalized{};
    if (!crimson_logic::Fill(base,cap,full,normalized)) return;
    Player fresh{};
    std::uint32_t freshKey{};
    if (!ValidatePlayer(player.owner, fresh) || !crimson_logic::SameIdentity(player,fresh) ||
        !IsTrackedStat(fresh,entry,freshKey) || freshKey!=expectedKey) return;
    // Both representations must agree; the next native commit reconstructs current from base + normalized.
    if (Write(entry + 0x20, normalized)) Write(entry + 8, full);
}

using CommitFn = std::int64_t(*)(void*, std::int64_t, std::int64_t, std::uint16_t);
using DamageFn = std::int64_t(*)(void*, std::uint16_t, std::int64_t, std::int64_t, uintptr_t,
    char, char, char, char, char, void*);
using LocoFn = void(*)(uintptr_t,float,float*,std::uint64_t,std::uint64_t,std::uint64_t,std::uint64_t);
using OuterMoveFn = void(*)(uintptr_t,float);
using MarkerSenderFn = void(*)(uintptr_t,std::int32_t,float*,std::uint8_t*);
using SyncProxyFn = void(*)(uintptr_t);
CommitFn g_originalCommit{};
DamageFn g_originalDamage{};
LocoFn g_originalLoco{};
OuterMoveFn g_originalOuterMove{};
MarkerSenderFn g_originalMarkerSender{};
SyncProxyFn g_syncProxy{};

__attribute__((noinline)) void MarkerSenderHook(uintptr_t sender,std::int32_t context,float* coordinates,std::uint8_t* set) {
    const auto caller=reinterpret_cast<uintptr_t>(__builtin_return_address(0));
    const crimson_logic::MarkerSenderArguments args{sender,context,coordinates,set};
    WarpContext before{};
    Position target{},baseline{};
    std::uint8_t setting{};
    std::uint64_t revision{},generation{},observed{};
    bool custom{};
    try {
        const bool source=Read(reinterpret_cast<uintptr_t>(set),setting) && WarpSnapshot(before) &&
            before.sender==sender && Read(before.nav+0x1E8,baseline) && crimson_logic::FinitePosition(baseline);
        custom=source && Read(reinterpret_cast<uintptr_t>(coordinates),target) &&
            crimson_logic::CustomMarkerSender(caller,g_game+kMarkerUiReturnRva,before.sender,sender,setting,target);
        if(custom || (source && setting==0)) {
            std::lock_guard<std::mutex> lock(g_warpMutex);
            revision=++g_markerRevision;
            generation=g_markerPolicy.Generation();
            observed=GetTickCount64();
            CancelWarpLocked(custom ? "A new user map destination is being placed." : "Map marker cleared; pending teleport canceled.");
        }
    } catch(...) { custom=false; AbortWarpNoThrow(); }
    // Preserve all original argument values, including the native scratch pointers.
    // No application mutex is held while the engine sends/dispatches its request.
    crimson_logic::ForwardMarkerSender(g_originalMarkerSender,args);
    if(!custom) return;
    try {
        WarpContext after{};
        const bool current=WarpSnapshot(after) && SameWarpContext(before,after);
        std::lock_guard<std::mutex> lock(g_warpMutex);
        if(revision!=g_markerRevision) return; // A newer clear/placement completed during dispatch.
        crimson_logic::MarkerWarpRequest request{};
        const crimson_logic::MarkerEvent event{revision,before.player,crimson_logic::MarkerEventKind::CustomSet,target,observed};
        const auto decision=g_markerPolicy.ObserveCompleted(event,generation,GetTickCount64(),request);
        if(decision!=crimson_logic::MarkerDecision::Queue) return;
        if(!current) {
            CancelWarpLocked("Marker teleport canceled: controlled player or navigation context changed."); return;
        }
        QueueWarpLocked(request,after,baseline,GetTickCount64());
    } catch(...) { AbortWarpNoThrow(); }
}

struct WarpObservationTicket {
    bool valid{};
    std::uint64_t generation{},revision{};
    WarpContext context{};
};
WarpObservationTicket CaptureWarpObservation(uintptr_t component) noexcept {
    WarpObservationTicket ticket{};
    if(!g_warpPending.load(std::memory_order_acquire)) return ticket;
    try {
        WarpContext context{};
        if(!WarpSnapshot(context) || context.component!=component) return ticket;
        std::lock_guard<std::mutex> lock(g_warpMutex);
        if(!g_warpInFlight && g_warpQueue.AwaitingObservation() && SameWarpContext(g_warpContext,context))
            ticket={true,g_warpGeneration,g_markerRevision,context};
    } catch(...) { AbortWarpNoThrow(); }
    return ticket;
}
void WarpAfterOuterMove(uintptr_t component,const WarpObservationTicket& ticket) {
    if(!g_warpPending.load(std::memory_order_acquire) || !g_markerReady.load() || !g_outerReady.load()) return;
    WarpContext current{};
    if(!WarpSnapshot(current) || current.component!=component) return;
    crimson_logic::MarkerWarpRequest request{};
    Position destination{};
    std::uint64_t generation{};
    try {
        std::lock_guard<std::mutex> lock(g_warpMutex);
        if(g_warpInFlight || !g_warpQueue.Pending()) return;
        if(!SameWarpContext(g_warpContext,current) ||
            !g_warpQueue.BeginTick(current.player,g_markerRevision,GetTickCount64(),request)) {
            CancelWarpLocked("Marker teleport canceled: request expired or player context changed."); return;
        }
        Position accepted{};
        if(!Read(current.nav+0x1E8,accepted)) {
            CancelWarpLocked("Marker teleport canceled: navigation destination is unreadable."); return;
        }
        const auto ack=g_markerAck.Check(accepted,GetTickCount64());
        if(ack==crimson_logic::MarkerAckDecision::Wait) return;
        if(ack==crimson_logic::MarkerAckDecision::Cancel) {
            CancelWarpLocked("Marker teleport canceled: destination changed, cleared, or was not accepted in time."); return;
        }
        if(g_warpQueue.AwaitingObservation()) {
            // Only a ticket taken BEFORE this original outer update may confirm
            // an earlier write. Same-callback readback never completes a warp.
            if(!ticket.valid || ticket.generation!=g_warpGeneration || ticket.revision!=request.revision ||
                !SameWarpContext(ticket.context,current)) return;
            const Position observed{current.local.x+current.origin.x,current.local.y+current.origin.y,
                current.local.z+current.origin.z};
            g_hasWarpObserved=crimson_logic::FinitePosition(observed);
            if(g_hasWarpObserved) g_warpObserved=observed;
            const bool observedNear=g_hasWarpObserved && crimson_logic::NearDestination(request.world,observed);
            g_warpQueue.CompleteObservation();
            g_warpPending.store(false,std::memory_order_release);
            g_warpObservation=observedNear ? WarpObservation::ObservedNearMarker : WarpObservation::NotRetained;
            g_warpMessage=observedNear ? "Position observed near the marker after a later movement update; terrain landing is unverified."
                : "Marker teleport not retained after a later movement update; no deferred retry.";
            return;
        }
        WarpContext fresh{};
        if(!WarpSnapshot(fresh) || !SameWarpContext(current,fresh) ||
            !crimson_logic::FrozenDestinationLocal(request.world,fresh.origin,destination)) {
            CancelWarpLocked("Marker teleport canceled: movement context or floating origin became invalid."); return;
        }
        current=fresh;
        // PE3424's native setter subtracts this origin, stores XYZ at proxy+90,
        // and calls 3C62A10. Preserve transform W, rotation and unproven fields.
        if(!Write(current.proxy+0x90,destination)) {
            CancelWarpLocked("Marker teleport canceled: position write failed; no deferred retry."); return;
        }
        ++g_warpWrites; // Count successful position stores even if later readback rejects them.
        generation=g_warpGeneration;
        g_warpInFlight=true;
    } catch(...) { AbortWarpNoThrow(); return; }
    // This is the player's native locomotion thread. Never hold our mutex over
    // an engine call: its physics locking and callbacks are outside our control.
    g_syncProxy(current.proxy);
    try {
        Position observed{},originAfter{};
        WarpContext after{};
        const bool writeReadback=WarpSnapshot(after) && SameWarpContext(current,after) &&
            Read(current.proxy+0x90,observed) && Read(g_game+kOriginRva,originAfter) &&
            crimson_logic::SamePosition(current.origin,originAfter) && crimson_logic::SamePosition(observed,destination);
        std::lock_guard<std::mutex> lock(g_warpMutex);
        g_warpInFlight=false;
        // A clear/disable/new placement can arrive during native synchronization.
        // Never consume or report success for that replacement request.
        crimson_logic::MarkerWarpRequest stillCurrent{};
        if(generation!=g_warpGeneration || request.revision!=g_markerRevision) return;
        if(!g_warpQueue.BeginTick(current.player,g_markerRevision,GetTickCount64(),stillCurrent)) {
            CancelWarpLocked("Marker teleport expired before movement observation completed; no deferred retry."); return;
        }
        if(!writeReadback) {
            CancelWarpLocked("Marker teleport stopped: position write readback failed; no deferred retry."); return;
        }
        g_warpQueue.CompleteWrite(true);
        g_warpPending.store(g_warpQueue.Pending(),std::memory_order_release);
        g_warpObservation=g_warpQueue.AwaitingObservation() ? WarpObservation::AwaitingUpdate : WarpObservation::Writing;
        g_warpMessage=g_warpQueue.AwaitingObservation()
            ? "Position updates submitted; waiting for a later movement update to observe the result."
            : "Marker position update submitted; movement has not been confirmed.";
    } catch(...) { AbortWarpNoThrow(true); }
}

std::int64_t CommitHook(void* entry, std::int64_t opaqueArg2, std::int64_t target, std::uint16_t flag) {
    const auto result = g_originalCommit(entry, opaqueArg2, target, flag);
    if (!g_health.load() && !g_stamina.load() && !g_spirit.load()) return result;
    Player player{};
    std::uint32_t key{};
    const auto address = reinterpret_cast<uintptr_t>(entry);
    if (!CurrentPlayer(player) || !IsTrackedStat(player, address, key)) return result;
    const bool enabled = crimson_logic::StatEnabled(key,g_health.load(),g_stamina.load(),g_spirit.load());
    if (enabled) PinStat(player, address,key);
    return result;
}
std::int64_t DamageHook(void* target, std::uint16_t status, std::int64_t time, std::int64_t delta,
    uintptr_t source, char a6, char a7, char a8, char a9, char a10, void* out) {
    if (g_oneHit.load() && delta < 0) {
        Player player{};
        std::uint32_t key{};
        uintptr_t attacker{};
        // The damage argument is the same WORD StatusInfo reference. The
        // engine compares it with its registered HP reference, not literal 0.
        if (StatusKey(status,key) && key==crimson_logic::HealthKey && CurrentPlayer(player) && reinterpret_cast<uintptr_t>(target) != player.root &&
            Pointer(source + 0x68, attacker) && attacker == player.actor) {
            // Scale only an outgoing player HP hit. NPC-vs-NPC hits, heals and other stats pass unchanged.
            delta = crimson_logic::ScaleOutgoing(delta,key,true,player,reinterpret_cast<uintptr_t>(target),attacker);
        }
    }
    return g_originalDamage(target, status, time, delta, source, a6, a7, a8, a9, a10, out);
}
void LocoHook(uintptr_t component,float dt,float* velocity,std::uint64_t a4,std::uint64_t a5,
              std::uint64_t a6,std::uint64_t a7) {
    alignas(16) float input[4]{},scaled[4]{};
    Player server{};
    Client client{};
    uintptr_t controlled{};
    float* inputForOriginal=velocity;
    // The native callee reads only XYZ, as checked against PE3424. Copy exactly
    // 12 bytes; the original scratch vector may end at a readable-page boundary.
    if (g_speedEnabled.load() && CurrentPlayer(server) && CurrentClientPlayer(client) && MovementComponent(client.player,controlled) &&
        ReadBytes(reinterpret_cast<uintptr_t>(velocity),input,3*sizeof(float)) &&
        crimson_logic::ScaleVelocity(input,scaled,g_speed.load(),controlled,component)) {
        Player fresh{}; Client freshClient{}; uintptr_t freshComponent{};
        if (CurrentPlayer(fresh) && crimson_logic::SameIdentity(server,fresh) &&
            CurrentClientPlayer(freshClient) && crimson_logic::SameClientIdentity(client,freshClient) &&
            MovementComponent(freshClient.player,freshComponent) && freshComponent==controlled) {
            inputForOriginal=scaled;
        }
    }
    g_originalLoco(component,dt,inputForOriginal,a4,a5,a6,a7);
}
void OuterMoveHook(uintptr_t component,float dt) {
    // PE3424 performs contact rollback after nested Loco calls. Consume only
    // after its complete outer update, and never count recursive callbacks.
    static thread_local unsigned depth{};
    struct DepthGuard { unsigned& value; explicit DepthGuard(unsigned& v):value(v) { ++value; } ~DepthGuard() { --value; } } guard(depth);
    const bool outermost=depth==1;
    const auto ticket=outermost ? CaptureWarpObservation(component) : WarpObservationTicket{};
    g_originalOuterMove(component,dt); // Preserve native ABI and invoke exactly once.
    if(outermost) WarpAfterOuterMove(component,ticket);
}

template<std::size_t N> bool Matches(uintptr_t address, const std::array<unsigned char,N>& expected) {
    std::array<unsigned char,N> observed{};
    return ReadBytes(address, observed.data(), N) && observed == expected;
}
bool ValidateBuild() {
    g_game = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    IMAGE_DOS_HEADER dos{};
    IMAGE_NT_HEADERS64 nt{};
    if (!Read(g_game, dos) || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < 0 || dos.e_lfanew > 0x100000 ||
        !Read(g_game + static_cast<uintptr_t>(dos.e_lfanew), nt) || nt.Signature != IMAGE_NT_SIGNATURE) {
        g_initError="pe_header_read_failed";
        g_initMessage="Executable PE header could not be read; no hooks installed.";
        return false;
    }
    g_observedImageSize=nt.OptionalHeader.SizeOfImage;
    const auto header=crimson_build::CheckHeader(nt.FileHeader.Machine,nt.FileHeader.TimeDateStamp,g_observedImageSize);
    if(header!=crimson_build::HeaderResult::Match) {
        g_initError=crimson_build::ErrorCode(header);
        g_initMessage=crimson_build::ErrorMessage(header);
        return false;
    }
    return true;
}
bool ValidateBaseBindings() {
    const std::array<unsigned char,17> commit{{0x66,0x44,0x89,0x4C,0x24,0x20,0x48,0x89,0x54,0x24,0x10,0x53,0x55,0x56,0x57,0x41,0x56}};
    const std::array<unsigned char,15> damage{{0x48,0x89,0x5C,0x24,0x08,0x48,0x89,0x6C,0x24,0x10,0x48,0x89,0x74,0x24,0x18}};
    const std::array<unsigned char,15> loco{{0x48,0x8B,0xC4,0x48,0x89,0x58,0x10,0x44,0x88,0x48,0x20,0x48,0x89,0x48,0x08}};
    const std::array<unsigned char,37> statusLookup{{0x8B,0xFA,0x48,0x8B,0x1D,0x8C,0x2A,0xDD,0x06,0x3B,0x53,0x08,0x0F,0x83,
        0x37,0x01,0x00,0x00,0x8B,0xF7,0x48,0x8B,0x43,0x58,0x48,0x8B,0x04,0xF8,0x48,0x85,0xC0,0x0F,0x85,0x2E,0x01,0x00,0x00}};
    const std::array<unsigned char,7> statusKeyField{{0x8B,0x44,0x24,0x78,0x89,0x47,0x08}};
    const std::array<unsigned char,18> statusReference{{0x49,0x8B,0x7C,0x24,0x58,0x46,0x0F,0xB7,0x34,0x3F,0x41,0x8B,0xD6,0xE8,0xCE,0x64,0xAF,0xFE}};
    return Matches(g_game+kCommitRva, commit) && Matches(g_game+kDamageRva, damage) && Matches(g_game+kLocoRva,loco) &&
        Matches(g_game+0x3DCF93,statusLookup) && Matches(g_game+0x171C7D9,statusKeyField) && Matches(g_game+0x18E6AA0,statusReference);
}
bool ValidateMarkerBindings() {
    const std::array<unsigned char,14> sender{{0x48,0x89,0x5C,0x24,0x08,0x48,0x89,0x74,0x24,0x18,0x89,0x54,0x24,0x10}};
    const std::array<unsigned char,18> sync{{0x48,0x89,0x5C,0x24,0x08,0x57,0x48,0x83,0xEC,0x30,0x48,0x8B,0xF9,0x48,0x83,0x79,0x38,0xFF}};
    const std::array<unsigned char,8> caller{{0xE8,0x92,0x2E,0xD3,0xFF,0x83,0xE3,0xFE}};
    return Matches(g_game+kMarkerSenderRva,sender) && Matches(g_game+kSyncProxyRva,sync) &&
        Matches(g_game+kMarkerUiReturnRva-5,caller);
}
bool ValidateOuterMovementBinding() {
    // PE3424 full outer locomotion update, including the saved-start rollback.
    // Direct caller passes component in RCX and float dt in XMM1.
    const std::array<unsigned char,29> entry{{0x48,0x8B,0xC4,0x55,0x53,0x56,0x57,0x41,0x54,0x41,0x55,0x41,0x56,0x41,0x57,0x48,0x8D,0xA8,0x08,0xF9,0xFF,0xFF,0x48,0x81,0xEC,0xB8,0x07,0x00,0x00}};
    const std::array<unsigned char,20> caller{{0xC5,0xFA,0x59,0xCF,0x48,0x8B,0xCE,0xE8,0x20,0x25,0x01,0x03,0x4C,0x8D,0x9C,0x24,0x80,0x01,0x00,0x00}};
    return Matches(g_game+kOuterMoveRva,entry) && Matches(g_game+0x964B34,caller);
}
bool ValidateClientBindings() {
    // World-map code loads [global], then [root+30], then calls the accessor
    // whose [service+50] is the current client owner. Check both ends in memory.
    const std::array<unsigned char,16> caller{{0x48,0x8B,0x0D,0xEB,0xA5,0x24,0x06,0x48,0x8B,0x49,0x30,0xE8,0x82,0x85,0xA3,0xFF}};
    const std::array<unsigned char,15> getter{{0x48,0x89,0x5C,0x24,0x08,0x48,0x89,0x54,0x24,0x10,
        0x57,0x48,0x83,0xEC,0x30}};
    const std::array<unsigned char,4> field{{0x48,0x8B,0x79,0x50}};
    // RTTI and current constructors establish Normal and ChildOnly primary
    // vtables. Both +178 slots use the same independent local-self predicate.
    const std::array<unsigned char,39> localControl{{0x48,0x8B,0x05,0x79,0xC9,0xBF,0x06,0x48,0x8B,0x50,0x30,
        0x48,0x8B,0x81,0xA0,0x00,0x00,0x00,0x48,0x39,0x42,0x58,0x75,0x0C,0x80,0xB9,0x94,0x00,0x00,0x00,
        0x00,0x74,0x03,0xB0,0x01,0xC3,0x32,0xC0,0xC3}};
    uintptr_t localControlTarget{},childLocalControlTarget{};
    return Matches(g_game+0xF6445E,caller) && Matches(g_game+0x99C9F0,getter) && Matches(g_game+0x99CA0A,field) &&
        Matches(g_game+crimson_logic::ClientLocalControlRva,localControl) &&
        Pointer(g_game+crimson_logic::ClientOwnerVtableRva+0x178,localControlTarget) &&
        localControlTarget==g_game+crimson_logic::ClientLocalControlRva &&
        Pointer(g_game+crimson_logic::ClientChildOwnerVtableRva+0x178,childLocalControlTarget) &&
        childLocalControlTarget==g_game+crimson_logic::ClientLocalControlRva;
}
bool ValidateExecutableHash() {
    wchar_t path[32768]{};
    if (!GetModuleFileNameW(nullptr,path,32768)) return false;
    std::ifstream input(std::filesystem::path(path),std::ios::binary);
    if (!input) return false;
    // The BCrypt hash object storage must outlive BCryptDestroyHash.
    std::vector<unsigned char> object,buffer(1024*1024);
    struct Handles {
        BCRYPT_ALG_HANDLE algorithm{};
        BCRYPT_HASH_HANDLE hash{};
        ~Handles() { if(hash) BCryptDestroyHash(hash); if(algorithm) BCryptCloseAlgorithmProvider(algorithm,0); }
    } handles;
    if (BCryptOpenAlgorithmProvider(&handles.algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0) return false;
    DWORD objectSize{}, returned{};
    if (BCryptGetProperty(handles.algorithm,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&objectSize),sizeof(objectSize),&returned,0)<0 ||
        objectSize==0 || objectSize>65536) return false;
    object.resize(objectSize);
    if (BCryptCreateHash(handles.algorithm,&handles.hash,object.data(),objectSize,nullptr,0,0)<0) return false;
    while (input) {
        input.read(reinterpret_cast<char*>(buffer.data()),static_cast<std::streamsize>(buffer.size()));
        const auto count=input.gcount();
        if (count>0 && BCryptHashData(handles.hash,buffer.data(),static_cast<ULONG>(count),0)<0) return false;
    }
    if (!input.eof()) return false;
    std::array<unsigned char,32> digest{};
    if (BCryptFinishHash(handles.hash,digest.data(),static_cast<ULONG>(digest.size()),0)<0) return false;
    constexpr std::array<unsigned char,32> expected{{0xD4,0x85,0x5C,0x08,0x99,0x86,0x38,0xF0,0x7F,0xA8,0xFA,0xE0,0x8B,0xD6,0x24,0x92,0x61,0xF1,0x0C,0x8F,0xEC,0x22,0x15,0xEF,0xFC,0x29,0xCB,0x33,0x9E,0x7F,0xA5,0x25}};
    return digest==expected;
}
void InstallHooks() {
    if (!ValidateBuild()) { SkipAllHooks("build_guard_failed"); return; }
    if (!ValidateExecutableHash()) {
        SkipAllHooks("executable_hash_failed");
        g_initError="executable_hash_mismatch";
        g_initMessage="Executable SHA-256 mismatch or unreadable file; no hooks installed."; return;
    }
    if(!ValidateBaseBindings()) {
        SkipAllHooks("base_hook_bytes_mismatch");
        g_initError="base_hook_bytes_mismatch";
        g_initMessage="Native stat, damage, or movement hook bytes mismatch; no hooks installed."; return;
    }
    g_clientBindingReady.store(ValidateClientBindings());
    if(!g_clientBindingReady.load()) {
        g_initError="client_owner_bindings_mismatch";
        g_initMessage="Controlled client owner bindings mismatch; movement and marker controls are unavailable.";
    }
    g_hookLibraryResult=CallHookStage([] { return MH_Initialize(); });
    if (g_hookLibraryResult.status != MH_OK) {
        SkipAllHooks("hook_library_initialization_failed");
        g_initError="hook_library_init_failed";
        g_initMessage = "The hook library could not initialize: "+HookStageSummary(g_hookLibraryResult)+". No hooks installed."; return;
    }
    auto commit = reinterpret_cast<void*>(g_game + kCommitRva);
    auto damage = reinterpret_cast<void*>(g_game + kDamageRva);
    auto loco = reinterpret_cast<void*>(g_game + kLocoRva);
    InstallOneHook(g_hookResults[0],commit,reinterpret_cast<void*>(&CommitHook),reinterpret_cast<void**>(&g_originalCommit),g_commitReady);
    InstallOneHook(g_hookResults[1],damage,reinterpret_cast<void*>(&DamageHook),reinterpret_cast<void**>(&g_originalDamage),g_damageReady);
    if(g_clientBindingReady.load())
        InstallOneHook(g_hookResults[2],loco,reinterpret_cast<void*>(&LocoHook),reinterpret_cast<void**>(&g_originalLoco),g_locoReady);
    else g_hookResults[2].skippedReason="client_binding_unavailable";
    const bool outerBinding=ValidateOuterMovementBinding();
    if(!outerBinding) {
        g_initError="outer_movement_hook_bytes_mismatch";
        g_initMessage="Outer movement hook bytes mismatch; marker controls are unavailable.";
    }
    if(g_clientBindingReady.load() && outerBinding)
        InstallOneHook(g_hookResults[4],reinterpret_cast<void*>(g_game+kOuterMoveRva),reinterpret_cast<void*>(&OuterMoveHook),
            reinterpret_cast<void**>(&g_originalOuterMove),g_outerReady);
    else g_hookResults[4].skippedReason=outerBinding ? "client_binding_unavailable" : "outer_binding_unavailable";
    const bool markerBindings=ValidateMarkerBindings();
    if(!markerBindings) {
        g_initError="marker_hook_bytes_mismatch";
        g_initMessage="Marker teleport hook bytes mismatch; marker controls are unavailable.";
    }
    if(g_outerReady.load() && markerBindings) {
        auto sender=reinterpret_cast<void*>(g_game+kMarkerSenderRva);
        g_syncProxy=reinterpret_cast<SyncProxyFn>(g_game+kSyncProxyRva);
        InstallOneHook(g_hookResults[3],sender,reinterpret_cast<void*>(&MarkerSenderHook),reinterpret_cast<void**>(&g_originalMarkerSender),g_markerReady);
    } else g_hookResults[3].skippedReason=markerBindings ? "outer_movement_hook_not_ready" : "marker_binding_unavailable";
    try {
        const auto failures=HookFailureSummary();
        if(!failures.empty()) {
            if(g_initError.empty()) g_initError="partial_hooks_failed";
            if(!g_initMessage.empty()) g_initMessage+=" ";
            g_initMessage+="Native hook setup failed: "+failures+". Affected controls are unavailable.";
        }
    } catch(...) { /* Diagnostic allocation cannot change completed hook outcomes. */ }
}
bool ReadText(const std::filesystem::path& path, std::string& text, std::size_t limit) {
    std::ifstream stream(path, std::ios::binary | std::ios::ate);
    if (!stream) return false;
    const auto size=stream.tellg();
    if (size<0 || static_cast<std::uint64_t>(size)>limit) return false;
    stream.seekg(0);
    text.resize(static_cast<std::size_t>(size));
    return text.empty() || static_cast<bool>(stream.read(text.data(),size));
}
std::uint64_t UnixMilliseconds(const FILETIME& value) {
    ULARGE_INTEGER ticks{};
    ticks.LowPart=value.dwLowDateTime; ticks.HighPart=value.dwHighDateTime;
    constexpr auto epoch=116444736000000000ULL;
    return ticks.QuadPart>=epoch ? (ticks.QuadPart-epoch)/10000ULL : 0;
}
std::uint64_t UnixMilliseconds() { FILETIME now{}; GetSystemTimeAsFileTime(&now); return UnixMilliseconds(now); }
bool LeaseFresh() {
    WIN32_FILE_ATTRIBUTE_DATA attrs{};
    const auto path = g_bridge / L"lease.txt";
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &attrs)) return false;
    FILETIME now{};
    GetSystemTimeAsFileTime(&now);
    ULARGE_INTEGER current{}, written{};
    current.LowPart = now.dwLowDateTime; current.HighPart = now.dwHighDateTime;
    written.LowPart = attrs.ftLastWriteTime.dwLowDateTime; written.HighPart = attrs.ftLastWriteTime.dwHighDateTime;
    // exFAT can round a last-write timestamp up by two seconds.
    if (written.QuadPart > current.QuadPart && written.QuadPart-current.QuadPart>20000000ULL) return false;
    const auto age=current.QuadPart>=written.QuadPart ? current.QuadPart-written.QuadPart : 0;
    if (age>kLeaseTicks) return false;
    std::string contents;
    if (!ReadText(path, contents, 128)) return false;
    while (!contents.empty() && (contents.back() == '\r' || contents.back() == '\n' || contents.back() == ' ')) contents.pop_back();
    if (contents!=g_session) return false;
    g_leaseDeadline.store(GetTickCount64()+(kLeaseTicks-age)/10000ULL);
    return true;
}
const char* FeatureUnavailableReason(const std::string& feature) {
    if (!g_playerReady.load()) return "The controlled player is not recognized.";
    if (feature == "crimson_health" || feature == "crimson_stamina" || feature == "crimson_spirit")
        return g_commitReady.load() ? nullptr : "The stat hook is unavailable.";
    if (feature == "crimson_one_hit") return g_damageReady.load() ? nullptr : "The damage hook is unavailable.";
    if (feature == "crimson_speed") {
        Player server{}; Client client{}; uintptr_t component{};
        if(!g_locoReady.load()) return "The movement hook is unavailable.";
        if(!CurrentPlayer(server)) return "The controlled player is not recognized.";
        if(!CurrentClientPlayer(client)) return "The controlled client player is not recognized.";
        return MovementComponent(client.player,component) ? nullptr : "The player movement component is unavailable.";
    }
    if(feature=="crimson_auto_teleport" || feature=="crimson_teleport") {
        WarpContext context{};
        if(!g_markerReady.load() || !g_outerReady.load()) return "The marker teleport hooks are unavailable.";
        if(!CurrentPlayer(context.player)) return "The controlled player is not recognized.";
        if(!CurrentClientPlayer(context.client)) return "The controlled client player is not recognized.";
        if(!MovementComponent(context.client.player,context.component)) return "The player movement component is unavailable.";
        if(!WarpSnapshot(context)) return "The map teleport context is unavailable.";
        if(feature=="crimson_auto_teleport") return nullptr; // Arming must work BEFORE placing a marker.
        Position marker{};
        if(!Read(context.nav+0x1E8,marker)) return "The map teleport context is unavailable.";
        return crimson_logic::MarkerPosition(marker) ? nullptr : "Place a custom map marker first.";
    }
    return "This control is unavailable for the current player or game binding.";
}
bool FeatureAvailable(const std::string& feature) { return FeatureUnavailableReason(feature)==nullptr; }
void Command() {
    std::string text;
    if (!ReadText(g_bridge / L"command.json", text, 16384)) return;
    try {
        const Json command = Json::parse(text);
        if (!command.is_object() || !command.contains("request_id") || !command["request_id"].is_string()) return;
        const auto id = command["request_id"].get<std::string>();
        if (id.empty() || id.size() > 128 || id == g_lastCommand) return;
        // A previous connection's mailbox cannot change this process or consume a current request id.
        if (command.value("session_id", std::string()) != g_session) return;
        g_lastCommand = id;
        g_lastSuccess = false;
        if (command.value("schema_version", 0) != 1) { g_lastMessage = "Unsupported command schema."; return; }
        if (command.value("version",std::string())!="0.2.7") { g_lastMessage="Unsupported protocol version."; return; }
        const auto now=UnixMilliseconds();
        if (!command.contains("expires_unix_ms") || !command["expires_unix_ms"].is_number_unsigned()) {
            g_lastMessage="Command expiration is required."; return;
        }
        const auto expiry=command["expires_unix_ms"].get<std::uint64_t>();
        if (expiry<now || expiry-now>30000) { g_lastMessage="Command expired or expiration is too far in the future."; return; }
        const auto feature = command.value("feature", std::string());
        if (feature == "disable_all") { DisableAll(); g_lastSuccess=true; g_lastMessage="All Crimson Desert controls disabled."; return; }
        // An authenticated, unexpired off command remains useful after player
        // availability or the live lease disappears.
        if(feature=="crimson_auto_teleport" && command.contains("enabled") &&
            command["enabled"].is_boolean() && !command["enabled"].get<bool>()) {
            g_lastSuccess=SetAutoTeleport(false,g_lastMessage); return;
        }
        if (!g_leaseValid.load() || GetTickCount64()>=g_leaseDeadline.load()) {
            DisableAll(); g_lastMessage="HappyCheats connection lease expired."; return;
        }
        if (!FeatureAvailable(feature)) { g_lastMessage="This control is unavailable for the current player or game binding."; return; }
        if(feature=="crimson_teleport") { g_lastSuccess=ManualTeleport(g_lastMessage); return; }
        if(feature=="crimson_auto_teleport") {
            if(!command.contains("enabled") || !command["enabled"].is_boolean()) {
                g_lastMessage="A boolean enabled value is required."; return;
            }
            g_lastSuccess=SetAutoTeleport(command["enabled"].get<bool>(),g_lastMessage); return;
        }
        if(feature=="crimson_speed") {
            if (!command.contains("value") || !command["value"].is_number()) {
                g_lastMessage="A movement speed value is required."; return;
            }
            const auto value=command["value"].get<float>();
            if(!crimson_logic::SpeedValue(value)) { g_lastMessage="Speed must be 0.25 to 3.00 in steps of 0.25."; return; }
            const auto enabled=command.value("enabled",true);
            g_speed.store(enabled ? value : 1.0f); g_speedEnabled.store(enabled && value!=1.0f);
            g_lastSuccess=true; g_lastMessage="Character movement speed updated."; return;
        }
        if (!command.contains("enabled") || !command["enabled"].is_boolean()) { g_lastMessage="A boolean enabled value is required."; return; }
        const bool enabled = command["enabled"].get<bool>();
        if (feature == "crimson_health") g_health.store(enabled);
        else if (feature == "crimson_stamina") g_stamina.store(enabled);
        else if (feature == "crimson_spirit") g_spirit.store(enabled);
        else if (feature == "crimson_one_hit") g_oneHit.store(enabled);
        else { g_lastMessage="Unknown control."; return; }
        g_lastSuccess=true;
        g_lastMessage = enabled ? "Enabled; awaiting the next matching player stat or damage event." : "Disabled.";
    } catch (...) { /* A torn or malformed command is never executed. */ }
}
void Hotkeys() {
    static std::array<bool,7> previous{};
    constexpr std::array<int,7> keys{{VK_F1,VK_F2,VK_F3,VK_F4,VK_F5,VK_F9,VK_F10}};
    DWORD foregroundPid{};
    GetWindowThreadProcessId(GetForegroundWindow(),&foregroundPid);
    const bool active=foregroundPid==GetCurrentProcessId() && g_leaseValid.load() && g_playerReady.load() &&
        GetTickCount64()<g_leaseDeadline.load();
    for (std::size_t index=0;index<keys.size();++index) {
        const bool down=(GetAsyncKeyState(keys[index])&0x8000)!=0;
        const bool rising=down && !previous[index];
        previous[index]=down;
        if (!active || !rising) continue;
        Player fresh{};
        if (!CurrentPlayer(fresh)) continue;
        if(index==0 && g_commitReady.load()) g_health.store(!g_health.load());
        if(index==1 && g_commitReady.load()) g_stamina.store(!g_stamina.load());
        if(index==2 && g_damageReady.load()) g_oneHit.store(!g_oneHit.load());
        if(index==3 && g_commitReady.load()) g_spirit.store(!g_spirit.load());
        if(index==4 && FeatureAvailable("crimson_teleport")) ManualTeleport(g_lastMessage);
        if(index==5) {
            bool enabled{};
            { std::lock_guard<std::mutex> lock(g_warpMutex); enabled=g_markerPolicy.Enabled(); }
            SetAutoTeleport(!enabled,g_lastMessage);
        }
        if(index==6) DisableAll();
    }
}
void Status() {
    bool autoEnabled{},warpPending{};
    std::string warpMessage;
    std::uint64_t revision{};
    Json teleportObservation;
    {
        std::lock_guard<std::mutex> lock(g_warpMutex);
        autoEnabled=g_markerPolicy.Enabled(); warpPending=g_warpQueue.Pending();
        warpMessage=g_warpMessage; revision=g_markerRevision;
        teleportObservation={{"state",ObservationName(g_warpObservation)},{"writes",g_warpWrites},
            {"write_limit",crimson_logic::MarkerWarpQueue::HoldUpdates},{"tolerance_world_units",crimson_logic::MarkerObservationTolerance},
            {"requested_world",g_hasWarpRequested ? Json::array({g_warpRequested.x,g_warpRequested.y,g_warpRequested.z}) : Json(nullptr)},
            {"observed_world",g_hasWarpObserved ? Json::array({g_warpObserved.x,g_warpObserved.y,g_warpObserved.z}) : Json(nullptr)}};
    }
    Json enabled = {{"crimson_health",g_health.load()},{"crimson_stamina",g_stamina.load()},
        {"crimson_spirit",g_spirit.load()},{"crimson_one_hit",g_oneHit.load()},
        {"crimson_speed",g_speedEnabled.load()},{"crimson_teleport",false},{"crimson_items",false},
        {"crimson_auto_teleport",autoEnabled}};
    Json values = {{"crimson_speed",g_speed.load()}};
    Json available=Json::object();
    std::string message = g_initMessage;
    if (message.empty()) message = g_playerReady.load() ? "Connected. Gameplay behavior has not yet been validated on this build."
        : "Load into the world with a supported controlled player. All controls are currently off.";
    Json status = {{"schema_version",1},{"version","0.2.7"},{"process_id",GetCurrentProcessId()},
        {"pid",GetCurrentProcessId()},{"process_start_unix_ms",g_processStartUnixMs},
        {"heartbeat_unix_ms",UnixMilliseconds()},
        {"session_id",g_session},{"ready",g_commitReady.load() || g_damageReady.load() || g_locoReady.load() || g_outerReady.load()},
        {"player_ready",g_playerReady.load()},{"lease_valid",g_leaseValid.load()},{"message",message},
        {"client_player_ready",g_clientReady.load()},
        {"initialization_error",g_initError},{"observed_image_size",g_observedImageSize},
        {"expected_image_size",crimson_build::ExpectedImageSize},
        {"last_command",g_lastCommand},{"result",{{"success",g_lastSuccess},{"message",g_lastMessage}}},
        {"command_result",{{"request_id",g_lastCommand},{"success",g_lastSuccess},{"message",g_lastMessage}}},
        {"enabled",enabled},{"teleport_pending",warpPending},{"teleport_message",warpMessage},
        {"marker_revision",revision},{"teleport_ground_checked",false},{"teleport_observation",teleportObservation}};
    try {
        status["hook_library"]=HookCallJson(g_hookLibraryResult);
        status["hook_results"]=HookResultsJson();
        status["hook_diagnostics_note"]="MH_STATUS determines success. Raw Win32 last-error values are contextual and may reflect internal enumeration or cleanup, not the failure cause. Free-candidate counts are observations, not allocation probes; incomplete scans provide only a lower bound.";
    } catch(...) { /* Optional diagnostic JSON must not disable otherwise ready controls. */ }
    for (auto feature : kFeatures) {
        const std::string key = std::string("available_") + feature;
        const auto reason=FeatureUnavailableReason(feature);
        const bool supported = reason==nullptr;
        values[key]=supported; status[key]=supported; available[feature]=supported;
        if(reason && (std::string(feature)=="crimson_speed" || std::string(feature)=="crimson_teleport" ||
            std::string(feature)=="crimson_auto_teleport")) values[std::string("reason_")+feature]=reason;
    }
    status["values"]=values;
    status["available"]=available;
    const auto temporary = g_bridge / L"status.json.tmp";
    {
        std::ofstream output(temporary, std::ios::binary | std::ios::trunc);
        if (!output) return;
        output << status.dump(2);
        if (!output) return;
    }
    MoveFileExW(temporary.c_str(), (g_bridge / L"status.json").c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
}
DWORD WINAPI Worker(void*) {
    wchar_t filename[32768]{};
    if (!GetModuleFileNameW(g_module, filename, 32768)) return 0;
    g_bridge = std::filesystem::path(filename).parent_path();
    LARGE_INTEGER sequence{};
    QueryPerformanceCounter(&sequence);
    char session[96]{};
    std::snprintf(session,sizeof(session),"%08lx-%016llx-%016llx",static_cast<unsigned long>(GetCurrentProcessId()),
        static_cast<unsigned long long>(GetTickCount64()),static_cast<unsigned long long>(sequence.QuadPart));
    g_session=session;
    FILETIME created{},exited{},kernel{},user{};
    if (GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user)) g_processStartUnixMs=UnixMilliseconds(created);
    try { InstallHooks(); } catch (...) {
        g_initError="native_initialization_exception";
        g_initMessage="Native initialization failed; all controls remain disabled.";
    }
    ULONGLONG nextRefresh{};
    for (;;) {
        try {
            const bool refresh=GetTickCount64()>=nextRefresh;
            if(refresh) {
                nextRefresh=GetTickCount64()+250;
                const bool lease=LeaseFresh();
                g_leaseValid.store(lease,std::memory_order_release);
                Player player{};
                const bool playerReady=(g_commitReady.load() || g_damageReady.load() || g_locoReady.load() || g_outerReady.load()) && ResolvePlayer(player);
                Client client{};
                const bool clientReady=ResolveClientPlayer(client);
                RefreshIdentityCache(lease,playerReady,player,clientReady,client);
                RefreshWarpState();
            }
            Command();
            Hotkeys();
            if(refresh) Status();
        } catch (...) { DisableAll(); g_playerReady.store(false); }
        Sleep(50);
    }
}
} // namespace

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_module=module;
        DisableThreadLibraryCalls(module);
        const HANDLE worker=CreateThread(nullptr,0,Worker,nullptr,0,nullptr);
        if (worker) CloseHandle(worker);
    }
    return TRUE;
}
