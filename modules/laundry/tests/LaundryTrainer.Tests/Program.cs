using System.Reflection;
using LaundryTrainer;
using Game = Il2Cpp.GameManager;
using Xp = Il2Cpp.LevelXPManager;
using Washer = Il2Cpp.WashingMachinePickableScript;

var assembly = typeof(Game).Assembly;
var tests = new (string Name, Action Run)[]
{
    ("Bindings gate money and XP until the current save is ready", () =>
    {
        Game.SetForTest(null);
        Xp.SetForTest(null);
        var bindings = new GameBindings(assembly);
        Check.False(bindings.Ready);
        Check.Throws<InvalidOperationException>(() => bindings.AddMoney(10));
        Check.Throws<InvalidOperationException>(() => bindings.AddXp(10));
        var game = new Game();
        var xp = new Xp { hasLoaded = true };
        Game.SetForTest(game);
        Xp.SetForTest(xp);
        Check.False(bindings.Ready);
        Check.Throws<InvalidOperationException>(() => bindings.AddMoney(10));
        Check.Throws<InvalidOperationException>(() => bindings.AddXp(10));
        Check.Equal(0, game.MoneyCalls);
        Check.Equal(0, xp.XpCalls);
        game.isFinishLoading = true;
        Check.True(bindings.Ready);
    }),
    ("Money uses Lp and the exact overload, returning the game's observed balance", () =>
    {
        var (bindings, game, _) = ReadyGame(assembly);
        Check.Equal(1500f, bindings.AddMoney(2000));
        Check.Equal((Il2Cpp.MoneyType.Lp, 2000f, true), game.LastMoney);
        Check.Equal(1, game.MoneyCalls);
    }),
    ("XP requires its own loaded manager and preserves game XP adjustments", () =>
    {
        var (bindings, _, xp) = ReadyGame(assembly);
        Xp.SetForTest(null);
        Check.Throws<InvalidOperationException>(() => bindings.AddXp(100));
        Xp.SetForTest(xp);
        xp.hasLoaded = false;
        Check.Throws<InvalidOperationException>(() => bindings.AddXp(100));
        Check.Equal(0, xp.XpCalls);
        xp.hasLoaded = true;
        Check.Equal(42f, bindings.AddXp(100));
        Check.Equal((100f, false, true), xp.LastXp);
        Check.Equal(1, xp.XpCalls);
    }),
    ("Bindings re-read singleton storage after a scene changes", () =>
    {
        var (bindings, oldGame, oldXp) = ReadyGame(assembly);
        var newGame = new Game { isFinishLoading = true };
        var newXp = new Xp { hasLoaded = true };
        Game.SetForTest(newGame);
        Xp.SetForTest(newXp);
        bindings.AddMoney(100);
        bindings.AddXp(10);
        Check.Equal(0, oldGame.MoneyCalls);
        Check.Equal(0, oldXp.XpCalls);
        Check.Equal(1, newGame.MoneyCalls);
        Check.Equal(1, newXp.XpCalls);
        Game.SetForTest(null);
        Check.False(bindings.Ready);
    }),
    ("Invalid amounts never reach game methods; documented bounds are accepted", () =>
    {
        var (bindings, game, xp) = ReadyGame(assembly);
        foreach (var bad in new[] { 0f, -1, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Check.Throws<ArgumentOutOfRangeException>(() => bindings.AddMoney(bad));
            Check.Throws<ArgumentOutOfRangeException>(() => bindings.AddXp(bad));
        }
        Check.Throws<ArgumentOutOfRangeException>(() => bindings.AddMoney(1_000_001));
        Check.Throws<ArgumentOutOfRangeException>(() => bindings.AddXp(10_001));
        Check.Equal(0, game.MoneyCalls);
        Check.Equal(0, xp.XpCalls);
        bindings.AddMoney(1_000_000);
        bindings.AddXp(10_000);
        Check.Equal(1, game.MoneyCalls);
        Check.Equal(1, xp.XpCalls);
    }),
    ("Reflection supports private inherited generic fields and timer writes", () =>
    {
        var manager = new FieldFixtures.Manager();
        FieldFixtures.Manager.SetForTest(manager);
        var singleton = GameBindings.FindMember(typeof(FieldFixtures.Manager), "_instance", typeof(object), true);
        Check.True(singleton is FieldInfo);
        Check.Same(manager, GameBindings.Read(singleton, null));
        var loaded = GameBindings.FindMember(typeof(FieldFixtures.Manager), "loaded", typeof(bool));
        Check.Equal(true, GameBindings.Read(loaded, manager));
        var timer = GameBindings.FindMember(typeof(FieldFixtures.Manager), "timer", typeof(float));
        GameBindings.Write(timer, manager, 3);
        Check.Equal(3f, manager.Remaining);
    }),
    ("Type and signature validation rejects incompatible builds", () =>
    {
        Check.Equal(typeof(Game), GameBindings.ResolveType(assembly, "GameManager"));
        Check.Equal(typeof(LegacyOnly), GameBindings.ResolveType(assembly, "LegacyOnly"));
        Check.Equal(typeof(Il2CppJoinedOnly), GameBindings.ResolveType(assembly, "JoinedOnly"));
        Check.Throws<TypeLoadException>(() => GameBindings.ResolveType(assembly, "AbsentGameType"));
        var type = typeof(FieldFixtures.WrongMembers);
        Check.Throws<MissingMemberException>(() => GameBindings.FindMember(type, "loaded", typeof(bool)));
        Check.Throws<MissingMethodException>(() => GameBindings.RequireMethod(type, "StaticMethod", typeof(float)));
        Check.Throws<MissingMethodException>(() => GameBindings.RequireMethod(type, "IncorrectResult", typeof(float)));
        Check.Throws<MissingMethodException>(() => GameBindings.RequireMethod(type, "WrongArguments", typeof(void), typeof(float)));
    }),
    ("Countdown scales elapsed time at 2x, 5x, and the 20x limit", () =>
    {
        Check.Equal(98f, WashSpeed.Accelerate(100, 99, 2));
        Check.Equal(95f, WashSpeed.Accelerate(100, 99, 5));
        Check.Equal(80f, WashSpeed.Accelerate(100, 99, 20));
        Check.Equal(0f, WashSpeed.Accelerate(2, 1, 5));
        // Large finite values must clamp to zero rather than overflow to infinity.
        Check.Equal(0f, WashSpeed.Accelerate(float.MaxValue, float.MaxValue / 2, 20));
    }),
    ("Countdown preserves pauses, resets, completed timers, and invalid input", () =>
    {
        Check.Equal(50f, WashSpeed.Accelerate(50, 50, 5));
        Check.Equal(100f, WashSpeed.Accelerate(20, 100, 5));
        Check.Equal(0f, WashSpeed.Accelerate(1, 0, 5));
        Check.Equal(-1f, WashSpeed.Accelerate(1, -1, 5));
        foreach (var multiplier in new[] { -1f, 0, 1, 21, float.MaxValue, float.NaN, float.PositiveInfinity })
            Check.Equal(9f, WashSpeed.Accelerate(10, 9, multiplier));
        Check.Equal(9f, WashSpeed.Accelerate(float.NaN, 9, 5));
        Check.Equal(9f, WashSpeed.Accelerate(float.PositiveInfinity, 9, 5));
        Check.True(float.IsNaN(WashSpeed.Accelerate(10, float.NaN, 5)));
        Check.Equal(float.PositiveInfinity, WashSpeed.Accelerate(10, float.PositiveInfinity, 5));
    }),
    ("Real managed Harmony patch starts disabled and toggles for existing cycles", () =>
    {
        var reports = new List<string>();
        using var speed = new WashSpeed(assembly, reports.Add);
        Check.False(speed.Enabled);
        Check.False(speed.Faulted);
        var washer = new Washer();
        washer.Tick();
        Check.Equal(99f, washer.Remaining);
        Check.Equal(0, washer.TimerWrites);
        speed.Enabled = true;
        washer.Tick();
        Check.Equal(94f, washer.Remaining);
        speed.Enabled = false;
        washer.Tick();
        Check.Equal(93f, washer.Remaining);
        Check.Equal(0, reports.Count);
    }),
    ("Harmony hook preserves pause/reset and lets the game finish a cycle", () =>
    {
        using var speed = new WashSpeed(assembly, _ => { }) { Enabled = true };
        var washer = new Washer { Remaining = 2, Paused = true };
        washer.Tick();
        Check.Equal(2f, washer.Remaining);
        Check.Equal(0, washer.TimerWrites);
        washer.Paused = false;
        washer.ResetCycle = true;
        washer.Tick();
        Check.Equal(100f, washer.Remaining);
        Check.Equal(0, washer.TimerWrites);
        washer.ResetCycle = false;
        washer.Remaining = 2;
        washer.Tick();
        Check.Equal(0f, washer.Remaining);
        Check.False(washer.Completed); // Completion runs in the next original update.
        washer.Tick();
        Check.True(washer.Completed);
        Check.Equal(-1f, washer.Remaining);
        Check.Equal(1, washer.TimerWrites);
    }),
    ("Harmony timer failures disable the feature once and preserve game updates", () =>
    {
        foreach (var failOnRead in new[] { true, false })
        {
            var reports = new List<string>();
            using var speed = new WashSpeed(assembly, reports.Add) { Enabled = true };
            var washer = new Washer { ThrowOnRead = failOnRead, ThrowOnWrite = !failOnRead };
            washer.Tick();
            Check.Equal(99f, washer.Remaining);
            Check.False(speed.Enabled);
            Check.True(speed.Faulted);
            Check.Equal(1, reports.Count);
            Check.True(reports[0].Contains(failOnRead ? "Timer read failed" : "Timer write failed"));
            speed.Enabled = true; // The fault latch still prevents further timer access.
            washer.Tick();
            Check.Equal(98f, washer.Remaining);
            Check.Equal(2, washer.Updates);
            Check.Equal(1, reports.Count);
        }
    }),
    ("Duplicate installation is rejected without disturbing the active patch", () =>
    {
        using var speed = new WashSpeed(assembly, _ => { }) { Enabled = true };
        Check.Throws<InvalidOperationException>(() => new WashSpeed(assembly, _ => { }));
        var washer = new Washer();
        washer.Tick();
        Check.Equal(95f, washer.Remaining);
    }),
    ("Disposing the patch restores normal speed and permits a fresh installation", () =>
    {
        var speed = new WashSpeed(assembly, _ => { }) { Enabled = true };
        speed.Dispose();
        var washer = new Washer();
        washer.Tick();
        Check.Equal(99f, washer.Remaining);
        using var next = new WashSpeed(assembly, _ => { }) { Enabled = true, Multiplier = 2 };
        washer.Tick();
        Check.Equal(97f, washer.Remaining);
    }),
    ("Disposing an old object again cannot remove its replacement patch", () =>
    {
        var old = new WashSpeed(assembly, _ => { });
        old.Dispose();
        using var current = new WashSpeed(assembly, _ => { }) { Enabled = true };
        old.Dispose();
        var washer = new Washer();
        washer.Tick();
        Check.Equal(95f, washer.Remaining);
    })
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}\n{error}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed ({Check.Assertions} assertions).");
return failures == 0 ? 0 : 1;

static (GameBindings Bindings, Game Game, Xp Xp) ReadyGame(Assembly assembly)
{
    var game = new Game { isFinishLoading = true };
    var xp = new Xp { hasLoaded = true };
    Game.SetForTest(game);
    Xp.SetForTest(xp);
    return (new GameBindings(assembly), game, xp);
}

internal static class Check
{
    public static int Assertions { get; private set; }
    public static void True(bool actual) { Assertions++; if (!actual) throw new Exception("Expected true."); }
    public static void False(bool actual) => True(!actual);
    public static void Equal<T>(T expected, T actual)
    {
        Assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}, got {actual}.");
    }
    public static void Same(object expected, object? actual)
    {
        Assertions++;
        if (!ReferenceEquals(expected, actual)) throw new Exception("Expected the same instance.");
    }
    public static void Throws<T>(Action action) where T : Exception
    {
        Assertions++;
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
