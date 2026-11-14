using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class NomadDriveModule(string baseDirectory) : IGameModule
{
    public IReadOnlyList<string> GameExecutables { get; } = ["Nomad Drive.exe"];
    public const string TrainerVersion = "1.0.2";
    public const string SupportedAssemblyHash = "6957F9A1180D811CA2AE1BB44748157D794FED5BEFD845290FD15E41FB54FFAF";
    internal static bool SupportsAssembly(string hash) => string.Equals(hash, SupportedAssemblyHash, StringComparison.OrdinalIgnoreCase);
    internal static bool MatchesInstalledPackage(string payload, string directory) => NomadDriveInstaller.ReadPackage(payload)
        .All(file => File.Exists(SafePath(directory, file.Path))
            && string.Equals(Hash(SafePath(directory, file.Path)), file.Sha256, StringComparison.OrdinalIgnoreCase));
    private readonly string package = Path.Combine(baseDirectory, "payload", "nomad-drive");
    private const string IpcRoot = "BepInEx/HappyCheats/nomad-drive/";
    private readonly SemaphoreSlim commandLock = new(1, 1);
    public GameDefinition Definition { get; } = new("nomad-drive", "Nomad Drive", "Steam build 26391437", "2223980", "ND",
        "Survival refills and locks, poison treatment, fuel and engine repair. Solo hosted play only.", new FeatureDefinition[] {
        new("heal", "Heal damage", "Restore direct-damage health; does not revive a dead character.", "F1", false, "Survival"),
        new("refill_food", "Refill food", "Fill nutrition to its current maximum.", "F2", false, "Survival"),
        new("refill_water", "Refill water", "Fill hydration to its current maximum.", "F3", false, "Survival"),
        new("refill_energy", "Refill energy", "Fill energy to its current maximum.", "F4", false, "Survival"),
        new("cure", "Cure poisoning", "Apply medicine to stop poison buildup and remove current poison.", "F5", false, "Survival"),
        new("refuel", "Refuel nearest vehicle", "Fill the existing gasoline tank within 15 m. Add gasoline first; liquid type is preserved.", "F6", false, "Vehicle"),
        new("repair_engine", "Repair nearest engine", "Repair the installed engine on the nearest vehicle within 15 m. Does not add missing parts.", "F7", false, "Vehicle"),
        new("unlimited_food", "Unlimited food", "Keep nutrition filled while the living solo character is loaded.", "F8", true, "Survival locks"),
        new("unlimited_water", "Unlimited water", "Keep hydration filled while the living solo character is loaded.", "F9", true, "Survival locks"),
        new("unlimited_energy", "Unlimited energy", "Keep energy filled while the living solo character is loaded.", "F10", true, "Survival locks"),
        new("rapid_heal", "Rapid healing", "Refill damage health each frame. Does not prevent instant lethal damage or revive death.", "F11", true, "Survival locks") });
    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var found = FindSteam(Definition.AppId, "Nomad Drive.exe", directory);
            var compatible = SupportsAssembly(Hash(SafePath(found.Directory, "Nomad Drive_Data/Managed/Assembly-CSharp.dll")));
            var installed = NomadDriveInstaller.Installed(found.Directory);
            bool matching;
            try { matching = installed && MatchesInstalledPackage(package, found.Directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) {
                return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, "Nomad Drive.exe").Length > 0, compatible,
                    "Nomad Drive trainer package unavailable: " + ex.Message);
            }
            return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, "Nomad Drive.exe").Length > 0, compatible,
                !compatible ? "This game build needs inspection before the trainer can be installed."
                : matching ? "Trainer installed · ready to launch"
                : installed ? "Nomad Drive trainer update required. Close the game before installing."
                : "Compatible Steam build · ready to install");
        } catch (Exception ex) { return new GameInstallation(directory ?? "", "", false, false, false, false, ex.Message); }
    });
    private static void CheckClosed(string root) { if (Processes(root, "Nomad Drive.exe").Length > 0) throw new IOException("Close Nomad Drive before changing trainer files."); }
    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        if (!SupportsAssembly(Hash(SafePath(installation.Directory, "Nomad Drive_Data/Managed/Assembly-CSharp.dll")))) throw new IOException("Unsupported Nomad Drive game build.");
        NomadDriveInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
    });
    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => NomadDriveInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));
    public Task LaunchAsync(GameInstallation installation) {
        var registered = FindSteam(Definition.AppId, "Nomad Drive.exe");
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase)) throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }
    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => {
        if (!installation.IsInstalled) return TrainerStatus.Offline(installation.Message);
        try {
            var processes = Processes(installation.Directory, "Nomad Drive.exe");
            if (processes.Length != 1) return TrainerStatus.Offline();
            var path = SafePath(installation.Directory, IpcRoot + "status.json");
            if (!File.Exists(path)) return TrainerStatus.Offline(ReadStartupFailure(installation.Directory, processes[0].Started)
                ?? "Waiting for the trainer. First launch can take a moment.");
            var modified = File.GetLastWriteTimeUtc(path);
            if (DateTime.UtcNow - modified > TimeSpan.FromSeconds(6) || modified < processes[0].Started) return TrainerStatus.Offline(ReadStartupFailure(installation.Directory, processes[0].Started)
                ?? "Waiting for a fresh trainer heartbeat.");
            var json = ReadJson(path); var session = Text(json, "session_id");
            if (Text(json, "schema_version") != "1" || Text(json, "process_id") != processes[0].Id.ToString() || !Guid.TryParseExact(session, "N", out _) || Text(json, "plugin_version") != TrainerVersion || !Bool(json, "bindings_valid") || !SupportsAssembly(Text(json, "assembly_sha256"))) return TrainerStatus.Offline("Trainer session does not match this game build.");
            return new TrainerStatus(true, Bool(json, "ready"), Text(json, "message"), session, Bools(Object(json, "enabled")), Strings(Object(json, "values")), Text(json, "last_command"));
        } catch (IOException) { return TrainerStatus.Offline("Waiting for trainer status..."); }
        catch (System.Text.Json.JsonException) { return TrainerStatus.Offline("Waiting for trainer status..."); }
    });
    internal static string? ReadStartupFailure(string directory, DateTime processStartedUtc)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "preloader_*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(5)) {
            var safe = SafePath(directory, Path.GetFileName(file));
            if (File.GetLastWriteTimeUtc(safe) < processStartedUtc || new FileInfo(safe).Length > 65536) continue;
            var line = File.ReadLines(safe).FirstOrDefault(x => x.Contains("Exception", StringComparison.Ordinal));
            if (line == null) continue;
            var reason = line.Length > 700 ? line[..700] : line;
            return "Nomad Drive loader failed before the trainer started: " + reason
                + " Close the game and install the latest Nomad Drive trainer update. Startup log: " + Path.GetFileName(safe);
        }
        return null;
    }
    public async Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous game command.");
        string? commandPath = null;
        try {
            var fresh = await ReadStatusAsync(installation);
            if (!fresh.Connected) throw new InvalidOperationException(fresh.Message);
            if (!fresh.Ready || status.SessionId != fresh.SessionId) throw new InvalidOperationException("Load a game before using trainer controls.");
            if (featureId != "disable_all" && !Definition.Features.Any(f => f.Id == featureId)) throw new ArgumentException("Unknown trainer control.");
            var id = Guid.NewGuid().ToString("N");
            commandPath = SafePath(installation.Directory, IpcRoot + "commands/" + id + ".json");
            var responsePath = SafePath(installation.Directory, IpcRoot + "responses/" + id + ".json");
            WriteJson(commandPath, new { schema_version = 1, session_id = fresh.SessionId, request_id = id, action = featureId, enabled, created_utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            for (var i = 0; i < 32; i++) {
                await Task.Delay(250);
                if (File.Exists(responsePath)) {
                    var response = ReadJson(responsePath);
                    if (Text(response, "session_id") != fresh.SessionId || Text(response, "request_id") != id) throw new IOException("Mismatched trainer response.");
                    File.Delete(responsePath);
                    if (!Bool(response, "success")) throw new InvalidOperationException(Text(response, "message", "The game rejected this control."));
                    return;
                }
                var next = await ReadStatusAsync(installation);
                if (!next.Connected || next.SessionId != fresh.SessionId) throw new IOException("Game session changed. Reconnect before trying again.");
            }
            throw new TimeoutException("No acknowledgement received. This action may have applied; check the in-game value before repeating it.");
        } finally {
            try { if (commandPath != null && File.Exists(commandPath)) File.Delete(commandPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally { commandLock.Release(); }
        }
    }
}
