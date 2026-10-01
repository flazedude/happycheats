using System.Reflection;

namespace LaundryTrainer;

internal sealed class GameBindings
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private readonly MemberInfo _gameInstance;
    private readonly MemberInfo _xpInstance;
    private readonly MemberInfo _loaded;
    private readonly MemberInfo _xpLoaded;
    private readonly MethodInfo _addMoney;
    private readonly MethodInfo _getMoney;
    private readonly MethodInfo _addXp;
    private readonly object _lp;

    public GameBindings(Assembly game)
    {
        var manager = ResolveType(game, "GameManager");
        var xp = ResolveType(game, "LevelXPManager");
        var money = ResolveType(game, "MoneyType");
        _lp = Enum.Parse(money, "Lp");
        _gameInstance = FindMember(manager, "_instance", typeof(object), isStatic: true);
        _xpInstance = FindMember(xp, "_instance", typeof(object), isStatic: true);
        _loaded = FindMember(manager, "isFinishLoading", typeof(bool));
        _xpLoaded = FindMember(xp, "hasLoaded", typeof(bool));
        _addMoney = RequireMethod(manager, "AddCurrency", typeof(void), money, typeof(float), typeof(bool));
        _getMoney = RequireMethod(manager, "GetCurrency", typeof(float), money);
        _addXp = RequireMethod(xp, "AddXP", typeof(float), typeof(float), typeof(bool), typeof(bool));
    }

    public bool Ready => TryGame() is not null;

    public float AddMoney(float amount)
    {
        ValidateAmount(amount, 1_000_000);
        var manager = TryGame() ?? throw new InvalidOperationException("请先载入存档，再增加金钱。");
        _addMoney.Invoke(manager, new[] { _lp, amount, (object)true });
        return (float)_getMoney.Invoke(manager, new[] { _lp })!;
    }

    public float AddXp(float amount)
    {
        ValidateAmount(amount, 10_000);
        if (TryGame() is null)
            throw new InvalidOperationException("请先载入存档，再增加经验。");
        var manager = Read(_xpInstance, null);
        if (manager is null || Read(_xpLoaded, manager) is not true)
            throw new InvalidOperationException("当前场景尚未载入玩家经验。");
        // Use the game's level-cap/reward path; bypass only the optional IAP multiplier.
        return (float)_addXp.Invoke(manager, new object[] { amount, false, true })!;
    }

    private object? TryGame()
    {
        // Read the stored singleton. Its Instance getter can create objects in some scenes.
        var manager = Read(_gameInstance, null);
        return manager is not null && Read(_loaded, manager) is true ? manager : null;
    }

    internal static Type ResolveType(Assembly assembly, string name) =>
        assembly.GetType("Il2Cpp." + name) ?? assembly.GetType("Il2Cpp" + name) ?? assembly.GetType(name)
        ?? throw new TypeLoadException($"游戏版本不受支持：未找到 {name}。");

    internal static MethodInfo RequireMethod(Type type, string name, Type result, params Type[] arguments)
    {
        var method = type.GetMethod(name, Members, binder: null, types: arguments, modifiers: null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new MissingMethodException($"游戏接口不兼容：未找到所需方法 {type.FullName}.{name}。");
        return method;
    }

    internal static MemberInfo FindMember(Type type, string name, Type expectedType, bool isStatic = false)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(name, Members);
            if (property?.GetMethod?.IsStatic == isStatic &&
                (expectedType == typeof(object) || property.PropertyType == expectedType))
                return property;
            var field = current.GetField(name, Members);
            if (field?.IsStatic == isStatic &&
                (expectedType == typeof(object) || field.FieldType == expectedType))
                return field;
        }
        throw new MissingMemberException($"游戏接口不兼容：未找到所需成员 {type.FullName}.{name}。");
    }

    internal static object? Read(MemberInfo member, object? instance) => member switch
    {
        PropertyInfo property => property.GetValue(instance),
        FieldInfo field => field.GetValue(instance),
        _ => throw new ArgumentException("目标成员必须是属性或字段。", nameof(member))
    };

    internal static void Write(MemberInfo member, object instance, float value)
    {
        if (member is PropertyInfo property) property.SetValue(instance, value);
        else if (member is FieldInfo field) field.SetValue(instance, value);
        else throw new ArgumentException("目标成员必须是属性或字段。", nameof(member));
    }

    private static void ValidateAmount(float amount, float maximum)
    {
        if (!float.IsFinite(amount) || amount <= 0 || amount > maximum)
            throw new ArgumentOutOfRangeException(nameof(amount), $"增加的数值必须大于 0，且不超过 {maximum:N0}。");
    }
}
