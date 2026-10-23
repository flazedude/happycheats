// Production cache publication and CommitHook against fixture-owned memory.
// No game process, live hook installation, injection, or game state is accessed.
#include "../trainer.cpp"
#include <cassert>

namespace fixture {
struct Block {
    std::array<unsigned char,0x1400> bytes{};
    uintptr_t Address() { return reinterpret_cast<uintptr_t>(bytes.data()); }
    template<class T> void Put(std::size_t offset,const T& value) {
        assert(offset+sizeof(value)<=bytes.size());
        std::memcpy(bytes.data()+offset,&value,sizeof(value));
    }
};
struct Body {
    Block owner,actor,marker,root,stats,descriptor,possessor;
    Player Build() {
        owner.Put(0x68,actor.Address()); owner.Put(0x88,descriptor.Address());
        descriptor.Put(1,std::uint8_t{1}); owner.Put(0xA0,possessor.Address());
        possessor.Put(0xD0,owner.Address()); actor.Put(0x20,marker.Address());
        marker.Put(0x18,root.Address()); root.Put(0,marker.Address());
        root.Put(0x58,stats.Address()); stats.Put(0,std::int32_t{0});
        return {owner.Address(),actor.Address(),marker.Address(),root.Address(),stats.Address()};
    }
};
constexpr std::uint64_t Base=100,Cap=125;
constexpr std::int64_t Opaque=0x12345678,Return=0x61234567;
constexpr std::uint16_t Flag=0x2468;
Player server{};
Client client{};
unsigned calls{},refreshes{};
bool refreshInOriginal{};
void Stat(uintptr_t address,std::int32_t type,std::uint64_t current=Base) {
    assert(Write(address,type));
    assert(Write(address+8,current));
    assert(Write(address+0x18,Base));
    assert(Write(address+0x20,current-Base));
    assert(Write(address+0x30,Cap));
}
std::uint64_t Current(uintptr_t address) {
    std::uint64_t current{}; assert(Read(address+8,current)); return current;
}
void Full(uintptr_t address) {
    std::uint64_t normalized{}; assert(Read(address+0x20,normalized));
    assert(Current(address)==Cap && normalized==Cap-Base);
}
std::int64_t Original(void* entry,std::int64_t opaque,std::int64_t target,std::uint16_t flag) {
    ++calls; assert(opaque==Opaque && flag==Flag);
    const auto address=reinterpret_cast<uintptr_t>(entry);
    const auto current=static_cast<std::uint64_t>(target);
    assert(Write(address+0x20,current-Base) && Write(address+8,current));
    if(refreshInOriginal) {
        // Exercise the real periodic refresh inside the original call, before
        // CommitHook reaches its fresh identity check and post-commit refill.
        const auto publication=RefreshIdentityCache(true,true,server,true,client);
        assert(!publication.player && !publication.client);
        assert(g_owner.load()==server.owner && g_clientOwner.load()==client.player.owner);
        ++refreshes;
    }
    return Return;
}
void Commit(uintptr_t entry,std::int64_t target=0) {
    const auto before=calls;
    assert(CommitHook(reinterpret_cast<void*>(entry),Opaque,target,Flag)==Return);
    assert(calls==before+1);
}
void Begin() {
    DisableAll(); refreshInOriginal=false;
    g_leaseValid=true; g_leaseDeadline=GetTickCount64()+60000;
    RefreshIdentityCache(true,true,server,true,client);
    g_health=true; g_stamina=false; g_spirit=false;
    Stat(server.stats,0);
}
void ArmAll() {
    g_health=true; g_stamina=true; g_spirit=true; g_oneHit=true;
    g_speedEnabled=true; g_speed=2.0f;
}
void Disabled() {
    assert(!g_health && !g_stamina && !g_spirit && !g_oneHit && !g_speedEnabled && g_speed==1.0f);
}
void Pass(const char* description) { std::printf("PASS %s\n",description); }
}

