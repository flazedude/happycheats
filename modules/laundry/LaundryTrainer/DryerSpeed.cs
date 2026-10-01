using System.Reflection;
using HarmonyLib;

namespace LaundryTrainer;

internal sealed class DryerSpeed : IDisposable
{
    private readonly HarmonyLib.Harmony _harmony = new("LaundryTrainer.DryerSpeed");
    private readonly MethodInfo _update;
    private readonly MemberInfo _timer;
    private readonly Action<string> _report;
    private static DryerSpeed? _active;
    private bool _disposed;
    private float _multiplier = 5;

    public bool Enabled { get; set; }
    public bool Faulted { get; private set; }

    public float Multiplier
    {
        get => _multiplier;
        set
        {
            if (!float.IsFinite(value) || value < 1 || value > 20)
                throw new ArgumentOutOfRangeException(nameof(value), "烘干速度必须在 1 至 20 倍之间。");
            _multiplier = value;
        }
    }

    public DryerSpeed(Assembly game, Action<string> report)
    {
        _report = report;
        var dryer = GameBindings.ResolveType(game, "DryingMachinePickableScript");
        _update = GameBindings.RequireMethod(dryer, "DryingUpdate", typeof(void));
        _timer = GameBindings.FindMember(dryer, "timer", typeof(float));
        if (_timer is PropertyInfo { SetMethod: null } || _timer is FieldInfo { IsInitOnly: true })
            throw new MissingMethodException("烘干机计时器不支持写入。");
        if (_active is not null) throw new InvalidOperationException("快速烘干补丁已安装。");
        try
        {
            _harmony.Patch(_update,
                prefix: new HarmonyMethod(typeof(DryerSpeed), nameof(BeforeUpdate)),
                postfix: new HarmonyMethod(typeof(DryerSpeed), nameof(AfterUpdate)));
            _active = this;
        }
        catch
        {
            _harmony.Unpatch(_update, HarmonyPatchType.All, _harmony.Id);
            throw;
        }
    }

    private static void BeforeUpdate(object __instance, out float __state)
    {
        __state = float.NaN;
        var patch = _active;
        if (patch is not { Enabled: true, Faulted: false }) return;
        try { __state = (float)GameBindings.Read(patch._timer, __instance)!; }
        catch (Exception error) { patch.Fail(error); }
    }

    private static void AfterUpdate(object __instance, float __state)
    {
        var patch = _active;
        if (patch is not { Enabled: true, Faulted: false } || !float.IsFinite(__state)) return;
        try
        {
            var remaining = (float)GameBindings.Read(patch._timer, __instance)!;
            var accelerated = Accelerate(__state, remaining, patch.Multiplier);
            if (remaining != accelerated) GameBindings.Write(patch._timer, __instance, accelerated);
        }
        catch (Exception error) { patch.Fail(error); }
    }

    internal static float Accelerate(float before, float after, float multiplier)
    {
        // Verified DryingUpdate counts down by deltaTime and enters Done at zero.
        // Only extend the countdown the original method actually performed. Leave
        // unchanged/reset timers alone; the game owns its next completion transition.
        if (!float.IsFinite(before) || !float.IsFinite(after) || !float.IsFinite(multiplier) ||
            multiplier <= 1 || multiplier > 20 || after <= 0 || after >= before) return after;
        return (float)Math.Max(0d, after - ((double)before - after) * (multiplier - 1));
    }

    private void Fail(Exception error)
    {
        Enabled = false;
        Faulted = true;
        _report($"快速烘干已停用：{error.GetBaseException().Message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Enabled = false;
        if (_active == this) _active = null;
        _harmony.Unpatch(_update, HarmonyPatchType.All, _harmony.Id);
    }
}
