using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class HotelArchitectModule : IGameModule, IFeatureValueModule, IDisposable
{
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string Executable = "HotelArchitect/Binaries/Win64/HotelArchitect-Win64-Shipping.exe";
    public const string SupportedExecutableHash = "E0B6E24E2D0D3CA466B9DD08ED83BC989594B6B17C31CDC3B63F543355621AE8";
    public const string SupportedSteamBuild = "25340122";
    public const string BridgeRoot = "HotelArchitect/Binaries/Win64/ue4ss/Mods/HappyCheatsHotelArchitect/bridge/";
    public const string TrainerVersion = "0.1.0";
    private readonly string package;
    private readonly HotelArchitectAging aging;
    public HotelArchitectModule(string baseDirectory)
    {
        package = Path.Combine(baseDirectory, "payload", "hotel-architect");
        aging = new HotelArchitectAging(IsCurrentWorld);
    }
    private readonly SemaphoreSlim commandLock = new(1, 1);
    private readonly object inspectionLock = new();
    private (string Path, long Length, DateTime Modified, bool Compatible)? inspected;
    private readonly object packageInspectionLock = new();
    private (PackageFileStamp[] Files, bool Matching)? inspectedPackage;

    private sealed record PackageFileStamp(string Path, bool Exists, long Length, DateTime Modified, DateTime Created);

    internal static readonly FeatureDefinition[] Features = {
        new("ha_no_needs", "No needs", "Clear restroom need and refill mood, health and employee energy while enabled.", "F1", true, "People"),
        new("ha_always_noon", "Always noon", "Freeze the hotel's simulation clock at 12:00 while enabled. Time-driven hotel activities are affected.", "F2", true, "Time"),
        new("ha_furniture_condition", "Unlimited furniture condition", "Keep furniture condition and cleanliness full while enabled.", "F3", true, "Rooms and furniture"),
        new("ha_game_speed", "Game speed", "Set the game's speed: 0 paused, 1 normal, 2 faster, 3 fastest.", "", false, "Time", ChoiceValueKey: "ha_game_speed", Number: new FeatureNumber(0, 3, 1)),
        new("ha_days_passed", "Days passed", "Set the hotel's elapsed day counter once.", "", false, "Time", ChoiceValueKey: "ha_days_passed", Number: new FeatureNumber(0, 100_000, 1)),
        new("ha_disable_aging", "Disable aging", "Disable the hotel's native aging setting while enabled. Keep HappyCheats open; turning this off restores the previous setting when still owned.", "", true, "People"),
        new("ha_money", "Money", "Set the loaded hotel's money once.", "", false, "Hotel", ChoiceValueKey: "ha_money", Number: new FeatureNumber(0, 1_000_000_000, 1_000_000)),
        new("ha_stars", "Stars", "Set the loaded hotel's star value once.", "", false, "Hotel", ChoiceValueKey: "ha_stars", Number: new FeatureNumber(0, 5, 5)),
        new("ha_day_number", "Day number", "Set the current calendar day number once.", "", false, "Time", ChoiceValueKey: "ha_day_number", Number: new FeatureNumber(0, 100_000, 1)),
        new("ha_hour", "Hour", "Set the current hour once. Disable Always noon before editing the clock.", "", false, "Time", ChoiceValueKey: "ha_hour", Number: new FeatureNumber(0, 23, 12)),
        new("ha_minute", "Minute", "Set the current minute once. Disable Always noon before editing the clock.", "", false, "Time", ChoiceValueKey: "ha_minute", Number: new FeatureNumber(0, 59, 0)),
        new("ha_total_hours", "Game hours time total", "Set total elapsed game hours once.", "", false, "Time", ChoiceValueKey: "ha_total_hours", Number: new FeatureNumber(0, 1_000_000, 12, 0.01m)),
        new("ha_restroom_need", "Restroom need", "Edit the selected person's restroom need percentage once. Zero means no need.", "", false, "People", ChoiceValueKey: "ha_restroom_need", Number: new FeatureNumber(0, 100, 0, 0.01m, "%")),
        new("ha_mood", "Mood", "Edit the selected person's mood percentage once.", "", false, "People", ChoiceValueKey: "ha_mood", Number: new FeatureNumber(0, 100, 100, 0.01m, "%")),
        new("ha_health", "Health", "Edit the selected person's health percentage once.", "", false, "People", ChoiceValueKey: "ha_health", Number: new FeatureNumber(0, 100, 100, 0.01m, "%")),
        new("ha_xp", "Current XP", "Set the selected employee's current-level XP. Must be below 110 × their level; level 10 requires zero.", "", false, "People", ChoiceValueKey: "ha_xp", Number: new FeatureNumber(0, 989, 100)),
        new("ha_level", "Level", "Edit the selected employee's level once, within the game's level 10 cap.", "", false, "People", ChoiceValueKey: "ha_level", Number: new FeatureNumber(1, 10, 5)),
        new("ha_retirement_days", "Days until retirement", "Edit the selected employee's remaining days until retirement once.", "", false, "People", ChoiceValueKey: "ha_retirement_days", Number: new FeatureNumber(0, 100_000, 1_000)),
        new("ha_energy", "Energy", "Edit the selected employee's energy percentage once.", "", false, "People", ChoiceValueKey: "ha_energy", Number: new FeatureNumber(0, 100, 100, 0.01m, "%")),
        new("ha_room_value_multiplier", "Room value multiplier", "Edit the selected furniture's contribution to room value once.", "", false, "Rooms and furniture", ChoiceValueKey: "ha_room_value_multiplier", Number: new FeatureNumber(0, 100, 1, 0.01m, "×")),
        new("ha_cleanliness", "Cleanliness", "Edit the selected furniture's cleanliness percentage once.", "", false, "Rooms and furniture", ChoiceValueKey: "ha_cleanliness", Number: new FeatureNumber(0, 100, 100, 0.01m, "%")),
        new("ha_attractiveness", "Attractiveness value", "Edit the selected furniture's attractiveness once.", "", false, "Rooms and furniture", ChoiceValueKey: "ha_attractiveness", Number: new FeatureNumber(0, 1_000_000, 100)),
        new("ha_negative_attractiveness", "Negative attractiveness value", "Edit the selected furniture's negative attractiveness once.", "", false, "Rooms and furniture", ChoiceValueKey: "ha_negative_attractiveness", Number: new FeatureNumber(0, 1_000_000, 0)),
        new("ha_condition", "Condition", "Edit the selected furniture's condition percentage once.", "", false, "Rooms and furniture", ChoiceValueKey: "ha_condition", Number: new FeatureNumber(0, 100, 100, 0.01m, "%"))
    };
    public GameDefinition Definition { get; } = new("hotel-architect", "Hotel Architect", "HOTEL SIMULATION", "1602000", "HA",
        "Manage your hotel, people, time and furniture with 24 controls. Experimental Steam module; game startup and gameplay validation are pending.", Features);

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
            var matching = HotelArchitectInstaller.MatchesPackage(package, directory);
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
        var paths = HotelArchitectInstaller.RequiredFiles.Append(HotelArchitectInstaller.ModRoot + "enabled.txt");
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
            var installed = HotelArchitectInstaller.Installed(found.Directory);
            var matching = false;
            if (installed) {
                try { matching = MatchesPackageForDiscovery(found.Directory); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            }
            return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, Executable).Length > 0, compatible,
                !compatible ? "This Hotel Architect build needs a compatibility check."
                : installed ? matching ? "Experimental Hotel Architect trainer installed. Gameplay validation is pending."
                    : "Hotel Architect trainer update required. Installed files differ from the current module."
                : "Hotel Architect Steam build " + SupportedSteamBuild + " detected. Experimental trainer; gameplay validation is pending.");
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) {
            return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message);
        }
    });

    private static void CheckClosed(string directory)
    {
        if (Processes(directory, Executable).Length > 0) throw new IOException("Close Hotel Architect before changing trainer files.");
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        if (!installation.IsInstalled || !Compatible(installation.Directory, true)) throw new IOException("Unsupported Hotel Architect executable. Game files were preserved.");
        HotelArchitectInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
    });

    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() =>
        HotelArchitectInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));

    public Task LaunchAsync(GameInstallation installation)
    {
        if (!installation.IsInstalled) throw new IOException("Install Hotel Architect through Steam first.");
        var registered = FindSteam(Definition.AppId, Executable);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadSnapshot(installation).Status);

    private Snapshot ReadSnapshot(GameInstallation installation)
    {
        if (!installation.IsInstalled) return new(TrainerStatus.Offline(installation.Message), false, "");
        if (!installation.Compatible) return new(TrainerStatus.Offline("This Hotel Architect build needs a compatibility check."), false, "");
        try {
            var processes = Processes(installation.Directory, Executable);
            if (processes.Length != 1) return new(TrainerStatus.Offline("Launch Hotel Architect to connect."), false, "");
            var path = SafePath(installation.Directory, BridgeRoot + "status.json");
            var snapshot = ReadSnapshotFile(path, processes[0].Started, processes[0].Id);
            if (!snapshot.Status.Connected) return snapshot;
            var context = AgingContext(installation.Directory, snapshot.Status, processes[0]);
            var values = new Dictionary<string, string>(snapshot.Status.Values);
            var enabled = new Dictionary<string, bool>(snapshot.Status.Enabled);
            values["available_ha_disable_aging"] = bool.FalseString;
            enabled["ha_disable_aging"] = aging.HasOwnership;
            try {
                var state = aging.Observe(context);
                values["available_ha_disable_aging"] = state.Available.ToString();
                enabled["ha_disable_aging"] = state.Available ? state.Enabled : aging.HasOwnership;
                if (state.Available) values["diagnostic_ha_native_aging_enabled"] = (state.NativeValue == 1).ToString();
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) {
                values["diagnostic_ha_aging"] = error.Message;
            }
            return snapshot with { Status = snapshot.Status with { Values = values, Enabled = enabled } };
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) {
            return new(TrainerStatus.Offline("Waiting for Hotel Architect trainer status."), false, "");
        }
    }

    private static HotelArchitectAging.Context? AgingContext(string directory, TrainerStatus status, (int Id, DateTime Started) process) =>
        status.Ready && status.Values.TryGetValue("diagnostic_ha_world_id", out var world) && Regex.IsMatch(world, "^[1-9][0-9]*:[1-9][0-9]*$", RegexOptions.CultureInvariant)
            ? new HotelArchitectAging.Context(Path.GetFullPath(directory), process.Id, process.Started, world) : null;

    private static bool IsCurrentWorld(HotelArchitectAging.Context context)
    {
        try {
            var processes = Processes(context.Directory, Executable);
            if (processes.Length != 1 || processes[0].Id != context.ProcessId || processes[0].Started != context.Started) return false;
            var current = ReadSnapshotFile(SafePath(context.Directory, BridgeRoot + "status.json"), context.Started, context.ProcessId).Status;
            return current.Connected && current.Ready && current.Values.TryGetValue("diagnostic_ha_world_id", out var world) && world == context.World;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    public void Dispose() => aging.Dispose();

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
        return new(TrainerStatus.Offline("Waiting for Hotel Architect trainer status."), false, "");
    }

    internal static Snapshot ReadSnapshotStream(FileStream stream, DateTime processStarted, int processId, DateTime now)
    {
        // Timestamp and content must describe the same file even if its path is
        // replaced while reading. Limit the bytes read as well as the file size.
        var buffer = new byte[16_385];
        var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (count > 16_384) return new(TrainerStatus.Offline("Invalid Hotel Architect trainer status."), false, "");
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
            return Offline("Waiting for a fresh Hotel Architect trainer heartbeat.");
        var session = Text(json, "session_id");
        var reportedPid = Text(json, "process_id");
        var seconds = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeSeconds();
        if (Text(json, "schema_version") != "1" || Text(json, "version") != TrainerVersion
            || !Regex.IsMatch(session, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant)
            || (reportedPid is not ("" or "0") && reportedPid != processId.ToString(CultureInfo.InvariantCulture))
            || !long.TryParse(Text(json, "updated_utc"), NumberStyles.None, CultureInfo.InvariantCulture, out var updated)
            || updated < seconds - 6 || updated > seconds + 2)
            return Offline("Restart Hotel Architect to load the current trainer module.");
        var values = Strings(Object(json, "values"));
        var availability = Bools(Object(json, "available"));
        foreach (var feature in FeatureIds)
            values["available_" + feature] = (availability.TryGetValue(feature, out var available) && available).ToString();
        return new(new TrainerStatus(true, Bool(json, "ready"), Text(json, "message", "Hotel Architect trainer connected."), session,
            Bools(Object(json, "enabled")), values, Text(json, "last_command")), Bool(json, "last_success"), Text(json, "last_message"));
    }

    internal static readonly string[] FeatureIds = Features.Select(feature => feature.Id).ToArray();

    private static bool IsToggle(string action) => Features.Any(feature => feature.Id == action && feature.IsToggle);
    private static bool IsAmount(string action) => Features.Any(feature => feature.Id == action && feature.Number != null);

    internal static decimal ValidateAmount(string action, decimal? amount)
    {
        var number = Features.SingleOrDefault(feature => feature.Id == action)?.Number
            ?? throw new ArgumentException("This Hotel Architect control does not accept a number.", nameof(action));
        if (!amount.HasValue) throw new ArgumentException("This Hotel Architect control requires a number.", nameof(amount));
        var value = amount.Value;
        if (value < number.Minimum || value > number.Maximum || (value - number.Minimum) % number.Step != 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Choose a number within this control's range and step.");
        return value;
    }

    internal static string FormatCommand(TrainerStatus current, TrainerStatus requested, string action, bool? enabled, string requestId, long createdUtc, decimal? amount = null)
    {
        var disabling = enabled == false && IsToggle(action);
        if (!current.Connected || (!current.Ready && action != "disable_all" && !disabling)) throw new InvalidOperationException(current.Message.Length > 0 ? current.Message : "Load a single-player save before using Hotel Architect controls.");
        if (current.SessionId != requested.SessionId) throw new InvalidOperationException("Hotel Architect session changed. Reconnect before trying again.");
        if (!FeatureIds.Contains(action) && action != "disable_all") throw new ArgumentException("Unknown Hotel Architect trainer control.", nameof(action));
        if (!Regex.IsMatch(current.SessionId, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant) || !Guid.TryParseExact(requestId, "N", out _))
            throw new ArgumentException("Invalid Hotel Architect command identity.");
        if (createdUtc <= 0) throw new ArgumentOutOfRangeException(nameof(createdUtc));
        if (action != "disable_all" && !disabling && (!current.Values.TryGetValue("available_" + action, out var available) || !string.Equals(available, "true", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This Hotel Architect control is unavailable in the current game session.");
        if (IsToggle(action) && !enabled.HasValue) throw new ArgumentException("An explicit toggle state is required.", nameof(enabled));
        if (!IsToggle(action) && enabled.HasValue) throw new ArgumentException("This action does not accept a toggle state.", nameof(enabled));
        if (!IsAmount(action) && amount.HasValue) throw new ArgumentException("This Hotel Architect control does not accept an amount.", nameof(amount));
        var command = $"schema_version=1\nsession_id={current.SessionId}\nrequest_id={requestId}\naction={action}\nenabled={(enabled == true ? "1" : "0")}\ncreated_utc={createdUtc.ToString(CultureInfo.InvariantCulture)}\n";
        return IsAmount(action) ? command + "amount=" + ValidateAmount(action, amount).ToString(CultureInfo.InvariantCulture) + "\n" : command;
    }

    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) =>
        SendCommandAsync(installation, status, featureId, enabled, Features.SingleOrDefault(feature => feature.Id == featureId)?.Number?.DefaultValue);

    public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value) =>
        SendCommandAsync(installation, status, featureId, null, value);

    internal static bool AcceptAcknowledgement(Snapshot next, string session, string requestId)
    {
        // Lua replaces the status file with remove/rename on Windows. A brief
        // publication gap must not fail or resend an already submitted command.
        if (!next.Status.Connected) return false;
        if (next.Status.SessionId != session) throw new IOException("Hotel Architect session changed. Reconnect before trying again.");
        if (next.Status.LastCommand != requestId) return false;
        if (!next.LastSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(next.LastMessage) ? "Hotel Architect rejected this control." : next.LastMessage);
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
            Exception? agingFailure = null;
            if (action is "ha_disable_aging" or "disable_all") {
                var processes = Processes(installation.Directory, Executable);
                var context = processes.Length == 1 ? AgingContext(installation.Directory, fresh, processes[0]) : null;
                try {
                    if (context != null) aging.Set(context, action == "ha_disable_aging" && enabled == true);
                    else if (action == "disable_all" || enabled == false) aging.RestoreOwned();
                    else throw new InvalidOperationException("Load a hotel before changing its aging setting.");
                } catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) {
                    if (action == "ha_disable_aging") throw;
                    agingFailure = error; // Still turn off all Lua controls if native cleanup is unavailable.
                }
                if (action == "ha_disable_aging") return;
            }
            commandPath = SafePath(installation.Directory, BridgeRoot + "command.txt");
            if (!Directory.Exists(Path.GetDirectoryName(commandPath))) throw new IOException("The Hotel Architect trainer command directory is unavailable.");
            var temporary = SafePath(installation.Directory, BridgeRoot + id + ".tmp");
            try {
                await File.WriteAllTextAsync(temporary, contents, new UTF8Encoding(false));
                File.Move(temporary, commandPath); // Never overwrite a command whose result is still unknown.
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            for (var attempt = 0; attempt < 32; attempt++) {
                await Task.Delay(250);
                var next = ReadSnapshot(installation);
                if (AcceptAcknowledgement(next, fresh.SessionId, id)) {
                    if (agingFailure != null) throw new IOException("Lua controls are off, but the aging setting could not be restored.", agingFailure);
                    return;
                }
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
