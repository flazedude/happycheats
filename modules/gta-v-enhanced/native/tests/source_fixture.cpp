// Synthetic PE fixture only: no GTA files/processes are read or launched.
#include "source_runtime.h"
#include <iostream>
#include <stdexcept>
using namespace happygta::source;
static std::uint8_t* memory{};
static std::uint8_t tls[0x7B0]{},script[0x198]{};
static std::uintptr_t threadPointers[1]{};
static unsigned resolvedCount{},nativeCalls{},ticks{},inactiveCalls{};
static bool observedOnline{};
static bool flipOnlineBeforeCall{};
static bool callFromForeignThread{};
static DWORD lastDiagnosticThread{};
static std::uint64_t lastArgs[4]{};
static std::uint32_t lastCount{};
void Check(bool okay,const char* reason){if(!okay)throw std::runtime_error(reason);}
void Handler(CallContext* context){
    Check(reinterpret_cast<std::uintptr_t>(context)%16==0,"native context not 16-byte aligned");
    ++nativeCalls;lastCount=context->count;
    for(unsigned i=0;i<context->count&&i<4;++i)lastArgs[i]=static_cast<std::uint64_t*>(context->arguments)[i];
    *static_cast<std::uint64_t*>(context->result)=0x1234;
}
void Initializer(Program* program){
    Check(program->nativeCount==41,"wrong native count");
    for(unsigned i=0;i<program->nativeCount;++i){Check(reinterpret_cast<std::uint64_t>(program->nativeEntries[i])==Mappings[i].mapped,"wrong native crossmap");program->nativeEntries[i]=memory+0x2700;}
    ++resolvedCount;
}
DWORD WINAPI ForeignScheduler(LPVOID){
    return reinterpret_cast<bool(*)(int)>(memory+0x2400)(100)?0:1;
}
void Tick(){
    ++ticks;
    Check(*reinterpret_cast<std::uintptr_t*>(tls+0x7A0)==reinterpret_cast<std::uintptr_t>(script),"Story Mode TLS not installed");
    Check(tls[0x7A8]==1,"TLS script flag not installed");
    if(callFromForeignThread){
        const auto callsBefore=nativeCalls,diagnosticsBefore=inactiveCalls;
        auto foreign=CreateThread(nullptr,0,&ForeignScheduler,nullptr,0,nullptr);
        Check(foreign!=nullptr,"unable to create foreign scheduler fixture");
        auto wait=WaitForSingleObject(foreign,2000);DWORD code{};
        const auto exited=GetExitCodeThread(foreign,&code);CloseHandle(foreign);
        Check(wait==WAIT_OBJECT_0&&exited&&code==0,"foreign scheduler changed original return or stalled");
        Check(inactiveCalls==diagnosticsBefore,"foreign scheduler ran a concurrent diagnostic callback");
        Check(!Ready(),"foreign scheduler did not immediately invalidate readiness");
        Native<>(0x47C3B5848C3E45D8ULL,12,0,0);
        Check(nativeCalls==callsBefore,"native ran after foreign scheduler violation");
        return;
    }
    if(flipOnlineBeforeCall){
        // Simulate a session transition after this scheduler frame passed its
        // initial guard, immediately before a gameplay native would be called.
        const auto callsBefore=nativeCalls,diagnosticsBefore=inactiveCalls;
        memory[0x5010]=1;
        Native<>(0x47C3B5848C3E45D8ULL,12,0,0);
        Check(nativeCalls==callsBefore,"gameplay native ran after a mid-frame Online transition");
        Check(OnlineLatched()&&observedOnline&&inactiveCalls>diagnosticsBefore,"mid-frame Online transition did not synchronously publish inactivity");
        Native<>(0x239528EACDC3E7DEULL,7,false);
        Check(nativeCalls==callsBefore,"cleanup native ran after the Online latch");
        return;
    }
    auto result=Native<int>(0x239528EACDC3E7DEULL,7,true,1000.0f,"probe");
    Check(result==0x1234,"native return value mismatch");
}
void Inactive(const std::string&,bool online){++inactiveCalls;lastDiagnosticThread=GetCurrentThreadId();observedOnline=observedOnline||online;}
void Pattern(std::size_t offset,const char* text){
    auto bytes=memory+offset;
    for(auto p=text;*p;){if(*p==' '){++p;continue;}if(*p=='?'){*bytes++=0;++p;continue;}unsigned value{};std::sscanf(p,"%2x",&value);*bytes++=static_cast<std::uint8_t>(value);p+=2;}
}
void Rip(std::size_t position,std::size_t target){auto relative=static_cast<std::int32_t>(target-position-4);std::memcpy(memory+position,&relative,4);}
void Jump(std::size_t offset,void* target){memory[offset]=0x48;memory[offset+1]=0xB8;std::memcpy(memory+offset+2,&target,8);memory[offset+10]=0xFF;memory[offset+11]=0xE0;}
void BuildImage(){
    memory=static_cast<std::uint8_t*>(VirtualAlloc(nullptr,0x8000,MEM_COMMIT|MEM_RESERVE,PAGE_EXECUTE_READWRITE));Check(memory,"allocation failed");
    auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(memory);dos->e_magic=IMAGE_DOS_SIGNATURE;dos->e_lfanew=0x100;
    auto nt=reinterpret_cast<IMAGE_NT_HEADERS64*>(memory+0x100);nt->Signature=IMAGE_NT_SIGNATURE;nt->FileHeader.Machine=IMAGE_FILE_MACHINE_AMD64;nt->FileHeader.NumberOfSections=4;nt->FileHeader.SizeOfOptionalHeader=sizeof(IMAGE_OPTIONAL_HEADER64);
    nt->OptionalHeader.Magic=IMAGE_NT_OPTIONAL_HDR64_MAGIC;nt->OptionalHeader.SizeOfImage=0x8000;nt->OptionalHeader.NumberOfRvaAndSizes=IMAGE_NUMBEROF_DIRECTORY_ENTRIES;
    nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION]={0x6000,2*sizeof(RUNTIME_FUNCTION)};
    auto sections=IMAGE_FIRST_SECTION(nt);
    for(unsigned i=0;i<4;++i){sections[i].VirtualAddress=0x2000+i*0x1000;sections[i].Misc.VirtualSize=0x1000;sections[i].Characteristics=IMAGE_SCN_MEM_READ;}
    sections[0].Characteristics|=IMAGE_SCN_MEM_EXECUTE;sections[1].VirtualAddress=0x4000;sections[2].VirtualAddress=0x5000;sections[2].Characteristics|=IMAGE_SCN_MEM_WRITE;sections[3].VirtualAddress=0x6000;
    std::memset(memory+0x2000,0x90,0x1000);
    Pattern(0x2100,"4C 8D 0D ? ? ? ? 48 8D 5C 24 ? 48 89 D9 48 89 FA");Rip(0x2103,0x4000);std::memcpy(memory+0x4000,"1158.16",8);
    Pattern(0x2200,"48 8B 05 ? ? ? ? 48 89 34 F8 48 FF C7 48 39 FB 75 97");Rip(0x2203,0x5000);
    Pattern(0x232A,"EB 2A 0F 1F 40 00 48 8B 54 17 10");Jump(0x2300,reinterpret_cast<void*>(&Initializer));
    Pattern(0x240A,"BE 40 5D C6 00");Pattern(0x2400,"B8 01 00 00 00 C3");
    Pattern(0x2500,"0F B6 05 ? ? ? ? 0A 05 ? ? ? ? 75 2A");Rip(0x2503,0x5010);Rip(0x2509,0x5011);
    Jump(0x2700,reinterpret_cast<void*>(&Handler));
    auto unwind=reinterpret_cast<RUNTIME_FUNCTION*>(memory+0x6000);unwind[0]={0x2300,0x2380,0x6500};unwind[1]={0x2400,0x2480,0x6500};memory[0x6500]=1;
    Check(RtlAddFunctionTable(unwind,2,reinterpret_cast<DWORD64>(memory)),"fixture unwind registration failed");
    threadPointers[0]=reinterpret_cast<std::uintptr_t>(script);std::memcpy(memory+0x5000,&threadPointers,8);auto array=threadPointers;std::memcpy(memory+0x5000,&array,8);*reinterpret_cast<std::uint16_t*>(memory+0x5008)=1;*reinterpret_cast<std::uint16_t*>(memory+0x500A)=1;
    *reinterpret_cast<std::uint32_t*>(script+8)=1;
    std::uint32_t hash{};for(const char* p="main_persistent";*p;++p){hash+=*p;hash+=hash<<10;hash^=hash>>6;}hash+=hash<<3;hash^=hash>>11;hash+=hash<<15;
    *reinterpret_cast<std::uint32_t*>(script+0x150)=hash;
    *reinterpret_cast<std::uintptr_t*>(tls+0x7A0)=0x12345678;
    SetFixtureTls(tls);
    FlushInstructionCache(GetCurrentProcess(),memory,0x8000);
}
int main(int argc,char** argv){try{
    BuildImage();Image image;std::string error;Check(image.Parse(memory,0x8000,error),error.c_str());Resolved resolved;
    Check(Resolve(image,resolved,error),error.c_str());Check(resolved.scheduler==reinterpret_cast<std::uintptr_t>(memory+0x2400),"wrong scheduler");
    // Ambiguous, missing, wrong-build, bad boundary and wrong section fixtures.
    Pattern(0x2800,"BE 40 5D C6 00");Check(!Resolve(image,resolved,error),"ambiguous scheduler accepted");std::memset(memory+0x2800,0x90,5);
    memory[0x2100]=0;Check(!Resolve(image,resolved,error),"missing version accepted");memory[0x2100]=0x4C;
    memory[0x4006]='3';Check(!Resolve(image,resolved,error),"wrong version accepted");memory[0x4006]='6';
    auto unwind=reinterpret_cast<RUNTIME_FUNCTION*>(memory+0x6000);unwind[1].BeginAddress=0x2401;Check(!Resolve(image,resolved,error),"non-boundary hook accepted");unwind[1].BeginAddress=0x2400;
    Rip(0x2503,0x4100);Check(!Resolve(image,resolved,error),"read-only session global accepted");Rip(0x2503,0x5010);
    DWORD oldProtection{};Check(VirtualProtect(memory+0x2000,0x1000,PAGE_READWRITE,&oldProtection),"unable to set fixture non-executable");
    Check(!Resolve(image,resolved,error),"non-executable function page accepted");
    Check(!image.Executable(reinterpret_cast<std::uintptr_t>(memory+0x2700)),"non-executable native handler accepted");
    Check(VirtualProtect(memory+0x2000,0x1000,oldProtection,&oldProtection),"unable to restore fixture executable");
    Check(Initialize(reinterpret_cast<HMODULE>(memory),&Tick,&Inactive),LastError().c_str());
    auto scheduler=reinterpret_cast<bool(*)(int)>(memory+0x2400);
    Check(scheduler(100),"original scheduler return not preserved");Check(ticks==1&&nativeCalls==1&&resolvedCount==1,"real hook/resolver did not run");
    Check(lastCount==4&&lastArgs[0]==7&&lastArgs[1]==1&&lastArgs[2]==0x447A0000ULL,"native argument ABI mismatch");Check(std::string(reinterpret_cast<const char*>(lastArgs[3]))=="probe","string argument mismatch");
    Check(*reinterpret_cast<std::uintptr_t*>(tls+0x7A0)==0x12345678&&tls[0x7A8]==0,"TLS not restored");
    Native<>(0x239528EACDC3E7DEULL,8,true);Check(nativeCalls==1,"native allowed outside scheduler");
    *reinterpret_cast<std::uint16_t*>(memory+0x5008)=0;scheduler(100);Check(ticks==1&&inactiveCalls>0,"missing thread did not go inactive");*reinterpret_cast<std::uint16_t*>(memory+0x5008)=1;
    scheduler(100);Check(ticks==2&&resolvedCount==1,"native cache not retained across missing thread");
    if(argc==2&&std::string(argv[1])=="--foreign-thread"){
        const auto diagnosticsBefore=inactiveCalls;
        callFromForeignThread=true;scheduler(100);
        Check(ticks==3&&nativeCalls==2&&!Ready(),"foreign scheduler did not fail closed");
        Check(inactiveCalls>diagnosticsBefore&&lastDiagnosticThread==GetCurrentThreadId(),"owner did not publish foreign-thread failure");
        Check(LastError().find("different operating-system thread")!=std::string::npos,"foreign-thread diagnostic missing");
        Check(*reinterpret_cast<std::uintptr_t*>(tls+0x7A0)==0x12345678&&tls[0x7A8]==0,"TLS not restored after foreign-thread violation");
        scheduler(100);Native<>(0x239528EACDC3E7DEULL,9,false);
        Check(ticks==3&&nativeCalls==2,"native activity resumed after foreign scheduler violation");
        std::cout<<"PASS foreign scheduler atomically disables readiness and natives; diagnostics remain on owner thread\n";
        return 0;
    }
    flipOnlineBeforeCall=true;scheduler(100);Check(OnlineLatched()&&ticks==3&&observedOnline,"mid-frame Online failed to latch");
    Check(*reinterpret_cast<std::uintptr_t*>(tls+0x7A0)==0x12345678&&tls[0x7A8]==0,"TLS not restored after a mid-frame Online transition");
    memory[0x5010]=0;scheduler(100);Native<>(0x239528EACDC3E7DEULL,9,false);Check(ticks==3&&nativeCalls==2,"native activity resumed after Online latch");
    std::cout<<"PASS synthetic PE resolver, real MinHook scheduler, 41-entry resolver, native ABI, TLS restoration, missing-thread inactivity, mid-frame Online transition, permanent Online lockout\n";
    // Process lifetime fixture: do not unload an active detour or release its image.
    return 0;
}catch(const std::exception& error){std::cerr<<"FAIL "<<error.what()<<"\n";return 1;}}
