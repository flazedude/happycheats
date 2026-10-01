using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class Witcher3Module(string baseDirectory) : IGameModule
{
    public const string TrainerVersion = "0.1.1";
    public const string SupportedSteamBuild = "25575366";
    public const string StatusPath = "bin/config/base/modsHappyCheatsWitcher3.ini";
    private readonly string package = Path.Combine(baseDirectory, "payload", "witcher-3");
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object inspectionGate = new();
    private (string Root, long ExeSize, DateTime ExeTime, long ScriptSize, DateTime ScriptTime, bool Valid)? inspection;

    private static readonly FeatureDefinition[] Features = [
        new("witcher3_health", "Infinite health", "Refill living Geralt's health every tick. Lethal damage between refills can still kill him. Keep this page open.", "F1", true, "Survival"),
        new("witcher3_stamina", "Infinite stamina", "Refill Geralt's stamina every tick while enabled. Keep this page open.", "F2", true, "Survival"),
        new("witcher3_adrenaline", "Full adrenaline", "Refill Geralt's adrenaline every tick while enabled. Keep this page open.", "F3", true, "Survival"),
        new("witcher3_toxicity", "Clear potion toxicity", "Drain Geralt's potion toxicity every tick. Decoction toxicity is retained. Keep this page open.", "F4", true, "Survival"),
        new("witcher3_heal", "Refill health and stamina", "Restore living Geralt's health and stamina once.", "F5", false, "Survival"),
        new("witcher3_money", "Add 1,000 crowns", "Add 1,000 crowns once. Added currency may be kept by normal game saves.", "F6", false, "Character growth"),
        new("witcher3_xp", "Add 1,000 base XP", "Grant 1,000 base experience through game rules; New Game+ modifiers and the level cap apply. Progression may be kept by normal saves.", "F7", false, "Character growth"),
        new("witcher3_skill", "Add 1 skill point", "Add one skill point once. Progression may be kept by normal game saves.", "F8", false, "Character growth")
    ];
    public GameDefinition Definition { get; } = new("witcher-3", "The Witcher 3: Wild Hunt — Remastered", "OPEN-WORLD RPG", "292030", "W3",
        "Experimental Steam Remastered 5.0 DX12 trainer. Eight survival and progression controls. Keep this page open for continuous effects; gameplay validation is pending.", Features);

    private bool Compatible(string root, bool force = false)
    {
        var exe = new FileInfo(SafePath(root, Witcher3Installer.Executable));
        var script = new FileInfo(SafePath(root, Witcher3Installer.VanillaScript));
        if (!exe.Exists || !script.Exists) return false;
        lock (inspectionGate) {
            if (!force && inspection is { } p && p.Root == root && p.ExeSize == exe.Length && p.ExeTime == exe.LastWriteTimeUtc && p.ScriptSize == script.Length && p.ScriptTime == script.LastWriteTimeUtc) return p.Valid;
            var valid = Hash(exe.FullName) == Witcher3Installer.SupportedExecutableSha256 && Hash(script.FullName) == Witcher3Installer.SupportedVanillaSha256;
            inspection = (root, exe.Length, exe.LastWriteTimeUtc, script.Length, script.LastWriteTimeUtc, valid);
            return valid;
        }
    }

    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var found = FindSteam(Definition.AppId, Witcher3Installer.Executable, directory);
            var valid = Compatible(found.Directory);
            var installed = Witcher3Installer.Installed(found.Directory);
            return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, Witcher3Installer.Executable).Length != 0, valid,
                !valid ? "This Witcher 3 build needs a compatibility check. Only Steam Remastered 5.0 DX12 is supported."
                : installed ? "Experimental Witcher 3 trainer installed. Load a save as Geralt; keep this page open. Gameplay is unverified."
                : "Steam Remastered 5.0 DX12 detected. Install the experimental trainer before launching.");
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) {
            return new GameInstallation(directory ?? "", "", false, false, false, false, ex.Message);
        }
    });

    private static void CheckClosed(string root)
    {
        if (Processes(root, Witcher3Installer.Executable).Length != 0) throw new IOException("Close The Witcher 3 before changing trainer files.");
    }
    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        if (!Compatible(installation.Directory, true)) throw new IOException("Unsupported Witcher 3 executable or vanilla scripts. Game files were preserved.");
        Witcher3Installer.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
    });
    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() =>
        Witcher3Installer.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));
    public Task LaunchAsync(GameInstallation installation)
    {
        var registered = FindSteam(Definition.AppId, Witcher3Installer.Executable);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }

    internal sealed record Snapshot(TrainerStatus Status, double EngineTime = 0, bool Success = false, string Result = "", bool WaitingForGame = false, DateTime Modified = default);
    internal static Snapshot ParseSnapshot(string text, DateTime modified, DateTime started, DateTime now, bool background = false)
    {
        Snapshot Offline(string message) => new(TrainerStatus.Offline(message));
        if (text.Length > 16384 || modified < started || modified > now.AddSeconds(2))
            return Offline("Waiting for a fresh Witcher 3 trainer heartbeat.");
        var rows = text.Split('\n').Select(s => s.Trim()).Where(s => s.Contains('=') && s[..s.IndexOf('=')].Trim().Equals("Snapshot", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (rows.Length != 1) return Offline("Waiting for Witcher 3 script initialization.");
        var p = rows[0][(rows[0].IndexOf('=') + 1)..].Trim().Split('|');
        if (p.Length != 16 || p[0] != "HCW3" || p[1] != "1" || p[2] != TrainerVersion ||
            !Regex.IsMatch(p[3], "^[0-9.-]{1,80}$") || !double.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var time) || !double.IsFinite(time) || time < 0 ||
            new[] { 4, 6, 7, 8, 9, 14 }.Any(i => p[i] is not ("0" or "1")) || (p[13] != "" && !Regex.IsMatch(p[13], "^[a-f0-9]{32}$")))
            return Offline("Invalid Witcher 3 trainer status. Restart after installing the current trainer.");
        var enabled = Features.Where(f => f.IsToggle).Select((f, i) => (f.Id, Value: p[6 + i] == "1")).ToDictionary(t => t.Id, t => t.Value);
        var values = new Dictionary<string, string> { ["Health"] = p[10], ["Stamina"] = p[11], ["Crowns"] = p[12] };
        values["witcher3_waiting_for_game"] = background.ToString();
        foreach (var f in Features) values["available_" + f.Id] = (p[4] == "1").ToString();
        return new(new TrainerStatus(true, p[4] == "1", background ? "The Witcher 3 is paused in the background. Select a control, then return to the game to apply it."
            : p[4] == "1" ? "Witcher 3 script connected. Keep this page open for continuous effects." : "Load a save as living Geralt to use these controls.", p[3], enabled, values, p[13]), time, p[14] == "1", p[15], background, modified);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    private static bool IsBackground(int processId)
    {
        var window = GetForegroundWindow();
        return window != IntPtr.Zero && GetWindowThreadProcessId(window, out var foreground) != 0 && foreground != (uint)processId;
    }

    private Snapshot ReadSnapshot(GameInstallation installation)
    {
        if (!installation.IsInstalled || !installation.TrainerInstalled || !Compatible(installation.Directory)) return new(TrainerStatus.Offline(installation.Message));
        try {
            var processes = Processes(installation.Directory, Witcher3Installer.Executable);
            if (processes.Length != 1) return new(TrainerStatus.Offline("Launch The Witcher 3 and load a save as Geralt."));
            var path = SafePath(installation.Directory, StatusPath);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 16384) return new(TrainerStatus.Offline("Invalid Witcher 3 status size."));
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var buffer = new char[16385];
            var count = reader.ReadBlock(buffer, 0, buffer.Length);
            return ParseSnapshot(new string(buffer, 0, count), File.GetLastWriteTimeUtc(stream.SafeFileHandle), processes[0].Started, DateTime.UtcNow, IsBackground(processes[0].Id));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) {
            return new(TrainerStatus.Offline("Waiting for Witcher 3 script initialization. Restart the game after installation."));
        }
    }

    private static void WriteCsv(string path, string text)
    {
        if (!Directory.Exists(Path.GetDirectoryName(path))) throw new IOException("Install the Witcher 3 trainer first.");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text, new UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void RenewLease(GameInstallation installation, Snapshot snapshot) => WriteCsv(SafePath(installation.Directory, Witcher3Installer.BridgeRoot + "/lease.csv"),
        "session,issued\r\n" + snapshot.Status.SessionId + "," + snapshot.EngineTime.ToString("F3", CultureInfo.InvariantCulture) + "\r\n");

    public async Task<TrainerStatus> ReadStatusAsync(GameInstallation installation)
    {
        if (!await gate.WaitAsync(0)) return ReadSnapshot(installation).Status;
        try {
            var snapshot = ReadSnapshot(installation);
            if (snapshot.Status.Connected) RenewLease(installation, snapshot);
            return snapshot.Status;
        } catch (IOException ex) { return TrainerStatus.Offline(ex.Message); }
        finally { gate.Release(); }
    }

    internal static string CreateCommand(TrainerStatus requested, Snapshot fresh, string id, string feature, bool? enabled)
    {
        if (!fresh.Status.Connected || requested.SessionId != fresh.Status.SessionId || !Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new InvalidOperationException("Witcher 3 session changed. Reconnect before trying again.");
        var definition = Features.FirstOrDefault(f => f.Id == feature);
        if (definition == null && feature != "disable_all") throw new ArgumentException("Unknown Witcher 3 control.");
        if ((definition?.IsToggle == true) != enabled.HasValue) throw new ArgumentException("Invalid Witcher 3 toggle state.");
        if (!fresh.Status.Ready && feature != "disable_all" && enabled != false) throw new InvalidOperationException("Load a save as living Geralt before using this control.");
        return "schema,session,request,action,enabled,issued\r\n1," + fresh.Status.SessionId + "," + id + "," + feature + "," + (enabled.HasValue ? enabled.Value ? "1" : "0" : "") + "," + fresh.EngineTime.ToString("F3", CultureInfo.InvariantCulture) + "\r\n";
    }

    public async Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
    {
        if (!await gate.WaitAsync(0)) throw new InvalidOperationException("Waiting for the previous Witcher 3 command.");
        try {
            var processes = Processes(installation.Directory, Witcher3Installer.Executable);
            if (processes.Length != 1) throw new IOException("Witcher 3 session changed. Check the game before repeating this action.");
            var process = processes[0];
            var initial = ReadSnapshot(installation);
            var path = SafePath(installation.Directory, Witcher3Installer.BridgeRoot + "/command.csv");
            var clock = Stopwatch.StartNew();
            Snapshot ReadSameProcess() {
                var running = Processes(installation.Directory, Witcher3Installer.Executable);
                if (running.Length != 1 || running[0] != process) {
                    throw new IOException("Witcher 3 session changed. Check the game before repeating this action.");
                }
                return ReadSnapshot(installation);
            }
            await DispatchAsync(status, initial, Guid.NewGuid().ToString("N"), featureId, enabled, ReadSameProcess,
                next => RenewLease(installation, next), command => WriteCsv(path, command),
                () => WriteCsv(path, "schema,session,request,action,enabled,issued\r\n"), () => Task.Delay(250), () => clock.Elapsed);
        } finally { gate.Release(); }
    }

    internal static async Task DispatchAsync(TrainerStatus requested, Snapshot initial, string id, string feature, bool? enabled,
        Func<Snapshot> read, Action<Snapshot> renew, Action<string> write, Action clear, Func<Task> delay, Func<TimeSpan> elapsed)
    {
        // Engine time pauses with the game. Publish immediately so its first resumed
        // tick can consume the command, regardless of which window has focus.
        var command = CreateCommand(requested, initial, id, feature, enabled);
        void CheckBridge(Snapshot snapshot) {
            if (snapshot.Result == "bridge_unavailable")
                throw new IOException("The Witcher 3 cannot read its command files. Close the game and choose Install / update.");
        }
        CheckBridge(initial);
        renew(initial);
        try {
            write(command);
            var sent = elapsed();
            while (elapsed() - sent < TimeSpan.FromSeconds(60)) {
                await delay();
                if (elapsed() - sent >= TimeSpan.FromSeconds(60)) break;
                var next = read();
                if (!next.Status.Connected) continue; // A partially written INI is not a new session.
                if (next.Status.SessionId != requested.SessionId) throw new IOException("Witcher 3 session changed. Check the game before repeating this action.");
                if (next.Status.LastCommand == id) {
                    if (!next.Success) throw new InvalidOperationException("The Witcher 3 trainer rejected this action: " + next.Result);
                    return;
                }
                CheckBridge(next);
                if (next.EngineTime - initial.EngineTime >= 8) break;
                renew(next);
            }
            throw new TimeoutException("No Witcher 3 acknowledgement received. This action may have applied; check the game before repeating it.");
        } finally {
            // Never republish a one-shot. Empty the mailbox on success and failure alike.
            clear();
        }
    }
}
