using HappyCheats.Core;
using HappyCheats.Core.Updates;

namespace HappyCheats.App;

// Adapt the existing module diagnostics without changing the frozen module contract.
internal static class TrainerVersionWarning
{
    internal static bool Newer(string? candidate, string? current) => candidate != null && current != null
        && ReleaseVersion.Parse(candidate).CompareTo(ReleaseVersion.Parse(current)) > 0;

    internal static bool RuntimeMismatch(TrainerStatus status) => !status.Ready && (
        status.Message.EndsWith("to load the current trainer module.", StringComparison.Ordinal)
        || status.Message == "Trainer session does not match this game build."
        || status.Message.StartsWith("Project Hospital trainer version ", StringComparison.Ordinal)
        || status.Message is "Invalid Civilization VI trainer status schema or runtime version."
            or "Invalid Civilization VII trainer status schema or runtime version."
            or "Restart Stardew Valley with the current HappyCheats SMAPI mod."
            or "Crimson Desert trainer identity or heartbeat is invalid. Reconnect or restart the game."
            or "GTA V Enhanced trainer identity or heartbeat is invalid. Restart Story Mode.");
}
