using System.Reflection;
using HappyCheats.ChineseParents;
using Newtonsoft.Json;

// Isolated contract tests: these fixture types are never loaded into a game or packaged.
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
void Reject(Action action, string name)
{
    try { action(); } catch (InvalidDataException) { checks++; return; } catch (InvalidOperationException) { checks++; return; }
    throw new Exception("Expected rejection: " + name);
}
var session = Guid.NewGuid().ToString("N");
const long now = 1000000;
TrainerCommand Valid(string action = "add_money") => new TrainerCommand
{
    schema_version = 1, session_id = session, request_id = Guid.NewGuid().ToString("N"), action = action, created_utc = now
};
void Validate(TrainerCommand command) => Protocol.Validate(command, command.request_id + ".json", session, now);
var command = Valid(); Validate(command); checks++;
command = Valid(); command.session_id = "old-session"; Reject(() => Validate(command), "old game session");
command = Valid(); command.created_utc = now - 31; Reject(() => Validate(command), "expired command");
command = Valid(); command.created_utc = now + 6; Reject(() => Validate(command), "future command");
command = Valid(); command.schema_version = 2; Reject(() => Validate(command), "schema mismatch");
command = Valid("unrecognized"); Reject(() => Validate(command), "action allowlist");
command = Valid(); command.request_id = "../../elsewhere"; Reject(() => Validate(command), "path traversal request id");
command = Valid(); Reject(() => Protocol.Validate(command, "other.json", session, now), "filename mismatch");
command = Valid("no_stress"); Reject(() => Validate(command), "missing toggle value");
command.enabled = false; Validate(command); checks++;
command = Valid("disable_all"); Validate(command); checks++;
var status = new TrainerStatus
{
    session_id = session, process_id = 123, ready = true, message = "Ready", last_command = command.request_id,
    updated_utc = now, assembly_sha256 = "fixture-hash", bindings_valid = true, initialization_error = null
};
Check(JsonConvert.DeserializeObject<TrainerStatus>(JsonConvert.SerializeObject(status)).process_id == 123, "Status JSON process identity round trip");
var bindings = new GameBindings(Assembly.GetExecutingAssembly());
Check(!bindings.TryGetPlayer(out _, out _), "Menu is not ready");
Check(NewPlayerDataMgr.Constructions == 0, "Reading readiness must not instantiate game managers");
var player = new NewPlayerDataMgr();
SingleTon<NewPlayerDataMgr>.SetTestInstance(player);
var recordManager = new NewRecordMgr { CurrentRecord = new object(), CurrentRoleCoreInfo = new object() };
SingleTon<NewRecordMgr>.SetTestInstance(recordManager);
Check(!bindings.TryGetPlayer(out _, out _), "Current player table is required");
player.CurTable = new object();
Check(bindings.TryGetPlayer(out var resolved, out _) && ReferenceEquals(resolved, player), "Loaded player resolves");
bindings.TryGetPlayer(out _, out var firstIdentity);
bindings.TryGetPlayer(out _, out var sameIdentity);
Check(ReferenceEquals(firstIdentity, sameIdentity), "Character identity remains stable for the same record and role");
recordManager.CurrentRoleCoreInfo = new object();
bindings.TryGetPlayer(out _, out var newIdentity);
Check(!ReferenceEquals(firstIdentity, newIdentity), "New generation inside the same record changes command session identity");
player.Money = 25; bindings.Apply(player, "add_money"); Check(player.Money == 1025, "Money uses the game's property");
player.Wisdom = 99900; bindings.Apply(player, "add_knowledge"); Check(player.Wisdom == 99999, "Knowledge respects inspected game cap");
player.Money = 2000000; bindings.Apply(player, "add_money"); Check(player.Money == 2000000, "High existing value is never decreased");
player.Money = float.NaN; Reject(() => bindings.Apply(player, "add_money"), "NaN source rejected");
player.Money = float.PositiveInfinity; Reject(() => bindings.Apply(player, "add_money"), "Infinity source rejected");
player.Potential_max = 15; player.Potentiality = 2; bindings.RefillEnergy(player); Check(player.Potentiality == 15, "Round-specific action point cap");
var sets = player.EnergySets; bindings.RefillEnergy(player); Check(player.EnergySets == sets, "No redundant setter calls at full points");
player.Potential_max = 0; Reject(() => bindings.RefillEnergy(player), "Uninitialized point cap");
player.Potential_max = 100001; Reject(() => bindings.RefillEnergy(player), "Implausible point cap");
player.Pressure = 30; bindings.ClearStress(player); Check(player.Pressure == 0, "Clear stress");
sets = player.StressSets; bindings.ClearStress(player); Check(player.StressSets == sets, "No redundant zero-stress setter");
player.Iq = 5; player.Eq = float.NaN; Reject(() => bindings.Apply(player, "add_attributes"), "Invalid batch value");
Check(player.Iq == 5, "Attribute batch is prevalidated before any write");
player.Eq = 10; bindings.Apply(player, "add_attributes");
Check(player.Iq == 105 && player.Eq == 110 && player.Imagination == 100 && player.Memory == 100 && player.Stamination == 100 && player.Charm == 100, "All six inspected attributes are changed");
var temporary = Path.Combine(Path.GetTempPath(), "happycheats-contract-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    Protocol.WriteAtomic(temporary, new { version = 1 }); Protocol.WriteAtomic(temporary, new { version = 2 });
    Check(File.ReadAllText(temporary).Contains("2"), "Atomic replacement publishes final JSON");
}
finally { if (File.Exists(temporary)) File.Delete(temporary); }
Console.WriteLine($"Passed {checks} isolated trainer checks. No game process or save was modified.");

namespace HappyCheats.ChineseParents { internal static class Plugin { public const string Version = "test-fixture"; } }
public class SingleTon<T>
{
    private static T m_instance;
    public static void SetTestInstance(T instance) { m_instance = instance; }
}
public class NewRecordMgr : SingleTon<NewRecordMgr>
{
    public object CurrentRecord { get; set; }
    public object CurrentRoleCoreInfo { get; set; }
}
public class NewPlayerDataMgr : SingleTon<NewPlayerDataMgr>
{
    public static int Constructions;
    public NewPlayerDataMgr() { Constructions++; }
    public object CurTable { get; set; }
    public int Potential_max { get; set; } = 10;
    public int CurRound { get; set; } = 1;
    public float Money { get; set; }
    public float Wisdom { get; set; }
    private float energy;
    public int EnergySets;
    public float Potentiality { get => energy; set { energy = value; EnergySets++; } }
    private float stress;
    public int StressSets;
    public float Pressure { get => stress; set { stress = value; StressSets++; } }
    public float Shadow { get; set; }
    public float Face { get; set; }
    public float Iq { get; set; }
    public float Eq { get; set; }
    public float Imagination { get; set; }
    public float Memory { get; set; }
    public float Stamination { get; set; }
    public float Charm { get; set; }
}
