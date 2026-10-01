using System.Text.Json;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class AstroneerTests
{
    private static int passed, failed;
    public static int RunAll()
    {
        Run("Astroneer verified installation and owned removal", () => {
            using var fixture = new Fixture();
            AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { });
            Assert(AstroneerInstaller.Installed(fixture.Game));
            Assert(AstroneerInstaller.MatchesPackage(fixture.Package, fixture.Game));
            Assert(Directory.Exists(Path.Combine(fixture.Game, AstroneerModule.BridgeRoot)));
            AstroneerInstaller.Remove(fixture.Game, () => { });
            Assert(!AstroneerInstaller.Installed(fixture.Game));
            Assert(!File.Exists(Path.Combine(fixture.Game, AstroneerInstaller.NativeLoader)));
        });
        Run("Astroneer foreign native loader blocks every payload write", () => {
            using var fixture = new Fixture();
            fixture.GameFile(AstroneerInstaller.NativeLoader, "another mod loader");
            Fails(() => AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(!AstroneerInstaller.Installed(fixture.Game));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, AstroneerInstaller.NativeLoader)) == "another mod loader");
            Assert(Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Count() == 1);
        });
        Run("Astroneer existing mods configuration is preserved", () => {
            using var fixture = new Fixture();
            var path = "Astro/Binaries/Win64/ue4ss/Mods/mods.txt";
            fixture.PayloadFile(path, "HappyCheatsAstroneer : 1"); fixture.Manifest();
            fixture.GameFile(path, "OtherMod : 1");
            Fails(() => AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, path)) == "OtherMod : 1");
            Assert(!AstroneerInstaller.Installed(fixture.Game));
        });
        Run("Astroneer corrupted payload fails before install", () => {
            using var fixture = new Fixture();
            File.AppendAllText(Path.Combine(fixture.Package, AstroneerInstaller.MainScript), "corrupt");
            Fails(() => AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("Astroneer modified owned file survives update and removal", () => {
            using var fixture = new Fixture(); AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile(AstroneerInstaller.MainScript, "user changed script");
            Fails(() => AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { }));
            AstroneerInstaller.Remove(fixture.Game, () => { });
            Assert(File.ReadAllText(Path.Combine(fixture.Game, AstroneerInstaller.MainScript)) == "user changed script");
        });
        Run("Astroneer identical foreign plugin never becomes owned", () => {
            using var fixture = new Fixture();
            fixture.GameFile(AstroneerInstaller.MainScript, File.ReadAllText(Path.Combine(fixture.Package, AstroneerInstaller.MainScript)));
            AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { });
            AstroneerInstaller.Remove(fixture.Game, () => { });
            Assert(File.Exists(Path.Combine(fixture.Game, AstroneerInstaller.MainScript)));
        });
        Run("Astroneer update verifies previous ownership", () => {
            using var fixture = new Fixture(); AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.PayloadFile(AstroneerInstaller.MainScript, "updated mod"); fixture.Manifest();
            AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { });
            Assert(Hash(Path.Combine(fixture.Game, AstroneerInstaller.MainScript)) == Hash(Path.Combine(fixture.Package, AstroneerInstaller.MainScript)));
        });
        Run("Astroneer interrupted install rolls back all written files", () => {
            using var fixture = new Fixture(); var checks = 0;
            Fails(() => AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { if (++checks == 5) throw new IOException("Game started"); }));
            Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("Astroneer rollback preserves a concurrently modified file", () => {
            using var fixture = new Fixture(); var checks = 0;
            Fails(() => AstroneerInstaller.Install(fixture.Package, fixture.Game, () => {
                if (++checks != 5) return;
                var written = Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).First(path => !path.EndsWith(".happycheats-stage"));
                File.WriteAllText(written, "concurrent user edit");
                throw new IOException("Game started");
            }));
            var remaining = Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).ToArray();
            Assert(remaining.Length == 1 && File.ReadAllText(remaining[0]) == "concurrent user edit");
        });
        Run("Astroneer running game blocks installation", () => {
            using var fixture = new Fixture();
            Fails(() => AstroneerInstaller.Install(fixture.Package, fixture.Game, () => throw new IOException("Running")));
            Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("Astroneer removal preserves unrelated game and mod files", () => {
            using var fixture = new Fixture();
            fixture.GameFile("Astro/Content/Paks/game.pak", "game"); fixture.GameFile("Astro/Binaries/Win64/ue4ss/Mods/OtherMod/main.lua", "other");
            AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { });
            AstroneerInstaller.Remove(fixture.Game, () => { });
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "Astro/Content/Paks/game.pak")) == "game");
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "Astro/Binaries/Win64/ue4ss/Mods/OtherMod/main.lua")) == "other");
            Assert(File.Exists(Path.Combine(fixture.Game, AstroneerInstaller.NativeLoader)));
            Assert(File.Exists(Path.Combine(fixture.Game, AstroneerInstaller.LoaderLibrary)));
            Assert(File.Exists(Path.Combine(fixture.Game, "Astro/Binaries/Win64/ue4ss/Mods/mods.txt")));
            Assert(!File.Exists(Path.Combine(fixture.Game, AstroneerInstaller.MainScript)));
        });
        Run("Astroneer removal preserves loader for mods added after installation", () => {
            using var fixture = new Fixture(); AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile("Astro/Binaries/Win64/ue4ss/Mods/NewMod/Scripts/main.lua", "new mod");
            AstroneerInstaller.Remove(fixture.Game, () => { });
            Assert(File.Exists(Path.Combine(fixture.Game, AstroneerInstaller.NativeLoader)));
            Assert(File.Exists(Path.Combine(fixture.Game, "Astro/Binaries/Win64/ue4ss/UE4SS-settings.ini")));
        });
        Run("Astroneer required loader configuration and bridge files cannot be omitted", () => {
            foreach (var required in AstroneerInstaller.RequiredFiles) {
                using var fixture = new Fixture();
                WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = AstroneerInstaller.RequiredFiles.Where(path => path != required).Select(path => new { path, sha256 = Hash(Path.Combine(fixture.Package, path)) }) });
                Fails(() => AstroneerInstaller.ReadPackage(fixture.Package));
            }
        });
        Run("Astroneer incomplete and changed installed payloads require an update", () => {
            using var fixture = new Fixture(); AstroneerInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile(AstroneerInstaller.MainScript, "changed");
            Assert(AstroneerInstaller.Installed(fixture.Game));
            Assert(!AstroneerInstaller.MatchesPackage(fixture.Package, fixture.Game));
            File.Delete(Path.Combine(fixture.Game, "Astro/Binaries/Win64/ue4ss/Mods/mods.txt"));
            Assert(!AstroneerInstaller.Installed(fixture.Game));
        });
        Run("Astroneer package cannot escape trainer paths", () => {
            using var fixture = new Fixture();
            foreach (var path in new[] { "../outside.dll", "Astro/Content/Paks/game.pak", AstroneerInstaller.ModRoot + "bridge/command.txt", AstroneerInstaller.ModRoot + "Scripts/../../OtherMod/main.lua" }) {
                WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = new[] { new { path, sha256 = new string('A', 64) } } });
                Fails(() => AstroneerInstaller.ReadPackage(fixture.Package));
            }
        });
        Run("Astroneer package rejects missing runtime and duplicate entries", () => {
            using var fixture = new Fixture();
            WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = Array.Empty<object>() });
            Fails(() => AstroneerInstaller.ReadPackage(fixture.Package));
            var entry = new { path = AstroneerInstaller.MainScript, sha256 = Hash(Path.Combine(fixture.Package, AstroneerInstaller.MainScript)) };
            WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = new[] { entry, entry } });
            Fails(() => AstroneerInstaller.ReadPackage(fixture.Package));
        });
        Run("Astroneer removal rejects forged receipt paths", () => {
            using var fixture = new Fixture(); fixture.GameFile("keep.txt", "keep");
            WriteJson(Path.Combine(fixture.Game, AstroneerInstaller.ReceiptPath), new AstroneerInstaller.Receipt("astroneer", new() { new("keep.txt", Hash(Path.Combine(fixture.Game, "keep.txt"))) }));
            Fails(() => AstroneerInstaller.Remove(fixture.Game, () => { }));
            Assert(File.Exists(Path.Combine(fixture.Game, "keep.txt")));
        });

        Run("Astroneer valid current-process heartbeat exposes feature availability", () => {
            var state = Parse(Status());
            Assert(state.Status.Connected && state.Status.Ready && state.Status.SessionId == "session-123456");
            Assert(state.Status.Values["available_resource_collection_10x"] == "True");
            Assert(!state.Status.Values.ContainsKey("available_unlimited_soil"));
        });
        Run("Astroneer supports absent Lua PID but rejects a mismatched reported PID", () => {
            Assert(Parse(Status(pid: null)).Status.Ready);
            Assert(Parse(Status(pid: 0)).Status.Ready);
            Assert(!Parse(Status(pid: 99)).Status.Connected);
        });
        Run("Astroneer stale future and previous-process heartbeats are disconnected", () => {
            var now = DateTime.UtcNow; var status = Status();
            Assert(!AstroneerModule.ParseSnapshot(status, now.AddSeconds(-7), now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(!AstroneerModule.ParseSnapshot(status, now.AddSeconds(2), now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(!AstroneerModule.ParseSnapshot(status, now.AddSeconds(-2), now.AddSeconds(-1), 42, now).Status.Connected);
        });
        Run("Astroneer rejects invalid schema version and injectable session", () => {
            Assert(!Parse(Status(version: "0.1.2")).Status.Connected);
            Assert(!Parse(Status(schema: 2)).Status.Connected);
            Assert(!Parse(Status(session: "session-123456\naction=add_bytes")).Status.Connected);
            Assert(!Parse(Status(session: "")).Status.Connected);
        });
        Run("Astroneer requires explicit toggle state and current session", () => {
            var current = Parse(Status()).Status;
            Fails(() => Command(current, current, "resource_collection_10x", null));
            Fails(() => Command(current, current with { SessionId = "old-session" }, "resource_collection_10x", true));
            Fails(() => Command(current with { Ready = false }, current, "resource_collection_10x", true));
            var command = Command(current, current, "resource_collection_10x", true);
            Assert(command.Contains("action=resource_collection_10x\nenabled=1\n"));
            Assert(command.Contains("created_utc=1234567890\n"));
        });
        Run("Astroneer rejects unavailable unknown and injectable controls", () => {
            var current = Parse(Status()).Status;
            Fails(() => Command(current, current, "unlimited_soil", true));
            var unavailable = current with { Values = new Dictionary<string, string>() };
            Fails(() => Command(unavailable, unavailable, "resource_collection_10x", true));
            Fails(() => Command(current, current, "unknown", true));
            Fails(() => Command(current, current, "resource_collection_10x\naction=add_bytes", true));
            Fails(() => AstroneerModule.FormatCommand(current, current, "resource_collection_10x", true, "bad\nrequest", 1));
        });
        Run("Astroneer disable all bypasses feature availability and has no toggle input", () => {
            var current = Parse(Status()).Status;
            Assert(Command(current, current, "disable_all", null).Contains("action=disable_all\n"));
            Fails(() => Command(current, current, "disable_all", true));
            Fails(() => Command(current, current, "add_bytes", true));
            Assert(Command(current, current, "add_bytes", null).Contains("action=add_bytes\n"));
        });
        Run("Astroneer command acknowledgements preserve success and rejection", () => {
            var success = Parse(Status(lastCommand: "abc", success: true, message: "applied"));
            Assert(success.Status.LastCommand == "abc" && success.LastSuccess && success.LastMessage == "applied");
            var rejected = Parse(Status(lastCommand: "abc", success: false, message: "binding unavailable"));
            Assert(!rejected.LastSuccess && rejected.LastMessage == "binding unavailable");
        });
        Run("Astroneer acknowledgement waits through publication gaps without accepting old results", () => {
            var pending = Parse(Status(lastCommand: "previous-request", success: true));
            var gap = new AstroneerModule.Snapshot(TrainerStatus.Offline("Waiting for ASTRONEER trainer status."), false, "");
            var accepted = Parse(Status(lastCommand: "current-request", success: true, message: "Applied"));
            Assert(!AstroneerModule.AcceptAcknowledgement(pending, "session-123456", "current-request"));
            Assert(!AstroneerModule.AcceptAcknowledgement(gap, "session-123456", "current-request"));
            Assert(AstroneerModule.AcceptAcknowledgement(accepted, "session-123456", "current-request"));
        });
        Run("Astroneer acknowledgement rejects fresh different sessions and matching failed requests", () => {
            var changed = Parse(Status(session: "another-session", lastCommand: "current-request", success: true));
            Fails(() => AstroneerModule.AcceptAcknowledgement(changed, "session-123456", "current-request"));
            var failedRequest = Parse(Status(lastCommand: "current-request", success: false, message: "Binding unavailable"));
            Fails(() => AstroneerModule.AcceptAcknowledgement(failedRequest, "session-123456", "current-request"));
        });
        Run("Astroneer exposes four implemented controls with bytes on F4", () => {
            var definition = new AstroneerModule(".").Definition;
            Assert(definition.Features.Count == 4);
            Assert(!definition.Features.Any(feature => feature.Id == "unlimited_soil"));
            Assert(definition.Features.Single(feature => feature.Id == "add_bytes").Hotkey == "F4");
            Assert(AstroneerModule.FeatureIds.SequenceEqual(definition.Features.Select(feature => feature.Id)));
        });
        Run("Astroneer exposes only the two supported resource multiplier choices", () => {
            var module = new AstroneerModule(".");
            Assert(module is IFeatureChoiceModule);
            var collection = module.Definition.Features.Single(feature => feature.Id == "resource_collection_10x");
            Assert(collection.ChoiceValueKey == "selected_collection_multiplier");
            Assert(collection.Choices!.SequenceEqual(new FeatureChoice[] { new("10", "10×"), new("20", "20×") }));
            Assert(module.Definition.Features.Where(feature => feature.Id != collection.Id).All(feature => feature.Choices == null && feature.ChoiceValueKey == null));
        });
        Run("Astroneer status retains confirmed selected and effective multipliers", () => {
            var state = Parse(Status(selectedMultiplier: 20));
            Assert(state.Status.Values["selected_collection_multiplier"] == "20");
            Assert(state.Status.Values["collection_multiplier"] == "1");
            Assert(!state.Status.Enabled["resource_collection_10x"]);
        });
        Run("Astroneer multiplier selection encodes 10 or 20 without toggling collection", () => {
            var current = Parse(Status()).Status;
            foreach (var multiplier in new[] { 10, 20 }) {
                var command = ChoiceCommand(current, current, multiplier);
                Assert(command.Contains("action=set_collection_multiplier\nenabled=0\n"));
                Assert(command.EndsWith("multiplier=" + multiplier + "\n", StringComparison.Ordinal));
                Assert(command.Contains("session_id=" + current.SessionId + "\n"));
            }
            Assert(!Command(current, current, "resource_collection_10x", true).Contains("multiplier="));
        });
        Run("Astroneer rejects out-of-range missing and malformed multiplier choices", () => {
            var current = Parse(Status()).Status;
            foreach (int? multiplier in new int?[] { null, 0, 1, 11, 19, 21, -10, int.MaxValue })
                Fails(() => ChoiceCommand(current, current, multiplier));
            foreach (var choice in new[] { "", "0", "1", "10.0", "+10", " 10", "20 ", "10\naction=add_bytes" })
                Fails(() => AstroneerModule.ChoiceMultiplier("resource_collection_10x", choice));
            Assert(AstroneerModule.ChoiceMultiplier("resource_collection_10x", "10") == 10);
            Assert(AstroneerModule.ChoiceMultiplier("resource_collection_10x", "20") == 20);
        });
        Run("Astroneer multiplier selection uses collection availability and current session", () => {
            var current = Parse(Status()).Status;
            var unavailable = current with { Values = new Dictionary<string, string> { ["available_set_collection_multiplier"] = "True" } };
            Fails(() => ChoiceCommand(unavailable, unavailable, 20));
            Fails(() => ChoiceCommand(current, current with { SessionId = "previous-session" }, 20));
            Fails(() => ChoiceCommand(current with { Connected = false }, current, 20));
            Fails(() => ChoiceCommand(current with { Ready = false }, current, 20));
        });
        Run("Astroneer rejects toggle input on multiplier selection and unrelated extra multipliers", () => {
            var current = Parse(Status()).Status;
            foreach (var enabled in new[] { true, false })
                Fails(() => AstroneerModule.FormatCommand(current, current, "set_collection_multiplier", enabled, "6c3962171e544382a06f94038a2b83df", 1234567890, 20));
            foreach (var action in AstroneerModule.FeatureIds.Concat(new[] { "disable_all" })) {
                bool? enabled = action is "add_bytes" or "disable_all" ? null : true;
                Fails(() => AstroneerModule.FormatCommand(current, current, action, enabled, "6c3962171e544382a06f94038a2b83df", 1234567890, 10));
            }
        });
        Run("Astroneer rejects selection on other features before accessing a game", () => {
            var module = new AstroneerModule(".");
            var installation = new GameInstallation("", "", false, false, false, false, "Not installed");
            var current = Parse(Status()).Status;
            foreach (var action in new[] { "add_bytes", "unlimited_oxygen", "unknown", "set_collection_multiplier" })
                Fails(() => module.SelectAsync(installation, current, action, "20").GetAwaiter().GetResult());
            Fails(() => module.SelectAsync(installation, current, "resource_collection_10x", "30").GetAwaiter().GetResult());
        });
        Console.WriteLine($"Astroneer tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static JsonElement Status(int? pid = 42, string version = "0.1.3", int schema = 1, string session = "session-123456", string lastCommand = "", bool success = false, string message = "", int selectedMultiplier = 10) => JsonSerializer.SerializeToElement(new {
        schema_version = schema, version, process_id = pid, session_id = session, ready = true, message = "Ready",
        enabled = new { resource_collection_10x = false }, values = new { bytes = 500, selected_collection_multiplier = selectedMultiplier, collection_multiplier = 1 },
        available = new { resource_collection_10x = true, unlimited_oxygen = true, unlimited_power = true, add_bytes = true },
        last_command = lastCommand, last_success = success, last_message = message
    });
    private static AstroneerModule.Snapshot Parse(JsonElement json) { var now = DateTime.UtcNow; return AstroneerModule.ParseSnapshot(json, now, now.AddMinutes(-1), 42, now); }
    private static string Command(TrainerStatus current, TrainerStatus requested, string action, bool? enabled) => AstroneerModule.FormatCommand(current, requested, action, enabled, "6c3962171e544382a06f94038a2b83df", 1234567890);
    private static string ChoiceCommand(TrainerStatus current, TrainerStatus requested, int? multiplier) => AstroneerModule.FormatCommand(current, requested, "set_collection_multiplier", null, "6c3962171e544382a06f94038a2b83df", 1234567890, multiplier);
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation rejection."); }
    private static void Run(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "HappyCheats-Astroneer-tests-" + Guid.NewGuid().ToString("N"));
        private readonly HashSet<string> paths = new();
        public string Package => Path.Combine(root, "package");
        public string Game => Path.Combine(root, "game");
        public Fixture()
        {
            Directory.CreateDirectory(Game);
            foreach (var path in AstroneerInstaller.RequiredFiles) PayloadFile(path, "fixture " + path);
            Manifest();
        }
        public void PayloadFile(string path, string content) { paths.Add(path); Put(Package, path, content); }
        public void GameFile(string path, string content) => Put(Game, path, content);
        private static void Put(string root, string path, string content) { var target = SafePath(root, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, content); }
        public void Manifest() => WriteJson(Path.Combine(Package, "package.json"), new { files = paths.Select(path => new { path, sha256 = Hash(Path.Combine(Package, path)) }) });
        public void Dispose()
        {
            var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("HappyCheats-Astroneer-tests-", StringComparison.Ordinal)) throw new IOException("Invalid test cleanup path.");
            Directory.Delete(resolved, true);
        }
    }
}
