#include "../trainer_logic.h"
#include "../marker_policy.h"
#include "../build_guard.h"
#include <cassert>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <string>
using namespace crimson_logic;
int main() {
    // This fixture was extracted from the actual PE inspection report, not
    // constructed from the production constants. It would fail with the old
    // 0x173AA000 image-size guard even though negative-host smoke tests passed.
    std::ifstream metadata(std::filesystem::path(__FILE__).parent_path()/"fixtures"/"pe2976-build.txt");
    assert(metadata);
    std::string provenance,digest;
    assert(std::getline(metadata,provenance) && std::getline(metadata,digest));
    std::uint16_t inspectedMachine{};
    std::uint32_t inspectedTimestamp{},inspectedImageSize{};
    assert(metadata>>inspectedMachine>>inspectedTimestamp>>inspectedImageSize);
    using HeaderResult=crimson_build::HeaderResult;
    assert(crimson_build::CheckHeader(inspectedMachine,inspectedTimestamp,inspectedImageSize)==HeaderResult::Match);
    assert(crimson_build::CheckHeader(inspectedMachine,inspectedTimestamp,0x173AA000)==HeaderResult::ImageSizeMismatch);
    assert(crimson_build::CheckHeader(inspectedMachine,inspectedTimestamp,inspectedImageSize+4096)==HeaderResult::ImageSizeMismatch);
    assert(crimson_build::CheckHeader(inspectedMachine,inspectedTimestamp+1,inspectedImageSize)==HeaderResult::TimestampMismatch);
    assert(crimson_build::CheckHeader(0x14C,inspectedTimestamp,inspectedImageSize)==HeaderResult::MachineMismatch);
    assert(std::string(crimson_build::ErrorCode(HeaderResult::ImageSizeMismatch))=="pe_image_size_mismatch");
    assert(std::string(crimson_build::ErrorMessage(HeaderResult::ImageSizeMismatch)).find("image size")!=std::string::npos);
    std::ifstream realms(std::filesystem::path(__FILE__).parent_path()/"fixtures"/"pe2976-realm-pointers.txt");
    assert(realms && std::getline(realms,provenance) && std::getline(realms,provenance));
    std::uintptr_t imageBase{},clientOwner{},clientActor{},serverOwner{},serverActor{},clientComponent{},clientProxy{};
    assert(realms>>std::hex>>imageBase>>clientOwner>>clientActor>>serverOwner>>serverActor>>clientComponent>>clientProxy);
    std::map<std::uintptr_t,std::uintptr_t> observedPointers;
    std::uintptr_t source{},value{};
    while(realms>>std::hex>>source>>value) observedPointers.emplace(source,value);
    auto pointer=[&](std::uintptr_t address,std::uintptr_t& out) {
        const auto found=observedPointers.find(address);
        if(found==observedPointers.end()) return false;
        out=found->second; return out!=0;
    };
    ClientAnchor clientAnchor{};
    assert(ResolveClientAnchor(imageBase+ClientRootRva,imageBase+ClientOwnerVtableRva,pointer,clientAnchor));
    assert(clientAnchor.owner==clientOwner && clientAnchor.owner!=serverOwner);
    std::uintptr_t resolvedComponent{},resolvedProxy{};
    assert(ResolveMovementComponent(clientActor,pointer,resolvedComponent,resolvedProxy));
    assert(resolvedComponent==clientComponent && resolvedProxy==clientProxy);
    assert(!ResolveMovementComponent(serverActor,pointer,resolvedComponent,resolvedProxy)); // Reproduces the old server-chain failure.
    const auto ownerField=clientAnchor.service+0x50;
    observedPointers[ownerField]=serverOwner;
    assert(!ResolveClientAnchor(imageBase+ClientRootRva,imageBase+ClientOwnerVtableRva,pointer,clientAnchor)); // Wrong realm vtable is never a fallback.
    observedPointers.erase(ownerField);
    assert(!ResolveClientAnchor(imageBase+ClientRootRva,imageBase+ClientOwnerVtableRva,pointer,clientAnchor)); // Loading/unreadable client anchor.
    observedPointers[ownerField]=clientOwner;
    assert(ResolveClientAnchor(imageBase+ClientRootRva,imageBase+ClientOwnerVtableRva,pointer,clientAnchor));
    ClientIdentity clientIdentity{clientAnchor,{clientOwner,clientActor,300,400,500}},clientChanged=clientIdentity;
    assert(SameClientIdentity(clientIdentity,clientChanged));
    ++clientChanged.anchor.service;
    assert(!SameClientIdentity(clientIdentity,clientChanged)); // New realm service, even with a reused owner.
    clientChanged=clientIdentity; ++clientChanged.player.stats;
    assert(!SameClientIdentity(clientIdentity,clientChanged)); // Rebuilt client vital chain invalidates pending work.
    clientChanged=clientIdentity; clientChanged.player.owner=serverOwner;
    assert(!SameClientIdentity(clientIdentity,clientChanged));
    std::uint64_t current=0, norm=0;
    assert(Fill(90000,120000,current,norm) && current==120000 && norm==30000);
    assert(Fill(120000,90000,current,norm) && current==120000 && norm==0);
    assert(!Fill(0,0,current,norm));
    assert(!Fill(~0ULL,1,current,norm));
    assert(!Fill(1,MaxStat+1,current,norm));
    assert(IsControlled(100,100,1)); assert(IsControlled(100,100,9));
    assert(!IsControlled(100,101,1)); assert(!IsControlled(100,100,3)); assert(!IsControlled(0,0,1));
    Identity original{100,200,300,400,500}, fresh=original;
    assert(SameIdentity(original,fresh)); fresh.stats=600; assert(!SameIdentity(original,fresh));
    fresh=original; fresh.actor=201; assert(!SameIdentity(original,fresh));
    fresh={}; assert(!SameIdentity(original,fresh));
    assert(StatAddress(0x1000,0x1000+31*0x90));
    assert(!StatAddress(0x1000,0x1000+32*0x90)); assert(!StatAddress(0x1000,0x1001));
    assert(!StatAddress(0x1000,0xFFF));
    assert(StatEnabled(0,true,false,false)); assert(!StatEnabled(0,false,true,true));
    assert(StatEnabled(22,false,true,false)); assert(StatEnabled(25,false,true,false));
    assert(StatEnabled(23,false,false,true)); assert(StatEnabled(26,false,false,true));
    assert(!StatEnabled(17,true,true,true)); assert(!StatEnabled(18,true,true,true));
    assert(ScaleOutgoing(-20,0,true,original,900,200)==-200000);
    assert(ScaleOutgoing(-20,0,true,original,400,200)==-20); // Incoming/self damage.
    assert(ScaleOutgoing(-20,0,true,original,900,201)==-20); // Another actor attacks.
    assert(ScaleOutgoing(-20,22,true,original,900,200)==-20); // A non-health resource.
    assert(ScaleOutgoing(20,0,true,original,900,200)==20); // Healing.
    assert(ScaleOutgoing(-20,0,false,original,900,200)==-20);
    assert(ScaleOutgoing((std::numeric_limits<std::int64_t>::min)(),0,true,original,900,200)==
        (std::numeric_limits<std::int64_t>::min)()/4);
    const float velocity[3]{20,-55,40}; float scaled[3]{};
    assert(ScaleVelocity(velocity,scaled,2.0f,100,100));
    assert(scaled[0]==40 && scaled[1]==-55 && scaled[2]==80);
    assert(velocity[0]==20 && velocity[1]==-55 && velocity[2]==40); // Native scratch vector is untouched.
    assert(!ScaleVelocity(velocity,scaled,2.0f,100,200)); // NPC component bypass.
    assert(!ScaleVelocity(velocity,scaled,2.0f,0,0));
    assert(!SpeedValue(0)); assert(!SpeedValue(3.25f)); assert(!SpeedValue(1.3f));
    assert(!SpeedValue((std::numeric_limits<float>::quiet_NaN)()));
    assert(SpeedValue(0.25f) && SpeedValue(3));
    NewMarkerPolicy markerPolicy;
    MarkerWarpRequest request{},tickRequest{};
    MarkerEvent event{41,original,MarkerEventKind::CustomSet,{100,200,300},1000};
    assert(!markerPolicy.Enabled());
    assert(markerPolicy.Observe(event,1000,request)==MarkerDecision::Ignore); // Default off.
    assert(markerPolicy.Enable(original,41));
    assert(markerPolicy.Observe(event,1000,request)==MarkerDecision::Ignore); // Enable never warps existing marker.
    event.revision=42;
    assert(markerPolicy.Observe(event,1001,request)==MarkerDecision::Queue);
    assert(markerPolicy.Observe(event,1002,request)==MarkerDecision::Ignore); // Never replay a consumed event.
    MarkerWarpQueue queue;
    assert(queue.Enqueue(request,1002));
    assert(queue.Remaining()==6);
    for(unsigned count=0;count<6;++count) {
        assert(queue.BeginTick(original,42,1003+count,tickRequest));
        assert(tickRequest.world.x==100 && tickRequest.world.y==200);
        queue.CompleteWrite(true);
    }
    assert(queue.Pending() && queue.AwaitingObservation() && queue.WritesCompleted()==6);
    assert(queue.BeginTick(original,42,1010,tickRequest)); // Writes alone never imply observed movement.
    queue.CompleteObservation();
    assert(!queue.Pending()); assert(!queue.BeginTick(original,42,1011,tickRequest));
    event.revision=43; // An identical coordinate re-pin is new when the setter has a new revision.
    assert(markerPolicy.Observe(event,1010,request)==MarkerDecision::Queue);
    assert(queue.Enqueue(request,1010));
    queue.CompleteWrite(false);
    assert(!queue.Pending());
    assert(markerPolicy.Observe(event,1020,request)==MarkerDecision::Ignore); // No delayed retry after a failed write.
    event.revision=44; event.kind=MarkerEventKind::Clear; event.world={};
    assert(markerPolicy.Observe(event,1020,request)==MarkerDecision::Cancel);
    event.revision=45; event.kind=MarkerEventKind::Other; event.world={400,500,600};
    assert(markerPolicy.Observe(event,1020,request)==MarkerDecision::Cancel); // Quest/route events are not custom pins.
    event.revision=46; event.kind=MarkerEventKind::CustomSet; event.observedTick=2000;
    assert(markerPolicy.Observe(event,2000,request)==MarkerDecision::Queue);
    assert(queue.Enqueue(request,2000));
    assert(!queue.BeginTick(original,47,2001,tickRequest) && !queue.Pending()); // Cleared/replaced event cancels.
    assert(queue.Enqueue(request,2000));
    assert(!queue.BeginTick(original,46,12000,tickRequest) && !queue.Pending()); // Never warp after a paused-world timeout.
    assert(!queue.Enqueue(request,12000));
    event.revision=47; event.observedTick=2000;
    assert(markerPolicy.Observe(event,12000,request)==MarkerDecision::Cancel);
    event.revision=48; event.observedTick=20000;
    assert(markerPolicy.Observe(event,19999,request)==MarkerDecision::Cancel); // Incoherent future event.
    event.revision=49; event.observedTick=20000; event.world.x=(std::numeric_limits<float>::quiet_NaN)();
    assert(markerPolicy.Observe(event,20000,request)==MarkerDecision::Cancel);
    markerPolicy.Disable(49);
    event.revision=50; event.world={100,200,300};
    assert(markerPolicy.Observe(event,20000,request)==MarkerDecision::Ignore);
    assert(markerPolicy.Enable(original,50));
    fresh=original; fresh.stats=600;
    assert(markerPolicy.RefreshIdentity(fresh,50) && !markerPolicy.Enabled());
    assert(markerPolicy.Enable(original,50));
    event.revision=51; event.player=fresh;
    assert(markerPolicy.Observe(event,20000,request)==MarkerDecision::Reset && !markerPolicy.Enabled());
    assert(markerPolicy.Enable(original,51));
    event.revision=52; event.player=original;
    assert(markerPolicy.Observe(event,20000,request)==MarkerDecision::Queue);
    assert(queue.Enqueue(request,20000));
    assert(!queue.BeginTick(fresh,52,20001,tickRequest)); // Same owner, rebuilt vital chain.
    markerPolicy.InvalidateContext(52);
    event.revision=53;
    assert(markerPolicy.Observe(event,20001,request)==MarkerDecision::Ignore && !markerPolicy.Enabled());
    assert(markerPolicy.Enable(original,1)); // A stale enable snapshot cannot rewind an already consumed event.
    assert(markerPolicy.LastRevision()==53);
    assert(markerPolicy.Observe(event,20001,request)==MarkerDecision::Ignore);
    const auto inFlightGeneration=markerPolicy.Generation();
    markerPolicy.Disable(53);
    assert(markerPolicy.Enable(original,53));
    event.revision=54;
    assert(markerPolicy.ObserveCompleted(event,inFlightGeneration,20001,request)==MarkerDecision::Ignore);
    assert(markerPolicy.Observe(event,20001,request)==MarkerDecision::Ignore); // Rejected in-flight completion is consumed.
    event.revision=55;
    assert(markerPolicy.ObserveCompleted(event,markerPolicy.Generation(),20001,request)==MarkerDecision::Queue);
    NewMarkerPolicy initiallyOff;
    const auto beforeFirstEnable=initiallyOff.Generation();
    assert(initiallyOff.Enable(original,55));
    event.revision=56;
    assert(initiallyOff.ObserveCompleted(event,beforeFirstEnable,20001,request)==MarkerDecision::Ignore);
    Position local{};
    assert(MarkerLocalDestination({-11935,850,100},{-11000,0,0},400,30,local));
    assert(local.x==-935 && local.y==880);
    assert(MarkerLocalDestination({-11935,850,100},{-12000,0,0},400,30,local));
    assert(local.x==65 && local.y==880); // Rebasing changes local coordinates, not world target.
    assert(MarkerLocalDestination({100,0,200},{0,100,0},850,30,local) && local.y==780);
    assert(!MarkerLocalDestination({}, {},850,30,local));
    MarkerAcknowledgement ack;
    ack.Begin({1,0,2},{10,0,20},5000);
    assert(ack.Check({1,0,2},5001)==MarkerAckDecision::Wait);
    assert(ack.Check({10,0,20},5002)==MarkerAckDecision::Accept);
    assert(ack.Check({10,0,20},8000)==MarkerAckDecision::Accept); // Already acknowledged; queue owns final deadline.
    assert(ack.Check({},8001)==MarkerAckDecision::Cancel); // Clear after acceptance, no replay.
    ack.Begin({1,0,2},{10,0,20},5000);
    assert(ack.Check({11,0,22},5001)==MarkerAckDecision::Cancel); // Unrelated destination never becomes an event.
    assert(ack.Check({10,0,20},15000)==MarkerAckDecision::Cancel); // Overall deadline forbids deferred teleport.
    ack.Begin({10,0,20},{10,0,20},5000);
    assert(ack.Check({10,0,20},5001)==MarkerAckDecision::Accept); // Identical re-pin still has unique sender revision.
    ack.Begin({},{10,0,20},5000);
    assert(ack.Check({},5001)==MarkerAckDecision::Wait); // No old marker is a valid baseline.
    assert(ack.Check({10,0,20},4999)==MarkerAckDecision::Cancel);
    assert(CustomMarkerSender(1000,1000,400,400,1,{1,0,2}));
    assert(!CustomMarkerSender(1001,1000,400,400,1,{1,0,2})); // A generic quest/navigation sender call.
    assert(!CustomMarkerSender(1000,1000,400,408,1,{1,0,2})); // The address nav+8 is not its pointed-to object.
    assert(!CustomMarkerSender(1000,1000,400,500,1,{1,0,2})); // Another player's sender object.
    assert(!CustomMarkerSender(1000,1000,400,400,0,{1,0,2})); // The map's clear action.
    assert(!CustomMarkerSender(1000,1000,400,400,1,{}));
    assert(!CustomMarkerSender(1000,1000,400,400,1,{1,(std::numeric_limits<float>::quiet_NaN)(),2}));
    float originalCoordinates[3]{1,0,2}; std::uint8_t originalSet=1; unsigned sends=0;
    const MarkerSenderArguments senderArgs{400,-123,originalCoordinates,&originalSet};
    ForwardMarkerSender([&](std::uintptr_t sender,std::int32_t context,float* coordinates,std::uint8_t* set) {
        ++sends; assert(sender==400 && context==-123 && coordinates==originalCoordinates && set==&originalSet);
    },senderArgs);
    assert(sends==1 && originalCoordinates[0]==1 && originalCoordinates[1]==0 && originalSet==1);
    Position frozen{};
    assert(FreezeMarkerWorld({10,0,20},{100,500,200},{1,30,2},frozen) && frozen.y==530);
    assert(FrozenDestinationLocal(frozen,{100,500,200},local) && local.y==30);
    assert(FrozenDestinationLocal(frozen,{100,600,200},local) && local.y==-70 && frozen.y==530);
    assert(FreezeMarkerWorld({10,0,20},{100,-30,200},{1,30,2},frozen) && frozen.y==0);
    assert(FrozenDestinationLocal(frozen,{100,600,200},local) && local.y==-600); // Frozen real zero is not another fallback.
    // Integrated producer/ack/consumer fixture: map stays open for five seconds,
    // then navigation accepts. No short acknowledgement timeout may drop it.
    NewMarkerPolicy mapPolicy; MarkerWarpQueue mapQueue; MarkerAcknowledgement mapAck;
    assert(mapPolicy.Enable(original,100));
    const auto arm=mapPolicy.Generation();
    MarkerEvent mapEvent{101,original,MarkerEventKind::CustomSet,{10,0,20},5000};
    assert(mapPolicy.ObserveCompleted(mapEvent,arm,5000,request)==MarkerDecision::Queue);
    assert(mapQueue.Enqueue(request,5000)); mapAck.Begin({},mapEvent.world,5000);
    assert(mapAck.Check({},9000)==MarkerAckDecision::Wait);
    assert(mapQueue.BeginTick(original,101,10000,tickRequest));
    assert(mapAck.Check({10,0,20},10000)==MarkerAckDecision::Accept);
    mapQueue.CompleteWrite(false); // One failed position write cannot be retried on the next tick.
    assert(!mapQueue.BeginTick(original,101,10001,tickRequest));
    assert(mapPolicy.ObserveCompleted(mapEvent,arm,10001,request)==MarkerDecision::Ignore);
    assert(NearDestination({100,200,300},{100,201,300}));
    assert(!NearDestination({100,200,300},{100.8f,200.8f,300})); // Euclidean radius, not per-axis cube.
    assert(!NearDestination({100,200,300},{100,201.01f,300}));
    assert(!NearDestination({100,200,300},{100,(std::numeric_limits<float>::quiet_NaN)(),300}));
    request={102,original,{100,200,300},5000,15000};
    assert(queue.Enqueue(request,5000));
    for(unsigned count=0;count<MarkerWarpQueue::HoldUpdates;++count) queue.CompleteWrite(true);
    assert(queue.AwaitingObservation());
    assert(!queue.BeginTick(original,102,15000,tickRequest) && !queue.Pending()); // Observation has the same bounded lifetime.
    std::cout << "Crimson Desert native logic checks passed.\n";
}
