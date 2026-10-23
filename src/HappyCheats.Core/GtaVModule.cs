using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Enhanced Story Mode bridge. GTA Online sessions are never eligible.</summary>
public sealed class GtaVModule(string baseDirectory) : IGameModule
{
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string Executable = "GTA5_Enhanced.exe";
    public const string LatestSupportedGameVersion = "1.0.1158.16";
    public const string LegacySupportedVersion = "1.0.1158.13";
    public const string SupportedGameVersion = LatestSupportedGameVersion;
    public const string LatestExecutableHash = "69DA07FF67D05E9DED11289E597E8B8DC5855B0A429C085F37D148DC267CB2C5";
    // Official ScriptHookV_3889.0_1158.13.zip, obtained from dev-c.com; runtime is not redistributed.
    public const string SupportedScriptHookHash = "126BE57CA9DCA00E471F9DE611766009C900AAD88F89AE6C2926EC62E5AAEB4F";
    public const string TrainerVersion = "0.2.0";
    public const string BridgeRoot = GtaVInstaller.TrainerRoot;
    private readonly string package = Path.Combine(baseDirectory, "payload", "gta-v-enhanced");
    private readonly SemaphoreSlim commandLock = new(1, 1);
    private readonly object hashLock = new();
    private (GameFileStamp Stamp, string Hash)? inspectedGame;
    private sealed record GameFileStamp(string Path, long Length, DateTime Modified, DateTime Created);
    internal Func<string, (int Id, DateTime Started)[]> ProcessProvider { get; set; } = directory => Processes(directory, Executable);
    internal Func<string, string> VersionProvider { get; set; } = ReadExecutableVersion;
    internal Func<string, string> GameHashProvider { get; set; } = Hash;
    internal GtaVRuntimeSetup RuntimeSetup { get; set; } = new();
    internal static readonly string[] FeatureIds = { "gta_health", "gta_ammo", "gta_wanted", "gta_refill", "gta_repair", "gta_noon", "gta_clear_weather" };
    private static bool IsToggle(string action) => action is "gta_health" or "gta_ammo" or "gta_wanted";
    private static bool HexId(string value) => Regex.IsMatch(value, "^[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant);

    public GameDefinition Definition { get; } = new("gta-v-enhanced", "Grand Theft Auto V Enhanced", "STORY MODE", "3240220", "V",
        "Experimental Story Mode menu for the latest Steam build, GTA V Enhanced 1.0.1158.16. F11 opens the menu. Install / update sets up the required loader or Script Hook V automatically. GTA Online, including invite-only sessions, is unsupported. Native startup and gameplay are unverified.",
        new FeatureDefinition[] {
            new("gta_health", "Invincible", "Protect the Story Mode player from damage while enabled. Scripted mission outcomes may still apply.", "F1", true, "Survival"),
            new("gta_ammo", "Unlimited ammo", "Keep ammunition available for the Story Mode player's weapons while enabled.", "F2", true, "Combat"),
            new("gta_wanted", "Never wanted", "Clear the Story Mode player's wanted level while enabled. Mission scripts may override normal police behavior.", "F3", true, "Player"),
            new("gta_refill", "Refill health and armor", "Restore the living Story Mode player's health and armor once.", "F4", false, "Survival"),
            new("gta_repair", "Repair current vehicle", "Repair the vehicle currently occupied by the Story Mode player.", "F6", false, "Vehicles"),
            new("gta_noon", "Set time to noon", "Set the Story Mode clock to 12:00 once.", "F7", false, "World"),
            new("gta_clear_weather", "Clear weather", "Set clear Story Mode weather once.", "F8", false, "World")
        });

    internal static string ReadExecutableVersion(string path)
    {
        var version = FileVersionInfo.GetVersionInfo(path);
        return $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.{version.FilePrivatePart}";
    }

    internal static bool IsSupportedVersion(string? version) => version is LatestSupportedGameVersion or LegacySupportedVersion;

    private string GameVersion(string directory)
    {
        var path = SafePath(directory, Executable);
        return File.Exists(path) ? VersionProvider(path) : "Unknown";
    }

    private static GameFileStamp Stamp(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new IOException("The GTA V Enhanced executable is missing.");
        return new(path, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc);
    }

    internal string InspectGameHash(string directory, bool force = false)
    {
        var path = SafePath(directory, Executable);
        lock (hashLock) {
            var before = Stamp(path);
            if (!force && inspectedGame is { } cached && cached.Stamp == before) return cached.Hash;
            inspectedGame = null;
            // The normal provider hashes through File.OpenRead, denying concurrent writes
            // and deletion while the stream is open. The metadata check also rejects
            // replacements between inspection and hashing, including test-provider races.
            var hash = GameHashProvider(path);
            if (Stamp(path) != before) throw new IOException("GTA V Enhanced changed during executable verification. Scan the game again.");
            inspectedGame = (before, hash);
            return hash;
        }
    }

    internal bool GameCompatible(string directory, bool force = false)
    {
        var version = GameVersion(directory);
        return version == LegacySupportedVersion || version == LatestSupportedGameVersion
            && string.Equals(InspectGameHash(directory, force), LatestExecutableHash, StringComparison.OrdinalIgnoreCase);
    }

    private bool RuntimeReady(string directory) => RuntimeSetup.IsReady(directory, GameVersion(directory) == LegacySupportedVersion);

    internal bool Compatible(string directory) => GameCompatible(directory) && RuntimeReady(directory);

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var found = FindSteam(Definition.AppId, Executable, directory);
            var version = GameVersion(found.Directory);
            var supported = GameCompatible(found.Directory);
            var requireScriptHook = version == LegacySupportedVersion;
            string? runtimeConflict = null;
            if (supported) {
                try { RuntimeSetup.ValidateExisting(found.Directory, requireScriptHook); }
                catch (IOException error) { runtimeConflict = error.Message; }
            }
            var runtimeReady = supported && runtimeConflict == null && RuntimeSetup.IsReady(found.Directory, requireScriptHook);
            var installed = GtaVInstaller.Installed(found.Directory);
            var matching = false;
            if (installed) {
                try { matching = GtaVInstaller.MatchesPackage(package, found.Directory); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
            }
            var message = !supported ? UnsupportedMessage(version)
                : runtimeConflict != null ? runtimeConflict
                : !runtimeReady ? requireScriptHook
                    ? "GTA V Enhanced 1.0.1158.13 detected. Install / update obtains the verified Script Hook V and ASI loader from the author. Native startup and gameplay are unverified."
                    : "Pinned GTA V Enhanced 1.0.1158.16 detected. Install / update obtains the verified ASI loader for the included experimental Story Mode backend. Native startup and gameplay are unverified."
                : installed ? matching ? "Experimental GTA V Enhanced Story Mode trainer installed. Native startup and gameplay are unverified."
                    : "GTA V Enhanced trainer update required. Close the game before installing."
                : "Ready to install the experimental GTA V Enhanced Story Mode trainer. Native startup and gameplay are unverified. GTA Online is unsupported.";
            return new GameInstallation(found.Directory, found.Build + " / " + version, true, installed,
                ProcessProvider(found.Directory).Length > 0, supported && runtimeConflict == null, message);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) {
            return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message);
        }
    });

    private void CheckClosed(string directory)
    {
        if (ProcessProvider(directory).Length > 0) throw new IOException("Close GTA V Enhanced before changing trainer files.");
    }

    private static string UnsupportedMessage(string version) => version == LatestSupportedGameVersion
        ? "This GTA V Enhanced 1.0.1158.16 executable does not match the reviewed Steam build. The experimental backend requires its pinned executable hash; game files were preserved."
        : $"GTA V Enhanced {version} is unsupported. Experimental targets are the pinned 1.0.1158.16 Steam build and 1.0.1158.13; game files were preserved.";

    private void CheckInstall(string directory, string expectedVersion)
    {
        CheckClosed(directory);
        var version = GameVersion(directory);
        if (version != expectedVersion || !GameCompatible(directory, force: true)) throw new IOException(UnsupportedMessage(version));
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(async () => {
        if (!installation.IsInstalled) throw new IOException("Choose an installed GTA V Enhanced game folder first.");
        var directory = installation.Directory;
        var version = GameVersion(directory);
        var requireScriptHook = version == LegacySupportedVersion;
        CheckInstall(directory, version);
        // Validate payload integrity and ownership conflicts before runtime setup can
        // download anything or change dependencies in the game directory.
        GtaVInstaller.ValidateInstall(package, directory);
        await RuntimeSetup.RunWithRuntimeAsync(directory, () => CheckInstall(directory, version), () => {
            void CheckTrainerInstall() {
                CheckInstall(directory, version);
                if (!RuntimeSetup.IsReady(directory, requireScriptHook)) throw new IOException("The verified GTA V runtime changed during setup; trainer installation was stopped.");
            }
            GtaVInstaller.Install(package, directory, CheckTrainerInstall, progress, requireScriptHook);
        }, progress, CancellationToken.None, requireScriptHook).ConfigureAwait(false);
    });

    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() =>
        GtaVInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));

    public Task LaunchAsync(GameInstallation installation)
    {
        if (!installation.IsInstalled) throw new IOException("Install GTA V Enhanced through Steam first.");
        if (installation.TrainerInstalled && !Compatible(installation.Directory))
            throw new IOException("The installed GTA V Enhanced trainer is incompatible. Remove it before launching this game version.");
        var registered = FindSteam(Definition.AppId, Executable);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }

    internal sealed record Snapshot(TrainerStatus Status, bool LastSuccess, string LastMessage);
    private static Snapshot Offline(string message) => new(TrainerStatus.Offline(message), false, "");
    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadSnapshot(installation).Status);

    private Snapshot ReadSnapshot(GameInstallation installation)
    {
        if (!installation.IsInstalled) return Offline(installation.Message);
        try {
            var version = GameVersion(installation.Directory);
            if (!GameCompatible(installation.Directory)) return Offline(UnsupportedMessage(version));
            if (!RuntimeReady(installation.Directory)) return Offline("GTA V Enhanced runtime setup is required. Close the game and choose Install / update.");
            var processes = ProcessProvider(installation.Directory);
            if (processes.Length != 1) return Offline("Start GTA V Enhanced Story Mode to connect.");
            return ReadSnapshotFile(SafePath(installation.Directory, BridgeRoot + "status.json"), processes[0].Started, processes[0].Id);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) {
            return Offline("Waiting for GTA V Enhanced Story Mode trainer status.");
        }
    }

    internal static Snapshot ReadSnapshotFile(string path, DateTime processStarted, int processId)
    {
        // Brief atomic publication gaps must not invalidate an outstanding request.
        for (var attempt = 0; attempt < 3; attempt++) {
            try {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buffer = new byte[16385];
                var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                if (count > 16384) return Offline("Invalid GTA V Enhanced trainer status.");
                using var document = JsonDocument.Parse(buffer.AsMemory(0, count));
                return ParseSnapshot(document.RootElement, File.GetLastWriteTimeUtc(stream.SafeFileHandle), processStarted, processId, DateTime.UtcNow);
            } catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) {
                if (attempt < 2) Thread.Sleep(20);
            }
        }
        return Offline("Waiting for GTA V Enhanced Story Mode trainer status.");
    }

    internal static Snapshot ParseSnapshot(JsonElement json, DateTime modified, DateTime processStarted, int processId, DateTime now)
    {
        if (modified < processStarted || now - modified > TimeSpan.FromSeconds(6) || modified - now > TimeSpan.FromSeconds(2))
            return Offline("Waiting for a fresh GTA V Enhanced trainer heartbeat.");
        var session = Text(json, "session_id");
        var nowSeconds = new DateTimeOffset(now.ToUniversalTime()).ToUnixTimeSeconds();
        var startSeconds = new DateTimeOffset(processStarted.ToUniversalTime()).ToUnixTimeSeconds();
        if (Text(json, "schema_version") != "1" || Text(json, "version") != TrainerVersion || !HexId(session)
            || Text(json, "process_id") != processId.ToString(CultureInfo.InvariantCulture)
            || !long.TryParse(Text(json, "updated_utc"), NumberStyles.None, CultureInfo.InvariantCulture, out var updated)
            || updated < startSeconds || updated < nowSeconds - 6 || updated > nowSeconds + 2
            || Object(json, "online").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || Object(json, "ready").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || Object(json, "available").ValueKind != JsonValueKind.Object || Object(json, "enabled").ValueKind != JsonValueKind.Object)
            return Offline("GTA V Enhanced trainer identity or heartbeat is invalid. Restart Story Mode.");
        var online = Bool(json, "online");
        var ready = !online && Bool(json, "ready");
        var values = Strings(Object(json, "values"));
        values["gta_online"] = online.ToString();
        var availability = Bools(Object(json, "available"));
        foreach (var id in FeatureIds) values["available_" + id] = (!online && availability.TryGetValue(id, out var value) && value).ToString();
        var enabled = Bools(Object(json, "enabled"));
        if (online) enabled.Clear();
        return new(new TrainerStatus(true, ready, online ? "GTA Online is unsupported. Story Mode controls are disabled."
            : Text(json, "message", "GTA V Enhanced Story Mode trainer connected."), session, enabled, values, Text(json, "last_command")),
            Bool(json, "last_success"), Text(json, "last_message"));
    }

    internal static Dictionary<string, object> CreateCommand(TrainerStatus current, TrainerStatus requested, string action, bool? enabled, string requestId, long createdUtc)
    {
        if (!current.Connected || current.SessionId != requested.SessionId || !HexId(current.SessionId))
            throw new InvalidOperationException("GTA V Enhanced session changed. Reconnect before trying again.");
        if (!current.Values.TryGetValue("gta_online", out var online) || !string.Equals(online, bool.FalseString, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("GTA Online is unsupported. Story Mode controls are disabled.");
        if (!HexId(requestId) || createdUtc <= 0) throw new ArgumentException("Invalid GTA V Enhanced command identity or time.");
        if (!FeatureIds.Contains(action) && action != "disable_all") throw new ArgumentException("Unknown GTA V Enhanced trainer control.");
        if (IsToggle(action) != enabled.HasValue) throw new ArgumentException("Only toggle controls require an explicit enabled state.");
        var cleanup = action == "disable_all" || IsToggle(action) && enabled == false;
        if (!cleanup && (!current.Ready || !current.Values.TryGetValue("available_" + action, out var available)
            || !string.Equals(available, bool.TrueString, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This GTA V Enhanced control is unavailable in the current Story Mode session.");
        var command = new Dictionary<string, object> {
            ["schema_version"] = 1, ["session_id"] = current.SessionId, ["request_id"] = requestId,
            ["action"] = action, ["created_utc"] = createdUtc
        };
        if (enabled.HasValue) command["enabled"] = enabled.Value;
        return command;
    }

    internal static bool AcceptAcknowledgement(Snapshot next, string session, string requestId)
    {
        if (!next.Status.Connected) return false;
        if (next.Status.SessionId != session) throw new IOException("GTA V Enhanced session changed while waiting for the command.");
        if (!next.Status.Values.TryGetValue("gta_online", out var online) || online != bool.FalseString)
            throw new InvalidOperationException("GTA Online is unsupported. Story Mode controls are disabled.");
        if (next.Status.LastCommand != requestId) return false;
        if (!next.LastSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(next.LastMessage) ? "GTA V Enhanced rejected this control." : next.LastMessage);
        return true;
    }

    public async Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous game command.");
        string? path = null, contents = null;
        try {
            var fresh = ReadSnapshot(installation).Status;
            var id = Guid.NewGuid().ToString("N");
            contents = JsonSerializer.Serialize(CreateCommand(fresh, status, featureId, enabled, id, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            path = SafePath(installation.Directory, BridgeRoot + "command.json");
            if (!Directory.Exists(Path.GetDirectoryName(path))) throw new IOException("The GTA V Enhanced trainer command directory is unavailable.");
            var temporary = SafePath(installation.Directory, BridgeRoot + id + ".tmp");
            try {
                await File.WriteAllTextAsync(temporary, contents, new UTF8Encoding(false));
                File.Move(temporary, path); // Never replace an unacknowledged action.
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            for (var attempt = 0; attempt < 32; attempt++) {
                await Task.Delay(250);
                if (AcceptAcknowledgement(ReadSnapshot(installation), fresh.SessionId, id)) return;
            }
            throw new TimeoutException("No GTA V Enhanced acknowledgement received. This action may have applied; it was not retried.");
        } finally {
            // Remove only this exact command; a replacement from another writer belongs to it.
            try { if (path != null && contents != null && File.Exists(path) && new FileInfo(path).Length <= 4096 && File.ReadAllText(path) == contents) File.Delete(path); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            commandLock.Release();
        }
    }
}
