using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class BuilderSimulatorModule(string baseDirectory) : IGameModule, IFeatureChoiceModule
{
    public IReadOnlyList<string> GameExecutables { get; } = ["Builder Simulator.exe"];
    public const string TrainerVersion = "1.0.0";
    public const string SupportedAssemblyHash = "364320218449C22DB4A6E851276F4236CDC51DC74FCBF8EBA4AFB7FDFC1556F0";
    internal static bool SupportsAssembly(string hash) => string.Equals(hash, SupportedAssemblyHash, StringComparison.OrdinalIgnoreCase);
    internal static bool MatchesInstalledPackage(string payload, string directory) => BuilderSimulatorInstaller.ReadPackage(payload)
        .All(file => File.Exists(SafePath(directory, file.Path))
            && string.Equals(Hash(SafePath(directory, file.Path)), file.Sha256, StringComparison.OrdinalIgnoreCase));
    private readonly string package = Path.Combine(baseDirectory, "payload", "builder-simulator");
    private const string IpcRoot = "BepInEx/HappyCheats/builder-simulator/";
    private readonly SemaphoreSlim commandLock = new(1, 1);
    public GameDefinition Definition { get; } = new("builder-simulator", "Builder Simulator", "Steam build 26240179 · 2.3.1", "1120320", "BS",
        "Money, skills, materials, mixes, movement, jump height and game speed. Live gameplay validation pending. / 金钱、技能、材料、混合料、移动、跳跃和游戏速度；尚未实机验证。", new FeatureDefinition[] {
        new("unlimited_money", "Unlimited money / 无限金钱", "Show a spendable balance of at least 100,000,000 and freeze cash writes while enabled. Original balance returns on Off. / 开启时显示可用资金并锁定原余额，关闭后恢复。", "F1", true, "Resources / 资源"),
        new("max_skill_level", "Max skill level / 技能满级", "Upgrade all loaded skills to the game's maximum level 3. Permanent progression; not undone by Disable all or replayed on load. / 一次性提升至3级；不随载入重放，也不由全部关闭撤销。", "F2", false, "Progression / 成长"),
        new("materials_never_decrease", "Materials never decrease / 材料不减", "Preserve owned construction items and furniture when consumed. Buy an item first; does not unlock DLC. / 保留已拥有的建材和家具；需先购买，不解锁DLC。", "F3", true, "Resources / 资源"),
        new("mixes_never_decrease", "Mixes never decrease / 混合料不减", "Preserve existing wheelbarrow concrete, mortar or sand while using it. Fill first; deliberate emptying still works. / 使用时保留手推车内已有混合料；先装料，仍可手动清空。", "F4", true, "Resources / 资源"),
        new("super_walk_speed", "Super walk speed / 超级行走速度", "Multiply the current player's normal movement speed. / 当前玩家移动速度倍率。", "F5", true, "Movement / 移动", new FeatureChoice[] { new("2", "2×"), new("3", "3×"), new("5", "5×") }, "selected_super_walk_speed"),
        new("super_jump", "Super jump / 超级跳跃", "Multiply jump height without changing the saved controller settings. / 跳跃高度倍率，不修改持久化设置。", "F6", true, "Movement / 移动", new FeatureChoice[] { new("2", "2×"), new("3", "3×"), new("5", "5×") }, "selected_super_jump"),
        new("game_speed", "Game speed / 游戏速度", "Change simulation speed while respecting a zero-time pause. Off restores the scale still owned by this trainer. / 调整游戏速度，保留暂停；关闭时恢复本修改器控制的时间倍率。", "F7", true, "Simulation / 模拟", new FeatureChoice[] { new("0.5", "0.5×"), new("1.5", "1.5×"), new("2", "2×"), new("3", "3×") }, "selected_game_speed") });
    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var found = FindSteam(Definition.AppId, "Builder Simulator.exe", directory);
            var compatible = SupportsAssembly(Hash(SafePath(found.Directory, "Builder Simulator_Data/Managed/Assembly-CSharp.dll")));
            var installed = BuilderSimulatorInstaller.Installed(found.Directory);
            bool matching;
            try { matching = installed && MatchesInstalledPackage(package, found.Directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) {
                return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, "Builder Simulator.exe").Length > 0, compatible,
                    "Builder Simulator trainer package unavailable: " + ex.Message);
            }
            return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, "Builder Simulator.exe").Length > 0, compatible,
                !compatible ? "This game build needs inspection before the trainer can be installed."
                : matching ? "Trainer installed · ready to launch"
                : installed ? "Builder Simulator trainer update required. Close the game before installing."
                : "Compatible Steam build · ready to install");
        } catch (Exception ex) { return new GameInstallation(directory ?? "", "", false, false, false, false, ex.Message); }
    });
    private static void CheckClosed(string root) { if (Processes(root, "Builder Simulator.exe").Length > 0) throw new IOException("Close Builder Simulator before changing trainer files."); }
    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => RunSetupAsync(() => {
        if (!SupportsAssembly(Hash(SafePath(installation.Directory, "Builder Simulator_Data/Managed/Assembly-CSharp.dll")))) throw new IOException("Unsupported Builder Simulator game build.");
        BuilderSimulatorInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
        if (!MatchesInstalledPackage(package, installation.Directory)) throw new IOException("Builder Simulator trainer installation did not verify. Close the game and retry Install / update.");
    });
    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => RunSetupAsync(() => BuilderSimulatorInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));
    private async Task RunSetupAsync(Action action) {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("A Builder Simulator operation is already running. Wait for it to finish and retry.");
        try { await Task.Run(action); } finally { commandLock.Release(); }
    }
    public Task LaunchAsync(GameInstallation installation) {
        var registered = FindSteam(Definition.AppId, "Builder Simulator.exe");
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase)) throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }
    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => {
        if (!installation.IsInstalled) return TrainerStatus.Offline(installation.Message);
        try {
            var processes = Processes(installation.Directory, "Builder Simulator.exe");
            if (processes.Length != 1) return TrainerStatus.Offline();
            var path = SafePath(installation.Directory, IpcRoot + "status.json");
            if (!File.Exists(path)) return TrainerStatus.Offline("Waiting for the trainer. First launch can take a moment.");
            var modified = File.GetLastWriteTimeUtc(path);
            if (DateTime.UtcNow - modified > TimeSpan.FromSeconds(6) || modified < processes[0].Started) return TrainerStatus.Offline("Waiting for a fresh trainer heartbeat.");
            var json = ReadJson(path); var session = Text(json, "session_id");
            if (Text(json, "schema_version") != "1" || Text(json, "process_id") != processes[0].Id.ToString() || !Guid.TryParseExact(session, "N", out _) || Text(json, "plugin_version") != TrainerVersion || !Bool(json, "bindings_valid") || !SupportsAssembly(Text(json, "assembly_sha256"))) return TrainerStatus.Offline("Trainer session does not match this game build.");
            return new TrainerStatus(true, Bool(json, "ready"), Text(json, "message"), session, Bools(Object(json, "enabled")), Strings(Object(json, "values")), Text(json, "last_command"));
        } catch (IOException) { return TrainerStatus.Offline("Waiting for trainer status..."); }
        catch (System.Text.Json.JsonException) { return TrainerStatus.Offline("Waiting for trainer status..."); }
    });
    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
    {
        var feature = Definition.Features.SingleOrDefault(f => f.Id == featureId);
        if (featureId != "disable_all" && feature == null) throw new ArgumentException("Unknown trainer control.");
        if (feature?.IsToggle == true && !enabled.HasValue) throw new ArgumentException("Choose On or Off for this control.");
        if (feature?.IsToggle != true && enabled.HasValue) throw new ArgumentException("This action is not a toggle.");
        return SendCommandAsync(installation, status, featureId, enabled, null);
    }
    public Task SelectAsync(GameInstallation installation, TrainerStatus status, string featureId, string choice)
    {
        var feature = Definition.Features.SingleOrDefault(f => f.Id == featureId);
        if (feature?.Choices?.Any(item => item.Value == choice) != true) throw new ArgumentException("Unsupported multiplier for this control.");
        return SendCommandAsync(installation, status, featureId, null, choice);
    }
    private async Task SendCommandAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled, string? choice)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous game command.");
        string? commandPath = null;
        try {
            var fresh = await ReadStatusAsync(installation);
            var cleanup = featureId == "disable_all" || (choice == null && enabled == false);
            if (!fresh.Connected || (!fresh.Ready && !cleanup) || status.SessionId != fresh.SessionId)
                throw new InvalidOperationException("Load a construction site before using trainer controls. Close the game and use Install / update if the trainer is not connected.");
            if (!SupportsAssembly(Hash(SafePath(installation.Directory, "Builder Simulator_Data/Managed/Assembly-CSharp.dll"))) || !MatchesInstalledPackage(package, installation.Directory))
                throw new IOException("The game or trainer files changed. Close Builder Simulator and use Install / update before retrying.");
            if (featureId != "disable_all" && !Definition.Features.Any(f => f.Id == featureId)) throw new ArgumentException("Unknown trainer control.");
            var id = Guid.NewGuid().ToString("N");
            commandPath = SafePath(installation.Directory, IpcRoot + "commands/" + id + ".json");
            var responsePath = SafePath(installation.Directory, IpcRoot + "responses/" + id + ".json");
            WriteJson(commandPath, new { schema_version = 1, session_id = fresh.SessionId, request_id = id, action = featureId, enabled, choice, created_utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            for (var i = 0; i < 32; i++) {
                await Task.Delay(250);
                if (File.Exists(responsePath)) {
                    var response = ReadJson(responsePath);
                    if (Text(response, "schema_version") != "1" || Text(response, "session_id") != fresh.SessionId || Text(response, "request_id") != id) throw new IOException("Mismatched trainer response.");
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
