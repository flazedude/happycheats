using System.Collections;
using System.Reflection;

namespace LaundryTrainer;

internal sealed class ShopLevel
{
    private readonly MemberInfo _gameInstance;
    private readonly MemberInfo _gameLoaded;
    private readonly MemberInfo _shopInstance;
    private readonly MemberInfo _saveInstance;
    private readonly MemberInfo _saveLoaded;
    private readonly MemberInfo _tiers;
    private readonly MemberInfo _rank;
    private readonly MemberInfo _xp;
    private readonly MemberInfo _xpRequired;
    private readonly MemberInfo _levelUpEvent;
    private readonly MemberInfo _challenges;
    private readonly MemberInfo _challengeType;
    private readonly MemberInfo _challengeMaximum;
    private readonly MemberInfo _challengeProgress;
    private readonly MethodInfo _isMaxLevel;
    private readonly MethodInfo _nextTier;
    private readonly MethodInfo _addXp;
    private readonly MethodInfo _addAllChallenges;
    private readonly MethodInfo _isTierUpReady;
    private readonly MethodInfo _checkTierUp;
    private readonly MethodInfo _markSaveRequired;

    public ShopLevel(Assembly game)
    {
        var manager = GameBindings.ResolveType(game, "GameManager");
        var shop = GameBindings.ResolveType(game, "LaundryTierSystem");
        var save = GameBindings.ResolveType(game, "OfflineDataManager");
        var tier = GameBindings.ResolveType(game, "LaundryTierContainer");
        var challenge = GameBindings.ResolveType(game, "LaundryTierChallengeContainer");
        var challengeType = GameBindings.ResolveType(game, "LaundryTierObjTypeEnum");

        _gameInstance = GameBindings.FindMember(manager, "_instance", typeof(object), isStatic: true);
        _gameLoaded = GameBindings.FindMember(manager, "isFinishLoading", typeof(bool));
        _shopInstance = GameBindings.FindMember(shop, "_instance", typeof(object), isStatic: true);
        _saveInstance = GameBindings.FindMember(save, "_instance", typeof(object), isStatic: true);
        _saveLoaded = GameBindings.FindMember(save, "hasFinishedLoadSaveFile", typeof(bool));
        _tiers = GameBindings.FindMember(shop, "tiers", typeof(object));
        _rank = GameBindings.FindMember(shop, "CurrRank", typeof(int));
        _xp = GameBindings.FindMember(shop, "CurrXp", typeof(int));
        _xpRequired = GameBindings.FindMember(shop, "XpRequired", typeof(int));
        _levelUpEvent = GameBindings.FindMember(shop, "OnLevelUp", typeof(object));
        _challenges = GameBindings.FindMember(tier, "challenges", typeof(object));
        _challengeType = GameBindings.FindMember(challenge, "type", challengeType);
        _challengeMaximum = GameBindings.FindMember(challenge, "MaxProgress", typeof(int));
        _challengeProgress = GameBindings.FindMember(challenge, "currProgress", typeof(float));
        _isMaxLevel = GameBindings.RequireMethod(shop, "IsMaxLevel", typeof(bool));
        _nextTier = GameBindings.RequireMethod(shop, "GetNextTierProgression", tier);
        _addXp = GameBindings.RequireMethod(shop, "AddXP", typeof(void), typeof(int));
        _addAllChallenges = GameBindings.RequireMethod(shop, "AddAllChallengesProgress", typeof(void), typeof(int));
        _isTierUpReady = GameBindings.RequireMethod(shop, "IsTierUpReady", typeof(bool));
        _checkTierUp = GameBindings.RequireMethod(shop, "CheckTierUp", typeof(bool));
        _markSaveRequired = GameBindings.RequireMethod(save, "MarkSaveIsRequired", typeof(void));
    }

    public bool Ready => TryContext() is not null;

