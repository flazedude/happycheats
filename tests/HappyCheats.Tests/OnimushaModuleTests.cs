using System.Text.Json;
using System.Text.Json.Nodes;
using HappyCheats.Core;

namespace HappyCheats.Tests;

internal static class OnimushaModuleTests
{
    private static readonly OnimushaModule Module = new(Path.GetTempPath());
    private static readonly string[] CombatIds = { "unlimited_health", "auto_parry", "one_hit_stun", "stun_immunity" };
    private static int passed, failed;

    public static int RunAll()
    {
        Run("Onimusha exposes exactly four combat toggles on F1-F4", () => {
            var features = Module.Definition.Features;
            Check(features.Count == CombatIds.Length);
            Check(features.Select(f => f.Id).SequenceEqual(CombatIds));
            for (var i = 0; i < CombatIds.Length; i++)
                Check(features[i].IsToggle && features[i].Hotkey == "F" + (i + 1) && features[i].Group == "Combat");
        });
        Run("Onimusha 0.4 heartbeat retains health red bar and enabled state", () => {
            var status = OnimushaModule.ParseStatus(Json(State()));
            Check(status.Connected && status.Ready && status.Enabled["unlimited_health"]);
            Check(status.Values.Count == 2 && status.Values["Health"] == "836 / 1320" && status.Values["Red bar"] == "40 / 100");
        });
        Run("Onimusha four toggle commands retain identity and explicit enable state", () => {
            foreach (var id in CombatIds)
            foreach (var enabled in new[] { true, false }) {
                var command = Command(State(), id, enabled);
                Check(command.EnumerateObject().Count() == 3);
                Check(command.GetProperty("session_id").GetString() == "session-a");
                Check(command.GetProperty("request_id").GetString() == "request-a");
                Check(command.GetProperty("enabled").GetProperty(id).GetBoolean() == enabled);
            }
        });
        Run("removed Onimusha teleport requests remain unknown even with stale runtime fields", () => {
            var state = State();
            state["teleport"] = new JsonObject { ["ready"] = true, ["reason"] = "Old readiness", ["target_label"] = "Old target" };
            state["features"]!["teleport_to_pin"] = new JsonObject { ["installed"] = true };
            var error = Reject(() => Command(state, "teleport_to_pin"));
            Check(error is ArgumentException && error.Message == "Unknown trainer control.");
            Check(Reject(() => Command(state, "unknown_action")) is ArgumentException);
            var status = OnimushaModule.ParseStatus(Json(state));
            Check(status.Values.Count == 2 && !status.Values.Keys.Any(k => k.Contains("teleport", StringComparison.OrdinalIgnoreCase)));
        });
        Run("player loading rejects existing trainer commands", () => {
            var state = State(); state["player"]!["ready"] = false;
            var status = OnimushaModule.ParseStatus(Json(state));
            Check(status.Connected && !status.Ready && status.Values.Count == 0);
            foreach (var id in CombatIds.Concat(new[] { "disable_all", "hud" }))
                Reject(() => Command(state, id, true));
        });
        Run("changed sessions and unshipped heartbeat versions reject commands", () => {
            var changed = State(); changed["session_id"] = "session-b";
            Reject(() => Command(changed, "unlimited_health", true));
            foreach (var version in new[] { "0.5.0", "0.6.0", "" }) {
                var unknown = State(); unknown["version"] = version;
                Check(!OnimushaModule.ParseStatus(Json(unknown)).Connected);
                Reject(() => Command(unknown, "unlimited_health", true));
            }
        });
        Run("invalid schema and missing session stay disconnected", () => {
            var state = State(); state["schema_version"] = 999;
            Check(!OnimushaModule.ParseStatus(Json(state)).Connected);
            state = State(); state.Remove("session_id");
            Check(!OnimushaModule.ParseStatus(Json(state)).Connected);
        });
        Run("missing and faulted combat bindings still reject toggles", () => {
            foreach (var id in CombatIds) {
                var state = State(); state["features"]![id]!["fault"] = "Binding lost";
                Reject(() => Command(state, id, true));
                state["features"]![id] = new JsonObject { ["installed"] = false };
                Reject(() => Command(state, id, true));
            }
        });
        Run("disable-all and HUD retain their mailbox contract without combat bindings", () => {
            var state = State(); state.Remove("features");
            var off = Command(state, "disable_all");
            Check(off.EnumerateObject().Count() == 3 && off.GetProperty("disable_all").GetBoolean());
            var hud = Command(state, "hud");
            Check(hud.EnumerateObject().Count() == 3 && !hud.GetProperty("hud_visible").GetBoolean());
            Check(Command(state, "hud", true).GetProperty("hud_visible").GetBoolean());
        });
        Run("legacy acknowledgement requires the current request and ignores unrelated result fields", () => {
            var state = State(); state["last_command"] = "previous-request";
            Check(!Acknowledged(state));
            state["last_command"] = "request-a";
            Check(Acknowledged(state));
            state["command_result"] = new JsonObject { ["request_id"] = "request-a", ["success"] = false, ["message"] = "Unused action result" };
            Check(Acknowledged(state));
        });
        Run("publication gaps cannot acknowledge or resend a command", () => {
            Check(!OnimushaModule.AcceptAcknowledgement(TrainerStatus.Offline(), "session-a", "request-a"));
            var state = State(); state["last_command"] = "request-a"; state["schema_version"] = 999;
            Check(!Acknowledged(state));
        });
        Run("acknowledgement rejects changed sessions and accepts completed requests during loading", () => {
            var state = State(); state["last_command"] = "request-a"; state["session_id"] = "new-session";
            try { Acknowledged(state); throw new Exception("Expected a changed-session rejection."); }
            catch (IOException ex) { Check(ex.Message == "Game session changed."); }
            state["session_id"] = "session-a"; state["player"]!["ready"] = false;
            Check(Acknowledged(state));
        });
        Console.WriteLine($"Onimusha module tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static JsonObject State() => new() {
        ["schema_version"] = 1, ["version"] = "0.4.0", ["session_id"] = "session-a",
        ["player"] = new JsonObject { ["ready"] = true, ["health"] = 836, ["maximum_health"] = 1320,
            ["rikido"] = new JsonObject { ["current"] = 40, ["maximum"] = 100 } },
        ["hud"] = new JsonObject { ["visible"] = true },
        ["enabled"] = new JsonObject { ["unlimited_health"] = true },
        ["features"] = new JsonObject {
            ["unlimited_health"] = new JsonObject { ["installed"] = true },
            ["auto_parry"] = new JsonObject { ["installed"] = true },
            ["one_hit_stun"] = new JsonObject { ["installed"] = true },
            ["stun_immunity"] = new JsonObject { ["installed"] = true }
        }
    };
    private static JsonElement Json(JsonObject state) => JsonSerializer.SerializeToElement(state);
    private static bool Acknowledged(JsonObject state) =>
        OnimushaModule.AcceptAcknowledgement(OnimushaModule.ParseStatus(Json(state)), "session-a", "request-a");
    private static JsonElement Command(JsonObject state, string id, bool? enabled = null) =>
        JsonSerializer.SerializeToElement(Module.CreateCommand(Json(state), "session-a", id, enabled, "request-a"));
    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static Exception Reject(Action action) {
        try { action(); } catch (InvalidOperationException ex) { return ex; } catch (ArgumentException ex) { return ex; }
        throw new Exception("Expected command rejection.");
    }
    private static void Run(string name, Action action) {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
    }
}
