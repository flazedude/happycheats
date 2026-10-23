using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class Rdr2Module(string baseDirectory) : IGameModule
{
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string Executable = "RDR2.exe";
    public const string SupportedExecutableVersion = "1.0.1491.50";
    public const string TrainerVersion = "0.1.0";
    public const string BridgeRoot = "HappyCheatsRdr2/bridge/";
    private readonly string package = Path.Combine(baseDirectory, "payload", "red-dead-redemption-2");
    private readonly SemaphoreSlim commandLock = new(1, 1);
    internal Func<string, string> VersionProvider { get; init; } = path => {
        var version = FileVersionInfo.GetVersionInfo(path);
        return $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.{version.FilePrivatePart}";
    };
    internal Func<string, (int Id, DateTime Started)[]> ProcessProvider { get; init; } = directory => Processes(directory, Executable);
    internal Func<string, Rdr2RuntimePackage.Installation> RuntimeInspector { get; init; } = Rdr2RuntimePackage.Inspect;
    internal Func<IProgress<string>?, Task<Rdr2RuntimePackage.VerifiedRuntime>> RuntimeDownloader { get; init; } = progress => Rdr2RuntimePackage.DownloadAsync(progress);
    internal static readonly string[] FeatureIds = { "rdr2_health", "rdr2_stamina", "rdr2_deadeye", "rdr2_ammo", "rdr2_horse_health", "rdr2_horse_stamina", "rdr2_clear_wanted" };
    internal static readonly string[] HookExports = {
        "?nativeInit@@YAX_K@Z", "?nativePush64@@YAX_K@Z", "?nativeCall@@YAPEA_KXZ",
        "?scriptRegister@@YAXPEAUHINSTANCE__@@P6AXXZ@Z", "?scriptWait@@YAXK@Z", "?scriptUnregister@@YAXPEAUHINSTANCE__@@@Z"
    };

    public GameDefinition Definition { get; } = new("red-dead-redemption-2", "Red Dead Redemption 2", "STORY MODE", "1174180", "R2",
        "Story Mode controls for health, stamina, Dead Eye, loaded ammo and your mounted horse on PC version 1.0.1491.50. Install / update sets up verified Script Hook RDR2 and its ASI loader automatically; first setup needs internet. Red Dead Online is unavailable. Gameplay validation is pending.",
        new FeatureDefinition[] {
            new("rdr2_health", "Unlimited health", "Refill player health and health core while enabled. This is not invincibility; lethal damage can still be fatal.", "", true, "Story Mode · Player"),
            new("rdr2_stamina", "Unlimited stamina", "Refill player stamina and stamina core while enabled.", "", true, "Story Mode · Player"),
            new("rdr2_deadeye", "Unlimited Dead Eye", "Refill Dead Eye and its core while enabled.", "", true, "Story Mode · Player"),
            new("rdr2_ammo", "Unlimited loaded ammo", "Enable infinite ammo in the current weapon's clip while enabled. Does not grant weapons or inventory ammo.", "", true, "Story Mode · Player"),
            new("rdr2_horse_health", "Unlimited horse health", "Refill the currently mounted horse's health and core. Mount changes reset this control; lethal damage can still be fatal.", "", true, "Story Mode · Horse"),
            new("rdr2_horse_stamina", "Unlimited horse stamina", "Refill the currently mounted horse's stamina and core. Mount changes reset this control.", "", true, "Story Mode · Horse"),
            new("rdr2_clear_wanted", "Clear current wanted pursuit", "Clear current wanted score and bounty-hunter pursuit once. Does not erase saved bounties.", "", false, "Story Mode · Player")
        });

    internal static bool IsSupportedVersion(string? version) => version == SupportedExecutableVersion;

    private bool CompatibleExecutable(string root)
    {
        var path = SafePath(root, Executable);
        if (!File.Exists(path)) return false;
        return IsSupportedVersion(VersionProvider(path));
    }

    // Inspect native exports without loading or running code from the user's game folder.
    internal static bool HasNativeExports(string path, IReadOnlyCollection<string> required)
    {
        try {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var headers = pe.PEHeaders;
            if (headers.CoffHeader.Machine != Machine.Amd64 || (headers.CoffHeader.Characteristics & Characteristics.Dll) == 0
                || headers.PEHeader?.Magic != PEMagic.PE32Plus) return false;
            var directory = headers.PEHeader.ExportTableDirectory;
            if (directory.RelativeVirtualAddress == 0 || directory.Size < 40) return false;
            var exports = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader(0, 40);
            exports.Offset = 24;
            var nameCount = exports.ReadUInt32();
            exports.Offset = 32;
            var namesRva = exports.ReadInt32();
            if (nameCount > 4096 || nameCount < required.Count || namesRva <= 0) return false;
            var names = pe.GetSectionData(namesRva).GetReader(0, checked((int)nameCount * 4));
            var found = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < nameCount; index++) {
                var rva = names.ReadInt32();
                if (rva <= 0) return false;
                var bytes = pe.GetSectionData(rva).GetContent(0, Math.Min(512, pe.GetSectionData(rva).Length));
                var terminator = bytes.IndexOf((byte)0);
                if (terminator < 0) return false;
                found.Add(Encoding.ASCII.GetString(bytes.AsSpan(0, terminator)));
            }
            return required.All(found.Contains);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException or OverflowException) { return false; }
    }

    internal static bool HasPrerequisites(string root) => HasNativeExports(SafePath(root, "ScriptHookRDR2.dll"), HookExports)
        && HasNativeExports(SafePath(root, "dinput8.dll"), new[] { "DirectInput8Create" });

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var found = FindSteam(Definition.AppId, Executable, directory);
            var compatible = CompatibleExecutable(found.Directory);
            var runtime = RuntimeInspector(found.Directory);
            var installed = Rdr2Installer.Installed(found.Directory);
            var matching = false;
            if (installed) {
                try { matching = Rdr2Installer.MatchesPackage(package, found.Directory); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            }
            return new GameInstallation(found.Directory, found.Build, true, installed, ProcessProvider(found.Directory).Length > 0,
                compatible && runtime.Status != Rdr2RuntimePackage.State.Conflict,
                !compatible ? "This Red Dead Redemption 2 executable needs a compatibility check. Supported file version: " + SupportedExecutableVersion + ". Game files were preserved."
                : runtime.Status != Rdr2RuntimePackage.State.Ready ? runtime.Message
                : installed && !matching ? "Red Dead Redemption 2 trainer update required. Installed files differ from the current module. Story Mode only."
                : "Experimental Red Dead Redemption 2 Story Mode trainer" + (installed ? " installed" : " available") + ". Red Dead Online is unavailable. Gameplay validation is pending.");
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or BadImageFormatException) {
            return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message);
        }
    });

    private void CheckClosed(string directory)
    {
        if (ProcessProvider(directory).Length > 0) throw new IOException("Close Red Dead Redemption 2 before changing trainer files.");
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(async () => {
        CheckClosed(installation.Directory);
        if (!installation.IsInstalled || !CompatibleExecutable(installation.Directory)) throw new IOException("Unsupported Red Dead Redemption 2 executable. Game files were preserved.");
        var runtime = RuntimeInspector(installation.Directory);
        if (runtime.Status == Rdr2RuntimePackage.State.Conflict) throw new IOException(runtime.Message);
        // Validate our payload before any network request, and repeat validation
        // inside the install transaction after the download has completed.
        _ = Rdr2Installer.ReadPackage(package);
        Rdr2RuntimePackage.VerifiedRuntime? download = null;
        if (runtime.NeedsDownload) download = await RuntimeDownloader(progress).ConfigureAwait(false);
        void CheckInstallation() {
            CheckClosed(installation.Directory);
            if (!CompatibleExecutable(installation.Directory)) throw new IOException("RDR2 changed during setup. No further files were installed.");
            var state = RuntimeInspector(installation.Directory);
            if (state.Status == Rdr2RuntimePackage.State.Conflict) throw new IOException(state.Message);
            if (download == null && state.NeedsDownload) throw new IOException("Script Hook RDR2 changed during setup. Choose Install / update again.");
        }
        CheckInstallation();
        Rdr2Installer.Install(package, installation.Directory, CheckInstallation, progress, download);
    });

    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() =>
        Rdr2Installer.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));

    public Task LaunchAsync(GameInstallation installation)
    {
        if (!installation.IsInstalled) throw new IOException("Install Red Dead Redemption 2 or select its folder first.");
        return LaunchSteam(Definition.AppId);
    }

    internal sealed record Snapshot(TrainerStatus Status, bool LastSuccess, string LastMessage);

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadSnapshot(installation).Status);

    private Snapshot ReadSnapshot(GameInstallation installation)
    {
        try {
            if (!installation.IsInstalled) return new(TrainerStatus.Offline(installation.Message), false, "");
            var runtime = RuntimeInspector(installation.Directory);
            if (!CompatibleExecutable(installation.Directory)) return new(TrainerStatus.Offline(installation.Message), false, "");
            if (runtime.Status != Rdr2RuntimePackage.State.Ready) return new(TrainerStatus.Offline(runtime.Message), false, "");
            var processes = ProcessProvider(installation.Directory);
            if (processes.Length != 1) return new(TrainerStatus.Offline("Launch Red Dead Redemption 2 and load Story Mode to connect. Red Dead Online is unavailable."), false, "");
            if (!HasPrerequisites(installation.Directory) || !Rdr2Installer.MatchesPackage(package, installation.Directory))
                return new(TrainerStatus.Offline("Close the game and verify its version, Script Hook prerequisites and current trainer installation."), false, "");
            var path = SafePath(installation.Directory, BridgeRoot + "status.json");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return ReadSnapshotStream(stream, processes[0].Started, processes[0].Id, DateTime.UtcNow);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or BadImageFormatException) {
            return new(TrainerStatus.Offline("Waiting for the Red Dead Redemption 2 Story Mode trainer: " + error.Message), false, "");
        }
    }

    internal static Snapshot ReadSnapshotStream(FileStream stream, DateTime processStarted, int processId, DateTime now)
    {
        var buffer = new byte[16_385];
        var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (count > 16_384) return new(TrainerStatus.Offline("Invalid Red Dead Redemption 2 trainer status."), false, "");
        using var document = JsonDocument.Parse(buffer.AsMemory(0, count));
        return ParseSnapshot(document.RootElement, File.GetLastWriteTimeUtc(stream.SafeFileHandle), processStarted, processId, now);
    }

    internal static Snapshot ParseSnapshot(JsonElement json, DateTime modified, DateTime processStarted, int processId, DateTime now)
    {
        Snapshot Offline(string message) => new(TrainerStatus.Offline(message), false, "");
        var session = Text(json, "session_id");
        if (modified < processStarted || now - modified > TimeSpan.FromSeconds(6) || modified - now > TimeSpan.FromSeconds(2)
            || !Object(json, "updated_utc").TryGetInt64Value(out var updated)
            || updated < new DateTimeOffset(processStarted.ToUniversalTime()).ToUnixTimeSeconds()
            || new DateTimeOffset(now.ToUniversalTime()).ToUnixTimeSeconds() - updated is > 6 or < -2)
            return Offline("Waiting for a fresh Red Dead Redemption 2 trainer heartbeat.");
        if (Object(json, "schema_version").ValueKind != JsonValueKind.Number || Text(json, "schema_version") != "1"
            || Text(json, "version") != TrainerVersion || !Regex.IsMatch(session, "^[a-f0-9]{32}$", RegexOptions.CultureInvariant)
            || Object(json, "process_id").ValueKind != JsonValueKind.Number || Text(json, "process_id") != processId.ToString(CultureInfo.InvariantCulture)
            || Object(json, "ready").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || Object(json, "online").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return Offline("Restart Red Dead Redemption 2 to load the current Story Mode trainer module.");
        var online = Bool(json, "online");
        var ready = !online && Bool(json, "ready");
        var enabled = new Dictionary<string, bool>();
        var values = new Dictionary<string, string> { ["online"] = online.ToString() };
        foreach (var feature in FeatureIds) {
            var available = Object(Object(json, "available"), feature);
            if (available.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return Offline("Invalid Red Dead Redemption 2 control availability.");
            values["available_" + feature] = (!online && available.ValueKind == JsonValueKind.True).ToString();
            if (!IsToggle(feature)) continue;
            var state = Object(Object(json, "enabled"), feature);
            if (state.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return Offline("Invalid Red Dead Redemption 2 toggle state.");
            enabled[feature] = !online && state.ValueKind == JsonValueKind.True;
        }
        return new(new TrainerStatus(true, ready, online ? "Red Dead Online is unavailable. Return to Story Mode to use this trainer."
            : Text(json, "message", "Red Dead Redemption 2 Story Mode trainer connected."), session, enabled, values, Text(json, "last_command")),
            Bool(json, "last_success"), Text(json, "last_message"));
    }

    private static bool IsToggle(string action) => FeatureIds.Take(6).Contains(action, StringComparer.Ordinal);

    internal static string FormatCommand(TrainerStatus current, TrainerStatus requested, string action, bool? enabled, string requestId, long createdUtc)
    {
        if (!FeatureIds.Contains(action, StringComparer.Ordinal) && action != "disable_all") throw new ArgumentException("Unknown Red Dead Redemption 2 trainer control.", nameof(action));
        var cleanup = action == "disable_all" || IsToggle(action) && enabled == false;
        if (!current.Connected || !cleanup && !current.Ready) throw new InvalidOperationException(current.Message.Length > 0 ? current.Message : "Load Story Mode before using Red Dead Redemption 2 controls.");
        if (current.SessionId != requested.SessionId) throw new InvalidOperationException("Red Dead Redemption 2 session changed. Reconnect before trying again.");
        if (!Regex.IsMatch(current.SessionId, "^[a-f0-9]{32}$", RegexOptions.CultureInvariant) || !Regex.IsMatch(requestId, "^[a-f0-9]{32}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid Red Dead Redemption 2 command identity.");
        if (createdUtc <= 0) throw new ArgumentOutOfRangeException(nameof(createdUtc));
        if (IsToggle(action) && !enabled.HasValue) throw new ArgumentException("An explicit toggle state is required.", nameof(enabled));
        if (!IsToggle(action) && enabled.HasValue) throw new ArgumentException("This action does not accept a toggle state.", nameof(enabled));
        if (!cleanup && (current.Values.TryGetValue("online", out var online) && !string.Equals(online, "false", StringComparison.OrdinalIgnoreCase)
            || !current.Values.TryGetValue("available_" + action, out var available) || !string.Equals(available, "true", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This Story Mode control is unavailable in the current game session. Red Dead Online is unavailable.");
        return JsonSerializer.Serialize(new { schema_version = 1, session_id = current.SessionId, request_id = requestId, action, enabled = enabled == true, created_utc = createdUtc });
    }

    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) => SendCommandAsync(installation, status, featureId, enabled);

    internal static bool AcceptAcknowledgement(Snapshot next, string session, string requestId)
    {
        if (!next.Status.Connected) return false;
        if (next.Status.SessionId != session) throw new IOException("Red Dead Redemption 2 session changed. Reconnect before trying again.");
        if (next.Status.LastCommand != requestId) return false;
        if (!next.LastSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(next.LastMessage) ? "Red Dead Redemption 2 rejected this control." : next.LastMessage);
        return true;
    }

    private async Task SendCommandAsync(GameInstallation installation, TrainerStatus status, string action, bool? enabled)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous game command.");
        string? commandPath = null;
        string? contents = null;
        try {
            var fresh = ReadSnapshot(installation).Status;
            var id = Guid.NewGuid().ToString("N");
            contents = FormatCommand(fresh, status, action, enabled, id, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            commandPath = SafePath(installation.Directory, BridgeRoot + "command.json");
            if (!Directory.Exists(Path.GetDirectoryName(commandPath))) throw new IOException("The Red Dead Redemption 2 trainer command directory is unavailable.");
            var temporary = SafePath(installation.Directory, BridgeRoot + id + ".tmp");
            try {
                await File.WriteAllTextAsync(temporary, contents, new UTF8Encoding(false));
                File.Move(temporary, commandPath); // An unacknowledged command is never overwritten or retried.
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            for (var attempt = 0; attempt < 32; attempt++) {
                await Task.Delay(250);
                if (AcceptAcknowledgement(ReadSnapshot(installation), fresh.SessionId, id)) return;
            }
            throw new TimeoutException("No acknowledgement received. This action may have applied; check the game before repeating it.");
        } finally {
            try {
                if (commandPath != null && contents != null && File.Exists(commandPath) && File.ReadAllText(commandPath) == contents) File.Delete(commandPath);
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            finally { commandLock.Release(); }
        }
    }
}

internal static class Rdr2JsonExtensions
{
    internal static bool TryGetInt64Value(this JsonElement value, out long result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result);
    }
}
