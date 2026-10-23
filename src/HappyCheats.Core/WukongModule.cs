using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class WukongModule(string baseDirectory) : IGameModule, IFeatureValueModule
{
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string Executable = "b1/Binaries/Win64/b1-Win64-Shipping.exe";
    public const string SupportedExecutableHash = "CBA6127DF85FE6AEDF5A71B398F268C8A963C7B19582C3CAC087462518358E9A";
    public const string SupportedSteamBuild = "21393610";
    public const string BridgeRoot = "b1/Binaries/Win64/ue4ss/Mods/HappyCheatsWukong/bridge/";
    public const string TrainerVersion = "0.1.1";
    private readonly string package = Path.Combine(baseDirectory, "payload", "black-myth-wukong");
    private readonly SemaphoreSlim commandLock = new(1, 1);
    private readonly object inspectionLock = new();
    private (string Path, long Length, DateTime Modified, bool Compatible)? inspected;
    private readonly object packageInspectionLock = new();
    private (PackageFileStamp[] Files, bool Matching)? inspectedPackage;

    private sealed record PackageFileStamp(string Path, bool Exists, long Length, DateTime Modified, DateTime Created);

    public GameDefinition Definition { get; } = new("black-myth-wukong", "Black Myth: Wukong", "ACTION RPG", "2358720", "WK",
        "Health, mana, stamina, damage, gourd and focus controls, plus experience, sparks and will. Experimental Steam module; gameplay validation is pending.",
        new FeatureDefinition[] {
            new("wukong_health", "Unlimited health", "Keep the current player's health topped up while enabled. Lethal hits between refills may still be fatal.", "F1", true, "Survival"),
            new("wukong_mana", "Unlimited mana", "Keep the current player's mana topped up while enabled.", "F2", true, "Survival"),
            new("wukong_stamina", "Unlimited stamina", "Keep the current player's stamina topped up while enabled.", "F3", true, "Survival"),
            new("wukong_damage", "Super damage", "Multiply the current player's attack by 10 while enabled. Disable restores the captured modifier when it is still owned by this trainer. Gameplay changes to the modifier may turn this off.", "F4", true, "Combat"),
            new("wukong_gourd", "Unlimited gourd meter", "Keep the current player's gourd meter full while enabled.", "F5", true, "Survival"),
            new("wukong_focus", "Instant max focus", "Fill the current player's available focus capacity once.", "F6", false, "Combat"),
            new("wukong_xp", "Add experience", "Add the selected amount of experience once. Progression may be saved by the game.", "F7", false, "Character growth", Number: new FeatureNumber(1, 1_000_000, 1_000, Unit: "XP")),
            new("wukong_sparks", "Add sparks", "Add the selected number of sparks once. Progression may be saved by the game.", "F8", false, "Character growth", Number: new FeatureNumber(1, 100, 1, Unit: "sparks")),
            new("wukong_will", "Add will", "Add the selected amount of will once. Added will may be saved by the game.", "F9", false, "Character growth", Number: new FeatureNumber(1, 1_000_000, 1_000, Unit: "will"))
        });

    private bool Compatible(string directory, bool force = false)
    {
        var path = SafePath(directory, Executable);
        var file = new FileInfo(path);
        if (!file.Exists) return false;
        lock (inspectionLock)
        {
            if (!force && inspected is { } previous && previous.Path == path && previous.Length == file.Length && previous.Modified == file.LastWriteTimeUtc)
                return previous.Compatible;
            var compatible = string.Equals(Hash(path), SupportedExecutableHash, StringComparison.OrdinalIgnoreCase);
            inspected = (path, file.Length, file.LastWriteTimeUtc, compatible);
            return compatible;
        }
    }

    internal bool MatchesPackageForDiscovery(string directory)
    {
        lock (packageInspectionLock) {
            var before = PackageFileStamps(directory);
            if (inspectedPackage is { } cached && cached.Files.SequenceEqual(before)) return cached.Matching;
            inspectedPackage = null;
            var matching = WukongInstaller.MatchesPackage(package, directory);
            var after = PackageFileStamps(directory);
            if (!before.SequenceEqual(after)) return false;
            // This cache only avoids repeated discovery hashes. Install and
            // removal still perform the installer's complete checksum checks.
            inspectedPackage = (after, matching);
            return matching;
        }
    }

    private PackageFileStamp[] PackageFileStamps(string directory)
    {
        var paths = WukongInstaller.RequiredFiles.Append(WukongInstaller.ModRoot + "enabled.txt");
        return paths.SelectMany(path => new[] { SafePath(package, path), SafePath(directory, path) })
            .Prepend(SafePath(package, "package.json")).Select(path => {
                var file = new FileInfo(path);
                return file.Exists ? new PackageFileStamp(path, true, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc)
                    : new PackageFileStamp(path, false, 0, default, default);
            }).ToArray();
    }

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var found = FindSteam(Definition.AppId, Executable, directory);
            var compatible = Compatible(found.Directory);
            var installed = WukongInstaller.Installed(found.Directory);
            var matching = false;
            if (installed) {
                try { matching = MatchesPackageForDiscovery(found.Directory); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            }
            return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, Executable).Length > 0, compatible,
                !compatible ? "This Wukong build needs a compatibility check."
                : installed ? matching ? "Experimental Black Myth: Wukong trainer installed. Gameplay validation is pending."
                    : "Black Myth: Wukong trainer update required. Installed files differ from the current module."
                : "Black Myth: Wukong Steam build " + SupportedSteamBuild + " detected. Experimental trainer; gameplay validation is pending.");
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) {
            return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message);
        }
    });

    private static void CheckClosed(string directory)
    {
        if (Processes(directory, Executable).Length > 0) throw new IOException("Close Black Myth: Wukong before changing trainer files.");
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        if (!installation.IsInstalled || !Compatible(installation.Directory, true)) throw new IOException("Unsupported Black Myth: Wukong executable. Game files were preserved.");
        WukongInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
    });

    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() =>
        WukongInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));

    public Task LaunchAsync(GameInstallation installation)
    {
        if (!installation.IsInstalled) throw new IOException("Install Black Myth: Wukong through Steam first.");
        var registered = FindSteam(Definition.AppId, Executable);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadSnapshot(installation).Status);

    private Snapshot ReadSnapshot(GameInstallation installation)
    {
        if (!installation.IsInstalled) return new(TrainerStatus.Offline(installation.Message), false, "");
        if (!installation.Compatible) return new(TrainerStatus.Offline("This Wukong build needs a compatibility check."), false, "");
        try {
            var processes = Processes(installation.Directory, Executable);
            if (processes.Length != 1) return new(TrainerStatus.Offline("Launch Black Myth: Wukong to connect."), false, "");
            var path = SafePath(installation.Directory, BridgeRoot + "status.json");
            return ReadSnapshotFile(path, processes[0].Started, processes[0].Id);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) {
            return new(TrainerStatus.Offline("Waiting for Black Myth: Wukong trainer status."), false, "");
        }
    }

    internal sealed record Snapshot(TrainerStatus Status, bool LastSuccess, string LastMessage);

    internal static Snapshot ReadSnapshotFile(string path, DateTime processStarted, int processId, Action? retryDelay = null)
    {
        // Lua publishes with remove/rename. Retry that brief gap without retaining
        // an old session, availability result, or command acknowledgement.
        for (var attempt = 0; attempt < 3; attempt++) {
            try {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return ReadSnapshotStream(stream, processStarted, processId, DateTime.UtcNow);
            } catch (Exception error) when (error is IOException or JsonException) {
                if (attempt < 2) {
                    if (retryDelay != null) retryDelay(); else Thread.Sleep(20);
                }
            }
        }
        return new(TrainerStatus.Offline("Waiting for Black Myth: Wukong trainer status."), false, "");
    }

    internal static Snapshot ReadSnapshotStream(FileStream stream, DateTime processStarted, int processId, DateTime now)
    {
        // Timestamp and content must describe the same file even if its path is
        // replaced while reading. Limit the bytes read as well as the file size.
        var buffer = new byte[16_385];
        var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (count > 16_384) return new(TrainerStatus.Offline("Invalid Wukong trainer status."), false, "");
        using var document = JsonDocument.Parse(buffer.AsMemory(0, count));
        return ParseSnapshot(document.RootElement, File.GetLastWriteTimeUtc(stream.SafeFileHandle), processStarted, processId, now);
    }

    internal static Snapshot ParseSnapshot(JsonElement json, DateTime modified, DateTime processStarted, int processId, DateTime now)
    {
        Snapshot Offline(string message) => new(TrainerStatus.Offline(message), false, "");
        // Windows can report fresh writes rounded up to the next two-second
        // filesystem timestamp (observed on exFAT). Keep stale/process guards
        // unchanged, but allow that bounded rounding into the future.
        if (modified < processStarted || now - modified > TimeSpan.FromSeconds(6) || modified - now > TimeSpan.FromSeconds(2))
            return Offline("Waiting for a fresh Black Myth: Wukong trainer heartbeat.");
        var session = Text(json, "session_id");
        var reportedPid = Text(json, "process_id");
        if (Text(json, "schema_version") != "1" || Text(json, "version") != TrainerVersion
            || !Regex.IsMatch(session, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant)
            || (reportedPid is not ("" or "0") && reportedPid != processId.ToString(CultureInfo.InvariantCulture)))
            return Offline("Restart Black Myth: Wukong to load the current trainer module.");
        var values = Strings(Object(json, "values"));
        var availability = Bools(Object(json, "available"));
        foreach (var feature in FeatureIds)
            values["available_" + feature] = (availability.TryGetValue(feature, out var available) && available).ToString();
        return new(new TrainerStatus(true, Bool(json, "ready"), Text(json, "message", "Black Myth: Wukong trainer connected."), session,
            Bools(Object(json, "enabled")), values, Text(json, "last_command")), Bool(json, "last_success"), Text(json, "last_message"));
    }

    internal static readonly string[] FeatureIds = { "wukong_health", "wukong_mana", "wukong_stamina", "wukong_damage", "wukong_gourd", "wukong_focus", "wukong_xp", "wukong_sparks", "wukong_will" };

    private static bool IsToggle(string action) => action is "wukong_health" or "wukong_mana" or "wukong_stamina" or "wukong_damage" or "wukong_gourd";
    private static bool IsAmount(string action) => action is "wukong_xp" or "wukong_sparks" or "wukong_will";

    internal static int ValidateAmount(string action, decimal? amount)
    {
        if (!IsAmount(action)) throw new ArgumentException("This Wukong control does not accept an amount.", nameof(action));
        var value = amount ?? (action == "wukong_sparks" ? 1 : 1_000);
        var maximum = action == "wukong_sparks" ? 100 : 1_000_000;
        if (value < 1 || value > maximum || decimal.Truncate(value) != value)
            throw new ArgumentOutOfRangeException(nameof(amount), "Choose a whole-number amount within this control's range.");
        return decimal.ToInt32(value);
    }

    internal static string FormatCommand(TrainerStatus current, TrainerStatus requested, string action, bool? enabled, string requestId, long createdUtc, decimal? amount = null)
    {
        var disabling = enabled == false && IsToggle(action);
        if (!current.Connected || (!current.Ready && action != "disable_all" && !disabling)) throw new InvalidOperationException(current.Message.Length > 0 ? current.Message : "Load a single-player save before using Black Myth: Wukong controls.");
        if (current.SessionId != requested.SessionId) throw new InvalidOperationException("Black Myth: Wukong session changed. Reconnect before trying again.");
        if (!FeatureIds.Contains(action) && action != "disable_all") throw new ArgumentException("Unknown Wukong trainer control.", nameof(action));
        if (!Regex.IsMatch(current.SessionId, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant) || !Guid.TryParseExact(requestId, "N", out _))
            throw new ArgumentException("Invalid Wukong command identity.");
        if (createdUtc <= 0) throw new ArgumentOutOfRangeException(nameof(createdUtc));
        if (action != "disable_all" && !disabling && (!current.Values.TryGetValue("available_" + action, out var available) || !string.Equals(available, "true", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This Wukong control is unavailable in the current game session.");
        if (IsToggle(action) && !enabled.HasValue) throw new ArgumentException("An explicit toggle state is required.", nameof(enabled));
        if (!IsToggle(action) && enabled.HasValue) throw new ArgumentException("This action does not accept a toggle state.", nameof(enabled));
        if (!IsAmount(action) && amount.HasValue) throw new ArgumentException("This Wukong control does not accept an amount.", nameof(amount));
        var command = $"schema_version=1\nsession_id={current.SessionId}\nrequest_id={requestId}\naction={action}\nenabled={(enabled == true ? "1" : "0")}\ncreated_utc={createdUtc.ToString(CultureInfo.InvariantCulture)}\n";
        return IsAmount(action) ? command + "amount=" + ValidateAmount(action, amount).ToString(CultureInfo.InvariantCulture) + "\n" : command;
    }

    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) =>
        SendCommandAsync(installation, status, featureId, enabled);

    public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value) =>
        SendCommandAsync(installation, status, featureId, null, value);

    internal static bool AcceptAcknowledgement(Snapshot next, string session, string requestId)
    {
        // Lua replaces the status file with remove/rename on Windows. A brief
        // publication gap must not fail or resend an already submitted command.
        if (!next.Status.Connected) return false;
        if (next.Status.SessionId != session) throw new IOException("Black Myth: Wukong session changed. Reconnect before trying again.");
        if (next.Status.LastCommand != requestId) return false;
        if (!next.LastSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(next.LastMessage) ? "Black Myth: Wukong rejected this control." : next.LastMessage);
        return true;
    }

    private async Task SendCommandAsync(GameInstallation installation, TrainerStatus status, string action, bool? enabled, decimal? amount = null)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous game command.");
        string? commandPath = null;
        string? contents = null;
        try {
            var fresh = ReadSnapshot(installation).Status;
            var id = Guid.NewGuid().ToString("N");
            contents = FormatCommand(fresh, status, action, enabled, id, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), amount);
            commandPath = SafePath(installation.Directory, BridgeRoot + "command.txt");
            if (!Directory.Exists(Path.GetDirectoryName(commandPath))) throw new IOException("The Wukong trainer command directory is unavailable.");
            var temporary = SafePath(installation.Directory, BridgeRoot + id + ".tmp");
            try {
                await File.WriteAllTextAsync(temporary, contents, new UTF8Encoding(false));
                File.Move(temporary, commandPath); // Never overwrite a command whose result is still unknown.
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            for (var attempt = 0; attempt < 32; attempt++) {
                await Task.Delay(250);
                var next = ReadSnapshot(installation);
                if (AcceptAcknowledgement(next, fresh.SessionId, id)) return;
            }
            throw new TimeoutException("No acknowledgement received. This action may have applied; check the in-game value before repeating it.");
        } finally {
            try {
                if (commandPath != null && contents != null && File.Exists(commandPath) && File.ReadAllText(commandPath) == contents) File.Delete(commandPath);
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            finally { commandLock.Release(); }
        }
    }
}
