using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class PhantomBladeModule(string baseDirectory) : IGameModule, IFeatureValueModule
{
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public const string Executable = "platform/steam/PBZ.exe";
    public const string SupportedExecutableHash = "4415933CE33476F14C837ABB61A1B60AF8B78CD81783896501913A7AAC69E423";
    public const string SupportedSteamBuild = "26367565";
    public const string BridgeRoot = "platform/steam/ue4ss/Mods/HappyCheatsPhantomBlade/bridge/";
    public const string TrainerVersion = "0.1.5";
    private readonly string package = Path.Combine(baseDirectory, "payload", "phantom-blade-zero");
    private readonly SemaphoreSlim commandLock = new(1, 1);
    private readonly object inspectionLock = new();
    private (string Path, long Length, DateTime Modified, bool Compatible)? inspected;
    private readonly object packageInspectionLock = new();
    private (PackageFileStamp[] Files, bool Matching)? inspectedPackage;

    private sealed record PackageFileStamp(string Path, bool Exists, long Length, DateTime Modified, DateTime Created);

    public GameDefinition Definition { get; } = new("phantom-blade-zero", "Phantom Blade Zero", "ACTION RPG", "4115450", "PB",
        "Health, Sha-Chi, damage, player damage guard and adjustable movement. Experimental Steam module; gameplay validation is pending.",
        new FeatureDefinition[] {
            new("pbz_health", "Unlimited health", "Keep the current player's health topped up while enabled. Lethal hits between refills may still be fatal.", "F1", true, "Survival"),
            new("pbz_stamina", "Unlimited stamina (Sha-Chi)", "Keep the current player's Sha-Chi resource topped up while enabled.", "F2", true, "Survival"),
            new("pbz_damage", "Damage multiplier (attack power)", "Apply a 1–20× player attack-power multiplier; final damage follows game rules. Game stat recalculation may cancel it. Choose 1× or Disable all to restore the captured value when it is still owned by this trainer.", "", false, "Combat", ChoiceValueKey: "pbz_damage", Number: new FeatureNumber(1, 20, 2, Unit: "×")),
            new("pbz_guard", "Player damage guard / 玩家伤害防护", "Turn off the current player's actor damage permission. Game-specific damage paths may bypass this guard. / 关闭当前玩家角色的受伤许可；游戏自定义伤害途径仍可能绕过防护。", "", true, "Survival"),
            new("pbz_heal", "Refill health / 恢复生命", "Once, use the game's native health recovery for the current living player; does not revive. / 调用游戏原生恢复功能，为当前存活玩家恢复一次生命；不能复活。", "", false, "Survival"),
            new("pbz_refill_shachi", "Refill Sha-Chi / 补满杀气", "Once, fill the current player's Sha-Chi to its current maximum. / 将当前玩家的杀气补满一次，不改变上限。", "", false, "Survival"),
            new("pbz_speed", "Player simulation speed / 玩家模拟速度", "Scale the player's actor time by 1–5×, affecting movement and animation rather than world time. Game recalculation may cancel it; choose 1× to restore the captured value if still owned. / 将玩家角色时间缩放至 1–5 倍，影响移动和动画，不改变世界时间。游戏重算可能取消；选择 1 倍可在仍归本修改器控制时恢复原值。", "", false, "Movement", ChoiceValueKey: "pbz_speed", Number: new FeatureNumber(1, 5, 2, Unit: "×")),
            new("pbz_regen", "Sha-Chi regeneration multiplier / 杀气恢复倍率", "Scale the current Sha-Chi regeneration stat by 1–10×. Game stat recalculation may cancel it; choose 1× to restore the captured value if still owned. / 将当前杀气恢复属性提高至 1–10 倍。游戏重算属性可能取消；选择 1 倍可在仍归本修改器控制时恢复原值。", "", false, "Survival", ChoiceValueKey: "pbz_regen", Number: new FeatureNumber(1, 10, 2, Unit: "×")),
            new("pbz_walk", "Walk speed multiplier / 行走速度倍率", "Scale the player's maximum walk speed by 1–5×. Game recalculation may cancel it; choose 1× to restore the captured value if still owned. / 将玩家最大行走速度提高至 1–5 倍。游戏重算可能取消；选择 1 倍可在仍归本修改器控制时恢复原值。", "", false, "Movement", ChoiceValueKey: "pbz_walk", Number: new FeatureNumber(1, 5, 2, Unit: "×")),
            new("pbz_jump", "Jump velocity multiplier / 跳跃初速度倍率", "Scale the player's vertical jump velocity by 1–5×; actual height follows game physics. Game recalculation may cancel it; choose 1× to restore the captured value if still owned. / 将玩家垂直起跳速度提高至 1–5 倍；实际高度遵循游戏物理。游戏重算可能取消；选择 1 倍可在仍归本修改器控制时恢复原值。", "", false, "Movement", ChoiceValueKey: "pbz_jump", Number: new FeatureNumber(1, 5, 2, Unit: "×")),
            new("pbz_gravity", "Gravity reduction / 降低重力", "Divide the player's gravity scale by 1–5. Game recalculation may cancel it; choose 1× to restore the captured value if still owned. / 将玩家重力系数除以 1–5。游戏重算可能取消；选择 1 倍可在仍归本修改器控制时恢复原值。", "", false, "Movement", ChoiceValueKey: "pbz_gravity", Number: new FeatureNumber(1, 5, 2, Unit: "×"))
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
            var matching = PhantomBladeInstaller.MatchesPackage(package, directory);
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
        var paths = PhantomBladeInstaller.RequiredFiles.Append(PhantomBladeInstaller.ModRoot + "enabled.txt");
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
            var installed = PhantomBladeInstaller.Installed(found.Directory);
            var matching = false;
            if (installed) {
                try { matching = MatchesPackageForDiscovery(found.Directory); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            }
            return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, Executable).Length > 0, compatible,
                !compatible ? "This Phantom Blade Zero build needs a compatibility check."
                : installed ? matching ? "Experimental Phantom Blade Zero trainer installed. Gameplay validation is pending."
                    : "Phantom Blade Zero trainer update required. Installed files differ from the current module."
                : "Phantom Blade Zero Steam build " + SupportedSteamBuild + " detected. Experimental trainer; gameplay validation is pending.");
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) {
            return new GameInstallation(directory ?? "", "", false, false, false, false, error.Message);
        }
    });

    private static void CheckClosed(string directory)
    {
        if (Processes(directory, Executable).Length > 0) throw new IOException("Close Phantom Blade Zero before changing trainer files.");
    }

    public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() => {
        if (!installation.IsInstalled || !Compatible(installation.Directory, true)) throw new IOException("Unsupported Phantom Blade Zero executable. Game files were preserved.");
        PhantomBladeInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
    });

    public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.Run(() =>
        PhantomBladeInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress));

    public Task LaunchAsync(GameInstallation installation)
    {
        if (!installation.IsInstalled) throw new IOException("Install Phantom Blade Zero through Steam first.");
        var registered = FindSteam(Definition.AppId, Executable);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }

    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => ReadSnapshot(installation).Status);

    private Snapshot ReadSnapshot(GameInstallation installation)
    {
        if (!installation.IsInstalled) return new(TrainerStatus.Offline(installation.Message), false, "");
        if (!installation.Compatible) return new(TrainerStatus.Offline("This Phantom Blade Zero build needs a compatibility check."), false, "");
        try {
            var processes = Processes(installation.Directory, Executable);
            if (processes.Length != 1) return new(TrainerStatus.Offline("Launch Phantom Blade Zero to connect."), false, "");
            var path = SafePath(installation.Directory, BridgeRoot + "status.json");
            return ReadSnapshotFile(path, processes[0].Started, processes[0].Id);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) {
            return new(TrainerStatus.Offline("Waiting for Phantom Blade Zero trainer status."), false, "");
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
        return new(TrainerStatus.Offline("Waiting for Phantom Blade Zero trainer status."), false, "");
    }

    internal static Snapshot ReadSnapshotStream(FileStream stream, DateTime processStarted, int processId, DateTime now)
    {
        // Timestamp and content must describe the same file even if its path is
        // replaced while reading. Limit the bytes read as well as the file size.
        var buffer = new byte[16_385];
        var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (count > 16_384) return new(TrainerStatus.Offline("Invalid Phantom Blade Zero trainer status."), false, "");
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
            return Offline("Waiting for a fresh Phantom Blade Zero trainer heartbeat.");
        var session = Text(json, "session_id");
        var reportedPid = Text(json, "process_id");
        if (Text(json, "schema_version") != "1" || Text(json, "version") != TrainerVersion
            || !Regex.IsMatch(session, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant)
            || (reportedPid is not ("" or "0") && reportedPid != processId.ToString(CultureInfo.InvariantCulture)))
            return Offline("Restart Phantom Blade Zero to load the current trainer module.");
        var values = Strings(Object(json, "values"));
        var availability = Bools(Object(json, "available"));
        foreach (var feature in FeatureIds)
            values["available_" + feature] = (availability.TryGetValue(feature, out var available) && available).ToString();
        return new(new TrainerStatus(true, Bool(json, "ready"), Text(json, "message", "Phantom Blade Zero trainer connected."), session,
            Bools(Object(json, "enabled")), values, Text(json, "last_command")), Bool(json, "last_success"), Text(json, "last_message"));
    }

    internal static readonly string[] FeatureIds = { "pbz_health", "pbz_stamina", "pbz_damage", "pbz_guard", "pbz_heal", "pbz_refill_shachi", "pbz_speed", "pbz_regen", "pbz_walk", "pbz_jump", "pbz_gravity" };

    private static bool IsToggle(string action) => action is "pbz_health" or "pbz_stamina" or "pbz_guard";
    private static bool IsAmount(string action) => action is "pbz_damage" or "pbz_speed" or "pbz_regen" or "pbz_walk" or "pbz_jump" or "pbz_gravity";

    internal static int ValidateAmount(string action, decimal? amount)
    {
        if (!IsAmount(action)) throw new ArgumentException("This Phantom Blade Zero control does not accept an amount.", nameof(action));
        var value = amount ?? 2;
        var maximum = action == "pbz_damage" ? 20 : action == "pbz_regen" ? 10 : 5;
        if (value < 1 || value > maximum || decimal.Truncate(value) != value)
            throw new ArgumentOutOfRangeException(nameof(amount), action == "pbz_damage"
                ? "Choose a whole-number damage multiplier from 1 to 20."
                : "Choose a whole-number multiplier from 1 to " + maximum.ToString(CultureInfo.InvariantCulture) + ".");
        return decimal.ToInt32(value);
    }

    internal static string FormatCommand(TrainerStatus current, TrainerStatus requested, string action, bool? enabled, string requestId, long createdUtc, decimal? amount = null)
    {
        var disabling = enabled == false && IsToggle(action) || IsAmount(action) && amount == 1;
        if (!current.Connected || (!current.Ready && action != "disable_all" && !disabling)) throw new InvalidOperationException(current.Message.Length > 0 ? current.Message : "Load a single-player save before using Phantom Blade Zero controls.");
        if (current.SessionId != requested.SessionId) throw new InvalidOperationException("Phantom Blade Zero session changed. Reconnect before trying again.");
        if (!FeatureIds.Contains(action) && action != "disable_all") throw new ArgumentException("Unknown Phantom Blade Zero trainer control.", nameof(action));
        if (!Regex.IsMatch(current.SessionId, "^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant) || !Guid.TryParseExact(requestId, "N", out _))
            throw new ArgumentException("Invalid Phantom Blade Zero command identity.");
        if (createdUtc <= 0) throw new ArgumentOutOfRangeException(nameof(createdUtc));
        if (action != "disable_all" && !disabling && (!current.Values.TryGetValue("available_" + action, out var available) || !string.Equals(available, "true", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This Phantom Blade Zero control is unavailable in the current game session.");
        if (IsToggle(action) && !enabled.HasValue) throw new ArgumentException("An explicit toggle state is required.", nameof(enabled));
        if (!IsToggle(action) && enabled.HasValue) throw new ArgumentException("This action does not accept a toggle state.", nameof(enabled));
        if (!IsAmount(action) && amount.HasValue) throw new ArgumentException("This Phantom Blade Zero control does not accept an amount.", nameof(amount));
        var command = $"schema_version=1\nsession_id={current.SessionId}\nrequest_id={requestId}\naction={action}\nenabled={(enabled == true ? "1" : "0")}\ncreated_utc={createdUtc.ToString(CultureInfo.InvariantCulture)}\n";
        return IsAmount(action) ? command + "amount=" + ValidateAmount(action, amount).ToString(CultureInfo.InvariantCulture) + "\n" : command;
    }

    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) =>
        SendCommandAsync(installation, status, featureId, enabled);

    public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value) =>
        SendCommandAsync(installation, status, featureId, null, value);

    internal static bool AcceptAcknowledgement(Snapshot next, string session, string requestId)
    {
        // Lua replaces the status file with remove/rename on Windows. A brief
        // publication gap must not fail or resend an already submitted command.
        if (!next.Status.Connected) return false;
        if (next.Status.SessionId != session) throw new IOException("Phantom Blade Zero session changed. Reconnect before trying again.");
        if (next.Status.LastCommand != requestId) return false;
        if (!next.LastSuccess) throw new InvalidOperationException(string.IsNullOrWhiteSpace(next.LastMessage) ? "Phantom Blade Zero rejected this control." : next.LastMessage);
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
            commandPath = SafePath(installation.Directory, BridgeRoot + "command.txt");
            if (!Directory.Exists(Path.GetDirectoryName(commandPath))) throw new IOException("The Phantom Blade Zero trainer command directory is unavailable.");
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
