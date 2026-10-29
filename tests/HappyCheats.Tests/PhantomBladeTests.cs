using System.Text.Json;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class PhantomBladeTests
{
    private static int passed, failed;
    public static int RunAll()
    {
        passed = failed = 0;
        Run("Phantom Blade identifies the inspected executable and three requested controls", () => {
            var module = new PhantomBladeModule("."); var definition = module.Definition;
            Assert(definition.Id == "phantom-blade-zero" && definition.AppId == "4115450");
            Assert(PhantomBladeModule.SupportedSteamBuild == "26266538");
            Assert(module.GameExecutables.SequenceEqual(new[] { "platform/steam/PBZ.exe" }));
            Assert(definition.Features.Select(f => f.Id).SequenceEqual(new[] { "pbz_health", "pbz_stamina", "pbz_damage" }));
            Assert(definition.Features.Select(f => f.Hotkey).SequenceEqual(new[] { "F1", "F2", "" }));
            Assert(definition.Features.Take(2).All(f => f.IsToggle && f.Number == null));
            var damage = definition.Features[2];
            Assert(!damage.IsToggle && damage.Number == new FeatureNumber(1, 20, 2, Unit: "×"));
            Assert(module is IFeatureValueModule);
        });
        Run("Phantom Blade installs all verified files and removes only owned files", () => {
            using var f = new Fixture();
            Assert(PhantomBladeInstaller.RequiredFiles.Length == 10);
            PhantomBladeInstaller.Install(f.Package, f.Game, () => { });
            Assert(PhantomBladeInstaller.Installed(f.Game) && PhantomBladeInstaller.MatchesPackage(f.Package, f.Game));
            Assert(Directory.Exists(SafePath(f.Game, PhantomBladeModule.BridgeRoot)));
            PhantomBladeInstaller.Remove(f.Game, () => { });
            Assert(!PhantomBladeInstaller.Installed(f.Game));
            Assert(!File.Exists(SafePath(f.Game, PhantomBladeInstaller.NativeLoader)));
        });
        Run("Phantom Blade preserves foreign loaders before any write", () => {
            using var f = new Fixture(); f.GameFile(PhantomBladeInstaller.NativeLoader, "foreign loader");
            Fails(() => PhantomBladeInstaller.Install(f.Package, f.Game, () => { }));
            Assert(File.ReadAllText(SafePath(f.Game, PhantomBladeInstaller.NativeLoader)) == "foreign loader");
            Assert(Directory.EnumerateFiles(f.Game, "*", SearchOption.AllDirectories).Count() == 1);
        });
        Run("Phantom Blade preserves foreign and modified loader profiles", () => {
            foreach (var profile in new[] { PhantomBladeInstaller.MemberVariableLayout, PhantomBladeInstaller.ObjectArraySignature }) {
                using (var f = new Fixture()) {
                    f.GameFile(profile, "foreign profile");
                    Fails(() => PhantomBladeInstaller.Install(f.Package, f.Game, () => { }));
                    Assert(Directory.EnumerateFiles(f.Game, "*", SearchOption.AllDirectories).Count() == 1);
                    Assert(File.ReadAllText(SafePath(f.Game, profile)) == "foreign profile");
                }
                using (var f = new Fixture()) {
                    PhantomBladeInstaller.Install(f.Package, f.Game, () => { });
                    f.GameFile(profile, "user profile edit");
                    Fails(() => PhantomBladeInstaller.Install(f.Package, f.Game, () => { }));
                    PhantomBladeInstaller.Remove(f.Game, () => { });
                    Assert(File.ReadAllText(SafePath(f.Game, profile)) == "user profile edit");
                }
            }
        });
        Run("Phantom Blade preserves modified files and foreign matching files", () => {
            using var f = new Fixture();
            f.GameFile(PhantomBladeInstaller.LoaderLibrary, File.ReadAllText(SafePath(f.Package, PhantomBladeInstaller.LoaderLibrary)));
            PhantomBladeInstaller.Install(f.Package, f.Game, () => { });
            f.GameFile(PhantomBladeInstaller.MainScript, "user edit");
            Fails(() => PhantomBladeInstaller.Install(f.Package, f.Game, () => { }));
            PhantomBladeInstaller.Remove(f.Game, () => { });
            Assert(File.ReadAllText(SafePath(f.Game, PhantomBladeInstaller.MainScript)) == "user edit");
            Assert(File.Exists(SafePath(f.Game, PhantomBladeInstaller.LoaderLibrary)));
        });
        Run("Phantom Blade retains the shared loader for other mods", () => {
            using var f = new Fixture(); PhantomBladeInstaller.Install(f.Package, f.Game, () => { });
            f.GameFile("platform/steam/ue4ss/Mods/AnotherMod/Scripts/main.lua", "other mod");
            PhantomBladeInstaller.Remove(f.Game, () => { });
            Assert(File.Exists(SafePath(f.Game, PhantomBladeInstaller.NativeLoader)));
            Assert(File.Exists(SafePath(f.Game, PhantomBladeInstaller.LoaderLibrary)));
            Assert(File.Exists(SafePath(f.Game, PhantomBladeInstaller.MemberVariableLayout)));
            Assert(File.Exists(SafePath(f.Game, PhantomBladeInstaller.ObjectArraySignature)));
            Assert(!File.Exists(SafePath(f.Game, PhantomBladeInstaller.MainScript)));
        });
        Run("Phantom Blade rejects missing corrupt and escaping payload files", () => {
            foreach (var missing in PhantomBladeInstaller.RequiredFiles) {
                using var f = new Fixture(); f.Manifest(PhantomBladeInstaller.RequiredFiles.Where(p => p != missing));
                Fails(() => PhantomBladeInstaller.ReadPackage(f.Package));
            }
            using (var f = new Fixture()) {
                File.AppendAllText(SafePath(f.Package, PhantomBladeInstaller.MainScript), "corrupt");
                Fails(() => PhantomBladeInstaller.Install(f.Package, f.Game, () => { }));
                Assert(!Directory.EnumerateFiles(f.Game, "*", SearchOption.AllDirectories).Any());
            }
            using (var f = new Fixture()) {
                WriteJson(SafePath(f.Package, "package.json"), new { files = new[] { new { path = "../outside", sha256 = new string('A', 64) } } });
                Fails(() => PhantomBladeInstaller.ReadPackage(f.Package));
            }
        });
        Run("Phantom Blade interrupted installation rolls back completed writes", () => {
            using var f = new Fixture(); var checks = 0;
            Fails(() => PhantomBladeInstaller.Install(f.Package, f.Game, () => { if (++checks == 5) throw new IOException("Game started"); }));
            Assert(!Directory.EnumerateFiles(f.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("Phantom Blade compatibility is rechecked before installation", () => {
            using var f = new Fixture(); f.GameFile(PhantomBladeModule.Executable, "unsupported executable");
            var installation = new GameInstallation(f.Game, PhantomBladeModule.SupportedSteamBuild, true, false, false, true, "stale discovery");
            Fails(() => new PhantomBladeModule(f.Root).InstallAsync(installation).GetAwaiter().GetResult());
            Assert(!File.Exists(SafePath(f.Game, PhantomBladeInstaller.NativeLoader)));
        });
        Run("Phantom Blade fresh status requires schema version session and matching process", () => {
            Assert(Parse(Status()).Status.Ready);
            Assert(Parse(Status(pid: 0)).Status.Ready);
            Assert(!Parse(Status(pid: 99)).Status.Connected);
            Assert(!Parse(Status(version: "old")).Status.Connected);
            Assert(!Parse(Status(schema: 2)).Status.Connected);
            Assert(!Parse(Status(session: "bad\naction=pbz_health")).Status.Connected);
            var now = DateTime.UtcNow;
            Assert(!PhantomBladeModule.ParseSnapshot(Status(), now.AddSeconds(-7), now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(!PhantomBladeModule.ParseSnapshot(Status(), now.AddSeconds(3), now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(!PhantomBladeModule.ParseSnapshot(Status(), now.AddSeconds(-1), now, 42, now).Status.Connected);
            Assert(PhantomBladeModule.ParseSnapshot(Status(), now.AddSeconds(2), now.AddMinutes(-1), 42, now).Status.Ready);
        });
        Run("Phantom Blade missing availability fails closed", () => {
            var current = Parse(Status()).Status;
            var unavailable = current with { Values = new Dictionary<string, string>() };
            Fails(() => Command(unavailable, unavailable, "pbz_health", true));
            Fails(() => Command(unavailable, unavailable, "pbz_damage", null, 2));
            Assert(Command(current, current, "pbz_health", true).Contains("action=pbz_health\nenabled=1\n"));
        });
        Run("Phantom Blade explicit intent and current session gate every command", () => {
            var current = Parse(Status()).Status;
            Fails(() => Command(current, current, "pbz_health", null));
            Fails(() => Command(current, current, "pbz_damage", true, 2));
            Fails(() => Command(current, current, "unknown", true));
            Fails(() => Command(current, current, "pbz_health\naction=disable_all", true));
            Fails(() => Command(current, current with { SessionId = "previous-session" }, "pbz_health", true));
            Fails(() => PhantomBladeModule.FormatCommand(current, current, "pbz_health", true, "bad\nrequest", 1));
            Fails(() => PhantomBladeModule.FormatCommand(current, current, "pbz_health", true, Guid.NewGuid().ToString("N"), 0));
        });
        Run("Phantom Blade bounded multiplier never permits fractional or extraneous amounts", () => {
            var current = Parse(Status()).Status;
            Assert(Command(current, current, "pbz_damage", null).EndsWith("amount=2\n", StringComparison.Ordinal));
            foreach (var value in new[] { 1m, 2m, 20m })
                Assert(Command(current, current, "pbz_damage", null, value).EndsWith("amount=" + value + "\n", StringComparison.Ordinal));
            foreach (var value in new[] { 0m, -1m, 1.5m, 21m, decimal.MaxValue })
                Fails(() => Command(current, current, "pbz_damage", null, value));
            Fails(() => Command(current, current, "pbz_health", true, 2));
            Fails(() => Command(current, current, "disable_all", null, 2));
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
                Assert(Command(current, current, "pbz_damage", null, 20).EndsWith("amount=20\n", StringComparison.Ordinal));
            } finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        });
        Run("Phantom Blade cleanup works without readiness but retains connection and session gates", () => {
            var current = Parse(Status()).Status with { Ready = false, Values = new Dictionary<string, string>() };
            foreach (var feature in new[] { "pbz_health", "pbz_stamina" }) {
                Assert(Command(current, current, feature, false).Contains("enabled=0\n"));
                Fails(() => Command(current, current, feature, true));
            }
            Assert(Command(current, current, "disable_all", null).Contains("action=disable_all\n"));
            Assert(Command(current, current, "pbz_damage", null, 1).EndsWith("amount=1\n", StringComparison.Ordinal));
            Fails(() => Command(current, current, "pbz_damage", null, 2));
            Fails(() => Command(current with { Connected = false }, current, "disable_all", null));
            Fails(() => Command(current with { Connected = false }, current, "pbz_damage", null, 1));
            Fails(() => Command(current, current with { SessionId = "previous-session" }, "disable_all", null));
        });
        Run("Phantom Blade acknowledgement ignores gaps and old responses, rejects failed matching responses", () => {
            var gap = new PhantomBladeModule.Snapshot(TrainerStatus.Offline(), false, "");
            Assert(!PhantomBladeModule.AcceptAcknowledgement(gap, "session-123456", "requested"));
            Assert(!PhantomBladeModule.AcceptAcknowledgement(Parse(Status(command: "old", success: true)), "session-123456", "requested"));
            Assert(PhantomBladeModule.AcceptAcknowledgement(Parse(Status(command: "requested", success: true)), "session-123456", "requested"));
            Fails(() => PhantomBladeModule.AcceptAcknowledgement(Parse(Status(command: "requested")), "session-123456", "requested"));
            Fails(() => PhantomBladeModule.AcceptAcknowledgement(Parse(Status(session: "changed-session", command: "requested", success: true)), "session-123456", "requested"));
        });
        Run("Phantom Blade status publication gaps retry without accepting oversized files", () => {
            using var f = new Fixture(); var relative = PhantomBladeModule.BridgeRoot + "status.json"; var path = SafePath(f.Game, relative);
            var snapshot = PhantomBladeModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () => f.GameFile(relative, Status().GetRawText()));
            Assert(snapshot.Status.Ready);
            f.GameFile(relative, new string(' ', 16_385)); var retries = 0;
            Assert(!PhantomBladeModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () => retries++).Status.Connected);
            Assert(retries == 0);
        });
        Console.WriteLine($"Phantom Blade tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static JsonElement Status(int pid = 42, string version = PhantomBladeModule.TrainerVersion, int schema = 1, string session = "session-123456", string command = "", bool success = false) => JsonSerializer.SerializeToElement(new {
        schema_version = schema, version, process_id = pid, session_id = session, ready = true, message = "Ready",
        enabled = new { pbz_health = false, pbz_stamina = false }, values = new { pbz_health = 100, pbz_stamina = 25, pbz_damage = 1 },
        available = PhantomBladeModule.FeatureIds.ToDictionary(id => id, _ => true), last_command = command, last_success = success, last_message = "result"
    });
    private static PhantomBladeModule.Snapshot Parse(JsonElement json) { var now = DateTime.UtcNow; return PhantomBladeModule.ParseSnapshot(json, now, now.AddMinutes(-1), 42, now); }
    private static string Command(TrainerStatus current, TrainerStatus requested, string action, bool? enabled, decimal? amount = null) => PhantomBladeModule.FormatCommand(current, requested, action, enabled, "6c3962171e544382a06f94038a2b83df", 1234567890, amount);
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation rejection."); }
    private static void Run(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-PhantomBlade-tests-" + Guid.NewGuid().ToString("N"));
        public string Package => Path.Combine(Root, "payload", "phantom-blade-zero");
        public string Game => Path.Combine(Root, "game");
        public Fixture() {
            Directory.CreateDirectory(Game);
            foreach (var path in PhantomBladeInstaller.RequiredFiles) Put(Package, path, "fixture " + path);
            Manifest(PhantomBladeInstaller.RequiredFiles);
        }
        public void GameFile(string path, string content) => Put(Game, path, content);
        private static void Put(string root, string path, string content) { var target = SafePath(root, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, content); }
        public void Manifest(IEnumerable<string> paths) => WriteJson(SafePath(Package, "package.json"), new { files = paths.Select(path => new { path, sha256 = Hash(SafePath(Package, path)) }) });
        public void Dispose() {
            var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("HappyCheats-PhantomBlade-tests-", StringComparison.Ordinal)) throw new IOException("Invalid test cleanup path.");
            Directory.Delete(resolved, true);
        }
    }
}