    public int IncreaseLevel()
    {
        var (shop, save) = TryContext()
            ?? throw new InvalidOperationException("请先载入存档并进入洗衣店，再提升店铺等级。");
        if (GameBindings.Read(_tiers, shop) is null)
            throw new InvalidOperationException("当前场景尚未载入店铺等级数据。");
        if (_isMaxLevel.Invoke(shop, null) is true)
            throw new InvalidOperationException("洗衣店已达到当前游戏版本的等级上限。");

        var before = ReadRank(shop);
        var next = _nextTier.Invoke(shop, null)
            ?? throw new InvalidOperationException("当前场景尚未载入店铺升级条件。");
        if (GameBindings.Read(_levelUpEvent, shop) is null)
            throw new InvalidOperationException("当前场景的店铺升级事件尚未初始化。");
        var currentXp = (int)GameBindings.Read(_xp, shop)!;
        var requiredXp = (int)GameBindings.Read(_xpRequired, shop)!;
        // Native AddXP applies bonuses before clamping; reject abnormal data before
        // entering its Int32 arithmetic. These are data sanity limits, not rank caps.
        if (before < 1 || currentXp < 0 || requiredXp < 0 || requiredXp > 1_000_000)
            throw new InvalidOperationException("店铺升级经验数据异常，已取消升级。");
        var challengeAddition = GetChallengeAddition(next);

        // Do not force a disk save while the shop is open. The game's regular
        // SaveAllData path collects this system's rank index, XP and challenges.
        _markSaveRequired.Invoke(save, null);

        // Challenges only accept progress AFTER this tier's XP threshold is met.
        if (currentXp < requiredXp)
            _addXp.Invoke(shop, new object[] { requiredXp - currentXp });
        if (HasAdvanced(shop, before)) return ReadRank(shop);
        if ((int)GameBindings.Read(_xp, shop)! < requiredXp)
            throw new InvalidOperationException("店铺经验未达到升级要求，店铺等级未提升。");

        if (challengeAddition > 0)
            _addAllChallenges.Invoke(shop, new object[] { challengeAddition });
        if (HasAdvanced(shop, before)) return ReadRank(shop);
        if (_isTierUpReady.Invoke(shop, null) is not true)
            throw new InvalidOperationException("店铺升级条件尚未完成，店铺等级未提升。");

        // Use the real progression path for rank, next-tier data, XP reset,
        // OnLevelUp listeners and rank achievements. Never write the rank field.
        _checkTierUp.Invoke(shop, null);
        if (!HasAdvanced(shop, before))
            throw new InvalidOperationException("游戏未完成店铺升级，请确认当前场景可以升级。");
        return ReadRank(shop);
    }

    private int GetChallengeAddition(object next)
    {
        // Il2CppReferenceArray<T> implements the managed IEnumerable interface.
        if (GameBindings.Read(_challenges, next) is not IEnumerable challenges)
            throw new InvalidOperationException("店铺升级挑战数据尚未载入。");
        var types = new HashSet<object>();
        var addition = 0;
        foreach (var challenge in challenges)
        {
            if (challenge is null)
                throw new InvalidOperationException("店铺升级挑战数据异常，已取消升级。");
            var type = GameBindings.Read(_challengeType, challenge)!;
            var maximum = (int)GameBindings.Read(_challengeMaximum, challenge)!;
            var progress = (float)GameBindings.Read(_challengeProgress, challenge)!;
            // AddChallengesProgress addresses the first matching type. Duplicate
            // types would leave later entries incomplete, so reject before changes.
            if (!types.Add(type) || maximum < 0 || maximum > 1_000_000 ||
                !float.IsFinite(progress) || progress < 0)
                throw new InvalidOperationException("店铺升级挑战数据异常，已取消升级。");
            addition = Math.Max(addition, (int)Math.Ceiling(Math.Max(0d, maximum - (double)progress)));
        }
        // Native AddAllChallengesProgress only touches nextTierProgression and
        // clamps every challenge to its own MaxProgress; later tiers stay intact.
        return addition;
    }

    private bool HasAdvanced(object shop, int before)
    {
        var after = ReadRank(shop);
        if (after == before) return false;
        if (after != before + 1)
            throw new InvalidOperationException($"店铺等级发生了意外变化，当前为 {after} 级；已停止继续升级。");
        return true;
    }

    private int ReadRank(object shop) => (int)GameBindings.Read(_rank, shop)!;

    private (object Shop, object Save)? TryContext()
    {
        // Stored singletons avoid Instance getters that can create scene objects.
        var manager = GameBindings.Read(_gameInstance, null);
        var shop = GameBindings.Read(_shopInstance, null);
        var save = GameBindings.Read(_saveInstance, null);
        return manager is not null && GameBindings.Read(_gameLoaded, manager) is true &&
            shop is not null && save is not null && GameBindings.Read(_saveLoaded, save) is true
                ? (shop, save) : null;
    }
}
