using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class AstroneerModule(string baseDirectory) : IGameModule, IFeatureChoiceModule
{
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string Executable = "Astro/Binaries/Win64/Astro-Win64-Shipping.exe";
    public const string SupportedExecutableHash = "4A57B4B1AC64DEFA09EA6D6C987D5E513D79243A2B33AB05CBA099AD1E377675";
    public const string Patch44ExecutableHash = "D4FA5EA711A12F3FE87E38F7D385AE4328B2EFC7D8C8B9B0BF495C20CAC54B27";
    public const string LegacyExecutableHash = "E1764F108E51BB172148F3A4AC71AE38AC031D9ECEB41AD213E139A9CBA61F56";
    public const string BridgeRoot = "Astro/Binaries/Win64/ue4ss/Mods/HappyCheatsAstroneer/bridge/";
    public const string TrainerVersion = "0.3.1";
    internal sealed record CompatibilityProfile(string GameVersion, string SteamBuild, string ExecutableSha256);
    private static readonly CompatibilityProfile[] Profiles = {
        new("1.43.2.0", "24409322", LegacyExecutableHash),
        new("1.44.4", "25119744", Patch44ExecutableHash),
        new("1.45.1", "25599237", SupportedExecutableHash)
    };
    private readonly string package = Path.Combine(baseDirectory, "payload", "astroneer");
    private readonly SemaphoreSlim commandLock = new(1, 1);
    private readonly object inspectionLock = new();
    private (string Path, long Length, DateTime Modified, CompatibilityProfile? Profile)? inspected;

    public GameDefinition Definition { get; } = new("astroneer", "ASTRONEER", "SPACE EXPLORATION", "361420", "AS",
        "Explore at your own pace. Choose terrain resource and running multipliers, prevent damage, maintain oxygen and backpack power, and add research bytes. Experimental module; gameplay validation is pending.",
        new FeatureDefinition[] {
            new("resource_collection_10x", "Resource collection", "Choose 10×, 20× or 100× resource yield from terrain mining, then enable collection. Existing inventory transfers are unchanged.", "F1", true, "Resources",
                new FeatureChoice[] { new("10", "10×"), new("20", "20×"), new("100", "100×") }, "selected_collection_multiplier"),
            new("unlimited_oxygen", "Unlimited oxygen", "Keep the local astronaut's oxygen supply full while enabled.", "F2", true, "Survival"),
            new("unlimited_power", "Unlimited backpack power", "Keep the local astronaut's backpack battery full while enabled.", "F3", true, "Survival"),
            new("add_bytes", "Add 10,000 bytes", "Add 10,000 research bytes through the game's research system.", "F4", false, "Research"),
            new("invulnerability", "Invulnerability", "Block normal damage to the local astronaut while enabled. Forced-death events may still apply.", "F5", true, "Survival"),
            new("player_run_speed", "Run speed", "Choose 3×, 5× or 10× on-foot running speed, then enable it. Other movement modes use normal speed; the selected rate resumes on foot. Disabling restores the original speed.", "F6", true, "Movement",
                new FeatureChoice[] { new("3", "3×"), new("5", "5×"), new("10", "10×") }, "selected_run_speed_multiplier")
        });

    internal static CompatibilityProfile? ProfileForHash(string? hash) =>
        Profiles.FirstOrDefault(profile => string.Equals(hash, profile.ExecutableSha256, StringComparison.OrdinalIgnoreCase));

    internal static string InspectionMessage(CompatibilityProfile? profile, bool installed, bool matching)
    {
        if (profile == null) return "This ASTRONEER build needs a compatibility check.";
        var evidence = $"ASTRONEER {profile.GameVersion} detected (inspected Steam build {profile.SteamBuild}). ";
        return evidence + (installed
            ? matching ? "Experimental ASTRONEER trainer installed. Gameplay validation is pending."
                : "ASTRONEER trainer update required. Installed files differ from the current module."
            : "Experimental trainer; gameplay validation is pending.");
    }

    private CompatibilityProfile? InspectCompatibility(string directory, bool force = false)
    {
        var path = SafePath(directory, Executable);
        var file = new FileInfo(path);
        if (!file.Exists) return null;
        lock (inspectionLock)
        {
            if (!force && inspected is { } previous && previous.Path == path && previous.Length == file.Length && previous.Modified == file.LastWriteTimeUtc)
                return previous.Profile;
            var profile = ProfileForHash(Hash(path));
            inspected = (path, file.Length, file.LastWriteTimeUtc, profile);
            return profile;
        }
    }

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var found = FindSteam(Definition.AppId, Executable, directory);
            var profile = InspectCompatibility(found.Directory);
            var installed = AstroneerInstaller.Installed(found.Directory);
            var matching = false;
            if (installed) {
                try { matching = AstroneerInstaller.MatchesPackage(package, found.Directory); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            }
            return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, Executable).Length > 0, profile != null,
                InspectionMessage(profile, installed, matching));
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) {
            return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message);
        }
    });

    private static void CheckClosed(string directory)
    {
        if (Processes(directory, Executable).Length > 0) throw new IOException("Close ASTRONEER before changing trainer files.");
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        if (!installation.IsInstalled || InspectCompatibility(installation.Directory, true) == null) throw new IOException("Unsupported ASTRONEER executable. Game files were preserved.");
        AstroneerInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
    });

    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() =>
        AstroneerInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));

    public Task LaunchAsync(GameInstallation installation)
    {
        if (!installation.IsInstalled) throw new IOException("Install ASTRONEER through Steam first.");
        var registered = FindSteam(Definition.AppId, Executable);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadSnapshot(installation).Status);

    private Snapshot ReadSnapshot(GameInstallation installation)
    {
        if (!installation.IsInstalled) return new(TrainerStatus.Offline(installation.Message), false, "");
        try {
            // Recheck executable metadata/hash instead of trusting a discovery
            // snapshot retained across a Steam update or manual folder change.
            if (InspectCompatibility(installation.Directory) == null)
                return new(TrainerStatus.Offline("This ASTRONEER build needs a compatibility check."), false, "");
            var processes = Processes(installation.Directory, Executable);
            if (processes.Length != 1) return new(TrainerStatus.Offline("Launch ASTRONEER to connect."), false, "");
            var path = SafePath(installation.Directory, BridgeRoot + "status.json");
            if (!File.Exists(path)) return new(TrainerStatus.Offline("Waiting for the ASTRONEER trainer. Restart the game after installation."), false, "");
            var file = new FileInfo(path);
            if (file.Length > 16_384) return new(TrainerStatus.Offline("Invalid ASTRONEER trainer status."), false, "");
            return ParseSnapshot(ReadJson(path), file.LastWriteTimeUtc, processes[0].Started, processes[0].Id, DateTime.UtcNow);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) {
            return new(TrainerStatus.Offline("Waiting for ASTRONEER trainer status."), false, "");
        }
    }

    internal sealed record Snapshot(TrainerStatus Status, bool LastSuccess, string LastMessage);

    internal static Snapshot ParseSnapshot(JsonElement json, DateTime modified, DateTime processStarted, int processId, DateTime now)
    {
        Snapshot Offline(string message) => new(TrainerStatus.Offline(message), false, "");
        if (modified < processStarted || now - modified > TimeSpan.FromSeconds(6) || modified - now > TimeSpan.FromSeconds(1))
            return Offline("Waiting for a fresh ASTRONEER trainer heartbeat.");
        var session = Text(json, "session_id");
        var reportedPid = Text(json, "process_id");
        if (Text(json, "schema_version") != "1" || Text(json, "version") != TrainerVersion
            || !Regex.IsMatch(session, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant)
            || (reportedPid is not ("" or "0") && reportedPid != processId.ToString(CultureInfo.InvariantCulture)))
            return Offline("Restart ASTRONEER to load the current trainer module.");
        var values = Strings(Object(json, "values"));
        var availability = Bools(Object(json, "available"));
        foreach (var feature in FeatureIds)
            values["available_" + feature] = (availability.TryGetValue(feature, out var available) && available).ToString();
        return new(new TrainerStatus(true, Bool(json, "ready"), Text(json, "message", "ASTRONEER trainer connected."), session,
            Bools(Object(json, "enabled")), values, Text(json, "last_command")), Bool(json, "last_success"), Text(json, "last_message"));
    }

    internal static readonly string[] FeatureIds = { "resource_collection_10x", "unlimited_oxygen", "unlimited_power", "add_bytes", "invulnerability", "player_run_speed" };

    internal static string FormatCommand(TrainerStatus current, TrainerStatus requested, string action, bool? enabled, string requestId, long createdUtc, int? multiplier = null)
    {
        if (!current.Connected || (!current.Ready && action != "disable_all")) throw new InvalidOperationException(current.Message.Length > 0 ? current.Message : "Load a world hosted on this PC before using ASTRONEER controls.");
        if (current.SessionId != requested.SessionId) throw new InvalidOperationException("ASTRONEER session changed. Reconnect before trying again.");
        if (!FeatureIds.Contains(action) && action is not ("disable_all" or "set_collection_multiplier" or "set_run_speed_multiplier")) throw new ArgumentException("Unknown ASTRONEER trainer control.", nameof(action));
        if (!Regex.IsMatch(current.SessionId, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant) || !Guid.TryParseExact(requestId, "N", out _))
            throw new ArgumentException("Invalid ASTRONEER command identity.");
        var availabilityId = action switch {
            "set_collection_multiplier" => "resource_collection_10x",
            "set_run_speed_multiplier" => "player_run_speed",
            _ => action
        };
        if (action != "disable_all" && (!current.Values.TryGetValue("available_" + availabilityId, out var available) || !string.Equals(available, "true", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This ASTRONEER control is unavailable in the current game session.");
        if (action is not ("disable_all" or "add_bytes" or "set_collection_multiplier" or "set_run_speed_multiplier") && !enabled.HasValue) throw new ArgumentException("An explicit toggle state is required.", nameof(enabled));
        if (action is "disable_all" or "add_bytes" or "set_collection_multiplier" or "set_run_speed_multiplier" && enabled.HasValue) throw new ArgumentException("This action does not accept a toggle state.", nameof(enabled));
        if (action == "set_collection_multiplier") {
            if (multiplier is not (10 or 20 or 100)) throw new ArgumentException("Choose a 10×, 20× or 100× resource collection multiplier.", nameof(multiplier));
        } else if (action == "set_run_speed_multiplier") {
            if (multiplier is not (3 or 5 or 10)) throw new ArgumentException("Choose a 3×, 5× or 10× run speed multiplier.", nameof(multiplier));
        } else if (multiplier.HasValue) throw new ArgumentException("This action does not accept a multiplier.", nameof(multiplier));
        var command = $"schema_version=1\nsession_id={current.SessionId}\nrequest_id={requestId}\naction={action}\nenabled={(enabled == true ? "1" : "0")}\ncreated_utc={createdUtc.ToString(CultureInfo.InvariantCulture)}\n";
        return multiplier.HasValue ? command + "multiplier=" + multiplier.Value.ToString(CultureInfo.InvariantCulture) + "\n" : command;
    }

    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) =>
        SendCommandAsync(installation, status, featureId, enabled);

    public Task SelectAsync(GameInstallation installation, TrainerStatus status, string featureId, string choice) =>
        SendCommandAsync(installation, status, ChoiceAction(featureId), null, ChoiceMultiplier(featureId, choice));

    internal static string ChoiceAction(string featureId) => featureId switch {
        "resource_collection_10x" => "set_collection_multiplier",
        "player_run_speed" => "set_run_speed_multiplier",
        _ => throw new ArgumentException("This ASTRONEER control does not support a multiplier selection.", nameof(featureId))
    };

    internal static int ChoiceMultiplier(string featureId, string choice)
    {
        _ = ChoiceAction(featureId);
        if (featureId == "player_run_speed") return choice switch {
            "3" => 3, "5" => 5, "10" => 10,
            _ => throw new ArgumentException("Choose a 3×, 5× or 10× run speed multiplier.", nameof(choice))
        };
        return choice switch {
            "10" => 10, "20" => 20, "100" => 100,
            _ => throw new ArgumentException("Choose a 10×, 20× or 100× resource collection multiplier.", nameof(choice))
        };
    }

    internal static bool AcceptAcknowledgement(Snapshot next, string session, string requestId)
    {
        // Lua replaces the status file with remove/rename on Windows. A brief
        // publication gap must not fail or resend an already submitted command.
        if (!next.Status.Connected) return false;
        if (next.Status.SessionId != session) throw new IOException("ASTRONEER session changed. Reconnect before trying again.");
        if (next.Status.LastCommand != requestId) return false;
        if (!next.LastSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(next.LastMessage) ? "ASTRONEER rejected this control." : next.LastMessage);
        return true;
    }

    private async Task SendCommandAsync(GameInstallation installation, TrainerStatus status, string action, bool? enabled, int? multiplier = null)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous game command.");
        string? commandPath = null;
        string? contents = null;
        try {
            var fresh = ReadSnapshot(installation).Status;
            var id = Guid.NewGuid().ToString("N");
            contents = FormatCommand(fresh, status, action, enabled, id, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), multiplier);
            commandPath = SafePath(installation.Directory, BridgeRoot + "command.txt");
            if (!Directory.Exists(Path.GetDirectoryName(commandPath))) throw new IOException("The ASTRONEER trainer command directory is unavailable.");
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
