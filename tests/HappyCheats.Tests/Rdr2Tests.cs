using System.Text;
using System.Text.Json;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class Rdr2Tests
{
    private static int passed, failed;
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private const string Session = "0123456789abcdef0123456789abcdef";
    private const string Request = "abcdef0123456789abcdef0123456789";

    public static int RunAll()
    {
        passed = failed = 0;
        Run("RDR2 verified install and removal preserve prerequisite loaders and foreign mods", () => {
            using var f = new Fixture(); f.GameFile("ScriptHookRDR2.dll", "existing hook"); f.GameFile("dinput8.dll", "existing loader"); f.GameFile("Rampage.asi", "other trainer");
            Rdr2Installer.Install(f.Package, f.Game, () => { });
            Assert(Rdr2Installer.Installed(f.Game) && Rdr2Installer.MatchesPackage(f.Package, f.Game));
            Rdr2Installer.Remove(f.Game, () => { });
            Assert(!Rdr2Installer.Installed(f.Game));
            Assert(File.ReadAllText(f.Path("ScriptHookRDR2.dll")) == "existing hook" && File.ReadAllText(f.Path("dinput8.dll")) == "existing loader" && File.ReadAllText(f.Path("Rampage.asi")) == "other trainer");
        });
        Run("RDR2 foreign ASI blocks every write", () => {
            using var f = new Fixture(); f.GameFile(Rdr2Installer.Plugin, "foreign");
            Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => { }));
            Assert(f.GameFiles().Length == 1 && File.ReadAllText(f.Path(Rdr2Installer.Plugin)) == "foreign");
        });
        Run("RDR2 identical foreign ASI remains unowned", () => {
            using var f = new Fixture(); f.GameFile(Rdr2Installer.Plugin, "trainer");
            Rdr2Installer.Install(f.Package, f.Game, () => { }); Rdr2Installer.Remove(f.Game, () => { });
            Assert(File.ReadAllText(f.Path(Rdr2Installer.Plugin)) == "trainer");
        });
        Run("RDR2 modified owned ASI survives update and removal", () => {
            using var f = new Fixture(); Rdr2Installer.Install(f.Package, f.Game, () => { }); f.GameFile(Rdr2Installer.Plugin, "user change");
            Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => { })); Rdr2Installer.Remove(f.Game, () => { });
            Assert(File.ReadAllText(f.Path(Rdr2Installer.Plugin)) == "user change");
        });
        Run("RDR2 owned update and repeated install retain removal ownership", () => {
            using var f = new Fixture(); Rdr2Installer.Install(f.Package, f.Game, () => { });
            f.PayloadFile(Rdr2Installer.Plugin, "new trainer"); f.Manifest();
            Rdr2Installer.Install(f.Package, f.Game, () => { }); Rdr2Installer.Install(f.Package, f.Game, () => { });
            Assert(File.ReadAllText(f.Path(Rdr2Installer.Plugin)) == "new trainer");
            Rdr2Installer.Remove(f.Game, () => { }); Assert(!File.Exists(f.Path(Rdr2Installer.Plugin)));
        });
        Run("RDR2 corrupt package is rejected without writes", () => {
            using var f = new Fixture(); f.PayloadFile(Rdr2Installer.Plugin, "corrupt");
            Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => { })); Assert(f.GameFiles().Length == 0);
        });
        Run("RDR2 manifest identity, version, file list and duplicate checks fail closed", () => {
            using var f = new Fixture();
            foreach (var manifest in new object[] {
                new { game_id = "other", app_id = 1174180, version = "0.1.0", files = f.Entries() },
                new { game_id = "red-dead-redemption-2", app_id = 1174180, version = "9.9.9", files = f.Entries() },
                new { game_id = "red-dead-redemption-2", app_id = 1, version = "0.1.0", files = f.Entries() },
                new { game_id = "red-dead-redemption-2", app_id = 1174180, version = "0.1.0", files = f.Entries().Take(1) },
                new { game_id = "red-dead-redemption-2", app_id = 1174180, version = "0.1.0", files = f.Entries().Concat(f.Entries()) },
                new { game_id = "red-dead-redemption-2", app_id = 1174180, version = "0.1.0", files = new[] { new { path = "../outside", sha256 = new string('A', 64) } } },
                new { game_id = "red-dead-redemption-2", app_id = 1174180, version = "0.1.0", files = new[] { new { path = "ScriptHookRDR2.dll", sha256 = new string('A', 64) } } }
            }) {
                WriteJson(System.IO.Path.Combine(f.Package, "package.json"), manifest);
                Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => { })); Assert(f.GameFiles().Length == 0);
            }
        });
        Run("RDR2 forged receipt cannot own prerequisite loaders", () => {
            using var f = new Fixture(); f.GameFile("dinput8.dll", "foreign loader");
            WriteJson(f.Path(Rdr2Installer.ReceiptPath), new Rdr2Installer.Receipt("red-dead-redemption-2", new() { new("dinput8.dll", Hash(f.Path("dinput8.dll"))) }));
            Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => { })); Fails(() => Rdr2Installer.Remove(f.Game, () => { }));
            Assert(File.ReadAllText(f.Path("dinput8.dll")) == "foreign loader");
        });
        Run("RDR2 receipt directory collision prevents all payload writes", () => {
            using var f = new Fixture(); Directory.CreateDirectory(f.Path(Rdr2Installer.ReceiptPath));
            Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => { })); Assert(f.GameFiles().Length == 0);
        });
        Run("RDR2 interrupted install rolls back completed files", () => {
            using var f = new Fixture(); var calls = 0;
            Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => { if (++calls == 4) throw new IOException("Game started"); }));
            Assert(calls == 4 && f.GameFiles().Length == 0);
        });
        Run("RDR2 rollback preserves concurrent user changes", () => {
            using var f = new Fixture(); var calls = 0;
            Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => {
                if (++calls == 4) { f.GameFile(Rdr2Installer.BridgeReadme, "concurrent edit"); throw new IOException("Game started"); }
            }));
            Assert(f.GameFiles().Length == 1 && File.ReadAllText(f.Path(Rdr2Installer.BridgeReadme)) == "concurrent edit");
        });
        Run("RDR2 failed upgrade restores prior owned payload", () => {
            using var f = new Fixture(); Rdr2Installer.Install(f.Package, f.Game, () => { });
            f.PayloadFile(Rdr2Installer.Plugin, "upgrade"); f.Manifest();
            Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => { }, new ImmediateProgress(_ => throw new IOException("Interrupted"))));
            Assert(File.ReadAllText(f.Path(Rdr2Installer.Plugin)) == "trainer");
            Rdr2Installer.Remove(f.Game, () => { }); Assert(!File.Exists(f.Path(Rdr2Installer.Plugin)));
        });
        Run("RDR2 failed upgrade preserves modified file and recovers its original", () => {
            using var f = new Fixture(); Rdr2Installer.Install(f.Package, f.Game, () => { });
            f.PayloadFile(Rdr2Installer.BridgeReadme, "updated bridge"); f.PayloadFile(Rdr2Installer.Plugin, "updated trainer"); f.Manifest();
            var calls = 0;
            Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => {
                if (++calls == 4) { f.GameFile(Rdr2Installer.BridgeReadme, "concurrent edit"); throw new IOException("Interrupted"); }
            }));
            Assert(File.ReadAllText(f.Path(Rdr2Installer.BridgeReadme)) == "concurrent edit");
            Assert(File.ReadAllText(f.Path(Rdr2Installer.Plugin)) == "trainer");
            Assert(Directory.EnumerateFiles(f.Path("happycheats-recovery"), "README.txt", SearchOption.AllDirectories).Single() is var recovery && File.ReadAllText(recovery) == "bridge");
        });
        Run("RDR2 previous staging file is preserved", () => {
            using var f = new Fixture(); f.GameFile(Rdr2Installer.Plugin + ".happycheats-stage", "old stage");
            Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => { }));
            Assert(f.GameFiles().Length == 1 && File.ReadAllText(f.Path(Rdr2Installer.Plugin + ".happycheats-stage")) == "old stage");
        });
        Run("RDR2 running game and missing ownership block modification", () => {
            using var f = new Fixture();
            Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => throw new IOException("Running")));
            Fails(() => Rdr2Installer.Remove(f.Game, () => { })); Assert(f.GameFiles().Length == 0);
        });
        Run("RDR2 unsupported executable discovery and install are read only", () => {
            using var f = new Fixture(); f.GameFile("RDR2.exe", "not a supported executable");
            var module = new Rdr2Module(f.Base);
            var installation = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Assert(installation.IsInstalled && !installation.Compatible && !installation.TrainerInstalled);
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
            Assert(f.GameFiles().Length == 1 && File.ReadAllText(f.Path("RDR2.exe")) == "not a supported executable");
            Assert(!Rdr2Module.IsSupportedVersion("1.0.1491.17") && !Rdr2Module.IsSupportedVersion("1.0.1491.99") && Rdr2Module.IsSupportedVersion("1.0.1491.50"));
        });
        Run("RDR2 prerequisite inspection verifies x64 DLL exports without loading code", () => {
            using var f = new Fixture();
            NativeDll(f.Path("ScriptHookRDR2.dll"), Rdr2Module.HookExports); NativeDll(f.Path("dinput8.dll"), new[] { "DirectInput8Create" });
            Assert(Rdr2Module.HasPrerequisites(f.Game));
            NativeDll(f.Path("ScriptHookRDR2.dll"), Rdr2Module.HookExports.Skip(1).ToArray()); Assert(!Rdr2Module.HasPrerequisites(f.Game));
            NativeDll(f.Path("ScriptHookRDR2.dll"), Rdr2Module.HookExports, machine: 0x14c); Assert(!Rdr2Module.HasPrerequisites(f.Game));
            f.GameFile("ScriptHookRDR2.dll", "arbitrary file"); Assert(!Rdr2Module.HasPrerequisites(f.Game));
        });
        Run("RDR2 fresh matching heartbeat connects all supported controls", () => {
            var snapshot = Parse(Status()); Assert(snapshot.Status.Connected && snapshot.Status.Ready && snapshot.Status.SessionId == Session);
            Assert(Rdr2Module.FeatureIds.All(id => snapshot.Status.Values["available_" + id] == "True"));
        });
        Run("RDR2 stale future and previous process disk heartbeats are rejected", () => {
            Assert(!Parse(Status(), modified: Now.AddSeconds(-7)).Status.Connected);
            Assert(!Parse(Status(), modified: Now.AddSeconds(3)).Status.Connected);
            Assert(!Parse(Status(), modified: Now.AddMinutes(-2)).Status.Connected);
            Assert(Parse(Status(), modified: Now.AddSeconds(2)).Status.Connected);
        });
        Run("RDR2 stale future missing malformed content timestamps are rejected", () => {
            foreach (var value in new object[] { new DateTimeOffset(Now).ToUnixTimeSeconds() - 7, new DateTimeOffset(Now).ToUnixTimeSeconds() + 3, "not a timestamp", "1", 1.5 }) {
                var data = Status(); data["updated_utc"] = value; Assert(!Parse(data).Status.Connected);
            }
            var missing = Status(); missing.Remove("updated_utc"); Assert(!Parse(missing).Status.Connected);
        });
        Run("RDR2 stale PID session protocol and trainer version fail closed", () => {
            foreach (var item in new (string, object)[] { ("process_id", 999), ("process_id", "123"), ("schema_version", "1"), ("schema_version", 2), ("version", "0.0.1"), ("session_id", "short"), ("session_id", new string('A', 32)), ("ready", "true"), ("online", "false") }) {
                var data = Status(); data[item.Item1] = item.Item2; Assert(!Parse(data).Status.Connected);
            }
        });
        Run("RDR2 missing or malformed per-control status is rejected", () => {
            var data = Status(); data["available"] = new { }; Assert(!Parse(data).Status.Connected);
            data = Status(); data["enabled"] = new { rdr2_health = "true" }; Assert(!Parse(data).Status.Connected);
        });
        Run("RDR2 Online heartbeat suppresses ready enabled and available controls", () => {
            var data = Status(); data["online"] = true; data["enabled"] = Rdr2Module.FeatureIds.Take(6).ToDictionary(id => id, _ => true);
            var status = Parse(data).Status; Assert(status.Connected && !status.Ready && status.Enabled.Values.All(value => !value));
            Assert(Rdr2Module.FeatureIds.All(id => status.Values["available_" + id] == "False"));
            Fails(() => Command(status, "rdr2_health", true)); Assert(Command(status, "rdr2_health", false).Length > 0); Assert(Command(status, "disable_all").Length > 0);
        });
        Run("RDR2 bounded stream parser rejects oversized status", () => {
            using var f = new Fixture(); f.GameFile("large.json", new string(' ', 16_385));
            using var stream = File.OpenRead(f.Path("large.json"));
            Assert(!Rdr2Module.ReadSnapshotStream(stream, Now.AddMinutes(-1), 123, Now).Status.Connected);
        });
        Run("RDR2 command serializes native bridge identity types and freshness", () => {
            using var document = JsonDocument.Parse(Command(Parse(Status()).Status, "rdr2_health", true));
            var value = document.RootElement;
            Assert(value.GetProperty("session_id").GetString() == Session && value.GetProperty("request_id").GetString() == Request);
            Assert(value.GetProperty("schema_version").GetInt32() == 1 && value.GetProperty("enabled").GetBoolean());
            Assert(value.GetProperty("created_utc").GetInt64() == new DateTimeOffset(Now).ToUnixTimeSeconds());
            using var action = JsonDocument.Parse(Command(Parse(Status()).Status, "rdr2_clear_wanted")); Assert(!action.RootElement.GetProperty("enabled").GetBoolean());
        });
        Run("RDR2 commands validate identities actions toggle shape and availability", () => {
            var status = Parse(Status()).Status;
            Fails(() => Command(status, "unknown", true)); Fails(() => Command(status, "rdr2_health")); Fails(() => Command(status, "rdr2_clear_wanted", true));
            Fails(() => Rdr2Module.FormatCommand(status, status with { SessionId = new string('b', 32) }, "rdr2_health", true, Request, 1));
            Fails(() => Rdr2Module.FormatCommand(status, status, "rdr2_health", true, "invalid", 1));
            Fails(() => Rdr2Module.FormatCommand(status, status, "rdr2_health", true, Request, 0));
            var unavailable = status with { Values = new Dictionary<string, string>() }; Fails(() => Command(unavailable, "rdr2_health", true));
            Assert(Command(unavailable, "rdr2_health", false).Length > 0);
        });
        Run("RDR2 readiness loss permits cleanup but blocks new actions", () => {
            var current = Parse(Status()).Status with { Ready = false };
            Fails(() => Command(current, "rdr2_clear_wanted")); Fails(() => Command(current, "rdr2_ammo", true));
            Assert(Command(current, "rdr2_ammo", false).Length > 0 && Command(current, "disable_all").Length > 0);
            Fails(() => Command(current with { Connected = false }, "disable_all"));
        });
        Run("RDR2 acknowledgements ignore gaps and unrelated requests and reject failure or session changes", () => {
            var status = Parse(Status()).Status;
            Assert(!Rdr2Module.AcceptAcknowledgement(new(TrainerStatus.Offline(), false, ""), Session, Request));
            Assert(!Rdr2Module.AcceptAcknowledgement(new(status, true, ""), Session, Request));
            Assert(Rdr2Module.AcceptAcknowledgement(new(status with { LastCommand = Request, Ready = false }, true, ""), Session, Request));
            Fails(() => Rdr2Module.AcceptAcknowledgement(new(status with { LastCommand = Request }, false, "Native rejected control"), Session, Request));
            Fails(() => Rdr2Module.AcceptAcknowledgement(new(status with { SessionId = new string('b', 32), LastCommand = Request }, true, ""), Session, Request));
        });
        Run("RDR2 controls are distinct Story Mode controls without unimplemented hotkeys", () => {
            var definition = new Rdr2Module("unused").Definition;
            Assert(definition.Id == "red-dead-redemption-2" && definition.AppId == "1174180" && definition.Features.Count == 7);
            Assert(definition.Features.Select(feature => feature.Id).SequenceEqual(Rdr2Module.FeatureIds));
            Assert(definition.Features.All(feature => feature.Hotkey == "") && definition.Features.Count(feature => feature.IsToggle) == 6);
            Assert(definition.Description.Contains("Red Dead Online is unavailable", StringComparison.Ordinal));
        });
        Console.WriteLine($"Red Dead Redemption 2 tests: {passed} passed, {failed} failed.");
        var runtimeFailed = Rdr2RuntimeTests.RunAll();
        return failed == 0 && runtimeFailed == 0 ? 0 : 1;
    }

    private static Dictionary<string, object> Status() => new() {
        ["schema_version"] = 1, ["version"] = Rdr2Module.TrainerVersion, ["process_id"] = 123, ["session_id"] = Session,
        ["updated_utc"] = new DateTimeOffset(Now).ToUnixTimeSeconds(), ["ready"] = true, ["online"] = false, ["message"] = "Story Mode ready",
        ["enabled"] = Rdr2Module.FeatureIds.Take(6).ToDictionary(id => id, _ => false),
        ["available"] = Rdr2Module.FeatureIds.ToDictionary(id => id, _ => true), ["last_command"] = "", ["last_success"] = false, ["last_message"] = ""
    };
    private static Rdr2Module.Snapshot Parse(Dictionary<string, object> data, DateTime? modified = null) => Rdr2Module.ParseSnapshot(JsonSerializer.SerializeToElement(data), modified ?? Now, Now.AddMinutes(-1), 123, Now);
    private static string Command(TrainerStatus status, string action, bool? enabled = null) => Rdr2Module.FormatCommand(status, status, action, enabled, Request, new DateTimeOffset(Now).ToUnixTimeSeconds());
    private static void NativeDll(string path, string[] exports, ushort machine = 0x8664)
    {
        var bytes = new byte[8192];
        using var stream = new MemoryStream(bytes); using var writer = new BinaryWriter(stream);
        void At(int offset, Action write) { stream.Position = offset; write(); }
        At(0, () => writer.Write((ushort)0x5a4d)); At(0x3c, () => writer.Write(0x80));
        At(0x80, () => { writer.Write(0x00004550); writer.Write(machine); writer.Write((ushort)1); });
        At(0x94, () => { writer.Write((ushort)240); writer.Write((ushort)0x2022); });
        const int optional = 0x98;
        At(optional, () => writer.Write((ushort)0x20b)); At(optional + 32, () => { writer.Write(0x1000); writer.Write(0x200); });
        At(optional + 56, () => { writer.Write(0x3000); writer.Write(0x200); });
        At(optional + 108, () => { writer.Write(16); writer.Write(0x1000); writer.Write(40); });
        At(optional + 240, () => { writer.Write(Encoding.ASCII.GetBytes(".rdata\0\0")); writer.Write(0x1000); writer.Write(0x1000); writer.Write(0x1000); writer.Write(0x200); });
        At(0x200 + 24, () => writer.Write(exports.Length)); At(0x200 + 32, () => writer.Write(0x1100));
        var nextName = 0x400;
        for (var index = 0; index < exports.Length; index++) {
            At(0x300 + index * 4, () => writer.Write(nextName - 0x200 + 0x1000));
            At(nextName, () => { writer.Write(Encoding.ASCII.GetBytes(exports[index])); writer.Write((byte)0); });
            nextName += Encoding.ASCII.GetByteCount(exports[index]) + 1;
        }
        File.WriteAllBytes(path, bytes);
    }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation rejection."); }
    private static void Run(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }
    private sealed class ImmediateProgress(Action<string> report) : IProgress<string> { public void Report(string value) => report(value); }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HappyCheats-rdr2-tests-" + Guid.NewGuid().ToString("N"));
        public string Base => System.IO.Path.Combine(root, "client");
        public string Package => System.IO.Path.Combine(Base, "payload", "red-dead-redemption-2");
        public string Game => System.IO.Path.Combine(root, "game");
        public Fixture() { Directory.CreateDirectory(Game); PayloadFile(Rdr2Installer.Plugin, "trainer"); PayloadFile(Rdr2Installer.BridgeReadme, "bridge"); Manifest(); }
        public string Path(string relative) => SafePath(Game, relative);
        public void GameFile(string relative, string contents) { var target = Path(relative); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!); File.WriteAllText(target, contents); }
        public void PayloadFile(string relative, string contents) { var target = SafePath(Package, relative); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!); File.WriteAllText(target, contents); }
        public string[] GameFiles() => Directory.GetFiles(Game, "*", SearchOption.AllDirectories);
        public object[] Entries() => Rdr2Installer.RequiredFiles.Select(path => (object)new { path, sha256 = Hash(SafePath(Package, path)) }).ToArray();
        public void Manifest() => WriteJson(SafePath(Package, "package.json"), new { game_id = "red-dead-redemption-2", app_id = 1174180, version = Rdr2Module.TrainerVersion, files = Entries() });
        public void Dispose() {
            var resolved = System.IO.Path.GetFullPath(root);
            if (resolved.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && System.IO.Path.GetFileName(resolved).StartsWith("HappyCheats-rdr2-tests-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }
}
