using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class CrimsonDesertModule : IGameModule, IFeatureValueModule
{
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string Executable = "bin64/CrimsonDesert.exe";
    public const string SupportedExecutableHash = "D4855C08998638F07FA8FAE08BD6249261F10C8FEC2215EFFC29CB339E7FA525";
    public const string TrainerVersion = "0.2.6";
    public const string BridgeRoot = CrimsonDesertInstaller.TrainerRoot;
    private readonly string package;
    private readonly SemaphoreSlim commandLock = new(1, 1);
    private readonly object stateLock = new();
    private (string Path, long Length, DateTime Modified, bool Compatible)? inspected;
    private string authorizedSession = "";
    private DateTime leaseWritten;

    // Test seams never alter normal discovery, process identity checks or injection.
    internal Func<string, (int Id, DateTime Started)[]> ProcessProvider { get; set; } = directory => Processes(directory, Executable);
    internal Func<string, string> HashProvider { get; set; } = Hash;
    internal Action<string> AttachTrainer { get; set; } = CrimsonDesertInjector.Attach;

    internal static readonly string[] FeatureIds = { "crimson_health", "crimson_stamina", "crimson_one_hit", "crimson_spirit", "crimson_teleport", "crimson_auto_teleport", "crimson_speed", "crimson_items" };
    private static readonly string[] ToggleIds = { "crimson_health", "crimson_stamina", "crimson_one_hit", "crimson_spirit", "crimson_auto_teleport", "crimson_items" };

    public CrimsonDesertModule(string baseDirectory) => package = Path.Combine(baseDirectory, "payload", "crimson-desert");

    public GameDefinition Definition { get; } = new("crimson-desert", "Crimson Desert", "OPEN WORLD ACTION ADVENTURE", "3321460", "CD",
        "Experimental Steam trainer with manual and automatic map-marker teleport. Pins without height use your current elevation; terrain landing is unverified. Item usage remains unavailable.",
        new FeatureDefinition[] {
            new("crimson_connect", "Connect trainer", "Load the installed trainer into the running game. Opening this page never connects automatically.", "F8", false, "Connection"),
            new("crimson_health", "Unlimited health", "Restore the controlled character's health when the matching stat update runs. Experimental; gameplay validation is pending.", "F1", true, "Survival"),
            new("crimson_stamina", "Unlimited stamina", "Restore the controlled character's stamina through the matching stat update. Experimental; gameplay validation is pending.", "F2", true, "Survival"),
            new("crimson_one_hit", "One hit kill", "Apply greatly increased damage only when the controlled player is confirmed as the attacker. Boss and scripted encounters remain unverified.", "F3", true, "Combat"),
            new("crimson_spirit", "Unlimited spirit", "Restore the controlled character's spirit resource through the matching stat update. Experimental; gameplay validation is pending.", "F4", true, "Survival"),
            new("crimson_teleport", "Teleport to marker", "Teleport once to the current map destination. Pins without height use your current elevation; terrain landing and streaming are unverified.", "F5", false, "Exploration"),
            new("crimson_auto_teleport", "Auto teleport", "Turn on, then place a new map marker. Teleports once at the next player movement update; close the map if waiting. Existing markers are ignored. Terrain landing is unverified.", "F9", true, "Exploration"),
            new("crimson_speed", "Character movement speed", "Set the controlled character's movement multiplier from 0.25× to 3×. Use 1× to restore normal speed. Experimental; gameplay validation is pending.", "F6", false, "Exploration", ChoiceValueKey: "crimson_speed", Number: new FeatureNumber(0.25m, 3m, 1m, 0.25m, "×")),
            new("crimson_items", "Unlimited item usage", "Unavailable: a player-only item-consumption binding has not been verified for this build.", "F7", true, "Inventory")
        });

    internal bool Compatible(string directory, bool force = false)
    {
        var path = SafePath(directory, Executable);
        var file = new FileInfo(path);
        if (!file.Exists) return false;
        lock (stateLock)
        {
            if (!force && inspected is { } prior && prior.Path == path && prior.Length == file.Length && prior.Modified == file.LastWriteTimeUtc)
                return prior.Compatible;
            var compatible = string.Equals(HashProvider(path), SupportedExecutableHash, StringComparison.OrdinalIgnoreCase);
            inspected = (path, file.Length, file.LastWriteTimeUtc, compatible);
            return compatible;
        }
    }

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try
        {
            var found = FindSteam(Definition.AppId, Executable, directory);
            var compatible = Compatible(found.Directory);
            var running = ProcessProvider(found.Directory).Length > 0;
            var installed = CrimsonDesertInstaller.Installed(found.Directory);
            bool matching;
            try { matching = installed && CrimsonDesertInstaller.MatchesPackage(package, found.Directory); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                return new GameInstallation(found.Directory, found.Build, true, installed, running, compatible,
                    "Crimson Desert trainer package unavailable: " + error.Message);
            }
            return new GameInstallation(found.Directory, found.Build, true, installed, running, compatible,
                !compatible ? "This Crimson Desert executable needs a compatibility check. Supported Steam build: 26317560 (PE 3424, Charting the Unknown update)."
                : matching ? "Experimental trainer installed. Start the game, then choose Connect trainer."
                : installed ? "Crimson Desert trainer update required. Close the game before installing."
                : "Ready to install the experimental Crimson Desert trainer.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message); }
    });

    private void CheckClosed(string directory)
    {
        if (ProcessProvider(directory).Length > 0) throw new IOException("Close Crimson Desert before changing trainer files.");
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        if (!Compatible(installation.Directory, force: true)) throw new IOException("Unsupported Crimson Desert executable. Trainer installation was refused.");
        CrimsonDesertInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
    });

    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        CrimsonDesertInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress);
        lock (stateLock) { authorizedSession = ""; leaseWritten = default; }
    });

    public Task LaunchAsync(GameInstallation installation)
    {
        var registered = FindSteam(Definition.AppId, Executable);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }

    internal sealed record Snapshot(TrainerStatus Status, string ResultRequest, bool ResultSuccess, string ResultMessage);

    internal static TrainerStatus AwaitingConnection(int processId, DateTime started, bool installed)
    {
        var values = FeatureIds.ToDictionary(id => "available_" + id, _ => bool.FalseString);
        values["available_crimson_connect"] = installed.ToString();
        values["crimson_limitations"] = "Marker teleport uses current elevation when height is missing. Terrain landing is unverified; unlimited item usage is unavailable.";
        return new TrainerStatus(true, true, installed ? "Game found. Choose Connect trainer." : "Close the game and install the Crimson Desert trainer first.",
            $"process{processId}-{started.Ticks}", new Dictionary<string, bool>(), values);
    }

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadStatus(installation));

    private TrainerStatus ReadStatus(GameInstallation installation)
    {
        if (!installation.IsInstalled || !installation.Compatible) return TrainerStatus.Offline(installation.Message);
        try
        {
            var processes = ProcessProvider(installation.Directory);
            if (processes.Length != 1) return TrainerStatus.Offline("Start Crimson Desert through Steam, then choose Connect trainer.");
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
        { return TrainerStatus.Offline("Waiting for Crimson Desert: " + error.Message); }
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
            if (count > 32768) return OfflineSnapshot("Invalid Crimson Desert trainer status.");
            using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
            return ParseSnapshot(json.RootElement, File.GetLastWriteTimeUtc(stream.SafeFileHandle), processStarted, processId, DateTime.UtcNow);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return OfflineSnapshot("Waiting for Crimson Desert trainer status."); }
    }

    private static Snapshot OfflineSnapshot(string message) => new(TrainerStatus.Offline(message), "", false, "");

    internal static Snapshot ParseSnapshot(JsonElement json, DateTime modified, DateTime processStarted, int processId, DateTime now)
    {
        if (modified < processStarted || now - modified > TimeSpan.FromSeconds(3) || modified - now > TimeSpan.FromSeconds(2))
            return OfflineSnapshot("Waiting for a fresh Crimson Desert trainer heartbeat.");
        var session = Text(json, "session_id");
        var expectedStart = new DateTimeOffset(processStarted.ToUniversalTime()).ToUnixTimeMilliseconds();
        var nowMs = new DateTimeOffset(now.ToUniversalTime()).ToUnixTimeMilliseconds();
        if (Text(json, "schema_version") != "1" || Text(json, "version") != TrainerVersion
            || Text(json, "process_id") != processId.ToString(CultureInfo.InvariantCulture)
            || !long.TryParse(Text(json, "process_start_unix_ms"), NumberStyles.None, CultureInfo.InvariantCulture, out var reportedStart) || reportedStart != expectedStart
            || !long.TryParse(Text(json, "heartbeat_unix_ms"), NumberStyles.None, CultureInfo.InvariantCulture, out var heartbeat)
            || heartbeat < expectedStart || heartbeat > nowMs + 2000 || heartbeat < nowMs - 3000
            || !Regex.IsMatch(session, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant))
            return OfflineSnapshot("Crimson Desert trainer identity or heartbeat is invalid. Reconnect or restart the game.");
        var values = Strings(Object(json, "values"));
        // Native status is first published after hook installation has completed.
        // Its ready flag means at least one hook installed, not player/loading state.
        var ready = Bool(json, "ready");
        values["diagnostic_crimson_initialization_failed"] = (!ready).ToString();
        var teleportMessage = Text(json, "teleport_message");
        if (!string.IsNullOrWhiteSpace(teleportMessage) && teleportMessage.Length <= 512)
            values["crimson_teleport_status"] = teleportMessage;
        var available = Bools(Object(json, "available"));
        foreach (var id in FeatureIds) values["available_" + id] = (available.TryGetValue(id, out var enabled) && enabled).ToString();
        values["available_crimson_connect"] = bool.FalseString;
        values["crimson_limitations"] = "Marker teleport uses current elevation when height is missing. Terrain landing is unverified; unlimited item usage is unavailable.";
        var result = Object(json, "command_result");
        return new Snapshot(new TrainerStatus(true, ready, Text(json, "message", "Crimson Desert trainer connected."), session,
            Bools(Object(json, "enabled")), values, Text(json, "last_command")), Text(result, "request_id"), Bool(result, "success"), Text(result, "message"));
    }

    internal static Dictionary<string, object> CreateCommand(TrainerStatus current, TrainerStatus requested, string featureId, bool? enabled,
        decimal? value, string requestId, long nowMilliseconds)
    {
        if (!current.Connected || current.SessionId != requested.SessionId || !Regex.IsMatch(current.SessionId, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant))
            throw new IOException("The Crimson Desert session changed. Reconnect before using trainer controls.");
        if (!Guid.TryParseExact(requestId, "N", out _) || nowMilliseconds <= 0 || nowMilliseconds > long.MaxValue - 5000)
            throw new ArgumentException("Invalid Crimson Desert command identity or time.");
        if (featureId != "disable_all" && !FeatureIds.Contains(featureId)) throw new ArgumentException("Unknown Crimson Desert trainer control.");
        var stopAutoTeleport = featureId == "crimson_auto_teleport" && enabled == false;
        if (featureId != "disable_all" && !stopAutoTeleport && (!current.Ready || !current.Values.TryGetValue("available_" + featureId, out var available) || available != bool.TrueString))
            throw new InvalidOperationException("This Crimson Desert control is unavailable in the current game session.");
        if (ToggleIds.Contains(featureId))
        {
            if (!enabled.HasValue || value.HasValue) throw new ArgumentException("An explicit toggle state is required.");
        }
        else if (featureId == "crimson_speed")
        {
            if (enabled.HasValue || !value.HasValue || value < 0.25m || value > 3m || value % 0.25m != 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Movement speed must be 0.25 to 3 in increments of 0.25.");
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
        if (next.Status.SessionId != session) throw new IOException("The Crimson Desert session changed while waiting for the command.");
        if (next.Status.LastCommand != requestId || next.ResultRequest != requestId) return false;
        if (!next.ResultSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(next.ResultMessage) ? "Crimson Desert rejected the control." : next.ResultMessage);
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
                catch (IOException error) { throw new IOException("A pending Crimson Desert command was preserved at " + held, error); }
            }
        }
    }

    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) =>
        SendCommandAsync(installation, status, featureId, enabled, null);

    public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value) =>
        SendCommandAsync(installation, status, featureId, null, value);

    private async Task SendCommandAsync(GameInstallation installation, TrainerStatus requested, string featureId, bool? enabled, decimal? value)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous Crimson Desert command.");
        string? commandPath = null;
        string? requestId = null;
        try
        {
            var current = await ReadStatusAsync(installation);
            if (featureId == "crimson_connect")
            {
                if (enabled.HasValue || value.HasValue) throw new ArgumentException("Connect trainer does not accept a value.");
                if (!current.Connected || current.SessionId != requested.SessionId || !current.Values.TryGetValue("available_crimson_connect", out var available) || available != bool.TrueString)
                    throw new InvalidOperationException("Refresh the Crimson Desert game status before connecting.");
                if (!Compatible(installation.Directory, force: true) || !CrimsonDesertInstaller.MatchesPackage(package, installation.Directory))
                    throw new IOException("The executable or installed Crimson Desert trainer differs from the verified package.");
                var before = ProcessProvider(installation.Directory);
                if (before.Length != 1 || AwaitingConnection(before[0].Id, before[0].Started, true).SessionId != requested.SessionId)
                    throw new IOException("The Crimson Desert process changed before connection.");
                await Task.Run(() => AttachTrainer(installation.Directory));
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    var processes = ProcessProvider(installation.Directory);
                    if (processes.Length != 1 || processes[0] != before[0]) throw new IOException("Crimson Desert changed process while connecting.");
                    var next = ReadSnapshotFile(SafePath(installation.Directory, BridgeRoot + "status.json"), before[0].Started, before[0].Id);
                    if (next.Status.Connected)
                    {
                        lock (stateLock)
                        {
                            WriteLease(installation.Directory, next.Status.SessionId, force: true);
                            authorizedSession = next.Status.SessionId;
                        }
                        return;
                    }
                    await Task.Delay(250);
                }
                throw new TimeoutException("The Crimson Desert trainer did not publish a valid heartbeat. Restart the game before retrying if connection remains unavailable.");
            }
            requestId = Guid.NewGuid().ToString("N");
            var command = CreateCommand(current, requested, featureId, enabled, value, requestId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            lock (stateLock)
                if (current.SessionId != authorizedSession) throw new InvalidOperationException("Choose Connect trainer before using Crimson Desert controls.");
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
                if (processes.Length != 1) throw new IOException("Crimson Desert stopped while waiting for the command.");
                var next = ReadSnapshotFile(SafePath(installation.Directory, BridgeRoot + "status.json"), processes[0].Started, processes[0].Id);
                if (AcceptAcknowledgement(next, current.SessionId, requestId)) return;
                lock (stateLock)
                    if (next.Status.Connected && next.Status.SessionId == authorizedSession) WriteLease(installation.Directory, authorizedSession);
            }
            throw new TimeoutException("Crimson Desert did not acknowledge the command. It may have applied; check the game before retrying.");
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
