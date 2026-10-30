// Production cache publication and CommitHook against fixture-owned memory.
// No game process, live hook installation, injection, or game state is accessed.
#include "../trainer.cpp"
#include <cassert>

namespace fixture {
constexpr std::uint16_t HealthReference=5,StaminaReference=74,SpiritReference=91;
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
        root.Put(0x58,stats.Address()); stats.Put(0,HealthReference);
        root.Put(0x60,std::uint32_t{20});
        stats.Put(2,std::uint16_t{0xBEEF}); // PE3424 writes only the WORD status reference; padding is not its high half.
        return {owner.Address(),actor.Address(),marker.Address(),root.Address(),stats.Address()};
    }
};
constexpr std::uint64_t Base=100,Cap=125;
constexpr std::int64_t Opaque=0x12345678,Return=0x61234567;
constexpr std::uint16_t Flag=0x2468;
Player server{};
Client client{};
unsigned calls{},refreshes{};
std::int64_t observedDamage{};
std::uint16_t observedStatus{};
bool refreshInOriginal{};
void Stat(uintptr_t address,std::uint16_t type,std::uint64_t current=Base) {
    assert(Write(address,type));
    assert(Write(address+2,std::uint16_t{0xBEEF}));
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
std::int64_t OriginalDamage(void*,std::uint16_t status,std::int64_t,std::int64_t delta,uintptr_t,
    char,char,char,char,char,void*) {
    observedStatus=status; observedDamage=delta; return Return;
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
    Stat(server.stats,HealthReference);
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
    auto image=static_cast<unsigned char*>(VirtualAlloc(nullptr,crimson_build::ExpectedImageSize,MEM_RESERVE,PAGE_NOACCESS));
    assert(image); g_game=reinterpret_cast<uintptr_t>(image);
    assert(VirtualAlloc(image+(crimson_logic::StatusRegistryRva&~uintptr_t{0xFFF}),4096,MEM_COMMIT,PAGE_READWRITE));
    Block registry;
    struct Row { std::uint64_t header{}; std::uint32_t key{},padding{}; };
    std::array<Row,128> rows{};
    std::array<uintptr_t,128> rowPointers{};
    auto bind=[&](std::uint16_t reference,std::uint32_t key) {
        rows[reference].key=key; rowPointers[reference]=reinterpret_cast<uintptr_t>(&rows[reference]);
    };
    bind(HealthReference,1000000); bind(StaminaReference,1000026); bind(SpiritReference,1000027);
    bind(22,1000026); bind(23,1000027); bind(24,1000078); bind(25,1000019); bind(26,1000058);
    bind(0,1000078); bind(99,2000000); // Ordinal zero is deliberately not HP in this synthetic reordered table.
    registry.Put(8,std::uint32_t{128}); registry.Put(0x58,reinterpret_cast<uintptr_t>(rowPointers.data()));
    assert(Write(g_game+crimson_logic::StatusRegistryRva,registry.Address()));
    Body playerBody,clientBody,npcBody,replacementBody;
    Block clientRoot,clientService;
    server=playerBody.Build();
    const auto clientPlayer=clientBody.Build();
    client={{clientRoot.Address(),clientService.Address(),clientPlayer.owner,clientBody.possessor.Address()},clientPlayer};
    const auto npc=npcBody.Build();
    const auto replacement=replacementBody.Build();
    g_originalCommit=Original;
    g_originalDamage=OriginalDamage;

    Player classified{};
    playerBody.descriptor.Put(1,std::uint8_t{9});
    assert(!ValidatePlayer(server.owner,classified)); // The PE3424 profile does not inherit historical tag 9 acceptance.
    playerBody.descriptor.Put(1,std::uint8_t{1});
    assert(ValidatePlayer(server.owner,classified));

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
    Stat(npc.stats,HealthReference); Commit(npc.stats); assert(Current(npc.stats)==0);
    const auto stamina=server.stats+0x90,spirit=server.stats+0x120,other=server.stats+0x1B0;
    Stat(stamina,22); Stat(spirit,23); Stat(other,99);
    Commit(stamina); Commit(spirit); assert(Current(stamina)==0 && Current(spirit)==0);
    g_stamina=true; g_spirit=true; refreshInOriginal=true;
    for(const std::uint16_t reference:{std::uint16_t{22},StaminaReference}) { Stat(stamina,reference); Commit(stamina); Full(stamina); }
    for(const std::uint16_t reference:{std::uint16_t{23},SpiritReference}) { Stat(spirit,reference); Commit(spirit); Full(spirit); }
    Commit(other); assert(Current(other)==0);
    Pass("NPC and disabled or unsupported resources pass through; enabled stamina and spirit still refill");

    Begin(); g_stamina=true; g_spirit=true;
    for(const std::uint16_t reference:{0,24,25,26,99,100,128,0xFFFF}) {
        Stat(other,reference); Commit(other); assert(Current(other)==0);
    }
    Pass("Breath, both weapon stats, unknown keys, unloaded rows and invalid references are never refilled");
    const auto adjacentEntry=server.stats+20*0x90;
    Stat(adjacentEntry,StaminaReference); Commit(adjacentEntry); assert(Current(adjacentEntry)==0);
    Pass("a valid resource entry just beyond the actual stat count is not treated as player-owned");

    Begin(); g_oneHit=true;
    for(const auto reference:{HealthReference,std::uint16_t{0},std::uint16_t{25},std::uint16_t{100}}) {
        assert(DamageHook(reinterpret_cast<void*>(npc.root),reference,0,-20,server.owner,0,0,0,0,0,nullptr)==Return);
        assert(observedStatus==reference && observedDamage==(reference==HealthReference ? -200000 : -20));
    }
    Pass("damage scales the relocated HP reference and preserves Breath, equipment and unloaded references");

    Begin(); g_stamina=true; g_spirit=true;
    for(const std::uint16_t unsupported:{0x100,0x116,0x117,0x119,0x11A}) {
        Stat(other,unsupported); Commit(other); assert(Current(other)==0);
    }
    Stat(other,99); assert(Write(other+2,std::uint16_t{22}));
    Commit(other); assert(Current(other)==0);
    Pass("WORD status references ignore nonzero padding but never match only a low byte or neighboring field");

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
    Stat(server.stats,HealthReference); Commit(server.stats); assert(Current(server.stats)==0);
    Player reused{}; assert(ValidatePlayer(server.owner,reused));
    assert(reused.owner==server.owner && reused.stats==replacement.stats);
    auto publication=RefreshIdentityCache(true,true,reused,true,client);
    assert(publication.player && !publication.client); Disabled();
    Stat(reused.stats,HealthReference); Commit(reused.stats); assert(Current(reused.stats)==0);
    playerBody.owner.Put(0x68,server.actor);
    Pass("owner reuse fails the fresh chain check and never carries enabled controls to the new body");

    for(unsigned field=0;field<8;++field) {
        Begin(); ArmAll(); auto changed=client;
        switch(field) {
        case 0: changed.anchor.root+=0x20; break;
        case 1: changed.anchor.service+=0x20; break;
        case 2: changed.anchor.owner+=0x20; changed.player.owner=changed.anchor.owner; break;
        case 3: changed.player.actor+=0x20; break;
        case 4: changed.player.marker+=0x20; break;
        case 5: changed.player.root+=0x20; break;
        case 6: changed.player.stats+=0x20; break;
        case 7: changed.anchor.selfUser+=0x20; break;
        }
        publication=RefreshIdentityCache(true,true,server,true,changed);
        assert(!publication.player && publication.client); Disabled();
        if(field==7) assert(g_clientOwner==client.player.owner && g_clientSelfUser==changed.anchor.selfUser);
    }
    Pass("client anchor, self-user and every client identity field reset controls without republishing the server");

    Begin(); ArmAll();
    publication=RefreshIdentityCache(true,true,server,false,Client{});
    assert(!publication.player && publication.client && !g_clientReady && !g_clientOwner && !g_clientSelfUser); Disabled();
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
    assert(VirtualFree(image,0,MEM_RELEASE));
    std::puts("PASS production health/cache fixture; no game accessed");
    return 0;
}
