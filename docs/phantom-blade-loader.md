# Phantom Blade Zero loader profile

This profile is for Steam build **26266538**, `platform/steam/PBZ.exe`, SHA-256 `7b4fcd286a350780faf0824d73aa150b21cbf1bccde883e6a96386cbe09973b4`. The executable identifies Unreal Engine 5.7.4. Other executable hashes are rejected by the client.

The pinned UE4SS dependency is **3.0.1-1133-gb4cefa18**, source commit `b4cefa18ac7349c4389c1ce244c1a70b66a05fe5`, archive SHA-256 `89b7eed47c37d6ff6eaa144a41311a75098279a3454777f4edd2446cea1ea7a8`. Its Unreal dependency is pinned at `671c7aad5515f919cbcbbbf971944ce7ae227530`. The public [5.7 member-layout template](https://github.com/UE4SS-RE/RE-UE4SS/blob/b4cefa18ac7349c4389c1ce244c1a70b66a05fe5/assets/MemberVarLayoutTemplates/MemberVariableLayout_5_07_Template.ini#L141) and generated setters establish the supported configuration keys; this review did not require access to the private Unreal dependency source.

## Object array discovery

The default patternsleuth scan failed to resolve `GUObjectArray` in this executable. The inspected native caller at RVA `0x164c416` loads that global into `rcx`, then calls `AllocateUObjectIndex` at RVA `0x164dba0`. Its 31-byte sequence occurs exactly once in the complete inspected executable. The RIP-relative displacement resolves the global to RVA `0x0ddb3c40`; read-only runtime inspection also identified that object array and decoded its first object as the `/Script/CoreUObject` package.

`UE4SS_Signatures/GUObjectArray.lua` supplies the exact caller signature, checks both the global-address and call displacements, then computes the global address relative to the relocated match. Invalid or unreadable candidates return no address. The callback does not use an absolute process address or write memory. The pinned [signature implementation](https://github.com/UE4SS-RE/RE-UE4SS/blob/b4cefa18ac7349c4389c1ce244c1a70b66a05fe5/UE4SS/src/Signatures.cpp#L124) explicitly supports this file and passes its resolved address to `SetupGUObjectArrayAddress`.

## Custom array member layout

The game's `FUObjectArray.ObjObjects` remains at the normal UE5.7 offset `0x10`. Native allocation and read-only runtime inspection identified a reordered `FChunkedFixedUObjectArray` inside it:

| Member | Relative offset |
| --- | --- |
| MaxChunks | `0x00` |
| NumChunks | `0x04` |
| Objects | `0x08` |
| NumElements | `0x10` |
| MaxElements | `0x14` |
| PreAllocatedObjects | `0x18` |
| Total size | `0x20` |

These seven values are supplied under `[TUObjectArray]` in `MemberVariableLayout.ini`. The pinned [generated setters](https://github.com/UE4SS-RE/RE-UE4SS/blob/b4cefa18ac7349c4389c1ce244c1a70b66a05fe5/UE4SS/generated_include/MacroSetter.hpp#L234) accept all seven keys. [Loader initialization](https://github.com/UE4SS-RE/RE-UE4SS/blob/b4cefa18ac7349c4389c1ce244c1a70b66a05fe5/UE4SS/src/UE4SSProgram.cpp#L560) reads the file before Unreal initialization.

`FUObjectItem` already matches the pinned UE5.7 template: size `0x18`, combined flags/reference count at `0x00`, object pointer at `0x08`, serial number at `0x10`, and cluster root index at `0x14`. It needs no override. Native allocation uses 65,536 entries per chunk and a 24-byte item stride.

## Reflected field and function layouts

Read-only live metadata also differs from the pinned UE5.7 template. The profile includes the following verified overrides:

| Structure | Members and offsets |
| --- | --- |
| FField | Next `0x10`, Owner `0x18` |
| FFieldClass | ConstructFn `0x00`, CastFlags `0x10`, Id `0x18`, Name `0x30` |
| UStruct | PropertiesSize `0xa8`, MinAlignment `0xac`, StructStateFlags `0xae`, Script `0xb0`, PropertyLink `0xc0`, RefLink `0xc8`, DestructorLink `0xd0`, PostConstructLink `0xd8`, total size `0x100` |
| UFunction | FunctionFlags `0x100`, NumParms `0x104`, ParmsSize `0x106`, ReturnValueOffset `0x108`, FirstPropertyToInit `0x110`, EventGraphFunction `0x118`, EventGraphCallOffset `0x120`, Func `0x128`, total size `0x130` |
| UClass | ClassFlags `0x124`, ClassCastFlags `0x128`, ClassDefaultObject `0x160`, FuncMap `0x180`, FuncMapLock `0x1d0`, AllFunctionsCache `0x1d8`, AllFunctionsCacheLock `0x228`, total size `0x258` |
| UScriptStruct | StructFlags `0x100`, bPrepareCppStructOpsCompleted `0x104`, CppStructOps `0x108`, total size `0x110` |

The `FFieldClass` mapping agrees with live string/object/struct property classes and native constructor dispatch. `FProperty` primitive metadata and property/reference/destructor linked-list offsets retain the stock layout; decoded property schemas and the `GameplayAttribute` lists were inspected. `UStruct.ChildProperties` remains at `0x50`. The changed `UStruct` fields follow additional data at `0x58..0xa7`; live blueprint bytecode confirms `Script`. Native structure sizes were confirmed through their `/Script/CoreUObject` class metadata.

`UClass.ClassDefaultObject` resolves the exact expected default object across seven native classes. Class cast flags agree with `Class`, `Function`, `ScriptStruct`, `Actor`, `PlayerController` and the player class. The function map and inherited-function cache were decoded as maps with 24-byte name/function entries. All 623 own-function entries and 255 cached entries across six classes resolve matching function names or explicit missing-function cache entries. The inspected native `CppStructOps` records report the matching sizes and alignments for `GameplayAttribute`, `GameplayAttributeData` and `Vector`; their internal size/alignment member offsets remain stock.

Native `UClass::FindFunctionByName` at RVA `0x15329c0` independently confirms both map lock offsets. It acquires the shared SRW lock at `0x1d0` before reading the own-function map at `0x180`, and acquires the lock at `0x228` before reading the inherited-function cache at `0x1d8`. Its cache update also uses the latter lock in exclusive mode. These overrides prevent stock lock offsets from referring to the relocated map data pointers.

The pinned generated setters explicitly support [UStruct](https://github.com/UE4SS-RE/RE-UE4SS/blob/b4cefa18ac7349c4389c1ce244c1a70b66a05fe5/UE4SS/generated_include/MacroSetter.hpp#L62), [FFieldClass and FField](https://github.com/UE4SS-RE/RE-UE4SS/blob/b4cefa18ac7349c4389c1ce244c1a70b66a05fe5/UE4SS/generated_include/MacroSetter.hpp#L780), and [UFunction](https://github.com/UE4SS-RE/RE-UE4SS/blob/b4cefa18ac7349c4389c1ce244c1a70b66a05fe5/UE4SS/generated_include/MacroSetter.hpp#L1095). `FFieldClass.Name` is the supported public alias for its internal `FName` member key. These observations verify individual overrides, not every engine layout or Lua marshaling path.

The stock UE5.7 `ProcessEvent` virtual slot remains index 76, byte offset `0x260`. The live default PBZ blueprint-library object resolves that slot to native RVA `0x1631410`; its code accesses the verified function flags at `0x100`, struct size at `0xa8`, first initialized property at `0x110`, and event-graph fields at `0x118`/`0x120`. Its initialization loop also reads the stock property flags, element size, array dimension, data offset and post-construction link members. The actor override occupies the same slot. No `ProcessEvent` vtable override is supplied. This was memory/code inspection without invoking the function.

`GetWorld` also retains the stock virtual slot, index 48 at byte offset `0x180`. The actual player and actor default object resolve it to native RVA `0x3ab6f50`; that code checks actor/outer flags, resolves the level and returns its owning world. No `GetWorld` vtable override is supplied, and the inspection did not invoke it.

## Packaging and validation

Both loader profile files are required, hashed members of the Phantom Blade package. Installation preserves foreign or modified profiles and removal follows the shared-loader ownership rules. Offline fixtures check relocated signature results, rejected candidates, the exact verified layout, required payload files and ownership behavior.

This evidence establishes the custom discovery/layout profile. It does not establish successful UE4SS startup, trainer loading, gameplay effects, shared-menu rendering over the game or physical-controller operation.