int main() {
    using namespace fixture;
    Body playerBody,clientBody,npcBody,replacementBody;
    Block clientRoot,clientService;
    server=playerBody.Build();
    const auto clientPlayer=clientBody.Build();
    client={{clientRoot.Address(),clientService.Address(),clientPlayer.owner},clientPlayer};
    const auto npc=npcBody.Build();
    const auto replacement=replacementBody.Build();
    g_originalCommit=Original;

    Begin();
    // Reproduce the old worker's clear -> fields -> owner publication gap.
    // The live body remains valid, but the real CommitHook skips its refill.
    g_owner.store(0,std::memory_order_release);
    Commit(server.stats);
    assert(Current(server.stats)==0 && g_health.load());
    g_owner.store(server.owner,std::memory_order_release);
    Player fresh{}; assert(CurrentPlayer(fresh) && crimson_logic::SameIdentity(server,fresh));
    assert(Current(server.stats)==0); // Restoring the cache does not repair the lost event.
    Pass("old unchanged-owner invalidation deterministically loses an actual health refill");

    Begin(); refreshInOriginal=true;
    for(unsigned i=0;i<32;++i) { Commit(server.stats); Full(server.stats); }
    assert(refreshes==32 && g_health.load());
    Pass("unchanged production refresh publishes neither cache and preserves health refill during commits");

    Begin();
    Stat(npc.stats,0); Commit(npc.stats); assert(Current(npc.stats)==0);
    const auto stamina=server.stats+0x90,spirit=server.stats+0x120,other=server.stats+0x1B0;
    Stat(stamina,22); Stat(spirit,23); Stat(other,99);
    Commit(stamina); Commit(spirit); assert(Current(stamina)==0 && Current(spirit)==0);
    g_stamina=true; g_spirit=true; refreshInOriginal=true;
    for(const std::int32_t type:{22,25}) { Stat(stamina,type); Commit(stamina); Full(stamina); }
    for(const std::int32_t type:{23,26}) { Stat(spirit,type); Commit(spirit); Full(spirit); }
    Commit(other); assert(Current(other)==0);
    Pass("NPC and disabled or unsupported resources pass through; enabled stamina and spirit still refill");

    // Every field participates: an unchanged owner alone is insufficient.
    const std::array<uintptr_t Player::*,5> fields{{&Player::owner,&Player::actor,&Player::marker,&Player::root,&Player::stats}};
    for(const auto field:fields) {
        Begin(); ArmAll(); Player changed=server; changed.*field+=0x20;
        const auto publication=RefreshIdentityCache(true,true,changed,true,client);
        assert(publication.player && !publication.client); Disabled();
    }
    Pass("all five server identity fields invalidate controls on change");

    Begin(); ArmAll();
    // A real owner-address reuse with a new vital chain must fail fresh checks
    // even before the worker notices it, then require explicit re-enabling.
    playerBody.owner.Put(0x68,replacement.actor);
    Stat(server.stats,0); Commit(server.stats); assert(Current(server.stats)==0);
    Player reused{}; assert(ValidatePlayer(server.owner,reused));
    assert(reused.owner==server.owner && reused.stats==replacement.stats);
    auto publication=RefreshIdentityCache(true,true,reused,true,client);
    assert(publication.player && !publication.client); Disabled();
    Stat(reused.stats,0); Commit(reused.stats); assert(Current(reused.stats)==0);
    playerBody.owner.Put(0x68,server.actor);
    Pass("owner reuse fails the fresh chain check and never carries enabled controls to the new body");

    for(unsigned field=0;field<7;++field) {
        Begin(); ArmAll(); auto changed=client;
        switch(field) {
        case 0: changed.anchor.root+=0x20; break;
        case 1: changed.anchor.service+=0x20; break;
        case 2: changed.anchor.owner+=0x20; changed.player.owner=changed.anchor.owner; break;
        case 3: changed.player.actor+=0x20; break;
        case 4: changed.player.marker+=0x20; break;
        case 5: changed.player.root+=0x20; break;
        case 6: changed.player.stats+=0x20; break;
        }
        publication=RefreshIdentityCache(true,true,server,true,changed);
        assert(!publication.player && publication.client); Disabled();
    }
    Pass("client anchor and every client identity field reset controls without republishing the server");

    Begin(); ArmAll();
    publication=RefreshIdentityCache(true,true,server,false,Client{});
    assert(!publication.player && publication.client && !g_clientReady && !g_clientOwner); Disabled();
    publication=RefreshIdentityCache(true,true,server,false,Client{});
    assert(!publication.player && !publication.client);
    Begin(); ArmAll();
    publication=RefreshIdentityCache(true,false,Player{},true,client);
    assert(publication.player && !publication.client && !g_owner && !g_playerReady); Disabled();
    publication=RefreshIdentityCache(true,false,Player{},true,client);
    assert(!publication.player && !publication.client);
    Pass("unavailable identities clear their caches and controls; repeated absence does not republish");

    Begin(); ArmAll(); g_playerReady=false; // Worker exception recovery.
    publication=RefreshIdentityCache(true,true,server,true,client);
    assert(!publication.player && !publication.client && g_playerReady); Disabled();
    Pass("readiness recovers without pointer invalidation or silently re-enabling controls");

    Begin(); g_leaseDeadline=0;
    Commit(server.stats); assert(Current(server.stats)==0);
    g_leaseValid=false; ArmAll();
    publication=RefreshIdentityCache(false,true,server,true,client);
    assert(!publication.player && !publication.client); Disabled();
    Commit(server.stats); assert(Current(server.stats)==0);
    Pass("expired or missing lease prevents refill and disables controls despite unchanged identities");

    Begin(); DisableAll(); Commit(server.stats); assert(Current(server.stats)==0);
    Pass("explicit disable remains effective with a valid unchanged player");
    std::puts("PASS production health/cache fixture; no game accessed");
    return 0;
}
