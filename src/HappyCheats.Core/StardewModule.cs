using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class StardewModule(string baseDirectory) : IGameModule, IFeatureValueModule
{
    public IReadOnlyList<string> GameExecutables { get; } = [SmapiExecutable, Executable];
    public const string Executable = "Stardew Valley.exe";
    public const string SmapiExecutable = "StardewModdingAPI.exe";
    public const string BridgeRoot = "Mods/HappyCheats.StardewValley/bridge/";
    public const string TrainerVersion = "0.1.1";
    public const string SmapiGuidance = "SMAPI is included. Choose Install / update to install SMAPI and the HappyCheats mod.";
    private readonly string package = Path.Combine(baseDirectory, "payload", "stardew");
    private readonly SemaphoreSlim commandLock = new(1, 1);

    private static readonly FeatureDefinition[] Features = {
        new("stardew_unlimited_health", "Unlimited health", "Keep health full and prevent damage while enabled.", "F1", true, "Player"),
        new("stardew_unlimited_energy", "Unlimited energy", "Keep your farmer's energy full while enabled.", "F2", true, "Player"),
        new("stardew_add_money", "Add money", "Add the chosen amount to your current money.", "", false, "Resources", ChoiceValueKey: "stardew_money", Number: new(1, 100_000_000, 10_000, Unit: "g")),
        new("stardew_easy_crafting", "Easy crafting", "Craft known non-cooking recipes without ingredients. Does not unlock recipes or affect cooking.", "F3", true, "Resources"),
        new("stardew_unlimited_items", "Unlimited use items", "Preserve items on direct use, eating, planting, placing and normal NPC gifting, including the last item. Daily and weekly gift limits remain. Special quest hand-ins, machines, crafting, shops and transfers are excluded.", "F4", true, "Resources"),
        new("stardew_set_health", "Health amount", "Set current health up to your farmer's maximum. Increase max health first to use a higher amount.", "", false, "Player", ChoiceValueKey: "stardew_health", Number: new(1, 10_000, 100)),
        new("stardew_set_max_health", "Max health", "Set your farmer's maximum health.", "", false, "Player", ChoiceValueKey: "stardew_max_health", Number: new(1, 10_000, 100)),
        new("stardew_set_time", "Game time", "Set the clock using HHMM, in ten-minute intervals (600–2550).", "", false, "World", ChoiceValueKey: "stardew_time", Number: new(600, 2550, 1200, 10, "HHMM")),
        new("stardew_unlimited_water", "Unlimited watering can", "Keep water in the watering can while enabled.", "F5", true, "Resources"),
        new("stardew_freeze_time", "Freeze time", "Pause the game clock while enabled.", "F6", true, "World"),
        new("stardew_set_speed", "Movement speed", "Set a movement speed bonus from 0 to 10. Disable all restores the normal bonus.", "", false, "Player", ChoiceValueKey: "stardew_speed", Number: new(0, 10, 0, Unit: "bonus")),
        new("stardew_refill", "Refill health, energy and water", "Restore health, energy and watering cans to their current maximums.", "F7", false, "Player")
    };

    public GameDefinition Definition { get; } = new("stardew", "Stardew Valley", "FARM LIFE", "413150", "SV",
        "Make more time for farm life. Requires Stardew Valley 1.6.15+ in the 1.6 series; SMAPI 4.5.2 is included. Use your own single-player save. Gameplay validation is pending.", Features);

    internal static bool SupportsGameVersion(Version? version) => version is { Major: 1, Minor: 6, Build: >= 15 };

    private static bool CompatibleGame(string directory)
    {
        var assembly = SafePath(directory, "Stardew Valley.dll");
        if (!File.Exists(assembly)) return false;
        try { return SupportsGameVersion(AssemblyName.GetAssemblyName(assembly).Version); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException) { return false; }
    }

    private static (int Id, DateTime Started)[] GameProcesses(string directory) => Processes(directory, SmapiExecutable)
        .Concat(Processes(directory, Executable)).DistinctBy(process => process.Id).ToArray();

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var found = FindSteam("413150", Executable, directory);
            var smapi = SmapiInstaller.Inspect(found.Directory);
            var gameCompatible = CompatibleGame(found.Directory);
            var compatible = gameCompatible && smapi.Status != SmapiInstaller.State.Invalid
                && (smapi.Status == SmapiInstaller.State.Ready || SmapiInstaller.BundleAvailable(package));
            var installed = StardewInstaller.Installed(found.Directory);
            var matching = false;
            if (installed) {
                try { matching = StardewInstaller.MatchesPackage(package, found.Directory); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            }
            return new GameInstallation(found.Directory, found.Build, true, installed, GameProcesses(found.Directory).Length > 0, compatible,
                !gameCompatible ? "This game build needs a compatibility check. Stardew Valley 1.6.15 or later in the 1.6 series is required."
                : smapi.Status == SmapiInstaller.State.Invalid ? SmapiInstaller.InvalidInstallation
                : smapi.Status != SmapiInstaller.State.Ready ? compatible ? SmapiGuidance : SmapiInstaller.MissingBundle
                : installed ? matching
                    ? "Stardew Valley mod installed. Launch with SMAPI and load a single-player save. Gameplay validation is pending."
                    : "Stardew Valley trainer update required. Installed mod files differ from this module."
                : "SMAPI detected. Ready to install the HappyCheats mod; gameplay validation is pending.");
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) {
            return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message);
        }
    });

    private static void CheckClosed(string directory)
    {
        if (GameProcesses(directory).Length > 0) throw new IOException("Close Stardew Valley and SMAPI before changing trainer files.");
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        if (!installation.IsInstalled || !File.Exists(SafePath(installation.Directory, Executable))) throw new IOException("Install Stardew Valley through Steam first.");
        if (!CompatibleGame(installation.Directory)) throw new IOException("Unsupported Stardew Valley game build. Version 1.6.15 or later in the 1.6 series is required.");
        CheckClosed(installation.Directory);
        StardewInstaller.ReadPackage(package); // Reject a damaged trainer payload before the official loader changes the game folder.
        SmapiInstaller.EnsureInstalled(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
        StardewInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
    });

    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() =>
        StardewInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));

    internal static ProcessStartInfo LaunchInfo(string directory)
    {
        if (!File.Exists(SafePath(directory, Executable))) throw new IOException("Install Stardew Valley through Steam first.");
        var executable = SafePath(directory, SmapiExecutable);
        var smapi = SmapiInstaller.Inspect(directory);
        if (smapi.Status != SmapiInstaller.State.Ready) throw new IOException(smapi.Status == SmapiInstaller.State.Invalid ? SmapiInstaller.InvalidInstallation : SmapiGuidance);
        var info = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetFullPath(directory), UseShellExecute = false };
        info.Environment["SteamAppId"] = "413150";
        info.Environment["SteamGameId"] = "413150";
        return info;
    }

    public Task LaunchAsync(GameInstallation installation)
    {
        if (!installation.IsInstalled) throw new IOException("Install Stardew Valley through Steam first.");
        var registered = FindSteam("413150", Executable);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        if (GameProcesses(installation.Directory).Length > 0) throw new IOException("Stardew Valley is already running. Close it before launching with SMAPI.");
        Process.Start(LaunchInfo(installation.Directory));
        return Task.CompletedTask;
    }

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadSnapshot(installation));

    private static TrainerStatus ReadSnapshot(GameInstallation installation)
    {
        if (!installation.IsInstalled) return TrainerStatus.Offline(installation.Message);
        if (!installation.Compatible) return TrainerStatus.Offline(installation.Message);
        try {
            var processes = GameProcesses(installation.Directory);
            if (processes.Length != 1) return TrainerStatus.Offline("Launch Stardew Valley with SMAPI to connect.");
            var path = SafePath(installation.Directory, BridgeRoot + "status.json");
            if (!File.Exists(path)) return TrainerStatus.Offline("Waiting for the HappyCheats SMAPI mod. Restart the game after installation.");
            var file = new FileInfo(path);
            if (file.Length > 16_384) return TrainerStatus.Offline("Invalid Stardew Valley trainer status.");
            return ParseSnapshot(ReadJson(path), file.LastWriteTimeUtc, processes[0].Started, processes[0].Id, DateTime.UtcNow);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) {
            return TrainerStatus.Offline("Waiting for Stardew Valley trainer status.");
        }
    }

    internal static TrainerStatus ParseSnapshot(JsonElement json, DateTime modified, DateTime processStarted, int processId, DateTime now)
    {
        if (modified < processStarted || now - modified > TimeSpan.FromSeconds(6) || modified - now > TimeSpan.FromSeconds(1))
            return TrainerStatus.Offline("Waiting for a fresh Stardew Valley trainer heartbeat.");
        var session = Text(json, "session_id");
        var currentSeconds = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeSeconds();
        if (Text(json, "schema_version") != "1" || Text(json, "plugin_version") != TrainerVersion
            || !Regex.IsMatch(session, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant)
            || Text(json, "process_id") != processId.ToString(CultureInfo.InvariantCulture)
            || !long.TryParse(Text(json, "updated_utc"), NumberStyles.None, CultureInfo.InvariantCulture, out var updated)
            || updated < currentSeconds - 6 || updated > currentSeconds + 1)
            return TrainerStatus.Offline("Restart Stardew Valley with the current HappyCheats SMAPI mod.");
        var values = Strings(Object(json, "values"));
        values["stardew_game_version"] = Text(json, "game_version");
        return new TrainerStatus(true, Bool(json, "ready"), Text(json, "message", "Stardew Valley trainer connected."), session,
            Bools(Object(json, "enabled")), values, Text(json, "last_command"));
    }

    internal static void ValidateValue(string action, decimal value)
    {
        var number = Features.SingleOrDefault(feature => feature.Id == action)?.Number
            ?? throw new ArgumentException("This Stardew Valley control does not accept a number.", nameof(action));
        if (value < number.Minimum || value > number.Maximum || decimal.Truncate(value) != value)
            throw new ArgumentOutOfRangeException(nameof(value), $"Enter a whole number from {number.Minimum} to {number.Maximum}.");
        if (action == "stardew_set_time" && (value % 100 >= 60 || value % 10 != 0))
            throw new ArgumentOutOfRangeException(nameof(value), "Enter HHMM with minutes 00, 10, 20, 30, 40 or 50 (600–2550).");
    }

    internal static string FormatCommand(TrainerStatus current, TrainerStatus requested, string action, bool? enabled, decimal? value, string requestId, long createdUtc)
    {
        if (!current.Connected || !current.Ready) throw new InvalidOperationException(string.IsNullOrWhiteSpace(current.Message) ? "Load a single-player save before using Stardew Valley controls." : current.Message);
        if (current.SessionId != requested.SessionId) throw new InvalidOperationException("Stardew Valley session changed. Reconnect before trying again.");
        if (!Regex.IsMatch(current.SessionId, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant) || !Guid.TryParseExact(requestId, "N", out _) || createdUtc <= 0)
            throw new ArgumentException("Invalid Stardew Valley command identity.");
        var feature = Features.SingleOrDefault(feature => feature.Id == action);
        if (feature == null && action != "disable_all") throw new ArgumentException("Unknown Stardew Valley control.", nameof(action));
        if (feature?.IsToggle == true) {
            if (!enabled.HasValue || value.HasValue) throw new ArgumentException("An explicit toggle state and no number are required.");
        } else {
            if (enabled.HasValue) throw new ArgumentException("This action does not accept a toggle state.", nameof(enabled));
            if (feature?.Number != null) {
                if (!value.HasValue) throw new ArgumentException("This action requires a number.", nameof(value));
                ValidateValue(action, value.Value);
            } else if (value.HasValue) throw new ArgumentException("This action does not accept a number.", nameof(value));
        }
        return JsonSerializer.Serialize(new { schema_version = 1, plugin_version = TrainerVersion, session_id = current.SessionId,
            request_id = requestId, action, enabled, value, created_utc = createdUtc });
    }

    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) =>
        SendCommandAsync(installation, status, featureId, enabled, Features.SingleOrDefault(feature => feature.Id == featureId)?.Number?.DefaultValue);

    public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value)
    {
        ValidateValue(featureId, value);
        return SendCommandAsync(installation, status, featureId, null, value);
    }

    internal static void AcceptAcknowledgement(JsonElement response, string session, string requestId)
    {
        if (Text(response, "schema_version") != "1" || Text(response, "session_id") != session || Text(response, "request_id") != requestId)
            throw new IOException("Mismatched Stardew Valley trainer response.");
        if (!Bool(response, "success")) throw new InvalidOperationException(Text(response, "message", "Stardew Valley rejected this control."));
    }

    private async Task SendCommandAsync(GameInstallation installation, TrainerStatus status, string action, bool? enabled, decimal? value)
    {
        if (!await commandLock.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous game command.");
        string? commandPath = null;
        string? contents = null;
        try {
            var fresh = ReadSnapshot(installation);
            var id = Guid.NewGuid().ToString("N");
            contents = FormatCommand(fresh, status, action, enabled, value, id, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            commandPath = SafePath(installation.Directory, BridgeRoot + "commands/" + id + ".json");
            var responsePath = SafePath(installation.Directory, BridgeRoot + "responses/" + id + ".json");
            if (!Directory.Exists(Path.GetDirectoryName(commandPath)) || !Directory.Exists(Path.GetDirectoryName(responsePath)))
                throw new IOException("The Stardew Valley trainer command directories are unavailable. Restart the game with SMAPI.");
            var temporary = SafePath(installation.Directory, BridgeRoot + "commands/" + id + ".tmp");
            try {
                await File.WriteAllTextAsync(temporary, contents, new UTF8Encoding(false));
                File.Move(temporary, commandPath);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            for (var attempt = 0; attempt < 32; attempt++) {
                await Task.Delay(250);
                var next = ReadSnapshot(installation);
                if (next.Connected && next.SessionId != fresh.SessionId) throw new IOException("Stardew Valley session changed. Reconnect before trying again.");
                if (File.Exists(responsePath)) {
                    var response = ReadJson(responsePath);
                    AcceptAcknowledgement(response, fresh.SessionId, id);
                    File.Delete(responsePath);
                    return;
                }
            }
            throw new TimeoutException("No acknowledgement received. This action may have applied; check the in-game value before repeating it.");
        } finally {
            try { if (commandPath != null && contents != null && File.Exists(commandPath) && File.ReadAllText(commandPath) == contents) File.Delete(commandPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            finally { commandLock.Release(); }
        }
    }
}
