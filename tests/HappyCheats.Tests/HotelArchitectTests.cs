using System.Text.Json;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class HotelArchitectTests
{
    private static int passed, failed;
    public static int RunAll()
    {
        passed = failed = 0;
        Run("HotelArchitect verified installation and owned removal", () => {
            using var fixture = new Fixture();
            HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { });
            Assert(HotelArchitectInstaller.Installed(fixture.Game));
            Assert(HotelArchitectInstaller.MatchesPackage(fixture.Package, fixture.Game));
            Assert(Directory.Exists(Path.Combine(fixture.Game, HotelArchitectModule.BridgeRoot)));
            HotelArchitectInstaller.Remove(fixture.Game, () => { });
            Assert(!HotelArchitectInstaller.Installed(fixture.Game));
            Assert(!File.Exists(Path.Combine(fixture.Game, HotelArchitectInstaller.NativeLoader)));
        });
        Run("HotelArchitect foreign native loader blocks every payload write", () => {
            using var fixture = new Fixture();
            fixture.GameFile(HotelArchitectInstaller.NativeLoader, "another mod loader");
            Fails(() => HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(!HotelArchitectInstaller.Installed(fixture.Game));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, HotelArchitectInstaller.NativeLoader)) == "another mod loader");
            Assert(Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Count() == 1);
        });
        Run("HotelArchitect existing mods configuration is preserved", () => {
            using var fixture = new Fixture();
            var path = "HotelArchitect/Binaries/Win64/ue4ss/Mods/mods.txt";
            fixture.PayloadFile(path, "HappyCheatsHotelArchitect : 1"); fixture.Manifest();
            fixture.GameFile(path, "OtherMod : 1");
            Fails(() => HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, path)) == "OtherMod : 1");
            Assert(!HotelArchitectInstaller.Installed(fixture.Game));
        });
        Run("HotelArchitect corrupted payload fails before install", () => {
            using var fixture = new Fixture();
            File.AppendAllText(Path.Combine(fixture.Package, HotelArchitectInstaller.MainScript), "corrupt");
            Fails(() => HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("HotelArchitect modified owned file survives update and removal", () => {
            using var fixture = new Fixture(); HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile(HotelArchitectInstaller.MainScript, "user changed script");
            Fails(() => HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { }));
            HotelArchitectInstaller.Remove(fixture.Game, () => { });
            Assert(File.ReadAllText(Path.Combine(fixture.Game, HotelArchitectInstaller.MainScript)) == "user changed script");
        });
        Run("HotelArchitect identical foreign plugin never becomes owned", () => {
            using var fixture = new Fixture();
            fixture.GameFile(HotelArchitectInstaller.MainScript, File.ReadAllText(Path.Combine(fixture.Package, HotelArchitectInstaller.MainScript)));
            HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { });
            HotelArchitectInstaller.Remove(fixture.Game, () => { });
            Assert(File.Exists(Path.Combine(fixture.Game, HotelArchitectInstaller.MainScript)));
        });
        Run("HotelArchitect update verifies previous ownership", () => {
            using var fixture = new Fixture(); HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.PayloadFile(HotelArchitectInstaller.MainScript, "updated mod"); fixture.Manifest();
            HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { });
            Assert(Hash(Path.Combine(fixture.Game, HotelArchitectInstaller.MainScript)) == Hash(Path.Combine(fixture.Package, HotelArchitectInstaller.MainScript)));
        });
        Run("HotelArchitect interrupted install rolls back all written files", () => {
            using var fixture = new Fixture(); var checks = 0;
            Fails(() => HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { if (++checks == 5) throw new IOException("Game started"); }));
            Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("HotelArchitect rollback preserves a concurrently modified file", () => {
            using var fixture = new Fixture(); var checks = 0;
            Fails(() => HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => {
                if (++checks != 5) return;
                var written = Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).First(path => !path.EndsWith(".happycheats-stage"));
                File.WriteAllText(written, "concurrent user edit");
                throw new IOException("Game started");
            }));
            var remaining = Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).ToArray();
            Assert(remaining.Length == 1 && File.ReadAllText(remaining[0]) == "concurrent user edit");
        });
        Run("HotelArchitect running game blocks installation", () => {
            using var fixture = new Fixture();
            Fails(() => HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => throw new IOException("Running")));
            Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("HotelArchitect removal preserves unrelated game and mod files", () => {
            using var fixture = new Fixture();
            fixture.GameFile("HotelArchitect/Content/Paks/game.pak", "game"); fixture.GameFile("HotelArchitect/Binaries/Win64/ue4ss/Mods/OtherMod/main.lua", "other");
            HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { });
            HotelArchitectInstaller.Remove(fixture.Game, () => { });
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "HotelArchitect/Content/Paks/game.pak")) == "game");
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "HotelArchitect/Binaries/Win64/ue4ss/Mods/OtherMod/main.lua")) == "other");
            Assert(File.Exists(Path.Combine(fixture.Game, HotelArchitectInstaller.NativeLoader)));
            Assert(File.Exists(Path.Combine(fixture.Game, HotelArchitectInstaller.LoaderLibrary)));
            Assert(File.Exists(Path.Combine(fixture.Game, "HotelArchitect/Binaries/Win64/ue4ss/Mods/mods.txt")));
            Assert(!File.Exists(Path.Combine(fixture.Game, HotelArchitectInstaller.MainScript)));
        });
        Run("HotelArchitect removal preserves loader for mods added after installation", () => {
            using var fixture = new Fixture(); HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile("HotelArchitect/Binaries/Win64/ue4ss/Mods/NewMod/Scripts/main.lua", "new mod");
            HotelArchitectInstaller.Remove(fixture.Game, () => { });
            Assert(File.Exists(Path.Combine(fixture.Game, HotelArchitectInstaller.NativeLoader)));
            Assert(File.Exists(Path.Combine(fixture.Game, "HotelArchitect/Binaries/Win64/ue4ss/UE4SS-settings.ini")));
        });
        Run("HotelArchitect required loader configuration and bridge files cannot be omitted", () => {
            foreach (var required in HotelArchitectInstaller.RequiredFiles) {
                using var fixture = new Fixture();
                WriteJson(Path.Combine(fixture.Package, "package.json"), new { schema_version = 1, game_id = "hotel-architect", version = HotelArchitectModule.TrainerVersion, game_executable_sha256 = HotelArchitectModule.SupportedExecutableHash, files = HotelArchitectInstaller.RequiredFiles.Where(path => path != required).Select(path => new { path, sha256 = Hash(Path.Combine(fixture.Package, path)) }) });
                Fails(() => HotelArchitectInstaller.ReadPackage(fixture.Package));
            }
        });
        Run("HotelArchitect incomplete and changed installed payloads require an update", () => {
            using var fixture = new Fixture(); HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile(HotelArchitectInstaller.MainScript, "changed");
            Assert(HotelArchitectInstaller.Installed(fixture.Game));
            Assert(!HotelArchitectInstaller.MatchesPackage(fixture.Package, fixture.Game));
            File.Delete(Path.Combine(fixture.Game, "HotelArchitect/Binaries/Win64/ue4ss/Mods/mods.txt"));
            Assert(!HotelArchitectInstaller.Installed(fixture.Game));
        });
        Run("HotelArchitect discovery reuses unchanged hashes without weakening installer verification", () => {
            using var fixture = new Fixture(); HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { });
            var module = new HotelArchitectModule(fixture.BaseDirectory);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            using var source = new FileStream(Path.Combine(fixture.Package, HotelArchitectInstaller.LoaderLibrary), FileMode.Open, FileAccess.Read, FileShare.None);
            using var installed = new FileStream(Path.Combine(fixture.Game, HotelArchitectInstaller.LoaderLibrary), FileMode.Open, FileAccess.Read, FileShare.None);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            Fails(() => HotelArchitectInstaller.MatchesPackage(fixture.Package, fixture.Game));
        });
        Run("HotelArchitect discovery rehashes modified and missing installed runtime files", () => {
            using var fixture = new Fixture(); HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { });
            var module = new HotelArchitectModule(fixture.BaseDirectory);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            var path = Path.Combine(fixture.Game, HotelArchitectInstaller.LoaderLibrary);
            var original = File.ReadAllText(path); var modified = File.GetLastWriteTimeUtc(path);
            fixture.GameFile(HotelArchitectInstaller.LoaderLibrary, new string('x', original.Length));
            File.SetLastWriteTimeUtc(path, modified.AddSeconds(2));
            Assert(!module.MatchesPackageForDiscovery(fixture.Game));
            fixture.GameFile(HotelArchitectInstaller.LoaderLibrary, original);
            File.SetLastWriteTimeUtc(path, modified.AddSeconds(4));
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            File.Delete(path);
            Assert(!module.MatchesPackageForDiscovery(fixture.Game));
        });
        Run("HotelArchitect discovery revalidates changed source files and manifest", () => {
            using var fixture = new Fixture(); HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { });
            var module = new HotelArchitectModule(fixture.BaseDirectory);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            var path = Path.Combine(fixture.Package, HotelArchitectInstaller.MainScript);
            var original = File.ReadAllText(path); var modified = File.GetLastWriteTimeUtc(path);
            fixture.PayloadFile(HotelArchitectInstaller.MainScript, new string('x', original.Length));
            File.SetLastWriteTimeUtc(path, modified.AddSeconds(2));
            Fails(() => module.MatchesPackageForDiscovery(fixture.Game));
            fixture.PayloadFile(HotelArchitectInstaller.MainScript, original);
            File.SetLastWriteTimeUtc(path, modified.AddSeconds(4));
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            WriteJson(Path.Combine(fixture.Package, "package.json"), new { schema_version = 1, game_id = "hotel-architect", version = HotelArchitectModule.TrainerVersion, game_executable_sha256 = HotelArchitectModule.SupportedExecutableHash, files = Array.Empty<object>() });
            Fails(() => module.MatchesPackageForDiscovery(fixture.Game));
        });
        Run("HotelArchitect package cannot escape trainer paths", () => {
            using var fixture = new Fixture();
            foreach (var path in new[] { "../outside.dll", "HotelArchitect/Content/Paks/game.pak", HotelArchitectInstaller.ModRoot + "bridge/command.txt", HotelArchitectInstaller.ModRoot + "Scripts/../../OtherMod/main.lua" }) {
                WriteJson(Path.Combine(fixture.Package, "package.json"), new { schema_version = 1, game_id = "hotel-architect", version = HotelArchitectModule.TrainerVersion, game_executable_sha256 = HotelArchitectModule.SupportedExecutableHash, files = new[] { new { path, sha256 = new string('A', 64) } } });
                Fails(() => HotelArchitectInstaller.ReadPackage(fixture.Package));
            }
        });
        Run("HotelArchitect package rejects missing runtime and duplicate entries", () => {
            using var fixture = new Fixture();
            WriteJson(Path.Combine(fixture.Package, "package.json"), new { schema_version = 1, game_id = "hotel-architect", version = HotelArchitectModule.TrainerVersion, game_executable_sha256 = HotelArchitectModule.SupportedExecutableHash, files = Array.Empty<object>() });
            Fails(() => HotelArchitectInstaller.ReadPackage(fixture.Package));
            var entry = new { path = HotelArchitectInstaller.MainScript, sha256 = Hash(Path.Combine(fixture.Package, HotelArchitectInstaller.MainScript)) };
            WriteJson(Path.Combine(fixture.Package, "package.json"), new { schema_version = 1, game_id = "hotel-architect", version = HotelArchitectModule.TrainerVersion, game_executable_sha256 = HotelArchitectModule.SupportedExecutableHash, files = new[] { entry, entry } });
            Fails(() => HotelArchitectInstaller.ReadPackage(fixture.Package));
        });
        Run("HotelArchitect removal rejects forged receipt paths", () => {
            using var fixture = new Fixture(); fixture.GameFile("keep.txt", "keep");
            WriteJson(Path.Combine(fixture.Game, HotelArchitectInstaller.ReceiptPath), new HotelArchitectInstaller.Receipt("hotel-architect", new() { new("keep.txt", Hash(Path.Combine(fixture.Game, "keep.txt"))) }));
            Fails(() => HotelArchitectInstaller.Remove(fixture.Game, () => { }));
            Assert(File.Exists(Path.Combine(fixture.Game, "keep.txt")));
        });

        Run("HotelArchitect valid current-process heartbeat exposes feature availability", () => {
            var state = Parse(Status());
            Assert(state.Status.Connected && state.Status.Ready && state.Status.SessionId == "session-123456");
            Assert(state.Status.Values["available_ha_no_needs"] == "True");
            Assert(!state.Status.Values.ContainsKey("available_unlimited_soil"));
        });
        Run("HotelArchitect status read survives remove rename publication gap", () => {
            using var fixture = new Fixture();
            var path = Path.Combine(fixture.Game, HotelArchitectModule.BridgeRoot, "status.json");
            var retries = 0;
            var snapshot = HotelArchitectModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () => {
                retries++;
                fixture.GameFile(HotelArchitectModule.BridgeRoot + "status.json", Status(lastCommand: "fresh-request", success: true).GetRawText());
            });
            Assert(retries == 1 && snapshot.Status.Connected && snapshot.Status.LastCommand == "fresh-request");
        });
        Run("HotelArchitect incomplete publication is retried but permanent gaps remain disconnected", () => {
            using var fixture = new Fixture();
            var relative = HotelArchitectModule.BridgeRoot + "status.json"; var path = Path.Combine(fixture.Game, relative);
            fixture.GameFile(relative, "{"); var retries = 0;
            var snapshot = HotelArchitectModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () => {
                retries++;
                fixture.GameFile(relative, Status().GetRawText());
            });
            Assert(retries == 1 && snapshot.Status.Connected);
            File.Delete(path); retries = 0;
            snapshot = HotelArchitectModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () => retries++);
            Assert(retries == 2 && !snapshot.Status.Connected && snapshot.Status.SessionId == "" && string.IsNullOrEmpty(snapshot.Status.LastCommand));
        });
        Run("HotelArchitect publication retries never accept a wrong process or cached session", () => {
            using var fixture = new Fixture();
            var relative = HotelArchitectModule.BridgeRoot + "status.json"; var path = Path.Combine(fixture.Game, relative);
            var snapshot = HotelArchitectModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () =>
                fixture.GameFile(relative, Status(pid: 99).GetRawText()));
            Assert(!snapshot.Status.Connected);
            fixture.GameFile(relative, Status(session: "new-session-123", lastCommand: "new-request", success: true).GetRawText());
            snapshot = HotelArchitectModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42);
            Assert(snapshot.Status.SessionId == "new-session-123" && snapshot.Status.LastCommand == "new-request");
            Fails(() => HotelArchitectModule.AcceptAcknowledgement(snapshot, "session-123456", "new-request"));
        });
        Run("HotelArchitect reads heartbeat age from its open file after path replacement", () => {
            using var fixture = new Fixture(); var now = DateTime.UtcNow;
            var relative = HotelArchitectModule.BridgeRoot + "status.json"; var path = Path.Combine(fixture.Game, relative);
            fixture.GameFile(relative, Status(session: "old-session-123").GetRawText());
            File.SetLastWriteTimeUtc(path, now.AddSeconds(-10));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            File.Delete(path);
            fixture.GameFile(relative, Status(session: "new-session-123").GetRawText());
            File.SetLastWriteTimeUtc(path, now);
            Assert(!HotelArchitectModule.ReadSnapshotStream(stream, now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(HotelArchitectModule.ReadSnapshotFile(path, now.AddMinutes(-1), 42).Status.SessionId == "new-session-123");
        });
        Run("HotelArchitect oversized and stale status files fail closed without retries", () => {
            using var fixture = new Fixture(); var now = DateTime.UtcNow;
            var relative = HotelArchitectModule.BridgeRoot + "status.json"; var path = Path.Combine(fixture.Game, relative);
            fixture.GameFile(relative, new string(' ', 16_385)); var retries = 0;
            Assert(!HotelArchitectModule.ReadSnapshotFile(path, now.AddMinutes(-1), 42, () => retries++).Status.Connected);
            fixture.GameFile(relative, Status().GetRawText()); File.SetLastWriteTimeUtc(path, now.AddSeconds(-10));
            Assert(!HotelArchitectModule.ReadSnapshotFile(path, now.AddMinutes(-1), 42, () => retries++).Status.Connected);
            Assert(retries == 0);
        });
        Run("HotelArchitect supports absent Lua PID but rejects a mismatched reported PID", () => {
            Assert(Parse(Status(pid: null)).Status.Ready);
            Assert(Parse(Status(pid: 0)).Status.Ready);
            Assert(!Parse(Status(pid: 99)).Status.Connected);
        });
        Run("HotelArchitect stale future and previous-process heartbeats are disconnected", () => {
            var now = DateTime.UtcNow; var status = Status();
            Assert(!HotelArchitectModule.ParseSnapshot(status, now.AddSeconds(-7), now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(!HotelArchitectModule.ParseSnapshot(status, now.AddSeconds(3), now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(!HotelArchitectModule.ParseSnapshot(status, now.AddSeconds(-2), now.AddSeconds(-1), 42, now).Status.Connected);
        });
        Run("HotelArchitect coarse filesystem heartbeat timestamps stay connected across rounding boundaries", () => {
            var started = new DateTime(2026, 9, 26, 14, 8, 46, DateTimeKind.Utc);
            var resolution = TimeSpan.FromSeconds(2).Ticks;
            for (var milliseconds = 0; milliseconds < 8000; milliseconds += 50) {
                var now = started.AddMilliseconds(milliseconds);
                var status = Status(updated: new DateTimeOffset(now).ToUnixTimeSeconds());
                var rounded = new DateTime(((now.Ticks + resolution - 1) / resolution) * resolution, DateTimeKind.Utc);
                Assert(HotelArchitectModule.ParseSnapshot(status, rounded, started.AddMinutes(-1), 42, now).Status.Ready);
            }
        });
        Run("HotelArchitect timestamp tolerance remains bounded and never relaxes process freshness", () => {
            var now = DateTime.UtcNow; var status = Status(); var started = now.AddMinutes(-1);
            Assert(HotelArchitectModule.ParseSnapshot(status, now.AddSeconds(2), started, 42, now).Status.Connected);
            Assert(!HotelArchitectModule.ParseSnapshot(status, now.AddSeconds(2).AddTicks(1), started, 42, now).Status.Connected);
            Assert(HotelArchitectModule.ParseSnapshot(status, now.AddSeconds(-6), started, 42, now).Status.Connected);
            Assert(!HotelArchitectModule.ParseSnapshot(status, now.AddSeconds(-6).AddTicks(-1), started, 42, now).Status.Connected);
            Assert(!HotelArchitectModule.ParseSnapshot(status, now.AddTicks(-1), now, 42, now).Status.Connected);
            Assert(!HotelArchitectModule.ParseSnapshot(Status(pid: 99), now.AddSeconds(2), started, 42, now).Status.Connected);
        });
        Run("HotelArchitect rejects invalid schema version and injectable session", () => {
            Assert(!Parse(Status(version: "0.1.1")).Status.Connected);
            Assert(!Parse(Status(schema: 2)).Status.Connected);
            Assert(!Parse(Status(session: "session-123456\naction=ha_health")).Status.Connected);
            Assert(!Parse(Status(session: "")).Status.Connected);
        });
        Run("HotelArchitect requires explicit toggle state and current session", () => {
            var current = Parse(Status()).Status;
            Fails(() => Command(current, current, "ha_no_needs", null));
            Fails(() => Command(current, current with { SessionId = "old-session" }, "ha_no_needs", true));
            Fails(() => Command(current with { Ready = false }, current, "ha_no_needs", true));
            var command = Command(current, current, "ha_no_needs", true);
            Assert(command.Contains("action=ha_no_needs\nenabled=1\n"));
            Assert(command.Contains("created_utc=1234567890\n"));
        });
        Run("HotelArchitect rejects unavailable unknown and injectable controls", () => {
            var current = Parse(Status()).Status;
            Fails(() => Command(current, current, "unlimited_soil", true));
            var unavailable = current with { Values = new Dictionary<string, string>() };
            Fails(() => Command(unavailable, unavailable, "ha_no_needs", true));
            Fails(() => Command(current, current, "unknown", true));
            Fails(() => Command(current, current, "ha_no_needs\naction=ha_health", true));
            Fails(() => HotelArchitectModule.FormatCommand(current, current, "ha_no_needs", true, "bad\nrequest", 1));
        });
        Run("HotelArchitect disable all bypasses feature availability and has no toggle input", () => {
            var current = Parse(Status()).Status;
            Assert(Command(current, current, "disable_all", null).Contains("action=disable_all\n"));
            var unavailable = current with { Ready = false, Values = new Dictionary<string, string>() };
            Assert(Command(unavailable, unavailable, "disable_all", null).Contains("action=disable_all\n"));
            Fails(() => Command(unavailable with { Connected = false }, unavailable, "disable_all", null));
            Fails(() => Command(current, current, "disable_all", true));
            Fails(() => Command(current, current, "ha_health", true));
            Assert(Command(current, current, "ha_health", null).Contains("action=ha_health\n"));
        });
        Run("HotelArchitect timestamps must be positive and toggle intent is explicit", () => {
            var current = Parse(Status()).Status;
            foreach (var stamp in new[] { 0L, -1L, long.MinValue })
                Fails(() => HotelArchitectModule.FormatCommand(current, current, "ha_no_needs", true, "6c3962171e544382a06f94038a2b83df", stamp));
            Assert(Command(current, current, "ha_no_needs", false).Contains("enabled=0\n"));
            Assert(Command(current, current, "ha_furniture_condition", true).Contains("action=ha_furniture_condition\nenabled=1\n"));
            Fails(() => Command(current, current, "ha_furniture_condition", null));
            Fails(() => Command(current, current, "ha_health", false));
        });
        Run("HotelArchitect command acknowledgements preserve success and rejection", () => {
            var success = Parse(Status(lastCommand: "abc", success: true, message: "applied"));
            Assert(success.Status.LastCommand == "abc" && success.LastSuccess && success.LastMessage == "applied");
            var rejected = Parse(Status(lastCommand: "abc", success: false, message: "binding unavailable"));
            Assert(!rejected.LastSuccess && rejected.LastMessage == "binding unavailable");
        });
        Run("HotelArchitect acknowledgement waits through publication gaps without accepting old results", () => {
            var pending = Parse(Status(lastCommand: "previous-request", success: true));
            var gap = new HotelArchitectModule.Snapshot(TrainerStatus.Offline("Waiting for Hotel Architect trainer status."), false, "");
            var accepted = Parse(Status(lastCommand: "current-request", success: true, message: "Applied"));
            Assert(!HotelArchitectModule.AcceptAcknowledgement(pending, "session-123456", "current-request"));
            Assert(!HotelArchitectModule.AcceptAcknowledgement(gap, "session-123456", "current-request"));
            Assert(HotelArchitectModule.AcceptAcknowledgement(accepted, "session-123456", "current-request"));
        });
        Run("HotelArchitect acknowledgement rejects fresh different sessions and matching failed requests", () => {
            var changed = Parse(Status(session: "another-session", lastCommand: "current-request", success: true));
            Fails(() => HotelArchitectModule.AcceptAcknowledgement(changed, "session-123456", "current-request"));
            var failedRequest = Parse(Status(lastCommand: "current-request", success: false, message: "Binding unavailable"));
            Fails(() => HotelArchitectModule.AcceptAcknowledgement(failedRequest, "session-123456", "current-request"));
        });
        Run("HotelArchitect cleanup survives lost readiness and unavailable bindings", () => {
            var current = Parse(Status()).Status with { Ready = false, Values = new Dictionary<string, string>() };
            foreach (var feature in new HotelArchitectModule(".").Definition.Features.Where(f => f.IsToggle)) {
                Assert(Command(current, current, feature.Id, false).Contains("enabled=0\n"));
                Fails(() => Command(current, current, feature.Id, true));
            }
            foreach (var feature in new HotelArchitectModule(".").Definition.Features.Where(f => !f.IsToggle))
                Fails(() => Command(current, current, feature.Id, null));
            Fails(() => Command(current with { Connected = false }, current, "ha_disable_aging", false));
        });
        Run("HotelArchitect exposes all 24 screenshot controls without numeric hotkeys", () => {
            var definition = new HotelArchitectModule(".").Definition;
            Assert(definition.Id == "hotel-architect" && definition.AppId == "1602000");
            Assert(definition.Features.Count == 24);
            Assert(definition.Features.Select(feature => feature.Id).Distinct().Count() == 24);
            Assert(definition.Features.Count(feature => feature.IsToggle) == 4);
            Assert(definition.Features.Count(feature => feature.Number != null) == 20);
            Assert(definition.Features.Where(feature => feature.Number != null).All(feature => feature.Hotkey == ""));
            Assert(definition.Features.Select(feature => feature.Id).SequenceEqual(HotelArchitectModule.FeatureIds));
            foreach (var feature in definition.Features.Where(feature => feature.Number != null)) {
                var number = feature.Number!;
                Assert(HotelArchitectModule.ValidateAmount(feature.Id, number.Minimum) == number.Minimum);
                Assert(HotelArchitectModule.ValidateAmount(feature.Id, number.Maximum) == number.Maximum);
                Assert(HotelArchitectModule.ValidateAmount(feature.Id, number.DefaultValue) == number.DefaultValue);
                Fails(() => HotelArchitectModule.ValidateAmount(feature.Id, number.Minimum - number.Step));
                Fails(() => HotelArchitectModule.ValidateAmount(feature.Id, number.Maximum + number.Step));
                Fails(() => HotelArchitectModule.ValidateAmount(feature.Id, number.Minimum + number.Step / 2));
                Fails(() => HotelArchitectModule.ValidateAmount(feature.Id, null));
            }
        });
        Run("HotelArchitect every numeric command requires value and current target availability", () => {
            var current = Parse(Status()).Status;
            var request = Guid.NewGuid().ToString("N");
            foreach (var feature in new HotelArchitectModule(".").Definition.Features.Where(feature => feature.Number != null)) {
                var amount = feature.Number!.DefaultValue;
                var command = HotelArchitectModule.FormatCommand(current, current, feature.Id, null, request, 1234567890, amount);
                Assert(command.Contains("action=" + feature.Id + "\n"));
                Assert(command.EndsWith("amount=" + amount.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n", StringComparison.Ordinal));
                Fails(() => HotelArchitectModule.FormatCommand(current, current, feature.Id, null, request, 1234567890));
                Fails(() => HotelArchitectModule.FormatCommand(current, current, feature.Id, true, request, 1234567890, amount));
                Fails(() => HotelArchitectModule.FormatCommand(current with { Values = new Dictionary<string, string>() }, current, feature.Id, null, request, 1234567890, amount));
            }
            Fails(() => HotelArchitectModule.FormatCommand(current, current, "ha_no_needs", true, request, 1234567890, 1));
            Fails(() => HotelArchitectModule.FormatCommand(current, current, "disable_all", null, request, 1234567890, 1));
        });
        Run("HotelArchitect decimal values are serialized without locale separators", () => {
            var current = Parse(Status()).Status;
            var prior = System.Globalization.CultureInfo.CurrentCulture;
            try {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
                var text = HotelArchitectModule.FormatCommand(current, current, "ha_health", null, Guid.NewGuid().ToString("N"), 1, 72.25m);
                Assert(text.EndsWith("amount=72.25\n", StringComparison.Ordinal));
            } finally { System.Globalization.CultureInfo.CurrentCulture = prior; }
        });
        Run("HotelArchitect content heartbeat rejects fresh copied stale status", () => {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Assert(!Parse(Status(updated: now - 7)).Status.Connected);
            Assert(!Parse(Status(updated: now + 3)).Status.Connected);
            Assert(Parse(Status(updated: now)).Status.Connected);
        });
        Run("HotelArchitect package identity cannot be substituted", () => {
            foreach (var field in new[] { "game_id", "version", "game_executable_sha256", "schema_version" }) {
                using var fixture = new Fixture();
                var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Package, "package.json")))!;
                json[field] = "invalid";
                File.WriteAllText(Path.Combine(fixture.Package, "package.json"), json.ToJsonString());
                Fails(() => HotelArchitectInstaller.Install(fixture.Package, fixture.Game, () => { }));
                Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
            }
        });
        Run("HotelArchitect aging reads never change the native setting", () => {
            var memory = new AgingFixture(); using var aging = memory.Create();
            Assert(aging.Observe(memory.Context).Available);
            Assert(!aging.Observe(memory.Context).Enabled && memory.Value == 1 && memory.Writes.Count == 0);
        });
        Run("HotelArchitect aging captures and restores only its boolean", () => {
            var memory = new AgingFixture(); using var aging = memory.Create();
            aging.Set(memory.Context, true);
            Assert(memory.Value == 0 && aging.Observe(memory.Context).Enabled);
            aging.Set(memory.Context, true); // Repeated enable must not replace original 1 with captured 0.
            aging.Set(memory.Context, false);
            Assert(memory.Value == 1 && !aging.Observe(memory.Context).Enabled);
            Assert(memory.Writes.SequenceEqual(new byte[] { 0, 1 }));
        });
        Run("HotelArchitect aging preserves an originally disabled difficulty setting", () => {
            var memory = new AgingFixture { Value = 0 }; using var aging = memory.Create();
            aging.Set(memory.Context, true); aging.Set(memory.Context, false);
            Assert(memory.Value == 0 && memory.Writes.Count == 0);
        });
        Run("HotelArchitect aging preserves a later native game change", () => {
            var memory = new AgingFixture(); using var aging = memory.Create();
            aging.Set(memory.Context, true); memory.Value = 1;
            Assert(!aging.Observe(memory.Context).Enabled);
            aging.Set(memory.Context, false);
            Assert(memory.Value == 1 && memory.Writes.SequenceEqual(new byte[] { 0 }));
        });
        Run("HotelArchitect aging keeps its capture across transient status gaps", () => {
            var memory = new AgingFixture(); using var aging = memory.Create();
            aging.Set(memory.Context, true);
            Assert(!aging.Observe(null).Available);
            memory.WorldAvailable = false;
            Assert(!aging.Observe(memory.Context).Available);
            Fails(() => aging.Set(memory.Context, false));
            memory.WorldAvailable = true;
            Assert(aging.Observe(memory.Context).Enabled);
            aging.Set(memory.Context, false);
            Assert(memory.Value == 1);
        });
        Run("HotelArchitect all-off cleanup reports unconfirmed world and retains recovery", () => {
            var memory = new AgingFixture(); using var aging = memory.Create();
            aging.Set(memory.Context, true);
            memory.WorldAvailable = false;
            Fails(aging.RestoreOwned);
            Assert(aging.HasOwnership && memory.Value == 0);
            memory.WorldAvailable = true;
            aging.RestoreOwned();
            Assert(!aging.HasOwnership && memory.Value == 1);
        });
        Run("HotelArchitect aging does not restore previous-world difficulty settings", () => {
            var memory = new AgingFixture(); using var aging = memory.Create();
            aging.Set(memory.Context, true);
            memory.Context = memory.Context with { World = "300:400" };
            memory.Value = 0; // New world's own setting.
            Assert(!aging.Observe(memory.Context).Enabled);
            aging.Set(memory.Context, false);
            Assert(memory.Value == 0 && memory.Writes.SequenceEqual(new byte[] { 0 }));
        });
        Run("HotelArchitect aging does not restore an exited process into a replacement", () => {
            var memory = new AgingFixture(); using var aging = memory.Create();
            aging.Set(memory.Context, true);
            memory.Context = memory.Context with { ProcessId = 43, Started = memory.Context.Started.AddMinutes(1) };
            Assert(!aging.Observe(memory.Context).Enabled);
            aging.Set(memory.Context, false);
            Assert(memory.Writes.SequenceEqual(new byte[] { 0 }));
        });
        Run("HotelArchitect aging retains original after an ambiguous successful write", () => {
            var memory = new AgingFixture { FailAfterWrite = true }; using var aging = memory.Create();
            Fails(() => aging.Set(memory.Context, true));
            Assert(memory.Value == 0);
            memory.FailAfterWrite = false;
            aging.Set(memory.Context, false);
            Assert(memory.Value == 1);
        });
        Run("HotelArchitect aging refuses non-boolean memory without any write", () => {
            var memory = new AgingFixture { Value = 2 }; using var aging = memory.Create();
            Fails(() => aging.Observe(memory.Context));
            Fails(() => aging.Set(memory.Context, true));
            Assert(memory.Value == 2 && memory.Writes.Count == 0);
        });
        Run("HotelArchitect aging rechecks world identity immediately before mutation", () => {
            var memory = new AgingFixture(); var checks = 0;
            using var aging = new HotelArchitectAging(context => ++checks == 1, memory.Open);
            Fails(() => aging.Set(memory.Context, true));
            Assert(memory.Value == 1 && memory.Writes.Count == 0);
        });
        Run("HotelArchitect aging normal shutdown restores the same live world", () => {
            var memory = new AgingFixture(); var aging = memory.Create();
            aging.Set(memory.Context, true); aging.Dispose(); aging.Dispose();
            Assert(memory.Value == 1 && memory.Writes.SequenceEqual(new byte[] { 0, 1 }));
            Fails(() => aging.Set(memory.Context, true));
        });
        Run("HotelArchitect aging shutdown never writes without a matching fresh world", () => {
            var memory = new AgingFixture(); var aging = memory.Create();
            aging.Set(memory.Context, true); memory.WorldAvailable = false; aging.Dispose();
            Assert(memory.Value == 0 && memory.Writes.SequenceEqual(new byte[] { 0 }));
        });
        Run("HotelArchitect unsupported executable cannot be installed through stale discovery", () => {
            using var fixture = new Fixture();
            fixture.GameFile(HotelArchitectModule.Executable, "Unsupported executable");
            var installed = new GameInstallation(fixture.Game, HotelArchitectModule.SupportedSteamBuild, true, false, false, true, "fixture");
            var module = new HotelArchitectModule(fixture.BaseDirectory);
            Fails(() => module.InstallAsync(installed).GetAwaiter().GetResult());
            Assert(!File.Exists(Path.Combine(fixture.Game, HotelArchitectInstaller.NativeLoader)));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, HotelArchitectModule.Executable)) == "Unsupported executable");
        });
        Console.WriteLine($"HotelArchitect tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static JsonElement Status(int? pid = 42, string version = HotelArchitectModule.TrainerVersion, int schema = 1, string session = "session-123456", string lastCommand = "", bool success = false, string message = "", long? updated = null) => JsonSerializer.SerializeToElement(new {
        schema_version = schema, version, process_id = pid, session_id = session, ready = true, message = "Ready",
        updated_utc = updated ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        enabled = new { ha_no_needs = false, ha_always_noon = false, ha_furniture_condition = false, ha_disable_aging = false },
        values = new { ha_no_needs = 100, ha_always_noon = 50, ha_furniture_condition = 25 },
        available = HotelArchitectModule.FeatureIds.ToDictionary(id => id, _ => true),
        last_command = lastCommand, last_success = success, last_message = message
    });
    private static HotelArchitectModule.Snapshot Parse(JsonElement json) { var now = DateTime.UtcNow; return HotelArchitectModule.ParseSnapshot(json, now, now.AddMinutes(-1), 42, now); }
    private static string Command(TrainerStatus current, TrainerStatus requested, string action, bool? enabled) => HotelArchitectModule.FormatCommand(current, requested, action, enabled, "6c3962171e544382a06f94038a2b83df", 1234567890, new HotelArchitectModule(".").Definition.Features.SingleOrDefault(feature => feature.Id == action)?.Number?.DefaultValue);
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation rejection."); }
    private static void Run(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }

    private sealed class AgingFixture
    {
        internal HotelArchitectAging.Context Context = new("fixture", 42, DateTime.UtcNow.AddMinutes(-1), "100:200");
        internal byte Value = 1;
        internal bool WorldAvailable = true, FailAfterWrite;
        internal readonly List<byte> Writes = new();
        internal HotelArchitectAging Create() => new(context => WorldAvailable && context == Context, Open);
        internal HotelArchitectAging.IMemory Open(HotelArchitectAging.Context context, bool writable)
        {
            if (context != Context) throw new IOException("Different process or world.");
            return new Memory(this, writable);
        }
        private sealed class Memory(AgingFixture fixture, bool writable) : HotelArchitectAging.IMemory
        {
            public byte Read() => fixture.Value;
            public void Write(byte expected, byte value)
            {
                if (!writable || fixture.Value != expected) throw new IOException("Native value changed.");
                if (expected == value) return;
                fixture.Value = value; fixture.Writes.Add(value);
                if (fixture.FailAfterWrite) throw new IOException("Simulated readback failure after successful write.");
            }
            public void Dispose() { }
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "HappyCheats-HotelArchitect-tests-" + Guid.NewGuid().ToString("N"));
        private readonly HashSet<string> paths = new();
        public string BaseDirectory => root;
        public string Package => Path.Combine(root, "payload", "hotel-architect");
        public string Game => Path.Combine(root, "game");
        public Fixture()
        {
            Directory.CreateDirectory(Game);
            foreach (var path in HotelArchitectInstaller.RequiredFiles) PayloadFile(path, "fixture " + path);
            Manifest();
        }
        public void PayloadFile(string path, string content) { paths.Add(path); Put(Package, path, content); }
        public void GameFile(string path, string content) => Put(Game, path, content);
        private static void Put(string root, string path, string content) { var target = SafePath(root, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, content); }
        public void Manifest() => WriteJson(Path.Combine(Package, "package.json"), new { schema_version = 1, game_id = "hotel-architect", version = HotelArchitectModule.TrainerVersion, game_executable_sha256 = HotelArchitectModule.SupportedExecutableHash, files = paths.Select(path => new { path, sha256 = Hash(Path.Combine(Package, path)) }) });
        public void Dispose()
        {
            var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("HappyCheats-HotelArchitect-tests-", StringComparison.Ordinal)) throw new IOException("Invalid test cleanup path.");
            Directory.Delete(resolved, true);
        }
    }
}
