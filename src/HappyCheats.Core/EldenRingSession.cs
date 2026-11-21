using System.Globalization;
using HappyCheats.Core.EldenRing;

namespace HappyCheats.Core;

/// <summary>Confirmed local effects. Requested toggles are restored by the shared client after session changes.</summary>
internal sealed class EldenRingSession : IDisposable
{
    internal interface IRuntime : IDisposable
    {
        EldenRingState Read();
        EldenRingState Refill(EldenRingState expected, bool health, bool fp, bool stamina);
        EldenRingState SetRunes(EldenRingState expected, int value);
        EldenRingState SetPlayerFlag(EldenRingState expected, string featureId, bool enabled);
        void ReleasePlayerFlags();
    }

    private readonly IRuntime runtime;
    private string context = "";
    private string session = Guid.NewGuid().ToString("N");
    private bool health, fp, stamina, damageProtection, consumables, disposed;
    private DateTime? lastPoll;
    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    internal EldenRingSession(IRuntime runtime) => this.runtime = runtime;

    private EldenRingState Observe()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EldenRingState state;
        try { state = runtime.Read(); }
        catch (Exception error) { InvalidateAfterFailure(error); throw; }
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
        if (state.Ready && damageProtection) state = PlayerFlag(state, "er_damage_protection", true);
        if (state.Ready && consumables) state = PlayerFlag(state, "er_consumables", true);
        return Status(state);
    }

    internal TrainerStatus Command(TrainerStatus requested, string feature, bool? enabled)
    {
        ValidateCommand(feature, enabled);
        var state = Observe();
        // Turning effects off must remain possible after a death or loading screen.
        if (feature == "disable_all") { Clear(); runtime.ReleasePlayerFlags(); return Status(state); }
        if (enabled == false)
        {
            SetToggle(feature, false);
            if (feature is "er_damage_protection" or "er_consumables") state = PlayerFlag(state, feature, false);
            return Status(state);
        }
        if (!state.Ready || requested.SessionId != session || !requested.Connected || !requested.Ready)
            throw new InvalidOperationException("Elden Ring character changed. Refresh and enable the controls again.");
        if (feature is "er_damage_protection" or "er_consumables")
        {
            var applied = PlayerFlag(state, feature, true);
            SetToggle(feature, true);
            return Status(applied);
        }
        var oneShot = feature == "er_restore";
        var next = Refill(state, oneShot || feature == "er_health", oneShot || feature == "er_fp", oneShot || feature == "er_stamina");
        if (enabled == true) SetToggle(feature, true);
        return Status(next);
    }

    internal TrainerStatus SetRunes(TrainerStatus requested, int value)
    {
        ValidateValue("er_runes", value);
        var state = Observe();
        if (!state.Ready || requested.SessionId != session || !requested.Connected || !requested.Ready)
            throw new InvalidOperationException("Elden Ring character changed. Refresh before setting runes. / 角色已改变，请刷新后再设置卢恩。");
        try
        {
            var result = runtime.SetRunes(state, value);
            if (!result.Ready || result.Context != state.Context)
                throw new IOException("Elden Ring character changed while setting runes. Refresh; do not automatically repeat the action.");
            return Status(result);
        }
        catch (Exception error) { InvalidateAfterFailure(error); throw; }
    }

    internal static int ValidateValue(string feature, decimal value)
    {
        if (feature != "er_runes") throw new ArgumentException("Unknown Elden Ring numeric control.");
        if (value < 0 || value > 999_999_999 || decimal.Truncate(value) != value)
            throw new ArgumentOutOfRangeException(nameof(value), "Enter a whole rune amount from 0 to 999999999. / 请输入 0 至 999999999 的整数卢恩数量。");
        return decimal.ToInt32(value);
    }

    private EldenRingState PlayerFlag(EldenRingState expected, string feature, bool enabled)
    {
        try
        {
            var result = runtime.SetPlayerFlag(expected, feature, enabled);
            if (enabled && (!result.Ready || result.Context != expected.Context
                || (feature == "er_damage_protection" ? !result.DamageProtected : !result.UnlimitedConsumables)))
                throw new IOException("Elden Ring player effect was not confirmed. Refresh before trying again.");
            return result;
        }
        catch (Exception error) { InvalidateAfterFailure(error); throw; }
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
        catch (Exception error) { InvalidateAfterFailure(error); throw; }
    }

    internal static void ValidateCommand(string feature, bool? enabled)
    {
        if (feature is "disable_all" or "er_restore")
        {
            if (enabled != null) throw new ArgumentException("This Elden Ring action does not accept a toggle value.");
        }
        else if (feature is "er_health" or "er_fp" or "er_stamina" or "er_damage_protection" or "er_consumables")
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
            case "er_damage_protection": damageProtection = value; break;
            case "er_consumables": consumables = value; break;
        }
    }

    private TrainerStatus Status(EldenRingState state)
    {
        var values = new Dictionary<string, string>();
        if (state.Ready)
        {
            values["Health"] = Pair(state.Health, state.MaxHealth);
            values["FP"] = Pair(state.FP, state.MaxFP);
            values["er_stamina_value"] = Pair(state.Stamina, state.MaxStamina);
            if (state.Runes.HasValue) values["Runes / 卢恩"] = state.Runes.Value.ToString(CultureInfo.InvariantCulture);
        }
        return new TrainerStatus(true, state.Ready, state.Ready
            ? "Offline Elden Ring connected. Player controls are experimental; keep this page open."
            : "Offline Elden Ring connected. Load a living character to use controls.", session,
            new Dictionary<string, bool> { ["er_health"] = health, ["er_fp"] = fp, ["er_stamina"] = stamina,
                ["er_damage_protection"] = damageProtection && state.DamageProtected,
                ["er_consumables"] = consumables && state.UnlimitedConsumables }, values);
    }

    private static string Pair(int current, int maximum) => current.ToString(CultureInfo.InvariantCulture) + " / " + maximum.ToString(CultureInfo.InvariantCulture);
    private void Clear() => health = fp = stamina = damageProtection = consumables = false;
    private void Invalidate()
    {
        Clear();
        context = "";
        session = Guid.NewGuid().ToString("N");
        runtime.ReleasePlayerFlags();
    }
    private void InvalidateAfterFailure(Exception operation)
    {
        try { Invalidate(); }
        catch (Exception cleanup)
        {
            throw new IOException(operation.Message + " Player effect cleanup also failed: " + cleanup.Message,
                new AggregateException(operation, cleanup));
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        Clear(); disposed = true;
        try { runtime.ReleasePlayerFlags(); }
        finally { runtime.Dispose(); }
    }
}
