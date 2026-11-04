using System.Reflection;
using HappyCheats.LivinCream;
using makif;
using makif.MoneyKit;
using Newtonsoft.Json;

const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
var checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
void Reject(Action action) { try { action(); } catch (Exception) { checks++; return; } throw new Exception("Expected rejection"); }
object Call(Plugin plugin, string method, params object[] args) => typeof(Plugin).GetMethod(method, flags)!.Invoke(plugin, args);
void Set(Plugin plugin, string name, object value) => typeof(Plugin).GetField(name, flags)!.SetValue(plugin, value);
string Session(Plugin plugin) => (string)typeof(Plugin).GetField("session", flags)!.GetValue(plugin);
TrainerCommand Command(Plugin plugin, string action, decimal? value = null, bool? enabled = null) => new() {
    schema_version = 1, session_id = Session(plugin), request_id = Guid.NewGuid().ToString("N"), action = action,
    created_utc = Protocol.Now(), value = value, enabled = enabled
};
var root = Path.Combine(Path.GetTempPath(), "HappyCheats-livin-cream-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try {
    UnityEngine.Object.Objects.Clear(); GlobalData.Database = new();
    var shop = new Shop(); var player = new Player { Shop = shop }; var level = new LevelManager(); var skills = new SkillTreeManager();
    var plugin = new Plugin(); Set(plugin, "valid", true); Set(plugin, "instance", plugin);
    var commands = Path.Combine(root, "commands"); var responses = Path.Combine(root, "responses");
    Directory.CreateDirectory(commands); Directory.CreateDirectory(responses);
    Set(plugin, "ipc", root); Set(plugin, "commands", commands); Set(plugin, "responses", responses);
    Call(plugin, "RefreshContext");
    Check((bool)Call(plugin, "ReadyNow"), "Loaded shop must be ready");
    var c = Command(plugin, "lc_money", 12345); Protocol.Validate(c, c.request_id + ".json", c.session_id, Protocol.Now());
    Call(plugin, "Execute", c); Check(Money.GetMoney() == 12345 && Money.Writes == 1, "Money native setter");
    foreach (var action in new[] { "lc_money", "lc_add_xp", "lc_add_skills" }) {
        foreach (var invalid in new decimal?[] { null, -1, 0.5m, 100000001 }) {
            c = Command(plugin, action, invalid); Reject(() => Protocol.Validate(c, c.request_id + ".json", c.session_id, Protocol.Now()));
        }
    }
    c = Command(plugin, "lc_add_xp", 250); Call(plugin, "Execute", c);
    Check(level.Grants == 1 && level.GetCurrentLevel() == 3 && level.GetCurrentXP() == 50 && skills.GetAvailablePoints() == 4, "Native XP grants and level rewards");
    level.Required = 0; Reject(() => Call(plugin, "Execute", Command(plugin, "lc_add_xp", 1)));
    Check(level.Grants == 1, "Invalid XP requirement must not mutate"); level.Required = 100;
    Call(plugin, "Execute", Command(plugin, "lc_add_skills", 10)); Check(skills.Grants == 1 && skills.GetAvailablePoints() == 14, "Native skill grant");
    GlobalData.Database.SkillPointDatabase.TotalEarned = int.MaxValue;
    Reject(() => Call(plugin, "Execute", Command(plugin, "lc_add_skills", 1)));
    Check(skills.Grants == 1, "Skill overflow rejected before mutation");
    Call(plugin, "Execute", Command(plugin, "lc_no_patience", enabled: true));
    var result = new object[] { true }; Call(plugin, "PatiencePostfix", result);
    Check(!(bool)result[0] && shop.TimerRefreshes == 1 && PlayStyleRules.Current == PlayStyle.Classic, "Patience hook and existing UI refresh preserve play style");
    Call(plugin, "Execute", Command(plugin, "lc_no_patience", enabled: false)); result = new object[] { true }; Call(plugin, "PatiencePostfix", result);
    Check((bool)result[0] && shop.TimerRefreshes == 2, "Off returns native timer logic");
    var stock = new SoftServeStock { Amount = 12 }; var empty = new SoftServeStock(); var foreign = new SoftServeStock { Amount = 20 }; foreign.gameObject.scene = 2;
    Call(plugin, "Execute", Command(plugin, "lc_refill_softserve"));
    Check(stock.Amount == 100 && stock.Restores == 1 && empty.Restores == 0 && foreign.Restores == 0, "Refill preserves empty and other-scene reservoirs");
    Call(plugin, "Execute", Command(plugin, "lc_unlimited_softserve", enabled: true));
    Check(!(bool)Call(plugin, "ConsumePrefix", stock) && (bool)Call(plugin, "ConsumePrefix", foreign), "Stock hook applies only in current shop scene");
    var old = Command(plugin, "lc_money", 99999); var oldSession = Session(plugin);
    GlobalData.Database.ProgressDatabase = new(); Call(plugin, "RefreshContext");
    Check(Session(plugin) != oldSession && (bool)Call(plugin, "ConsumePrefix", stock), "Same database with replaced progress rotates session and clears effects");
    Reject(() => Call(plugin, "Execute", old)); Check(Money.GetMoney() == 12345, "No grant replay after load");
    Call(plugin, "Execute", Command(plugin, "lc_unlimited_softserve", enabled: true)); oldSession = Session(plugin);
    ShopSaveManager.MarkRestorePending(); Call(plugin, "RestorePendingPostfix");
    Check(!(bool)Call(plugin, "ReadyNow") && Session(plugin) != oldSession && (bool)Call(plugin, "ConsumePrefix", stock), "Restore-pending hook invalidates effects immediately");
    Call(plugin, "RefreshContext"); Check(!(bool)Call(plugin, "ReadyNow"), "Restoring not ready");
    Call(plugin, "Execute", Command(plugin, "lc_unlimited_softserve", enabled: false));
    Call(plugin, "Execute", Command(plugin, "disable_all"));
    ShopSaveManager.RestoreComplete = true; Call(plugin, "RefreshContext");
    // Shared-client restoration is an explicit fresh-session command; one-shot grants stay absent.
    Call(plugin, "Execute", Command(plugin, "lc_unlimited_softserve", enabled: true));
    Check(!(bool)Call(plugin, "ConsumePrefix", stock) && Money.Writes == 1, "Fresh toggle restoration does not replay money grants");
    Call(plugin, "Execute", Command(plugin, "disable_all")); Check((bool)Call(plugin, "ConsumePrefix", stock), "Disable all releases stock hook");
    // Poll the real mailbox code twice with the same request and prove at-most-once mutation.
    c = Command(plugin, "lc_money", 900); var path = Path.Combine(commands, c.request_id + ".json");
    File.WriteAllText(path, JsonConvert.SerializeObject(c)); Call(plugin, "PollCommands");
    File.WriteAllText(path, JsonConvert.SerializeObject(c)); Call(plugin, "PollCommands");
    Check(Money.Writes == 2 && !File.Exists(path), "Duplicate request cannot repeat a mutation");
    c = Command(plugin, "lc_money", 1000); c.created_utc -= 10; path = Path.Combine(commands, c.request_id + ".json");
    File.WriteAllText(path, JsonConvert.SerializeObject(c)); Call(plugin, "PollCommands");
    Check(Money.Writes == 2 && !File.Exists(path) && File.Exists(Path.Combine(responses, c.request_id + ".json")), "Expired command receives rejection without mutation");
    c = Command(plugin, "lc_money", 1000); c.session_id = Guid.NewGuid().ToString("N");
    Reject(() => Protocol.Validate(c, c.request_id + ".json", Session(plugin), Protocol.Now()));
    c = Command(plugin, "lc_no_patience"); Reject(() => Protocol.Validate(c, c.request_id + ".json", c.session_id, Protocol.Now()));
    c = Command(plugin, "lc_money", 10); Reject(() => Protocol.Validate(c, Guid.NewGuid().ToString("N") + ".json", c.session_id, Protocol.Now()));
    var extraPlayer = new Player { Shop = shop }; Call(plugin, "RefreshContext");
    Check(!(bool)Call(plugin, "ReadyNow"), "Ambiguous players must fail closed");
    Console.WriteLine($"Livin Cream runtime/protocol: {checks} checks passed. Synthetic engine/game; no installed game launched or saves touched.");
} finally {
    if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe fixture cleanup");
    Directory.Delete(root, true);
}
