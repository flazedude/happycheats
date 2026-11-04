using System.Globalization;
using System.Text.Json;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

public sealed class LivinCreamModule(string baseDirectory) : IGameModule, IFeatureValueModule
{
    public const string Executable = "Livin' the Cream Ice Cream Simulator.exe";
    public const string AssemblyPath = "Livin' the Cream Ice Cream Simulator_Data/Managed/Assembly-CSharp.dll";
    public const string SupportedAssemblyHash = "3756614FB3952A6BACF1E2E8D1994EFD7EB451D7F27218A66B033E386E3CB98E";
    public const string SupportedSteamBuild = "26415994";
    public const string TrainerVersion = "1.0.0";
    public const string MinimumClientVersion = "0.3.69";
    private const string IpcRoot = "BepInEx/HappyCheats/livin-cream/";
    private readonly string package = Path.Combine(baseDirectory, "payload", "livin-cream");
    private readonly SemaphoreSlim operation = new(1, 1);
    public IReadOnlyList<string> GameExecutables { get; } = [Executable];
    public GameDefinition Definition { get; } = new("livin-cream", "Livin' the Cream: Ice Cream Simulator", "甜蜜冰淇淋：冰淇淋店模拟器", "4621810", "🍦",
        "Build your ice cream shop with money, XP, skill points and soft-serve controls. | 金钱、经验、技能点和软冰淇淋功能。", [
        new("lc_money", "Money / 金钱", "Set shop money once. Normal game saving may retain the result. / 单次设置店铺金钱。", "", false, "Shop / 店铺", ChoiceValueKey: "lc_money", Number: new(0, 100_000_000, 100_000)),
        new("lc_add_xp", "Add shop XP / 增加店铺经验", "Grant 1–10,000 XP once through normal level-up rewards. / 单次增加经验，保留正常升级奖励。", "", false, "Shop / 店铺", Number: new(1, 10_000, 1_000)),
        new("lc_add_skills", "Add skill points / 增加技能点", "Grant 1–1,000 skill points once. / 单次增加技能点。", "", false, "Shop / 店铺", Number: new(1, 1_000, 10)),
        new("lc_no_patience", "Unlimited customer patience / 顾客无限耐心", "Remove the front customer's wait timer without changing play style. Off restarts a normal full timer where applicable. / 关闭后恢复完整计时，不更改游戏模式。", "", true, "Customers / 顾客"),
        new("lc_unlimited_softserve", "Unlimited soft-serve stock / 软冰淇淋不消耗", "Prevent soft-serve reservoirs from consuming their existing contents. Fill each machine normally first. / 保留机器现有原料，请先正常添加口味。", "", true, "Stock / 原料"),
        new("lc_refill_softserve", "Refill soft-serve stock / 补满软冰淇淋原料", "Refill nonempty soft-serve reservoirs once, preserving flavors. Does not refill scoop pans or other supplies. / 单次补满非空软冰淇淋机器并保留口味。", "", false, "Stock / 原料")
    ]);

