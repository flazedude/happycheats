using System.Text.Json;
using System.Text.Json.Nodes;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class ProjectHospitalTests
{
    private static int passed, failed;
    private static readonly string[] Actions = { "max_money", "freeze_time", "max_staff", "unlock_campaigns", "max_prestige" };

    public static int RunAll()
    {
        Run("Project Hospital catalog has all five requested controls and staff hiring limit", () => {
            var module = ModuleCatalog.Create().Single(module => module.Definition.Id == "project-hospital");
            Check(module.Definition.AppId == "868360" && module is ProjectHospitalModule);
            Check(module.Definition.Features.Select(feature => feature.Id).SequenceEqual(Actions));
            Check(module.Definition.Features.Select(feature => feature.Name).SequenceEqual(new[] { "Max Money", "Freeze Time of Day", "Max Staff", "Unlock All Campaigns", "Max Prestige" }));
            Check(!module.Definition.Features[0].IsToggle && module.Definition.Features.Skip(1).All(feature => feature.IsToggle));
            Check(module.Definition.Features[2].Description.Contains("hiring limit to 1,000"));
            Check(module.Definition.Features.Select(feature => feature.Hotkey).SequenceEqual(new[] { "F1", "F2", "F3", "F4", "F5" }));
        });
        Run("Project Hospital heartbeat binds the schema version process and inspected assembly", () => {
            var good = ProjectHospitalModule.ParseStatus(Json(State()), 42);
            Check(good.Connected && good.Ready && good.SessionId == "session-a" && good.Enabled["freeze_time"]);
            Check(good.Values["hospital_staff_limit"] == "1000");
            foreach (var key in new[] { "schema_version", "version", "process_id", "assembly_sha256", "session_id" }) {
                var state = State(); state.Remove(key);
                Check(!ProjectHospitalModule.ParseStatus(Json(state), 42).Connected);
            }
            var changed = State(); changed["assembly_sha256"] = new string('A', 64);
            Check(!ProjectHospitalModule.ParseStatus(Json(changed), 42).Connected);
            Check(!ProjectHospitalModule.ParseStatus(Json(State()), 43).Connected);
            changed = State(); changed["ready"] = false;
            Check(ProjectHospitalModule.ParseStatus(Json(changed), 42) is { Connected: true, Ready: false });
        });
        Run("Project Hospital rejects stale previous-process and future heartbeats", () => {
            var now = DateTime.UtcNow;
            Check(ProjectHospitalModule.IsFreshHeartbeat(now.AddSeconds(-2), now.AddMinutes(-1), now));
            Check(!ProjectHospitalModule.IsFreshHeartbeat(now.AddSeconds(-7), now.AddMinutes(-1), now));
            Check(!ProjectHospitalModule.IsFreshHeartbeat(now.AddSeconds(-2), now.AddSeconds(-1), now));
            Check(!ProjectHospitalModule.IsFreshHeartbeat(now.AddSeconds(10), now.AddMinutes(-1), now));
        });
        Run("Project Hospital commands use current session explicit toggle state and unique request", () => {
            var status = ProjectHospitalModule.ParseStatus(Json(State()), 42);
            foreach (var action in Actions) {
                var command = JsonSerializer.SerializeToElement(ProjectHospitalModule.CreateCommand(status, status, "request-a", action, action == "max_money" ? null : true, 12345));
                Check(command.GetProperty("schema_version").GetInt32() == 1 && Text(command, "action") == action);
                Check(Text(command, "session_id") == "session-a" && Text(command, "request_id") == "request-a");
                Check(command.GetProperty("created_utc").GetInt64() == 12345);
                if (action != "max_money") Check(Bool(command, "enabled"));
            }
            Reject(() => ProjectHospitalModule.CreateCommand(status, status, "r", "unknown", true, 0));
            foreach (var toggle in Actions.Skip(1)) Reject(() => ProjectHospitalModule.CreateCommand(status, status, "r", toggle, null, 0));
            foreach (var action in new[] { "max_money", "disable_all" }) Reject(() => ProjectHospitalModule.CreateCommand(status, status, "r", action, true, 0));
            Reject(() => ProjectHospitalModule.CreateCommand(status, status with { SessionId = "new-session" }, "r", "max_money", null, 0));
            Reject(() => ProjectHospitalModule.CreateCommand(status, TrainerStatus.Offline(), "r", "disable_all", null, 0));
        });
        Run("Project Hospital menu permits campaigns and blocks hospital-only actions", () => {
            var state = State();
            foreach (var action in Actions.Where(action => action != "unlock_campaigns")) state["values"]!["available_" + action] = false;
            var status = ProjectHospitalModule.ParseStatus(Json(state), 42);
            ProjectHospitalModule.CreateCommand(status, status, "r", "unlock_campaigns", true, 0);
            foreach (var action in Actions.Where(action => action != "unlock_campaigns"))
                Reject(() => ProjectHospitalModule.CreateCommand(status, status, "r", action, action == "max_money" ? null : true, 0));
            foreach (var action in Actions.Skip(1)) ProjectHospitalModule.CreateCommand(status, status, "r", action, false, 0);
            state["values"] = new JsonObject(); status = ProjectHospitalModule.ParseStatus(Json(state), 42);
            Reject(() => ProjectHospitalModule.CreateCommand(status, status, "r", "max_money", null, 0));
            ProjectHospitalModule.CreateCommand(status, status with { Ready = false }, "r", "disable_all", null, 0);
        });
        Run("Project Hospital acknowledgements require matching identities and success", () => {
            var response = new JsonObject { ["schema_version"] = 1, ["session_id"] = "session-a", ["request_id"] = "request-a", ["success"] = true };
            ProjectHospitalModule.AcceptResponse(Json(response), "session-a", "request-a");
            Reject(() => ProjectHospitalModule.AcceptResponse(Json(response), "session-b", "request-a"));
            Reject(() => ProjectHospitalModule.AcceptResponse(Json(response), "session-a", "request-b"));
            response["success"] = false; response["message"] = "No loaded hospital";
            Check(Reject(() => ProjectHospitalModule.AcceptResponse(Json(response), "session-a", "request-a")).Message == "No loaded hospital");
            response["success"] = true; response["schema_version"] = 2;
            Reject(() => ProjectHospitalModule.AcceptResponse(Json(response), "session-a", "request-a"));
        });
        Run("Project Hospital rejects uninspected game assemblies before installer writes", () => {
            using var f = new Fixture();
            File.WriteAllText(Path.Combine(f.Game, ProjectHospitalModule.Executable), "fake executable");
            var assembly = SafePath(f.Game, ProjectHospitalModule.GameAssembly);
            Directory.CreateDirectory(Path.GetDirectoryName(assembly)!); File.WriteAllText(assembly, "unsupported");
            var module = new ProjectHospitalModule(f.Root);
            var installation = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Check(installation.IsInstalled && !installation.Compatible && !installation.TrainerInstalled);
            Reject(() => module.InstallAsync(installation).GetAwaiter().GetResult());
            Check(!File.Exists(Path.Combine(f.Game, "winhttp.dll")));
        });
        Run("Project Hospital verified package installs and plugin removal preserves shared loader", () => {
            using var f = new Fixture(); ProjectHospitalInstaller.Install(f.Package, f.Game, () => { });
            Check(ProjectHospitalInstaller.Installed(f.Game));
            ProjectHospitalInstaller.Remove(f.Game, () => { });
            Check(!ProjectHospitalInstaller.Installed(f.Game) && File.Exists(Path.Combine(f.Game, "winhttp.dll")));
        });
        Run("Project Hospital rejects corrupt missing and wrong-architecture payloads", () => {
            using var f = new Fixture();
            File.AppendAllText(SafePath(f.Package, ProjectHospitalInstaller.Plugin), "changed");
            Reject(() => ProjectHospitalInstaller.Install(f.Package, f.Game, () => { }));
            Check(!Directory.EnumerateFiles(f.Game, "*", SearchOption.AllDirectories).Any());
            f.Manifest(architecture: "win-x86"); Reject(() => ProjectHospitalInstaller.ReadPackage(f.Package));
            f.Manifest(ProjectHospitalInstaller.RequiredFiles.Where(path => path != "BepInEx/core/BepInEx.Preloader.dll"));
            Reject(() => ProjectHospitalInstaller.ReadPackage(f.Package));
        });
        Run("Project Hospital foreign files are preserved and never adopted", () => {
            using var f = new Fixture();
            File.WriteAllText(Path.Combine(f.Game, "winhttp.dll"), "foreign");
            Reject(() => ProjectHospitalInstaller.Install(f.Package, f.Game, () => { }));
            Check(File.ReadAllText(Path.Combine(f.Game, "winhttp.dll")) == "foreign");
            Check(!File.Exists(SafePath(f.Game, ProjectHospitalInstaller.Plugin)));
            File.Copy(Path.Combine(f.Package, "winhttp.dll"), Path.Combine(f.Game, "winhttp.dll"), true);
            var plugin = SafePath(f.Game, ProjectHospitalInstaller.Plugin);
            Directory.CreateDirectory(Path.GetDirectoryName(plugin)!); File.Copy(SafePath(f.Package, ProjectHospitalInstaller.Plugin), plugin);
            ProjectHospitalInstaller.Install(f.Package, f.Game, () => { });
            ProjectHospitalInstaller.Remove(f.Game, () => { });
            Check(File.Exists(plugin));
        });
        Run("Project Hospital interrupted install rolls back and modified trainer survives removal", () => {
            using var f = new Fixture(); var checks = 0;
            Reject(() => ProjectHospitalInstaller.Install(f.Package, f.Game, () => { if (++checks == 4) throw new IOException("Game started"); }));
            Check(!Directory.EnumerateFiles(f.Game, "*", SearchOption.AllDirectories).Any());
            ProjectHospitalInstaller.Install(f.Package, f.Game, () => { });
            File.AppendAllText(SafePath(f.Game, ProjectHospitalInstaller.Plugin), "user change");
            Reject(() => ProjectHospitalInstaller.Remove(f.Game, () => { }));
            Check(File.ReadAllText(SafePath(f.Game, ProjectHospitalInstaller.Plugin)).EndsWith("user change"));
        });
        Run("Project Hospital package path traversal and duplicate entries are rejected", () => {
            using var f = new Fixture();
            f.Manifest(ProjectHospitalInstaller.RequiredFiles.Concat(new[] { "BepInEx/core/../../outside.dll" }));
            Reject(() => ProjectHospitalInstaller.ReadPackage(f.Package));
            f.Manifest(ProjectHospitalInstaller.RequiredFiles.Concat(new[] { ProjectHospitalInstaller.Plugin }));
            Reject(() => ProjectHospitalInstaller.ReadPackage(f.Package));
        });
        Run("Project Hospital rejects unused framework-dependent JSON payload", () => {
            using var f = new Fixture();
            f.Manifest(ProjectHospitalInstaller.RequiredFiles.Concat(new[] { "BepInEx/plugins/HappyCheats.ProjectHospital/Newtonsoft.Json.dll" }));
            Reject(() => ProjectHospitalInstaller.ReadPackage(f.Package));
        });
        Console.WriteLine($"Project Hospital module tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static JsonObject State()
    {
        var values = new JsonObject { ["hospital_staff_limit"] = "1000" };
        foreach (var action in Actions) values["available_" + action] = true;
        return new JsonObject { ["schema_version"] = 1, ["version"] = ProjectHospitalModule.TrainerVersion,
            ["process_id"] = 42, ["assembly_sha256"] = ProjectHospitalModule.SupportedAssemblyHash,
            ["session_id"] = "session-a", ["ready"] = true,
            ["enabled"] = new JsonObject { ["freeze_time"] = true }, ["values"] = values };
    }
    private static JsonElement Json(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static Exception Reject(Action action) { try { action(); } catch (Exception ex) { return ex; } throw new Exception("Expected rejection."); }
    private static void Run(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); } }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-hospital-tests-" + Guid.NewGuid().ToString("N"));
        public string Package => Path.Combine(Root, "payload", "project-hospital");
        public string Game => Path.Combine(Root, "game");
        public Fixture()
        {
            Directory.CreateDirectory(Game);
            foreach (var path in ProjectHospitalInstaller.RequiredFiles) {
                var full = SafePath(Package, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, "test payload " + path);
            }
            Manifest();
        }
        public void Manifest(IEnumerable<string>? files = null, string architecture = "win-x64") => WriteJson(Path.Combine(Package, "package.json"), new {
            schema_version = 1, game_id = "project-hospital", steam_app_id = 868360, version = ProjectHospitalModule.TrainerVersion,
            game_assembly_sha256 = ProjectHospitalModule.SupportedAssemblyHash, loader = new { architecture },
            files = (files ?? ProjectHospitalInstaller.RequiredFiles).Select(path => new { path, sha256 = File.Exists(Path.Combine(Package, path)) ? Hash(Path.Combine(Package, path)) : new string('A', 64) })
        });
        public void Dispose()
        {
            if (Path.GetFullPath(Root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(Root).StartsWith("HappyCheats-hospital-tests-")) Directory.Delete(Root, true);
        }
    }
}
