#pragma once
#include <algorithm>
#include <cstdint>
#include <cmath>
#include <limits>

namespace crimson_logic {
constexpr std::uint64_t MaxStat = 1000000000000ULL;
inline constexpr std::uintptr_t ClientRootRva=0x6D69190;
inline constexpr std::uintptr_t ClientOwnerVtableRva=0x5585C40;
struct Identity { std::uintptr_t owner{}, actor{}, marker{}, root{}, stats{}; };
struct ClientAnchor { std::uintptr_t root{},service{},owner{}; };
struct ClientIdentity { ClientAnchor anchor{}; Identity player{}; };
template<class PointerReader> bool ResolveClientAnchor(std::uintptr_t globalAddress,std::uintptr_t expectedVtable,
                                                     PointerReader pointer,ClientAnchor& anchor) {
    ClientAnchor candidate{};
    std::uintptr_t vtable{};
    if(!pointer(globalAddress,candidate.root) || !pointer(candidate.root+0x30,candidate.service) ||
        !pointer(candidate.service+0x50,candidate.owner) || !pointer(candidate.owner,vtable) ||
        vtable!=expectedVtable) return false;
    anchor=candidate;
    return true;
}
template<class PointerReader> bool ResolveMovementComponent(std::uintptr_t clientActor,PointerReader pointer,
                                                           std::uintptr_t& component,std::uintptr_t& proxy) {
    std::uintptr_t object{};
    return clientActor && pointer(clientActor+0x40,object) && pointer(object+0x140,component) && pointer(component+0x2C0,proxy);
}
inline bool IsControlled(std::uintptr_t owner, std::uintptr_t pawn, std::uint8_t tag) {
    return owner != 0 && owner == pawn && (tag == 1 || tag == 9);
}
inline bool SameIdentity(const Identity& first, const Identity& second) {
    return first.owner && first.actor && first.marker && first.root && first.stats &&
        first.owner == second.owner && first.actor == second.actor && first.marker == second.marker &&
        first.root == second.root && first.stats == second.stats;
}
inline bool SameClientIdentity(const ClientIdentity& first,const ClientIdentity& second) {
    return first.anchor.root && first.anchor.service && first.anchor.owner==first.player.owner &&
        second.anchor.owner==second.player.owner && first.anchor.root==second.anchor.root &&
        first.anchor.service==second.anchor.service && SameIdentity(first.player,second.player);
}
inline bool Fill(std::uint64_t base, std::uint64_t cap, std::uint64_t& current, std::uint64_t& normalized) {
    const auto full = (std::max)(base, cap);
    if (full == 0 || full > MaxStat) return false;
    current = full;
    normalized = full - base;
    return true;
}
inline bool StatAddress(std::uintptr_t start, std::uintptr_t entry) {
    return start && entry >= start && (entry-start) % 0x90 == 0 && (entry-start)/0x90 < 32;
}
inline bool StatEnabled(std::int32_t type, bool health, bool stamina, bool spirit) {
    return (type==0 && health) || ((type==22 || type==25) && stamina) || ((type==23 || type==26) && spirit);
}
inline std::int64_t ScaleOutgoing(std::int64_t delta, std::uint16_t status, bool enabled,
                                  const Identity& player, std::uintptr_t target, std::uintptr_t attacker) {
    if (!enabled || status != 0 || delta >= 0 || !player.actor || !player.root ||
        target == player.root || attacker != player.actor) return delta;
    constexpr auto limit = (std::numeric_limits<std::int64_t>::min)()/4;
    return delta < limit/10000 ? limit : delta*10000;
}
inline bool SpeedValue(float value) {
    return std::isfinite(value) && value>=0.25f && value<=3.0f &&
        std::fabs(value*4.0f-std::round(value*4.0f))<0.001f;
}
inline bool ScaleVelocity(const float input[3], float output[3], float multiplier,
                          std::uintptr_t controlledComponent, std::uintptr_t actualComponent) {
    if (!controlledComponent || controlledComponent!=actualComponent || !SpeedValue(multiplier)) return false;
    for(int index=0;index<3;++index) if(!std::isfinite(input[index])) return false;
    output[0]=input[0]*multiplier; output[1]=input[1]; output[2]=input[2]*multiplier;
    return std::isfinite(output[0]) && std::isfinite(output[2]);
}
}
