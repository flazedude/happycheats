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
}

public interface IFeatureChoiceModule
{
    Task SelectAsync(GameInstallation installation, TrainerStatus status, string featureId, string choice);
}

public interface IFeatureValueModule
{
    Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value);
}

public static class ModuleCatalog
{
    public static IReadOnlyList<IGameModule> Create(string? baseDirectory = null) => new IGameModule[]
    {
        new ChineseParentsModule(baseDirectory ?? AppContext.BaseDirectory),
        new ProjectHospitalModule(baseDirectory ?? AppContext.BaseDirectory),
        new HotelArchitectModule(baseDirectory ?? AppContext.BaseDirectory),
        new CivilizationVIModule(baseDirectory ?? AppContext.BaseDirectory),
        new CivilizationVIIModule(baseDirectory ?? AppContext.BaseDirectory),
        new EldenRingModule(baseDirectory ?? AppContext.BaseDirectory),
        new OnimushaModule(baseDirectory ?? AppContext.BaseDirectory),
        new LaundryModule(baseDirectory ?? AppContext.BaseDirectory),
        new AstroneerModule(baseDirectory ?? AppContext.BaseDirectory),
        new StardewModule(baseDirectory ?? AppContext.BaseDirectory),
        new DawnwalkerModule(baseDirectory ?? AppContext.BaseDirectory),
        new CrimsonDesertModule(baseDirectory ?? AppContext.BaseDirectory),
        new WukongModule(baseDirectory ?? AppContext.BaseDirectory),
        new PhantomBladeModule(baseDirectory ?? AppContext.BaseDirectory),
        new Rdr2Module(baseDirectory ?? AppContext.BaseDirectory),
        new GtaVModule(baseDirectory ?? AppContext.BaseDirectory),
        new Witcher3Module(baseDirectory ?? AppContext.BaseDirectory)
    };
}
