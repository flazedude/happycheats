// GPL-2.0. See PROVENANCE.md. Experimental, unverified in actual gameplay.
#include "source_runtime.hpp"
#include <MinHook.h>
#include <intrin.h>
#include <algorithm>
#include <cstdio>
#include <limits>
#include <stdexcept>

namespace happygta::source {
bool MemoryAccessible(std::uintptr_t address, std::size_t size, bool writable) {
    if (!address || !size || address > UINTPTR_MAX - size) return false;
    auto end = address + size;
    while (address < end) {
        MEMORY_BASIC_INFORMATION region{};
        if (!VirtualQuery(reinterpret_cast<void*>(address), &region, sizeof(region)) ||
            region.State != MEM_COMMIT || (region.Protect & (PAGE_GUARD | PAGE_NOACCESS))) return false;
        auto protection = region.Protect & 0xFF;
        bool readable = protection == PAGE_READONLY || protection == PAGE_READWRITE || protection == PAGE_WRITECOPY ||
            protection == PAGE_EXECUTE_READ || protection == PAGE_EXECUTE_READWRITE || protection == PAGE_EXECUTE_WRITECOPY;
        bool canWrite = protection == PAGE_READWRITE || protection == PAGE_WRITECOPY || protection == PAGE_EXECUTE_READWRITE || protection == PAGE_EXECUTE_WRITECOPY;
        if (!readable || (writable && !canWrite)) return false;
        auto next = reinterpret_cast<std::uintptr_t>(region.BaseAddress) + region.RegionSize;
        if (next <= address) return false;
        address = next;
    }
    return true;
}
bool Image::Contains(std::uintptr_t address, std::size_t length) const {
    return address >= base_ && length <= size_ && address - base_ <= size_ - length;
}
bool Image::Parse(void* base, std::size_t suppliedSize, std::string& error) {
    base_ = reinterpret_cast<std::uintptr_t>(base); size_ = suppliedSize; sections_.clear();
    if (size_ < 0x1000 || size_ > 1024ULL*1024*1024 || !MemoryAccessible(base_, 0x1000)) {error="Invalid image header memory";return false;}
    auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base_);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 || !Contains(base_+dos->e_lfanew,sizeof(IMAGE_NT_HEADERS64))) {error="Invalid DOS header";return false;}
    auto nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base_+dos->e_lfanew);
    if (!MemoryAccessible(reinterpret_cast<std::uintptr_t>(nt),sizeof(*nt)) || nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64 || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC || nt->OptionalHeader.SizeOfImage != size_ || !nt->FileHeader.NumberOfSections || nt->FileHeader.NumberOfSections > 96 || nt->OptionalHeader.NumberOfRvaAndSizes <= IMAGE_DIRECTORY_ENTRY_EXCEPTION) {error="Invalid x64 PE image";return false;}
    auto section = IMAGE_FIRST_SECTION(nt);
    auto sectionBytes = sizeof(IMAGE_SECTION_HEADER)*nt->FileHeader.NumberOfSections;
    if (!Contains(reinterpret_cast<std::uintptr_t>(section),sectionBytes) || !MemoryAccessible(reinterpret_cast<std::uintptr_t>(section),sectionBytes)) {error="Invalid sections";return false;}
    for (unsigned i=0;i<nt->FileHeader.NumberOfSections;++i) {
        std::uint32_t size=section[i].Misc.VirtualSize;
        if (size && !Contains(base_+section[i].VirtualAddress,size)) {error="Section outside image";return false;}
        sections_.push_back({section[i].VirtualAddress,size,section[i].Characteristics});
    }
    auto exception=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
    exceptionRva_=exception.VirtualAddress;exceptionSize_=exception.Size;
    if (!exceptionSize_ || exceptionSize_%sizeof(RUNTIME_FUNCTION) || !Contains(base_+exceptionRva_,exceptionSize_) || !MemoryAccessible(base_+exceptionRva_,exceptionSize_)) {error="Missing valid unwind table";return false;}
    return true;
}
bool Image::Executable(std::uintptr_t address) const {
    if (!Contains(address) || !MemoryAccessible(address,1)) return false;
    MEMORY_BASIC_INFORMATION region{};
    if(!VirtualQuery(reinterpret_cast<void*>(address),&region,sizeof(region)))return false;
    auto protection=region.Protect&0xFF;
    if(protection!=PAGE_EXECUTE_READ&&protection!=PAGE_EXECUTE_READWRITE&&protection!=PAGE_EXECUTE_WRITECOPY)return false;
    for(auto section:sections_) if(address-base_>=section.rva && address-base_-section.rva<section.size) return (section.flags&IMAGE_SCN_MEM_EXECUTE)!=0;
    return false;
}
bool Image::Writable(std::uintptr_t address,std::size_t length) const {
    if(!Contains(address,length)||!MemoryAccessible(address,length,true))return false;
    for(auto section:sections_) if(address-base_>=section.rva && length<=section.size && address-base_-section.rva<=section.size-length) return (section.flags&IMAGE_SCN_MEM_WRITE)!=0;
    return false;
}
bool Image::FunctionStart(std::uintptr_t address) const {
    if(!Executable(address))return false;
    DWORD64 unwindBase{};
    auto registered=RtlLookupFunctionEntry(address,&unwindBase,nullptr);
    if(!registered||unwindBase!=base_||registered->BeginAddress!=address-base_)return false;
    auto functions=reinterpret_cast<const RUNTIME_FUNCTION*>(base_+exceptionRva_);
    for(std::size_t i=0;i<exceptionSize_/sizeof(*functions);++i)
        if(functions[i].BeginAddress==address-base_ && functions[i].EndAddress>functions[i].BeginAddress && Contains(base_+functions[i].BeginAddress,functions[i].EndAddress-functions[i].BeginAddress)) return true;
    return false;
}
std::uintptr_t Image::Rip(std::uintptr_t displacement) const {
    if(!Contains(displacement,4)||!MemoryAccessible(displacement,4))return 0;
    std::int32_t relative{};std::memcpy(&relative,reinterpret_cast<void*>(displacement),4);
    auto result=displacement+4+relative;return Contains(result)?result:0;
}
std::vector<std::uintptr_t> Image::Find(const char* signature) const {
    std::vector<int> pattern;
    for(const char* p=signature;*p;){
        if(*p==' '){++p;continue;}
        if(*p=='?'){pattern.push_back(-1);++p;continue;}
        unsigned value{};if(std::sscanf(p,"%2x",&value)!=1)return {};
        pattern.push_back(static_cast<int>(value));p+=2;
    }
    std::vector<std::uintptr_t> found;
    if(pattern.empty())return found;
    for(auto section:sections_) {
        if(!(section.flags&IMAGE_SCN_MEM_EXECUTE)||pattern.size()>section.size)continue;
        // Do not read inaccessible/guarded executable sections, even if PE metadata names them.
        if(!MemoryAccessible(base_+section.rva,section.size))continue;
        auto bytes=reinterpret_cast<const std::uint8_t*>(base_+section.rva);
        for(std::size_t offset=0;offset<=section.size-pattern.size();++offset){
            bool match=true;
            for(std::size_t j=0;j<pattern.size();++j)if(pattern[j]>=0&&bytes[offset+j]!=pattern[j]){match=false;break;}
            if(match)found.push_back(base_+section.rva+offset);
        }
    }
    return found;
}
bool Resolve(const Image& image,Resolved& result,std::string& error){
    struct Search{const char* name;const char* pattern;};
    constexpr Search searches[]{
        {"version","4C 8D 0D ? ? ? ? 48 8D 5C 24 ? 48 89 D9 48 89 FA"},
        {"script threads","48 8B 05 ? ? ? ? 48 89 34 F8 48 FF C7 48 39 FB 75 97"},
        {"native initializer","EB 2A 0F 1F 40 00 48 8B 54 17 10"},
        {"script scheduler","BE 40 5D C6 00"},
        {"session guard","0F B6 05 ? ? ? ? 0A 05 ? ? ? ? 75 2A"}};
    std::uintptr_t hits[5]{};
    for(int i=0;i<5;++i){auto found=image.Find(searches[i].pattern);if(found.size()!=1){error=std::string("Expected one ")+searches[i].name+" pattern; found "+std::to_string(found.size());return false;}hits[i]=found[0];}
    result.version=image.Rip(hits[0]+3);result.threads=image.Rip(hits[1]+3);
    result.initializer=hits[2]-0x2A;result.scheduler=hits[3]-0xA;
    result.sessionA=image.Rip(hits[4]+3);result.sessionB=image.Rip(hits[4]+9);
    if(!image.Contains(result.version,8)||!MemoryAccessible(result.version,8)||std::memcmp(reinterpret_cast<void*>(result.version),"1158.16",8)!=0){error="Runtime version string is not exactly 1158.16";return false;}
    if(!image.Writable(result.threads,0x10)||!image.Writable(result.sessionA)||!image.Writable(result.sessionB)){error="Script/session globals are not validated writable image data";return false;}
    if(!image.FunctionStart(result.initializer)||!image.FunctionStart(result.scheduler)){error="Hook/resolver signature does not identify an unwind function boundary";return false;}
    return true;
}

