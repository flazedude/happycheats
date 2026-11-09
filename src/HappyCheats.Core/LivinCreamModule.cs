using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class LivinCreamModule : IGameModule, IFeatureValueModule
{
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string Executable = "Livin' the Cream Ice Cream Simulator.exe";
    public const string AssemblyPath = "GameAssembly.dll";
    public const string MinimumClientVersion = "0.3.69";
    internal static bool SupportsAssembly(string hash) => string.Equals(hash, SupportedAssemblyHash, StringComparison.OrdinalIgnoreCase);
    public const string SupportedAssemblyHash = "C6081C74041250A89BF8CB943B5E482AF8A05A15853DC727193FFD6FCF51F168";
    public const string MetadataPath = "Livin' the Cream Ice Cream Simulator_Data/il2cpp_data/Metadata/global-metadata.dat";
    public const string SupportedMetadataHash = "F19C0AF79B93122307211E81D2F2C2DEAEB09F908670643F687F83773062D137";
    public const string TrainerVersion = "1.0.2";
    public const string BridgeRoot = LivinCreamInstaller.TrainerRoot;
    private readonly string package;
    private readonly SemaphoreSlim commandLock = new(1, 1);
    private readonly object stateLock = new();
    private (string Path, long Length, DateTime Modified, long MetaLength, DateTime MetaModified, bool Compatible)? inspected;
    private string authorizedSession = "";
    private string authorizedProcessSession = "";
    private DateTime leaseWritten;
    private string? leaseDirectory;

    // Test seams never alter normal discovery, process identity checks or injection.
    internal Func<string, (int Id, DateTime Started)[]> ProcessProvider { get; set; } = directory => Processes(directory, Executable);
    internal Func<string, string> HashProvider { get; set; } = Hash;
    internal Action<string> AttachTrainer { get; set; } = LivinCreamInjector.Attach;

    internal static readonly string[] ToggleIds = { "lc_no_patience", "lc_unlimited_softserve" };
    internal static readonly string[] FeatureIds = ToggleIds.Concat(new[] { "lc_money", "lc_add_xp", "lc_add_skills", "lc_refill_softserve" }).ToArray();
    public const string SupportedSteamBuild = "26511230";
    public LivinCreamModule(string baseDirectory) => package = Path.Combine(baseDirectory, "payload", "livin-cream");
    public GameDefinition Definition { get; } = new("livin-cream", "Livin' the Cream: Ice Cream Simulator", "甜蜜冰淇淋：冰淇淋店模拟器", "4621810", "🍦",
        "Steam build 26511230 (IL2CPP). Install, launch normally, load your shop, then Connect trainer. / 安装后正常启动并读取店铺，再手动连接修改器。", [
        new("lc_connect", "Connect trainer / 连接修改器", "After launching normally and loading your shop, connect explicitly to this IL2CPP build. / 正常启动并读取店铺后，手动连接。", "", false, "Connection / 连接"),
        new("lc_money", "Money / 金钱", "Set shop money once. Normal game saving may retain the result. / 单次设置店铺金钱。", "", false, "Shop / 店铺", ChoiceValueKey: "lc_money", Number: new(0, 100_000_000, 100_000)),
        new("lc_add_xp", "Add shop XP / 增加店铺经验", "Grant 1–10,000 XP once through normal level-up rewards. / 单次增加经验，保留正常升级奖励。", "", false, "Shop / 店铺", Number: new(1, 10_000, 1_000)),
        new("lc_add_skills", "Add skill points / 增加技能点", "Grant 1–1,000 skill points once. / 单次增加技能点。", "", false, "Shop / 店铺", Number: new(1, 1_000, 10)),
        new("lc_no_patience", "Unlimited customer patience / 顾客无限耐心", "Remove the front customer's wait timer without changing play style. Off restarts a normal full timer where applicable. / 关闭后恢复完整计时，不更改游戏模式。", "", true, "Customers / 顾客"),
        new("lc_unlimited_softserve", "Unlimited soft-serve stock / 软冰淇淋不消耗", "Prevent soft-serve reservoirs from consuming their existing contents. Fill each machine normally first. / 保留机器现有原料，请先正常添加口味。", "", true, "Stock / 原料"),
        new("lc_refill_softserve", "Refill soft-serve stock / 补满软冰淇淋原料", "Refill nonempty soft-serve reservoirs once, preserving flavors. Does not refill scoop pans or other supplies. / 单次补满非空软冰淇淋机器并保留口味。", "", false, "Stock / 原料")
    ]);


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
            var installed = LivinCreamInstaller.Installed(found.Directory);
            bool matching;
            try { matching = installed && LivinCreamInstaller.MatchesPackage(package, found.Directory); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                return new GameInstallation(found.Directory, found.Build, true, installed, running, compatible,
                    "Livin' the Cream trainer package unavailable: " + error.Message);
            }
            return new GameInstallation(found.Directory, found.Build, true, installed, running, compatible,
                !compatible ? "This Livin' the Cream native assembly or metadata needs a compatibility check. Supported Steam build: 26511230."
                : matching ? "Experimental trainer installed. Start the game, then choose Connect trainer."
                : installed ? "Livin' the Cream trainer update required. Close the game before installing."
                : "Ready to install the experimental Livin' the Cream trainer.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message); }
    });

    private void CheckClosed(string directory)
    {
        if (ProcessProvider(directory).Length > 0) throw new IOException("Close Livin' the Cream before changing trainer files.");
    }

    public async Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("A Livin' the Cream operation is already running. Wait and retry.");
        try { await Task.Run(() => {
            if (!Compatible(installation.Directory, force: true)) throw new IOException("Unsupported Livin' the Cream native assembly or metadata. Trainer installation was refused.");
            LivinCreamInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
            if (!LivinCreamInstaller.MatchesPackage(package, installation.Directory)) throw new IOException("Trainer installation did not verify. Retry Install / update.");
        }); } finally { commandLock.Release(); }
    }

    public async Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("A Livin' the Cream operation is already running. Wait and retry.");
        try { await Task.Run(() => {
            LivinCreamInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress);
            lock (stateLock) { authorizedSession = ""; authorizedProcessSession = ""; leaseWritten = default; }
        }); } finally { commandLock.Release(); }
    }

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
                if (File.Exists(path) && File.ReadAllText(path) == authorizedProcessSession) File.Delete(path);
            }
            authorizedSession = ""; authorizedProcessSession = ""; leaseWritten = default; leaseDirectory = null;
        }
    }

    internal sealed record Snapshot(TrainerStatus Status, string ResultRequest, bool ResultSuccess, string ResultMessage);

    internal static TrainerStatus AwaitingConnection(int processId, DateTime started, bool installed)
    {
        var values = FeatureIds.ToDictionary(id => "available_" + id, _ => bool.FalseString);
        values["available_lc_connect"] = installed.ToString();
        values["lc_limitations"] = "Experimental bindings; gameplay validation is pending.";
        return new TrainerStatus(true, true, installed ? "Game found. Choose Connect trainer." : "Close the game and install the Livin' the Cream trainer first.",
            $"process{processId}-{started.Ticks}", new Dictionary<string, bool>(), values);
    }

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadStatus(installation));

    private TrainerStatus ReadStatus(GameInstallation installation)
    {
        if (!installation.IsInstalled || !installation.Compatible) return TrainerStatus.Offline(installation.Message);
        try
        {
            var processes = ProcessProvider(installation.Directory);
            if (processes.Length != 1) return TrainerStatus.Offline("Start Livin' the Cream through Steam, then choose Connect trainer.");
            var process = processes[0];
            var native = ReadSnapshotFile(SafePath(installation.Directory, BridgeRoot + "status.json"), process.Started, process.Id);
            lock (stateLock)
            {
                if (!native.Status.Connected || !SameProcessSession(native.Status, authorizedProcessSession))
                    return AwaitingConnection(process.Id, process.Started, installation.TrainerInstalled);
                authorizedSession = native.Status.SessionId;
                WriteLease(installation.Directory, authorizedProcessSession);
            }
            return native.Status;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return TrainerStatus.Offline("Waiting for Livin' the Cream: " + error.Message); }
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

    internal static bool SameProcessSession(TrainerStatus native, string authorized) => authorized.Length == 32
        && native.Values.GetValueOrDefault("diagnostic_lc_process_session") == authorized
        && native.SessionId.StartsWith(authorized + "_", StringComparison.Ordinal);

    internal static Snapshot ReadSnapshotFile(string path, DateTime processStarted, int processId)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[32769];
            var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (count > 32768) return OfflineSnapshot("Invalid Livin' the Cream trainer status.");
            using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
            return ParseSnapshot(json.RootElement, File.GetLastWriteTimeUtc(stream.SafeFileHandle), processStarted, processId, DateTime.UtcNow);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return OfflineSnapshot("Waiting for Livin' the Cream trainer status."); }
    }

    private static Snapshot OfflineSnapshot(string message) => new(TrainerStatus.Offline(message), "", false, "");

    internal static Snapshot ParseSnapshot(JsonElement json, DateTime modified, DateTime processStarted, int processId, DateTime now)
    {
        if (modified < processStarted || now - modified > TimeSpan.FromSeconds(3) || modified - now > TimeSpan.FromSeconds(2))
            return OfflineSnapshot("Waiting for a fresh Livin' the Cream trainer heartbeat.");
        var session = Text(json, "session_id");
        var expectedStart = new DateTimeOffset(processStarted.ToUniversalTime()).ToUnixTimeMilliseconds();
        var nowMs = new DateTimeOffset(now.ToUniversalTime()).ToUnixTimeMilliseconds();
        if (Text(json, "schema_version") != "1" || Text(json, "version") != TrainerVersion
            || Text(json, "process_id") != processId.ToString(CultureInfo.InvariantCulture)
            || !long.TryParse(Text(json, "process_start_unix_ms"), NumberStyles.None, CultureInfo.InvariantCulture, out var reportedStart) || reportedStart != expectedStart
            || !long.TryParse(Text(json, "heartbeat_unix_ms"), NumberStyles.None, CultureInfo.InvariantCulture, out var heartbeat)
            || heartbeat < expectedStart || heartbeat > nowMs + 2000 || heartbeat < nowMs - 3000
            || !Regex.IsMatch(session, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant))
            return OfflineSnapshot("Livin' the Cream trainer identity or heartbeat is invalid. Reconnect or restart the game.");
        var processSession = Text(json, "process_session");
        if (!Regex.IsMatch(processSession, "^[a-f0-9]{32}$") || !session.StartsWith(processSession + "_", StringComparison.Ordinal)
            || !ulong.TryParse(session[(processSession.Length + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return OfflineSnapshot("Native process/session identity mismatch. Reconnect the trainer.");
        var values = Strings(Object(json, "values"));
        // Native readiness requires a living player, current game update and an authorized client lease.
        var ready = Bool(json, "ready");
        values["diagnostic_lc_process_session"] = Text(json, "process_session");
        values["diagnostic_lc_initialization_failed"] = (!string.IsNullOrWhiteSpace(values.GetValueOrDefault("initialization_error"))).ToString();
        var available = Bools(Object(json, "available"));
        foreach (var id in FeatureIds) values["available_" + id] = (available.TryGetValue(id, out var enabled) && enabled).ToString();
        values["available_lc_connect"] = bool.FalseString;
        values["lc_limitations"] = "Experimental bindings; gameplay validation is pending.";
        var result = Object(json, "command_result");
        return new Snapshot(new TrainerStatus(true, ready, Text(json, "message", "Livin' the Cream trainer connected."), session,
            Bools(Object(json, "enabled")), values, Text(json, "last_command")), Text(result, "request_id"), Bool(result, "success"), Text(result, "message"));
    }

    internal static Dictionary<string, object> CreateCommand(TrainerStatus current, TrainerStatus requested, string featureId, bool? enabled,
        decimal? value, string requestId, long nowMilliseconds)
    {
        if (!Regex.IsMatch(current.SessionId, "^[a-f0-9]{32}_[0-9]+$", RegexOptions.CultureInvariant))
            throw new IOException("Connect the Livin' the Cream trainer once, then retry this control.");
        if (!Guid.TryParseExact(requestId, "N", out _) || nowMilliseconds <= 0 || nowMilliseconds > long.MaxValue - 5000)
            throw new ArgumentException("Invalid Livin' the Cream command identity or time.");
        if (featureId != "disable_all" && !FeatureIds.Contains(featureId)) throw new ArgumentException("Unknown Livin' the Cream trainer control.");
        if (ToggleIds.Contains(featureId))
        {
            if (!enabled.HasValue || value.HasValue) throw new ArgumentException("An explicit toggle state is required.");
        }
        else if (FeatureIds.Contains(featureId) && featureId != "lc_refill_softserve")
        {
            var minimum = featureId == "lc_money" ? 0 : 1;
            var maximum = featureId == "lc_money" ? 100_000_000 : featureId == "lc_add_xp" ? 10_000 : 1_000;
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
        if (!next.Status.SessionId.StartsWith(session.Split('_')[0] + "_", StringComparison.Ordinal))
            throw new IOException("The game process restarted while waiting for the command.");
        if (next.Status.LastCommand != requestId || next.ResultRequest != requestId) return false;
        if (!next.ResultSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(next.ResultMessage) ? "Livin' the Cream rejected the control." : next.ResultMessage);
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
                catch (IOException error) { throw new IOException("A pending Livin' the Cream command was preserved at " + held, error); }
            }
        }
    }

    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) =>
        SendCommandAsync(installation, status, featureId, enabled, null);

    public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value) =>
        SendCommandAsync(installation, status, featureId, null, value);

    private async Task SendCommandAsync(GameInstallation installation, TrainerStatus requested, string featureId, bool? enabled, decimal? value)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous Livin' the Cream command.");
        string? commandPath = null;
        string? requestId = null;
        try
        {
            var current = await ReadStatusAsync(installation);
            if (featureId == "lc_connect")
            {
                if (enabled.HasValue || value.HasValue) throw new ArgumentException("Connect trainer does not accept a value.");
                if (!Compatible(installation.Directory, force: true) || !LivinCreamInstaller.MatchesPackage(package, installation.Directory))
                    throw new IOException("The executable or installed Livin' the Cream trainer differs from the verified package.");
                var before = ProcessProvider(installation.Directory);
                if (before.Length != 1) throw new IOException("Start Livin' the Cream, then connect the trainer.");
                await Task.Run(() => AttachTrainer(installation.Directory));
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    var processes = ProcessProvider(installation.Directory);
                    if (processes.Length != 1 || processes[0] != before[0]) throw new IOException("Livin' the Cream changed process while connecting.");
                    var next = ReadSnapshotFile(SafePath(installation.Directory, BridgeRoot + "status.json"), before[0].Started, before[0].Id);
                    if (next.Status.Connected)
                    {
                        if (!string.IsNullOrWhiteSpace(next.Status.Values.GetValueOrDefault("initialization_error")))
                            throw new IOException(next.Status.Values["initialization_error"]);
                        lock (stateLock)
                        {
                            authorizedProcessSession = next.Status.Values.GetValueOrDefault("diagnostic_lc_process_session", "");
                            WriteLease(installation.Directory, authorizedProcessSession, force: true);
                            authorizedSession = next.Status.SessionId;
                        }
                        return;
                    }
                    await Task.Delay(250);
                }
                throw new TimeoutException("The Livin' the Cream trainer did not publish a valid heartbeat. Restart the game before retrying if connection remains unavailable.");
            }
            requestId = Guid.NewGuid().ToString("N");
            var command = CreateCommand(current, requested, featureId, enabled, value, requestId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            lock (stateLock)
                if (current.SessionId != authorizedSession) throw new InvalidOperationException("Choose Connect trainer before using Livin' the Cream controls.");
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
                if (processes.Length != 1) throw new IOException("Livin' the Cream stopped while waiting for the command.");
                var next = ReadSnapshotFile(SafePath(installation.Directory, BridgeRoot + "status.json"), processes[0].Started, processes[0].Id);
                if (AcceptAcknowledgement(next, current.SessionId, requestId)) return;
                lock (stateLock)
                    if (next.Status.Connected && next.Status.SessionId == authorizedSession) WriteLease(installation.Directory, authorizedProcessSession);
            }
            throw new TimeoutException("Livin' the Cream did not acknowledge the command. It may have applied; check the game before retrying.");
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
