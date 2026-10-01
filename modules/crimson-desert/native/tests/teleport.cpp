// Actual production hooks against fixture-owned memory and fake engine calls.
// No game process, binary, worker, injection, or live hook installation is used.
#include "../trainer.cpp"
#include <cassert>

namespace fixture {
struct Block {
    std::array<unsigned char,0x600> bytes{};
    uintptr_t address() { return reinterpret_cast<uintptr_t>(bytes.data()); }
    template<class T> void Put(std::size_t offset,const T& value) {
        assert(offset+sizeof(value)<=bytes.size());
        std::memcpy(bytes.data()+offset,&value,sizeof(value));
    }
};
struct Body {
    Block owner,actor,marker,root,stats,descriptor,possessor;
    Player Build() {
        owner.Put(0x68,actor.address()); owner.Put(0x88,descriptor.address());
        descriptor.Put(1,std::uint8_t{1}); owner.Put(0xA0,possessor.address());
        possessor.Put(0xD0,owner.address()); actor.Put(0x20,marker.address());
        marker.Put(0x18,root.address()); root.Put(0,marker.address());
        root.Put(0x58,stats.address()); stats.Put(0,std::int32_t{0});
        return {owner.address(),actor.address(),marker.address(),root.address(),stats.address()};
    }
};
Position start{10,20,30},marker{100,0,300},destination{100,20,300};
uintptr_t componentAddress{},proxyAddress{},navAddress{};
unsigned syncCalls{},outerCalls{},locoCalls{};
float observedDt{};
float observedVelocity[3]{};
bool recurse{},insideRecursion{},immediateCorrection{};
enum class Action { None,CorrectLater,NearMotion,FarMotion,Clear,Disable,ExpireLease,SwapClient,Replace,Rebase };
Action action{};
std::vector<Position> writes;
void Sync(uintptr_t proxy) {
    assert(proxy==proxyAddress); ++syncCalls;
    Position value{}; assert(Read(proxy+0x90,value)); writes.push_back(value);
    if(immediateCorrection) assert(Write(proxy+0x90,start));
}
void Loco(uintptr_t,float dt,float* velocity,std::uint64_t a4,std::uint64_t a5,std::uint64_t a6,std::uint64_t a7) {
    ++locoCalls; observedDt=dt;
    std::copy(velocity,velocity+3,observedVelocity);
    assert(a4==4 && a5==5 && a6==6 && a7==7);
}
void Outer(uintptr_t component,float dt) {
    ++outerCalls; observedDt=dt;
    if(component!=componentAddress) return;
    Position entry{},origin{};
    assert(Read(proxyAddress+0x90,entry) && Read(g_game+kOriginRva,origin));
    const auto before=syncCalls;
    alignas(16) float velocity[4]{2,3,4,0};
    for(unsigned substep=0;substep<5;++substep) LocoHook(component,dt,velocity,4,5,6,7);
    if(recurse && !insideRecursion) {
        insideRecursion=true; OuterMoveHook(component,dt); insideRecursion=false;
    }
    assert(syncCalls==before); // Neither Loco substeps nor recursive outers consume a write.
    assert(Write(proxyAddress+0x90,entry)); // Native saved-start rollback shape.
    if(insideRecursion) return;
    const auto currentAction=action; action=Action::None;
    switch(currentAction) {
    case Action::CorrectLater: assert(Write(proxyAddress+0x90,start)); break;
    case Action::NearMotion: entry.x+=0.3f; entry.y+=0.4f; assert(Write(proxyAddress+0x90,entry)); break;
    case Action::FarMotion: entry.x+=2; assert(Write(proxyAddress+0x90,entry)); break;
    case Action::Clear: {
        assert(Write(navAddress+0x1E8,Position{}));
        std::lock_guard<std::mutex> lock(g_warpMutex);
        ++g_markerRevision; CancelWarpLocked("fixture marker cleared"); break;
    }
    case Action::Disable: DisableAll(); break;
    case Action::ExpireLease: g_leaseDeadline=0; break;
    case Action::SwapClient: g_clientOwner=0; break;
    case Action::Replace: {
        const Position replacement{400,0,500}; assert(Write(navAddress+0x1E8,replacement));
        std::string message; assert(ManualTeleport(message)); break;
    }
    case Action::Rebase: {
        const Position newOrigin{100,-200,300};
        entry={entry.x+origin.x-newOrigin.x,entry.y+origin.y-newOrigin.y,entry.z+origin.z-newOrigin.z};
        assert(Write(g_game+kOriginRva,newOrigin) && Write(proxyAddress+0x90,entry)); break;
    }
    default: break;
    }
}
void Cache(const Player& server,const Client& client) {
    g_actor=server.actor; g_marker=server.marker; g_root=server.root; g_stats=server.stats; g_owner=server.owner;
    g_clientRoot=client.anchor.root; g_clientService=client.anchor.service;
    g_clientActor=client.player.actor; g_clientMarker=client.player.marker;
    g_clientVitalRoot=client.player.root; g_clientStats=client.player.stats; g_clientOwner=client.player.owner;
    g_leaseValid=true; g_leaseDeadline=GetTickCount64()+60000;
    g_clientBindingReady=true; g_clientReady=true; g_playerReady=true;
    g_locoReady=true; g_outerReady=true; g_markerReady=true;
}
void Begin(const Player& server,const Client& client) {
    DisableAll(); Cache(server,client);
    syncCalls=0; outerCalls=0; locoCalls=0; writes.clear();
    recurse=false; immediateCorrection=false; action=Action::None;
    assert(Write(g_game+kOriginRva,Position{}) && Write(proxyAddress+0x90,start) && Write(navAddress+0x1E8,marker));
    std::string result;
    assert(ManualTeleport(result) && result.find("queued")!=std::string::npos);
    assert(syncCalls==0 && g_warpWrites==0 && g_warpObservation==WarpObservation::Queued);
}
void SixUpdates() {
    for(unsigned update=0;update<crimson_logic::MarkerWarpQueue::HoldUpdates;++update) {
        OuterMoveHook(componentAddress,0.125f);
        assert(g_warpWrites==update+1 && syncCalls==update+1);
        assert(g_warpPending.load() && g_warpObservation!=WarpObservation::ObservedNearMarker);
        assert(!g_hasWarpObserved);
    }
    assert(g_warpQueue.AwaitingObservation() && g_warpObservation==WarpObservation::AwaitingUpdate);
}
}

