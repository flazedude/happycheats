using System.Globalization;
using System.Text.Json;
using HappyCheats.Core.CivilizationVI;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class CivilizationVIModule(string baseDirectory) : IGameModule, IFeatureValueModule
{
    public const string TrainerVersion = "0.1.1";
    private readonly string package = Path.Combine(baseDirectory, "payload", "civilization-vi");
    private readonly SemaphoreSlim gate = new(1, 1);
    private FireTunerClient? tuner;
    private string session = "";
    private int processId;
    public int ConnectedProcessId => tuner?.IsConnected == true ? processId : 0;
    private static readonly string[] Executables = [
        "Base/Binaries/Win64Steam/CivilizationVI.exe", "Base/Binaries/Win64Steam/CivilizationVI_DX12.exe",
        "Base/Binaries/Win64EOS/CivilizationVI.exe", "Base/Binaries/Win64EOS/CivilizationVI_DX12.exe"];

    private static readonly FeatureDefinition[] Features = [
        new("civ6_movement", "Unlimited movement", "Refill your units' movement while this page is connected. Does not add attacks or restore consumed units.", "F1", true, "Units"),
        new("civ6_units", "Fast build units", "Complete current unit production in your cities while connected. Select production first.", "F2", true, "Production"),
        new("civ6_buildings", "Fast build buildings and wonders", "Complete current building or wonder production in your cities while connected.", "F3", true, "Production"),
        new("civ6_districts", "Fast specialized buildings", "Complete current district production in your cities while connected.", "F4", true, "Production"),
        new("civ6_projects", "Fast projects", "Complete current city projects while connected. Repeat projects can finish repeatedly.", "F5", true, "Production"),
        new("civ6_research", "Fast research", "Complete your currently selected technology while connected. Choose research first.", "F6", true, "Progression"),
        new("civ6_civics", "Fast civics", "Complete your currently selected civic while connected. Choose a civic first.", "F7", true, "Progression"),
        new("civ6_damage", "Edit current damage done to unit", "Set damage on your selected unit; zero heals it. Cannot kill a unit with this editor.", "F8", false, "Selected unit", ChoiceValueKey: "civ6_damage", Number: new(0, 99, 0)),
        new("civ6_moves", "Edit number of moves remaining", "Set remaining movement points on your selected unit.", "F9", false, "Selected unit", ChoiceValueKey: "civ6_moves", Number: new(0, 100, 10)),
        new("civ6_can_level", "Edit unit can level up", "Set promotion availability for your selected unit: 1 adds the required XP; 0 attempts to clear one stored promotion. Reports failure if the game cannot match the requested state.", "F10", false, "Selected unit", ChoiceValueKey: "civ6_can_level", Number: new(0, 1, 1)),
        new("civ6_xp", "Edit unit XP", "Set experience on your selected unit. Promotion and experience rules still apply.", "F11", false, "Selected unit", ChoiceValueKey: "civ6_xp", Number: new(0, 100000, 100)),
        new("civ6_level", "Edit unit level", "Unavailable: the supported Lua interface has no verified raw unit-level setter. Use XP and promotions instead.", "", false, "Selected unit", ChoiceValueKey: "civ6_level", Number: new(1, 100, 1)),
        new("civ6_gold", "Edit gold", "Set your treasury to this amount.", "Shift+F1", false, "Resources", ChoiceValueKey: "civ6_gold", Number: new(0, 100000000, 10000)),
        new("civ6_faith", "Edit faith", "Set your civilization's faith to this amount.", "Shift+F2", false, "Resources", ChoiceValueKey: "civ6_faith", Number: new(0, 100000000, 10000)),
        new("civ6_influence", "Edit influence", "Unavailable: raw influence points have no verified setter in the supported Lua interface. Envoys are a different value.", "", false, "Resources", ChoiceValueKey: "civ6_influence", Number: new(0, 1000000, 100)),
        new("civ6_favor", "Edit diplomatic favor", "Set diplomatic favor. Requires the Gathering Storm ruleset and its diplomacy API.", "Shift+F3", false, "Resources", ChoiceValueKey: "civ6_favor", Number: new(0, 1000000, 1000)),
        new("civ6_population", "Edit population", "Set population of your selected city. Select a city you own first.", "Shift+F4", false, "Selected city", ChoiceValueKey: "civ6_population", Number: new(1, 1000, 20)),
        new("civ6_amenities", "Edit amenities bonus", "Unavailable: no verified raw amenities-bonus setter in the supported Lua interface.", "", false, "Selected city", ChoiceValueKey: "civ6_amenities", Number: new(0, 1000, 20)),
        new("civ6_housing", "Edit housing bonus", "Unavailable: no verified raw housing-bonus setter in the supported Lua interface.", "", false, "Selected city", ChoiceValueKey: "civ6_housing", Number: new(0, 1000, 20))
    ];

    public GameDefinition Definition { get; } = new("civilization-vi", "Civilization VI", "TURN-BASED STRATEGY", "289070", "VI",
        "Experimental single-player trainer using the local FireTuner interface. Install / update enables Tuner and disables achievements. Keep this page open for effects and in-game hotkeys: F1–F11, Shift+F1–F4; F12 disables all. Editors apply the entered value. 15 controls implemented; four raw editors unavailable. Gameplay validation is pending.", Features);

    private static (string Directory, string Build) FindGame(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory)) directory = NormalizeGameDirectory(directory);
        foreach (var exe in Executables) {
            try { return FindSteam("289070", exe, directory); }
            catch (IOException) { }
        }
        // Steam discovery can lag a moved installation. A running game's exact executable
        // path is stronger evidence than a missing/stale appmanifest.
        if (directory == null) {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { "CivilizationVI", "CivilizationVI_DX12" })
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
        throw new IOException("Civilization VI was not found. Choose its installation root containing Base/Binaries, or install it through Steam.");
    }

    internal static string NormalizeGameDirectory(string directory)
    {
        var path = Path.GetFullPath(directory);
        // Accept the game root or its Base/Binaries/Win64Steam executable folder.
        for (var level = 0; level <= 3 && path != null; level++, path = Path.GetDirectoryName(path))
            if (Executables.Any(exe => File.Exists(SafePath(path, exe)))) return path;
        return Path.GetFullPath(directory);
    }

    private static (int Id, DateTime Started)[] GameProcesses(string directory) => Executables.SelectMany(exe => Processes(directory, exe)).DistinctBy(p => p.Id).ToArray();
    private static void CheckClosed(string directory)
    {
        // AppOptions.txt is shared by every installation, including another store/build.
        foreach (var name in new[] { "CivilizationVI", "CivilizationVI_DX12" }) {
            var processes = System.Diagnostics.Process.GetProcessesByName(name);
            try {
                if (processes.Any(process => !process.HasExited)) throw new IOException("Close all Civilization VI installations before changing Tuner settings.");
            } finally { foreach (var process in processes) process.Dispose(); }
        }
    }

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var game = FindGame(directory);
            var running = GameProcesses(game.Directory).Length > 0;
            _ = Civ6Setup.ReadRuntime(package);
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
            var options = optionsPath ?? Civ6Setup.FindOptions();
            var tunerEnabled = Civ6Setup.IsTunerEnabled(options);
            bool installed;
            try { installed = Civ6Setup.Installed(options); }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { installed = false; }
            return (installed, tunerEnabled
                ? "Tuner is enabled. HappyCheats can connect without a setup receipt. Load single-player and close other Tuner clients."
                : "Install / update enables Civilization VI's Tuner. Achievements are disabled while Tuner is enabled.");
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) {
            return (false, error.Message);
        }
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        var game = FindGame(installation.Directory);
        Civ6Setup.Install(package, Civ6Setup.FindOptions(), () => CheckClosed(game.Directory), progress);
    });

    public async Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null)
    {
        await gate.WaitAsync();
        try {
            CheckClosed(installation.Directory);
            await DisconnectAsync();
            Civ6Setup.Remove(Civ6Setup.FindOptions(), () => CheckClosed(installation.Directory), progress);
        } finally { gate.Release(); }
    }

    public Task LaunchAsync(GameInstallation installation)
    {
        var registered = FindGame(null);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Launch this Civilization VI installation from its own game launcher, then return to HappyCheats.");
        return LaunchSteam("289070");
    }

    public async Task<TrainerStatus> ReadStatusAsync(GameInstallation installation)
    {
        await gate.WaitAsync();
        try { return await RefreshAsync(installation, tick: true); }
        catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException or TimeoutException or JsonException or InvalidOperationException or UnauthorizedAccessException) {
            await DisconnectAsync();
            return TrainerStatus.Offline(error is System.Net.Sockets.SocketException
                ? "No Civilization VI Tuner connection. Close the game, choose Install / update, restart it and load single-player. Close other Tuner clients."
                : "Civilization VI: " + error.Message);
        } finally { gate.Release(); }
    }

    private async Task DisconnectAsync()
    {
        if (tuner != null) await tuner.DisposeAsync();
        tuner = null; session = ""; processId = 0;
    }

    private async Task<string> ExecuteAsync(string context, string expression)
    {
        var marker = "HCVI_" + Guid.NewGuid().ToString("N") + "|";
        var result = await tuner!.ExecuteAsync(context, "print('" + marker + "' .. (" + expression + "))", marker);
        if (!result.StartsWith(marker, StringComparison.Ordinal)) throw new IOException("Invalid Civilization VI response.");
        return result[marker.Length..];
    }

    // Resolve UI selection each time. Only a unit/city owned by the local player is forwarded.
    internal const string SelectionExpression = "(function() " +
        "local function static(t,k) if not t or type(t[k])~='function' then return nil end; local ok,v=pcall(t[k]); if ok then return v end end; " +
        "local multi=static(GameConfiguration,'IsAnyMultiplayer'); local hotseat=static(GameConfiguration,'IsHotseat'); " +
        "if multi==true or hotseat==true then return '-1|-1|-1|false' end; " +
        "local p=static(Game,'GetLocalPlayer'); if type(p)~='number' or p<0 then return '-1|-1|-1|false' end; " +
        "local function selected(name) local item=static(UI,name); if not item then return -1 end; " +
        "local ok,id=pcall(function() if item:GetOwner()~=p then return -1 end; return item:GetID() end); return ok and type(id)=='number' and id or -1 end; " +
        "return tostring(p)..'|'..tostring(selected('GetHeadSelectedUnit'))..'|'..tostring(selected('GetHeadSelectedCity'))..'|'..tostring(multi==false) end)()";

    private async Task<string> SelectionAsync()
    {
        return ParseSelection(await ExecuteAsync("InGame", SelectionExpression));
    }

    internal static string ParseSelection(string selection)
    {
        if (selection.Length > 128) throw new IOException("Invalid Civilization VI selection.");
        var parts = selection.Split('|');
        var ids = new int[3];
        if (parts.Length != 4 || parts[3] is not ("true" or "false")) throw new IOException("Invalid Civilization VI selection.");
        for (var index = 0; index < ids.Length; index++) {
            if (!int.TryParse(parts[index], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out ids[index]) || ids[index] < -1)
                throw new IOException("Invalid Civilization VI selection.");
        }
        // Never forward a unit/city ID without a valid local player. Format parsed numbers,
        // not text received from the socket, when interpolating the following Lua request.
        if (ids[0] == -1) ids[1] = ids[2] = -1;
        return string.Join(',', ids.Select(id => id.ToString(CultureInfo.InvariantCulture))) + "," + (ids[0] >= 0 ? parts[3] : "false");
    }

    private async Task<TrainerStatus> RefreshAsync(GameInstallation installation, bool tick)
    {
        if (!installation.IsInstalled) { await DisconnectAsync(); return TrainerStatus.Offline(installation.Message); }
        var processes = GameProcesses(installation.Directory);
        if (processes.Length != 1) { await DisconnectAsync(); return TrainerStatus.Offline("Start Civilization VI and load a single-player game to connect."); }
        if (tuner == null || !tuner.IsConnected || processId != processes[0].Id) {
            await DisconnectAsync();
            // The live, process-owned socket is sufficient proof that Tuner is enabled.
            // Configuration ownership/backups matter for removal, never for connecting.
            tuner = new FireTunerClient();
            await tuner.ConnectAsync();
            FireTunerEndpointOwnership.RequireConnectionOwner(processes[0].Id, tuner.LocalPort);
            processId = processes[0].Id;
            session = Guid.NewGuid().ToString("N");
            var runtime = Civ6Setup.ReadRuntime(package);
            var marker = "HCVI_LOAD_" + Guid.NewGuid().ToString("N");
            await tuner.ExecuteAsync("GameCore_Tuner", runtime + "\nHappyCheatsCiv6.Init('" + session + "'); print('" + marker + "')", marker);
        }
        var selection = await SelectionAsync();
        var code = tick
            ? "HappyCheatsCiv6.Tick('" + session + "',HappyCheatsCiv6.Enabled or {}," + selection + ")"
            : "HappyCheatsCiv6.Status(" + selection + ")";
        var status = ParseStatus(await ExecuteAsync("GameCore_Tuner", code));
        if (status.SessionId != session) throw new IOException("Civilization VI game session changed. Reconnect before using controls.");
        return status;
    }

    internal static TrainerStatus ParseStatus(string json)
    {
        if (json.Length > 65536) throw new IOException("Civilization VI response is too large.");
        using var doc = JsonDocument.Parse(json);
        var state = doc.RootElement;
        if (state.ValueKind != JsonValueKind.Object || Object(state, "schema_version").ValueKind != JsonValueKind.Number
            || !Object(state, "schema_version").TryGetInt32(out var schema) || schema != 1
            || Object(state, "version").ValueKind != JsonValueKind.String || Text(state, "version") != TrainerVersion
            || Object(state, "ready").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || Object(state, "command_success").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new IOException("Invalid Civilization VI trainer status schema or runtime version.");
        var sessionId = Text(state, "session_id");
        if (Object(state, "session_id").ValueKind != JsonValueKind.String || !Guid.TryParseExact(sessionId, "N", out _))
            throw new IOException("Invalid Civilization VI trainer session.");
        foreach (var key in new[] { "message", "last_command", "command_error" })
            if (Object(state, key).ValueKind != JsonValueKind.String || Text(state, key).Length > 4096)
                throw new IOException("Invalid Civilization VI status text.");
        var lastCommand = Text(state, "last_command");
        if (lastCommand.Length > 0 && !Guid.TryParseExact(lastCommand, "N", out _)) throw new IOException("Invalid Civilization VI command identity.");
        var toggles = Object(state, "enabled");
        var rawValues = Object(state, "values");
        if (toggles.ValueKind != JsonValueKind.Object || rawValues.ValueKind != JsonValueKind.Object)
            throw new IOException("Invalid Civilization VI trainer controls.");
        var enabled = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var feature in Features.Where(feature => feature.IsToggle)) {
            var value = Object(toggles, feature.Id);
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new IOException("Invalid Civilization VI toggle state.");
            enabled.Add(feature.Id, value.ValueKind == JsonValueKind.True);
        }
        if (toggles.EnumerateObject().Count() != enabled.Count) throw new IOException("Unexpected Civilization VI toggle state.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in rawValues.EnumerateObject()) {
            if (entry.Name.Length > 128 || entry.Value.ValueKind != JsonValueKind.String || entry.Value.GetString()!.Length > 4096 || values.Count >= 128)
                throw new IOException("Invalid Civilization VI status value.");
            var key = entry.Name.StartsWith("reason_", StringComparison.Ordinal) || entry.Name.StartsWith("error_", StringComparison.Ordinal)
                ? "diagnostic_" + entry.Name : entry.Name;
            if (!values.TryAdd(key, entry.Value.GetString()!)) throw new IOException("Duplicate Civilization VI status value.");
        }
        foreach (var feature in Features)
            if (!values.TryGetValue("available_" + feature.Id, out var value) || value is not ("true" or "false"))
                throw new IOException("Invalid Civilization VI feature availability.");
        foreach (var feature in new[] { "civ6_level", "civ6_influence", "civ6_amenities", "civ6_housing" })
            if (values["available_" + feature] != "false") throw new IOException("The Civilization VI runtime advertised an unsupported editor.");
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
            return "false";
        }
        var definition = Features.SingleOrDefault(item => item.Id == feature) ?? throw new ArgumentException("Unknown Civilization VI control.");
        if (definition.IsToggle) {
            if (!enabled.HasValue || value.HasValue) throw new ArgumentException("An explicit toggle state is required.");
            return enabled.Value ? "true" : "false";
        }
        if (enabled.HasValue || !value.HasValue) throw new ArgumentException("Choose a number and Apply for this Civilization VI editor.");
        ValidateValue(feature, value.Value);
        return value.Value.ToString(CultureInfo.InvariantCulture);
    }

    internal static void ValidateCommandState(TrainerStatus fresh, TrainerStatus requested, string feature, bool? enabled)
    {
        var definition = Features.SingleOrDefault(item => item.Id == feature);
        if (definition == null && feature != "disable_all") throw new ArgumentException("Unknown Civilization VI control.");
        var cleanup = feature == "disable_all" || definition?.IsToggle == true && enabled == false;
        if (!fresh.Connected || !Guid.TryParseExact(fresh.SessionId, "N", out _) || fresh.SessionId != requested.SessionId || !cleanup && !fresh.Ready)
            throw new InvalidOperationException("Load a single-player game and reconnect before using this control.");
        if (!cleanup && (!fresh.Values.TryGetValue("available_" + feature, out var available) || available != "true"))
            throw new InvalidOperationException("This Civilization VI control is unavailable. Check its description and select your own unit or city as needed.");
    }

    private async Task CommandAsync(GameInstallation installation, TrainerStatus requested, string feature, bool? enabled, decimal? number)
    {
        var value = ValidateCommandInput(feature, enabled, number);
        await gate.WaitAsync();
        try {
            var fresh = await RefreshAsync(installation, tick: false);
            ValidateCommandState(fresh, requested, feature, enabled);
            var request = Guid.NewGuid().ToString("N");
            var selection = await SelectionAsync();
            var json = await ExecuteAsync("GameCore_Tuner", "HappyCheatsCiv6.Command('" + session + "','" + request + "','" + feature + "'," + value + "," + selection + ")");
            using var doc = JsonDocument.Parse(json);
            var next = ParseStatus(json);
            if (next.SessionId != session || next.LastCommand != request) throw new IOException("No matching Civilization VI acknowledgement. The action was not retried.");
            if (!Bool(doc.RootElement, "command_success")) throw new InvalidOperationException(Text(doc.RootElement, "command_error", "Civilization VI rejected this change."));
        } catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException or TimeoutException or JsonException) {
            await DisconnectAsync();
            throw new IOException("Civilization VI command failed; it may have applied and was not retried. " + error.Message, error);
        } finally { gate.Release(); }
    }
}
