using System.Globalization;
using HappyCheats.Core.EldenRing;

namespace HappyCheats.Core;

/// <summary>Resource refill policy. It never keeps a toggle across a player or connection change.</summary>
internal sealed class EldenRingSession : IDisposable
{
    internal interface IRuntime : IDisposable
    {
        EldenRingState Read();
        EldenRingState Refill(EldenRingState expected, bool health, bool fp, bool stamina);
    }

    private readonly IRuntime runtime;
    private string context = "";
    private string session = Guid.NewGuid().ToString("N");
    private bool health, fp, stamina, disposed;
    private DateTime? lastPoll;
    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    internal EldenRingSession(IRuntime runtime) => this.runtime = runtime;

    private EldenRingState Observe()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EldenRingState state;
        try { state = runtime.Read(); }
        catch { Invalidate(); throw; }
        var now = Clock();
        if (!state.Ready || state.Context != context ||
            (lastPoll.HasValue && (now < lastPoll.Value || now - lastPoll.Value > TimeSpan.FromSeconds(4))))
        {
            Invalidate();
        }
        context = state.Ready ? state.Context : "";
        lastPoll = now;
        return state;
    }

    internal TrainerStatus Poll()
    {
        var state = Observe();
        if (state.Ready && (health || fp || stamina)) state = Refill(state, health, fp, stamina);
        return Status(state);
    }

    internal TrainerStatus Command(TrainerStatus requested, string feature, bool? enabled)
    {
        ValidateCommand(feature, enabled);
        var state = Observe();
        // Turning effects off must remain possible after a death or loading screen.
        if (feature == "disable_all") { Clear(); return Status(state); }
        if (enabled == false)
        {
            SetToggle(feature, false);
            return Status(state);
        }
        if (!state.Ready || requested.SessionId != session || !requested.Connected || !requested.Ready)
            throw new InvalidOperationException("Elden Ring character changed. Refresh and enable the controls again.");
        var oneShot = feature == "er_restore";
        var next = Refill(state, oneShot || feature == "er_health", oneShot || feature == "er_fp", oneShot || feature == "er_stamina");
        if (enabled == true) SetToggle(feature, true);
        return Status(next);
    }

    private EldenRingState Refill(EldenRingState expected, bool refillHealth, bool refillFp, bool refillStamina)
    {
        try
        {
            var result = runtime.Refill(expected, refillHealth, refillFp, refillStamina);
            if (!result.Ready || result.Context != expected.Context)
                throw new InvalidOperationException("Elden Ring character changed during refill. Refresh and enable the controls again.");
            return result;
        }
        catch { Invalidate(); throw; }
    }

    internal static void ValidateCommand(string feature, bool? enabled)
    {
        if (feature is "disable_all" or "er_restore")
        {
            if (enabled != null) throw new ArgumentException("This Elden Ring action does not accept a toggle value.");
        }
        else if (feature is "er_health" or "er_fp" or "er_stamina")
        {
            if (enabled == null) throw new ArgumentException("An explicit Elden Ring toggle state is required.");
        }
        else throw new ArgumentException("Unknown Elden Ring control.");
    }

    private void SetToggle(string feature, bool value)
    {
        switch (feature)
        {
            case "er_health": health = value; break;
            case "er_fp": fp = value; break;
            case "er_stamina": stamina = value; break;
        }
    }

    private TrainerStatus Status(EldenRingState state)
    {
        var values = new Dictionary<string, string>();
        if (state.Ready)
        {
            values["Health"] = Pair(state.Health, state.MaxHealth);
            values["FP"] = Pair(state.FP, state.MaxFP);
            values["Stamina"] = Pair(state.Stamina, state.MaxStamina);
        }
        return new TrainerStatus(true, state.Ready, state.Ready
            ? "Offline Elden Ring connected. Refills are experimental; keep this page open."
            : "Offline Elden Ring connected. Load a living character to use refills.", session,
            new Dictionary<string, bool> { ["er_health"] = health, ["er_fp"] = fp, ["er_stamina"] = stamina }, values);
    }

    private static string Pair(int current, int maximum) => current.ToString(CultureInfo.InvariantCulture) + " / " + maximum.ToString(CultureInfo.InvariantCulture);
    private void Clear() => health = fp = stamina = false;
    private void Invalidate()
    {
        Clear();
        context = "";
        session = Guid.NewGuid().ToString("N");
    }
    public void Dispose()
    {
        if (disposed) return;
        Clear(); disposed = true; runtime.Dispose();
    }
}
