using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using HappyCheats.Core;

namespace HappyCheats.Tests;

internal static class Civ7ModuleTests
{
    private const string Session = "0123456789abcdef0123456789abcdef";
    private static int passed, failed;
    public static int RunAll()
    {
        passed = failed = 0;
        Run("Steam identity and complete actionable feature contract", () => {
            var game = new CivilizationVIIModule(".").Definition;
            Assert(game.Id == "civilization-vii" && game.AppId == "1295660");
            Assert(game.Features.Count == 15 && game.Features.Count(f => f.IsToggle) == 6);
            Assert(game.Features.Count(f => f.Number != null) == 6);
            Assert(game.Features.Select(f => f.Hotkey).Distinct().Count() == 15);
            Assert(game.Features.All(f => f.Id.StartsWith("civ7_") && !f.Description.StartsWith("Unavailable")));
        });
        Run("Numeric input rejects fractional, out-of-range and injection payloads", () => {
            var previousCulture = CultureInfo.CurrentCulture;
            try {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                foreach (var f in new CivilizationVIIModule(".").Definition.Features.Where(f => f.Number != null)) {
                    var n = f.Number!;
                    CivilizationVIIModule.ValidateValue(f.Id, n.Minimum);
                    CivilizationVIIModule.ValidateValue(f.Id, n.Maximum);
                    Fails(() => CivilizationVIIModule.ValidateValue(f.Id, n.Minimum - 1));
                    Fails(() => CivilizationVIIModule.ValidateValue(f.Id, n.Maximum + 1));
                    Fails(() => CivilizationVIIModule.ValidateValue(f.Id, n.Minimum + 0.5m));
                }
                Assert(CivilizationVIIModule.ValidateCommandInput("civ7_gold", null, 12345) == "12345");
                Fails(() => CivilizationVIIModule.ValidateCommandInput("civ7_gold');alert(1);//", null, 1));
            } finally { CultureInfo.CurrentCulture = previousCulture; }
        });
        Run("Actions, toggles and editors enforce distinct input types", () => {
            foreach (var id in new[] { "civ7_restore_moves", "civ7_growth", "civ7_golden_age", "disable_all" }) {
                Assert(CivilizationVIIModule.ValidateCommandInput(id, null, null) == "null");
                Fails(() => CivilizationVIIModule.ValidateCommandInput(id, true, null));
                Fails(() => CivilizationVIIModule.ValidateCommandInput(id, null, 1));
            }
            Assert(CivilizationVIIModule.ValidateCommandInput("civ7_movement", true, null) == "true");
            Assert(CivilizationVIIModule.ValidateCommandInput("civ7_movement", false, null) == "false");
            Fails(() => CivilizationVIIModule.ValidateCommandInput("civ7_movement", null, null));
            Fails(() => CivilizationVIIModule.ValidateCommandInput("civ7_gold", null, null));
            Fails(() => CivilizationVIIModule.ValidateCommandInput("civ7_gold", true, 1));
        });
        Run("Status preserves availability and refuses malformed protocol", () => {
            var good = Status();
            good["values"]!["civ7_gold"] = "12345";
            var parsed = CivilizationVIIModule.ParseStatus(good.ToJsonString());
            Assert(parsed.Connected && parsed.Ready && parsed.Values["civ7_gold"] == "12345");
            foreach (var key in new[] { "schema_version", "version", "ready", "session_id", "enabled", "values", "command_success", "last_command" }) {
                var bad = Status(); bad.Remove(key);
                Fails(() => CivilizationVIIModule.ParseStatus(bad.ToJsonString()));
            }
            foreach (var json in new[] { "[]", "null", "{}", "{", new string(' ', 65537) }) Fails(() => CivilizationVIIModule.ParseStatus(json));
            var wrongVersion = Status(); wrongVersion["version"] = "0.0.0";
            Fails(() => CivilizationVIIModule.ParseStatus(wrongVersion.ToJsonString()));
            var wrongAvailability = Status(); wrongAvailability["values"]!["available_civ7_gold"] = true;
            Fails(() => CivilizationVIIModule.ParseStatus(wrongAvailability.ToJsonString()));
            var extraToggle = Status(); extraToggle["enabled"]!["civ7_unknown"] = true;
            Fails(() => CivilizationVIIModule.ParseStatus(extraToggle.ToJsonString()));
        });
        Run("Stale session and unavailable edits fail; cleanup works while not ready", () => {
            var good = CivilizationVIIModule.ParseStatus(Status().ToJsonString());
            var waiting = good with { Ready = false };
            CivilizationVIIModule.ValidateCommandState(waiting, good, "disable_all", null);
            CivilizationVIIModule.ValidateCommandState(waiting, good, "civ7_movement", false);
            Fails(() => CivilizationVIIModule.ValidateCommandState(waiting, good, "civ7_gold", null));
            Fails(() => CivilizationVIIModule.ValidateCommandState(good, good with { SessionId = Guid.NewGuid().ToString("N") }, "civ7_gold", null));
            var unavailable = Status(); unavailable["values"]!["available_civ7_gold"] = "false";
            Fails(() => CivilizationVIIModule.ValidateCommandState(CivilizationVIIModule.ParseStatus(unavailable.ToJsonString()), good, "civ7_gold", null));
            Fails(() => CivilizationVIIModule.ValidateCommandState(TrainerStatus.Offline(), good, "disable_all", null));
            var changedSelection = Status(); changedSelection["values"]!["selected_unit_id"] = "8";
            Fails(() => CivilizationVIIModule.ValidateCommandState(CivilizationVIIModule.ParseStatus(changedSelection.ToJsonString()), good, "civ7_xp", null));
            CivilizationVIIModule.ValidateCommandState(CivilizationVIIModule.ParseStatus(changedSelection.ToJsonString()), good, "civ7_gold", null);
            var changedAge = Status(); changedAge["values"]!["age"] = "2";
            Fails(() => CivilizationVIIModule.ValidateCommandState(CivilizationVIIModule.ParseStatus(changedAge.ToJsonString()), good, "civ7_gold", null));
        });
        Run("Steam root and both executable folder variants normalize correctly", () => {
            var root = Path.Combine(Path.GetTempPath(), "hc-civ7-module-" + Guid.NewGuid().ToString("N"));
            var binary = Path.Combine(root, "Base", "Binaries", "Win64");
            Directory.CreateDirectory(binary);
            try {
                foreach (var name in new[] { "Civ7_Win64_DX12_FinalRelease.exe", "Civ7_Win64_Vulkan_FinalRelease.exe" }) {
                    var file = Path.Combine(binary, name); File.WriteAllText(file, "fixture only");
                    Assert(CivilizationVIIModule.NormalizeGameDirectory(binary) == root);
                    Assert(CivilizationVIIModule.NormalizeGameDirectory(root) == root);
                    File.Delete(file);
                }
            } finally { Directory.Delete(root, true); }
        });
        Console.WriteLine($"Civ7 module tests: {passed} passed, {failed} failed.");
        return failed;
    }
    private static JsonObject Status()
    {
        var enabled = new JsonObject(); var values = new JsonObject {
            ["player_id"] = "0", ["age"] = "1", ["selected_unit_id"] = "7", ["selected_city_id"] = "3"
        };
        foreach (var f in new CivilizationVIIModule(".").Definition.Features) {
            if (f.IsToggle) enabled[f.Id] = false;
            values["available_" + f.Id] = "true";
        }
        return new JsonObject {
            ["schema_version"] = 1, ["version"] = "0.1.0", ["session_id"] = Session,
            ["ready"] = true, ["message"] = "Ready", ["last_command"] = "", ["command_success"] = true,
            ["command_error"] = "", ["enabled"] = enabled, ["values"] = values
        };
    }
    private static void Run(string name, Action check) { try { check(); passed++; Console.WriteLine("PASS Civ7 " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL Civ7 " + name + ": " + e); } }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action check) { try { check(); } catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or JsonException) { return; } throw new Exception("Expected rejection."); }
}
