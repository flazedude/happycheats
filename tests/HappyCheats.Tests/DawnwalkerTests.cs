using System.Text.Json;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class DawnwalkerTests
{
    private static int passed, failed;
    public static int RunAll()
    {
        passed = failed = 0;
        Run("Dawnwalker verified installation and owned removal", () => {
            using var fixture = new Fixture();
            DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { });
            Assert(DawnwalkerInstaller.Installed(fixture.Game));
            Assert(DawnwalkerInstaller.MatchesPackage(fixture.Package, fixture.Game));
            Assert(Directory.Exists(Path.Combine(fixture.Game, DawnwalkerModule.BridgeRoot)));
            DawnwalkerInstaller.Remove(fixture.Game, () => { });
            Assert(!DawnwalkerInstaller.Installed(fixture.Game));
            Assert(!File.Exists(Path.Combine(fixture.Game, DawnwalkerInstaller.NativeLoader)));
        });
        Run("Dawnwalker foreign native loader blocks every payload write", () => {
            using var fixture = new Fixture();
            fixture.GameFile(DawnwalkerInstaller.NativeLoader, "another mod loader");
            Fails(() => DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(!DawnwalkerInstaller.Installed(fixture.Game));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, DawnwalkerInstaller.NativeLoader)) == "another mod loader");
            Assert(Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Count() == 1);
        });
        Run("Dawnwalker existing mods configuration is preserved", () => {
            using var fixture = new Fixture();
            var path = "Dawnwalker/Binaries/Win64/ue4ss/Mods/mods.txt";
            fixture.PayloadFile(path, "HappyCheatsDawnwalker : 1"); fixture.Manifest();
            fixture.GameFile(path, "OtherMod : 1");
            Fails(() => DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, path)) == "OtherMod : 1");
            Assert(!DawnwalkerInstaller.Installed(fixture.Game));
        });
        Run("Dawnwalker corrupted payload fails before install", () => {
            using var fixture = new Fixture();
            File.AppendAllText(Path.Combine(fixture.Package, DawnwalkerInstaller.MainScript), "corrupt");
            Fails(() => DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { }));
            Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("Dawnwalker modified owned file survives update and removal", () => {
            using var fixture = new Fixture(); DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile(DawnwalkerInstaller.MainScript, "user changed script");
            Fails(() => DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { }));
            DawnwalkerInstaller.Remove(fixture.Game, () => { });
            Assert(File.ReadAllText(Path.Combine(fixture.Game, DawnwalkerInstaller.MainScript)) == "user changed script");
        });
        Run("Dawnwalker identical foreign plugin never becomes owned", () => {
            using var fixture = new Fixture();
            fixture.GameFile(DawnwalkerInstaller.MainScript, File.ReadAllText(Path.Combine(fixture.Package, DawnwalkerInstaller.MainScript)));
            DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { });
            DawnwalkerInstaller.Remove(fixture.Game, () => { });
            Assert(File.Exists(Path.Combine(fixture.Game, DawnwalkerInstaller.MainScript)));
        });
        Run("Dawnwalker update verifies previous ownership", () => {
            using var fixture = new Fixture(); DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.PayloadFile(DawnwalkerInstaller.MainScript, "updated mod"); fixture.Manifest();
            DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { });
            Assert(Hash(Path.Combine(fixture.Game, DawnwalkerInstaller.MainScript)) == Hash(Path.Combine(fixture.Package, DawnwalkerInstaller.MainScript)));
        });
        Run("Dawnwalker interrupted install rolls back all written files", () => {
            using var fixture = new Fixture(); var checks = 0;
            Fails(() => DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { if (++checks == 5) throw new IOException("Game started"); }));
            Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("Dawnwalker rollback preserves a concurrently modified file", () => {
            using var fixture = new Fixture(); var checks = 0;
            Fails(() => DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => {
                if (++checks != 5) return;
                var written = Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).First(path => !path.EndsWith(".happycheats-stage"));
                File.WriteAllText(written, "concurrent user edit");
                throw new IOException("Game started");
            }));
            var remaining = Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).ToArray();
            Assert(remaining.Length == 1 && File.ReadAllText(remaining[0]) == "concurrent user edit");
        });
        Run("Dawnwalker running game blocks installation", () => {
            using var fixture = new Fixture();
            Fails(() => DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => throw new IOException("Running")));
            Assert(!Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("Dawnwalker removal preserves unrelated game and mod files", () => {
            using var fixture = new Fixture();
            fixture.GameFile("Dawnwalker/Content/Paks/game.pak", "game"); fixture.GameFile("Dawnwalker/Binaries/Win64/ue4ss/Mods/OtherMod/main.lua", "other");
            DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { });
            DawnwalkerInstaller.Remove(fixture.Game, () => { });
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "Dawnwalker/Content/Paks/game.pak")) == "game");
            Assert(File.ReadAllText(Path.Combine(fixture.Game, "Dawnwalker/Binaries/Win64/ue4ss/Mods/OtherMod/main.lua")) == "other");
            Assert(File.Exists(Path.Combine(fixture.Game, DawnwalkerInstaller.NativeLoader)));
            Assert(File.Exists(Path.Combine(fixture.Game, DawnwalkerInstaller.LoaderLibrary)));
            Assert(File.Exists(Path.Combine(fixture.Game, "Dawnwalker/Binaries/Win64/ue4ss/Mods/mods.txt")));
            Assert(!File.Exists(Path.Combine(fixture.Game, DawnwalkerInstaller.MainScript)));
        });
        Run("Dawnwalker removal preserves loader for mods added after installation", () => {
            using var fixture = new Fixture(); DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile("Dawnwalker/Binaries/Win64/ue4ss/Mods/NewMod/Scripts/main.lua", "new mod");
            DawnwalkerInstaller.Remove(fixture.Game, () => { });
            Assert(File.Exists(Path.Combine(fixture.Game, DawnwalkerInstaller.NativeLoader)));
            Assert(File.Exists(Path.Combine(fixture.Game, "Dawnwalker/Binaries/Win64/ue4ss/UE4SS-settings.ini")));
        });
        Run("Dawnwalker required loader configuration and bridge files cannot be omitted", () => {
            foreach (var required in DawnwalkerInstaller.RequiredFiles) {
                using var fixture = new Fixture();
                WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = DawnwalkerInstaller.RequiredFiles.Where(path => path != required).Select(path => new { path, sha256 = Hash(Path.Combine(fixture.Package, path)) }) });
                Fails(() => DawnwalkerInstaller.ReadPackage(fixture.Package));
            }
        });
        Run("Dawnwalker incomplete and changed installed payloads require an update", () => {
            using var fixture = new Fixture(); DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { });
            fixture.GameFile(DawnwalkerInstaller.MainScript, "changed");
            Assert(DawnwalkerInstaller.Installed(fixture.Game));
            Assert(!DawnwalkerInstaller.MatchesPackage(fixture.Package, fixture.Game));
            File.Delete(Path.Combine(fixture.Game, "Dawnwalker/Binaries/Win64/ue4ss/Mods/mods.txt"));
            Assert(!DawnwalkerInstaller.Installed(fixture.Game));
        });
        Run("Dawnwalker discovery reuses unchanged hashes without weakening installer verification", () => {
            using var fixture = new Fixture(); DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { });
            var module = new DawnwalkerModule(fixture.BaseDirectory);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            using var source = new FileStream(Path.Combine(fixture.Package, DawnwalkerInstaller.LoaderLibrary), FileMode.Open, FileAccess.Read, FileShare.None);
            using var installed = new FileStream(Path.Combine(fixture.Game, DawnwalkerInstaller.LoaderLibrary), FileMode.Open, FileAccess.Read, FileShare.None);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            Fails(() => DawnwalkerInstaller.MatchesPackage(fixture.Package, fixture.Game));
        });
        Run("Dawnwalker discovery rehashes modified and missing installed runtime files", () => {
            using var fixture = new Fixture(); DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { });
            var module = new DawnwalkerModule(fixture.BaseDirectory);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            var path = Path.Combine(fixture.Game, DawnwalkerInstaller.LoaderLibrary);
            var original = File.ReadAllText(path); var modified = File.GetLastWriteTimeUtc(path);
            fixture.GameFile(DawnwalkerInstaller.LoaderLibrary, new string('x', original.Length));
            File.SetLastWriteTimeUtc(path, modified.AddSeconds(2));
            Assert(!module.MatchesPackageForDiscovery(fixture.Game));
            fixture.GameFile(DawnwalkerInstaller.LoaderLibrary, original);
            File.SetLastWriteTimeUtc(path, modified.AddSeconds(4));
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            File.Delete(path);
            Assert(!module.MatchesPackageForDiscovery(fixture.Game));
        });
        Run("Dawnwalker discovery revalidates changed source files and manifest", () => {
            using var fixture = new Fixture(); DawnwalkerInstaller.Install(fixture.Package, fixture.Game, () => { });
            var module = new DawnwalkerModule(fixture.BaseDirectory);
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            var path = Path.Combine(fixture.Package, DawnwalkerInstaller.MainScript);
            var original = File.ReadAllText(path); var modified = File.GetLastWriteTimeUtc(path);
            fixture.PayloadFile(DawnwalkerInstaller.MainScript, new string('x', original.Length));
            File.SetLastWriteTimeUtc(path, modified.AddSeconds(2));
            Fails(() => module.MatchesPackageForDiscovery(fixture.Game));
            fixture.PayloadFile(DawnwalkerInstaller.MainScript, original);
            File.SetLastWriteTimeUtc(path, modified.AddSeconds(4));
            Assert(module.MatchesPackageForDiscovery(fixture.Game));
            WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = Array.Empty<object>() });
            Fails(() => module.MatchesPackageForDiscovery(fixture.Game));
        });
        Run("Dawnwalker package cannot escape trainer paths", () => {
            using var fixture = new Fixture();
            foreach (var path in new[] { "../outside.dll", "Dawnwalker/Content/Paks/game.pak", DawnwalkerInstaller.ModRoot + "bridge/command.txt", DawnwalkerInstaller.ModRoot + "Scripts/../../OtherMod/main.lua" }) {
                WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = new[] { new { path, sha256 = new string('A', 64) } } });
                Fails(() => DawnwalkerInstaller.ReadPackage(fixture.Package));
            }
        });
        Run("Dawnwalker package rejects missing runtime and duplicate entries", () => {
            using var fixture = new Fixture();
            WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = Array.Empty<object>() });
            Fails(() => DawnwalkerInstaller.ReadPackage(fixture.Package));
            var entry = new { path = DawnwalkerInstaller.MainScript, sha256 = Hash(Path.Combine(fixture.Package, DawnwalkerInstaller.MainScript)) };
            WriteJson(Path.Combine(fixture.Package, "package.json"), new { files = new[] { entry, entry } });
            Fails(() => DawnwalkerInstaller.ReadPackage(fixture.Package));
        });
        Run("Dawnwalker removal rejects forged receipt paths", () => {
            using var fixture = new Fixture(); fixture.GameFile("keep.txt", "keep");
            WriteJson(Path.Combine(fixture.Game, DawnwalkerInstaller.ReceiptPath), new DawnwalkerInstaller.Receipt("dawnwalker", new() { new("keep.txt", Hash(Path.Combine(fixture.Game, "keep.txt"))) }));
            Fails(() => DawnwalkerInstaller.Remove(fixture.Game, () => { }));
            Assert(File.Exists(Path.Combine(fixture.Game, "keep.txt")));
        });

        Run("Dawnwalker valid current-process heartbeat exposes feature availability", () => {
            var state = Parse(Status());
            Assert(state.Status.Connected && state.Status.Ready && state.Status.SessionId == "session-123456");
            Assert(state.Status.Values["available_dawnwalker_health"] == "True");
            Assert(!state.Status.Values.ContainsKey("available_unlimited_soil"));
        });
        Run("Dawnwalker status read survives remove rename publication gap", () => {
            using var fixture = new Fixture();
            var path = Path.Combine(fixture.Game, DawnwalkerModule.BridgeRoot, "status.json");
            var retries = 0;
            var snapshot = DawnwalkerModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () => {
                retries++;
                fixture.GameFile(DawnwalkerModule.BridgeRoot + "status.json", Status(lastCommand: "fresh-request", success: true).GetRawText());
            });
            Assert(retries == 1 && snapshot.Status.Connected && snapshot.Status.LastCommand == "fresh-request");
        });
        Run("Dawnwalker incomplete publication is retried but permanent gaps remain disconnected", () => {
            using var fixture = new Fixture();
            var relative = DawnwalkerModule.BridgeRoot + "status.json"; var path = Path.Combine(fixture.Game, relative);
            fixture.GameFile(relative, "{"); var retries = 0;
            var snapshot = DawnwalkerModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () => {
                retries++;
                fixture.GameFile(relative, Status().GetRawText());
            });
            Assert(retries == 1 && snapshot.Status.Connected);
            File.Delete(path); retries = 0;
            snapshot = DawnwalkerModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () => retries++);
            Assert(retries == 2 && !snapshot.Status.Connected && snapshot.Status.SessionId == "" && string.IsNullOrEmpty(snapshot.Status.LastCommand));
        });
        Run("Dawnwalker publication retries never accept a wrong process or cached session", () => {
            using var fixture = new Fixture();
            var relative = DawnwalkerModule.BridgeRoot + "status.json"; var path = Path.Combine(fixture.Game, relative);
            var snapshot = DawnwalkerModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42, () =>
                fixture.GameFile(relative, Status(pid: 99).GetRawText()));
            Assert(!snapshot.Status.Connected);
            fixture.GameFile(relative, Status(session: "new-session-123", lastCommand: "new-request", success: true).GetRawText());
            snapshot = DawnwalkerModule.ReadSnapshotFile(path, DateTime.UtcNow.AddMinutes(-1), 42);
            Assert(snapshot.Status.SessionId == "new-session-123" && snapshot.Status.LastCommand == "new-request");
            Fails(() => DawnwalkerModule.AcceptAcknowledgement(snapshot, "session-123456", "new-request"));
        });
        Run("Dawnwalker reads heartbeat age from its open file after path replacement", () => {
            using var fixture = new Fixture(); var now = DateTime.UtcNow;
            var relative = DawnwalkerModule.BridgeRoot + "status.json"; var path = Path.Combine(fixture.Game, relative);
            fixture.GameFile(relative, Status(session: "old-session-123").GetRawText());
            File.SetLastWriteTimeUtc(path, now.AddSeconds(-10));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            File.Delete(path);
            fixture.GameFile(relative, Status(session: "new-session-123").GetRawText());
            File.SetLastWriteTimeUtc(path, now);
            Assert(!DawnwalkerModule.ReadSnapshotStream(stream, now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(DawnwalkerModule.ReadSnapshotFile(path, now.AddMinutes(-1), 42).Status.SessionId == "new-session-123");
        });
        Run("Dawnwalker oversized and stale status files fail closed without retries", () => {
            using var fixture = new Fixture(); var now = DateTime.UtcNow;
            var relative = DawnwalkerModule.BridgeRoot + "status.json"; var path = Path.Combine(fixture.Game, relative);
            fixture.GameFile(relative, new string(' ', 16_385)); var retries = 0;
            Assert(!DawnwalkerModule.ReadSnapshotFile(path, now.AddMinutes(-1), 42, () => retries++).Status.Connected);
            fixture.GameFile(relative, Status().GetRawText()); File.SetLastWriteTimeUtc(path, now.AddSeconds(-10));
            Assert(!DawnwalkerModule.ReadSnapshotFile(path, now.AddMinutes(-1), 42, () => retries++).Status.Connected);
            Assert(retries == 0);
        });
        Run("Dawnwalker supports absent Lua PID but rejects a mismatched reported PID", () => {
            Assert(Parse(Status(pid: null)).Status.Ready);
            Assert(Parse(Status(pid: 0)).Status.Ready);
            Assert(!Parse(Status(pid: 99)).Status.Connected);
        });
        Run("Dawnwalker stale future and previous-process heartbeats are disconnected", () => {
            var now = DateTime.UtcNow; var status = Status();
            Assert(!DawnwalkerModule.ParseSnapshot(status, now.AddSeconds(-7), now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(!DawnwalkerModule.ParseSnapshot(status, now.AddSeconds(3), now.AddMinutes(-1), 42, now).Status.Connected);
            Assert(!DawnwalkerModule.ParseSnapshot(status, now.AddSeconds(-2), now.AddSeconds(-1), 42, now).Status.Connected);
        });
        Run("Dawnwalker coarse filesystem heartbeat timestamps stay connected across rounding boundaries", () => {
            var started = new DateTime(2026, 9, 26, 14, 8, 46, DateTimeKind.Utc); var status = Status();
            var resolution = TimeSpan.FromSeconds(2).Ticks;
            for (var milliseconds = 0; milliseconds < 8000; milliseconds += 50) {
                var now = started.AddMilliseconds(milliseconds);
                var rounded = new DateTime(((now.Ticks + resolution - 1) / resolution) * resolution, DateTimeKind.Utc);
                Assert(DawnwalkerModule.ParseSnapshot(status, rounded, started.AddMinutes(-1), 42, now).Status.Ready);
            }
        });
        Run("Dawnwalker timestamp tolerance remains bounded and never relaxes process freshness", () => {
            var now = DateTime.UtcNow; var status = Status(); var started = now.AddMinutes(-1);
            Assert(DawnwalkerModule.ParseSnapshot(status, now.AddSeconds(2), started, 42, now).Status.Connected);
            Assert(!DawnwalkerModule.ParseSnapshot(status, now.AddSeconds(2).AddTicks(1), started, 42, now).Status.Connected);
            Assert(DawnwalkerModule.ParseSnapshot(status, now.AddSeconds(-6), started, 42, now).Status.Connected);
            Assert(!DawnwalkerModule.ParseSnapshot(status, now.AddSeconds(-6).AddTicks(-1), started, 42, now).Status.Connected);
            Assert(!DawnwalkerModule.ParseSnapshot(status, now.AddTicks(-1), now, 42, now).Status.Connected);
            Assert(!DawnwalkerModule.ParseSnapshot(Status(pid: 99), now.AddSeconds(2), started, 42, now).Status.Connected);
        });
        Run("Dawnwalker rejects invalid schema version and injectable session", () => {
            Assert(!Parse(Status(version: "0.1.1")).Status.Connected);
            Assert(!Parse(Status(schema: 2)).Status.Connected);
            Assert(!Parse(Status(session: "session-123456\naction=dawnwalker_refill")).Status.Connected);
            Assert(!Parse(Status(session: "")).Status.Connected);
        });
        Run("Dawnwalker requires explicit toggle state and current session", () => {
            var current = Parse(Status()).Status;
            Fails(() => Command(current, current, "dawnwalker_health", null));
            Fails(() => Command(current, current with { SessionId = "old-session" }, "dawnwalker_health", true));
            Fails(() => Command(current with { Ready = false }, current, "dawnwalker_health", true));
            var command = Command(current, current, "dawnwalker_health", true);
            Assert(command.Contains("action=dawnwalker_health\nenabled=1\n"));
            Assert(command.Contains("created_utc=1234567890\n"));
        });
        Run("Dawnwalker rejects unavailable unknown and injectable controls", () => {
            var current = Parse(Status()).Status;
            Fails(() => Command(current, current, "unlimited_soil", true));
            var unavailable = current with { Values = new Dictionary<string, string>() };
            Fails(() => Command(unavailable, unavailable, "dawnwalker_health", true));
            Fails(() => Command(current, current, "unknown", true));
            Fails(() => Command(current, current, "dawnwalker_health\naction=dawnwalker_refill", true));
            Fails(() => DawnwalkerModule.FormatCommand(current, current, "dawnwalker_health", true, "bad\nrequest", 1));
        });
        Run("Dawnwalker disable all bypasses feature availability and has no toggle input", () => {
            var current = Parse(Status()).Status;
            Assert(Command(current, current, "disable_all", null).Contains("action=disable_all\n"));
            var unavailable = current with { Ready = false, Values = new Dictionary<string, string>() };
            Assert(Command(unavailable, unavailable, "disable_all", null).Contains("action=disable_all\n"));
            Fails(() => Command(unavailable with { Connected = false }, unavailable, "disable_all", null));
            Fails(() => Command(current, current, "disable_all", true));
            Fails(() => Command(current, current, "dawnwalker_refill", true));
            Assert(Command(current, current, "dawnwalker_refill", null).Contains("action=dawnwalker_refill\n"));
        });
        Run("Dawnwalker timestamps must be positive and toggle intent is explicit", () => {
            var current = Parse(Status()).Status;
            foreach (var stamp in new[] { 0L, -1L, long.MinValue })
                Fails(() => DawnwalkerModule.FormatCommand(current, current, "dawnwalker_health", true, "6c3962171e544382a06f94038a2b83df", stamp));
            Assert(Command(current, current, "dawnwalker_health", false).Contains("enabled=0\n"));
            Assert(Command(current, current, "dawnwalker_stamina", true).Contains("action=dawnwalker_stamina\nenabled=1\n"));
            Fails(() => Command(current, current, "dawnwalker_stamina", null));
            Fails(() => Command(current, current, "dawnwalker_refill", false));
        });
        Run("Dawnwalker command acknowledgements preserve success and rejection", () => {
            var success = Parse(Status(lastCommand: "abc", success: true, message: "applied"));
            Assert(success.Status.LastCommand == "abc" && success.LastSuccess && success.LastMessage == "applied");
            var rejected = Parse(Status(lastCommand: "abc", success: false, message: "binding unavailable"));
            Assert(!rejected.LastSuccess && rejected.LastMessage == "binding unavailable");
        });
        Run("Dawnwalker acknowledgement waits through publication gaps without accepting old results", () => {
            var pending = Parse(Status(lastCommand: "previous-request", success: true));
            var gap = new DawnwalkerModule.Snapshot(TrainerStatus.Offline("Waiting for The Blood of Dawnwalker trainer status."), false, "");
            var accepted = Parse(Status(lastCommand: "current-request", success: true, message: "Applied"));
            Assert(!DawnwalkerModule.AcceptAcknowledgement(pending, "session-123456", "current-request"));
            Assert(!DawnwalkerModule.AcceptAcknowledgement(gap, "session-123456", "current-request"));
            Assert(DawnwalkerModule.AcceptAcknowledgement(accepted, "session-123456", "current-request"));
        });
        Run("Dawnwalker acknowledgement rejects fresh different sessions and matching failed requests", () => {
            var changed = Parse(Status(session: "another-session", lastCommand: "current-request", success: true));
            Fails(() => DawnwalkerModule.AcceptAcknowledgement(changed, "session-123456", "current-request"));
            var failedRequest = Parse(Status(lastCommand: "current-request", success: false, message: "Binding unavailable"));
            Fails(() => DawnwalkerModule.AcceptAcknowledgement(failedRequest, "session-123456", "current-request"));
        });
        Run("Dawnwalker exposes implemented controls and explicit one-shot XP", () => {
            var definition = new DawnwalkerModule(".").Definition;
            Assert(definition.AppId == "3751260" && definition.Id == "dawnwalker");
            Assert(definition.Features.Count == 14);
            Assert(DawnwalkerModule.FeatureIds.SequenceEqual(definition.Features.Select(feature => feature.Id)));
            Assert(definition.Features.Select(feature => feature.Hotkey).Distinct().Count() == definition.Features.Count);
            Assert(definition.Features.Where(feature => feature.Id is not ("dawnwalker_refill" or "dawnwalker_xp" or "dawnwalker_teleport")).All(feature => feature.IsToggle));
            Assert(!definition.Features.Single(feature => feature.Id == "dawnwalker_refill").IsToggle);
            Assert(definition.Features.Single(feature => feature.Id == "dawnwalker_health").Description.Contains("lethal hits", StringComparison.Ordinal));
            Assert(!definition.Features.Single(feature => feature.Id == "dawnwalker_xp").IsToggle);
            Assert(!definition.Features.Single(feature => feature.Id == "dawnwalker_teleport").IsToggle);
            var autoTeleport = definition.Features.Single(feature => feature.Id == "dawnwalker_auto_teleport");
            Assert(autoTeleport.IsToggle && autoTeleport.Hotkey == "NumPad4" && autoTeleport.Name == "Auto teleport");
            Assert(autoTeleport.Description.Contains("new custom map marker", StringComparison.Ordinal)
                && autoTeleport.Description.Contains("does not use an existing marker", StringComparison.Ordinal));
            Assert(definition.Features.Single(feature => feature.Id == "dawnwalker_invisible").Hotkey == "F9");
            Assert(definition.Features.Single(feature => feature.Id == "dawnwalker_health").Name == "Infinite health");
            Assert(definition.Features.Single(feature => feature.Id == "dawnwalker_stamina").Name == "Infinite stamina");
        });
        Run("Dawnwalker new controls require availability and XP cannot become a toggle", () => {
            var current = Parse(Status()).Status;
            foreach (var feature in new DawnwalkerModule(".").Definition.Features) {
                var state = feature.IsToggle ? true : (bool?)null;
                Assert(Command(current, current, feature.Id, state).Contains("action=" + feature.Id + "\n"));
                Fails(() => Command(current with { Values = new Dictionary<string, string>() }, current, feature.Id, state));
                if (feature.IsToggle) Fails(() => Command(current, current, feature.Id, null));
                else { Fails(() => Command(current, current, feature.Id, true)); Fails(() => Command(current, current, feature.Id, false)); }
            }
        });
        Run("Dawnwalker blood toggle requires explicit intent and blood availability", () => {
            var current = Parse(Status()).Status;
            Assert(current.Values["available_dawnwalker_blood"] == "True");
            Assert(Command(current, current, "dawnwalker_blood", true).Contains("action=dawnwalker_blood\nenabled=1\n"));
            Assert(Command(current, current, "dawnwalker_blood", false).Contains("action=dawnwalker_blood\nenabled=0\n"));
            Fails(() => Command(current, current, "dawnwalker_blood", null));
            var values = new Dictionary<string, string>(current.Values) { ["available_dawnwalker_blood"] = "False" };
            var unavailable = current with { Values = values };
            Fails(() => Command(unavailable, unavailable, "dawnwalker_blood", true));
        });
        Run("Dawnwalker auto teleport preserves explicit intent and asynchronous outcome", () => {
            var current = Parse(Status(autoTeleport: true, autoTeleportStatus: "Auto teleport enabled; drop a new custom map marker")).Status;
            Assert(current.Enabled["dawnwalker_auto_teleport"] && current.Values["available_dawnwalker_auto_teleport"] == "True");
            Assert(current.Values["dawnwalker_auto_teleport_status"] == "Auto teleport enabled; drop a new custom map marker");
            Assert(Command(current, current, "dawnwalker_auto_teleport", true).Contains("action=dawnwalker_auto_teleport\nenabled=1\n"));
            Assert(Command(current, current, "dawnwalker_auto_teleport", false).Contains("action=dawnwalker_auto_teleport\nenabled=0\n"));
            Fails(() => Command(current, current, "dawnwalker_auto_teleport", null));
            Fails(() => Command(current, current with { SessionId = "previous-session" }, "dawnwalker_auto_teleport", true));
            var waiting = current with { Ready = false, Values = new Dictionary<string, string> { ["available_dawnwalker_auto_teleport"] = "False" } };
            Assert(Command(waiting, waiting, "dawnwalker_auto_teleport", false).Contains("enabled=0\n"));
            Fails(() => Command(waiting, waiting, "dawnwalker_auto_teleport", true));
            var completed = Parse(Status(autoTeleport: true, autoTeleportStatus: "Auto teleport complete; drop a new custom map marker")).Status;
            Assert(completed.Enabled["dawnwalker_auto_teleport"] && completed.Values["dawnwalker_auto_teleport_status"] == "Auto teleport complete; drop a new custom map marker");
            Assert(!Parse(Status(version: "0.3.1")).Status.Connected);
        });
        Run("Dawnwalker cleanup survives lost readiness and unavailable bindings", () => {
            var current = Parse(Status()).Status with { Ready = false, Values = new Dictionary<string, string>() };
            foreach (var feature in new DawnwalkerModule(".").Definition.Features.Where(f => f.IsToggle)) {
                Assert(Command(current, current, feature.Id, false).Contains("enabled=0\n"));
                Fails(() => Command(current, current, feature.Id, true));
            }
            Fails(() => Command(current, current, "dawnwalker_xp", null));
            Fails(() => Command(current, current, "dawnwalker_refill", null));
            Fails(() => Command(current with { Connected = false }, current, "dawnwalker_damage", false));
        });
        Run("Dawnwalker unsupported executable cannot be installed through stale discovery", () => {
            using var fixture = new Fixture();
            fixture.GameFile(DawnwalkerModule.Executable, "Unsupported executable");
            var installed = new GameInstallation(fixture.Game, "25232147", true, false, false, true, "fixture");
            var module = new DawnwalkerModule(fixture.Package);
            Fails(() => module.InstallAsync(installed).GetAwaiter().GetResult());
            Assert(!File.Exists(Path.Combine(fixture.Game, DawnwalkerInstaller.NativeLoader)));
            Assert(File.ReadAllText(Path.Combine(fixture.Game, DawnwalkerModule.Executable)) == "Unsupported executable");
        });
        Console.WriteLine($"Dawnwalker tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static JsonElement Status(int? pid = 42, string version = DawnwalkerModule.TrainerVersion, int schema = 1, string session = "session-123456", string lastCommand = "", bool success = false, string message = "", bool autoTeleport = false, string autoTeleportStatus = "Off") => JsonSerializer.SerializeToElement(new {
        schema_version = schema, version, process_id = pid, session_id = session, ready = true, message = "Ready",
        enabled = new { dawnwalker_health = false, dawnwalker_stamina = false, dawnwalker_blood = false, dawnwalker_auto_teleport = autoTeleport },
        values = new { health = 100, stamina = 50, blood = 25, dawnwalker_auto_teleport_status = autoTeleportStatus },
        available = DawnwalkerModule.FeatureIds.ToDictionary(id => id, _ => true),
        last_command = lastCommand, last_success = success, last_message = message
    });
    private static DawnwalkerModule.Snapshot Parse(JsonElement json) { var now = DateTime.UtcNow; return DawnwalkerModule.ParseSnapshot(json, now, now.AddMinutes(-1), 42, now); }
    private static string Command(TrainerStatus current, TrainerStatus requested, string action, bool? enabled) => DawnwalkerModule.FormatCommand(current, requested, action, enabled, "6c3962171e544382a06f94038a2b83df", 1234567890);
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation rejection."); }
    private static void Run(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "HappyCheats-Dawnwalker-tests-" + Guid.NewGuid().ToString("N"));
        private readonly HashSet<string> paths = new();
        public string BaseDirectory => root;
        public string Package => Path.Combine(root, "payload", "dawnwalker");
        public string Game => Path.Combine(root, "game");
        public Fixture()
        {
            Directory.CreateDirectory(Game);
            foreach (var path in DawnwalkerInstaller.RequiredFiles) PayloadFile(path, "fixture " + path);
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
            if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("HappyCheats-Dawnwalker-tests-", StringComparison.Ordinal)) throw new IOException("Invalid test cleanup path.");
            Directory.Delete(resolved, true);
        }
    }
}
