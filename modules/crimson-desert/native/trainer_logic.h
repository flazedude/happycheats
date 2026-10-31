#pragma once
#include <algorithm>
#include <cstdint>
#include <cmath>
#include <limits>

namespace crimson_logic {
constexpr std::uint64_t MaxStat = 1000000000000ULL;
inline constexpr std::uintptr_t ClientRootRva=0x71B1A40;
inline constexpr std::uintptr_t ClientOwnerVtableRva=0x5907788;
inline constexpr std::uintptr_t ClientChildOwnerVtableRva=0x5907C90;
inline constexpr std::uintptr_t ClientLocalControlRva=0x5B2140;
inline constexpr std::uintptr_t StatusRegistryRva=0x71B2A18;
inline constexpr std::uint32_t HealthKey=1000000,StaminaKey=1000026,SpiritKey=1000027;
struct Identity { std::uintptr_t owner{}, actor{}, marker{}, root{}, stats{}; };
struct ClientAnchor { std::uintptr_t root{},service{},owner{},selfUser{}; };
struct ClientIdentity { ClientAnchor anchor{}; Identity player{}; };
template<class PointerReader> bool ResolveClientAnchor(std::uintptr_t globalAddress,std::uintptr_t expectedVtable,
                                                     PointerReader pointer,ClientAnchor& anchor,std::uintptr_t expectedChildVtable=0) {
    ClientAnchor candidate{};
    std::uintptr_t vtable{};
    if(!pointer(globalAddress,candidate.root) || !pointer(candidate.root+0x30,candidate.service) ||
        !pointer(candidate.service+0x50,candidate.owner) || !pointer(candidate.owner,vtable) ||
        (vtable!=expectedVtable && (!expectedChildVtable || vtable!=expectedChildVtable))) return false;
    anchor=candidate;
    return true;
}

template<class PointerReader,class ByteReader> bool ResolveControlledClientAnchor(
    std::uintptr_t globalAddress,std::uintptr_t expectedVtable,PointerReader pointer,ByteReader byte,
    ClientAnchor& anchor,std::uintptr_t expectedChildVtable=0) {
    ClientAnchor candidate{};
    std::uintptr_t possessor{},pawn{};
    std::uint8_t local{};
    // PE3446 ClientActor virtual +178 is the engine's local-control predicate:
    // owner+A0 equals this service's self-user (+58), and owner+94 is nonzero.
    // Normal and ChildOnly share this exact method on PE3446. Kliff's owner
    // was observed as ChildOnly on PE3424; retain every local-self check.
    if(!ResolveClientAnchor(globalAddress,expectedVtable,pointer,candidate,expectedChildVtable) ||
        !pointer(candidate.service+0x58,candidate.selfUser) ||
        !pointer(candidate.owner+0xA0,possessor) || possessor!=candidate.selfUser ||
        !byte(candidate.owner+0x94,local) || !local ||
        !pointer(possessor+0xD0,pawn) || pawn!=candidate.owner) return false;
    ClientAnchor fresh{};
    std::uintptr_t selfUserAgain{};
    if(!ResolveClientAnchor(globalAddress,expectedVtable,pointer,fresh,expectedChildVtable) ||
        fresh.root!=candidate.root || fresh.service!=candidate.service || fresh.owner!=candidate.owner ||
        !pointer(candidate.service+0x58,selfUserAgain) || selfUserAgain!=candidate.selfUser) return false;
    anchor=candidate;
    return true;
}
template<class PointerReader> bool ResolveMovementComponent(std::uintptr_t clientActor,PointerReader pointer,
                                                           std::uintptr_t& component,std::uintptr_t& proxy) {
    std::uintptr_t object{};
    return clientActor && pointer(clientActor+0x40,object) && pointer(object+0x140,component) && pointer(component+0x2C0,proxy);
}
inline bool IsControlled(std::uintptr_t owner, std::uintptr_t pawn, std::uint8_t tag) {
    // Only tag 1 has current-build player-class evidence. Historical tag 9 is
    // deliberately rejected until independently established for this profile.
    return owner != 0 && owner == pawn && tag == 1;
}
inline bool SameIdentity(const Identity& first, const Identity& second) {
    return first.owner && first.actor && first.marker && first.root && first.stats &&
        first.owner == second.owner && first.actor == second.actor && first.marker == second.marker &&
        first.root == second.root && first.stats == second.stats;
}
inline bool SameClientIdentity(const ClientIdentity& first,const ClientIdentity& second) {
    return first.anchor.root && first.anchor.service && first.anchor.selfUser && first.anchor.owner==first.player.owner &&
        second.anchor.owner==second.player.owner && first.anchor.root==second.anchor.root &&
        first.anchor.service==second.anchor.service && first.anchor.selfUser==second.anchor.selfUser &&
        SameIdentity(first.player,second.player);
}
inline bool Fill(std::uint64_t base, std::uint64_t cap, std::uint64_t& current, std::uint64_t& normalized) {
    const auto full = (std::max)(base, cap);
    if (full == 0 || full > MaxStat) return false;
    current = full;
    normalized = full - base;
    return true;
}
inline bool StatAddress(std::uintptr_t start, std::uintptr_t entry,std::uint32_t count) {
    return count && count<=0xFFFF && start && entry >= start && (entry-start) % 0x90 == 0 &&
        (entry-start)/0x90 < (std::min)(count,std::uint32_t{32});
}
template<class PointerReader,class DwordReader> bool ResolveStatusKey(std::uintptr_t globalAddress,
    std::uint16_t reference,PointerReader pointer,DwordReader dword,std::uint32_t& key) {
    std::uintptr_t registry{},table{},row{},again{};
    std::uint32_t count{},candidate{},valueAgain{};
    // Current engine getter 0x3DCF80 bounds a WORD table reference by +08,
    // then reads its loaded row from +58. Deserializer stores the stable key
    // at row+08. A missing cache row is rejected; never invoke the lazy loader.
    if(reference==0xFFFF || !pointer(globalAddress,registry) || !dword(registry+8,count) ||
        !count || count>0xFFFF || reference>=count || !pointer(registry+0x58,table) ||
        !pointer(table+sizeof(std::uintptr_t)*reference,row) || !dword(row+8,candidate) ||
        !pointer(globalAddress,again) || again!=registry || !dword(registry+8,valueAgain) || valueAgain!=count ||
        !pointer(registry+0x58,again) || again!=table ||
        !pointer(table+sizeof(std::uintptr_t)*reference,again) || again!=row ||
        !dword(row+8,valueAgain) || valueAgain!=candidate) return false;
    key=candidate;
    return true;
}
inline bool StatEnabled(std::uint32_t key, bool health, bool stamina, bool spirit) {
    return (key==HealthKey && health) || (key==StaminaKey && stamina) || (key==SpiritKey && spirit);
}
inline std::int64_t ScaleOutgoing(std::int64_t delta, std::uint32_t statusKey, bool enabled,
                                  const Identity& player, std::uintptr_t target, std::uintptr_t attacker) {
    if (!enabled || statusKey != HealthKey || delta >= 0 || !player.actor || !player.root ||
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
