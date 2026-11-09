namespace HappyCheats.Core;

// Synthetic new game absent from the client's compiled catalog.
public sealed class FutureGameModule(string directory) : IGameModule, IFeatureChoiceModule, IFeatureValueModule
{
    public IReadOnlyList<string> GameExecutables => ["FutureGame.exe"];
    public GameDefinition Definition => new("future-game", "Future Game", "Synthetic fixture", "1", "FG", "Downloaded module fixture.", [
        new("future_health", "Health", "Synthetic toggle control.", "F1", true),
        new("future_amount", "Amount", "Synthetic numeric control.", "", Number: new(0, 1000, 10)),
        new("future_speed", "Speed", "Synthetic choice control.", "", Choices: [new("1", "1×"), new("2", "2×")], ChoiceValueKey: "speed")]);
    private readonly Dictionary<string, bool> enabled = new();
    private readonly Dictionary<string, string> values = new();
    public Task<GameInstallation> DiscoverAsync(string? path = null)
    {
        var discovered = Path.GetFullPath(Path.Combine(directory, "../../../../..", "game"));
        var root = path ?? (Directory.Exists(discovered) ? discovered : directory);
        return Task.FromResult(new GameInstallation(root, "synthetic", !File.Exists(Path.Combine(root, "simulate-missing")),
            File.Exists(Path.Combine(root, "installed-trainer.txt")), File.Exists(Path.Combine(root, "simulate-running")), true, "Synthetic fixture"));
    }
    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null)
    {
        if (File.Exists(Path.Combine(installation.Directory, "simulate-install-error"))) throw new IOException("Synthetic installation failed; retry Update.");
        File.Copy(Path.Combine(directory, "payload/future-game/fixture.txt"), Path.Combine(installation.Directory, "installed-trainer.txt"), true);
        progress?.Report("Synthetic installer copied trainer files.");
        return Task.CompletedTask;
    }
    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.CompletedTask;
    public Task LaunchAsync(GameInstallation installation) => Task.CompletedTask;
    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.FromResult(new TrainerStatus(true, true, "Synthetic", "future-session", enabled, values));
    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? value = null) { enabled[featureId] = value ?? true; return Task.CompletedTask; }
    public Task SelectAsync(GameInstallation installation, TrainerStatus status, string featureId, string choice) { values["speed"] = choice; return Task.CompletedTask; }
    public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value) { values[featureId] = value.ToString(System.Globalization.CultureInfo.InvariantCulture); return Task.CompletedTask; }
}
