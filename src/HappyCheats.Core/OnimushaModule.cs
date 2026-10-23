using static HappyCheats.Core.ModuleIO;
using Backend = OnimushaTrainer.Backend;
using System.Text.Json;

namespace HappyCheats.Core;

public sealed class OnimushaModule(string baseDirectory) : IGameModule
{
    public IReadOnlyList<string> GameExecutables { get; } = ["OnimushaWotS.exe"];
    private readonly string package = Path.Combine(baseDirectory, "payload", "onimusha");
    private readonly SemaphoreSlim commandLock = new(1, 1);
    public GameDefinition Definition { get; } = new("onimusha", "Onimusha", "WAY OF THE SWORD", "2638890", "鬼", "Master the blade. Shape every encounter with your own rules.", new FeatureDefinition[] {
        new("unlimited_health", "Unlimited health", "Keep your living character’s health full.", "F1", true, "Combat"),
        new("auto_parry", "Auto-parry", "Parry eligible incoming attacks. Experimental.", "F2", true, "Combat"),
        new("one_hit_stun", "One-hit stun", "Break an enemy’s red bar with a qualifying hit.", "F3", true, "Combat"),
        new("stun_immunity", "Stun immunity", "Keep your own red bar full.", "F4", true, "Combat") });
    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var game = directory == null ? Backend.DiscoverGame() : Backend.InspectGame(directory);
            var compatible = game.BuildId == "24769601";
            OnimushaTrainer.VerificationResult verified;
            string packageVersion;
            try { verified = Backend.VerifyInstalled(package, game.Path); packageVersion = Text(ReadJson(SafePath(package, "package.json")), "Version"); }
            catch (Exception ex) { return new GameInstallation(game.Path, game.BuildId ?? "Unknown", true, false, Backend.GetRunningGame(game.Path).Length > 0, compatible, "Trainer package unavailable: " + ex.Message); }
            return new GameInstallation(game.Path, game.BuildId ?? "Unknown", true, verified.Installed, Backend.GetRunningGame(game.Path).Length > 0, compatible,
                !compatible ? "This Steam build needs a compatibility check." : verified.MatchesPackage ? "Trainer installed · module " + packageVersion : verified.Installed ? "Trainer update required · installed files differ from module " + packageVersion : "Ready to install trainer.");
        } catch (Exception ex) { return new GameInstallation(directory ?? "", "", false, false, false, false, ex.Message); }
    });
    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        var current = Backend.InspectGame(installation.Directory);
        if (current.BuildId != "24769601") throw new InvalidOperationException("Unsupported Onimusha Steam build.");
        progress?.Report("Verifying and installing Onimusha trainer...");
        var result = Backend.Install(package, installation.Directory);
        progress?.Report(result.Message);
    });
    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => { var result = Backend.Uninstall(installation.Directory); progress?.Report(result.Message); });
    public Task LaunchAsync(GameInstallation installation) {
        var registered = Backend.DiscoverGame();
        if (!string.Equals(Path.GetFullPath(registered.Path), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase)) throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        Backend.PrepareLaunch(installation.Directory);
        return LaunchSteam(Definition.AppId);
    }
    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadStatus(installation));
    private static TrainerStatus ReadStatus(GameInstallation installation)
    {
        if (!installation.IsInstalled) return TrainerStatus.Offline(installation.Message);
        try {
            var processes = Processes(installation.Directory, "OnimushaWotS.exe");
            if (processes.Length != 1) return TrainerStatus.Offline();
            var path = SafePath(installation.Directory, @"reframework\data\onimusha_trainer_status.json");
            if (!File.Exists(path)) return TrainerStatus.Offline("Waiting for the trainer. Restart the game after installation.");
            var modified = File.GetLastWriteTimeUtc(path);
            if (DateTime.UtcNow - modified > TimeSpan.FromSeconds(10) || modified < processes[0].Started) return TrainerStatus.Offline("Waiting for a fresh trainer heartbeat.");
            return ParseStatus(ReadJson(path));
        } catch (IOException) { return TrainerStatus.Offline("Waiting for trainer status..."); }
        catch (JsonException) { return TrainerStatus.Offline("Waiting for trainer status..."); }
    }
    internal static TrainerStatus ParseStatus(JsonElement json)
    {
        var session = Text(json, "session_id");
        var version = Text(json, "version");
        if (Text(json, "schema_version") != "1" || version != "0.4.0" || session.Length == 0)
            return TrainerStatus.Offline("Restart the game to load the current trainer module.");
        var player = Object(json, "player");
        var ready = Bool(player, "ready");
        var values = new Dictionary<string, string>();
        if (ready) {
            values["Health"] = Text(player, "health") + " / " + Text(player, "maximum_health");
            var gauge = Object(player, "rikido");
            values["Red bar"] = Text(gauge, "current") + " / " + Text(gauge, "maximum");
        }
        return new TrainerStatus(true, ready, ready ? "Trainer connected. Changes apply in game." : "Connected · load into gameplay to use trainer controls.", session, Bools(Object(json, "enabled")), values, Text(json, "last_command"));
    }
    internal Dictionary<string, object> CreateCommand(JsonElement state, string expectedSession, string featureId, bool? enabled, string request)
    {
        var current = ParseStatus(state);
        if (!current.Connected || !current.Ready || current.SessionId != expectedSession)
            throw new InvalidOperationException("Load gameplay before using this control.");
        if (featureId is not ("disable_all" or "hud") && !Definition.Features.Any(f => f.Id == featureId))
            throw new ArgumentException("Unknown trainer control.");
        if (featureId is not ("disable_all" or "hud")) {
            var binding = Object(Object(state, "features"), featureId);
            if (!Bool(binding, "installed") || Text(binding, "fault").Length > 0)
                throw new InvalidOperationException("This control is unavailable in the current game session.");
        }
        var command = new Dictionary<string, object> { ["session_id"] = current.SessionId, ["request_id"] = request };
        if (featureId == "disable_all") command["disable_all"] = true;
        else if (featureId == "hud") command["hud_visible"] = enabled ?? !Bool(Object(state, "hud"), "visible");
        else command["enabled"] = new Dictionary<string, bool> { [featureId] = enabled ?? false };
        return command;
    }
    internal static bool AcceptAcknowledgement(TrainerStatus next, string session, string request)
    {
        // A transient status publication gap cannot confirm or resend an action.
        if (!next.Connected) return false;
        if (next.SessionId != session) throw new IOException("Game session changed.");
        return next.LastCommand == request;
    }
    public async Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous game command.");
        string? path = null;
        try {
            var fresh = await ReadStatusAsync(installation);
            if (!fresh.Connected || !fresh.Ready || fresh.SessionId != status.SessionId) throw new InvalidOperationException("Load gameplay before using this control.");
            var state = ReadJson(SafePath(installation.Directory, @"reframework\data\onimusha_trainer_status.json"));
            var request = Guid.NewGuid().ToString("N");
            var command = CreateCommand(state, fresh.SessionId, featureId, enabled, request);
            path = SafePath(installation.Directory, @"reframework\data\onimusha_trainer_command.json"); WriteJson(path, command);
            for (int i = 0; i < 32; i++) {
                await Task.Delay(250);
                if (AcceptAcknowledgement(ReadStatus(installation), fresh.SessionId, request)) return;
            }
            throw new TimeoutException("The game did not acknowledge this change. Resume gameplay and try again.");
        } finally {
            // Remove an unconsumed mailbox command so a timeout cannot apply later.
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally { commandLock.Release(); }
        }
    }
}
