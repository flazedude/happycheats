#include "../native_api.h"
#include <bit>
#include <cstdlib>
#include <iostream>
#include <unordered_map>
#include <vector>
struct Call { std::uint64_t hash; std::vector<std::uint64_t> args; };
std::vector<Call> calls;
std::unordered_map<std::uint64_t,std::uint64_t> replies;
int assertions = 0;
void check(bool okay, const char* message) { ++assertions; if(!okay) { std::cerr << "FAIL " << message << '\n'; std::exit(1); } }
void init(std::uint64_t hash) { calls.push_back({hash,{}}); }
void push(std::uint64_t argument) { calls.back().args.push_back(argument); }
std::uint64_t* invoke() { static std::uint64_t result; result = replies[calls.back().hash]; return &result; }
void equal(std::size_t index, std::uint64_t hash, std::vector<std::uint64_t> args) {
    check(index < calls.size() && calls[index].hash == hash && calls[index].args == args, "native hash and argument ABI");
}
int main() {
    rdr2::ScriptHook hook; hook.init = init; hook.push = push; hook.call = invoke;
    rdr2::NativeBackend native(hook);
    replies[0x15D757606D170C3CULL] = 500;
    native.health(101);
    equal(0,0x15D757606D170C3CULL,{101,0}); equal(1,0xAC2767ED8BDFAB15ULL,{101,500,0}); equal(2,0xC6258F41D86676E0ULL,{101,0,100});
    calls.clear(); native.stamina(2,101);
    equal(0,0x675680D089BFA21FULL,{101,std::bit_cast<std::uint32_t>(100.0f)}); equal(1,0xC6258F41D86676E0ULL,{101,1,100});
    replies[0x592F58BC4D2A2CF3ULL] = std::bit_cast<std::uint32_t>(150.0f);
    calls.clear(); native.deadeye(2,101);
    equal(0,0x592F58BC4D2A2CF3ULL,{2,0}); equal(1,0x51345AE20F22C261ULL,{2,std::bit_cast<std::uint32_t>(150.0f),0,0,1}); equal(2,0xC6258F41D86676E0ULL,{101,2,100});
    calls.clear(); native.ammo(101,true); native.ammo(101,false);
    equal(0,0xFBAA1E06B6BCA741ULL,{101,1}); equal(1,0xFBAA1E06B6BCA741ULL,{101,0});
    calls.clear(); native.horse_stamina(201);
    equal(0,0x675680D089BFA21FULL,{201,std::bit_cast<std::uint32_t>(100.0f)}); equal(1,0xC6258F41D86676E0ULL,{201,1,100});
    calls.clear(); native.clear_wanted(2);
    equal(0,0x062B4A4A3396351DULL,{2}); equal(1,0x55F37F5F3F2475E1ULL,{});
    calls.clear(); replies[0x9DE624D2FC4B603FULL] = 1;
    auto state = native.sample();
    check(state.online && !state.ready && calls.size() == 1, "online session short-circuits all player reads");
    replies[0x9DE624D2FC4B603FULL] = 0; replies[0x10FAB35428CCC9D7ULL] = 1; calls.clear();
    state = native.sample(); check(state.online && !state.ready && calls.size() == 2, "online game guard short-circuits player reads");
    replies[0x10FAB35428CCC9D7ULL] = 0; replies[0x217E9DC48139933DULL] = 2; replies[0x096275889B8E0EE0ULL] = 101;
    replies[0xD42BD6EB2E0F1677ULL] = 1; replies[0xBFFB35986CAAE58CULL] = 1; replies[0x460BC76A0E10655EULL] = 1; replies[0xE7E11B8DCBED1058ULL] = 201;
    calls.clear(); state = native.sample();
    check(!state.online && state.ready && state.player == 2 && state.ped == 101 && state.mount == 201, "living mounted Story player sample");
    replies[0x3317DEDB88C95038ULL] = 1; state = native.sample(); check(!state.ready && state.mount == 0, "dying player blocked");
    replies[0x3317DEDB88C95038ULL] = 0; replies[0xBFFB35986CAAE58CULL] = 0; state = native.sample(); check(!state.ready, "nonplaying player blocked");
    replies[0xBFFB35986CAAE58CULL] = 1; replies[0xD42BD6EB2E0F1677ULL] = 0; state = native.sample(); check(!state.ready && state.mount == 0, "missing player blocked");
    calls.clear(); replies[0x15D757606D170C3CULL] = 0; native.health(101);
    check(calls.size() == 2 && calls.back().hash == 0xC6258F41D86676E0ULL, "invalid maximum never writes health value");
    calls.clear(); replies[0x592F58BC4D2A2CF3ULL] = std::bit_cast<std::uint32_t>(-1.0f); native.deadeye(2,101);
    check(calls.size() == 2 && calls.back().hash == 0xC6258F41D86676E0ULL, "invalid maximum never writes Dead Eye outer ring");
    std::cout << "PASS " << assertions << " RDR2 native wrapper/ABI/Online-gate checks (mock invoker)\n";
}
