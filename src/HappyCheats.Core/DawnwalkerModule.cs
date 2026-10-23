using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class DawnwalkerModule(string baseDirectory) : IGameModule
{
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string Executable = "Dawnwalker/Binaries/Win64/Dawnwalker.exe";
    public const string SupportedExecutableHash = "CB9B7D7BD88A6754C0A9C08318AA64D5013DDFD92D5BADCAE84E1B4EA980DCFC";
    public const string BridgeRoot = "Dawnwalker/Binaries/Win64/ue4ss/Mods/HappyCheatsDawnwalker/bridge/";
    public const string TrainerVersion = "0.3.3";
    private readonly string package = Path.Combine(baseDirectory, "payload", "dawnwalker");
    private readonly SemaphoreSlim commandLock = new(1, 1);
    private readonly object inspectionLock = new();
    private (string Path, long Length, DateTime Modified, bool Compatible)? inspected;
    private readonly object packageInspectionLock = new();
    private (PackageFileStamp[] Files, bool Matching)? inspectedPackage;

    private sealed record PackageFileStamp(string Path, bool Exists, long Length, DateTime Modified, DateTime Created);

    public GameDefinition Definition { get; } = new("dawnwalker", "The Blood of Dawnwalker", "DARK FANTASY RPG", "3751260", "DW",
        "Survival, flight, inventory and XP controls with a three-page controller overlay for windowed or borderless play. New controls are experimental; gameplay validation is pending.",
        new FeatureDefinition[] {
            new("dawnwalker_health", "Infinite health", "Keep health topped up. Does not prevent lethal hits between refills; use Invulnerability to block health damage.", "F1", true, "Survival"),
            new("dawnwalker_stamina", "Infinite stamina", "Keep stamina topped up while enabled. Stamina can still be consumed between refills.", "F2", true, "Survival"),
            new("dawnwalker_refill", "Refill health and stamina", "Restore the current player's health and stamina once without enabling either toggle.", "F3", false, "Survival"),
            new("dawnwalker_blood", "Refill blood continuously", "Periodically fill the player's blood bar and restore damaged blood segments through the game's native setter. These refills may persist in normal saves.", "F4", true, "Survival"),
            new("dawnwalker_invulnerable", "Invulnerability", "Apply the game's invulnerability effect to prevent health damage. Disable removes only the effect created by this trainer.", "F5", true, "Combat & movement"),
            new("dawnwalker_damage", "Super damage", "Multiply the player's base melee, claw, magic and unarmed damage multipliers by 10. Disable restores the captured values while preserving later changes made by the game.", "F8", true, "Combat & movement"),
            new("dawnwalker_invisible", "Invisibility", "Block normal enemy perception and targeting while keeping your character visible. Existing alerts and scripted encounters may persist. Disable restores normal detection.", "F9", true, "Combat & movement"),
            new("dawnwalker_fly", "Fly", "Enable flight with normal directional movement. Space / Left Ctrl or right / left trigger ascend and descend. Disable restores the previous movement mode; collision remains active.", "F11", true, "Combat & movement"),
            new("dawnwalker_teleport", "Teleport to map marker", "Place a custom map marker after loading this trainer, close the map, then teleport once. Requires loaded, walkable ground with space for the player; unsafe destinations are refused.", "F7", false, "Combat & movement"),
            new("dawnwalker_auto_teleport", "Auto teleport", "Enable, then place a new custom map marker to attempt travel once. Close the map if waiting for player control. Enabling does not use an existing marker. Requires loaded, walkable ground with space for the player; unsafe destinations are refused.", "NumPad4", true, "Combat & movement"),
            new("dawnwalker_carry", "Infinite carry weight", "Remove the inventory weight limit while enabled and restore its previous value when disabled.", "F12", true, "Inventory & XP"),
            new("dawnwalker_money", "Infinite money", "Keep at least 999,999 coins using the game's currency API. Added coins remain after disabling and may be saved normally.", "NumPad1", true, "Inventory & XP"),
            new("dawnwalker_items", "Unlimited item use", "Preserve ordinary consumables in your inventory after use. Quest items are excluded; newly acquired items may take two seconds to update.", "NumPad2", true, "Inventory & XP"),
            new("dawnwalker_xp", "Add XP instantly", "Add exactly the XP needed for the next level once. Requires initialized level data and may be unavailable near the level cap. Gained progression may be saved normally.", "NumPad3", false, "Inventory & XP")
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
            var matching = DawnwalkerInstaller.MatchesPackage(package, directory);
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
        var paths = DawnwalkerInstaller.RequiredFiles.Append(DawnwalkerInstaller.ModRoot + "enabled.txt");
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
            var installed = DawnwalkerInstaller.Installed(found.Directory);
            var matching = false;
            if (installed) {
                try { matching = MatchesPackageForDiscovery(found.Directory); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            }
            return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, Executable).Length > 0, compatible,
                !compatible ? "This Dawnwalker build needs a compatibility check."
                : installed ? matching ? "Experimental The Blood of Dawnwalker trainer installed. Gameplay validation is pending."
                    : "The Blood of Dawnwalker trainer update required. Installed files differ from the current module."
                : "The Blood of Dawnwalker Steam build 25232147 detected. Experimental trainer; gameplay validation is pending.");
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) {
            return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message);
        }
    });

    private static void CheckClosed(string directory)
    {
        if (Processes(directory, Executable).Length > 0) throw new IOException("Close The Blood of Dawnwalker before changing trainer files.");
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        if (!installation.IsInstalled || !Compatible(installation.Directory, true)) throw new IOException("Unsupported The Blood of Dawnwalker executable. Game files were preserved.");
        DawnwalkerInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
    });

    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() =>
        DawnwalkerInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));

    public Task LaunchAsync(GameInstallation installation)
    {
        if (!installation.IsInstalled) throw new IOException("Install The Blood of Dawnwalker through Steam first.");
        var registered = FindSteam(Definition.AppId, Executable);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadSnapshot(installation).Status);

    private Snapshot ReadSnapshot(GameInstallation installation)
    {
        if (!installation.IsInstalled) return new(TrainerStatus.Offline(installation.Message), false, "");
        if (!installation.Compatible) return new(TrainerStatus.Offline("This Dawnwalker build needs a compatibility check."), false, "");
        try {
            var processes = Processes(installation.Directory, Executable);
            if (processes.Length != 1) return new(TrainerStatus.Offline("Launch The Blood of Dawnwalker to connect."), false, "");
            var path = SafePath(installation.Directory, BridgeRoot + "status.json");
            return ReadSnapshotFile(path, processes[0].Started, processes[0].Id);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) {
            return new(TrainerStatus.Offline("Waiting for The Blood of Dawnwalker trainer status."), false, "");
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
        return new(TrainerStatus.Offline("Waiting for The Blood of Dawnwalker trainer status."), false, "");
    }

    internal static Snapshot ReadSnapshotStream(FileStream stream, DateTime processStarted, int processId, DateTime now)
    {
        // Timestamp and content must describe the same file even if its path is
        // replaced while reading. Limit the bytes read as well as the file size.
        var buffer = new byte[16_385];
        var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (count > 16_384) return new(TrainerStatus.Offline("Invalid Dawnwalker trainer status."), false, "");
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
            return Offline("Waiting for a fresh The Blood of Dawnwalker trainer heartbeat.");
        var session = Text(json, "session_id");
        var reportedPid = Text(json, "process_id");
        if (Text(json, "schema_version") != "1" || Text(json, "version") != TrainerVersion
            || !Regex.IsMatch(session, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant)
            || (reportedPid is not ("" or "0") && reportedPid != processId.ToString(CultureInfo.InvariantCulture)))
            return Offline("Restart The Blood of Dawnwalker to load the current trainer module.");
        var values = Strings(Object(json, "values"));
        var availability = Bools(Object(json, "available"));
        foreach (var feature in FeatureIds)
            values["available_" + feature] = (availability.TryGetValue(feature, out var available) && available).ToString();
        return new(new TrainerStatus(true, Bool(json, "ready"), Text(json, "message", "The Blood of Dawnwalker trainer connected."), session,
            Bools(Object(json, "enabled")), values, Text(json, "last_command")), Bool(json, "last_success"), Text(json, "last_message"));
    }

    internal static readonly string[] FeatureIds = { "dawnwalker_health", "dawnwalker_stamina", "dawnwalker_refill", "dawnwalker_blood",
        "dawnwalker_invulnerable", "dawnwalker_damage", "dawnwalker_invisible", "dawnwalker_fly", "dawnwalker_teleport", "dawnwalker_auto_teleport", "dawnwalker_carry", "dawnwalker_money", "dawnwalker_items", "dawnwalker_xp" };

    internal static string FormatCommand(TrainerStatus current, TrainerStatus requested, string action, bool? enabled, string requestId, long createdUtc)
    {
        var disabling = enabled == false && FeatureIds.Contains(action) && action is not ("dawnwalker_refill" or "dawnwalker_xp" or "dawnwalker_teleport");
        if (!current.Connected || (!current.Ready && action != "disable_all" && !disabling)) throw new InvalidOperationException(current.Message.Length > 0 ? current.Message : "Load a single-player save before using The Blood of Dawnwalker controls.");
        if (current.SessionId != requested.SessionId) throw new InvalidOperationException("The Blood of Dawnwalker session changed. Reconnect before trying again.");
        if (!FeatureIds.Contains(action) && action != "disable_all") throw new ArgumentException("Unknown Dawnwalker trainer control.", nameof(action));
        if (!Regex.IsMatch(current.SessionId, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant) || !Guid.TryParseExact(requestId, "N", out _))
            throw new ArgumentException("Invalid Dawnwalker command identity.");
        if (createdUtc <= 0) throw new ArgumentOutOfRangeException(nameof(createdUtc));
        if (action != "disable_all" && !disabling && (!current.Values.TryGetValue("available_" + action, out var available) || !string.Equals(available, "true", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This Dawnwalker control is unavailable in the current game session.");
        if (action is not ("disable_all" or "dawnwalker_refill" or "dawnwalker_xp" or "dawnwalker_teleport") && !enabled.HasValue) throw new ArgumentException("An explicit toggle state is required.", nameof(enabled));
        if (action is "disable_all" or "dawnwalker_refill" or "dawnwalker_xp" or "dawnwalker_teleport" && enabled.HasValue) throw new ArgumentException("This action does not accept a toggle state.", nameof(enabled));
        return $"schema_version=1\nsession_id={current.SessionId}\nrequest_id={requestId}\naction={action}\nenabled={(enabled == true ? "1" : "0")}\ncreated_utc={createdUtc.ToString(CultureInfo.InvariantCulture)}\n";
    }

    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) =>
        SendCommandAsync(installation, status, featureId, enabled);

    internal static bool AcceptAcknowledgement(Snapshot next, string session, string requestId)
    {
        // Lua replaces the status file with remove/rename on Windows. A brief
        // publication gap must not fail or resend an already submitted command.
        if (!next.Status.Connected) return false;
        if (next.Status.SessionId != session) throw new IOException("The Blood of Dawnwalker session changed. Reconnect before trying again.");
        if (next.Status.LastCommand != requestId) return false;
        if (!next.LastSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(next.LastMessage) ? "The Blood of Dawnwalker rejected this control." : next.LastMessage);
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
            commandPath = SafePath(installation.Directory, BridgeRoot + "command.txt");
            if (!Directory.Exists(Path.GetDirectoryName(commandPath))) throw new IOException("The Dawnwalker trainer command directory is unavailable.");
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