int main() {
    using namespace fixture;
    auto image=static_cast<unsigned char*>(VirtualAlloc(nullptr,0x6D6A000,MEM_RESERVE,PAGE_NOACCESS));
    assert(image); g_game=reinterpret_cast<uintptr_t>(image);
    for(const uintptr_t rva:{kOriginRva,crimson_logic::ClientRootRva})
        assert(VirtualAlloc(image+(rva&~uintptr_t{0xFFF}),4096,MEM_COMMIT,PAGE_READWRITE));
    Body serverBody,clientBody;
    const Player server=serverBody.Build(),player=clientBody.Build();
    Block root,service,object,component,proxy,nav,npc;
    const Client client{{root.address(),service.address(),player.owner},player};
    assert(Write(g_game+crimson_logic::ClientRootRva,root.address()));
    root.Put(0x30,service.address()); service.Put(0x50,player.owner);
    clientBody.owner.Put(0,g_game+crimson_logic::ClientOwnerVtableRva);
    clientBody.actor.Put(0x40,object.address()); object.Put(0x140,component.address());
    component.Put(0x2C0,proxy.address()); proxy.Put(0x38,uintptr_t{1234});
    clientBody.actor.Put(0x168,nav.address()); nav.Put(8,player.owner);
    componentAddress=component.address(); proxyAddress=proxy.address(); navAddress=nav.address();
    g_syncProxy=Sync; g_originalLoco=Loco; g_originalOuterMove=Outer;

    Begin(server,client);
    g_outerReady=false; assert(!FeatureAvailable("crimson_teleport") && !FeatureAvailable("crimson_auto_teleport"));
    g_outerReady=true; g_locoReady=false; assert(FeatureAvailable("crimson_teleport")); g_locoReady=true;
    SixUpdates();
    assert(locoCalls==30 && outerCalls==6 && syncCalls==6 && observedDt==0.125f);
    OuterMoveHook(npc.address(),0.125f);
    assert(outerCalls==7 && syncCalls==6 && !g_hasWarpObserved && g_warpPending.load());
    OuterMoveHook(componentAddress,0.125f);
    assert(syncCalls==6 && !g_warpPending.load() && g_warpObservation==WarpObservation::ObservedNearMarker);
    assert(g_hasWarpObserved && crimson_logic::SamePosition(g_warpObserved,destination));
    assert(g_warpMessage.find("after a later movement update")!=std::string::npos);

    Begin(server,client); recurse=true; OuterMoveHook(componentAddress,0.125f);
    assert(outerCalls==2 && locoCalls==10 && syncCalls==1 && g_warpWrites==1);

    Begin(server,client); SixUpdates(); action=Action::CorrectLater; OuterMoveHook(componentAddress,0.125f);
    assert(syncCalls==6 && !g_warpPending.load() && g_warpObservation==WarpObservation::NotRetained);
    assert(crimson_logic::SamePosition(g_warpObserved,start));
    OuterMoveHook(componentAddress,0.125f); assert(syncCalls==6); // No deferred retry.
    Begin(server,client); SixUpdates(); action=Action::NearMotion; OuterMoveHook(componentAddress,0.125f);
    assert(syncCalls==6 && g_warpObservation==WarpObservation::ObservedNearMarker);
    Begin(server,client); SixUpdates(); action=Action::FarMotion; OuterMoveHook(componentAddress,0.125f);
    assert(syncCalls==6 && g_warpObservation==WarpObservation::NotRetained);
    Begin(server,client); SixUpdates(); action=Action::Rebase; OuterMoveHook(componentAddress,0.125f);
    assert(syncCalls==6 && g_warpObservation==WarpObservation::ObservedNearMarker);
    assert(crimson_logic::SamePosition(g_warpObserved,destination));

    for(const auto cancel:{Action::Clear,Action::Disable,Action::ExpireLease,Action::SwapClient}) {
        Begin(server,client); SixUpdates(); action=cancel; OuterMoveHook(componentAddress,0.125f);
        RefreshWarpState();
        assert(syncCalls==6 && !g_warpPending.load() && g_warpObservation==WarpObservation::Canceled);
        assert(!g_hasWarpObserved); // A pre-original ticket never survives cancellation/context loss.
        Begin(server,client); action=cancel; OuterMoveHook(componentAddress,0.125f); RefreshWarpState();
        assert(syncCalls==0 && !g_warpPending.load()); // No first write after context loss either.
    }
    Begin(server,client); SixUpdates(); action=Action::Replace; OuterMoveHook(componentAddress,0.125f);
    assert(syncCalls==7 && g_warpWrites==1 && !g_hasWarpObserved && g_warpObservation==WarpObservation::Writing);
    assert(writes.back().x==400 && writes.back().z==500); // Old ticket cannot complete or write the replaced target.

    Begin(server,client); immediateCorrection=true; OuterMoveHook(componentAddress,0.125f);
    assert(syncCalls==1 && !g_warpPending.load() && g_warpObservation==WarpObservation::Canceled);
    OuterMoveHook(componentAddress,0.125f); assert(syncCalls==1);

    // Moving the teleport consumer must not change the original speed hook.
    Begin(server,client); g_speedEnabled=true; g_speed=2.0f;
    alignas(16) float input[4]{2,3,4,0};
    LocoHook(componentAddress,0.25f,input,4,5,6,7);
    assert(observedVelocity[0]==4 && observedVelocity[1]==3 && observedVelocity[2]==8 && observedDt==0.25f);
    assert(input[0]==2 && input[1]==3 && input[2]==4 && syncCalls==0);
    LocoHook(npc.address(),0.25f,input,4,5,6,7);
    assert(observedVelocity[0]==2 && observedVelocity[1]==3 && observedVelocity[2]==4 && syncCalls==0);

    assert(VirtualFree(image,0,MEM_RELEASE));
    std::puts("PASS actual outer teleport hooks: nested Loco rollback, six outer writes, later observation, corrections, reentry, cancellation, replacement, rebase, player-only speed and no off-target writes. No game accessed.");
}
