using HappyCheats.Core;

namespace HappyCheats.App;

/// <summary>Client-session intent, separate from a module's confirmed engine state.</summary>
internal sealed class ToggleContinuity
{
    private readonly Dictionary<string, bool> requested = new(StringComparer.Ordinal);
    private readonly HashSet<string> pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> attempted = new(StringComparer.Ordinal);
    private readonly HashSet<string> cancelled = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> availability = new(StringComparer.Ordinal);
    private string? session;
    private bool interrupted;

    internal bool Requested(string id) => requested.GetValueOrDefault(id);
    internal bool IsOn(string id, TrainerStatus status) => Requested(id) || status.Enabled.GetValueOrDefault(id);

    internal void Request(string id, bool enabled)
    {
        requested[id] = enabled;
        attempted.Remove(id);
        if (enabled) { pending.Add(id); cancelled.Remove(id); }
        else { pending.Remove(id); cancelled.Add(id); }
    }

    internal void CancelAll(IEnumerable<FeatureDefinition> features)
    {
        foreach (var feature in features.Where(x => x.IsToggle)) Request(feature.Id, false);
    }

    internal void Observe(IReadOnlyList<FeatureDefinition> features, TrainerStatus status)
    {
        var boundary = interrupted || session != null && session != status.SessionId;
        if (!status.Connected || !status.Ready)
        {
            interrupted = true;
            foreach (var pair in requested.Where(x => x.Value)) pending.Add(pair.Key);
            return;
        }
        if (boundary)
        {
            attempted.Clear();
            foreach (var pair in requested.Where(x => x.Value)) pending.Add(pair.Key);
        }
        interrupted = false;
        session = status.SessionId;
        foreach (var feature in features.Where(x => x.IsToggle))
        {
            var id = feature.Id;
            var key = status.Values.GetValueOrDefault("available_" + id);
            if (availability.TryGetValue(id, out var previous) && previous != key) attempted.Remove(id);
            availability[id] = key;
            if (status.Enabled.GetValueOrDefault(id))
            {
                if (!cancelled.Contains(id))
                {
                    requested[id] = true;
                    pending.Remove(id);
                    attempted.Remove(id);
                }
            }
            else
            {
                cancelled.Remove(id);
                // A stable-session native hotkey Off remains authoritative.
                // Only interrupted/session-replaced controls retain On intent.
                if (!pending.Contains(id)) requested[id] = false;
            }
        }
    }

    internal void Attempted(string id) => attempted.Add(id);

    internal FeatureDefinition? Next(IReadOnlyList<FeatureDefinition> features, TrainerStatus status)
    {
        if (!status.Connected || !status.Ready) return null;
        var next = features.FirstOrDefault(feature => feature.IsToggle && pending.Contains(feature.Id)
            && Requested(feature.Id) && !attempted.Contains(feature.Id)
            && !status.Enabled.GetValueOrDefault(feature.Id)
            && !string.Equals(status.Values.GetValueOrDefault("available_" + feature.Id), "false", StringComparison.OrdinalIgnoreCase));
        if (next != null) attempted.Add(next.Id);
        return next;
    }
}
