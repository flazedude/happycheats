using System.Text.Json;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class ProjectHospitalModule(string baseDirectory) : IGameModule
{
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string Executable = "ProjectHospital.exe";
    public const string GameAssembly = "ProjectHospital_Data/Managed/Assembly-CSharp.dll";
    public const string SupportedAssemblyHash = "1120D899FB74D16F31115C5C27325EDBD1154B68E9419E6B7EA03CC3D43B6F24";
    public const string SupportedSteamBuild = "21161440";
    public const string TrainerVersion = "0.1.0";
    public const string IpcRoot = "BepInEx/HappyCheats/project-hospital/";
    private readonly string package = Path.Combine(baseDirectory, "payload", "project-hospital");
    private readonly SemaphoreSlim commandLock = new(1, 1);

    public GameDefinition Definition { get; } = new("project-hospital", "Project Hospital", "HOSPITAL MANAGEMENT", "868360", "PH",
        "Manage hospital funds, time, the staff hiring limit, campaigns and prestige. Experimental Steam module; gameplay validation is pending.", new FeatureDefinition[] {
            new("max_money", "Max Money", "Raise hospital funds to at least 999,999,999 once. The game may save the resulting balance.", "F1", false, "Hospital"),
            new("freeze_time", "Freeze Time of Day", "Hold the time of day while hospital activity continues. Turn off to resume the clock.", "F2", true, "Time"),
            new("max_staff", "Max Staff", "Raise the staff hiring limit to 1,000 while enabled. Existing staff remain after disabling; normal hiring limits return.", "F3", true, "Hospital"),
            new("unlock_campaigns", "Unlock All Campaigns", "Make installed campaigns selectable while enabled, without changing campaign completion. DLC ownership requirements still apply.", "F4", true, "Progression"),
            new("max_prestige", "Max Prestige", "Keep hospital and department prestige at the game's maximum while enabled. Turn off to restore naturally calculated prestige.", "F5", true, "Hospital") });

    private static bool Compatible(string directory) => string.Equals(Hash(SafePath(directory, GameAssembly)), SupportedAssemblyHash, StringComparison.OrdinalIgnoreCase);

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var found = FindSteam(Definition.AppId, Executable, directory);
            var compatible = Compatible(found.Directory);
            var installed = ProjectHospitalInstaller.Installed(found.Directory);
            return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, Executable).Length > 0, compatible,
                !compatible ? "This Project Hospital build needs inspection before the trainer can be installed."
                : installed ? "Trainer installed · ready to launch" : "Compatible Steam build · ready to install");
        } catch (Exception ex) { return new GameInstallation(directory ?? "", "", false, false, false, false, ex.Message); }
    });

    private static void CheckClosed(string root)
    {
        if (Processes(root, Executable).Length > 0) throw new IOException("Close Project Hospital before changing trainer files.");
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        if (!Compatible(installation.Directory)) throw new IOException("Unsupported Project Hospital game build.");
        ProjectHospitalInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
    });
    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() =>
        ProjectHospitalInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));

    public Task LaunchAsync(GameInstallation installation)
    {
        var registered = FindSteam(Definition.AppId, Executable);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => {
        if (!installation.IsInstalled) return TrainerStatus.Offline(installation.Message);
        try {
            var processes = Processes(installation.Directory, Executable);
            if (processes.Length != 1) return TrainerStatus.Offline();
            var path = SafePath(installation.Directory, IpcRoot + "status.json");
            if (!File.Exists(path)) return TrainerStatus.Offline("Waiting for the trainer. First launch can take a moment.");
            var modified = File.GetLastWriteTimeUtc(path);
            if (!IsFreshHeartbeat(modified, processes[0].Started, DateTime.UtcNow)) return TrainerStatus.Offline("Waiting for a fresh trainer heartbeat.");
            return ParseStatus(ReadJson(path), processes[0].Id);
        } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
            return TrainerStatus.Offline("Waiting for trainer status...");
        }
    });

    internal static bool IsFreshHeartbeat(DateTime modified, DateTime processStarted, DateTime now) =>
        modified >= processStarted && modified <= now + TimeSpan.FromSeconds(1) && now - modified <= TimeSpan.FromSeconds(6);

    internal static TrainerStatus ParseStatus(JsonElement json, int processId)
    {
        var session = Text(json, "session_id");
        if (Text(json, "schema_version") != "1" || Text(json, "version") != TrainerVersion ||
            Text(json, "process_id") != processId.ToString(System.Globalization.CultureInfo.InvariantCulture) || session.Length == 0 ||
            !string.Equals(Text(json, "assembly_sha256"), SupportedAssemblyHash, StringComparison.OrdinalIgnoreCase))
            return TrainerStatus.Offline("Trainer session does not match this game build.");
        return new TrainerStatus(true, Bool(json, "ready"), Text(json, "message"), session,
            Bools(Object(json, "enabled")), Strings(Object(json, "values")), Text(json, "last_command"));
    }

    internal static object CreateCommand(TrainerStatus requested, TrainerStatus fresh, string requestId, string featureId, bool? enabled, long createdUtc)
    {
        if (!fresh.Connected || string.IsNullOrEmpty(fresh.SessionId) || requested.SessionId != fresh.SessionId)
            throw new InvalidOperationException("Game session changed. Reconnect before trying again.");
        if (featureId is not ("max_money" or "freeze_time" or "max_staff" or "unlock_campaigns" or "max_prestige" or "disable_all"))
            throw new ArgumentException("Unknown trainer control.");
        var toggle = featureId is "freeze_time" or "max_staff" or "unlock_campaigns" or "max_prestige";
        if (toggle && enabled is null) throw new ArgumentException("Choose whether to enable or disable this control.");
        if (!toggle && enabled is not null) throw new ArgumentException("This action does not accept a toggle state.");
        if (!fresh.Ready && featureId != "disable_all" && !(toggle && enabled == false))
            throw new InvalidOperationException("Load a hospital before using trainer controls.");
        if (featureId != "disable_all" && !(toggle && enabled == false) &&
            (!fresh.Values.TryGetValue("available_" + featureId, out var available) || !bool.TryParse(available, out var allowed) || !allowed))
            throw new InvalidOperationException(fresh.Values.GetValueOrDefault("reason_" + featureId, "Unavailable in this scene"));
        return new { schema_version = 1, session_id = fresh.SessionId, request_id = requestId, action = featureId, enabled, created_utc = createdUtc };
    }

    internal static void AcceptResponse(JsonElement response, string sessionId, string requestId)
    {
        if (Text(response, "schema_version") != "1" || Text(response, "session_id") != sessionId || Text(response, "request_id") != requestId)
            throw new IOException("Mismatched trainer response.");
        if (!Bool(response, "success")) throw new InvalidOperationException(Text(response, "message", "The game rejected this control."));
    }

    public async Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous game command.");
        string? commandPath = null;
        try {
            var fresh = await ReadStatusAsync(installation);
            var id = Guid.NewGuid().ToString("N");
            var command = CreateCommand(status, fresh, id, featureId, enabled, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            commandPath = SafePath(installation.Directory, IpcRoot + "commands/" + id + ".json");
            var responsePath = SafePath(installation.Directory, IpcRoot + "responses/" + id + ".json");
            WriteJson(commandPath, command);
            for (var i = 0; i < 32; i++) {
                await Task.Delay(250);
                if (File.Exists(responsePath)) {
                    var response = ReadJson(responsePath);
                    AcceptResponse(response, fresh.SessionId, id);
                    File.Delete(responsePath);
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
