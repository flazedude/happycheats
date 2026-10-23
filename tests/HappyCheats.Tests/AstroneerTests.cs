using System.Text.Json;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class AstroneerTests
{
    private static int passed, failed;
    public static int RunAll()
    {
        Run("Astroneer compatibility accepts exactly the three inspected executable profiles", () => {
            foreach (var (hash, version, build) in InspectedProfiles) {
                var profile = AstroneerModule.ProfileForHash(hash);
                Assert(profile?.GameVersion == version && profile.SteamBuild == build && profile.ExecutableSha256 == hash);
                Assert(AstroneerModule.ProfileForHash(hash.ToLowerInvariant()) == profile);
            }
            Assert(AstroneerModule.SupportedExecutableHash == InspectedProfiles[2].Hash);
        });
        Run("Astroneer compatibility rejects unknown incomplete and decorated hashes", () => {
            foreach (var hash in new string?[] { null, "", new string('A', 64) })
                Assert(AstroneerModule.ProfileForHash(hash) == null);
            foreach (var (hash, _, _) in InspectedProfiles) {
                Assert(AstroneerModule.ProfileForHash(hash[..63]) == null);
                Assert(AstroneerModule.ProfileForHash(" " + hash) == null);
                Assert(AstroneerModule.ProfileForHash(hash + " ") == null);
                Assert(AstroneerModule.ProfileForHash(hash[..^1] + (hash[^1] == '0' ? '1' : '0')) == null);
            }
        });
        Run("Astroneer inspection labels reflect matched executable version and build evidence", () => {
            foreach (var (hash, version, build) in InspectedProfiles) {
                var profile = AstroneerModule.ProfileForHash(hash);
                var prefix = $"ASTRONEER {version} detected (inspected Steam build {build}). ";
                Assert(AstroneerModule.InspectionMessage(profile, false, false) == prefix + "Experimental trainer; gameplay validation is pending.");
                Assert(AstroneerModule.InspectionMessage(profile, true, false) == prefix + "ASTRONEER trainer update required. Installed files differ from the current module.");
                Assert(AstroneerModule.InspectionMessage(profile, true, true) == prefix + "Experimental ASTRONEER trainer installed. Gameplay validation is pending.");
            }
            Assert(AstroneerModule.InspectionMessage(null, true, true) == "This ASTRONEER build needs a compatibility check.");
        });
        Run("Astroneer unknown executable cannot install even with a compatible discovery snapshot", () => {
            using var fixture = new Fixture();
            fixture.GameFile(AstroneerModule.Executable, "unrecognized updated executable");
            var installation = new GameInstallation(fixture.Game, "25599237", true, false, false, true, "Old discovery");
            var module = new AstroneerModule(fixture.Package);
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
            Assert(Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Count() == 1);
            var status = module.ReadStatusAsync(installation).GetAwaiter().GetResult();
            Assert(!status.Connected && status.Message == "This ASTRONEER build needs a compatibility check.");
        });
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
            Assert(state.Status.Values["available_invulnerability"] == "True");
            Assert(state.Status.Values["available_player_run_speed"] == "True");
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
            var previous = Parse(Status(version: "0.3.0"));
            Assert(!previous.Status.Connected && !previous.Status.Ready);
            Assert(previous.Status.Message == "Restart ASTRONEER to load the current trainer module.");
            Assert(!Parse(Status(version: "0.2.1")).Status.Connected);
            Assert(!Parse(Status(version: "0.2.0")).Status.Connected);
            Assert(!Parse(Status(version: "0.1.3")).Status.Connected);
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
        Run("Astroneer emergency off is allowed while connected but waiting for gameplay", () => {
            var waiting = Parse(Status()).Status with { Ready = false, Values = new Dictionary<string, string>() };
            Assert(Command(waiting, waiting, "disable_all", null).Contains("action=disable_all\n"));
            Fails(() => Command(waiting with { Connected = false }, waiting, "disable_all", null));
            Fails(() => Command(waiting, waiting with { SessionId = "previous-session" }, "disable_all", null));
            Fails(() => Command(waiting, waiting, "resource_collection_10x", false));
            Fails(() => Command(waiting, waiting, "add_bytes", null));
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
        Run("Astroneer exposes six controls with stable F1 through F6 hotkeys", () => {
            var definition = new AstroneerModule(".").Definition;
            Assert(definition.Features.Count == 6);
            Assert(!definition.Features.Any(feature => feature.Id == "unlimited_soil"));
            Assert(definition.Features.Single(feature => feature.Id == "add_bytes").Hotkey == "F4");
            Assert(definition.Features.Single(feature => feature.Id == "invulnerability").Hotkey == "F5");
            Assert(definition.Features.Single(feature => feature.Id == "player_run_speed").Hotkey == "F6");
            Assert(definition.Features.Where(feature => feature.Id is "invulnerability" or "player_run_speed").All(feature => feature.IsToggle));
            Assert(AstroneerModule.FeatureIds.SequenceEqual(definition.Features.Select(feature => feature.Id)));
        });
        Run("Astroneer overlay targets the actual shipping executable without a native menu payload", () => {
            Assert(new AstroneerModule(".").GameExecutables.SequenceEqual(new[] { "Astro/Binaries/Win64/Astro-Win64-Shipping.exe" }));
            Assert(!AstroneerInstaller.RequiredFiles.Any(path => path.EndsWith("/menu.lua", StringComparison.OrdinalIgnoreCase)));
        });
        Run("Astroneer exposes only the three supported resource multiplier choices", () => {
            var module = new AstroneerModule(".");
            Assert(module is IFeatureChoiceModule);
            var collection = module.Definition.Features.Single(feature => feature.Id == "resource_collection_10x");
            Assert(collection.ChoiceValueKey == "selected_collection_multiplier");
            Assert(collection.Choices!.SequenceEqual(new FeatureChoice[] { new("10", "10×"), new("20", "20×"), new("100", "100×") }));
            Assert(module.Definition.Features.Where(feature => feature.Id != collection.Id && feature.Id != "player_run_speed").All(feature => feature.Choices == null && feature.ChoiceValueKey == null));
        });
        Run("Astroneer status retains confirmed selected and effective multipliers", () => {
            Assert(Parse(Status()).Status.Values["selected_collection_multiplier"] == "10");
            foreach (var multiplier in new[] { 10, 20, 100 }) {
                var inactive = Parse(Status(selectedMultiplier: multiplier));
                Assert(inactive.Status.Values["selected_collection_multiplier"] == multiplier.ToString());
                Assert(inactive.Status.Values["collection_multiplier"] == "1");
                Assert(!inactive.Status.Enabled["resource_collection_10x"]);
                var active = Parse(Status(selectedMultiplier: multiplier, collectionEnabled: true));
                Assert(active.Status.Values["selected_collection_multiplier"] == multiplier.ToString());
                Assert(active.Status.Values["collection_multiplier"] == multiplier.ToString());
                Assert(active.Status.Enabled["resource_collection_10x"]);
            }
        });
        Run("Astroneer multiplier selection encodes 10 20 or 100 without toggling collection", () => {
            var current = Parse(Status()).Status;
            foreach (var multiplier in new[] { 10, 20, 100 }) {
                var command = ChoiceCommand(current, current, multiplier);
                Assert(command.Contains("action=set_collection_multiplier\nenabled=0\n"));
                Assert(command.EndsWith("multiplier=" + multiplier + "\n", StringComparison.Ordinal));
                Assert(command.Contains("session_id=" + current.SessionId + "\n"));
            }
            Assert(!Command(current, current, "resource_collection_10x", true).Contains("multiplier="));
        });
        Run("Astroneer rejects out-of-range missing and malformed multiplier choices", () => {
            var current = Parse(Status()).Status;
            foreach (int? multiplier in new int?[] { null, 0, 1, 11, 19, 21, 99, 101, -10, -100, int.MaxValue })
                Fails(() => ChoiceCommand(current, current, multiplier));
            foreach (var choice in new[] { "", "0", "1", "10.0", "+10", " 10", "20 ", "100.0", "0100", "1e2", "+100", " 100", "100 ", "100\naction=add_bytes" })
                Fails(() => AstroneerModule.ChoiceMultiplier("resource_collection_10x", choice));
            Assert(AstroneerModule.ChoiceMultiplier("resource_collection_10x", "10") == 10);
            Assert(AstroneerModule.ChoiceMultiplier("resource_collection_10x", "20") == 20);
            Assert(AstroneerModule.ChoiceMultiplier("resource_collection_10x", "100") == 100);
        });
        Run("Astroneer multiplier selection uses collection availability and current session", () => {
            var current = Parse(Status()).Status;
            var unavailable = current with { Values = new Dictionary<string, string> { ["available_set_collection_multiplier"] = "True" } };
            foreach (var multiplier in new[] { 10, 20, 100 }) {
                Fails(() => ChoiceCommand(unavailable, unavailable, multiplier));
                Fails(() => ChoiceCommand(current, current with { SessionId = "previous-session" }, multiplier));
                Fails(() => ChoiceCommand(current with { Connected = false }, current, multiplier));
                Fails(() => ChoiceCommand(current with { Ready = false }, current, multiplier));
            }
        });
        Run("Astroneer rejects toggle input on multiplier selection and unrelated extra multipliers", () => {
            var current = Parse(Status()).Status;
            foreach (var enabled in new[] { true, false })
                Fails(() => AstroneerModule.FormatCommand(current, current, "set_collection_multiplier", enabled, "6c3962171e544382a06f94038a2b83df", 1234567890, 100));
            foreach (var action in AstroneerModule.FeatureIds.Concat(new[] { "disable_all" })) {
                bool? enabled = action is "add_bytes" or "disable_all" ? null : true;
                Fails(() => AstroneerModule.FormatCommand(current, current, action, enabled, "6c3962171e544382a06f94038a2b83df", 1234567890, 100));
            }
        });
        Run("Astroneer rejects selection on other features before accessing a game", () => {
            var module = new AstroneerModule(".");
            var installation = new GameInstallation("", "", false, false, false, false, "Not installed");
            var current = Parse(Status()).Status;
            foreach (var action in new[] { "add_bytes", "unlimited_oxygen", "invulnerability", "unknown", "set_collection_multiplier", "set_run_speed_multiplier" })
                Fails(() => module.SelectAsync(installation, current, action, "100").GetAwaiter().GetResult());
            Fails(() => module.SelectAsync(installation, current, "resource_collection_10x", "30").GetAwaiter().GetResult());
            Fails(() => module.SelectAsync(installation, current, "player_run_speed", "20").GetAwaiter().GetResult());
        });
        Run("Astroneer speed metadata and dispatch remain independent of collection", () => {
            var feature = new AstroneerModule(".").Definition.Features.Single(feature => feature.Id == "player_run_speed");
            Assert(feature.ChoiceValueKey == "selected_run_speed_multiplier");
            Assert(feature.Choices!.SequenceEqual(new FeatureChoice[] { new("3", "3×"), new("5", "5×"), new("10", "10×") }));
            Assert(AstroneerModule.ChoiceAction("player_run_speed") == "set_run_speed_multiplier");
            Assert(AstroneerModule.ChoiceAction("resource_collection_10x") == "set_collection_multiplier");
            Fails(() => AstroneerModule.ChoiceAction("invulnerability"));
            Assert(Parse(Status()).Status.Values["selected_run_speed_multiplier"] == "3");
        });
        Run("Astroneer speed status separates selected and effective rates", () => {
            foreach (var multiplier in new[] { 3, 5, 10 }) {
                var inactive = Parse(Status(selectedRunSpeed: multiplier)).Status;
                Assert(inactive.Values["selected_run_speed_multiplier"] == multiplier.ToString());
                Assert(inactive.Values["run_speed_multiplier"] == "1" && !inactive.Enabled["player_run_speed"]);
                var active = Parse(Status(selectedMultiplier: 100, collectionEnabled: true, selectedRunSpeed: multiplier, runSpeedEnabled: true, invulnerable: true)).Status;
                Assert(active.Values["run_speed_multiplier"] == multiplier.ToString() && active.Enabled["player_run_speed"]);
                Assert(active.Values["selected_run_speed_multiplier"] == multiplier.ToString());
                Assert(active.Values["collection_multiplier"] == "100" && active.Enabled["invulnerability"]);
            }
        });
        Run("Astroneer speed selection accepts exact 3 5 or 10 without toggling", () => {
            var current = Parse(Status()).Status;
            foreach (var multiplier in new[] { 3, 5, 10 }) {
                Assert(AstroneerModule.ChoiceMultiplier("player_run_speed", multiplier.ToString()) == multiplier);
                var command = SpeedCommand(current, current, multiplier);
                Assert(command.Contains("action=set_run_speed_multiplier\nenabled=0\n"));
                Assert(command.EndsWith("multiplier=" + multiplier + "\n", StringComparison.Ordinal));
            }
            foreach (int? multiplier in new int?[] { null, 0, 1, 2, 4, 6, 9, 11, 20, 100, -3, int.MaxValue })
                Fails(() => SpeedCommand(current, current, multiplier));
            foreach (var choice in new[] { "", "03", "+3", "3.0", " 3", "5 ", "1e1", "010", "20", "100", "3\naction=add_bytes" })
                Fails(() => AstroneerModule.ChoiceMultiplier("player_run_speed", choice));
            foreach (var multiplier in new[] { 3, 5 }) {
                Fails(() => ChoiceCommand(current, current, multiplier));
                Fails(() => AstroneerModule.ChoiceMultiplier("resource_collection_10x", multiplier.ToString()));
            }
        });
        Run("Astroneer new controls require their own availability and current session", () => {
            var current = Parse(Status()).Status;
            var collectionOnly = current with { Values = new Dictionary<string, string> {
                ["available_resource_collection_10x"] = "True", ["available_set_run_speed_multiplier"] = "True" } };
            Fails(() => SpeedCommand(collectionOnly, collectionOnly, 3));
            foreach (var multiplier in new[] { 3, 5, 10 }) {
                Fails(() => SpeedCommand(current, current with { SessionId = "previous-session" }, multiplier));
                Fails(() => SpeedCommand(current with { Connected = false }, current, multiplier));
                Fails(() => SpeedCommand(current with { Ready = false }, current, multiplier));
                foreach (var enabled in new[] { true, false })
                    Fails(() => AstroneerModule.FormatCommand(current, current, "set_run_speed_multiplier", enabled, "6c3962171e544382a06f94038a2b83df", 1234567890, multiplier));
            }
            foreach (var feature in new[] { "invulnerability", "player_run_speed" }) {
                Fails(() => Command(collectionOnly, collectionOnly, feature, true));
                Fails(() => Command(current, current, feature, null));
                Fails(() => Command(current with { Ready = false }, current, feature, true));
                Fails(() => Command(current, current with { SessionId = "previous-session" }, feature, true));
                foreach (var enabled in new[] { true, false }) {
                    var command = Command(current, current, feature, enabled);
                    Assert(command.Contains("action=" + feature + "\nenabled=" + (enabled ? "1" : "0") + "\n"));
                    Assert(!command.Contains("multiplier="));
                }
            }
        });
        Console.WriteLine($"Astroneer tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static readonly (string Hash, string Version, string Build)[] InspectedProfiles = {
        ("E1764F108E51BB172148F3A4AC71AE38AC031D9ECEB41AD213E139A9CBA61F56", "1.43.2.0", "24409322"),
        ("D4FA5EA711A12F3FE87E38F7D385AE4328B2EFC7D8C8B9B0BF495C20CAC54B27", "1.44.4", "25119744"),
        ("4A57B4B1AC64DEFA09EA6D6C987D5E513D79243A2B33AB05CBA099AD1E377675", "1.45.1", "25599237")
    };
    private static JsonElement Status(int? pid = 42, string version = "0.3.1", int schema = 1, string session = "session-123456", string lastCommand = "", bool success = false, string message = "", int selectedMultiplier = 10, bool collectionEnabled = false, int selectedRunSpeed = 3, bool runSpeedEnabled = false, bool invulnerable = false) => JsonSerializer.SerializeToElement(new {
        schema_version = schema, version, process_id = pid, session_id = session, ready = true, message = "Ready",
        enabled = new { resource_collection_10x = collectionEnabled, player_run_speed = runSpeedEnabled, invulnerability = invulnerable },
        values = new { bytes = 500, selected_collection_multiplier = selectedMultiplier, collection_multiplier = collectionEnabled ? selectedMultiplier : 1,
            selected_run_speed_multiplier = selectedRunSpeed, run_speed_multiplier = runSpeedEnabled ? selectedRunSpeed : 1 },
        available = new { resource_collection_10x = true, unlimited_oxygen = true, unlimited_power = true, add_bytes = true, invulnerability = true, player_run_speed = true },
        last_command = lastCommand, last_success = success, last_message = message
    });
    private static AstroneerModule.Snapshot Parse(JsonElement json) { var now = DateTime.UtcNow; return AstroneerModule.ParseSnapshot(json, now, now.AddMinutes(-1), 42, now); }
    private static string Command(TrainerStatus current, TrainerStatus requested, string action, bool? enabled) => AstroneerModule.FormatCommand(current, requested, action, enabled, "6c3962171e544382a06f94038a2b83df", 1234567890);
    private static string ChoiceCommand(TrainerStatus current, TrainerStatus requested, int? multiplier) => AstroneerModule.FormatCommand(current, requested, "set_collection_multiplier", null, "6c3962171e544382a06f94038a2b83df", 1234567890, multiplier);
    private static string SpeedCommand(TrainerStatus current, TrainerStatus requested, int? multiplier) => AstroneerModule.FormatCommand(current, requested, "set_run_speed_multiplier", null, "6c3962171e544382a06f94038a2b83df", 1234567890, multiplier);
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
