namespace HappyCheats.Core;

public sealed record FeatureChoice(string Value, string Label);
public sealed record FeatureNumber(decimal Minimum, decimal Maximum, decimal DefaultValue, decimal Step = 1, string? Unit = null);
public sealed record FeatureDefinition(string Id, string Name, string Description, string Hotkey, bool IsToggle = false, string Group = "Trainer", IReadOnlyList<FeatureChoice>? Choices = null, string? ChoiceValueKey = null, FeatureNumber? Number = null);
public sealed record GameDefinition(string Id, string Name, string Subtitle, string AppId, string Initials, string Description, IReadOnlyList<FeatureDefinition> Features);
public sealed record GameInstallation(string Directory, string Build, bool IsInstalled, bool TrainerInstalled, bool IsRunning, bool Compatible, string Message);
public sealed record TrainerStatus(bool Connected, bool Ready, string Message, string SessionId, IReadOnlyDictionary<string, bool> Enabled, IReadOnlyDictionary<string, string> Values, string? LastCommand = null)
{
    public static TrainerStatus Offline(string message = "Launch the game to connect your trainer.") => new(false, false, message, "", new Dictionary<string, bool>(), new Dictionary<string, string>());
}
public interface IGameModule
{
    GameDefinition Definition { get; }
    /// <summary>Executable paths relative to the installation, used to attach the shared game menu.</summary>
    IReadOnlyList<string> GameExecutables => Array.Empty<string>();
    Task<GameInstallation> DiscoverAsync(string? directory = null);
    Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null);
    Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null);
    Task<TrainerStatus> ReadStatusAsync(GameInstallation installation);
    Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null);
    Task LaunchAsync(GameInstallation installation);
    /// <summary>Release a client-owned session when changing games or closing the client.</summary>
    void Deactivate() { }
}

public interface IFeatureChoiceModule
{
    Task SelectAsync(GameInstallation installation, TrainerStatus status, string featureId, string choice);
}

public interface IFeatureValueModule
{
    Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value);
}

