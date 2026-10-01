using System.Text.Json;
using System.Text.Json.Nodes;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using HappyCheats.Core;
using HappyCheats.Core.CivilizationVI;

namespace HappyCheats.Tests;

internal static class Civ6ModuleTests
{
    private static int passed, failed;
    private const string Session = "0123456789abcdef0123456789abcdef";
    private static readonly string[] Toggles = ["civ6_movement", "civ6_units", "civ6_buildings", "civ6_districts", "civ6_projects", "civ6_research", "civ6_civics"];
    private static readonly string[] Editors = ["civ6_damage", "civ6_moves", "civ6_can_level", "civ6_xp", "civ6_level", "civ6_gold", "civ6_faith", "civ6_influence", "civ6_favor", "civ6_population", "civ6_amenities", "civ6_housing"];
    private static readonly string[] Unsupported = ["civ6_level", "civ6_influence", "civ6_amenities", "civ6_housing"];

    public static int RunAll()
    {
        passed = failed = 0;
        Run("Civ6 matches the 19 screenshot controls and identifies four unavailable editors", () => {
            var definition = new CivilizationVIModule(".").Definition;
            Assert(definition.Id == "civilization-vi" && definition.AppId == "289070");
            Assert(definition.Features.Count == 19);
            Assert(definition.Features.Select(feature => feature.Id).Distinct().Count() == 19);
            Assert(definition.Features.Where(feature => feature.IsToggle).Select(feature => feature.Id).SequenceEqual(Toggles));
            Assert(definition.Features.Where(feature => feature.Number != null).Select(feature => feature.Id).SequenceEqual(Editors));
            Assert(definition.Features.Where(feature => feature.Description.StartsWith("Unavailable:")).Select(feature => feature.Id).SequenceEqual(Unsupported));
            Assert(definition.Features.Where(feature => !Unsupported.Contains(feature.Id)).Select(feature => feature.Hotkey)
                .SequenceEqual(new[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "Shift+F1", "Shift+F2", "Shift+F3", "Shift+F4" }));
            Assert(definition.Features.Where(feature => Unsupported.Contains(feature.Id)).All(feature => feature.Hotkey == ""));
            Assert(definition.Features.Where(feature => feature.Number != null).All(feature => feature.ChoiceValueKey == feature.Id));
            Assert(definition.Description.Contains("15 controls implemented") && definition.Description.Contains("Gameplay validation is pending"));
        });
        Run("Civ6 numeric editors accept integer limits and reject fractional or out-of-range values", () => {
            foreach (var feature in new CivilizationVIModule(".").Definition.Features.Where(feature => feature.Number != null)) {
                var range = feature.Number!;
                CivilizationVIModule.ValidateValue(feature.Id, range.Minimum);
                CivilizationVIModule.ValidateValue(feature.Id, range.Maximum);
                CivilizationVIModule.ValidateValue(feature.Id, range.DefaultValue);
                Fails(() => CivilizationVIModule.ValidateValue(feature.Id, range.Minimum - 1));
                Fails(() => CivilizationVIModule.ValidateValue(feature.Id, range.Maximum + 1));
                Fails(() => CivilizationVIModule.ValidateValue(feature.Id, range.Minimum + 0.5m));
            }
            Fails(() => CivilizationVIModule.ValidateValue("civ6_movement", 1));
            Fails(() => CivilizationVIModule.ValidateValue("disable_all", 1));
            Fails(() => CivilizationVIModule.ValidateValue("unknown", 1));
        });
        Run("Civ6 rejects arbitrary feature strings and invalid editor dispatch before game access", () => {
            var module = new CivilizationVIModule(".");
            var installation = new GameInstallation("not a game", "", false, false, false, false, "fixture");
            foreach (var id in new[] { "civ6_gold');Mutation();--", "civ6_movement');Mutation();--", "unknown", "CIV6_GOLD", "" }) {
                Fails(() => module.SetValueAsync(installation, TrainerStatus.Offline(), id, 1).GetAwaiter().GetResult());
                Fails(() => module.SendAsync(installation, TrainerStatus.Offline(), id, true).GetAwaiter().GetResult());
            }
            Fails(() => module.SendAsync(installation, TrainerStatus.Offline(), "civ6_gold", true).GetAwaiter().GetResult());
            Fails(() => module.SetValueAsync(installation, TrainerStatus.Offline(), "civ6_movement", 1).GetAwaiter().GetResult());
        });
        Run("Civ6 status preserves game-confirmed state and per-control availability", () => {
            var data = Status();
            data["enabled"]!["civ6_research"] = true;
            data["values"]!["civ6_gold"] = "12345";
            data["values"]!["selected_unit_id"] = "7";
            var parsed = CivilizationVIModule.ParseStatus(data.ToJsonString());
            Assert(parsed.Connected && parsed.Ready && parsed.SessionId == Session);
            Assert(parsed.Enabled["civ6_research"] && !parsed.Enabled["civ6_movement"]);
            Assert(parsed.Values["civ6_gold"] == "12345" && parsed.Values["selected_unit_id"] == "7");
            Assert(Unsupported.All(id => parsed.Values["available_" + id] == "false"));
            data["ready"] = false;
            data["message"] = "Waiting for the local player's active turn.";
            var waiting = CivilizationVIModule.ParseStatus(data.ToJsonString());
            Assert(waiting.Connected && !waiting.Ready && waiting.Message.Contains("active turn"));
        });
        Run("Civ6 status rejects missing, malformed and oversized session data", () => {
            foreach (var session in new[] { "", "new-session", "0123456789abcdef0123456789abcdef0", "';Mutation();--" }) {
                var data = Status(); data["session_id"] = session;
                Fails(() => CivilizationVIModule.ParseStatus(data.ToJsonString()));
            }
            var missing = Status(); missing.Remove("session_id");
            Fails(() => CivilizationVIModule.ParseStatus(missing.ToJsonString()));
            Fails(() => CivilizationVIModule.ParseStatus("[]"));
            Fails(() => CivilizationVIModule.ParseStatus("null"));
            Fails(() => CivilizationVIModule.ParseStatus("{broken"));
            Fails(() => CivilizationVIModule.ParseStatus(new string(' ', 65537)));
        });
        Run("Civ6 status rejects wrong runtime version and malformed readiness or feature state", () => {
            foreach (var version in new[] { "", "0.0.1", "0.2.0" }) {
                var data = Status(); data["version"] = version;
                Fails(() => CivilizationVIModule.ParseStatus(data.ToJsonString()));
            }
            foreach (var key in new[] { "schema_version", "version", "ready", "enabled", "values", "command_success", "last_command" }) {
                var data = Status(); data.Remove(key);
                Fails(() => CivilizationVIModule.ParseStatus(data.ToJsonString()));
            }
            var ready = Status(); ready["ready"] = "true";
            Fails(() => CivilizationVIModule.ParseStatus(ready.ToJsonString()));
            var toggle = Status(); toggle["enabled"]!["civ6_movement"] = "true";
            Fails(() => CivilizationVIModule.ParseStatus(toggle.ToJsonString()));
            var missingToggle = Status(); ((JsonObject)missingToggle["enabled"]!).Remove("civ6_movement");
            Fails(() => CivilizationVIModule.ParseStatus(missingToggle.ToJsonString()));
            var missingAvailability = Status(); ((JsonObject)missingAvailability["values"]!).Remove("available_civ6_gold");
            Fails(() => CivilizationVIModule.ParseStatus(missingAvailability.ToJsonString()));
            var invalidAvailability = Status(); invalidAvailability["values"]!["available_civ6_gold"] = "maybe";
            Fails(() => CivilizationVIModule.ParseStatus(invalidAvailability.ToJsonString()));
            var unsupported = Status(); unsupported["values"]!["available_civ6_level"] = "true";
            Fails(() => CivilizationVIModule.ParseStatus(unsupported.ToJsonString()));
            var badSchema = Status(); badSchema["schema_version"] = "1";
            Fails(() => CivilizationVIModule.ParseStatus(badSchema.ToJsonString()));
            var badRequest = Status(); badRequest["last_command"] = "old-request";
            Fails(() => CivilizationVIModule.ParseStatus(badRequest.ToJsonString()));
        });
        Run("Civ6 canonical selection parsing prevents Lua injection and orphaned selections", () => {
            Assert(CivilizationVIModule.ParseSelection("0|7|3|true") == "0,7,3,true");
            Assert(CivilizationVIModule.ParseSelection("+0|0007|-1|false") == "0,7,-1,false");
            Assert(CivilizationVIModule.ParseSelection("-1|7|3|true") == "-1,-1,-1,false");
            foreach (var invalid in new[] { "0|7", "0|7|3", "0|7|3|4", "0|7|3|TRUE", "0|7|3|true);Mutation()", "0|7);Mutation()|3|true", "0|7.1|3|true", "0|-2|3|true", "-2|7|3|true", "0|2147483648|3|true", "0| 7|3|true", "0|1e2|3|true" })
                Fails(() => CivilizationVIModule.ParseSelection(invalid));
        });
        Run("Civ6 accepts the installation root or executable folder without a Steam manifest", () => {
            var root = Path.Combine(Path.GetTempPath(), "HappyCheats-Civ6-Discovery-" + Guid.NewGuid().ToString("N"));
            var bin = Path.Combine(root, "Base", "Binaries", "Win64Steam");
            Directory.CreateDirectory(bin);
            try {
                File.WriteAllText(Path.Combine(bin, "CivilizationVI_DX12.exe"), "fixture only");
                Assert(CivilizationVIModule.NormalizeGameDirectory(root) == root);
                Assert(CivilizationVIModule.NormalizeGameDirectory(bin) == root);
            } finally { Directory.Delete(root, recursive: true); }
        });
        Run("Civ6 command cleanup remains possible between turns but stale sessions cannot act", () => {
            var current = CivilizationVIModule.ParseStatus(Status().ToJsonString());
            var waiting = current with { Ready = false };
            CivilizationVIModule.ValidateCommandState(waiting, current, "disable_all", null);
            CivilizationVIModule.ValidateCommandState(waiting, current, "civ6_movement", false);
            Fails(() => CivilizationVIModule.ValidateCommandState(waiting, current, "civ6_movement", true));
            Fails(() => CivilizationVIModule.ValidateCommandState(waiting, current, "civ6_gold", null));
            var old = current with { SessionId = "abcdef0123456789abcdef0123456789" };
            Fails(() => CivilizationVIModule.ValidateCommandState(current, old, "civ6_gold", null));
            Fails(() => CivilizationVIModule.ValidateCommandState(waiting, old, "disable_all", null));
            Fails(() => CivilizationVIModule.ValidateCommandState(TrainerStatus.Offline(), current, "disable_all", null));
            Fails(() => CivilizationVIModule.ValidateCommandState(current, current, "civ6_level", null));
            Fails(() => CivilizationVIModule.ValidateCommandState(current, current, "unknown", null));
            var unavailable = current with { Values = new Dictionary<string, string>() };
            Fails(() => CivilizationVIModule.ValidateCommandState(unavailable, current, "civ6_gold", null));
            CivilizationVIModule.ValidateCommandState(unavailable, current, "civ6_movement", false);
        });
        Run("Civ6 command input requires explicit toggle state and one correct value type", () => {
            Assert(CivilizationVIModule.ValidateCommandInput("disable_all", null, null) == "false");
            Assert(CivilizationVIModule.ValidateCommandInput("civ6_movement", true, null) == "true");
            Assert(CivilizationVIModule.ValidateCommandInput("civ6_movement", false, null) == "false");
            Assert(CivilizationVIModule.ValidateCommandInput("civ6_gold", null, 12345) == "12345");
            Fails(() => CivilizationVIModule.ValidateCommandInput("civ6_movement", null, null));
            Fails(() => CivilizationVIModule.ValidateCommandInput("civ6_movement", true, 1));
            Fails(() => CivilizationVIModule.ValidateCommandInput("civ6_gold", true, 1));
            Fails(() => CivilizationVIModule.ValidateCommandInput("civ6_gold", null, null));
            Fails(() => CivilizationVIModule.ValidateCommandInput("disable_all", true, null));
            Fails(() => CivilizationVIModule.ValidateCommandInput("disable_all", null, 1));
        });
        Run("Civ6 periodic failures remain visible while detailed values stay out of the footer", () => {
            var data = Status();
            data["values"]!["error_civ6_research"] = "Native API rejected progress.";
            data["values"]!["reason_civ6_level"] = "Unavailable raw editor.";
            var parsed = CivilizationVIModule.ParseStatus(data.ToJsonString());
            Assert(parsed.Message.Contains("Fast research: Native API rejected progress."));
            Assert(parsed.Values["diagnostic_error_civ6_research"] == "Native API rejected progress.");
            Assert(parsed.Values["diagnostic_reason_civ6_level"] == "Unavailable raw editor.");
            Assert(!parsed.Values.ContainsKey("error_civ6_research") && !parsed.Values.ContainsKey("reason_civ6_level"));
        });
        Run("Civ6 tuner endpoint ownership matches the actual local listener process", () => {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                FireTunerEndpointOwnership.RequireOwner(Environment.ProcessId, port);
                Fails(() => FireTunerEndpointOwnership.RequireOwner(Environment.ProcessId + 1, port));
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, port);
                using var accepted = listener.AcceptTcpClient();
                var clientPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
                // The exact accepted socket can still be verified after the listener closes.
                listener.Stop();
                FireTunerEndpointOwnership.RequireConnectionOwner(Environment.ProcessId, clientPort, port);
                Fails(() => FireTunerEndpointOwnership.RequireConnectionOwner(Environment.ProcessId + 1, clientPort, port));
                Fails(() => FireTunerEndpointOwnership.RequireConnectionOwner(Environment.ProcessId, clientPort == 65535 ? 65534 : clientPort + 1, port));
            } finally { listener.Stop(); }
        });
        Run("Civ6 Windows listener parser uses network byte order and limits eligible addresses", () => {
            foreach (var family in new[] { 2, 23 }) {
                var size = family == 2 ? 24 : 56;
                var table = new byte[4 + size];
                BinaryPrimitives.WriteUInt32LittleEndian(table, 1);
                var row = table.AsSpan(4);
                BinaryPrimitives.WriteUInt16BigEndian(row.Slice(family == 2 ? 8 : 20, 2), 4318);
                BinaryPrimitives.WriteInt32LittleEndian(row.Slice(family == 2 ? 20 : 52, 4), 12345);
                Assert(FireTunerEndpointOwnership.ParseOwners(table, family, 4318).SequenceEqual(new[] { 12345 }));
                Assert(FireTunerEndpointOwnership.ParseOwners(table, family, 4319).Length == 0);
                row[family == 2 ? 4 : 0] = 192;
                Assert(FireTunerEndpointOwnership.ParseOwners(table, family, 4318).Length == 0);
                BinaryPrimitives.WriteUInt32LittleEndian(table, 2);
                Fails(() => FireTunerEndpointOwnership.ParseOwners(table, family, 4318));
            }
        });
        Console.WriteLine($"Civ6 module tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static JsonObject Status()
    {
        var enabled = new JsonObject();
        var values = new JsonObject { ["player_id"] = "0", ["selected_unit_id"] = "-1", ["selected_city_id"] = "-1" };
        foreach (var id in Toggles) enabled[id] = false;
        foreach (var id in Toggles.Concat(Editors)) values["available_" + id] = Unsupported.Contains(id) ? "false" : "true";
        return new JsonObject {
            ["schema_version"] = 1, ["version"] = CivilizationVIModule.TrainerVersion, ["session_id"] = Session,
            ["ready"] = true, ["message"] = "Ready: local single-player turn.",
            ["enabled"] = enabled, ["values"] = values, ["last_command"] = "",
            ["command_success"] = false, ["command_error"] = ""
        };
    }
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failed++; Console.WriteLine("FAIL " + name + ": " + exception); }
    }
    private static void Fails(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is IOException or ArgumentException or JsonException or InvalidOperationException) { return; }
        throw new Exception("Expected input rejection.");
    }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
}
