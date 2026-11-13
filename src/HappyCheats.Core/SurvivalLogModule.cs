using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class SurvivalLogModule : IGameModule, IFeatureValueModule
{
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string Executable = "SurvivalLog.exe";
    public const string SupportedAssemblyHash = "CADD20FF7E2ADC2348BABA81683E4ADA2A7ADD33C6CDC502DF8759ED3F158024";
    public const string MetadataPath = "SurvivalLog_Data/il2cpp_data/Metadata/global-metadata.dat";
    public const string SupportedMetadataHash = "33C65797086A9457A9707998DD55CD70E976DEE4C03CCDA19A1DA711DC613975";
    public const string TrainerVersion = "0.1.2";
    public const string BridgeRoot = SurvivalLogInstaller.TrainerRoot;
    private readonly string package;
    private readonly SemaphoreSlim commandLock = new(1, 1);
    private readonly object stateLock = new();
    private (string Path, long Length, DateTime Modified, long MetaLength, DateTime MetaModified, bool Compatible)? inspected;
    private string authorizedSession = "";
    private DateTime leaseWritten;
    private string? leaseDirectory;

    // Test seams never alter normal discovery, process identity checks or injection.
    internal Func<string, (int Id, DateTime Started)[]> ProcessProvider { get; set; } = directory => Processes(directory, Executable);
    internal Func<string, string> HashProvider { get; set; } = Hash;
    internal Action<string> AttachTrainer { get; set; } = SurvivalLogInjector.Attach;

    internal static readonly string[] ToggleIds = { "sl_satiety", "sl_morale", "sl_stamina", "sl_health", "sl_vitality", "sl_freeze_clock", "sl_carry" };
    internal static readonly string[] FeatureIds = ToggleIds.Concat(new[] { "sl_money", "sl_survival_points", "sl_xp" }).ToArray();
    public const string SupportedSteamBuild = "26615062";
    public SurvivalLogModule(string baseDirectory) => package = Path.Combine(baseDirectory, "payload", "survival-log");
    public GameDefinition Definition { get; } = new("survival-log", "Survival Log / 生存日志", "SURVIVAL MANAGEMENT", "4164790", "SL",
        "Experimental trainer for Steam build 26615062. Install, launch normally, then Connect trainer. Offline checks only; gameplay validation pending. / 实验性修改器，尚未经实际游戏验证。",
        new FeatureDefinition[] {
            new("sl_connect", "Connect trainer / 连接修改器", "Connect explicitly to the running supported game. / 手动连接已运行的受支持游戏。", "F8", false, "Connection / 连接"),
            new("sl_satiety", "Unlimited satiety / 无限饱食度", "Keep player satiety at its maximum on the next stat update. / 下次属性更新时保持玩家饱食度满值。", "F1", true, "Survival / 生存"),
            new("sl_morale", "Unlimited morale / 无限士气", "Keep player morale at its maximum on the next stat update. / 下次属性更新时保持玩家士气满值。", "F2", true, "Survival / 生存"),
            new("sl_stamina", "Unlimited stamina / 无限体力", "Keep player stamina at its maximum on the next stat update. / 下次属性更新时保持玩家体力满值。", "F3", true, "Survival / 生存"),
            new("sl_health", "Unlimited health / 无限健康", "Keep the player's health attribute at maximum on its next update. Scripted death is unaffected. / 下次属性更新时保持健康满值，不影响剧情死亡。", "F4", true, "Survival / 生存"),
            new("sl_vitality", "Unlimited life / 无限生命", "Keep player vitality at maximum on its next update. Scripted death is unaffected. / 下次属性更新时保持生命满值，不影响剧情死亡。", "F5", true, "Survival / 生存"),
            new("sl_freeze_clock", "Freeze clock / 冻结时钟", "Hold the native game clock; timed activities may also stop. Off restores the prior clock setting while still owned. / 冻结游戏时钟，计时活动可能同时暂停；关闭后恢复原设置。", "F6", true, "Time / 时间"),
            new("sl_carry", "High carrying capacity / 超高负重", "Use a player-only 1,000,000 carrying limit while enabled. Bag slots stay unchanged. / 开启后玩家负重上限为1,000,000，不改变背包格数。", "F7", true, "Inventory / 背包"),
            new("sl_money", "Set money / 设置金钱", "Set the current player's gold once through the game's own GM method. The game may save it. / 通过游戏原生方法设置当前金钱，可能被游戏存档。", "", false, "Resources / 资源", ChoiceValueKey: "sl_money", Number: new FeatureNumber(0, 100_000_000, 100_000)),
            new("sl_survival_points", "Add survival points / 增加生存点数", "Grant survival points once. This is not replayed after loading; the game may save the result. / 一次性增加生存点数，加载后不重复发放，可能被游戏存档。", "", false, "Resources / 资源", ChoiceValueKey: "sl_survival_points", Number: new FeatureNumber(1, 1_000_000, 1_000)),
            new("sl_xp", "Skill XP multiplier / 技能经验倍率", "Multiply newly earned base/discovery skill XP from 1× to 100×. Resets to 1× after loading or disconnection. / 新获得的基础与发现技能经验倍率1至100倍，加载或断连后恢复1倍。", "", false, "Progression / 成长", ChoiceValueKey: "sl_xp", Number: new FeatureNumber(1, 100, 10, Unit: "×"))
        });

    internal bool Compatible(string directory, bool force = false)
    {
        var path = SafePath(directory, "GameAssembly.dll");
        var file = new FileInfo(path);
        var metadata = new FileInfo(SafePath(directory, MetadataPath));
        if (!file.Exists || !metadata.Exists) return false;
        lock (stateLock)
        {
            if (!force && inspected is { } prior && prior.Path == path && prior.Length == file.Length && prior.Modified == file.LastWriteTimeUtc && prior.MetaLength == metadata.Length && prior.MetaModified == metadata.LastWriteTimeUtc)
                return prior.Compatible;
            var compatible = string.Equals(HashProvider(path), SupportedAssemblyHash, StringComparison.OrdinalIgnoreCase)
                && string.Equals(HashProvider(metadata.FullName), SupportedMetadataHash, StringComparison.OrdinalIgnoreCase);
            inspected = (path, file.Length, file.LastWriteTimeUtc, metadata.Length, metadata.LastWriteTimeUtc, compatible);
            return compatible;
        }
    }

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try
        {
            var found = FindSteam(Definition.AppId, Executable, directory);
            var compatible = Compatible(found.Directory);
            var running = ProcessProvider(found.Directory).Length > 0;
            var installed = SurvivalLogInstaller.Installed(found.Directory);
            bool matching;
            try { matching = installed && SurvivalLogInstaller.MatchesPackage(package, found.Directory); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                return new GameInstallation(found.Directory, found.Build, true, installed, running, compatible,
                    "Survival Log trainer package unavailable: " + error.Message);
            }
            return new GameInstallation(found.Directory, found.Build, true, installed, running, compatible,
                !compatible ? "This Survival Log native assembly or metadata needs a compatibility check. Supported Steam build: 26615062."
                : matching ? "Experimental trainer installed. Start the game, then choose Connect trainer."
                : installed ? "Survival Log trainer update required. Close the game before installing."
                : "Ready to install the experimental Survival Log trainer.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message); }
    });

    private void CheckClosed(string directory)
    {
        if (ProcessProvider(directory).Length > 0) throw new IOException("Close Survival Log before changing trainer files.");
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        if (!Compatible(installation.Directory, force: true)) throw new IOException("Unsupported Survival Log native assembly or metadata. Trainer installation was refused.");
        SurvivalLogInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
    });

    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        SurvivalLogInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress);
        lock (stateLock) { authorizedSession = ""; leaseWritten = default; }
    });

    public Task LaunchAsync(GameInstallation installation)
    {
        var registered = FindSteam(Definition.AppId, Executable);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }

    public void Deactivate()
    {
        lock (stateLock)
        {
            if (leaseDirectory != null && authorizedSession.Length > 0)
            {
                var path = SafePath(leaseDirectory, BridgeRoot + "lease.txt");
                if (File.Exists(path) && File.ReadAllText(path) == authorizedSession) File.Delete(path);
            }
            authorizedSession = ""; leaseWritten = default; leaseDirectory = null;
        }
    }

    internal sealed record Snapshot(TrainerStatus Status, string ResultRequest, bool ResultSuccess, string ResultMessage);

    internal static TrainerStatus AwaitingConnection(int processId, DateTime started, bool installed)
    {
        var values = FeatureIds.ToDictionary(id => "available_" + id, _ => bool.FalseString);
        values["available_sl_connect"] = installed.ToString();
        values["sl_limitations"] = "Experimental bindings; gameplay validation is pending.";
        return new TrainerStatus(true, true, installed ? "Game found. Choose Connect trainer." : "Close the game and install the Survival Log trainer first.",
            $"process{processId}-{started.Ticks}", new Dictionary<string, bool>(), values);
    }

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadStatus(installation));

    private TrainerStatus ReadStatus(GameInstallation installation)
    {
        if (!installation.IsInstalled || !installation.Compatible) return TrainerStatus.Offline(installation.Message);
        try
        {
            var processes = ProcessProvider(installation.Directory);
            if (processes.Length != 1) return TrainerStatus.Offline("Start Survival Log through Steam, then choose Connect trainer.");
            var process = processes[0];
            var native = ReadSnapshotFile(SafePath(installation.Directory, BridgeRoot + "status.json"), process.Started, process.Id);
            lock (stateLock)
            {
                if (!native.Status.Connected || native.Status.SessionId != authorizedSession)
                    return AwaitingConnection(process.Id, process.Started, installation.TrainerInstalled);
                WriteLease(installation.Directory, native.Status.SessionId);
            }
            return native.Status;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return TrainerStatus.Offline("Waiting for Survival Log: " + error.Message); }
    }

    // The caller holds stateLock. A lease is issued only for a session authorized by Connect.
    private void WriteLease(string directory, string session, bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - leaseWritten < TimeSpan.FromSeconds(1)) return;
        var path = SafePath(directory, BridgeRoot + "lease.txt");
        var temporary = SafePath(directory, BridgeRoot + "lease." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, session, new UTF8Encoding(false));
            File.Move(temporary, path, true);
            leaseWritten = now;
            leaseDirectory = directory;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static Snapshot ReadSnapshotFile(string path, DateTime processStarted, int processId)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[32769];
            var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (count > 32768) return OfflineSnapshot("Invalid Survival Log trainer status.");
            using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
            return ParseSnapshot(json.RootElement, File.GetLastWriteTimeUtc(stream.SafeFileHandle), processStarted, processId, DateTime.UtcNow);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return OfflineSnapshot("Waiting for Survival Log trainer status."); }
    }

    private static Snapshot OfflineSnapshot(string message) => new(TrainerStatus.Offline(message), "", false, "");

    internal static Snapshot ParseSnapshot(JsonElement json, DateTime modified, DateTime processStarted, int processId, DateTime now)
    {
        if (modified < processStarted || now - modified > TimeSpan.FromSeconds(3) || modified - now > TimeSpan.FromSeconds(2))
            return OfflineSnapshot("Waiting for a fresh Survival Log trainer heartbeat.");
        var session = Text(json, "session_id");
        var expectedStart = new DateTimeOffset(processStarted.ToUniversalTime()).ToUnixTimeMilliseconds();
        var nowMs = new DateTimeOffset(now.ToUniversalTime()).ToUnixTimeMilliseconds();
        if (Text(json, "schema_version") != "1" || Text(json, "version") != TrainerVersion
            || Text(json, "process_id") != processId.ToString(CultureInfo.InvariantCulture)
            || !long.TryParse(Text(json, "process_start_unix_ms"), NumberStyles.None, CultureInfo.InvariantCulture, out var reportedStart) || reportedStart != expectedStart
            || !long.TryParse(Text(json, "heartbeat_unix_ms"), NumberStyles.None, CultureInfo.InvariantCulture, out var heartbeat)
            || heartbeat < expectedStart || heartbeat > nowMs + 2000 || heartbeat < nowMs - 3000
            || !Regex.IsMatch(session, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant))
            return OfflineSnapshot("Survival Log trainer identity or heartbeat is invalid. Reconnect or restart the game.");
        var values = Strings(Object(json, "values"));
        // Native readiness requires a living player, current game update and an authorized client lease.
        var ready = Bool(json, "ready");
        values["diagnostic_sl_initialization_failed"] = (!string.IsNullOrWhiteSpace(values.GetValueOrDefault("initialization_error"))).ToString();
        var available = Bools(Object(json, "available"));
        foreach (var id in FeatureIds) values["available_" + id] = (available.TryGetValue(id, out var enabled) && enabled).ToString();
        values["available_sl_connect"] = bool.FalseString;
        values["sl_limitations"] = "Experimental bindings; gameplay validation is pending.";
        var result = Object(json, "command_result");
        return new Snapshot(new TrainerStatus(true, ready, Text(json, "message", "Survival Log trainer connected."), session,
            Bools(Object(json, "enabled")), values, Text(json, "last_command")), Text(result, "request_id"), Bool(result, "success"), Text(result, "message"));
    }

    internal static Dictionary<string, object> CreateCommand(TrainerStatus current, TrainerStatus requested, string featureId, bool? enabled,
        decimal? value, string requestId, long nowMilliseconds)
    {
        if (!current.Connected || current.SessionId != requested.SessionId || !Regex.IsMatch(current.SessionId, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant))
            throw new IOException("The Survival Log session changed. Reconnect before using trainer controls.");
        if (!Guid.TryParseExact(requestId, "N", out _) || nowMilliseconds <= 0 || nowMilliseconds > long.MaxValue - 5000)
            throw new ArgumentException("Invalid Survival Log command identity or time.");
        if (featureId != "disable_all" && !FeatureIds.Contains(featureId)) throw new ArgumentException("Unknown Survival Log trainer control.");
        var stopping = ToggleIds.Contains(featureId) && enabled == false;
        if (featureId != "disable_all" && !stopping && (!current.Ready || !current.Values.TryGetValue("available_" + featureId, out var available) || available != bool.TrueString))
            throw new InvalidOperationException("This Survival Log control is unavailable in the current game session.");
        if (ToggleIds.Contains(featureId))
        {
            if (!enabled.HasValue || value.HasValue) throw new ArgumentException("An explicit toggle state is required.");
        }
        else if (FeatureIds.Contains(featureId))
        {
            var minimum = featureId == "sl_money" ? 0 : 1;
            var maximum = featureId == "sl_money" ? 100_000_000 : featureId == "sl_survival_points" ? 1_000_000 : 100;
            if (enabled.HasValue || !value.HasValue || value < minimum || value > maximum || decimal.Truncate(value.Value) != value.Value)
                throw new ArgumentOutOfRangeException(nameof(value), $"Choose a whole value from {minimum} to {maximum}.");
        }
        else if (enabled.HasValue || value.HasValue) throw new ArgumentException("This action does not accept a toggle or numeric value.");
        var result = new Dictionary<string, object> {
            ["schema_version"] = 1, ["version"] = TrainerVersion, ["session_id"] = current.SessionId,
            ["request_id"] = requestId, ["feature"] = featureId, ["expires_unix_ms"] = nowMilliseconds + 5000
        };
        if (enabled.HasValue) result["enabled"] = enabled.Value;
        if (value.HasValue) result["value"] = value.Value;
        return result;
    }

    internal static bool AcceptAcknowledgement(Snapshot next, string session, string requestId)
    {
        if (!next.Status.Connected) return false;
        if (next.Status.SessionId != session) throw new IOException("The Survival Log session changed while waiting for the command.");
        if (next.Status.LastCommand != requestId || next.ResultRequest != requestId) return false;
        if (!next.ResultSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(next.ResultMessage) ? "Survival Log rejected the control." : next.ResultMessage);
        return true;
    }

    internal static bool IsExpiredCommand(JsonElement command, long nowMilliseconds) =>
        Text(command, "schema_version") == "1" && Text(command, "version") == TrainerVersion
        && Guid.TryParseExact(Text(command, "request_id"), "N", out _)
        && Regex.IsMatch(Text(command, "session_id"), "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant)
        && long.TryParse(Text(command, "expires_unix_ms"), NumberStyles.None, CultureInfo.InvariantCulture, out var expiry)
        && expiry > 0 && expiry < nowMilliseconds;

    internal static void RetireExpiredCommand(string path, long nowMilliseconds)
    {
        if (!File.Exists(path) || !IsExpiredCommand(ReadJson(path), nowMilliseconds)) return;
        var held = path + ".expired-" + Guid.NewGuid().ToString("N");
        File.Move(path, held); // Capture the exact file before deciding whether to remove it.
        var remove = false;
        try { remove = IsExpiredCommand(ReadJson(held), nowMilliseconds); }
        finally
        {
            if (remove) File.Delete(held);
            else
            {
                // A concurrent replacement may be fresh. Restore it without overwriting another request.
                try { File.Move(held, path); }
                catch (IOException error) { throw new IOException("A pending Survival Log command was preserved at " + held, error); }
            }
        }
    }

    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) =>
        SendCommandAsync(installation, status, featureId, enabled, null);

    public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value) =>
        SendCommandAsync(installation, status, featureId, null, value);

    private async Task SendCommandAsync(GameInstallation installation, TrainerStatus requested, string featureId, bool? enabled, decimal? value)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous Survival Log command.");
        string? commandPath = null;
        string? requestId = null;
        try
        {
            var current = await ReadStatusAsync(installation);
            if (featureId == "sl_connect")
            {
                if (enabled.HasValue || value.HasValue) throw new ArgumentException("Connect trainer does not accept a value.");
                if (!current.Connected || current.SessionId != requested.SessionId || !current.Values.TryGetValue("available_sl_connect", out var available) || available != bool.TrueString)
                    throw new InvalidOperationException("Refresh the Survival Log game status before connecting.");
                if (!Compatible(installation.Directory, force: true) || !SurvivalLogInstaller.MatchesPackage(package, installation.Directory))
                    throw new IOException("The executable or installed Survival Log trainer differs from the verified package.");
                var before = ProcessProvider(installation.Directory);
                if (before.Length != 1 || AwaitingConnection(before[0].Id, before[0].Started, true).SessionId != requested.SessionId)
                    throw new IOException("The Survival Log process changed before connection.");
                await Task.Run(() => AttachTrainer(installation.Directory));
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    var processes = ProcessProvider(installation.Directory);
                    if (processes.Length != 1 || processes[0] != before[0]) throw new IOException("Survival Log changed process while connecting.");
                    var next = ReadSnapshotFile(SafePath(installation.Directory, BridgeRoot + "status.json"), before[0].Started, before[0].Id);
                    if (next.Status.Connected)
                    {
                        if (!string.IsNullOrWhiteSpace(next.Status.Values.GetValueOrDefault("initialization_error")))
                            throw new IOException(next.Status.Values["initialization_error"]);
                        lock (stateLock)
                        {
                            WriteLease(installation.Directory, next.Status.SessionId, force: true);
                            authorizedSession = next.Status.SessionId;
                        }
                        return;
                    }
                    await Task.Delay(250);
                }
                throw new TimeoutException("The Survival Log trainer did not publish a valid heartbeat. Restart the game before retrying if connection remains unavailable.");
            }
            requestId = Guid.NewGuid().ToString("N");
            var command = CreateCommand(current, requested, featureId, enabled, value, requestId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            lock (stateLock)
                if (current.SessionId != authorizedSession) throw new InvalidOperationException("Choose Connect trainer before using Survival Log controls.");
            commandPath = SafePath(installation.Directory, BridgeRoot + "command.json");
            RetireExpiredCommand(commandPath, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var temporary = SafePath(installation.Directory, BridgeRoot + requestId + ".tmp");
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(command), new UTF8Encoding(false));
                File.Move(temporary, commandPath); // Do not overwrite a request whose outcome is unknown.
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            for (var attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(250);
                var processes = ProcessProvider(installation.Directory);
                if (processes.Length != 1) throw new IOException("Survival Log stopped while waiting for the command.");
                var next = ReadSnapshotFile(SafePath(installation.Directory, BridgeRoot + "status.json"), processes[0].Started, processes[0].Id);
                if (AcceptAcknowledgement(next, current.SessionId, requestId)) return;
                lock (stateLock)
                    if (next.Status.Connected && next.Status.SessionId == authorizedSession) WriteLease(installation.Directory, authorizedSession);
            }
            throw new TimeoutException("Survival Log did not acknowledge the command. It may have applied; check the game before retrying.");
        }
        finally
        {
            try
            {
                if (commandPath != null && requestId != null && File.Exists(commandPath) && Text(ReadJson(commandPath), "request_id") == requestId)
                    File.Delete(commandPath);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            commandLock.Release();
        }
    }
}
