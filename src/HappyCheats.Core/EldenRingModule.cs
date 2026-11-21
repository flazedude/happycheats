using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using HappyCheats.Core.EldenRing;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class EldenRingModule(string baseDirectory) : IGameModule, IFeatureValueModule, IDisposable
{
    public void Deactivate()
    {
        try { Stop(); }
        catch (Exception error) when (Expected(error))
        {
            cleanupFailure = error.Message;
            // Deactivate is called directly by the frozen host during page changes and exit.
            // Record an unconfirmed cleanup without aborting the host's remaining shutdown work.
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HappyCheats");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "client-errors.log"),
                    $"{DateTimeOffset.UtcNow:O} [elden-ring] {error.Message.ReplaceLineEndings(" ")}{Environment.NewLine}");
            }
            catch (Exception logging) when (logging is IOException or UnauthorizedAccessException) { }
        }
    }
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string TrainerVersion = "0.2.0";
    public const string Executable = "Game/eldenring.exe";
    private readonly string package = Path.Combine(baseDirectory, "payload", "elden-ring");
    private readonly object gate = new();
    private EldenRingSession? connection;
    private string connectedDirectory = "";
    private int connectedProcessId;
    private string cleanupFailure = "";
    // Read by the low-level shortcut hook: never wait for a memory operation here.
    public int ConnectedProcessId => Volatile.Read(ref connectedProcessId);

    public GameDefinition Definition { get; } = new("elden-ring", "Elden Ring", "OFFLINE SINGLE PLAYER", "1245620", "ER",
        "Experimental offline resource refills and player controls. Easy Anti-Cheat, online sessions and Seamless Co-op are unsupported. Start Steam, choose Offline launch, load a character and Connect trainer. Keep this page open. Setup does not edit saves; the game may save rune changes. Gameplay effects are unverified.", [
        new("er_health", "Refill health", "Periodically refill a living character's HP while this page is open. Not invulnerability; lethal damage can still kill.", "F1", true, "Resources"),
        new("er_fp", "Refill FP", "Periodically refill your current FP while this page is open. Does not change maximum FP.", "F2", true, "Resources"),
        new("er_stamina", "Refill stamina", "Periodically refill your current stamina while this page is open. Does not change maximum stamina.", "F3", true, "Resources"),
        new("er_restore", "Restore all resources", "Refill current HP, FP and stamina once for a living offline character.", "F4", false, "Resources"),
        new("er_damage_protection", "Damage protection / 伤害防护", "Experimental protection against incoming hits to your character. Protection from falls, scripted deaths and status effects is unverified. / 实验性玩家受击防护；尚未验证坠落、剧情死亡及异常状态的防护效果。", "", true, "Resources"),
        new("er_consumables", "Unlimited consumables / 消耗品不减", "Prevent consumable depletion for your character. Flask, ammunition and quest-item coverage is unverified. / 防止玩家使用消耗品时数量减少；尚未验证圣杯瓶、弹药和任务物品的适用范围。", "", true, "Resources"),
        new("er_runes", "Set runes / 设置卢恩", "Set the current character's rune balance once. The game may save this change; it is never replayed after loading. / 一次设置当前角色的卢恩数量；游戏可能保存此改动，加载后不会重复执行。", "", false, "Resources", ChoiceValueKey: "Runes / 卢恩", Number: new FeatureNumber(0, 999_999_999, 10_000, Unit: "runes / 卢恩"))]);

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
                cleanupFailure = "";
                progress?.Report(connection.Poll().Message);
            }
            catch (Exception error) { StopAfterFailure(error); throw; }
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
            if (connection == null) return TrainerStatus.Offline(cleanupFailure.Length != 0 ? cleanupFailure
                : "Use Offline launch, then load Elden Ring and choose Connect trainer.");
            try
            {
                if (!installation.IsInstalled || !SameDirectory(installation.Directory, connectedDirectory))
                    throw new IOException("Elden Ring installation changed. Reconnect the trainer.");
                RequireUnprotectedEnvironment();
                return connection.Poll();
            }
            catch (Exception error) when (Expected(error))
            {
                StopAfterFailure(error);
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
                catch (Exception error) { StopAfterFailure(error); throw; }
            }
        });
    }

    public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value)
    {
        var runes = EldenRingSession.ValidateValue(featureId, value);
        return Task.Run(() =>
        {
            lock (gate)
            {
                try
                {
                    if (connection == null || !installation.IsInstalled || !SameDirectory(installation.Directory, connectedDirectory))
                        throw new InvalidOperationException("Connect the offline Elden Ring trainer before setting runes. / 请先连接离线修改器，再设置卢恩。");
                    RequireUnprotectedEnvironment();
                    connection.SetRunes(status, runes);
                }
                catch (Exception error) { StopAfterFailure(error); throw; }
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
    private void StopAfterFailure(Exception operation)
    {
        try { StopCore(); }
        catch (Exception cleanup)
        {
            throw new IOException(operation.Message + " Player effect cleanup also failed: " + cleanup.Message,
                new AggregateException(operation, cleanup));
        }
    }
    public void Dispose() => Stop();

    private sealed class MemoryAdapter(EldenRingMemory memory) : EldenRingSession.IRuntime
    {
        public EldenRingState Read() => memory.Read();
        public EldenRingState Refill(EldenRingState expected, bool health, bool fp, bool stamina) => memory.Refill(expected, health, fp, stamina);
        public EldenRingState SetRunes(EldenRingState expected, int value) => memory.SetRunes(expected, value);
        public EldenRingState SetPlayerFlag(EldenRingState expected, string featureId, bool enabled) => memory.SetPlayerFlag(expected, featureId, enabled);
        public void ReleasePlayerFlags() => memory.ReleasePlayerFlags();
        public void Dispose() => memory.Dispose();
    }
}
