using System.Text.Json;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class StardewTests
{
    private static int passed, failed;
    private const string Request = "6c3962171e544382a06f94038a2b83df";

    public static int RunAll()
    {
        Run("Stardew install and removal preserve SMAPI other mods and bridge data", () => {
            using var f = new Fixture();
            f.GameFile("StardewModdingAPI.exe", "SMAPI"); f.GameFile("Mods/AnotherMod/manifest.json", "foreign");
            f.GameFile(StardewModule.BridgeRoot + "status.json", "runtime");
            StardewInstaller.Install(f.Package, f.Game, () => { });
            Assert(StardewInstaller.Installed(f.Game)); Assert(StardewInstaller.MatchesPackage(f.Package, f.Game));
            StardewInstaller.Remove(f.Game, () => { });
            Assert(!StardewInstaller.Installed(f.Game));
            Assert(File.ReadAllText(SafePath(f.Game, "StardewModdingAPI.exe")) == "SMAPI");
            Assert(File.ReadAllText(SafePath(f.Game, "Mods/AnotherMod/manifest.json")) == "foreign");
            Assert(File.ReadAllText(SafePath(f.Game, StardewModule.BridgeRoot + "status.json")) == "runtime");
        });
        Run("Stardew corrupt package and missing required files make no writes", () => {
            using var f = new Fixture(); f.PayloadFile(StardewInstaller.Plugin, "corrupt");
            Fails(() => StardewInstaller.Install(f.Package, f.Game, () => { })); Assert(!GameFiles(f).Any());
            foreach (var required in StardewInstaller.RequiredFiles) {
                f.Manifest(StardewInstaller.RequiredFiles.Where(path => path != required));
                Fails(() => StardewInstaller.Install(f.Package, f.Game, () => { })); Assert(!GameFiles(f).Any());
            }
        });
        Run("Stardew rejects loader shared files runtime commands traversal and duplicate payload entries", () => {
            using var f = new Fixture();
            foreach (var path in new[] { "StardewModdingAPI.exe", "Stardew Valley.dll", "Mods/AnotherMod/file.dll", StardewInstaller.ModRoot + "../AnotherMod/file.dll",
                StardewModule.BridgeRoot + "commands/command.json", StardewInstaller.ReceiptPath, "../outside", StardewInstaller.Plugin.ToUpperInvariant() }) {
                WriteJson(SafePath(f.Package, "package.json"), new { files = new[] { new { path, sha256 = new string('A', 64) } } });
                Fails(() => StardewInstaller.Install(f.Package, f.Game, () => { })); Assert(!GameFiles(f).Any());
            }
            f.Manifest(StardewInstaller.RequiredFiles.Concat(new[] { StardewInstaller.Plugin }));
            Fails(() => StardewInstaller.ReadPackage(f.Package));
        });
        Run("Stardew unowned conflicting mod blocks whole installation", () => {
            using var f = new Fixture(); f.GameFile(StardewInstaller.ModRoot + "manifest.json", "foreign");
            Fails(() => StardewInstaller.Install(f.Package, f.Game, () => { }));
            Assert(GameFiles(f).Count() == 1); Assert(File.ReadAllText(SafePath(f.Game, StardewInstaller.ModRoot + "manifest.json")) == "foreign");
        });
        Run("Stardew identical foreign mod remains unowned after removal", () => {
            using var f = new Fixture();
            foreach (var path in StardewInstaller.RequiredFiles) f.GameFile(path, File.ReadAllText(SafePath(f.Package, path)));
            StardewInstaller.Install(f.Package, f.Game, () => { });
            StardewInstaller.Remove(f.Game, () => { });
            Assert(StardewInstaller.Installed(f.Game));
        });
        Run("Stardew verified owned mod updates and modified files block removal before writes", () => {
            using var f = new Fixture(); StardewInstaller.Install(f.Package, f.Game, () => { });
            f.PayloadFile(StardewInstaller.Plugin, "version two"); f.Manifest();
            StardewInstaller.Install(f.Package, f.Game, () => { }); Assert(StardewInstaller.MatchesPackage(f.Package, f.Game));
            f.GameFile(StardewInstaller.Plugin, "local modification");
            var snapshot = Snapshot(f.Game);
            Fails(() => StardewInstaller.Remove(f.Game, () => { }));
            Assert(snapshot.SequenceEqual(Snapshot(f.Game)));
            Fails(() => StardewInstaller.Install(f.Package, f.Game, () => { }));
            Assert(snapshot.SequenceEqual(Snapshot(f.Game)));
        });
        Run("Stardew interrupted installation rolls back all owned writes", () => {
            using var f = new Fixture(); var checks = 0;
            Fails(() => StardewInstaller.Install(f.Package, f.Game, () => { if (++checks == 4) throw new IOException("Game started"); }));
            Assert(!GameFiles(f).Any());
        });
        Run("Stardew interrupted update restores original receipt and files", () => {
            using var f = new Fixture(); StardewInstaller.Install(f.Package, f.Game, () => { });
            var snapshot = Snapshot(f.Game);
            foreach (var path in StardewInstaller.RequiredFiles) f.PayloadFile(path, "updated " + path);
            f.Manifest(); var checks = 0;
            Fails(() => StardewInstaller.Install(f.Package, f.Game, () => { if (++checks == 4) throw new IOException("Game started"); }));
            Assert(snapshot.SequenceEqual(Snapshot(f.Game)));
        });
        Run("Stardew interrupted removal restores original receipt and files", () => {
            using var f = new Fixture(); StardewInstaller.Install(f.Package, f.Game, () => { }); var snapshot = Snapshot(f.Game); var checks = 0;
            Fails(() => StardewInstaller.Remove(f.Game, () => { if (++checks == 3) throw new IOException("Game started"); }));
            Assert(snapshot.SequenceEqual(Snapshot(f.Game)));
        });
        Run("Stardew forged ownership cannot remove SMAPI or other mods", () => {
            using var f = new Fixture(); f.GameFile("StardewModdingAPI.exe", "SMAPI");
            WriteJson(SafePath(f.Game, StardewInstaller.ReceiptPath), new StardewInstaller.Receipt("stardew", new() { new("StardewModdingAPI.exe", Hash(SafePath(f.Game, "StardewModdingAPI.exe"))) }));
            var snapshot = Snapshot(f.Game);
            Fails(() => StardewInstaller.Remove(f.Game, () => { })); Assert(snapshot.SequenceEqual(Snapshot(f.Game)));
        });
        Run("Stardew game-running checks and invalid receipts stop before install writes", () => {
            using var f = new Fixture();
            Fails(() => StardewInstaller.Install(f.Package, f.Game, () => throw new IOException("running"))); Assert(!GameFiles(f).Any());
            WriteJson(SafePath(f.Game, StardewInstaller.ReceiptPath), new { module = "another", files = Array.Empty<object>() });
            Fails(() => StardewInstaller.Install(f.Package, f.Game, () => { })); Assert(GameFiles(f).Count() == 1);
        });
        Run("Stardew discovery gates unsupported games and launch targets compatible SMAPI directly", () => {
            using var f = new Fixture(); f.GameFile(StardewModule.Executable, "game");
            var module = new StardewModule(f.Package);
            var missing = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Assert(missing.IsInstalled && !missing.Compatible && missing.Message.Contains("compatibility check"));
            Fails(() => StardewModule.LaunchInfo(f.Game));
            SmapiInstallerTests.WriteLoader(f.Game, SmapiInstaller.BundledVersion);
            var info = StardewModule.LaunchInfo(f.Game);
            Assert(info.FileName == SafePath(f.Game, StardewModule.SmapiExecutable)); Assert(info.WorkingDirectory == Path.GetFullPath(f.Game));
            Assert(!info.UseShellExecute && info.Environment["SteamAppId"] == "413150");
            var unsupported = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Assert(!unsupported.Compatible && unsupported.Message.Contains("compatibility check"));
        });
        Run("Stardew game metadata gate accepts compatible patch versions and rejects other series", () => {
            foreach (var version in new[] { new Version(1, 6, 15, 24356), new Version(1, 6, 16), new Version(1, 6, 99) }) Assert(StardewModule.SupportsGameVersion(version));
            foreach (var version in new Version?[] { null, new(1, 6), new(1, 6, 14), new(1, 5, 6), new(1, 7, 0), new(2, 0, 0) }) Assert(!StardewModule.SupportsGameVersion(version));
        });
        Run("Stardew valid heartbeat exposes farmer values and requires exact current PID", () => {
            var state = Parse(Status()); Assert(state.Connected && state.Ready && state.SessionId == "session-123456");
            Assert(state.Values["stardew_money"] == "500" && state.Values["stardew_game_version"] == "1.6.15");
            foreach (var pid in new[] { 0, 1, 43 }) Assert(!Parse(Status(pid: pid)).Connected);
        });
        Run("Stardew stale future and previous-process heartbeat files are rejected", () => {
            var now = DateTime.UtcNow; var state = Status();
            Assert(!StardewModule.ParseSnapshot(state, now.AddSeconds(-7), now.AddMinutes(-1), 42, now).Connected);
            Assert(!StardewModule.ParseSnapshot(state, now.AddSeconds(2), now.AddMinutes(-1), 42, now).Connected);
            Assert(!StardewModule.ParseSnapshot(state, now.AddSeconds(-2), now.AddSeconds(-1), 42, now).Connected);
            Assert(!Parse(Status(updated: DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 8)).Connected);
            Assert(!Parse(Status(updated: DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3)).Connected);
        });
        Run("Stardew rejects wrong schema plugin version and malformed sessions", () => {
            Assert(!Parse(Status(schema: 2)).Connected); Assert(!Parse(Status(version: "0.0.1")).Connected);
            Assert(!Parse(Status(version: "0.1.0")).Connected); // The gift fix requires installing the updated trainer.
            Assert(!Parse(Status(session: "")).Connected); Assert(!Parse(Status(session: "session\ncommand")).Connected);
        });
        Run("Stardew commands require loaded current session and explicit toggle state", () => {
            var current = Parse(Status());
            Fails(() => Command(current, current, "stardew_unlimited_health", null));
            Fails(() => Command(current with { Connected = false }, current, "stardew_unlimited_health", true));
            Fails(() => Command(current with { Ready = false }, current, "stardew_unlimited_health", true));
            Fails(() => Command(current, current with { SessionId = "other-session" }, "stardew_unlimited_health", true));
            var command = ReadCommand(Command(current, current, "stardew_unlimited_health", false));
            Assert(Text(command, "action") == "stardew_unlimited_health" && Object(command, "enabled").ValueKind == JsonValueKind.False);
            Assert(Object(command, "value").ValueKind == JsonValueKind.Null && Text(command, "created_utc") == "1234567890");
        });
        Run("Stardew numeric ranges permit endpoints and reject missing fractional and out-of-range values", () => {
            var current = Parse(Status());
            foreach (var feature in new StardewModule(".").Definition.Features.Where(feature => feature.Number != null)) {
                var number = feature.Number!;
                foreach (var value in new[] { number.Minimum, number.DefaultValue, number.Maximum })
                    Assert(Object(ReadCommand(Command(current, current, feature.Id, null, value)), "value").GetDecimal() == value);
                foreach (var value in new[] { number.Minimum - 1, number.Maximum + 1, number.DefaultValue + 0.5m })
                    Fails(() => Command(current, current, feature.Id, null, value));
                Fails(() => Command(current, current, feature.Id, null));
                Fails(() => Command(current, current, feature.Id, true, number.DefaultValue));
            }
        });
        Run("Stardew HHMM validates minute digits instead of accepting every number in range", () => {
            foreach (var time in new decimal[] { 600, 610, 650, 700, 1200, 2400, 2550 }) StardewModule.ValidateValue("stardew_set_time", time);
            foreach (var time in new decimal[] { 601, 659, 660, 690, 1260, 2360, 2560, 2600 }) Fails(() => StardewModule.ValidateValue("stardew_set_time", time));
        });
        Run("Stardew unknown controls and extra input cannot enter the bridge", () => {
            var current = Parse(Status());
            foreach (var action in new[] { "unknown", "stardew_add_money\naction=other" }) Fails(() => Command(current, current, action, null));
            foreach (var action in new[] { "disable_all", "stardew_refill" }) {
                Command(current, current, action, null);
                Fails(() => Command(current, current, action, true)); Fails(() => Command(current, current, action, null, 10));
            }
            Fails(() => Command(current, current, "stardew_unlimited_energy", true, 10));
            Fails(() => StardewModule.FormatCommand(current, current, "disable_all", null, null, "../request", 1));
            Fails(() => StardewModule.FormatCommand(current, current, "disable_all", null, null, Request, -1));
        });
        Run("Stardew acknowledgement must match schema request and session and expose rejection", () => {
            JsonElement Response(string session = "session-123456", string id = Request, int schema = 1, bool success = true) => JsonSerializer.SerializeToElement(new { schema_version = schema, session_id = session, request_id = id, success, message = "Rejected" });
            StardewModule.AcceptAcknowledgement(Response(), "session-123456", Request);
            Fails(() => StardewModule.AcceptAcknowledgement(Response(session: "old-session"), "session-123456", Request));
            Fails(() => StardewModule.AcceptAcknowledgement(Response(id: "different"), "session-123456", Request));
            Fails(() => StardewModule.AcceptAcknowledgement(Response(schema: 2), "session-123456", Request));
            Fails(() => StardewModule.AcceptAcknowledgement(Response(success: false), "session-123456", Request));
        });
        Run("Stardew includes all required controls numeric contracts and no F10 collision", () => {
            var module = new StardewModule("."); Assert(module is IFeatureValueModule); Assert(module.Definition.AppId == "413150");
            var required = new[] { "stardew_unlimited_health", "stardew_unlimited_energy", "stardew_add_money", "stardew_easy_crafting", "stardew_unlimited_items", "stardew_set_health", "stardew_set_max_health", "stardew_set_time", "stardew_unlimited_water" };
            Assert(required.All(id => module.Definition.Features.Any(feature => feature.Id == id)));
            Assert(module.Definition.Features.Count == 12 && module.Definition.Features.All(feature => feature.Hotkey != "F10"));
            Assert(module.Definition.Features.Where(feature => feature.Number != null).All(feature => !feature.IsToggle && feature.Hotkey == "" && feature.ChoiceValueKey != null));
        });
        Console.WriteLine($"Stardew tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static JsonElement Status(int pid = 42, string version = StardewModule.TrainerVersion, int schema = 1, string session = "session-123456", long? updated = null) => JsonSerializer.SerializeToElement(new {
        schema_version = schema, plugin_version = version, process_id = pid, session_id = session, ready = true, message = "Ready",
        enabled = new { stardew_unlimited_health = false }, values = new { stardew_money = 500, stardew_health = 100 },
        last_command = "", game_version = "1.6.15", updated_utc = updated ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    });
    private static TrainerStatus Parse(JsonElement json) { var now = DateTime.UtcNow; return StardewModule.ParseSnapshot(json, now, now.AddMinutes(-1), 42, now); }
    private static string Command(TrainerStatus current, TrainerStatus requested, string action, bool? enabled, decimal? value = null) => StardewModule.FormatCommand(current, requested, action, enabled, value, Request, 1234567890);
    private static JsonElement ReadCommand(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static IEnumerable<string> GameFiles(Fixture fixture) => Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories);
    private static string[] Snapshot(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order().Select(path => path + ":" + Hash(path)).ToArray();
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation rejection."); }
    private static void Run(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "HappyCheats-Stardew-tests-" + Guid.NewGuid().ToString("N"));
        public string Package => Path.Combine(root, "package");
        public string Game => Path.Combine(root, "game");
        public Fixture() { Directory.CreateDirectory(Game); foreach (var path in StardewInstaller.RequiredFiles) PayloadFile(path, "fixture " + path); Manifest(); }
        public void PayloadFile(string path, string content) => Put(Package, path, content);
        public void GameFile(string path, string content) => Put(Game, path, content);
        private static void Put(string root, string path, string content) { var target = SafePath(root, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, content); }
        public void Manifest(IEnumerable<string>? paths = null) => WriteJson(SafePath(Package, "package.json"), new { files = (paths ?? StardewInstaller.RequiredFiles).Select(path => new { path, sha256 = Hash(SafePath(Package, path)) }) });
        public void Dispose()
        {
            var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("HappyCheats-Stardew-tests-", StringComparison.Ordinal)) throw new IOException("Invalid test cleanup path.");
            Directory.Delete(resolved, true);
        }
    }
}