    internal static bool SupportsAssembly(string hash) => string.Equals(hash, SupportedAssemblyHash, StringComparison.OrdinalIgnoreCase);
    private static void CheckClosed(string root)
    {
        if (Processes(root, Executable).Length != 0) throw new IOException("Close Livin' the Cream before installing or removing trainer files. / 请先关闭游戏。");
    }
    internal bool PackageMatches(string root) => LivinCreamInstaller.ReadPackage(package).All(file =>
        File.Exists(SafePath(root, file.Path)) && string.Equals(Hash(SafePath(root, file.Path)), file.Sha256, StringComparison.OrdinalIgnoreCase));
    public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.Run(() => {
        try {
            var found = FindSteam(Definition.AppId, Executable, directory);
            var supported = SupportsAssembly(Hash(SafePath(found.Directory, AssemblyPath)));
            var installed = LivinCreamInstaller.Installed(found.Directory);
            var matching = supported && PackageMatches(found.Directory);
            return new GameInstallation(found.Directory, found.Build, true, installed, Processes(found.Directory, Executable).Length > 0, supported,
                !supported ? "Unsupported game build. Check this game's trainer update. / 游戏版本不受支持，请检查修改器更新。"
                : matching ? "Trainer installed. Launch normally and load your shop. / 修改器已安装，请正常启动并读取店铺。"
                : "Install / update this trainer with the game closed. / 请关闭游戏后安装或更新修改器。");
        } catch (Exception ex) { return new GameInstallation(directory ?? "", "", false, false, false, false, ex.Message); }
    });
    public async Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null)
    {
        if (!await operation.WaitAsync(0)) throw new InvalidOperationException("A Livin' the Cream operation is already running. Wait and retry.");
        try {
            await Task.Run(() => {
                CheckClosed(installation.Directory);
                if (!SupportsAssembly(Hash(SafePath(installation.Directory, AssemblyPath)))) throw new IOException("Unsupported game build. Check this game's trainer update before installing.");
                LivinCreamInstaller.Install(package, installation.Directory, () => CheckClosed(installation.Directory), progress);
                if (!PackageMatches(installation.Directory)) throw new IOException("Trainer installation verification failed. Retry Install / update.");
            });
        } finally { operation.Release(); }
    }
    public async Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null)
    {
        if (!await operation.WaitAsync(0)) throw new InvalidOperationException("A Livin' the Cream operation is already running. Wait and retry.");
        try { await Task.Run(() => LivinCreamInstaller.Remove(installation.Directory, () => CheckClosed(installation.Directory), progress)); }
        finally { operation.Release(); }
    }
    public Task LaunchAsync(GameInstallation installation)
    {
        var registered = FindSteam(Definition.AppId, Executable);
        if (!string.Equals(Path.GetFullPath(registered.Directory), Path.GetFullPath(installation.Directory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Selected folder differs from Steam's active installation. Scan Steam before launching.");
        return LaunchSteam(Definition.AppId);
    }
    public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.Run(() => {
        try {
            var processes = Processes(installation.Directory, Executable);
            if (processes.Length != 1) return TrainerStatus.Offline("Launch Livin' the Cream normally, then load your shop. / 请启动游戏并读取店铺。");
            var path = SafePath(installation.Directory, IpcRoot + "status.json");
            if (!File.Exists(path)) return TrainerStatus.Offline("Waiting for trainer startup. If this persists, close the game and retry Install / update.");
            var modified = File.GetLastWriteTimeUtc(path);
            if (DateTime.UtcNow - modified > TimeSpan.FromSeconds(6) || modified < processes[0].Started || modified > DateTime.UtcNow.AddSeconds(2))
                return TrainerStatus.Offline("Waiting for a fresh trainer heartbeat.");
            if (new FileInfo(path).Length > 65536) return TrainerStatus.Offline("Invalid trainer status size. Reinstall this trainer.");
            var json = ReadJson(path); var session = Text(json, "session_id");
            if (Text(json, "schema_version") != "1" || Text(json, "trainer_version") != TrainerVersion
                || Text(json, "process_id") != processes[0].Id.ToString(CultureInfo.InvariantCulture)
                || !Guid.TryParseExact(session, "N", out _) || !SupportsAssembly(Text(json, "assembly_sha256")))
                return TrainerStatus.Offline("Trainer identity/build mismatch. Close the game and update this trainer.");
            return new TrainerStatus(true, Bool(json, "ready"), Text(json, "message"), session,
                Bools(Object(json, "enabled")), Strings(Object(json, "values")), Text(json, "last_command"));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) {
            return TrainerStatus.Offline("Cannot read trainer status: " + ex.Message);
        }
    });
    public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
        => SendCoreAsync(installation, status, featureId, enabled, null);
    public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value)
        => SendCoreAsync(installation, status, featureId, null, value);
    private async Task SendCoreAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled, decimal? value)
    {
        if (!await operation.WaitAsync(0)) throw new InvalidOperationException("A Livin' the Cream operation is already running. Wait and retry.");
        string? commandPath = null;
        try {
            var feature = Definition.Features.SingleOrDefault(x => x.Id == featureId);
            if (featureId != "disable_all" && feature == null) throw new ArgumentException("Unknown trainer control.");
            if (feature?.IsToggle == true ? !enabled.HasValue || value.HasValue : enabled.HasValue) throw new ArgumentException("Invalid toggle state.");
            if (feature?.Number is { } number) {
                if (!value.HasValue || value < number.Minimum || value > number.Maximum || decimal.Truncate(value.Value) != value) throw new ArgumentOutOfRangeException(nameof(value), "Enter a whole number within the allowed range.");
            } else if (value.HasValue) throw new ArgumentException("Unexpected numeric value.");
            var fresh = await ReadStatusAsync(installation);
            if (!fresh.Connected || fresh.SessionId != status.SessionId || (!fresh.Ready && !(featureId == "disable_all" || feature?.IsToggle == true && enabled == false)))
                throw new InvalidOperationException("Load your shop, wait for restoration to finish, and reconnect before using this control. / 请读取店铺并等待加载完成。");
            if (!SupportsAssembly(Hash(SafePath(installation.Directory, AssemblyPath))) || !PackageMatches(installation.Directory))
                throw new IOException("Game/trainer files changed. Close the game and retry Install / update.");
            var id = Guid.NewGuid().ToString("N");
            commandPath = SafePath(installation.Directory, IpcRoot + "commands/" + id + ".json");
            var responsePath = SafePath(installation.Directory, IpcRoot + "responses/" + id + ".json");
            WriteJson(commandPath, new { schema_version = 1, session_id = fresh.SessionId, request_id = id, action = featureId, enabled, value, created_utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            for (var i = 0; i < 32; i++) {
                await Task.Delay(250);
                if (File.Exists(responsePath)) {
                    if (new FileInfo(responsePath).Length > 65536) throw new IOException("Invalid trainer response size.");
                    var response = ReadJson(responsePath);
                    if (Text(response, "schema_version") != "1" || Text(response, "session_id") != fresh.SessionId || Text(response, "request_id") != id) throw new IOException("Mismatched trainer response.");
                    File.Delete(responsePath);
                    if (!Bool(response, "success")) throw new InvalidOperationException(Text(response, "message", "The game rejected this control."));
                    return;
                }
                var next = await ReadStatusAsync(installation);
                if (!next.Connected || next.SessionId != fresh.SessionId) throw new IOException("Shop session changed. Check the in-game value before retrying.");
            }
            throw new TimeoutException("No acknowledgement received. The action may have applied; check the in-game value before repeating it.");
        } finally {
            try { if (commandPath != null && File.Exists(commandPath)) File.Delete(commandPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally { operation.Release(); }
        }
    }
}
