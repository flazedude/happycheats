using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LaundryTrainer.Setup.Core;

namespace HappyCheats.Core;

public sealed class LaundryModule(string baseDirectory) : IGameModule
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _package = Path.Combine(baseDirectory, "payload", "laundry");
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public GameDefinition Definition { get; } = new("laundry", "Laundry Store Simulator", "Build 22778860 · Steam", "3150440", "LS",
        "Build your laundry empire. Add cash and player XP, advance shop rank, and control washers and dryers independently.",
        new FeatureDefinition[]
        {
            new("cash", "Add 10,000 cash", "Adds cash through the game's currency system.", "F6", Group: "Progression"),
            new("xp", "Add 100 player XP", "Uses the game's player level caps and rewards.", "F7", Group: "Progression"),
            new("shop", "Increase shop level", "Completes this rank's requirements and promotes one shop level.", "F10", Group: "Progression"),
            new("wash", "Fast washing · 5×", "Accelerates standard washers. Resets when the scene changes.", "F8", true, "Machines"),
            new("dry", "Fast drying", "Independent dryer acceleration, configurable from 1× to 20×.", "F9", true, "Machines"),
            new("dry_slower", "Dryer speed −1×", "Reduces the configured dryer multiplier; minimum 1×.", "Ctrl+F9", Group: "Machines"),
            new("dry_faster", "Dryer speed +1×", "Increases the configured dryer multiplier; maximum 20×.", "Shift+F9", Group: "Machines")
        });

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() =>
    {
        if (!string.IsNullOrWhiteSpace(directory)) return ConvertInspection(GameInspector.Probe(directory));
        var discovered = SteamDiscovery.Discover();
        var installation = discovered.Installations.OrderByDescending(x => x.Game.IsCompatible).FirstOrDefault();
        return installation is null
            ? new GameInstallation("", "22778860", false, false, false, false,
                discovered.Problems.Count > 0 ? string.Join(" ", discovered.Problems) : "Install Laundry Store Simulator through Steam, then scan again.")
            : ConvertInspection(installation.Game);
    });

    private static GameInstallation ConvertInspection(GameInspection game) => new(game.Directory, "22778860", game.Exists,
        game.TrainerInstalled, game.IsRunning, game.IsCompatible,
        game.IsCompatible ? game.IsRunning ? "Game running. Load a save to use trainer actions." : "Verified game build. Ready to install and launch."
            : string.Join(" ", game.Problems));

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) =>
        LaundryPackage.InstallAsync(_package, installation.Directory, progress);

    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() =>
    {
        var message = LaundryPackage.Remove(installation.Directory);
        progress?.Report(message);
    });

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadStatus(installation));

    private static TrainerStatus ReadStatus(GameInstallation installation)
    {
        var bridge = ReadBridge(installation);
        if (bridge is null) return TrainerStatus.Offline(installation.IsRunning
            ? "Waiting for the Laundry trainer. Install the latest module and load a save."
            : "Launch Laundry Store Simulator to connect.");
        var enabled = new Dictionary<string, bool> { ["wash"] = bridge.WashEnabled, ["dry"] = bridge.DryEnabled };
        var values = new Dictionary<string, string>
        {
            ["wash"] = bridge.WashMultiplier.ToString("0.#", CultureInfo.InvariantCulture) + "×",
            ["dry"] = bridge.DryMultiplier.ToString("0.#", CultureInfo.InvariantCulture) + "×",
            ["dryMultiplier"] = bridge.DryMultiplier.ToString(CultureInfo.InvariantCulture),
            ["washAvailable"] = bridge.WashAvailable.ToString(),
            ["dryAvailable"] = bridge.DryAvailable.ToString(),
            ["shopAvailable"] = bridge.ShopAvailable.ToString()
        };
        return new TrainerStatus(true, bridge.Compatible && bridge.Ready, bridge.Message, bridge.Session, enabled, values);
    }

    private static BridgeStatus? ReadBridge(GameInstallation installation)
    {
        if (string.IsNullOrWhiteSpace(installation.Directory)) return null;
        try
        {
            var path = Path.Combine(BridgeDirectory(installation), "status.json");
            AssertPlain(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 16_384) return null;
            var state = JsonSerializer.Deserialize<BridgeStatus>(File.ReadAllText(path), Json);
            if (state is null || state.SchemaVersion != 1 || !Guid.TryParseExact(state.Session, "N", out _)) return null;
            var age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - state.UpdatedUtcMs;
            if (age < -1000 || age > 3000) return null;
            using var process = Process.GetProcessById(state.Pid);
            if (process.HasExited || !string.Equals(Path.GetFileName(process.MainModule?.FileName), GameInspector.GameExecutableName, StringComparison.OrdinalIgnoreCase)) return null;
            var expected = Path.GetFullPath(Path.Combine(installation.Directory, GameInspector.GameExecutableName));
            if (!string.Equals(Path.GetFullPath(process.MainModule!.FileName), expected, StringComparison.OrdinalIgnoreCase)) return null;
            return state;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return null; }
    }

    public async Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
    {
        await _sendLock.WaitAsync();
        try
        {
            var live = ReadBridge(installation) ?? throw new InvalidOperationException("Laundry trainer is disconnected.");
            if (live.Session != status.SessionId) throw new InvalidOperationException("The game session changed. Refresh and try again.");
            if (!live.Compatible && featureId != "disable_all") throw new InvalidOperationException("The game build is unsupported.");
            if (!live.Ready && featureId is "cash" or "xp" or "shop") throw new InvalidOperationException("Load a save before changing progression.");
            var action = featureId;
            double? value = null;
            switch (featureId)
            {
                case "cash": value = 10_000; break;
                case "xp": value = 100; break;
                case "shop": break;
                case "wash": value = 5; enabled ??= !live.WashEnabled; break;
                case "dry": value = live.DryMultiplier; enabled ??= !live.DryEnabled; break;
                case "dry_slower": action = "dry"; value = Math.Max(1, live.DryMultiplier - 1); enabled = live.DryEnabled; break;
                case "dry_faster": action = "dry"; value = Math.Min(20, live.DryMultiplier + 1); enabled = live.DryEnabled; break;
                case "disable_all": break;
                default: throw new ArgumentException("Unknown Laundry trainer action.", nameof(featureId));
            }
            var id = Guid.NewGuid().ToString("N");
            var directory = BridgeDirectory(installation);
            var commands = Path.Combine(directory, "commands");
            var response = Path.Combine(directory, "responses", id + ".json");
            AssertPlain(commands);
            AssertPlain(response);
            if (!Directory.Exists(commands)) throw new IOException("The Laundry trainer command directory is unavailable.");
            var temporary = Path.Combine(commands, id + ".tmp");
            var request = Path.Combine(commands, id + ".json");
            var expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000;
            try
            {
                var command = new { id, session = live.Session, expiresUtcMs = expires, action, value, enabled };
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    JsonSerializer.Serialize(stream, command, Json);
                File.Move(temporary, request);
                var deadline = Stopwatch.StartNew();
                while (deadline.Elapsed < TimeSpan.FromSeconds(7))
                {
                    await Task.Delay(100);
                    AssertPlain(response);
                    if (!File.Exists(response)) continue;
                    if (new FileInfo(response).Length > 16_384) throw new IOException("Invalid Laundry trainer response.");
                    var result = JsonSerializer.Deserialize<BridgeResponse>(await File.ReadAllTextAsync(response), Json)
                        ?? throw new IOException("The trainer response was empty.");
                    if (result.Id != id || result.Session != live.Session) throw new IOException("Trainer response belongs to a different command or game session.");
                    File.Delete(response);
                    if (!result.Success) throw new InvalidOperationException(result.Message);
                    return;
                }
                throw new TimeoutException("The trainer did not acknowledge this action. Its result is unknown; check the game before sending it again.");
            }
            finally
            {
                // Expired, unclaimed commands must not run on a later launch.
                if (File.Exists(temporary)) File.Delete(temporary);
                if (File.Exists(request)) File.Delete(request);
            }
        }
        finally { _sendLock.Release(); }
    }

    public Task LaunchAsync(GameInstallation installation)
    {
        if (!installation.IsInstalled) throw new InvalidOperationException("Install Laundry Store Simulator through Steam first.");
        var selected = Path.GetFullPath(installation.Directory).TrimEnd(Path.DirectorySeparatorChar);
        var discovered = SteamDiscovery.Discover().Installations;
        if (discovered.Count > 0 && !discovered.Any(x => string.Equals(Path.GetFullPath(x.GameDirectory).TrimEnd(Path.DirectorySeparatorChar), selected, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The selected folder does not match the Steam installation. Scan again before launching.");
        var executable = SteamDiscovery.FindSteamExecutable();
        var info = executable is null ? new ProcessStartInfo("steam://run/3150440") { UseShellExecute = true }
            : new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        if (executable is not null) info.ArgumentList.Add("steam://run/3150440");
        Process.Start(info)?.Dispose();
        return Task.CompletedTask;
    }

    private static string BridgeDirectory(GameInstallation installation) => Path.Combine(installation.Directory, "UserData", "HappyCheats", "laundry");
    private static void AssertPlain(string path)
    {
        for (string? cursor = Path.GetFullPath(path); cursor is not null; cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Laundry trainer paths cannot contain symbolic links or junctions.");
    }

    private sealed record BridgeStatus(int SchemaVersion, string Session, int Pid, long UpdatedUtcMs, bool Compatible, bool Ready,
        string Message, bool WashEnabled, double WashMultiplier, bool DryEnabled, double DryMultiplier,
        bool WashAvailable, bool DryAvailable, bool ShopAvailable);
    private sealed record BridgeResponse(string Id, string Session, bool Success, string Message);
}
