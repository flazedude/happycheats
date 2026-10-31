using System.Text.Json;
using System.Text.Json.Nodes;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class CrimsonDesertTests
{
    private static int passed, failed;
    private static readonly DateTime Now = new(2026, 9, 26, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Started = Now.AddMinutes(-1);
    private const int Pid = 371;
    private const string Session = "crimson-test-session-123";
    private const string Request = "0123456789abcdef0123456789abcdef";

    public static int RunAll()
    {
        passed = failed = 0;
        Run("Crimson Desert exposes explicit connection and independent manual and automatic teleport controls", () => {
            var module = new CrimsonDesertModule(Path.GetTempPath());
            Check(module.Definition.AppId == "3321460" && module.Definition.Features.Count == 9);
            Check(!module.Definition.Features.Single(feature => feature.Id == "crimson_connect").IsToggle);
            Check(!module.Definition.Features.Single(feature => feature.Id == "crimson_teleport").IsToggle);
            var automatic = module.Definition.Features.Single(feature => feature.Id == "crimson_auto_teleport");
            Check(automatic.IsToggle && automatic.Hotkey == "F9" && automatic.Number == null);
            Check(module.Definition.Features.Select(feature => feature.Hotkey).Distinct().Count() == module.Definition.Features.Count);
            var speed = module.Definition.Features.Single(feature => feature.Id == "crimson_speed");
            Check(speed.Number == new FeatureNumber(0.25m, 3m, 1m, 0.25m, "×") && speed.ChoiceValueKey == "crimson_speed");
        });
        Run("hotfix compatibility rejects previous executables before installation", () => {
            using var f = new Fixture(); var module = f.Module();
            module.HashProvider = _ => "57DA440D72F4DB974F25FEF047CF84C4DADD999A88CB2A3C5AF4C9BD67FDE1E7";
            Check(!module.Compatible(f.Game, force: true));
            Reject(() => module.InstallAsync(f.Installation).GetAwaiter().GetResult());
            Check(!File.Exists(f.InstalledDll));
            module.HashProvider = _ => "D4855C08998638F07FA8FAE08BD6249261F10C8FEC2215EFFC29CB339E7FA525";
            Check(!module.Compatible(f.Game, force: true));
            Reject(() => module.InstallAsync(f.Installation).GetAwaiter().GetResult());
            Check(!File.Exists(f.InstalledDll));
            module.HashProvider = _ => "FEC07BE1B725A46624D31FCD74BF3730EA809765226CEA6BDE01607AB2D79F66";
            Check(module.Compatible(f.Game, force: true));
        });
        Run("waiting game exposes only explicit connection", () => {
            var waiting = CrimsonDesertModule.AwaitingConnection(Pid, Started, true);
            Check(waiting.Connected && waiting.Ready && waiting.Values["available_crimson_connect"] == bool.TrueString);
            foreach (var id in CrimsonDesertModule.FeatureIds) Check(waiting.Values["available_" + id] == bool.FalseString);
            Check(CrimsonDesertModule.AwaitingConnection(Pid, Started, false).Values["available_crimson_connect"] == bool.FalseString);
        });
        Run("native heartbeat requires exact schema, version, PID and process start", () => {
            Check(Parse(State()).Status.Connected);
            foreach (var key in new[] { "schema_version", "version", "process_id", "process_start_unix_ms", "heartbeat_unix_ms", "session_id" }) {
                var missing = State(); missing.Remove(key); Check(!Parse(missing).Status.Connected);
            }
            foreach (var replacement in new (string Key, JsonNode Value)[] {
                ("schema_version", JsonValue.Create(2)!), ("version", JsonValue.Create("0.1.0")!),
                ("version", JsonValue.Create("0.2.1")!),
                ("version", JsonValue.Create("0.2.2")!),
                ("version", JsonValue.Create("0.2.3")!),
                ("version", JsonValue.Create("0.2.4")!),
                ("version", JsonValue.Create("0.2.5")!),
                ("version", JsonValue.Create("0.2.6")!),
                ("version", JsonValue.Create("0.2.7")!),
                ("process_id", JsonValue.Create(Pid + 1)!), ("process_start_unix_ms", JsonValue.Create(Milliseconds(Started) + 1)!),
                ("session_id", JsonValue.Create("../../bad")!)
            }) {
                var state = State(); state[replacement.Key] = replacement.Value; Check(!Parse(state).Status.Connected);
            }
        });
        Run("stale, pre-process and future heartbeats fail closed", () => {
            Check(!CrimsonDesertModule.ParseSnapshot(Json(State()), Now.AddSeconds(-4), Started, Pid, Now).Status.Connected);
            Check(!CrimsonDesertModule.ParseSnapshot(Json(State()), Started.AddTicks(-1), Started, Pid, Now).Status.Connected);
            Check(!CrimsonDesertModule.ParseSnapshot(Json(State()), Now.AddSeconds(3), Started, Pid, Now).Status.Connected);
            Check(CrimsonDesertModule.ParseSnapshot(Json(State()), Now.AddSeconds(2), Started, Pid, Now).Status.Connected);
            foreach (var heartbeat in new[] { Milliseconds(Now) - 3001, Milliseconds(Now) + 2001, Milliseconds(Started) - 1, long.MaxValue }) {
                var state = State(); state["heartbeat_unix_ms"] = heartbeat; Check(!Parse(state).Status.Connected);
            }
        });
        Run("feature availability is explicit Boolean data", () => {
            var state = State(); state["available"]!["crimson_health"] = "true";
            state["values"]!["available_crimson_health"] = true;
            state["values"]!["available_crimson_connect"] = true;
            var parsed = Parse(state).Status;
            Check(parsed.Values["available_crimson_health"] == bool.FalseString);
            Check(parsed.Values["available_crimson_connect"] == bool.FalseString);
            Check(parsed.Values["available_crimson_teleport"] == bool.FalseString);
            Check(parsed.Values["available_crimson_auto_teleport"] == bool.FalseString);
            state.Remove("available");
            foreach (var id in CrimsonDesertModule.FeatureIds) Check(Parse(state).Status.Values["available_" + id] == bool.FalseString);
        });
        Run("native initialization failure remains distinct from a missing player", () => {
            var state = State(); state["ready"] = false; state["player_ready"] = false;
            state["message"] = "Unsupported executable or modified hook locations; no hooks installed.";
            var failed = Parse(state).Status;
            Check(failed.Connected && !failed.Ready && failed.Values["diagnostic_crimson_initialization_failed"] == bool.TrueString);
            Check(failed.Message == "Unsupported executable or modified hook locations; no hooks installed.");
            Reject(() => Command(failed, "crimson_health", true));
            state["ready"] = true;
            state["values"]!["diagnostic_crimson_initialization_failed"] = true;
            var waitingForPlayer = Parse(state).Status;
            Check(waitingForPlayer.Ready && waitingForPlayer.Values["diagnostic_crimson_initialization_failed"] == bool.FalseString);
            Check(!CrimsonDesertModule.AwaitingConnection(Pid, Started, true).Values.ContainsKey("diagnostic_crimson_initialization_failed"));
        });
        Run("teleport diagnostics do not authorize a control", () => {
            var state = State();
            state["teleport_message"] = "Waiting for the new map marker to be accepted. Terrain height is not checked.";
            state["teleport_pending"] = true;
            state["values"]!["reason_crimson_auto_teleport"] = "The controlled client player is not recognized.";
            var parsed = Parse(state).Status;
            Check(parsed.Values["crimson_teleport_status"].StartsWith("Waiting for the new map marker", StringComparison.Ordinal));
            Check(parsed.Values["reason_crimson_auto_teleport"] == "The controlled client player is not recognized.");
            Check(parsed.Values["available_crimson_auto_teleport"] == bool.FalseString);
            Reject(() => Command(parsed, "crimson_auto_teleport", true));
            state["teleport_message"] = new string('x', 513);
            Check(!Parse(state).Status.Values.ContainsKey("crimson_teleport_status"));
        });
        Run("detailed hook failure passes through without authorizing controls or stale status", () => {
            const string message = "Native hook setup failed: stats create=MH_ERROR_MEMORY_ALLOC (9); damage enable=MH_ERROR_MEMORY_PROTECT (10); movement create=MH_ERROR_MEMORY_ALLOC (9); marker skipped=movement_hook_not_ready. Affected controls are unavailable.";
            var state = State(); state["ready"] = false; state["player_ready"] = false;
            state["message"] = message; state["initialization_error"] = "partial_hooks_failed";
            state["available"] = new JsonObject(CrimsonDesertModule.FeatureIds.Select(id => KeyValuePair.Create<string, JsonNode?>(id, JsonValue.Create(false))));
            state["hook_library"] = new JsonObject {
                ["attempted"] = true, ["status"] = "MH_OK", ["code"] = 0, ["raw_win32_last_error"] = 18
            };
            state["hook_results"] = new JsonObject {
                ["stats"] = new JsonObject {
                    ["ready"] = false, ["target_rva"] = 0xC7EFDE0,
                    ["create"] = new JsonObject { ["attempted"] = true, ["status"] = "MH_ERROR_MEMORY_ALLOC", ["code"] = 9, ["raw_win32_last_error"] = 0 },
                    ["enable"] = new JsonObject { ["attempted"] = false, ["status"] = null, ["code"] = null, ["raw_win32_last_error"] = null },
                    ["skipped_reason"] = "create_failed"
                }
            };
            var parsed = Parse(state).Status;
            Check(parsed.Connected && !parsed.Ready && parsed.Message == message);
            Check(parsed.Values["diagnostic_crimson_initialization_failed"] == bool.TrueString);
            foreach (var id in CrimsonDesertModule.FeatureIds) Check(parsed.Values["available_" + id] == bool.FalseString);
            Reject(() => Command(parsed, "crimson_health", true));
            Reject(() => Command(parsed, "crimson_auto_teleport", true));
            var staleFile = CrimsonDesertModule.ParseSnapshot(Json(state), Now.AddSeconds(-4), Started, Pid, Now).Status;
            Check(!staleFile.Connected && staleFile.Message != message);
            state["heartbeat_unix_ms"] = Milliseconds(Now.AddSeconds(-4));
            var staleHeartbeat = Parse(state).Status;
            Check(!staleHeartbeat.Connected && staleHeartbeat.Message != message);
        });
        Run("toggle command is versioned, expiring and session bound", () => {
            var current = Parse(State()).Status;
            var command = Command(current, "crimson_health", true);
            Check((string)command["session_id"] == Session && (string)command["request_id"] == Request);
            Check((string)command["version"] == "0.2.8" && (int)command["schema_version"] == 1);
            Check((bool)command["enabled"] && (long)command["expires_unix_ms"] == Milliseconds(Now) + 5000);
            Reject(() => CrimsonDesertModule.CreateCommand(current, current with { SessionId = "another-session" }, "crimson_health", true, null, Request, Milliseconds(Now)));
            Reject(() => CrimsonDesertModule.CreateCommand(current, current, "crimson_health", true, null, "bad", Milliseconds(Now)));
            Reject(() => CrimsonDesertModule.CreateCommand(current, current, "crimson_health", true, null, Request, long.MaxValue));
        });
        Run("unavailable, loading, unknown and malformed control requests are refused", () => {
            var current = Parse(State()).Status;
            Reject(() => Command(current, "crimson_teleport"));
            Reject(() => Command(current with { Ready = false }, "crimson_health", true));
            Reject(() => Command(current, "unknown", true));
            Reject(() => Command(current, "crimson_connect"));
            Reject(() => Command(current, "crimson_health"));
            Reject(() => Command(current, "crimson_health", true, 1));
            Reject(() => Command(current, "disable_all", false));
        });
        Run("disable-all bypasses readiness while retaining session validation", () => {
            var current = Parse(State()).Status with { Ready = false };
            Check((string)Command(current, "disable_all")["feature"] == "disable_all");
            Reject(() => Command(current with { Connected = false }, "disable_all"));
        });
        Run("numeric speed validates range and quarter steps", () => {
            var state = State(); state["available"]!["crimson_speed"] = true;
            var current = Parse(state).Status;
            Check((decimal)Command(current, "crimson_speed", value: 0.25m)["value"] == 0.25m);
            Check((decimal)Command(current, "crimson_speed", value: 3m)["value"] == 3m);
            foreach (var value in new[] { 0m, 0.3m, 3.25m, decimal.MaxValue }) Reject(() => Command(current, "crimson_speed", value: value));
            Reject(() => Command(current, "crimson_speed", true, 1m));
            Reject(() => Command(current, "crimson_speed"));
        });
        Run("teleport is one-shot and cannot contain a toggle", () => {
            var state = State(); state["available"]!["crimson_teleport"] = true;
            var current = Parse(state).Status;
            var command = Command(current, "crimson_teleport");
            Check(!command.ContainsKey("enabled") && !command.ContainsKey("value"));
            Reject(() => Command(current, "crimson_teleport", true));
        });
        Run("automatic teleport requires an explicit Boolean state and independent availability", () => {
            var state = State(); state["available"]!["crimson_teleport"] = true;
            Reject(() => Command(Parse(state).Status, "crimson_auto_teleport", true));
            state["available"]!["crimson_auto_teleport"] = "true";
            Reject(() => Command(Parse(state).Status, "crimson_auto_teleport", true));
            state["available"]!["crimson_auto_teleport"] = true;
            var current = Parse(state).Status;
            var command = Command(current, "crimson_auto_teleport", true);
            Check((string)command["feature"] == "crimson_auto_teleport" && (bool)command["enabled"]);
            Check(!command.ContainsKey("value") && !command.ContainsKey("crimson_teleport"));
            Reject(() => Command(current, "crimson_auto_teleport"));
            Reject(() => Command(current, "crimson_auto_teleport", true, 1));
            Reject(() => Command(current with { Ready = false }, "crimson_auto_teleport", true));
        });
        Run("automatic teleport off survives lost readiness without relaxing session identity", () => {
            var current = Parse(State()).Status with { Ready = false };
            Check(!(bool)Command(current, "crimson_auto_teleport", false)["enabled"]);
            Reject(() => Command(current, "crimson_auto_teleport", false, 1));
            Reject(() => Command(current with { Connected = false }, "crimson_auto_teleport", false));
            Reject(() => CrimsonDesertModule.CreateCommand(current, current with { SessionId = "different-session" },
                "crimson_auto_teleport", false, null, Request, Milliseconds(Now)));
        });
        Run("automatic teleport state is native-confirmed and does not depend on manual readiness", () => {
            var state = State();
            state["enabled"]!["crimson_auto_teleport"] = true;
            state["available"]!["crimson_auto_teleport"] = true;
            var current = Parse(state).Status;
            Check(current.Enabled["crimson_auto_teleport"] && current.Values["available_crimson_auto_teleport"] == bool.TrueString);
            Check(current.Values["available_crimson_teleport"] == bool.FalseString);
            var reset = State(); reset["enabled"]!["crimson_auto_teleport"] = false;
            Check(!Parse(reset).Status.Enabled["crimson_auto_teleport"]);
        });
        Run("acknowledgements require matching last-command and result identities", () => {
            var state = State(); state["last_command"] = Request;
            state["command_result"] = new JsonObject { ["request_id"] = Request, ["success"] = true, ["message"] = "Applied" };
            Check(CrimsonDesertModule.AcceptAcknowledgement(Parse(state), Session, Request));
            state["command_result"]!["request_id"] = "previous";
            Check(!CrimsonDesertModule.AcceptAcknowledgement(Parse(state), Session, Request));
            state["command_result"]!["request_id"] = Request; state["command_result"]!["success"] = false;
            Check(Reject(() => CrimsonDesertModule.AcceptAcknowledgement(Parse(state), Session, Request)).Message == "Applied");
            Reject(() => CrimsonDesertModule.AcceptAcknowledgement(Parse(state), "different-session", Request));
            Check(!CrimsonDesertModule.AcceptAcknowledgement(new(TrainerStatus.Offline(), "", false, ""), Session, Request));
        });
        Run("queued teleport acknowledgement does not hide a later asynchronous cancellation", () => {
            const string queued = "Waiting for the new map marker to be accepted. Terrain height is not checked.";
            const string canceled = "Marker teleport canceled: destination changed, cleared, or was not accepted in time.";
            var state = State();
            state["last_command"] = Request;
            state["command_result"] = new JsonObject {
                ["request_id"] = Request, ["success"] = true,
                ["message"] = "Experimental marker teleport queued; terrain height and landing are unverified."
            };
            state["teleport_message"] = queued; state["teleport_pending"] = true;
            var accepted = Parse(state);
            Check(CrimsonDesertModule.AcceptAcknowledgement(accepted, Session, Request));
            Check(accepted.Status.Values["crimson_teleport_status"] == queued);
            // The command acknowledgement intentionally remains successful after a queued
            // movement is canceled. The live outcome must remain separately observable.
            state["teleport_message"] = canceled; state["teleport_pending"] = false;
            var later = Parse(state);
            Check(CrimsonDesertModule.AcceptAcknowledgement(later, Session, Request));
            Check(later.Status.Values["crimson_teleport_status"] == canceled && later.Status.Connected && later.Status.Ready);
            Check(later.Status.Values["available_crimson_teleport"] == bool.FalseString);
            Reject(() => Command(later.Status, "crimson_teleport"));
            state["heartbeat_unix_ms"] = Milliseconds(Now.AddSeconds(-4));
            var stale = Parse(state);
            Check(!stale.Status.Connected && !stale.Status.Values.ContainsKey("crimson_teleport_status"));
            Check(!CrimsonDesertModule.AcceptAcknowledgement(stale, Session, Request));
        });
        Run("later movement observations remain distinct from the accepted command", () => {
            const string acceptedMessage = "Experimental marker teleport queued; terrain height and landing are unverified.";
            var state = State();
            state["last_command"] = Request;
            state["command_result"] = new JsonObject { ["request_id"] = Request, ["success"] = true, ["message"] = acceptedMessage };
            foreach (var outcome in new (string Message, bool Pending)[] {
                ("Marker position update submitted; movement has not been confirmed.", true),
                ("Position updates submitted; waiting for a later movement update to observe the result.", true),
                ("Position observed near the marker after a later movement update; terrain landing is unverified.", false),
                ("Marker teleport not retained after a later movement update; no deferred retry.", false),
                ("Marker teleport expired before movement observation completed; no deferred retry.", false),
                ("Marker teleport stopped: position write readback failed; no deferred retry.", false)
            }) {
                state["teleport_message"] = outcome.Message;
                state["teleport_pending"] = outcome.Pending;
                var snapshot = Parse(state);
                Check(CrimsonDesertModule.AcceptAcknowledgement(snapshot, Session, Request));
                Check(snapshot.ResultMessage == acceptedMessage && snapshot.Status.Values["crimson_teleport_status"] == outcome.Message);
                Check(snapshot.Status.Connected && snapshot.Status.Ready);
                Check(snapshot.Status.Values["available_crimson_teleport"] == bool.FalseString
                    && snapshot.Status.Values["available_crimson_auto_teleport"] == bool.FalseString);
            }
        });
        Run("package identity, completeness, path and checksums are mandatory", () => {
            using var f = new Fixture(); Check(CrimsonDesertInstaller.ReadPackage(f.Package).Count == 2);
            foreach (var field in new[] { "schemaVersion", "game", "version" }) {
                var manifest = f.Manifest(); manifest[field] = "wrong"; f.WriteManifest(manifest);
                Reject(() => CrimsonDesertInstaller.ReadPackage(f.Package));
            }
            foreach (var version in new[] { "0.1.0", "0.3.0" }) {
                var manifest = f.Manifest(); manifest["version"] = version; f.WriteManifest(manifest);
                Reject(() => CrimsonDesertInstaller.ReadPackage(f.Package));
            }
            var empty = f.Manifest(); empty["files"] = new JsonArray(); f.WriteManifest(empty); Reject(() => CrimsonDesertInstaller.ReadPackage(f.Package));
            var escape = f.Manifest(); escape["files"]![0]!["path"] = "../escape"; f.WriteManifest(escape); Reject(() => CrimsonDesertInstaller.ReadPackage(f.Package));
            var duplicate = f.Manifest(); duplicate["files"]![1]!["path"] = CrimsonDesertInstaller.DllPath; f.WriteManifest(duplicate); Reject(() => CrimsonDesertInstaller.ReadPackage(f.Package));
            var badHash = f.Manifest(); badHash["files"]![0]!["sha256"] = "not a hash"; f.WriteManifest(badHash); Reject(() => CrimsonDesertInstaller.ReadPackage(f.Package));
            f.WriteManifest(); File.AppendAllText(f.PayloadDll, "corrupt"); Reject(() => CrimsonDesertInstaller.ReadPackage(f.Package));
            Check(!Directory.EnumerateFiles(f.Game, "*", SearchOption.AllDirectories).Any(path => path != f.Executable));
        });
        Run("install and remove only owned files while preserving user files", () => {
            using var f = new Fixture(); f.Install();
            Check(CrimsonDesertInstaller.Installed(f.Game) && CrimsonDesertInstaller.MatchesPackage(f.Package, f.Game));
            var user = SafePath(f.Game, CrimsonDesertInstaller.TrainerRoot + "user.txt"); File.WriteAllText(user, "keep");
            File.WriteAllText(f.StatusPath, "{}"); File.WriteAllText(f.LeasePath, Session);
            CrimsonDesertInstaller.Remove(f.Game, () => { });
            Check(!File.Exists(f.InstalledDll) && !File.Exists(f.StatusPath) && !File.Exists(f.LeasePath));
            Check(File.ReadAllText(user) == "keep" && File.Exists(f.Executable));
        });
        Run("foreign and modified trainer files remain untouched", () => {
            using var f = new Fixture(); Directory.CreateDirectory(Path.GetDirectoryName(f.InstalledDll)!); File.WriteAllText(f.InstalledDll, "foreign");
            Reject(f.Install); Check(File.ReadAllText(f.InstalledDll) == "foreign" && !File.Exists(f.ReceiptPath));
            File.Delete(f.InstalledDll); f.Install(); File.AppendAllText(f.InstalledDll, " modified");
            File.WriteAllText(f.StatusPath, "keep modified trainer state");
            Reject(f.Install); CrimsonDesertInstaller.Remove(f.Game, () => { });
            Check(File.ReadAllText(f.InstalledDll).EndsWith(" modified") && File.Exists(f.StatusPath));
        });
        Run("identical foreign payload is never adopted for removal", () => {
            using var f = new Fixture(); Directory.CreateDirectory(Path.GetDirectoryName(f.InstalledDll)!);
            File.Copy(f.PayloadDll, f.InstalledDll); f.Install();
            CrimsonDesertInstaller.Remove(f.Game, () => { });
            Check(File.Exists(f.InstalledDll));
        });
        Run("interrupted installation rolls back every committed file", () => {
            using var f = new Fixture(); var calls = 0;
            Reject(() => CrimsonDesertInstaller.Install(f.Package, f.Game, () => { if (++calls == 4) throw new IOException("Game started"); }));
            Check(!File.Exists(f.InstalledDll) && !File.Exists(f.InstalledNotice) && !File.Exists(f.ReceiptPath));
        });
        Run("interrupted update restores prior payload and receipt", () => {
            using var f = new Fixture(); f.Install(); var oldDll = Hash(f.InstalledDll); var oldNotice = Hash(f.InstalledNotice); var oldReceipt = Hash(f.ReceiptPath);
            File.AppendAllText(f.PayloadDll, " new"); File.AppendAllText(f.PayloadNotice, " new"); f.WriteManifest();
            var calls = 0; Reject(() => CrimsonDesertInstaller.Install(f.Package, f.Game, () => { if (++calls == 4) throw new IOException("Game started"); }));
            Check(Hash(f.InstalledDll) == oldDll && Hash(f.InstalledNotice) == oldNotice && Hash(f.ReceiptPath) == oldReceipt);
            f.Install(); Check(CrimsonDesertInstaller.MatchesPackage(f.Package, f.Game));
        });
        Run("running-game guard blocks install and removal before writes", () => {
            using var f = new Fixture(); Reject(() => CrimsonDesertInstaller.Install(f.Package, f.Game, () => throw new IOException("running")));
            Check(!File.Exists(f.InstalledDll)); f.Install(); var before = Hash(f.InstalledDll);
            Reject(() => CrimsonDesertInstaller.Remove(f.Game, () => throw new IOException("running"))); Check(Hash(f.InstalledDll) == before);
        });
        Run("removal rollback restores files if game starts during operation", () => {
            using var f = new Fixture(); f.Install(); var dll = Hash(f.InstalledDll); var notice = Hash(f.InstalledNotice);
            var calls = 0; Reject(() => CrimsonDesertInstaller.Remove(f.Game, () => { if (++calls == 3) throw new IOException("Game started"); }));
            Check(Hash(f.InstalledDll) == dll && Hash(f.InstalledNotice) == notice && CrimsonDesertInstaller.MatchesPackage(f.Package, f.Game));
        });
        Run("invalid ownership receipt and missing ownership never authorize writes", () => {
            using var f = new Fixture(); Reject(() => CrimsonDesertInstaller.Remove(f.Game, () => { }));
            WriteJson(f.ReceiptPath, new { SchemaVersion = 1, Module = "foreign", Files = Array.Empty<object>() });
            Reject(f.Install); Check(!File.Exists(f.InstalledDll));
            WriteJson(f.ReceiptPath, new { SchemaVersion = 1, Module = "crimson-desert", Files = new[] { new { Path = "../outside", Sha256 = new string('a', 64) } } });
            Reject(f.Install); Check(!File.Exists(f.InstalledDll));
        });
        Run("compatibility inspection caches metadata but forced attach rehashes", () => {
            using var f = new Fixture(); var module = f.Module(); var calls = 0;
            module.HashProvider = _ => { calls++; return CrimsonDesertModule.SupportedExecutableHash; };
            Check(module.Compatible(f.Game) && module.Compatible(f.Game) && calls == 1);
            Check(module.Compatible(f.Game, true) && calls == 2);
            module.HashProvider = _ => new string('0', 64); Check(!module.Compatible(f.Game, true));
        });
        Run("discovery and passive status never attach or create a lease", () => {
            using var f = new Fixture(); f.Install(); var module = f.Module(); var attached = 0; module.AttachTrainer = _ => attached++;
            f.WriteStatus(); var statusHash = Hash(f.StatusPath);
            var installation = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Check(installation.IsInstalled && installation.TrainerInstalled && installation.Compatible && installation.IsRunning);
            var state = module.ReadStatusAsync(installation).GetAwaiter().GetResult();
            Check(state.Values["available_crimson_connect"] == bool.TrueString && attached == 0);
            Check(!File.Exists(f.LeasePath) && !File.Exists(f.CommandPath) && Hash(f.StatusPath) == statusHash);
        });
        Run("explicit connection verifies payload then authorizes only its native session", () => {
            using var f = new Fixture(); f.Install(); var module = f.Module(); var attached = 0;
            module.AttachTrainer = _ => { attached++; f.WriteStatus(); };
            var waiting = module.ReadStatusAsync(f.Installation).GetAwaiter().GetResult();
            module.SendAsync(f.Installation, waiting, "crimson_connect").GetAwaiter().GetResult();
            Check(attached == 1 && File.ReadAllText(f.LeasePath) == Session);
            var active = module.ReadStatusAsync(f.Installation).GetAwaiter().GetResult();
            Check(active.SessionId == Session && active.Values["available_crimson_connect"] == bool.FalseString);
            f.WriteStatus(session: "new-native-session");
            var changed = module.ReadStatusAsync(f.Installation).GetAwaiter().GetResult();
            Check(changed.Values["available_crimson_connect"] == bool.TrueString && File.ReadAllText(f.LeasePath) == Session);
        });
        Run("tampered DLL, changed process and unsupported executable cannot attach", () => {
            using var f = new Fixture(); f.Install(); var module = f.Module(); var attached = 0; module.AttachTrainer = _ => attached++;
            var waiting = module.ReadStatusAsync(f.Installation).GetAwaiter().GetResult();
            File.AppendAllText(f.InstalledDll, "tampered");
            Reject(() => module.SendAsync(f.Installation, waiting, "crimson_connect").GetAwaiter().GetResult());
            Check(attached == 0 && !File.Exists(f.LeasePath));
            File.Copy(f.PayloadDll, f.InstalledDll, true); module.HashProvider = _ => new string('0', 64);
            Reject(() => module.SendAsync(f.Installation, waiting, "crimson_connect").GetAwaiter().GetResult());
            module.HashProvider = _ => CrimsonDesertModule.SupportedExecutableHash;
            module.ProcessProvider = _ => new[] { (Pid + 1, f.ProcessStarted) };
            Reject(() => module.SendAsync(f.Installation, waiting, "crimson_connect").GetAwaiter().GetResult());
            Check(attached == 0 && !File.Exists(f.LeasePath));
        });
        Run("oversized and malformed status cannot authorize connection", () => {
            using var f = new Fixture(); f.Install();
            File.WriteAllText(f.StatusPath, new string('x', 32769));
            Check(!CrimsonDesertModule.ReadSnapshotFile(f.StatusPath, f.ProcessStarted, Pid).Status.Connected);
            File.WriteAllText(f.StatusPath, "{");
            Check(!CrimsonDesertModule.ReadSnapshotFile(f.StatusPath, f.ProcessStarted, Pid).Status.Connected);
        });
        Run("expired known commands can be retired while pending and foreign requests survive", () => {
            using var f = new Fixture(); f.Install();
            var command = JsonSerializer.SerializeToElement(Command(Parse(State()).Status, "crimson_health", true));
            WriteJson(f.CommandPath, command);
            CrimsonDesertModule.RetireExpiredCommand(f.CommandPath, Milliseconds(Now) + 5000);
            Check(File.Exists(f.CommandPath));
            CrimsonDesertModule.RetireExpiredCommand(f.CommandPath, Milliseconds(Now) + 5001);
            Check(!File.Exists(f.CommandPath));
            WriteJson(f.CommandPath, new { request_id = Request, expires_unix_ms = 1 });
            CrimsonDesertModule.RetireExpiredCommand(f.CommandPath, Milliseconds(Now)); Check(File.Exists(f.CommandPath));
            File.WriteAllText(f.CommandPath, "{");
            Reject(() => CrimsonDesertModule.RetireExpiredCommand(f.CommandPath, Milliseconds(Now)));
            Check(File.ReadAllText(f.CommandPath) == "{");
        });
        Run("mailbox command requires a real matching acknowledgement and cleans itself", () => {
            using var f = new Fixture(); f.Install(); var module = f.Module(); module.AttachTrainer = _ => f.WriteStatus();
            module.SendAsync(f.Installation, module.ReadStatusAsync(f.Installation).GetAwaiter().GetResult(), "crimson_connect").GetAwaiter().GetResult();
            var status = module.ReadStatusAsync(f.Installation).GetAwaiter().GetResult();
            var responder = Task.Run(async () => {
                for (var attempt = 0; attempt < 100; attempt++) {
                    if (File.Exists(f.CommandPath)) { var command = ReadJson(f.CommandPath); f.WriteStatus(request: Text(command, "request_id")); return; }
                    await Task.Delay(20);
                }
                throw new Exception("No command was published.");
            });
            module.SendAsync(f.Installation, status, "crimson_health", true).GetAwaiter().GetResult(); responder.GetAwaiter().GetResult();
            Check(!File.Exists(f.CommandPath));
            WriteJson(f.CommandPath, new { request_id = "unrelated" });
            Reject(() => module.SendAsync(f.Installation, status, "crimson_health", false).GetAwaiter().GetResult());
            Check(Text(ReadJson(f.CommandPath), "request_id") == "unrelated");
        });
        Console.WriteLine($"Crimson Desert core tests: {passed} passed, {failed} failed. No game was launched or attached.");
        return failed > 0 ? 1 : 0;
    }

    private static Dictionary<string, object> Command(TrainerStatus state, string feature, bool? enabled = null, decimal? value = null) =>
        CrimsonDesertModule.CreateCommand(state, state, feature, enabled, value, Request, Milliseconds(Now));
    private static long Milliseconds(DateTime time) => new DateTimeOffset(time).ToUnixTimeMilliseconds();
    private static JsonElement Json(JsonObject value) => JsonSerializer.SerializeToElement(value);
    private static CrimsonDesertModule.Snapshot Parse(JsonObject state) => CrimsonDesertModule.ParseSnapshot(Json(state), Now, Started, Pid, Now);
    private static JsonObject State(DateTime? now = null, DateTime? started = null, string session = Session) => new() {
        ["schema_version"] = 1, ["version"] = CrimsonDesertModule.TrainerVersion, ["process_id"] = Pid, ["process_start_unix_ms"] = Milliseconds(started ?? Started),
        ["heartbeat_unix_ms"] = Milliseconds(now ?? Now), ["session_id"] = session, ["ready"] = true,
        ["message"] = "Fixture status", ["enabled"] = new JsonObject(), ["values"] = new JsonObject { ["crimson_speed"] = 1 },
        ["available"] = new JsonObject { ["crimson_health"] = true, ["crimson_stamina"] = true, ["crimson_one_hit"] = true, ["crimson_spirit"] = true },
        ["last_command"] = "", ["command_result"] = new JsonObject { ["request_id"] = "", ["success"] = false, ["message"] = "" }
    };
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static Exception Reject(Action action) { try { action(); } catch (Exception error) { return error; } throw new Exception("Expected rejection."); }
    private static void Run(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-CrimsonTests-" + Guid.NewGuid().ToString("N"));
        public string Game => Path.Combine(Root, "game");
        public string Package => Path.Combine(Root, "payload", "crimson-desert");
        public string Executable => SafePath(Game, CrimsonDesertModule.Executable);
        public string PayloadDll => SafePath(Package, CrimsonDesertInstaller.DllPath);
        public string PayloadNotice => SafePath(Package, CrimsonDesertInstaller.NoticePath);
        public string InstalledDll => SafePath(Game, CrimsonDesertInstaller.DllPath);
        public string InstalledNotice => SafePath(Game, CrimsonDesertInstaller.NoticePath);
        public string ReceiptPath => SafePath(Game, CrimsonDesertInstaller.ReceiptPath);
        public string StatusPath => SafePath(Game, CrimsonDesertModule.BridgeRoot + "status.json");
        public string LeasePath => SafePath(Game, CrimsonDesertModule.BridgeRoot + "lease.txt");
        public string CommandPath => SafePath(Game, CrimsonDesertModule.BridgeRoot + "command.json");
        public DateTime ProcessStarted { get; } = DateTime.UtcNow.AddMinutes(-1);
        public GameInstallation Installation => new(Game, "26329202", true, true, true, true, "Fixture");
        public Fixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Executable)!); File.WriteAllText(Executable, "fixture executable; never launched");
            Directory.CreateDirectory(Path.GetDirectoryName(PayloadDll)!); File.WriteAllText(PayloadDll, "fixture DLL; never loaded");
            File.WriteAllText(PayloadNotice, "Fixture notices"); WriteManifest();
        }
        public JsonObject Manifest() => new() {
            ["schemaVersion"] = 1, ["game"] = "crimson-desert", ["version"] = CrimsonDesertModule.TrainerVersion,
            ["files"] = new JsonArray(CrimsonDesertInstaller.RequiredFiles.Select(path => (JsonNode)new JsonObject { ["path"] = path, ["sha256"] = Hash(SafePath(Package, path)) }).ToArray())
        };
        public void WriteManifest(JsonObject? manifest = null) => WriteJson(SafePath(Package, "package.json"), manifest ?? Manifest());
        public void Install() => CrimsonDesertInstaller.Install(Package, Game, () => { });
        public CrimsonDesertModule Module() => new(Root) {
            HashProvider = _ => CrimsonDesertModule.SupportedExecutableHash,
            ProcessProvider = _ => new[] { (Pid, ProcessStarted) },
            AttachTrainer = _ => throw new Exception("Unexpected native attachment during fixture test.")
        };
        public void WriteStatus(string session = Session, string? request = null)
        {
            var state = State(DateTime.UtcNow, ProcessStarted, session);
            if (request != null) { state["last_command"] = request; state["command_result"] = new JsonObject { ["request_id"] = request, ["success"] = true, ["message"] = "Fixture acknowledgement" }; }
            WriteJson(StatusPath, state);
        }
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("HappyCheats-CrimsonTests-", StringComparison.Ordinal))
                throw new IOException("Unexpected fixture cleanup path.");
            Directory.Delete(resolved, true);
        }
    }
}