Runtime* Runtime::instance_{};
Runtime& CurrentRuntime(){ static Runtime* runtime=new Runtime();return *runtime; }
namespace { void (*frameCallback)(){};void (*inactiveCallback)(const std::string&,bool){}; }
#ifdef HC_SOURCE_TESTING
namespace { std::uintptr_t fixtureTls{}; }
void SetFixtureTls(void* tls){fixtureTls=reinterpret_cast<std::uintptr_t>(tls);}
#endif
bool Initialize(HMODULE game,void (*tick)(),void (*inactive)(const std::string&,bool)){
    frameCallback=tick;inactiveCallback=inactive;
    return CurrentRuntime().Initialize(game,[](void*){if(frameCallback)frameCallback();},
        [](void*,const char* reason){if(inactiveCallback)inactiveCallback(reason,CurrentRuntime().OnlineLatched());},nullptr);
}
bool OnlineLatched(){return CurrentRuntime().OnlineLatched();}
bool Ready(){return CurrentRuntime().Ready();}
const std::string& LastError(){return CurrentRuntime().Error();}
void Runtime::Report(const char* reason) noexcept {
    try {if(diagnostic_)diagnostic_(context_,reason);}catch(...){ready_.store(false);failed_.store(true);}
}
void Runtime::Inactive(const char* reason){
    ready_.store(false);auto now=GetTickCount64();
    if(now-lastInactive_>=200){lastInactive_=now;Report(reason);}
}
void Runtime::Fail(const char* reason){
    ready_.store(false);failed_.store(true);error_=reason;
    Report(reason);
}
bool Runtime::Initialize(HMODULE game,Tick tick,Diagnostic diagnostic,void* context){
    if(instance_||!game||!tick){error_="Runtime already initialized or missing callback";return false;}
    tick_=tick;diagnostic_=diagnostic;context_=context;
    auto address=reinterpret_cast<std::uintptr_t>(game);
    if(!MemoryAccessible(address,0x1000)){error_="Game header is unavailable";return false;}
    auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(address);
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||dos->e_lfanew>0x100000||!MemoryAccessible(address+dos->e_lfanew,sizeof(IMAGE_NT_HEADERS64))){error_="Invalid game module";return false;}
    auto nt=reinterpret_cast<IMAGE_NT_HEADERS64*>(address+dos->e_lfanew);
    if(!image_.Parse(game,nt->OptionalHeader.SizeOfImage,error_)||!Resolve(image_,resolved_,error_))return false;
    if(!SessionSafe())return false;
    auto init=MH_Initialize();
    if(init!=MH_OK&&init!=MH_ERROR_ALREADY_INITIALIZED){error_="MinHook initialization failed";return false;}
    auto status=MH_CreateHook(reinterpret_cast<void*>(resolved_.scheduler),reinterpret_cast<void*>(&Scheduler),reinterpret_cast<void**>(&original_));
    if(status!=MH_OK){error_="Unable to create scheduler hook";return false;}
    instance_=this;active_.store(true);
    if(MH_EnableHook(reinterpret_cast<void*>(resolved_.scheduler))!=MH_OK){active_.store(false);instance_=nullptr;MH_RemoveHook(reinterpret_cast<void*>(resolved_.scheduler));error_="Unable to enable scheduler hook";return false;}
    return true;
}
bool Runtime::SessionSafe(){
    if(online_.load()||failed_.load()||threadViolation_.load())return false;
    if(!image_.Writable(resolved_.sessionA)||!image_.Writable(resolved_.sessionB)){Fail("Session guard unavailable; experimental runtime disabled");return false;}
    auto a=*reinterpret_cast<volatile const std::uint8_t*>(resolved_.sessionA),b=*reinterpret_cast<volatile const std::uint8_t*>(resolved_.sessionB);
    if(a>1||b>1){Fail("Session guard has invalid data; experimental runtime disabled");return false;}
    if(a||b){online_.store(true);ready_.store(false);Report("Online session detected; source runtime disabled until the game restarts");return false;}
    return true;
}
bool Runtime::CacheNatives(){
    if(nativesCached_){ready_.store(true);return true;}
    if(!inCallback_||!SessionSafe())return false;
    for(std::size_t i=0;i<Mappings.size();++i)handlers_[i]=reinterpret_cast<void*>(Mappings[i].mapped);
    Program program{};program.nativeCount=static_cast<std::uint32_t>(handlers_.size());program.nativeEntries=handlers_.data();
    reinterpret_cast<void(*)(Program*)>(resolved_.initializer)(&program);
    for(auto handler:handlers_)if(!image_.Executable(reinterpret_cast<std::uintptr_t>(handler))){Fail("Native resolver returned an invalid handler; experimental runtime disabled");return false;}
    nativesCached_=true;ready_.store(true);return true;
}
bool Runtime::Scheduler(int budget){
    auto self=instance_;
    if(!self||!self->original_)return false;
    bool result=self->original_(budget);
    if(self->active_.load()){
        DWORD expected{};
        const auto currentThread=GetCurrentThreadId();
        if(!self->callbackThread_.compare_exchange_strong(expected,currentThread)&&expected!=currentThread){
            // Foreign scheduler callers must never touch diagnostics, strings,
            // TLS or trainer state concurrently with the established owner.
            self->threadViolation_.store(true);self->ready_.store(false);return result;
        }
        if(self->threadViolation_.load()&&!self->failed_.load())self->Fail("Script scheduler moved to a different operating-system thread");
        else if(self->failed_.load())self->Inactive(self->error_.c_str());
        else if(self->online_.load())self->Inactive("Online session detected; source runtime disabled until the game restarts");
        else self->Frame();
    }
    return result;
}
static constexpr std::uint32_t Joaat(const char* text){
    std::uint32_t value{};for(;*text;++text){value+=static_cast<std::uint8_t>(*text);value+=value<<10;value^=value>>6;}value+=value<<3;value^=value>>11;value+=value<<15;return value;
}
void Runtime::Frame(){
    if(inCallback_||!SessionSafe())return;
    // The script array is visited only from the original scheduler's own thread.
    struct Threads{std::uintptr_t* data;std::uint16_t count,capacity;};
    Threads threads{};std::memcpy(&threads,reinterpret_cast<void*>(resolved_.threads),sizeof(threads));
    if(!threads.count||threads.count>1024||threads.capacity<threads.count||!MemoryAccessible(reinterpret_cast<std::uintptr_t>(threads.data),threads.count*sizeof(std::uintptr_t))){Inactive("Waiting for a valid Story Mode script array");return;}
    std::uintptr_t story{};
    for(std::uint16_t i=0;i<threads.count;++i){
        auto thread=threads.data[i];if(!MemoryAccessible(thread,0x198))continue;
        auto id=*reinterpret_cast<const std::uint32_t*>(thread+8),hash=*reinterpret_cast<const std::uint32_t*>(thread+0x150),state=*reinterpret_cast<const std::uint32_t*>(thread+0x18);
        if(id&&state<=1&&(hash==Joaat("main_persistent")||(!story&&hash==Joaat("startup")))){story=thread;if(hash==Joaat("main_persistent"))break;}
    }
    if(!story){Inactive("Waiting for the Story Mode script thread");return;}
    std::uintptr_t tls{};
#ifdef HC_SOURCE_TESTING
    tls=fixtureTls;
#else
    auto slots=static_cast<std::uintptr_t>(__readgsqword(0x58));
    if(!MemoryAccessible(slots,sizeof(std::uintptr_t))){Inactive("Waiting for game script TLS");return;}
    tls=*reinterpret_cast<std::uintptr_t*>(slots);
#endif
    if(!MemoryAccessible(tls+0x7A0,9,true)){Inactive("Waiting for valid game script TLS");return;}
    auto current=reinterpret_cast<std::uintptr_t*>(tls+0x7A0);auto scriptActive=reinterpret_cast<std::uint8_t*>(tls+0x7A8);
    if(*scriptActive>1){Fail("Invalid script TLS state");return;}
    const auto previous=*current;const auto previousActive=*scriptActive;
    struct Restore { std::uintptr_t* current;std::uint8_t* active;std::uintptr_t previous;std::uint8_t previousActive;~Restore(){*active=previousActive;*current=previous;} } restore{current,scriptActive,previous,previousActive};
    *current=story;*scriptActive=1;inCallback_=true;
    try {if(CacheNatives()&&SessionSafe())tick_(context_);}catch(const std::exception& error){Fail(error.what());}catch(...){Fail("Experimental script callback stopped");}
    inCallback_=false;
    if(threadViolation_.load()&&!failed_.load())Fail("Script scheduler moved to a different operating-system thread");
}
std::uint64_t Runtime::Call(std::uint64_t hash,const std::uint64_t* args,std::size_t count){
    if(callbackThread_.load()!=GetCurrentThreadId()||!inCallback_||!Ready()||!SessionSafe())return 0;
    auto found=std::find_if(Mappings.begin(),Mappings.end(),[hash](auto entry){return entry.original==hash;});
    if(found==Mappings.end()||count>40)throw std::runtime_error("Unknown or oversized source native call");
    std::array<std::uint64_t,10> returned{};std::array<std::uint64_t,40> parameters{};
    if(count)std::memcpy(parameters.data(),args,count*sizeof(std::uint64_t));
    CallContext context{};context.result=returned.data();context.count=static_cast<std::uint32_t>(count);context.arguments=parameters.data();
    auto handler=handlers_[static_cast<std::size_t>(found-Mappings.begin())];
    reinterpret_cast<void(*)(CallContext*)>(handler)(&context);
    if(context.vectorCount)throw std::runtime_error("Unexpected vector native result");
    return returned[0];
}
}
