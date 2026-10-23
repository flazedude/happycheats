using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class ChineseParentsModule(string baseDirectory) : IGameModule
{
    public IReadOnlyList<string> GameExecutables { get; } = ["game.exe"];
    public const string SupportedAssemblyHash = "0262F1B24B148D99E8CBA54A79B6B7299622C46BE4BBC000A500D7519BF3BF3A";
    private readonly string package = Path.Combine(baseDirectory, "payload", "chinese-parents");
    private const string IpcRoot = "BepInEx/HappyCheats/chinese-parents/";
    private readonly SemaphoreSlim commandLock = new(1, 1);
    public GameDefinition Definition { get; } = new("chinese-parents", "Chinese Parents", "中国式家长", "736190", "学", "A little more freedom for your next generation. Make room to grow, explore, and enjoy the journey.", new FeatureDefinition[] {
        new("add_money", "Add money", "Add 1,000 to your current pocket money.", "F1", false, "Resources"),
        new("refill_energy", "Action points", "Restore brain-map energy to its current maximum.", "F2", false, "Resources"),
        new("add_knowledge", "Learning points", "Add 1,000 learning points for studying skills, within the game’s cap.", "F3", false, "Resources"),
        new("unlimited_energy", "Unlimited action points", "Keep brain-map energy filled while enabled.", "F8", true, "Resources"),
        new("clear_stress", "Clear stress", "Bring your current stress back to zero.", "F4", false, "Wellbeing"),
        new("no_stress", "Stress-free", "Keep stress at zero while enabled.", "F9", true, "Wellbeing"),
        new("clear_shadow", "Clear psychological shadow", "Reset your current psychological shadow.", "F5", false, "Wellbeing"),
        new("add_attributes", "All-round growth", "Add 100 IQ, EQ, imagination, memory, stamina and charm.", "F6", false, "Growth"),
        new("add_face", "Family face", "Add 100 face to the current generation.", "F7", false, "Growth") });
    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var found = FindSteam(Definition.AppId, "game.exe", directory);
            var compatible = Hash(SafePath(found.Directory, "game_Data/Managed/Assembly-CSharp.dll")) == SupportedAssemblyHash;
            var installed = OwnedInstaller.Installed(found.Directory);
            return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, "game.exe").Length > 0, compatible,
                !compatible ? "This game build needs inspection before the trainer can be installed." : installed ? "Trainer installed · ready to launch" : "Compatible Steam build · ready to install");
        } catch (Exception ex) { return new GameInstallation(directory ?? "", "", false, false, false, false, ex.Message); }
    });
    private static void CheckClosed(string root) { if (Processes(root, "game.exe").Length > 0) throw new IOException("Close Chinese Parents before changing trainer files."); }
    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        if (Hash(SafePath(installation.Directory, "game_Data/Managed/Assembly-CSharp.dll")) != SupportedAssemblyHash) throw new IOException("Unsupported Chinese Parents game build.");
        OwnedInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
    });
    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => OwnedInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));
    public Task LaunchAsync(GameInstallation installation) {
        var registered = FindSteam(Definition.AppId, "game.exe");
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase)) throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }
    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => {
        if (!installation.IsInstalled) return TrainerStatus.Offline(installation.Message);
        try {
            var processes = Processes(installation.Directory, "game.exe");
            if (processes.Length != 1) return TrainerStatus.Offline();
            var path = SafePath(installation.Directory, IpcRoot + "status.json");
            if (!File.Exists(path)) return TrainerStatus.Offline("Waiting for the trainer. First launch can take a moment.");
            var modified = File.GetLastWriteTimeUtc(path);
            if (DateTime.UtcNow - modified > TimeSpan.FromSeconds(6) || modified < processes[0].Started) return TrainerStatus.Offline("Waiting for a fresh trainer heartbeat.");
            var json = ReadJson(path); var session = Text(json, "session_id");
            if (Text(json, "schema_version") != "1" || Text(json, "process_id") != processes[0].Id.ToString() || session.Length == 0 || !string.Equals(Text(json, "assembly_sha256"), SupportedAssemblyHash, StringComparison.OrdinalIgnoreCase)) return TrainerStatus.Offline("Trainer session does not match this game build.");
            return new TrainerStatus(true, Bool(json, "ready"), Text(json, "message"), session, Bools(Object(json, "enabled")), Strings(Object(json, "values")), Text(json, "last_command"));
        } catch (IOException) { return TrainerStatus.Offline("Waiting for trainer status..."); }
        catch (System.Text.Json.JsonException) { return TrainerStatus.Offline("Waiting for trainer status..."); }
    });
    public async Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous game command.");
        string? commandPath = null;
        try {
            var fresh = await ReadStatusAsync(installation);
            if (!fresh.Ready || !fresh.Connected || status.SessionId != fresh.SessionId) throw new InvalidOperationException("Load a game before using trainer controls.");
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
