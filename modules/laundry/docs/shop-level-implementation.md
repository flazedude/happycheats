# Laundry shop level: static implementation evidence

Inspected on 2026-09-12 using cached IL2CPP metadata/generated wrappers and read-only
disassembly of the installed `GameAssembly.dll`. No game process was launched,
attached to, or operated; no tests were run for this feature.

The native binary SHA256 remained
`7CB67E3558EB20B78090BF6A28DE3DCB002EC48970444E40CB6974C83703D7D5`, matching the
trainer's supported-build guard. The implementation is in
[`ShopLevel.cs`](../src/LaundryTrainer/ShopLevel.cs).

## Progression and cap

The shop's progression system is `Il2Cpp.LaundryTierSystem`, separate from the
player's `LevelXPManager`. `CurrRank` is one-based: its getter at RVA `0x76C1D0`
returns `currTierIdx + 1`.

`IsMaxLevel()` at RVA `0x76BB90` returns true when
`currTierIdx >= min(maxTier, tiers.Length) - 1`. The feature calls this before
changing progression and relies on the game's configured cap, without assuming
a fixed final rank. This precheck matters: `CheckTierUp()` alone checks the
current index against the tier count rather than the final zero-based index,
and can retain a next-tier reference when a configured cap is below the array
length.

The one-level action performs these operations:

1. Read stored game/shop/save singletons, require `GameManager.isFinishLoading`
   and `OfflineDataManager.hasFinishedLoadSaveFile`, and validate next-tier data.
2. Call `AddXP(int)` with the missing XP. The native function applies its normal
   bonuses, clamps XP to `nextTierProgression.xpNeeded`, and invokes the XP event.
3. Call `AddAllChallengesProgress(int)` with the largest missing challenge amount.
   This method visits only `nextTierProgression.challenges`. Its called method,
   `AddChallengesProgress`, ignores progress until XP is full and clamps each
   challenge to its own maximum. XP must therefore be filled first.
4. Require `IsTierUpReady()`, then invoke `CheckTierUp()` once. The native path
   increments the index once, assigns the following tier, resets current XP,
   invokes `OnXpChanged` and `OnLevelUp`, and checks rank achievements.
5. Return the actual `CurrRank`, requiring it to have increased by exactly one.

`AddXP` and challenge methods do not directly call `CheckTierUp` in the inspected
native code. Their Unity events can have listeners, so the feature reads the
actual rank after each operation and stops if a listener already advanced it.
This avoids requesting another tier after an early level-up. It does not edit
rank, XP, challenge, or obscured save fields directly.

The challenge wrapper array implements managed `System.Collections.IEnumerable`.
Challenge types are checked for duplicates because the native progress method
addresses the first matching type. Abnormal/negative progression and values
above one million are rejected before mutation to keep bonus arithmetic bounded;
these sanity limits are separate from the game's rank cap.

## Persistence

`OfflineDataManager.MarkSaveIsRequired()` at RVA `0x590050` sets the game's
`isSaveRequired` flag. The feature calls it before the progression operations.
It does not force a save while the shop is open.

The regular `OfflineDataManager.SaveAllData` path at RVA `0x592CF0` reads the
shop system's current index and XP, wraps them as the game's obscured integers,
and calls `SaveChallengeProgress()`. The corresponding writes at RVAs
`0x5939BC`, `0x5939F4`, and `0x593A27` target `laundryCurrTierIdx`,
`laundryTierCurrXp`, and `laundryTierChallengeProgress`. Persistence therefore
uses normal game saves; the trainer does not parse or overwrite save files.

## Local evidence files

Generated wrapper names, signatures, singleton inheritance, and static getter
flags were inspected with Mono.Cecil. `OfflineDataManager` inherits
`Il2Cpp.Singleton<OfflineDataManager>`; its stored `_instance` getter is static,
`hasFinishedLoadSaveFile` is a public Boolean getter, and
`MarkSaveIsRequired()` is a public instance method returning `void`.

Ignored research artifacts contain the underlying evidence:

- `artifacts/research/interop/shop-level-members.txt`
- `artifacts/research/shop-challenges-native.txt` (RVAs `0x76B200–0x76B440`)
- `artifacts/research/shop-tier-up-native.txt` (RVAs `0x76B860–0x76C220`)
- `artifacts/research/shop-save-native.txt`
- `artifacts/research/shop-mark-save-native.txt`
- `artifacts/research/laundry-xp-native.txt` (previous inspection)

These are static implementation findings. Live listener behavior, visible rank
updates, rewards/unlocks, and persistence after reloading remain untested.
