#pragma once
#include "trainer_logic.h"
#include <cstdint>
#include <limits>

// Pure policy only: this file neither discovers markers nor writes game memory.
// A caller must prove a CustomSet event came from the user's custom map-marker
// setter, not infer it from a changed navigation/quest destination vector.
namespace crimson_logic {
inline constexpr std::uint64_t MarkerRequestLifetimeMs=10000;
struct Position { float x{},y{},z{}; };
inline bool FinitePosition(const Position& value) {
    constexpr float limit=1.0e9f;
    return std::isfinite(value.x) && std::isfinite(value.y) && std::isfinite(value.z) &&
        std::fabs(value.x)<=limit && std::fabs(value.y)<=limit && std::fabs(value.z)<=limit;
}
inline bool MarkerPosition(const Position& value) {
    return FinitePosition(value) && (value.x!=0 || value.y!=0 || value.z!=0);
}
inline bool SamePosition(const Position& a,const Position& b) {
    return FinitePosition(a) && FinitePosition(b) && a.x==b.x && a.y==b.y && a.z==b.z;
}
// This is an observation tolerance in native world-coordinate units, not a
// terrain, collision, actor-authority, or safe-landing guarantee.
inline constexpr double MarkerObservationTolerance=1.0;
inline bool NearDestination(const Position& requested,const Position& observed) {
    if(!FinitePosition(requested) || !FinitePosition(observed)) return false;
    const double dx=double(requested.x)-observed.x,dy=double(requested.y)-observed.y,dz=double(requested.z)-observed.z;
    return dx*dx+dy*dy+dz*dz<=MarkerObservationTolerance*MarkerObservationTolerance;
}
inline bool CustomMarkerSender(std::uintptr_t returnAddress,std::uintptr_t expectedReturn,
                               std::uintptr_t navSenderObject,std::uintptr_t senderObject,
                               std::uint8_t set,const Position& marker) {
    return returnAddress==expectedReturn && expectedReturn!=0 && navSenderObject!=0 &&
        navSenderObject==senderObject && set==1 && MarkerPosition(marker);
}
inline bool FreezeMarkerWorld(const Position& marker,const Position& origin,const Position& currentLocal,Position& world) {
    if(!MarkerPosition(marker) || !FinitePosition(origin) || !FinitePosition(currentLocal)) return false;
    world=marker;
    if(world.y==0) world.y=origin.y+currentLocal.y;
    return MarkerPosition(world);
}
inline bool FrozenDestinationLocal(const Position& world,const Position& origin,Position& local) {
    if(!MarkerPosition(world) || !FinitePosition(origin)) return false;
    local={world.x-origin.x,world.y-origin.y,world.z-origin.z};
    return FinitePosition(local);
}
struct MarkerSenderArguments {
    std::uintptr_t sender{};
    std::int32_t context{};
    float* coordinates{};
    std::uint8_t* set{};
};
template<class Original> inline void ForwardMarkerSender(Original original,const MarkerSenderArguments& args) {
    original(args.sender,args.context,args.coordinates,args.set);
}

enum class MarkerAckDecision { Wait, Accept, Cancel };
// A sender event is user intent, not proof the asynchronous request reached the
// local navigation object. Until acknowledgement, only the previous baseline
// is tolerated. A different/cleared marker cancels and can never trigger a warp.
class MarkerAcknowledgement {
public:
    static constexpr std::uint64_t LifetimeMs=MarkerRequestLifetimeMs;
    void Begin(const Position& baseline,const Position& target,std::uint64_t now) {
        baseline_=baseline; target_=target; started_=now; accepted_=false;
    }
    MarkerAckDecision Check(const Position& observed,std::uint64_t now) {
        if(now<started_ || !FinitePosition(observed)) return MarkerAckDecision::Cancel;
        if(accepted_) return SamePosition(observed,target_) ? MarkerAckDecision::Accept : MarkerAckDecision::Cancel;
        if(now-started_>=LifetimeMs) return MarkerAckDecision::Cancel;
        if(SamePosition(observed,target_)) { accepted_=true; return MarkerAckDecision::Accept; }
        return SamePosition(observed,baseline_) ? MarkerAckDecision::Wait : MarkerAckDecision::Cancel;
    }
private:
    Position baseline_{},target_{};
    std::uint64_t started_{};
    bool accepted_{};
};
enum class MarkerEventKind { CustomSet, Clear, Other };
struct MarkerEvent {
    std::uint64_t revision{};
    Identity player{};
    MarkerEventKind kind=MarkerEventKind::Other;
    Position world{};
    std::uint64_t observedTick{};
};
struct MarkerWarpRequest {
    std::uint64_t revision{};
    Identity player{};
    Position world{};
    std::uint64_t createdTick{},deadlineTick{};
};
enum class MarkerDecision { Ignore, Queue, Cancel, Reset };

class NewMarkerPolicy {
public:
    static constexpr std::uint64_t RequestLifetimeMs=MarkerRequestLifetimeMs;
    bool Enable(const Identity& player,std::uint64_t latestRevision) {
        ++generation_;
        player_=player;
        lastRevision_=(std::max)(lastRevision_,latestRevision); // Existing markers are the baseline, never a request.
        enabled_=SameIdentity(player,player);
        return enabled_;
    }
    void Disable(std::uint64_t latestRevision) {
        ++generation_;
        enabled_=false;
        lastRevision_=(std::max)(lastRevision_,latestRevision);
    }
    // Use a proven map/world invalidation event even when owner pointers remain
    // unchanged. Clearing/replacing markers is handled separately by Observe.
    void InvalidateContext(std::uint64_t latestRevision) {
        player_={};
        Disable(latestRevision);
    }
    // Call on load/character changes even if no marker event arrived.
    // Returning true instructs the owner to cancel any queued warp as well.
    bool RefreshIdentity(const Identity& player,std::uint64_t latestRevision) {
        if(SameIdentity(player_,player)) return false;
        player_=player;
        Disable(latestRevision);
        return true;
    }
    bool Enabled() const { return enabled_; }
    std::uint64_t Generation() const { return generation_; }
    std::uint64_t LastRevision() const { return lastRevision_; }
    // Capture Generation under the external state lock BEFORE invoking the
    // original setter. A request already in flight when the user arms the
    // feature cannot count as a newly placed marker after arming.
    MarkerDecision ObserveCompleted(const MarkerEvent& event,std::uint64_t capturedGeneration,
                                    std::uint64_t now,MarkerWarpRequest& request) {
        if(capturedGeneration!=generation_) {
            lastRevision_=(std::max)(lastRevision_,event.revision);
            return MarkerDecision::Ignore;
        }
        return Observe(event,now,request);
    }
    MarkerDecision Observe(const MarkerEvent& event,std::uint64_t now,MarkerWarpRequest& request) {
        if(event.revision<=lastRevision_) return MarkerDecision::Ignore;
        // Consume before validating. A failed request must never become a
        // deferred teleport when readiness or terrain data later changes.
        lastRevision_=event.revision;
        if(!enabled_) return MarkerDecision::Ignore;
        if(!SameIdentity(player_,event.player)) {
            enabled_=false;
            return MarkerDecision::Reset;
        }
        if(event.kind!=MarkerEventKind::CustomSet || !MarkerPosition(event.world) ||
            event.observedTick>now || now-event.observedTick>=RequestLifetimeMs ||
            event.observedTick>(std::numeric_limits<std::uint64_t>::max)()-RequestLifetimeMs)
            return MarkerDecision::Cancel;
        request={event.revision,event.player,event.world,event.observedTick,event.observedTick+RequestLifetimeMs};
        return MarkerDecision::Queue;
    }
private:
    bool enabled_{};
    Identity player_{};
    std::uint64_t lastRevision_{};
    std::uint64_t generation_{};
};

// One owner must serialize this state. Hook/worker threads must not mutate this
// object concurrently; publish immutable requests or use an external lock.
class MarkerWarpQueue {
public:
    static constexpr unsigned HoldUpdates=6;
    bool Enqueue(const MarkerWarpRequest& request,std::uint64_t now) {
        Cancel();
        if(request.revision==0 || !SameIdentity(request.player,request.player) || !MarkerPosition(request.world) ||
            request.createdTick>now || request.deadlineTick<=now || request.deadlineTick<request.createdTick ||
            request.deadlineTick-request.createdTick>NewMarkerPolicy::RequestLifetimeMs) return false;
        request_=request;
        remaining_=HoldUpdates;
        awaitingObservation_=false;
        return true;
    }
    bool BeginTick(const Identity& player,std::uint64_t latestRevision,std::uint64_t now,MarkerWarpRequest& request) {
        if(!Pending()) return false;
        if(!SameIdentity(request_.player,player) || latestRevision!=request_.revision ||
            now<request_.createdTick || now>=request_.deadlineTick) { Cancel(); return false; }
        request=request_;
        return true;
    }
    void CompleteWrite(bool writeAndReadbackSucceeded) {
        if(!writeAndReadbackSucceeded) Cancel();
        else if(remaining_ && --remaining_==0) awaitingObservation_=true;
    }
    void CompleteObservation() { Cancel(); }
    void Cancel() { remaining_=0; awaitingObservation_=false; request_={}; }
    bool Pending() const { return remaining_!=0 || awaitingObservation_; }
    bool AwaitingObservation() const { return awaitingObservation_; }
    unsigned WritesCompleted() const { return Pending() ? HoldUpdates-remaining_ : 0; }
    unsigned Remaining() const { return remaining_; }
private:
    MarkerWarpRequest request_{};
    unsigned remaining_{};
    bool awaitingObservation_{};
};

// Conversion only, not a terrain/ground check. The marker is already absolute;
// adding origin to it would double-rebase the destination. A zero marker Y uses
// the supplied current WORLD height, which is an explicitly experimental fallback.
inline bool MarkerLocalDestination(const Position& marker,const Position& origin,
                                   float fallbackWorldY,float rise,Position& local) {
    if(!MarkerPosition(marker) || !FinitePosition(origin) || !std::isfinite(fallbackWorldY) ||
        !std::isfinite(rise)) return false;
    local={marker.x-origin.x,(marker.y==0 ? fallbackWorldY : marker.y)-origin.y+rise,marker.z-origin.z};
    return FinitePosition(local);
}
}
