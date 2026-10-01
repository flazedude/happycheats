using System.Text.Json;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class WukongTests
{
    private static int passed, failed;
    public static int RunAll()
    {
        passed = failed = 0;
        Run("Wukong verified installation and owned removal", () => {
            using var fixture = new Fixture();
            Assert(WukongInstaller.RequiredFiles.Length == 9 && WukongInstaller.RequiredFiles.Contains(WukongInstaller.VTableLayout));
            WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            Assert(WukongInstaller.Installed(fixture.Game));
            Assert(WukongInstaller.MatchesPackage(fixture.Package, fixture.Game));
            Assert(Directory.Exists(Path.Combine(fixture.Game, WukongModule.BridgeRoot)));
            WukongInstaller.Remove(fixture.Game, () => { });
            Assert(!WukongInstaller.Installed(fixture.Game));
            Assert(!File.Exists(Path.Combine(fixture.Game, WukongInstaller.NativeLoader)));
            Assert(!File.Exists(Path.Combine(fixture.Game, WukongInstaller.VTableLayout)));
        });
        Run("Wukong legacy installation gains the owned ProcessEvent layout", () => {
            using var fixture = new Fixture();
            var legacy = WukongInstaller.RequiredFiles.Where(path => path != WukongInstaller.VTableLayout).ToArray();
            foreach (var path in legacy) fixture.GameFile(path, File.ReadAllText(Path.Combine(fixture.Package, path)));
            WriteJson(Path.Combine(fixture.Game, WukongInstaller.ReceiptPath), new WukongInstaller.Receipt("black-myth-wukong",
                legacy.Select(path => new WukongInstaller.Entry(path, Hash(Path.Combine(fixture.Game, path)))).ToList()));
            Assert(!WukongInstaller.Installed(fixture.Game));
            Assert(!WukongInstaller.MatchesPackage(fixture.Package, fixture.Game));
            WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            Assert(WukongInstaller.Installed(fixture.Game) && WukongInstaller.MatchesPackage(fixture.Package, fixture.Game));
            var receipt = JsonSerializer.Deserialize<WukongInstaller.Receipt>(File.ReadAllText(Path.Combine(fixture.Game, WukongInstaller.ReceiptPath)))!;
            Assert(receipt.Files.Count == 9 && receipt.Files.Any(file => file.Path == WukongInstaller.VTableLayout));
            WukongInstaller.Remove(fixture.Game, () => { });
            Assert(!File.Exists(Path.Combine(fixture.Game, WukongInstaller.VTableLayout)));
        });
        Run("Wukong foreign ProcessEvent layout blocks every payload write", () => {
            using var fixture = new Fixture();
            fixture.GameFile(WukongInstaller.VTableLayout, "another mod's vtable layout");
            Fails(() => WukongInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, WukongInstaller.VTableLayout)) == "another mod's vtable layout");
            Assert(Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Count() == 1);
        });
        Run("Wukong modified ProcessEvent layout remains unmodified on update and removal", () => {
            using var fixture = new Fixture(); WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            var module = new WukongModule(fixture.BaseDirectory);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            fixture.GameFile(WukongInstaller.VTableLayout, "user changed ProcessEvent layout");
            Assert(!module.MatchesPackageForDiscovery(fixture.Game));
            Fails(() => WukongInstaller.Install(fixture.Package, fixture.Game, () => { }));
            WukongInstaller.Remove(fixture.Game, () => { });
            Assert(File.ReadAllText(Path.Combine(fixture.Game, WukongInstaller.VTableLayout)) == "user changed ProcessEvent layout");
        });
        Run("Wukong matching foreign ProcessEvent layout never becomes owned", () => {
            using var fixture = new Fixture();
            fixture.GameFile(WukongInstaller.VTableLayout, File.ReadAllText(Path.Combine(fixture.Package, WukongInstaller.VTableLayout)));
            WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            WukongInstaller.Remove(fixture.Game, () => { });
            Assert(File.Exists(Path.Combine(fixture.Game, WukongInstaller.VTableLayout)));
        });
        Run("Wukong foreign native loader blocks every payload write", () => {
            using var fixture = new Fixture();
            fixture.GameFile(WukongInstaller.NativeLoader, "another mod loader");
            Fails(() => WukongInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(!WukongInstaller.Installed(fixture.Game));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, WukongInstaller.NativeLoader)) == "another mod loader");
            Assert(Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Count() == 1);
        });
        Run("Wukong existing mods configuration is preserved", () => {
            using var fixture = new Fixture();
            var path = "b1/Binaries/Win64/ue4ss/Mods/mods.txt";
            fixture.PayloadFile(path, "HappyCheatsWukong : 1"); fixture.Manifest();
            fixture.GameFile(path, "OtherMod : 1");
            Fails(() => WukongInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, path)) == "OtherMod : 1");
            Assert(!WukongInstaller.Installed(fixture.Game));
        });
        Run("Wukong corrupted payload fails before install", () => {
            using var fixture = new Fixture();
            File.AppendAllText(Path.Combine(fixture.Package, WukongInstaller.MainScript), "corrupt");
            Fails(() => WukongInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("Wukong modified owned file survives update and removal", () => {
            using var fixture = new Fixture(); WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile(WukongInstaller.MainScript, "user changed script");
            Fails(() => WukongInstaller.Install(fixture.Package, fixture.Game, () => { }));
            WukongInstaller.Remove(fixture.Game, () => { });
            Assert(File.ReadAllText(Path.Combine(fixture.Game, WukongInstaller.MainScript)) == "user changed script");
        });
        Run("Wukong identical foreign plugin never becomes owned", () => {
            using var fixture = new Fixture();
            fixture.GameFile(WukongInstaller.MainScript, File.ReadAllText(Path.Combine(fixture.Package, WukongInstaller.MainScript)));
            WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            WukongInstaller.Remove(fixture.Game, () => { });
            Assert(File.Exists(Path.Combine(fixture.Game, WukongInstaller.MainScript)));
        });
        Run("Wukong update verifies previous ownership", () => {
            using var fixture = new Fixture(); WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.PayloadFile(WukongInstaller.MainScript, "updated mod"); fixture.Manifest();
            WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            Assert(Hash(Path.Combine(fixture.Game, WukongInstaller.MainScript)) == Hash(Path.Combine(fixture.Package, WukongInstaller.MainScript)));
        });
        Run("Wukong interrupted install rolls back all written files", () => {
            using var fixture = new Fixture(); var checks = 0;
            Fails(() => WukongInstaller.Install(fixture.Package, fixture.Game, () => { if (++checks == 5) throw new IOException("Game started"); }));
            Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("Wukong rollback preserves a concurrently modified file", () => {
            using var fixture = new Fixture(); var checks = 0;
            Fails(() => WukongInstaller.Install(fixture.Package, fixture.Game, () => {
                if (++checks != 5) return;
                var written = Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).First(path => !path.EndsWith(".happycheats-stage"));
                File.WriteAllText(written, "concurrent user edit");
                throw new IOException("Game started");
            }));
            var remaining = Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).ToArray();
            Assert(remaining.Length == 1 && File.ReadAllText(remaining[0]) == "concurrent user edit");
        });
        Run("Wukong running game blocks installation", () => {
            using var fixture = new Fixture();
            Fails(() => WukongInstaller.Install(fixture.Package, fixture.Game, () => throw new IOException("Running")));
            Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("Wukong removal preserves unrelated game and mod files", () => {
            using var fixture = new Fixture();
            fixture.GameFile("Wukong/Content/Paks/game.pak", "game"); fixture.GameFile("b1/Binaries/Win64/ue4ss/Mods/OtherMod/main.lua", "other");
            WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            WukongInstaller.Remove(fixture.Game, () => { });
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "Wukong/Content/Paks/game.pak")) == "game");
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "b1/Binaries/Win64/ue4ss/Mods/OtherMod/main.lua")) == "other");
            Assert(File.Exists(Path.Combine(fixture.Game, WukongInstaller.NativeLoader)));
            Assert(File.Exists(Path.Combine(fixture.Game, WukongInstaller.LoaderLibrary)));
            Assert(File.Exists(Path.Combine(fixture.Game, WukongInstaller.VTableLayout)));
            Assert(File.Exists(Path.Combine(fixture.Game, "b1/Binaries/Win64/ue4ss/Mods/mods.txt")));
            Assert(!File.Exists(Path.Combine(fixture.Game, WukongInstaller.MainScript)));
        });
        Run("Wukong removal preserves loader for mods added after installation", () => {
            using var fixture = new Fixture(); WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile("b1/Binaries/Win64/ue4ss/Mods/NewMod/Scripts/main.lua", "new mod");
            WukongInstaller.Remove(fixture.Game, () => { });
            Assert(File.Exists(Path.Combine(fixture.Game, WukongInstaller.NativeLoader)));
            Assert(File.Exists(Path.Combine(fixture.Game, "b1/Binaries/Win64/ue4ss/UE4SS-settings.ini")));
            Assert(File.Exists(Path.Combine(fixture.Game, WukongInstaller.VTableLayout)));
        });
        Run("Wukong required loader configuration and bridge files cannot be omitted", () => {
            foreach (var required in WukongInstaller.RequiredFiles) {
                using var fixture = new Fixture();
                WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = WukongInstaller.RequiredFiles.Where(path => path != required).Select(path => new { path, sha256 = Hash(Path.Combine(fixture.Package, path)) }) });
                Fails(() => WukongInstaller.ReadPackage(fixture.Package));
            }
        });
        Run("Wukong incomplete and changed installed payloads require an update", () => {
            using var fixture = new Fixture(); WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile(WukongInstaller.MainScript, "changed");
            Assert(WukongInstaller.Installed(fixture.Game));
            Assert(!WukongInstaller.MatchesPackage(fixture.Package, fixture.Game));
            File.Delete(Path.Combine(fixture.Game, "b1/Binaries/Win64/ue4ss/Mods/mods.txt"));
            Assert(!WukongInstaller.Installed(fixture.Game));
        });
        Run("Wukong discovery reuses unchanged hashes without weakening installer verification", () => {
            using var fixture = new Fixture(); WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            var module = new WukongModule(fixture.BaseDirectory);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            using var source = new FileStream(Path.Combine(fixture.Package, WukongInstaller.LoaderLibrary), FileMode.Open, FileAccess.Read, FileShare.None);
            using var installed = new FileStream(Path.Combine(fixture.Game, WukongInstaller.LoaderLibrary), FileMode.Open, FileAccess.Read, FileShare.None);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            Fails(() => WukongInstaller.MatchesPackage(fixture.Package, fixture.Game));
        });
        Run("Wukong discovery rehashes modified and missing installed runtime files", () => {
            using var fixture = new Fixture(); WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            var module = new WukongModule(fixture.BaseDirectory);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            var path = Path.Combine(fixture.Game, WukongInstaller.LoaderLibrary);
            var original = File.ReadAllText(path); var modified = File.GetLastWriteTimeUtc(path);
            fixture.GameFile(WukongInstaller.LoaderLibrary, new string('x', original.Length));
            File.SetLastWriteTimeUtc(path, modified.AddSeconds(2));
            Assert(!module.MatchesPackageForDiscovery(fixture.Game));
            fixture.GameFile(WukongInstaller.LoaderLibrary, original);
            File.SetLastWriteTimeUtc(path, modified.AddSeconds(4));
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            File.Delete(path);
            Assert(!module.MatchesPackageForDiscovery(fixture.Game));
        });
        Run("Wukong discovery revalidates changed source files and manifest", () => {
            using var fixture = new Fixture(); WukongInstaller.Install(fixture.Package, fixture.Game, () => { });
            var module = new WukongModule(fixture.BaseDirectory);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            var path = Path.Combine(fixture.Package, WukongInstaller.MainScript);
            var original = File.ReadAllText(path); var modified = File.GetLastWriteTimeUtc(path);
            fixture.PayloadFile(WukongInstaller.MainScript, new string('x', original.Length));
            File.SetLastWriteTimeUtc(path, modified.AddSeconds(2));
            Fails(() => module.MatchesPackageForDiscovery(fixture.Game));
            fixture.PayloadFile(WukongInstaller.MainScript, original);
            File.SetLastWriteTimeUtc(path, modified.AddSeconds(4));
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = Array.Empty<object>() });
            Fails(() => module.MatchesPackageForDiscovery(fixture.Game));
        });
        Run("Wukong package cannot escape trainer paths", () => {
            using var fixture = new Fixture();
            foreach (var path in new[] { "../outside.dll", "Wukong/Content/Paks/game.pak", WukongInstaller.ModRoot + "bridge/command.txt", WukongInstaller.ModRoot + "Scripts/../../OtherMod/main.lua" }) {
                WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = new[] { new { path, sha256 = new string('A', 64) } } });
                Fails(() => WukongInstaller.ReadPackage(fixture.Package));
            }
        });
        Run("Wukong package rejects missing runtime and duplicate entries", () => {
            using var fixture = new Fixture();
            WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = Array.Empty<object>() });
            Fails(() => WukongInstaller.ReadPackage(fixture.Package));
            var entry = new { path = WukongInstaller.MainScript, sha256 = Hash(Path.Combine(fixture.Package, WukongInstaller.MainScript)) };
            WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = new[] { entry, entry } });
            Fails(() => WukongInstaller.ReadPackage(fixture.Package));
        });
        Run("Wukong removal rejects forged receipt paths", () => {
            using var fixture = new Fixture(); fixture.GameFile("keep.txt", "keep");
            WriteJson(Path.Combine(fixture.Game, WukongInstaller.ReceiptPath), new WukongInstaller.Receipt("black-myth-wukong", new() { new("keep.txt", Hash(Path.Combine(fixture.Game, "keep.txt"))) }));
            Fails(() => WukongInstaller.Remove(fixture.Game, () => { }));
            Assert(File.Exists(Path.Combine(fixture.Game, "keep.txt")));
        });

        Run("Wukong valid current-process heartbeat exposes feature availability", () => {
            var state = Parse(Status());
            Assert(state.Status.Connected && state.Status.Ready && state.Status.SessionId == "session-123456");
            Assert(state.Status.Values["available_wukong_health"] == "True");
            Assert(!state.Status.Values.ContainsKey("available_unlimited_soil"));
        });
        Run("Wukong status read survives remove rename publication gap", () => {
            using var fixture = new Fixture();
            var path = Path.Combine(fixture.Game, WukongModule.BridgeRoot, "status.json");
            var retries = 0;
            var snapshot = WukongModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () => {
                retries++;
                fixture.GameFile(WukongModule.BridgeRoot + "status.json", Status(lastCommand: "fresh-request", success: true).GetRawText());
            });
            Assert(retries == 1 && snapshot.Status.Connected && snapshot.Status.LastCommand == "fresh-request");
        });
        Run("Wukong incomplete publication is retried but permanent gaps remain disconnected", () => {
            using var fixture = new Fixture();
            var relative = WukongModule.BridgeRoot + "status.json"; var path = Path.Combine(fixture.Game, relative);
            fixture.GameFile(relative, "{"); var retries = 0;
            var snapshot = WukongModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () => {
                retries++;
                fixture.GameFile(relative, Status().GetRawText());
            });
            Assert(retries == 1 && snapshot.Status.Connected);
            File.Delete(path); retries = 0;
            snapshot = WukongModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () => retries++);
            Assert(retries == 2 && !snapshot.Status.Connected && snapshot.Status.SessionId == "" && string.IsNullOrEmpty(snapshot.Status.LastCommand));
        });
        Run("Wukong publication retries never accept a wrong process or cached session", () => {
            using var fixture = new Fixture();
            var relative = WukongModule.BridgeRoot + "status.json"; var path = Path.Combine(fixture.Game, relative);
            var snapshot = WukongModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () =>
                fixture.GameFile(relative, Status(pid: 99).GetRawText()));
            Assert(!snapshot.Status.Connected);
            fixture.GameFile(relative, Status(session: "new-session-123", lastCommand: "new-request", success: true).GetRawText());
            snapshot = WukongModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42);
            Assert(snapshot.Status.SessionId == "new-session-123" && snapshot.Status.LastCommand == "new-request");
            Fails(() => WukongModule.AcceptAcknowledgement(snapshot, "session-123456", "new-request"));
        });
        Run("Wukong reads heartbeat age from its open file after path replacement", () => {
            using var fixture = new Fixture(); var now = DateTime.UtcNow;
            var relative = WukongModule.BridgeRoot + "status.json"; var path = Path.Combine(fixture.Game, relative);
            fixture.GameFile(relative, Status(session: "old-session-123").GetRawText());
            File.SetLastWriteTimeUtc(path, now.AddSeconds(-10));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            File.Delete(path);
            fixture.GameFile(relative, Status(session: "new-session-123").GetRawText());
            File.SetLastWriteTimeUtc(path, now);
            Assert(!WukongModule.ReadSnapshotStream(stream, now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(WukongModule.ReadSnapshotFile(path, now.AddMinutes(-1), 42).Status.SessionId == "new-session-123");
        });
        Run("Wukong oversized and stale status files fail closed without retries", () => {
            using var fixture = new Fixture(); var now = DateTime.UtcNow;
            var relative = WukongModule.BridgeRoot + "status.json"; var path = Path.Combine(fixture.Game, relative);
            fixture.GameFile(relative, new string(' ', 16_385)); var retries = 0;
            Assert(!WukongModule.ReadSnapshotFile(path, now.AddMinutes(-1), 42, () => retries++).Status.Connected);
            fixture.GameFile(relative, Status().GetRawText()); File.SetLastWriteTimeUtc(path, now.AddSeconds(-10));
            Assert(!WukongModule.ReadSnapshotFile(path, now.AddMinutes(-1), 42, () => retries++).Status.Connected);
            Assert(retries == 0);
        });
        Run("Wukong supports absent Lua PID but rejects a mismatched reported PID", () => {
            Assert(Parse(Status(pid: null)).Status.Ready);
            Assert(Parse(Status(pid: 0)).Status.Ready);
            Assert(!Parse(Status(pid: 99)).Status.Connected);
        });
        Run("Wukong stale future and previous-process heartbeats are disconnected", () => {
            var now = DateTime.UtcNow; var status = Status();
            Assert(!WukongModule.ParseSnapshot(status, now.AddSeconds(-7), now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(!WukongModule.ParseSnapshot(status, now.AddSeconds(3), now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(!WukongModule.ParseSnapshot(status, now.AddSeconds(-2), now.AddSeconds(-1), 42, now).Status.Connected);
        });
        Run("Wukong coarse filesystem heartbeat timestamps stay connected across rounding boundaries", () => {
            var started = new DateTime(2026, 9, 26, 14, 8, 46, DateTimeKind.Utc); var status = Status();
            var resolution = TimeSpan.FromSeconds(2).Ticks;
            for (var milliseconds = 0; milliseconds < 8000; milliseconds += 50) {
                var now = started.AddMilliseconds(milliseconds);
                var rounded = new DateTime(((now.Ticks + resolution - 1) / resolution) * resolution, DateTimeKind.Utc);
                Assert(WukongModule.ParseSnapshot(status, rounded, started.AddMinutes(-1), 42, now).Status.Ready);
            }
        });
        Run("Wukong timestamp tolerance remains bounded and never relaxes process freshness", () => {
            var now = DateTime.UtcNow; var status = Status(); var started = now.AddMinutes(-1);
            Assert(WukongModule.ParseSnapshot(status, now.AddSeconds(2), started, 42, now).Status.Connected);
            Assert(!WukongModule.ParseSnapshot(status, now.AddSeconds(2).AddTicks(1), started, 42, now).Status.Connected);
            Assert(WukongModule.ParseSnapshot(status, now.AddSeconds(-6), started, 42, now).Status.Connected);
            Assert(!WukongModule.ParseSnapshot(status, now.AddSeconds(-6).AddTicks(-1), started, 42, now).Status.Connected);
            Assert(!WukongModule.ParseSnapshot(status, now.AddTicks(-1), now, 42, now).Status.Connected);
            Assert(!WukongModule.ParseSnapshot(Status(pid: 99), now.AddSeconds(2), started, 42, now).Status.Connected);
        });
        Run("Wukong rejects invalid schema version and injectable session", () => {
            Assert(!Parse(Status(version: "0.1.0")).Status.Connected);
            Assert(!Parse(Status(schema: 2)).Status.Connected);
            Assert(!Parse(Status(session: "session-123456\naction=wukong_focus")).Status.Connected);
            Assert(!Parse(Status(session: "")).Status.Connected);
        });
        Run("Wukong requires explicit toggle state and current session", () => {
            var current = Parse(Status()).Status;
            Fails(() => Command(current, current, "wukong_health", null));
            Fails(() => Command(current, current with { SessionId = "old-session" }, "wukong_health", true));
            Fails(() => Command(current with { Ready = false }, current, "wukong_health", true));
            var command = Command(current, current, "wukong_health", true);
            Assert(command.Contains("action=wukong_health\nenabled=1\n"));
            Assert(command.Contains("created_utc=1234567890\n"));
        });
        Run("Wukong rejects unavailable unknown and injectable controls", () => {
            var current = Parse(Status()).Status;
            Fails(() => Command(current, current, "unlimited_soil", true));
            var unavailable = current with { Values = new Dictionary<string, string>() };
            Fails(() => Command(unavailable, unavailable, "wukong_health", true));
            Fails(() => Command(current, current, "unknown", true));
            Fails(() => Command(current, current, "wukong_health\naction=wukong_focus", true));
            Fails(() => WukongModule.FormatCommand(current, current, "wukong_health", true, "bad\nrequest", 1));
        });
        Run("Wukong disable all bypasses feature availability and has no toggle input", () => {
            var current = Parse(Status()).Status;
            Assert(Command(current, current, "disable_all", null).Contains("action=disable_all\n"));
            var unavailable = current with { Ready = false, Values = new Dictionary<string, string>() };
            Assert(Command(unavailable, unavailable, "disable_all", null).Contains("action=disable_all\n"));
            Fails(() => Command(unavailable with { Connected = false }, unavailable, "disable_all", null));
            Fails(() => Command(current, current, "disable_all", true));
            Fails(() => Command(current, current, "wukong_focus", true));
            Assert(Command(current, current, "wukong_focus", null).Contains("action=wukong_focus\n"));
        });
        Run("Wukong timestamps must be positive and toggle intent is explicit", () => {
            var current = Parse(Status()).Status;
            foreach (var stamp in new[] { 0L, -1L, long.MinValue })
                Fails(() => WukongModule.FormatCommand(current, current, "wukong_health", true, "6c3962171e544382a06f94038a2b83df", stamp));
            Assert(Command(current, current, "wukong_health", false).Contains("enabled=0\n"));
            Assert(Command(current, current, "wukong_stamina", true).Contains("action=wukong_stamina\nenabled=1\n"));
            Fails(() => Command(current, current, "wukong_stamina", null));
            Fails(() => Command(current, current, "wukong_focus", false));
        });
        Run("Wukong command acknowledgements preserve success and rejection", () => {
            var success = Parse(Status(lastCommand: "abc", success: true, message: "applied"));
            Assert(success.Status.LastCommand == "abc" && success.LastSuccess && success.LastMessage == "applied");
            var rejected = Parse(Status(lastCommand: "abc", success: false, message: "binding unavailable"));
            Assert(!rejected.LastSuccess && rejected.LastMessage == "binding unavailable");
        });
        Run("Wukong acknowledgement waits through publication gaps without accepting old results", () => {
            var pending = Parse(Status(lastCommand: "previous-request", success: true));
            var gap = new WukongModule.Snapshot(TrainerStatus.Offline("Waiting for Black Myth: Wukong trainer status."), false, "");
            var accepted = Parse(Status(lastCommand: "current-request", success: true, message: "Applied"));
            Assert(!WukongModule.AcceptAcknowledgement(pending, "session-123456", "current-request"));
            Assert(!WukongModule.AcceptAcknowledgement(gap, "session-123456", "current-request"));
            Assert(WukongModule.AcceptAcknowledgement(accepted, "session-123456", "current-request"));
        });
        Run("Wukong acknowledgement rejects fresh different sessions and matching failed requests", () => {
            var changed = Parse(Status(session: "another-session", lastCommand: "current-request", success: true));
            Fails(() => WukongModule.AcceptAcknowledgement(changed, "session-123456", "current-request"));
            var failedRequest = Parse(Status(lastCommand: "current-request", success: false, message: "Binding unavailable"));
            Fails(() => WukongModule.AcceptAcknowledgement(failedRequest, "session-123456", "current-request"));
        });
        Run("Wukong exposes nine requested Steam controls with bounded progression amounts", () => {
            var definition = new WukongModule(".").Definition;
            Assert(definition.AppId == "2358720" && definition.Id == "black-myth-wukong");
            Assert(WukongModule.Executable == "b1/Binaries/Win64/b1-Win64-Shipping.exe");
            Assert(definition.Features.Count == 9);
            Assert(WukongModule.FeatureIds.SequenceEqual(definition.Features.Select(feature => feature.Id)));
            Assert(definition.Features.Select(feature => feature.Hotkey).SequenceEqual(Enumerable.Range(1, 9).Select(number => "F" + number)));
            Assert(definition.Features.Take(5).All(feature => feature.IsToggle));
            Assert(definition.Features.Skip(5).All(feature => !feature.IsToggle));
            Assert(definition.Features.Single(feature => feature.Id == "wukong_focus").Number == null);
            foreach (var feature in definition.Features.Skip(6)) {
                Assert(feature.Number != null);
                Assert(WukongModule.ValidateAmount(feature.Id, null) == feature.Number!.DefaultValue);
                Assert(WukongModule.ValidateAmount(feature.Id, feature.Number.Maximum) == feature.Number.Maximum);
            }
        });
        Run("Wukong controls require availability and one-shot intent", () => {
            var current = Parse(Status()).Status;
            foreach (var feature in new WukongModule(".").Definition.Features) {
                var state = feature.IsToggle ? true : (bool?)null;
                Assert(Command(current, current, feature.Id, state).Contains("action=" + feature.Id + "\n"));
                Fails(() => Command(current with { Values = new Dictionary<string, string>() }, current, feature.Id, state));
                if (feature.IsToggle) Fails(() => Command(current, current, feature.Id, null));
                else {
                    Fails(() => Command(current, current, feature.Id, true));
                    Fails(() => Command(current, current, feature.Id, false));
                }
            }
        });
        Run("Wukong quantity commands preserve selected integers and reject invalid amounts", () => {
            var current = Parse(Status()).Status;
            var id = Guid.NewGuid().ToString("N");
            string Amount(string action, decimal? value) => WukongModule.FormatCommand(current, current, action, null, id, 1234567890, value);
            Assert(Amount("wukong_xp", null).EndsWith("amount=1000\n", StringComparison.Ordinal));
            Assert(Amount("wukong_will", null).EndsWith("amount=1000\n", StringComparison.Ordinal));
            Assert(Amount("wukong_sparks", null).EndsWith("amount=1\n", StringComparison.Ordinal));
            foreach (var action in new[] { "wukong_xp", "wukong_sparks", "wukong_will" }) {
                Assert(Amount(action, 17).EndsWith("amount=17\n", StringComparison.Ordinal));
                foreach (var value in new[] { 0m, -1m, 1.5m, decimal.MaxValue, decimal.MinValue }) Fails(() => Amount(action, value));
            }
            Fails(() => Amount("wukong_sparks", 101));
            Fails(() => Amount("wukong_xp", 1_000_001));
            Fails(() => Amount("wukong_will", 1_000_001));
            Fails(() => Amount("wukong_focus", 1));
            Fails(() => Amount("disable_all", 1));
            Fails(() => WukongModule.FormatCommand(current, current, "wukong_health", true, id, 1234567890, 10));
            Assert(!Amount("wukong_focus", null).Contains("amount="));
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
                Assert(Amount("wukong_xp", 10000).EndsWith("amount=10000\n", StringComparison.Ordinal));
            } finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        });
        Run("Wukong cleanup survives lost readiness and unavailable bindings", () => {
            var current = Parse(Status()).Status with { Ready = false, Values = new Dictionary<string, string>() };
            foreach (var feature in new WukongModule(".").Definition.Features.Where(f => f.IsToggle)) {
                Assert(Command(current, current, feature.Id, false).Contains("enabled=0\n"));
                Fails(() => Command(current, current, feature.Id, true));
            }
            foreach (var feature in new WukongModule(".").Definition.Features.Where(f => !f.IsToggle))
                Fails(() => Command(current, current, feature.Id, null));
            Fails(() => Command(current with { Connected = false }, current, "wukong_damage", false));
        });
        Run("Wukong unsupported executable cannot be installed through stale discovery", () => {
            using var fixture = new Fixture();
            fixture.GameFile(WukongModule.Executable, "Unsupported executable");
            var installed = new GameInstallation(fixture.Game, WukongModule.SupportedSteamBuild, true, false, false, true, "fixture");
            var module = new WukongModule(fixture.BaseDirectory);
            Fails(() => module.InstallAsync(installed).GetAwaiter().GetResult());
            Assert(!File.Exists(Path.Combine(fixture.Game, WukongInstaller.NativeLoader)));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, WukongModule.Executable)) == "Unsupported executable");
        });
        Console.WriteLine($"Wukong tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static JsonElement Status(int? pid = 42, string version = WukongModule.TrainerVersion, int schema = 1, string session = "session-123456", string lastCommand = "", bool success = false, string message = "") => JsonSerializer.SerializeToElement(new {
        schema_version = schema, version, process_id = pid, session_id = session, ready = true, message = "Ready",
        enabled = new { wukong_health = false, wukong_mana = false, wukong_stamina = false, wukong_damage = false, wukong_gourd = false },
        values = new { wukong_health = 100, wukong_mana = 50, wukong_stamina = 25 },
        available = WukongModule.FeatureIds.ToDictionary(id => id, _ => true),
        last_command = lastCommand, last_success = success, last_message = message
    });
    private static WukongModule.Snapshot Parse(JsonElement json) { var now = DateTime.UtcNow; return WukongModule.ParseSnapshot(json, now, now.AddMinutes(-1), 42, now); }
    private static string Command(TrainerStatus current, TrainerStatus requested, string action, bool? enabled) => WukongModule.FormatCommand(current, requested, action, enabled, "6c3962171e544382a06f94038a2b83df", 1234567890);
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation rejection."); }
    private static void Run(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "HappyCheats-Wukong-tests-" + Guid.NewGuid().ToString("N"));
        private readonly HashSet<string> paths = new();
        public string BaseDirectory => root;
        public string Package => Path.Combine(root, "payload", "black-myth-wukong");
        public string Game => Path.Combine(root, "game");
        public Fixture()
        {
            Directory.CreateDirectory(Game);
            foreach (var path in WukongInstaller.RequiredFiles) PayloadFile(path, "fixture " + path);
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
            if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("HappyCheats-Wukong-tests-", StringComparison.Ordinal)) throw new IOException("Invalid test cleanup path.");
            Directory.Delete(resolved, true);
        }
    }
}
