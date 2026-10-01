using System.Reflection;
using HarmonyLib;

namespace LaundryTrainer;

internal sealed class WashSpeed : IDisposable
{
    private readonly HarmonyLib.Harmony _harmony = new("LaundryTrainer.WashSpeed");
    private readonly MethodInfo _update;
    private readonly MemberInfo _timer;
    private readonly Action<string> _report;
    private static WashSpeed? _active;
    private bool _disposed;

    public bool Enabled { get; set; }
    public float Multiplier { get; set; } = 5;
    public bool Faulted { get; private set; }

    public WashSpeed(Assembly game, Action<string> report)
    {
        _report = report;
        var washer = GameBindings.ResolveType(game, "WashingMachinePickableScript");
        _update = GameBindings.RequireMethod(washer, "WashingUpdate", typeof(void));
        _timer = GameBindings.FindMember(washer, "timer", typeof(float));
        if (_timer is PropertyInfo { SetMethod: null })
            throw new MissingMethodException("洗衣机计时器不支持写入。");
        if (_active is not null) throw new InvalidOperationException("快速洗衣补丁已安装。");
        try
        {
            _harmony.Patch(_update,
                prefix: new HarmonyMethod(typeof(WashSpeed), nameof(BeforeUpdate)),
                postfix: new HarmonyMethod(typeof(WashSpeed), nameof(AfterUpdate)));
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
        // Only accelerate time the original game actually advanced. This preserves
        // pauses, obstacles, reset timers and the game's completion transition.
        if (!float.IsFinite(before) || !float.IsFinite(after) || !float.IsFinite(multiplier) ||
            multiplier <= 1 || multiplier > 20 || after <= 0 || after >= before) return after;
        return (float)Math.Max(0d, after - ((double)before - after) * (multiplier - 1));
    }

    private void Fail(Exception error)
    {
        Enabled = false;
        Faulted = true;
        _report($"快速洗衣已停用：{error.GetBaseException().Message}");
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
