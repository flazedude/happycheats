using System.Globalization;
using System.Text.Json;
using HappyCheats.Core.CivilizationVII;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class CivilizationVIIModule(string baseDirectory) : IGameModule, IFeatureValueModule
{
    public const string TrainerVersion = "0.1.0";
    private readonly string package = Path.Combine(baseDirectory, "payload", "civilization-vii");
    private readonly SemaphoreSlim gate = new(1, 1);
    private Civ7TunerClient? tuner;
    private string session = "";
    private int processId;
    public int ConnectedProcessId => tuner?.IsConnected == true ? processId : 0;
    private static readonly string[] Executables = [
        "Base/Binaries/Win64/Civ7_Win64_DX12_FinalRelease.exe",
        "Base/Binaries/Win64/Civ7_Win64_Vulkan_FinalRelease.exe"];

    private static readonly FeatureDefinition[] Features = [
        new("civ7_movement", "Unlimited movement", "Restore movement for your units while this page is connected. Does not add attacks or restore consumed units.", "F1", true, "Units"),
        new("civ7_units", "Fast build units", "Complete current unit production in your cities while connected. Select production first.", "F2", true, "Production"),
        new("civ7_buildings", "Fast build buildings and wonders", "Complete current building or wonder production in your cities while connected.", "F3", true, "Production"),
        new("civ7_projects", "Fast projects", "Complete current city projects while connected. Repeat projects can finish repeatedly.", "F4", true, "Production"),
        new("civ7_research", "Fast research", "Grant the current technology node while connected. Choose research first; mastery follows the game's rules.", "F5", true, "Progression"),
        new("civ7_civics", "Fast civics", "Grant the current civic node while connected. Choose a civic first; mastery follows the game's rules.", "F6", true, "Progression"),
        new("civ7_damage", "Edit unit damage", "Set damage on your selected unit; zero heals it. Requires your own unit and cannot kill it.", "F7", false, "Selected unit", ChoiceValueKey: "civ7_damage", Number: new(0, 99, 0)),
        new("civ7_restore_moves", "Restore selected unit movement", "Restore your selected unit's normal movement allowance. Does not grant extra attacks.", "F8", false, "Selected unit"),
        new("civ7_xp", "Add unit XP", "Add experience to your selected unit or commander. Promotion rules and game caps still apply.", "F9", false, "Selected unit", ChoiceValueKey: "civ7_xp", Number: new(1, 100000, 100)),
        new("civ7_gold", "Edit gold", "Set your treasury to this amount.", "Shift+F1", false, "Resources", ChoiceValueKey: "civ7_gold", Number: new(0, 100000000, 10000)),
        new("civ7_influence", "Edit influence", "Set your diplomacy influence balance to this amount.", "Shift+F2", false, "Resources", ChoiceValueKey: "civ7_influence", Number: new(0, 1000000, 1000)),
        new("civ7_population", "Add rural population", "Add rural population to your selected settlement. This is an increment, not a total population editor.", "Shift+F3", false, "Selected city", ChoiceValueKey: "civ7_population", Number: new(1, 100, 1)),
        new("civ7_growth", "Complete food queue", "Complete the current food-funded item in your selected settlement. This can be growth or a food-trained unit.", "F10", false, "Selected city"),
        new("civ7_attributes", "Add wildcard attribute points", "Add unspent wildcard leader attribute points. Spend them in the game's attribute screen.", "Shift+F4", false, "Progression", ChoiceValueKey: "civ7_attributes", Number: new(1, 100, 1)),
        new("civ7_golden_age", "Start celebration", "Start a celebration using the game's Golden Age API. Resolve celebration choices in the game.", "F11", false, "Progression")
    ];

    public GameDefinition Definition { get; } = new("civilization-vii", "Civilization VII", "TURN-BASED STRATEGY", "1295660", "VII",
        "Experimental Steam single-player trainer with 15 controls using the local JavaScript Tuner. Install / update enables Tuner; its achievement impact is unverified. Keep this page open for continuous effects and F1–F11 / Shift+F1–F4 shortcuts; F12 disables all. Based on installed Firaxis panels; gameplay validation is pending.", Features);
    private static (string Directory, string Build) FindGame(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory)) directory = NormalizeGameDirectory(directory);
        foreach (var exe in Executables) {
            try { return FindSteam("1295660", exe, directory); }
            catch (IOException) { }
        }
        // Steam discovery can lag a moved installation. A running game's exact executable
        // path is stronger evidence than a missing/stale appmanifest.
        if (directory == null) {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { "Civ7_Win64_DX12_FinalRelease", "Civ7_Win64_Vulkan_FinalRelease" })
                foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
                    using (process) {
                        try {
                            if (process.MainModule?.FileName is { } file) {
                                var root = NormalizeGameDirectory(Path.GetDirectoryName(file)!);
                                if (Executables.Any(exe => string.Equals(Path.GetFullPath(Path.Combine(root, exe)), file, StringComparison.OrdinalIgnoreCase))) roots.Add(root);
                            }
                        } catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException) { }
                    }
            if (roots.Count == 1) return (roots.Single(), "Running game");
        }
        throw new IOException("Civilization VII was not found. Choose its installation root containing Base/Binaries, or install it through Steam.");
    }

    internal static string NormalizeGameDirectory(string directory)
    {
        var path = Path.GetFullPath(directory);
        // Accept the game root or its Base/Binaries/Win64 executable folder.
        for (var level = 0; level <= 3 && path != null; level++, path = Path.GetDirectoryName(path))
            if (Executables.Any(exe => File.Exists(SafePath(path, exe)))) return path;
        return Path.GetFullPath(directory);
    }

    private static (int Id, DateTime Started)[] GameProcesses(string directory) => Executables.SelectMany(exe => Processes(directory, exe)).DistinctBy(p => p.Id).ToArray();
    private static void CheckClosed(string directory)
    {
        // AppOptions.txt is shared by every installation, including another store/build.
        foreach (var name in new[] { "Civ7_Win64_DX12_FinalRelease", "Civ7_Win64_Vulkan_FinalRelease" }) {
            var processes = System.Diagnostics.Process.GetProcessesByName(name);
            try {
                if (processes.Any(process => !process.HasExited)) throw new IOException("Close all Civilization VII installations before changing Tuner settings.");
            } finally { foreach (var process in processes) process.Dispose(); }
        }
    }

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var game = FindGame(directory);
            var running = GameProcesses(game.Directory).Length > 0;
            _ = Civ7Setup.ReadRuntime(package);
            var (installed, message) = ReadSetupStatus();
            return new GameInstallation(game.Directory, game.Build, true, installed, running, true, message);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException) {
            return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message);
        }
    });

    internal static (bool Installed, string Message) ReadSetupStatus(string? optionsPath = null)
    {
        // Saved settings are advisory. Missing/ambiguous profiles, malformed bytes,
        // or lost ownership metadata must not hide an otherwise discovered game.
        try {
            var options = optionsPath ?? Civ7Setup.FindOptions();
            var tunerEnabled = Civ7Setup.IsTunerEnabled(options);
            bool installed;
            try { installed = Civ7Setup.Installed(options); }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { installed = false; }
            return (installed, tunerEnabled
                ? "Tuner is enabled. Load a single-player game and close other Tuner clients."
                : "Install / update enables Civilization VII's Tuner. Achievement behavior is unverified.");
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) {
            return (false, error.Message);
        }
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        var game = FindGame(installation.Directory);
        Civ7Setup.Install(package, Civ7Setup.FindOptions(), () => CheckClosed(game.Directory), progress);
    });

    public async Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null)
    {
        await gate.WaitAsync();
        try {
            CheckClosed(installation.Directory);
            await DisconnectAsync();
            Civ7Setup.Remove(Civ7Setup.FindOptions(), () => CheckClosed(installation.Directory), progress);
        } finally { gate.Release(); }
    }

    public Task LaunchAsync(GameInstallation installation)
    {
        var registered = FindGame(null);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Launch this Civilization VII installation from its own game launcher, then return to HappyCheats.");
        return LaunchSteam("1295660");
    }

    public async Task<TrainerStatus> ReadStatusAsync(GameInstallation installation)
    {
        await gate.WaitAsync();
        try { return await RefreshAsync(installation, tick: true); }
        catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException or TimeoutException or JsonException or InvalidOperationException or UnauthorizedAccessException) {
            await DisconnectAsync();
            return TrainerStatus.Offline(error is System.Net.Sockets.SocketException
                ? "No Civilization VII Tuner connection. Close the game, choose Install / update, restart it and load single-player. Close other Tuner clients."
                : "Civilization VII: " + error.Message);
        } finally { gate.Release(); }
    }

    private async Task DisconnectAsync()
    {
        if (tuner != null) await tuner.DisposeAsync();
        tuner = null; session = ""; processId = 0;
    }

    private async Task<TrainerStatus> RefreshAsync(GameInstallation installation, bool tick)
    {
        if (!installation.IsInstalled) { await DisconnectAsync(); return TrainerStatus.Offline(installation.Message); }
        var processes = GameProcesses(installation.Directory);
        if (processes.Length != 1) { await DisconnectAsync(); return TrainerStatus.Offline("Start Civilization VII and load a single-player game to connect."); }
        if (tuner == null || !tuner.IsConnected || processId != processes[0].Id) {
            await DisconnectAsync();
            // The live, process-owned socket is sufficient proof that Tuner is enabled.
            // Configuration ownership/backups matter for removal, never for connecting.
            tuner = new Civ7TunerClient();
            await tuner.ConnectAsync();
            Civ7EndpointOwnership.RequireConnectionOwner(processes[0].Id, tuner.LocalPort);
            processId = processes[0].Id;
            session = Guid.NewGuid().ToString("N");
            var runtime = Civ7Setup.ReadRuntime(package);
            await tuner.ExecuteAsync(runtime + "\nHappyCheatsCiv7.Init('" + session + "')");
        }
        var code = tick ? "HappyCheatsCiv7.Tick('" + session + "')" : "HappyCheatsCiv7.Status()";
        var status = ParseStatus(await tuner!.ExecuteAsync(code));
        if (status.SessionId != session) throw new IOException("Civilization VII game session changed. Reconnect before using controls.");
        return status;
    }

    internal static TrainerStatus ParseStatus(string json)
    {
        if (json.Length > 65536) throw new IOException("Civilization VII response is too large.");
        using var doc = JsonDocument.Parse(json);
        var state = doc.RootElement;
        if (state.ValueKind != JsonValueKind.Object || Object(state, "schema_version").ValueKind != JsonValueKind.Number
            || !Object(state, "schema_version").TryGetInt32(out var schema) || schema != 1
            || Object(state, "version").ValueKind != JsonValueKind.String || Text(state, "version") != TrainerVersion
            || Object(state, "ready").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || Object(state, "command_success").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new IOException("Invalid Civilization VII trainer status schema or runtime version.");
        var sessionId = Text(state, "session_id");
        if (Object(state, "session_id").ValueKind != JsonValueKind.String || !Guid.TryParseExact(sessionId, "N", out _))
            throw new IOException("Invalid Civilization VII trainer session.");
        foreach (var key in new[] { "message", "last_command", "command_error" })
            if (Object(state, key).ValueKind != JsonValueKind.String || Text(state, key).Length > 4096)
                throw new IOException("Invalid Civilization VII status text.");
        var lastCommand = Text(state, "last_command");
        if (lastCommand.Length > 0 && !Guid.TryParseExact(lastCommand, "N", out _)) throw new IOException("Invalid Civilization VII command identity.");
        var toggles = Object(state, "enabled");
        var rawValues = Object(state, "values");
        if (toggles.ValueKind != JsonValueKind.Object || rawValues.ValueKind != JsonValueKind.Object)
            throw new IOException("Invalid Civilization VII trainer controls.");
        var enabled = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var feature in Features.Where(feature => feature.IsToggle)) {
            var value = Object(toggles, feature.Id);
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new IOException("Invalid Civilization VII toggle state.");
            enabled.Add(feature.Id, value.ValueKind == JsonValueKind.True);
        }
        if (toggles.EnumerateObject().Count() != enabled.Count) throw new IOException("Unexpected Civilization VII toggle state.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in rawValues.EnumerateObject()) {
            if (entry.Name.Length > 128 || entry.Value.ValueKind != JsonValueKind.String || entry.Value.GetString()!.Length > 4096 || values.Count >= 128)
                throw new IOException("Invalid Civilization VII status value.");
            var key = entry.Name.StartsWith("reason_", StringComparison.Ordinal) || entry.Name.StartsWith("error_", StringComparison.Ordinal)
                ? "diagnostic_" + entry.Name : entry.Name;
            if (!values.TryAdd(key, entry.Value.GetString()!)) throw new IOException("Duplicate Civilization VII status value.");
        }
        foreach (var feature in Features)
            if (!values.TryGetValue("available_" + feature.Id, out var value) || value is not ("true" or "false"))
                throw new IOException("Invalid Civilization VII feature availability.");
        var message = Text(state, "message");
        var tickFailure = Features.Select(feature => (Feature: feature, Error: values.GetValueOrDefault("diagnostic_error_" + feature.Id)))
            .FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Error));
        if (tickFailure.Error != null) message += " " + tickFailure.Feature.Name + ": " + tickFailure.Error[..Math.Min(tickFailure.Error.Length, 512)];
        return new TrainerStatus(true, Bool(state, "ready"), message, sessionId, enabled, values, lastCommand);
    }

    internal static void ValidateValue(string id, decimal value)
    {
        var number = Features.SingleOrDefault(f => f.Id == id)?.Number ?? throw new ArgumentException("This control does not accept a number.");
        if (value != decimal.Truncate(value) || value < number.Minimum || value > number.Maximum)
            throw new ArgumentOutOfRangeException(nameof(value), $"Enter a whole number from {number.Minimum} to {number.Maximum}.");
    }

    public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value)
    { ValidateValue(featureId, value); return CommandAsync(installation, status, featureId, null, value); }

    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
    {
        ValidateCommandInput(featureId, enabled, null);
        return CommandAsync(installation, status, featureId, enabled, null);
    }

    internal static string ValidateCommandInput(string feature, bool? enabled, decimal? value)
    {
        if (feature == "disable_all") {
            if (enabled.HasValue || value.HasValue) throw new ArgumentException("Disable all does not accept a value.");
            return "null";
        }
        var definition = Features.SingleOrDefault(item => item.Id == feature) ?? throw new ArgumentException("Unknown Civilization VII control.");
        if (definition.IsToggle) {
            if (!enabled.HasValue || value.HasValue) throw new ArgumentException("An explicit toggle state is required.");
            return enabled.Value ? "true" : "false";
        }
        if (definition.Number == null) {
            if (enabled.HasValue || value.HasValue) throw new ArgumentException("This action does not accept a value.");
            return "null";
        }
        if (enabled.HasValue || !value.HasValue) throw new ArgumentException("Choose a number and Apply for this Civilization VII editor.");
        ValidateValue(feature, value.Value);
        return value.Value.ToString(CultureInfo.InvariantCulture);
    }

    internal static void ValidateCommandState(TrainerStatus fresh, TrainerStatus requested, string feature, bool? enabled)
    {
        var definition = Features.SingleOrDefault(item => item.Id == feature);
        if (definition == null && feature != "disable_all") throw new ArgumentException("Unknown Civilization VII control.");
        var cleanup = feature == "disable_all" || definition?.IsToggle == true && enabled == false;
        if (!fresh.Connected || !Guid.TryParseExact(fresh.SessionId, "N", out _) || fresh.SessionId != requested.SessionId || !cleanup && !fresh.Ready)
            throw new InvalidOperationException("Load a single-player game and reconnect before using this control.");
        if (!cleanup && (!fresh.Values.TryGetValue("available_" + feature, out var available) || available != "true"))
            throw new InvalidOperationException("This Civilization VII control is unavailable. Check its description and select your own unit or city as needed.");
        if (!cleanup) {
            var contextKeys = new List<string> { "player_id", "age" };
            if (feature is "civ7_damage" or "civ7_xp" or "civ7_restore_moves") contextKeys.Add("selected_unit_id");
            if (feature is "civ7_population" or "civ7_growth") contextKeys.Add("selected_city_id");
            foreach (var key in contextKeys)
                if (!fresh.Values.TryGetValue(key, out var actual) || !requested.Values.TryGetValue(key, out var expected) || actual != expected || actual.Length == 0 || actual == "-1")
                    throw new InvalidOperationException("Civilization VII player, Age or selection changed. Wait for the controls to refresh before applying again.");
        }
    }

    private async Task CommandAsync(GameInstallation installation, TrainerStatus requested, string feature, bool? enabled, decimal? number)
    {
        var value = ValidateCommandInput(feature, enabled, number);
        await gate.WaitAsync();
        try {
            var fresh = await RefreshAsync(installation, tick: false);
            ValidateCommandState(fresh, requested, feature, enabled);
            var request = Guid.NewGuid().ToString("N");
            var context = JsonSerializer.Serialize(new[] { "player_id", "age", "selected_unit_id", "selected_city_id" }
                .ToDictionary(key => key, key => fresh.Values.GetValueOrDefault(key, "")));
            var json = await tuner!.ExecuteAsync("HappyCheatsCiv7.Command('" + session + "','" + request + "','" + feature + "'," + value + "," + context + ")");
            using var doc = JsonDocument.Parse(json);
            var next = ParseStatus(json);
            if (next.SessionId != session || next.LastCommand != request) throw new IOException("No matching Civilization VII acknowledgement. The action was not retried.");
            if (!Bool(doc.RootElement, "command_success")) throw new InvalidOperationException(Text(doc.RootElement, "command_error", "Civilization VII rejected this change."));
        } catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException or TimeoutException or JsonException) {
            await DisconnectAsync();
            throw new IOException("Civilization VII command failed; it may have applied and was not retried. " + error.Message, error);
        } finally { gate.Release(); }
    }
}
