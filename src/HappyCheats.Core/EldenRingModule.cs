using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using HappyCheats.Core.EldenRing;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class EldenRingModule(string baseDirectory) : IGameModule, IDisposable
{
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string TrainerVersion = "0.1.0";
    public const string Executable = "Game/eldenring.exe";
    private readonly string package = Path.Combine(baseDirectory, "payload", "elden-ring");
    private readonly object gate = new();
    private EldenRingSession? connection;
    private string connectedDirectory = "";
    private int connectedProcessId;
    // Read by the low-level shortcut hook: never wait for a memory operation here.
    public int ConnectedProcessId => Volatile.Read(ref connectedProcessId);

    public GameDefinition Definition { get; } = new("elden-ring", "Elden Ring", "OFFLINE SINGLE PLAYER", "1245620", "ER",
        "Experimental offline resource refills. Easy Anti-Cheat, online sessions and Seamless Co-op are unsupported. Start Steam, choose Offline launch, load a character and Connect trainer. Keep this page open. No game files or saves are edited by setup; gameplay effects are unverified.", [
        new("er_health", "Refill health", "Periodically refill a living character's HP while this page is open. Not invulnerability; lethal damage can still kill.", "F1", true, "Resources"),
        new("er_fp", "Refill FP", "Periodically refill your current FP while this page is open. Does not change maximum FP.", "F2", true, "Resources"),
        new("er_stamina", "Refill stamina", "Periodically refill your current stamina while this page is open. Does not change maximum stamina.", "F3", true, "Resources"),
        new("er_restore", "Restore all resources", "Refill current HP, FP and stamina once for a living offline character.", "F4", false, "Resources")]);

    internal static string NormalizeDirectory(string directory)
    {
        var path = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if (File.Exists(SafePath(path, Executable))) return path;
        if (Path.GetFileName(path).Equals("Game", StringComparison.OrdinalIgnoreCase)
            && File.Exists(SafePath(path, "eldenring.exe"))) return Directory.GetParent(path)!.FullName;
        return path;
    }

    private static (string Directory, string Build) FindGame(string? directory) => FindSteam("1245620", Executable,
        string.IsNullOrWhiteSpace(directory) ? null : NormalizeDirectory(directory));

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() =>
    {
        try
        {
            var game = FindGame(directory);
            var running = Processes(game.Directory, Executable).Length > 0;
            try
            {
                EldenRingProfile.Load(package).VerifyExecutable(SafePath(game.Directory, Executable));
                lock (gate) return new GameInstallation(game.Directory, game.Build, true, connection != null, running, true,
                    "Bundled Elden Ring trainer ready. Offline launch, then Connect trainer. Gameplay is unverified.");
            }
            catch (Exception error) when (Expected(error))
            {
                return new GameInstallation(game.Directory, game.Build, true, false, running, false, error.Message);
            }
        }
        catch (Exception error) when (Expected(error))
        {
            return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message);
        }
    });

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() =>
    {
        lock (gate)
        {
            StopCore();
            try
            {
                var game = FindGame(installation.Directory);
                var profile = EldenRingProfile.Load(package);
                var exe = SafePath(game.Directory, Executable);
                profile.VerifyExecutable(exe);
                RequireUnprotectedEnvironment();
                var processes = Processes(game.Directory, Executable);
                if (processes.Length != 1) throw new IOException("Use Offline launch, then load Elden Ring and choose Connect trainer.");
                var process = processes[0];
                var runtime = EldenRingMemory.Open(process.Id, process.Started, exe, profile);
                connection = new EldenRingSession(new MemoryAdapter(runtime));
                connectedDirectory = game.Directory;
                connectedProcessId = process.Id;
                progress?.Report(connection.Poll().Message);
            }
            catch { StopCore(); throw; }
        }
    });

    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null)
    {
        Stop();
        progress?.Report("Elden Ring disconnected. All refills are off; game files were unchanged.");
        return Task.CompletedTask;
    }

    public Task LaunchAsync(GameInstallation installation) => Task.Run(() =>
    {
        lock (gate)
        {
            StopCore();
            var game = FindGame(installation.Directory);
            var exe = SafePath(game.Directory, Executable);
            EldenRingProfile.Load(package).VerifyExecutable(exe);
            RequireUnprotectedEnvironment();
            if (AnyProcess("eldenring")) throw new IOException("Close all Elden Ring sessions before using Offline launch.");
            if (!AnyProcess("steam")) throw new IOException("Start Steam with the account that owns Elden Ring, then use Offline launch.");
            // SteamAppId identifies the owned game. It is not an offline switch;
            // the runtime independently requires a positive offline state before any write.
            var start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false };
            start.Environment["SteamAppId"] = "1245620";
            using var process = Process.Start(start) ?? throw new IOException("Elden Ring did not start.");
        }
    });

    internal static void RequireUnprotectedEnvironment()
    {
        foreach (var name in new[] { "start_protected_game", "EasyAntiCheat", "EasyAntiCheat_EOS", "EasyAntiCheat_EOS_Setup" })
            if (AnyProcess(name)) throw new IOException("Easy Anti-Cheat or a protected launcher is running. Close the protected game before connecting Elden Ring.");
    }

    private static bool AnyProcess(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try { return processes.Any(process => !process.HasExited); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() =>
    {
        lock (gate)
        {
            if (connection == null) return TrainerStatus.Offline("Use Offline launch, then load Elden Ring and choose Connect trainer.");
            try
            {
                if (!installation.IsInstalled || !SameDirectory(installation.Directory, connectedDirectory))
                    throw new IOException("Elden Ring installation changed. Reconnect the trainer.");
                RequireUnprotectedEnvironment();
                return connection.Poll();
            }
            catch (Exception error) when (Expected(error))
            {
                StopCore();
                return TrainerStatus.Offline(error.Message);
            }
        }
    });

    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
    {
        EldenRingSession.ValidateCommand(featureId, enabled);
        return Task.Run(() =>
        {
            lock (gate)
            {
                try
                {
                    if (connection == null || !installation.IsInstalled || !SameDirectory(installation.Directory, connectedDirectory))
                        throw new InvalidOperationException("Connect the offline Elden Ring trainer before using controls.");
                    RequireUnprotectedEnvironment();
                    connection.Command(status, featureId, enabled);
                }
                catch { StopCore(); throw; }
            }
        });
    }

    private static bool SameDirectory(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or Win32Exception or NotSupportedException;
    public void Stop() { lock (gate) StopCore(); }
    private void StopCore()
    {
        var old = connection;
        connection = null; connectedProcessId = 0; connectedDirectory = "";
        old?.Dispose();
    }
    public void Dispose() => Stop();

    private sealed class MemoryAdapter(EldenRingMemory memory) : EldenRingSession.IRuntime
    {
        public EldenRingState Read() => memory.Read();
        public EldenRingState Refill(EldenRingState expected, bool health, bool fp, bool stamina) => memory.Refill(expected, health, fp, stamina);
        public void Dispose() => memory.Dispose();
    }
}
