# Black Myth: Wukong binding evidence

## Scope and provenance

The initial trainer targets Steam app **2358720**, installed build **21393610**. Read-only inspection found `b1/Binaries/Win64/b1-Win64-Shipping.exe` with SHA-256:

```text
CBA6127DF85FE6AEDF5A71B398F268C8A963C7B19582C3CAC087462518358E9A
```

The executable has no Windows `FileVersion` or `ProductVersion`. The installation's managed game assemblies are packaged inside encrypted PAK files; they were not extracted. Its preexisting crash reports identify Unreal **5.0.0-0+++UE5+Release-5.0**, including the report dated 7 August 2025. This establishes the previously used engine family, not a live startup check of the currently installed build. The initial loader configuration uses Unreal 5.0.

The APIs and behavior below were inspected with Mono.Cecil without loading or executing game code. The reference assemblies came from [xyzkljl1/MyBlackMythWukongMods, commit 66eb62453e81a62b8fa7eafdaafc7ee7ad6485d2](https://github.com/xyzkljl1/MyBlackMythWukongMods/tree/66eb62453e81a62b8fa7eafdaafc7ee7ad6485d2/CSharpMods/GameDll). They are **external references**, not assemblies extracted from installed Steam build 21393610. The research copies remain under `artifacts/wukong/research/` and are not included in the trainer distribution.

| Reference file | SHA-256 |
| --- | --- |
| `BtlSvr.Main.dll` | `9CEA86EEB2B89DCBFAA5598B666A262CBEDDF0E1978ED10A10AACB0FC4318340` |
| `GSE.ProtobufDB.dll` | `DCDF6D92F53C3FB3FBAEF932D1BF22867377888AAEBA81D9BF72D4444EA9BB82` |
| `B1UI_GSE.Script.dll` | `BC1E8ABC38A05E880C9B5F24CF485BF4A6715BF4BBAEB0E6693206EA6A5B69D7` |

Additional reference-only `b1.Managed.dll` and `b1.Native.dll` came from [czastack/B1CSharpLoader, commit 5ae148a68dd81dadfdd22c1d6029d24f0563531b](https://github.com/czastack/B1CSharpLoader/tree/5ae148a68dd81dadfdd22c1d6029d24f0563531b/GameDll); they are not used as distributable loader dependencies. CSharpLoader itself is not bundled. The runtime uses the project's pinned UE4SS dependency and calls reflected game functions.

## Attribute functions

`b1.BGUFunctionLibraryCS` carries `UClass` and `USharpPath` metadata for `/Script/b1-Managed.BGUFunctionLibraryCS`. Its relevant public static methods carry `UFunction` and `BlueprintCallable` metadata:

```text
float GetAttrValue(AActor Unit, EBGUAttrFloat AttrID)
void BGUSetAttrValue(AActor Unit, EBGUAttrFloat AttrID, float Value)
```

`GetAttrValue` reads `IBUC_AttrContainer.GetFloatValue`; it returns `-1` when the container is unavailable. `BGUSetAttrValue` obtains the actor's event collection and invokes `Evt_SetAttrFloat`. `BUS_AttrComp.SetFloatValue` checks actor authority and delegates to `BUC_AttrContainer.SetFloatValue`, which applies the game's own bounds, derived-stat calculation and bound-list notifications.

The enum below is `BtlShare.EBGUAttrFloat` in `GSE.ProtobufDB.dll`:

| Feature | Current attribute | Maximum / associated attribute |
| --- | --- | --- |
| Health | `Hp = 151` | `HpMax = 1` |
| Mana | `Mp = 152` | `MpMax = 2` |
| Stamina | `Stamina = 158` | `StaminaMax = 8` |
| Gourd meter | `BloodBottomNum = 186` | `BloodBottomNumMax = 36` |
| Focus | `Pevalue = 191` | `PevalueMax = 39` |
| Attack | `Atk = 153` | `AtkBase = 103`, `AtkMul = 53` |

Health, mana, stamina and gourd toggles replenish the current attribute from the game's current maximum. They do not increase the maximum. Periodic replenishment is not damage immunity and cannot prevent an instantly lethal hit. Instant max focus is a one-shot refill of the current focus meter, not a progression unlock or a permanent increase in focus capacity. The [PlayerStatus mod's source](https://github.com/xyzkljl1/MyBlackMythWukongMods/blob/66eb62453e81a62b8fa7eafdaafc7ee7ad6485d2/CSharpMods/PlayerStatus/Program.cs) independently associates health, mana, stamina and focus with these enum names in its displayed bars.

Gourd semantics were checked beyond enum names. `BI_GourdCS.HeroBloodBottomChange` divides attribute 186 by 36 and sends that ratio to `HealGourdBar`. `BUACBloodBottleNumCost.DoCostAttrValue` consumes attribute 186 when drinking. `BUS_RegisterGMSystemComp.OnFullBloodBottom` reads attribute 36 and sets attribute 186 through `Evt_SetAttrFloat`, matching the trainer's refill operation.

### Super damage and restoration

`BUC_AttrContainer` registers the attack calculation group as `(Atk = 153, AtkMul = 53, AtkBase = 103)`. Every member, including the final attack attribute, maps back to the calculation group. Consequently, writing `Atk` directly is overwritten immediately by the derived calculation:

```text
Atk = AtkBase * (10000 + AtkMul) * 0.0001
```

The trainer's 10x attack toggle must therefore override `AtkMul`, using:

```text
temporary AtkMul = (original AtkMul + 10000) * 10 - 10000
```

Changing the multiplier preserves the game's current base attack, including weapon changes. The trainer retains the original multiplier and restores it on disable only when the current value still belongs to its override. A readback must verify that the write was accepted; an unavailable attribute or changed pawn must not receive an unguarded restoration. Ten times attack is the specific implementation of “super damage”; final damage still passes through the game's combat rules.

## Experience, sparks and Will

`b1.BGUFunctionLibraryManaged.RunScriptGM(string StrCommand, UObject WorldContext)` carries the reflected path `/Script/b1-Managed.BGUFunctionLibraryManaged:RunScriptGM`. It checks for a non-null context, then invokes `B1UI.GSSvc.GSGMSvc.RunGSScriptGM` through the managed reflection manager.

The following are existing game commands, not inferred command names:

| Feature | Command format | Inspected behavior |
| --- | --- | --- |
| Add experience | `AddExp <amount>` | `CsB1.PlayerGmExecutor.AddExp` parses an integer and calls `PlayerMgr.Actor.GainXp(amount)`. |
| Add sparks | `AddTalentPoint <amount>` | `PlayerGmExecutor.AddTalentPoint` parses an integer and calls `PlayerMgr.Actor.GainTalentPoint(amount)`. |
| Add Will | `AddItem 1002 <amount>` | `PlayerGmExecutor.AddItem` parses item ID and count, creates `ItemOne`, then calls `PlayerMgr.Bag.GainItemOne`. |

`DSMiniGMPage.GenGMBtnCfgList` pairs item 1002 with the Chinese Will label and contains example commands for 1,000/10,000 Will, 1,000/10,000 experience and 100 talent points. `GMAttribute` descriptions on the executor methods confirm parameter order and meaning. Sparks use the game's talent-point addition path rather than direct save editing.

`GSGMSvc.RunGMCommand` parses whitespace-separated arguments and dispatches recognized player commands through its game RPC path. `RunServerGM` constructs `CSMsgMiscExecGMReq`; `CallServerGmRpc` calls the game's `RequestMiscExecGM` with `bBlock = true`, which forwards to `SendMiscExecGMRequestToUnrealChannel`. This is the game's internal request path; the flag means blocking, not proof of offline routing. The trainer does not implement an external network client or modify save files directly.

`RunScriptGM` returns **void** and does not expose the executor's result. Successful invocation confirms dispatch only. It must not be presented as a verified balance change, nor retried automatically after an ambiguous outcome. The game may persist experience, sparks and Will through its normal progression/save system when the user invokes these actions.

## Validation boundary

Confirmed offline: installed Steam discovery and executable hash; method signatures and reflected paths in external references; enum constants; gourd consumption/display linkage; attack derivation; progression command parsing and native game call paths.

The original offline investigation did not establish startup or gameplay compatibility. Subsequent live diagnosis on 29 September 2026 is recorded below. Runtime availability checks and value readback are necessary safeguards, not substitutes for live gameplay validation.

## Live connection diagnosis, 29 September 2026

The user launched the same hash-gated Steam build and loaded a save. The trainer produced fresh heartbeats, but the original profile's `IsLocalController` call did not yield a positive result. Passive inspection confirmed the unique live controller, the Wukong player-class hierarchy, reciprocal pawn/controller ownership, a local player, and the reflected attribute/death/progression function metadata.

The loader logged `ProcessEvent` at image-relative address `0x0AE5DBF0`. Disassembly showed `mov eax, 2; ret`, the adjacent `GetFunctionCallspace` entry, which never dispatches a UFunction. The real `ProcessEvent` is at image-relative address `0x0CCF9000`, vtable slot 74 / byte offset `0x250`. Its body checks the UFunction flags, parameter size and return offset and dispatches the function. The old default used slot 75 / `0x258`. Trainer 0.1.1 supplies a custom loader vtable profile so reflected calls reach the correct implementation. The runtime uses the configured virtual slot, not hardcoded process addresses.

This investigation used query/read-only process handles; it did not allocate, inject, write memory or call game functions through the inspection scripts. The installed Lua trainer continued its ordinary readiness checks with all toggles off. No XP, Sparks or Will grants were sent, and no save was edited. Cheat effects and persistence remain unverified. Session-specific evidence is retained locally in `artifacts/wukong/grey-controls/` and is not shipped.

### Corrected connection verified

Following installation of trainer 0.1.1, the user relaunched the game and loaded a save. At 23:29:55 local time on 29 September 2026, the trainer transitioned to ready. The installed profile logged `UObject::ProcessEvent = 0x250` and dispatched at `0x14CCF9000`. The actual client status parser confirmed Connected/Ready and availability for all nine features. Observed resources included health 480/480, mana 248/310, stamina 290/290, gourd 2/5, focus 0/330 and attack 86. Every toggle was off and the command acknowledgement was empty. This validates the loader/profile and readiness repair without claiming any cheat effect. Local evidence: `fixed-ue4ss.log`, `fixed-status.json` and `live-connection-verification.json` under `artifacts/wukong/grey-controls/`.
