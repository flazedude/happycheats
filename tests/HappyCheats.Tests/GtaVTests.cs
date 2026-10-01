using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class GtaVTests
{
    private static int passed, failed;
    private const string Session = "0123456789abcdef0123456789abcdef";
    private const string Request = "abcdef0123456789abcdef0123456789";
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    public static int RunAll()
    {
        passed = failed = 0;
        Run("GTA Enhanced exact version compatibility rejects newer and Legacy versions", () => {
            Assert(GtaVModule.IsSupportedVersion("1.0.1158.13"));
            Assert(GtaVModule.IsSupportedVersion("1.0.1158.16"));
            foreach (var version in new[] { "1.0.1158.17", "1.0.1158.12", "1.0.3788.0", "1.0.1158.13-beta", "01.0.1158.13", "", null })
                Assert(!GtaVModule.IsSupportedVersion(version));
        });
        Run("GTA module contains seven Story Mode controls and no F5 collision", () => {
            var definition = new GtaVModule(".").Definition;
            Assert(definition.Id == "gta-v-enhanced" && definition.AppId == "3240220");
            Assert(definition.Features.Count == 7 && definition.Features.Select(feature => feature.Id).SequenceEqual(GtaVModule.FeatureIds));
            Assert(definition.Features.Count(feature => feature.IsToggle) == 3);
            Assert(definition.Features.All(feature => feature.Hotkey != "F5"));
        });
        Run("GTA verified installation and removal preserve shared loaders and foreign mods", () => {
            using var fixture = new Fixture();
            fixture.GameFile("AnotherTrainer.asi", "foreign trainer");
            GtaVInstaller.Install(fixture.Package, fixture.Game, () => { });
            Assert(GtaVInstaller.Installed(fixture.Game) && GtaVInstaller.MatchesPackage(fixture.Package, fixture.Game));
            fixture.GameFile(GtaVModule.BridgeRoot + "status.json", "{}");
            fixture.GameFile(GtaVModule.BridgeRoot + "command.json", "{}");
            GtaVInstaller.Remove(fixture.Game, () => { });
            Assert(!GtaVInstaller.Installed(fixture.Game));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "ScriptHookV.dll")) == "existing official hook");
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "dinput8.dll")) == "existing ASI loader");
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "AnotherTrainer.asi")) == "foreign trainer");
            Assert(!File.Exists(Path.Combine(fixture.Game, GtaVModule.BridgeRoot + "status.json")));
        });
        Run("GTA missing or empty Script Hook prerequisites block every write", () => {
            foreach (var prerequisite in new[] { "ScriptHookV.dll", "dinput8.dll" }) {
                using var fixture = new Fixture();
                File.Delete(Path.Combine(fixture.Game, prerequisite));
                var original = fixture.GameHashes();
                Fails(() => GtaVInstaller.Install(fixture.Package, fixture.Game, () => { }));
                Assert(fixture.GameHashes().SequenceEqual(original));
                fixture.GameFile(prerequisite, "");
                Assert(!GtaVInstaller.PrerequisitesAvailable(fixture.Game));
            }
        });
        Run("GTA foreign trainer is preserved and blocks every payload write", () => {
            using var fixture = new Fixture(); fixture.GameFile(GtaVInstaller.DllPath, "foreign ASI");
            var original = fixture.GameHashes();
            Fails(() => GtaVInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(fixture.GameHashes().SequenceEqual(original));
        });
        Run("GTA corrupted payload is rejected before writes", () => {
            using var fixture = new Fixture(); fixture.PayloadFile(GtaVInstaller.DllPath, "corrupt");
            var original = fixture.GameHashes();
            Fails(() => GtaVInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(fixture.GameHashes().SequenceEqual(original));
        });
        Run("GTA manifest cannot take ownership of shared loaders or escape root", () => {
            foreach (var path in new[] { "dinput8.dll", "ScriptHookV.dll", "../outside.asi", "AnotherTrainer.asi" }) {
                using var fixture = new Fixture();
                fixture.Manifest(new[] { new GtaVInstaller.Entry(path, new string('A', 64)) });
                Fails(() => GtaVInstaller.ReadPackage(fixture.Package));
            }
            using var missing = new Fixture(); missing.Manifest(Array.Empty<GtaVInstaller.Entry>());
            Fails(() => GtaVInstaller.ReadPackage(missing.Package));
        });
        Run("GTA partial installation rolls back and preserves loaders", () => {
            using var fixture = new Fixture(); var original = fixture.GameHashes(); var calls = 0;
            Fails(() => GtaVInstaller.Install(fixture.Package, fixture.Game, () => { if (++calls == 4) throw new IOException("game started"); }));
            Assert(fixture.GameHashes().SequenceEqual(original));
        });
        Run("GTA running game blocks install and removal", () => {
            using var fixture = new Fixture(); var original = fixture.GameHashes();
            Fails(() => GtaVInstaller.Install(fixture.Package, fixture.Game, () => throw new IOException("running")));
            Assert(fixture.GameHashes().SequenceEqual(original));
            GtaVInstaller.Install(fixture.Package, fixture.Game, () => { }); original = fixture.GameHashes();
            Fails(() => GtaVInstaller.Remove(fixture.Game, () => throw new IOException("running")));
            Assert(fixture.GameHashes().SequenceEqual(original));
        });
        Run("GTA modified owned ASI and its status survive removal", () => {
            using var fixture = new Fixture(); GtaVInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile(GtaVInstaller.DllPath, "user edited"); fixture.GameFile(GtaVModule.BridgeRoot + "status.json", "user status");
            Fails(() => GtaVInstaller.Install(fixture.Package, fixture.Game, () => { }));
            GtaVInstaller.Remove(fixture.Game, () => { });
            Assert(File.ReadAllText(Path.Combine(fixture.Game, GtaVInstaller.DllPath)) == "user edited");
            Assert(File.ReadAllText(Path.Combine(fixture.Game, GtaVModule.BridgeRoot + "status.json")) == "user status");
        });
        Run("GTA identical foreign ASI remains unowned", () => {
            using var fixture = new Fixture(); fixture.GameFile(GtaVInstaller.DllPath, "test ASI payload");
            GtaVInstaller.Install(fixture.Package, fixture.Game, () => { });
            GtaVInstaller.Remove(fixture.Game, () => { });
            Assert(File.Exists(Path.Combine(fixture.Game, GtaVInstaller.DllPath)));
        });
        Run("GTA owned ASI can be updated with a verified package", () => {
            using var fixture = new Fixture(); GtaVInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.PayloadFile(GtaVInstaller.DllPath, "new ASI payload"); fixture.Manifest();
            GtaVInstaller.Install(fixture.Package, fixture.Game, () => { });
            Assert(GtaVInstaller.MatchesPackage(fixture.Package, fixture.Game));
        });
        Run("GTA discovery reports incompatible installed build without writing", () => {
            using var fixture = new Fixture(); var module = fixture.Module("1.0.1158.16"); var original = fixture.GameHashes();
            module.GameHashProvider = _ => new string('0', 64);
            var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult();
            Assert(installation.IsInstalled && !installation.Compatible && installation.Message.Contains("1.0.1158.16") && installation.Message.Contains("pinned"));
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
            Assert(fixture.GameHashes().SequenceEqual(original));
        });
        Run("GTA missing prerequisites are distinct from a missing game", () => {
            using var fixture = new Fixture(); File.Delete(Path.Combine(fixture.Game, "ScriptHookV.dll"));
            var installation = fixture.Module().DiscoverAsync(fixture.Game).GetAwaiter().GetResult();
            Assert(installation.IsInstalled && installation.Compatible && installation.Message.Contains("Install / update"));
        });
        Run("GTA install rechecks actual executable version after discovery", () => {
            using var fixture = new Fixture(); var module = fixture.Module();
            var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult(); Assert(installation.Compatible);
            module.VersionProvider = _ => "1.0.1158.17"; var original = fixture.GameHashes();
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
            Assert(fixture.GameHashes().SequenceEqual(original));
        });
        Run("GTA wrong Script Hook runtime blocks discovery and installation", () => {
            using var fixture = new Fixture(); var module = fixture.Module();
            fixture.GameFile("ScriptHookV.dll", "unrecognized hook");
            var original = fixture.GameHashes();
            var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult();
            Assert(installation.IsInstalled && !installation.Compatible && installation.Message.Contains("ScriptHookV.dll"));
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
            Assert(fixture.GameHashes().SequenceEqual(original));
        });
        Run("GTA install rechecks hook identity after discovery", () => {
            using var fixture = new Fixture(); var module = fixture.Module();
            var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult(); Assert(installation.Compatible);
            fixture.GameFile("ScriptHookV.dll", "changed unrecognized hook"); var original = fixture.GameHashes();
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
            Assert(fixture.GameHashes().SequenceEqual(original));
        });
        Run("GTA latest target requires the reviewed executable hash", () => {
            using var fixture = new Fixture(); var module = fixture.Module(GtaVModule.LatestSupportedGameVersion);
            Assert(module.GameCompatible(fixture.Game));
            var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult(); Assert(installation.Compatible);
            module.GameHashProvider = _ => new string('0', 64);
            var original = fixture.GameHashes();
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
            Assert(fixture.Downloads == 0 && fixture.GameHashes().SequenceEqual(original));
        });
        Run("GTA hash cache follows stable file stamps and supports forced revalidation", () => {
            using var fixture = new Fixture(); var module = fixture.Module(GtaVModule.LatestSupportedGameVersion); var reads = 0;
            module.GameHashProvider = _ => { reads++; return GtaVModule.LatestExecutableHash; };
            Assert(module.GameCompatible(fixture.Game) && module.GameCompatible(fixture.Game) && reads == 1);
            fixture.GameFile(GtaVModule.Executable, "fixture executable with a new length");
            Assert(module.GameCompatible(fixture.Game) && reads == 2);
            var executable = Path.Combine(fixture.Game, GtaVModule.Executable);
            File.SetLastWriteTimeUtc(executable, File.GetLastWriteTimeUtc(executable).AddSeconds(2));
            Assert(module.GameCompatible(fixture.Game) && reads == 3);
            Assert(module.GameCompatible(fixture.Game, force: true) && reads == 4);
            module.GameHashProvider = _ => {
                File.AppendAllText(executable, "changed during read"); return GtaVModule.LatestExecutableHash;
            };
            Fails(() => module.InspectGameHash(fixture.Game, force: true));
            module.GameHashProvider = _ => { reads++; return GtaVModule.LatestExecutableHash; };
            Assert(module.GameCompatible(fixture.Game) && reads == 5);
        });
        Run("GTA legacy missing dependencies remain installable and are acquired together", () => {
            using var fixture = new Fixture();
            File.Delete(Path.Combine(fixture.Game, "ScriptHookV.dll")); File.Delete(Path.Combine(fixture.Game, "dinput8.dll"));
            var module = fixture.Module(); var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult();
            Assert(installation.Compatible && !module.Compatible(fixture.Game));
            Assert(!module.ReadStatusAsync(installation).GetAwaiter().GetResult().Connected);
            module.InstallAsync(installation).GetAwaiter().GetResult();
            Assert(fixture.Downloads == 1 && module.Compatible(fixture.Game) && GtaVInstaller.MatchesPackage(fixture.Package, fixture.Game));
        });
        Run("GTA latest backend sets up only its loader and preserves an unrelated hook", () => {
            using var fixture = new Fixture(); fixture.GameFile("ScriptHookV.dll", "unrelated hook remains untouched");
            File.Delete(Path.Combine(fixture.Game, "dinput8.dll"));
            var module = fixture.Module(GtaVModule.LatestSupportedGameVersion);
            var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult();
            Assert(installation.Compatible && installation.Message.Contains("included experimental Story Mode backend"));
            module.InstallAsync(installation).GetAwaiter().GetResult();
            Assert(fixture.Downloads == 1 && module.Compatible(fixture.Game));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "ScriptHookV.dll")) == "unrelated hook remains untouched");
            Assert(GtaVInstaller.MatchesPackage(fixture.Package, fixture.Game));
        });
        Run("GTA latest backend installs and connects without any Script Hook V file", () => {
            using var fixture = new Fixture(); File.Delete(Path.Combine(fixture.Game, "ScriptHookV.dll"));
            var module = fixture.Module(GtaVModule.LatestSupportedGameVersion);
            var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult();
            module.InstallAsync(installation).GetAwaiter().GetResult();
            Assert(fixture.Downloads == 0 && !File.Exists(Path.Combine(fixture.Game, "ScriptHookV.dll")));
            var now = DateTime.UtcNow; module.ProcessProvider = _ => new[] { (321, now.AddSeconds(-30)) }; fixture.WriteStatus(Status(now));
            Assert(module.ReadStatusAsync(installation).GetAwaiter().GetResult().Ready);
        });
        Run("GTA unknown loader is preserved and blocks both backends before acquisition", () => {
            foreach (var version in new[] { GtaVModule.LegacySupportedVersion, GtaVModule.LatestSupportedGameVersion }) {
                using var fixture = new Fixture(); fixture.GameFile("dinput8.dll", "unrecognized loader");
                var module = fixture.Module(version); var original = fixture.GameHashes();
                var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult();
                Assert(installation.IsInstalled && !installation.Compatible && installation.Message.Contains("dinput8.dll"));
                Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
                Assert(fixture.Downloads == 0 && fixture.GameHashes().SequenceEqual(original));
            }
        });
        Run("GTA source archive and ownership preflight precede runtime acquisition", () => {
            foreach (var conflict in new[] { "source", "foreign trainer", "parent file" }) {
                using var fixture = new Fixture();
                File.Delete(Path.Combine(fixture.Game, "ScriptHookV.dll")); File.Delete(Path.Combine(fixture.Game, "dinput8.dll"));
                if (conflict == "source") File.Delete(Path.Combine(fixture.Package, GtaVInstaller.SourcePath));
                else if (conflict == "foreign trainer") fixture.GameFile(GtaVInstaller.DllPath, "foreign trainer");
                else fixture.GameFile(GtaVInstaller.TrainerRoot.TrimEnd('/'), "foreign file blocking directory");
                var module = fixture.Module(); var original = fixture.GameHashes();
                var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult();
                Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
                Assert(fixture.Downloads == 0 && fixture.GameHashes().SequenceEqual(original));
            }
        });
        Run("GTA runtime download rechecks build, executable hash and game closure before writes", () => {
            foreach (var change in new[] { "version", "hash", "process" }) {
                using var fixture = new Fixture(); File.Delete(Path.Combine(fixture.Game, "dinput8.dll"));
                var module = fixture.Module(GtaVModule.LatestSupportedGameVersion);
                var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult(); var original = fixture.GameHashes();
                fixture.OnDownload = () => {
                    if (change == "version") module.VersionProvider = _ => GtaVModule.LegacySupportedVersion;
                    else if (change == "hash") module.GameHashProvider = _ => new string('0', 64);
                    else module.ProcessProvider = _ => new[] { (321, DateTime.UtcNow) };
                };
                Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
                Assert(fixture.Downloads == 1 && fixture.GameHashes().SequenceEqual(original));
            }
        });
        Run("GTA runtime transaction restores dependencies after trainer rollback", () => {
            using var fixture = new Fixture(); fixture.GameFile("ScriptHookV.dll", "recognized old official hook");
            File.Delete(Path.Combine(fixture.Game, "dinput8.dll")); var module = fixture.Module();
            var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult(); Assert(installation.Compatible);
            var progress = new InlineProgress(message => { if (message == "Installing ThirdPartyNotices.txt") throw new IOException("fixture trainer failure"); });
            Fails(() => module.InstallAsync(installation, progress).GetAwaiter().GetResult());
            Assert(fixture.Downloads == 1);
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "ScriptHookV.dll")) == "recognized old official hook");
            Assert(!File.Exists(Path.Combine(fixture.Game, "dinput8.dll")));
            Assert(GtaVInstaller.RequiredFiles.All(path => !File.Exists(Path.Combine(fixture.Game, path))));
            Assert(!File.Exists(Path.Combine(fixture.Game, GtaVInstaller.ReceiptPath)));
            Assert(Directory.GetFiles(Path.Combine(fixture.Game, "happycheats-runtime-backups"), "*.bak", SearchOption.AllDirectories).Length == 1);
        });
        Run("GTA old two-file receipts upgrade with owned source and remain removable", () => {
            using var fixture = new Fixture();
            var oldFiles = GtaVInstaller.RequiredFiles.Where(path => path != GtaVInstaller.SourcePath).ToArray();
            foreach (var path in oldFiles) fixture.GameFile(path, File.ReadAllText(Path.Combine(fixture.Package, path)));
            WriteJson(Path.Combine(fixture.Game, GtaVInstaller.ReceiptPath), new GtaVInstaller.Receipt(1, "gta-v-enhanced",
                oldFiles.Select(path => new GtaVInstaller.Entry(path, Hash(Path.Combine(fixture.Game, path)))).ToList()));
            Assert(GtaVInstaller.Installed(fixture.Game) && !GtaVInstaller.MatchesPackage(fixture.Package, fixture.Game));
            GtaVInstaller.Install(fixture.Package, fixture.Game, () => { });
            Assert(GtaVInstaller.MatchesPackage(fixture.Package, fixture.Game));
            GtaVInstaller.Remove(fixture.Game, () => { });
            Assert(GtaVInstaller.RequiredFiles.All(path => !File.Exists(Path.Combine(fixture.Game, path))));
        });
        Run("GTA status requires PID identity, safe session, fresh clock and mandatory online flag", () => {
            Assert(Parse(Status()).Status.Ready);
            foreach (var pair in new KeyValuePair<string, object>[] {
                new("schema_version", 2), new("version", "0.0.1"), new("process_id", 777), new("session_id", "invalid-session"),
                new("updated_utc", Epoch(Now.AddSeconds(-7))), new("updated_utc", Epoch(Now.AddSeconds(3))),
                new("online", "false"), new("ready", "true"), new("available", new object[0]), new("enabled", new object[0]) }) {
                var status = Status(); status[pair.Key] = pair.Value; Assert(!Parse(status).Status.Connected);
            }
            var missing = Status(); missing.Remove("online"); Assert(!Parse(missing).Status.Connected);
            Assert(!Parse(Status(), Now.AddSeconds(-7)).Status.Connected);
            Assert(!Parse(Status(), Now.AddSeconds(3)).Status.Connected);
            Assert(!Parse(Status(), Now.AddSeconds(-31)).Status.Connected);
        });
        Run("GTA Online status clears toggles and rejects every command including cleanup", () => {
            var json = Status(); json["online"] = true;
            var current = Parse(json).Status;
            Assert(current.Connected && !current.Ready && current.Enabled.Count == 0 && current.Values["gta_online"] == bool.TrueString);
            Assert(GtaVModule.FeatureIds.All(id => current.Values["available_" + id] == bool.FalseString));
            Fails(() => GtaVModule.CreateCommand(current, current, "gta_health", true, Request, Epoch(Now)));
            Fails(() => GtaVModule.CreateCommand(current, current, "gta_health", false, Request, Epoch(Now)));
            Fails(() => GtaVModule.CreateCommand(current, current, "disable_all", null, Request, Epoch(Now)));
        });
        Run("GTA commands validate identity, readiness, availability and toggle shape", () => {
            var current = Parse(Status()).Status;
            var command = GtaVModule.CreateCommand(current, current, "gta_health", true, Request, Epoch(Now));
            Assert((bool)command["enabled"] && (string)command["action"] == "gta_health" && (long)command["created_utc"] == Epoch(Now));
            Assert(!GtaVModule.CreateCommand(current, current, "gta_refill", null, Request, Epoch(Now)).ContainsKey("enabled"));
            Fails(() => GtaVModule.CreateCommand(current, current with { SessionId = new string('f', 32) }, "gta_health", true, Request, Epoch(Now)));
            Fails(() => GtaVModule.CreateCommand(current, current, "unknown", null, Request, Epoch(Now)));
            Fails(() => GtaVModule.CreateCommand(current, current, "gta_health", null, Request, Epoch(Now)));
            Fails(() => GtaVModule.CreateCommand(current, current, "gta_refill", true, Request, Epoch(Now)));
            Fails(() => GtaVModule.CreateCommand(current, current, "gta_refill", null, "bad", Epoch(Now)));
            Fails(() => GtaVModule.CreateCommand(current, current, "gta_refill", null, Request, 0));
            var unavailableJson = Status(); unavailableJson["available"] = new Dictionary<string, bool>();
            var unavailable = Parse(unavailableJson).Status;
            Fails(() => GtaVModule.CreateCommand(unavailable, unavailable, "gta_refill", null, Request, Epoch(Now)));
            var loading = current with { Ready = false };
            Fails(() => GtaVModule.CreateCommand(loading, loading, "gta_health", true, Request, Epoch(Now)));
            Assert((bool)GtaVModule.CreateCommand(loading, loading, "gta_health", false, Request, Epoch(Now))["enabled"] == false);
            Assert((string)GtaVModule.CreateCommand(loading, loading, "disable_all", null, Request, Epoch(Now))["action"] == "disable_all");
        });
        Run("GTA acknowledgement cannot cross sessions, accept rejection or ignore Online transition", () => {
            var status = Status(); status["last_command"] = Request; var success = Parse(status);
            Assert(GtaVModule.AcceptAcknowledgement(success, Session, Request));
            Assert(!GtaVModule.AcceptAcknowledgement(success, Session, new string('f', 32)));
            Assert(!GtaVModule.AcceptAcknowledgement(new(TrainerStatus.Offline(), false, ""), Session, Request));
            Fails(() => GtaVModule.AcceptAcknowledgement(success, new string('f', 32), Request));
            Fails(() => GtaVModule.AcceptAcknowledgement(success with { LastSuccess = false, LastMessage = "not in a vehicle" }, Session, Request));
            status["online"] = true;
            Fails(() => GtaVModule.AcceptAcknowledgement(Parse(status), Session, Request));
        });
        Run("GTA oversized and malformed status files are rejected", () => {
            using var fixture = new Fixture(); var path = Path.Combine(fixture.Game, "status.json");
            File.WriteAllText(path, new string(' ', 16385)); Assert(!GtaVModule.ReadSnapshotFile(path, Now.AddSeconds(-30), 321).Status.Connected);
            File.WriteAllText(path, "{"); Assert(!GtaVModule.ReadSnapshotFile(path, Now.AddSeconds(-30), 321).Status.Connected);
        });
        Run("GTA real mailbox action is dispatched once and waits for acknowledgement", () => {
            using var fixture = new Fixture(); var now = DateTime.UtcNow; var module = fixture.Module();
            module.ProcessProvider = _ => new[] { (321, now.AddSeconds(-30)) };
            var status = Status(now); fixture.WriteStatus(status);
            var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult();
            var current = module.ReadStatusAsync(installation).GetAwaiter().GetResult(); Assert(current.Ready);
            var send = module.SendAsync(installation, current, "gta_refill");
            var commandPath = Path.Combine(fixture.Game, GtaVModule.BridgeRoot + "command.json");
            Assert(SpinWait.SpinUntil(() => File.Exists(commandPath), TimeSpan.FromSeconds(2)));
            using var command = JsonDocument.Parse(File.ReadAllText(commandPath));
            Assert(command.RootElement.GetProperty("action").GetString() == "gta_refill");
            Assert(!command.RootElement.TryGetProperty("enabled", out _));
            status["last_command"] = command.RootElement.GetProperty("request_id").GetString()!;
            status["updated_utc"] = Epoch(DateTime.UtcNow); fixture.WriteStatus(status);
            send.GetAwaiter().GetResult(); Assert(!File.Exists(commandPath));
        });
        Console.WriteLine($"GTA V Enhanced tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static long Epoch(DateTime time) => new DateTimeOffset(time).ToUnixTimeSeconds();
    private static Dictionary<string, object> Status(DateTime? time = null) => new() {
        ["schema_version"] = 1, ["version"] = GtaVModule.TrainerVersion, ["session_id"] = Session, ["process_id"] = 321,
        ["updated_utc"] = Epoch(time ?? Now), ["online"] = false, ["ready"] = true, ["message"] = "Story Mode ready",
        ["enabled"] = new Dictionary<string, bool> { ["gta_health"] = true }, ["values"] = new Dictionary<string, string>(),
        ["available"] = GtaVModule.FeatureIds.ToDictionary(id => id, _ => true),
        ["last_command"] = "", ["last_success"] = true, ["last_message"] = "Applied"
    };
    private static GtaVModule.Snapshot Parse(Dictionary<string, object> status, DateTime? modified = null) {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(status));
        return GtaVModule.ParseSnapshot(json.RootElement, modified ?? Now, Now.AddSeconds(-30), 321, Now);
    }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation rejection."); }
    private static void Run(string name, Action test) {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
    }
    private sealed class InlineProgress(Action<string> report) : IProgress<string> { public void Report(string value) => report(value); }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "HappyCheats-gta-tests-" + Guid.NewGuid().ToString("N"));
        public string Game => Path.Combine(root, "game");
        public string Package => Path.Combine(root, "payload", "gta-v-enhanced");
        public int Downloads { get; private set; }
        public Action? OnDownload { get; set; }
        private static readonly byte[] Hook = Encoding.UTF8.GetBytes("existing official hook");
        private static readonly byte[] Loader = Encoding.UTF8.GetBytes("existing ASI loader");
        private static readonly byte[] OldHook = Encoding.UTF8.GetBytes("recognized old official hook");
        public Fixture() {
            GameFile(GtaVModule.Executable, "fixture executable");
            GameFile("ScriptHookV.dll", "existing official hook"); GameFile("dinput8.dll", "existing ASI loader");
            PayloadFile(GtaVInstaller.DllPath, "test ASI payload"); PayloadFile(GtaVInstaller.NoticePath, "test notices");
            PayloadFile(GtaVInstaller.SourcePath, "GPL source fixture"); Manifest();
        }
        public GtaVModule Module(string version = GtaVModule.LegacySupportedVersion) => new(root) {
            VersionProvider = _ => version, GameHashProvider = _ => GtaVModule.LatestExecutableHash,
            RuntimeSetup = CreateRuntime(),
            ProcessProvider = _ => Array.Empty<(int, DateTime)>()
        };
        private GtaVRuntimeSetup CreateRuntime() {
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true)) {
                using (var output = zip.CreateEntry("bin/ScriptHookV.dll").Open()) output.Write(Hook);
                using (var output = zip.CreateEntry("bin/dinput8.dll").Open()) output.Write(Loader);
            }
            var bytes = buffer.ToArray();
            string Digest(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
            return new(new(Digest(bytes), Digest(Hook), Digest(Loader), Digest(OldHook)), Path.Combine(root, "cache"), _ => {
                Downloads++; OnDownload?.Invoke(); return Task.FromResult(bytes);
            });
        }
        public void GameFile(string path, string text) => Put(Game, path, text);
        public void PayloadFile(string path, string text) => Put(Package, path, text);
        private static void Put(string directory, string path, string text) {
            var full = SafePath(directory, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, text);
        }
        public void Manifest(IEnumerable<GtaVInstaller.Entry>? entries = null) => WriteJson(Path.Combine(Package, "package.json"), new {
            schemaVersion = 1, game = "gta-v-enhanced", version = GtaVModule.TrainerVersion,
            files = (entries ?? GtaVInstaller.RequiredFiles.Select(path => new GtaVInstaller.Entry(path, Hash(Path.Combine(Package, path))))).Select(file => new { path = file.Path, sha256 = file.Sha256 })
        });
        public void WriteStatus(Dictionary<string, object> status) => WriteJson(Path.Combine(Game, GtaVModule.BridgeRoot + "status.json"), status);
        public string[] GameHashes() => Directory.GetFiles(Game, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).Select(path => Path.GetRelativePath(Game, path) + ":" + Hash(path)).ToArray();
        public void Dispose() {
            var full = Path.GetFullPath(root);
            if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("HappyCheats-gta-tests-", StringComparison.Ordinal)) Directory.Delete(full, true);
        }
    }
}
