using System.Reflection;
using HappyCheats.ProjectHospital;
using Lopital;
using Newtonsoft.Json;

internal static class Program
{
    private static int passed;
    private static readonly string Session = Guid.NewGuid().ToString("N");
    private const long Now = 1800000000;

    private static int Main()
    {
        try
        {
            Check("Menu availability and campaign readiness", MenuAvailability);
            Check("Hospital readiness rejects every incomplete transition", Readiness);
            Check("Money raises low balance without lowering a larger balance", Money);
            Check("A replaced hospital state cannot inherit mutations", HospitalTransition);
            Check("Freeze only intercepts an active loaded hospital", Freeze);
            Check("Staff extension preserves native unlimited settings", Staff);
            Check("Campaign unlock is scoped to campaign categories", Campaign);
            Check("Prestige restores latest natural values without lowering bonuses", Prestige);
            Check("Prestige touches current statistics only across a new day", PrestigeRollover);
            Check("Prestige restoration preserves intervening game or mod changes", PrestigeForeignChanges);
            Check("Disable all clears toggles and restores current prestige", DisableAll);
            Check("Protocol accepts valid actions and timestamp boundaries", ValidCommands);
            Check("Protocol rejects stale, future, wrong-session and malformed requests", InvalidCommands);
            Check("Atomic status writes round-trip and preserve old file on serialization failure", AtomicStatus);
            Check("Malformed command batches drain before a valid request", QueueDrain);
            Check("Memory and durable responses prevent command replay", QueueReplay);
            Check("A response write failure cannot replay an applied command", QueueResponseFailure);
            Check("Wire codec parses escapes and serializes invariant interoperable JSON", JsonRoundtrip);
            Check("Wire codec rejects ambiguous and malformed input", JsonRejects);
            Console.WriteLine($"PASS: {passed} offline Project Hospital scenarios. Game execution and gameplay were not tested.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Reset(); }
    }

    private static void Check(string name, Action test)
    {
        Reset();
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }
    private static void Reset()
    {
        Hospital.sm_instance = null;
        DayTime.sm_instance = null;
        PlayerProfile.sm_instance = null;
        MapEditorController.sm_instance = null;
        RuntimeHooks.Bindings = null;
    }
    private static GameBindings NewBindings() => new(typeof(Hospital).Assembly);
    private static (GameBindings bindings, Hospital hospital) Loaded()
    {
        var hospital = Hospital.sm_instance = new Hospital();
        hospital.m_departments.Add(new Department());
        DayTime.sm_instance = new DayTime();
        PlayerProfile.sm_instance = new PlayerProfile();
        MapEditorController.sm_instance = new MapEditorController { m_initialized = true, m_levelLoaded = true };
        var bindings = NewBindings();
        bindings.ActiveState = bindings.GetLoadedState();
        RuntimeHooks.Bindings = bindings;
        return (bindings, hospital);
    }
    private static void Equal<T>(T expected, T actual, string because = "")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}. {because}");
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void MenuAvailability()
    {
        var bindings = NewBindings();
        Equal(false, bindings.ProfileReady);
        Equal<object>(null, bindings.GetLoadedState());
        PlayerProfile.sm_instance = new PlayerProfile();
        var values = bindings.ReadValues();
        Equal("false", values["hospital_loaded"]);
        Equal("True", values["available_unlock_campaigns"]);
        foreach (var action in new[] { "max_money", "freeze_time", "max_staff", "max_prestige" })
        {
            Equal("False", values["available_" + action]);
            Equal(true, values.ContainsKey("reason_" + action));
        }
        Equal(false, values.ContainsKey("hospital_money"));
        Reject<InvalidOperationException>(() => bindings.MaxMoney());
        Reject<InvalidOperationException>(() => bindings.SetPrestige(true));
    }
    private static void Readiness()
    {
        var (bindings, hospital) = Loaded();
        Equal<object>(hospital.m_state, bindings.GetLoadedState());
        var editor = MapEditorController.sm_instance;
        editor.m_initialized = false; Equal<object>(null, bindings.GetLoadedState()); editor.m_initialized = true;
        editor.m_levelLoaded = false; Equal<object>(null, bindings.GetLoadedState()); editor.m_levelLoaded = true;
        editor.m_unloading = true; Equal<object>(null, bindings.GetLoadedState()); editor.m_unloading = false;
        var budget = hospital.m_state.m_budget;
        hospital.m_state.m_budget = null; Equal<object>(null, bindings.GetLoadedState()); hospital.m_state.m_budget = budget;
        var statistics = hospital.m_state.m_dailyStatistics;
        hospital.m_state.m_dailyStatistics = null; Equal<object>(null, bindings.GetLoadedState()); hospital.m_state.m_dailyStatistics = statistics;
        var time = DayTime.sm_instance;
        DayTime.sm_instance = null; Equal<object>(null, bindings.GetLoadedState()); DayTime.sm_instance = time;
        var data = time.m_persistentData;
        time.m_persistentData = null; Equal<object>(null, bindings.GetLoadedState()); time.m_persistentData = data;
        Hospital.sm_instance = null; Equal<object>(null, bindings.GetLoadedState()); Hospital.sm_instance = hospital;
        MapEditorController.sm_instance = null; Equal<object>(null, bindings.GetLoadedState()); MapEditorController.sm_instance = editor;
        Equal<object>(hospital.m_state, bindings.GetLoadedState());
    }
    private static void Money()
    {
        var (bindings, hospital) = Loaded();
        hospital.m_state.m_budget.m_currentBalance = -125000;
        bindings.MaxMoney();
        Equal(999999999, hospital.m_state.m_budget.m_currentBalance);
        hospital.m_state.m_budget.m_currentBalance = int.MaxValue;
        bindings.MaxMoney();
        Equal(int.MaxValue, hospital.m_state.m_budget.m_currentBalance);
        Equal("2147483647", bindings.ReadValues()["hospital_money"]);
    }
    private static void HospitalTransition()
    {
        var (bindings, hospital) = Loaded();
        bindings.FreezeTime = bindings.MaxStaff = true;
        bindings.SetPrestige(true);
        var oldState = hospital.m_state;
        hospital.m_state = new HospitalPersistentData();
        hospital.m_state.m_budget.m_currentBalance = 100;
        hospital.m_state.m_dailyStatistics.m_reputation = 0.3f;
        Equal(false, bindings.IsCurrentHospitalActive);
        Equal("False", bindings.ReadValues()["available_max_money"]);
        Reject<InvalidOperationException>(() => bindings.MaxMoney());
        Reject<InvalidOperationException>(() => bindings.SetPrestige(true));
        bindings.ApplyPrestige(true);
        Equal(100, hospital.m_state.m_budget.m_currentBalance);
        Equal(0.3f, hospital.m_state.m_dailyStatistics.m_reputation);
        Equal(true, RuntimeHooks.DayTimePrefix());
        bool result = true;
        RuntimeHooks.StaffPostfix(hospital, ref result);
        Equal(true, result);
        bindings.DisableAll();
        Equal(0.3f, hospital.m_state.m_dailyStatistics.m_reputation);
        Equal(1f, oldState.m_dailyStatistics.m_reputation, "Do not mutate departed hospital state.");
    }
    private static void Freeze()
    {
        Equal(true, RuntimeHooks.DayTimePrefix());
        var (bindings, _) = Loaded();
        Equal(true, RuntimeHooks.DayTimePrefix());
        bindings.FreezeTime = true;
        Equal(false, RuntimeHooks.DayTimePrefix());
        MapEditorController.sm_instance.m_unloading = true;
        Equal(true, RuntimeHooks.DayTimePrefix());
        MapEditorController.sm_instance.m_unloading = false;
        bindings.ActiveState = null;
        Equal(true, RuntimeHooks.DayTimePrefix());
    }
    private static void Staff()
    {
        var (bindings, hospital) = Loaded();
        hospital.EmployeeCount = 500;
        bool result = true;
        RuntimeHooks.StaffPostfix(hospital, ref result); Equal(true, result);
        bindings.MaxStaff = true;
        RuntimeHooks.StaffPostfix(hospital, ref result); Equal(false, result);
        hospital.EmployeeCount = 999; result = true;
        RuntimeHooks.StaffPostfix(hospital, ref result); Equal(false, result);
        hospital.EmployeeCount = 1000; result = true;
        RuntimeHooks.StaffPostfix(hospital, ref result); Equal(true, result);
        hospital.EmployeeCount = 1500; result = false;
        RuntimeHooks.StaffPostfix(hospital, ref result); Equal(false, result, "Native unlimited setting must remain unlimited.");
        var unrelated = new Hospital { EmployeeCount = 0 }; result = true;
        RuntimeHooks.StaffPostfix(unrelated, ref result); Equal(true, result);
        Equal("1000", bindings.ReadValues()["hospital_staff_limit"]);
    }
    private static void Campaign()
    {
        var bindings = RuntimeHooks.Bindings = NewBindings();
        PlayerProfile.sm_instance = new PlayerProfile();
        bindings.UnlockCampaigns = true;
        foreach (var category in Enum.GetValues<LevelCategory>())
        {
            bool result = false;
            bool runOriginal = RuntimeHooks.CampaignPrefix(new GameDBCampaignLevel { LevelCategory = category }, ref result);
            bool campaign = category is LevelCategory.CAMPAIGN or LevelCategory.DLC_CAMPAIGN;
            Equal(!campaign, runOriginal);
            Equal(campaign, result);
        }
        bool ignored = false;
        Equal(true, RuntimeHooks.CampaignPrefix(null, ref ignored));
        PlayerProfile.sm_instance = null;
        Equal(true, RuntimeHooks.CampaignPrefix(new GameDBCampaignLevel { LevelCategory = LevelCategory.CAMPAIGN }, ref ignored));
        Equal(false, ignored);
        PlayerProfile.sm_instance = new PlayerProfile();
        bindings.UnlockCampaigns = false;
        Equal(true, RuntimeHooks.CampaignPrefix(new GameDBCampaignLevel { LevelCategory = LevelCategory.CAMPAIGN }, ref ignored));
    }
    private static void Prestige()
    {
        var (bindings, hospital) = Loaded();
        var current = hospital.m_state.m_dailyStatistics;
        var department = hospital.m_departments[0].m_departmentPersistentData.m_todaysStatistics;
        var bonus = new Department(); hospital.m_departments.Add(bonus);
        current.m_reputation = 0.4f; department.m_reputation = 0.65f;
        bonus.m_departmentPersistentData.m_todaysStatistics.m_reputation = 1.25f;
        bindings.SetPrestige(true);
        Equal(1f, current.m_reputation); Equal(1f, department.m_reputation);
        Equal(1.25f, bonus.m_departmentPersistentData.m_todaysStatistics.m_reputation);
        bindings.ApplyPrestige(false);
        bindings.SetPrestige(false);
        Equal(0.4f, current.m_reputation); Equal(0.65f, department.m_reputation);
        bindings.SetPrestige(true);
        current.m_reputation = 0.55f; department.m_reputation = 0.75f;
        RuntimeHooks.StatisticsPostfix(hospital);
        Equal(1f, current.m_reputation);
        bindings.SetPrestige(false);
        Equal(0.55f, current.m_reputation); Equal(0.75f, department.m_reputation);
        Equal(1.25f, bonus.m_departmentPersistentData.m_todaysStatistics.m_reputation);
    }
    private static void PrestigeRollover()
    {
        var (bindings, hospital) = Loaded();
        var department = hospital.m_departments[0].m_departmentPersistentData;
        var historic = new DailyStatistics { m_reputation = 0.2f };
        hospital.m_state.m_statisticsHistory.Add(historic);
        department.m_statistics.Add(historic);
        var yesterday = hospital.m_state.m_dailyStatistics;
        yesterday.m_reputation = 0.35f;
        bindings.SetPrestige(true);
        hospital.m_state.m_dailyStatistics = new DailyStatistics { m_reputation = 0.6f };
        department.m_todaysStatistics = new DailyStatistics { m_reputation = 0.7f };
        bindings.ApplyPrestige(true);
        yesterday.m_reputation = 0.8f;
        bindings.SetPrestige(false);
        Equal(0.6f, hospital.m_state.m_dailyStatistics.m_reputation);
        Equal(0.7f, department.m_todaysStatistics.m_reputation);
        Equal(0.2f, historic.m_reputation);
        Equal(0.8f, yesterday.m_reputation, "No historical snapshot restoration.");
    }
    private static void DisableAll()
    {
        var (bindings, hospital) = Loaded();
        hospital.m_state.m_dailyStatistics.m_reputation = 0.48f;
        bindings.FreezeTime = bindings.MaxStaff = bindings.UnlockCampaigns = true;
        bindings.SetPrestige(true);
        bindings.DisableAll();
        Equal(0.48f, hospital.m_state.m_dailyStatistics.m_reputation);
        Equal(true, bindings.ToggleValues().Values.All(value => !value));
        Equal(true, RuntimeHooks.DayTimePrefix());
    }
    private static void PrestigeForeignChanges()
    {
        var (bindings, hospital) = Loaded();
        var current = hospital.m_state.m_dailyStatistics;
        var department = hospital.m_departments[0].m_departmentPersistentData.m_todaysStatistics;
        current.m_reputation = 0.4f; department.m_reputation = 0.6f;
        bindings.SetPrestige(true);
        current.m_reputation = 0.8f;
        department.m_reputation = 1.3f;
        bindings.SetPrestige(false);
        Equal(0.8f, current.m_reputation);
        Equal(1.3f, department.m_reputation);
        current.m_reputation = 0.35f;
        bindings.SetPrestige(true);
        Equal(1f, current.m_reputation);
        bindings.SetPrestige(false);
        Equal(0.35f, current.m_reputation);
        Equal(1.3f, department.m_reputation);
    }
    private static TrainerCommand Command(string action = "max_money", long time = Now) => new()
    {
        schema_version = 1, session_id = Session, request_id = Guid.NewGuid().ToString("N"), action = action, created_utc = time
    };
    private static void Validate(TrainerCommand command) => Protocol.Validate(command, command.request_id + ".json", Session, Now);
    private static void ValidCommands()
    {
        foreach (var action in Protocol.Actions)
        {
            var command = Command(action);
            if (Protocol.Toggles.Contains(action)) command.enabled = true;
            Validate(command);
            if (Protocol.Toggles.Contains(action)) { command.enabled = false; Validate(command); }
        }
        Validate(Command(time: Now - 30));
        Validate(Command(time: Now + 5));
        var dashed = Command(); dashed.request_id = Guid.NewGuid().ToString("D"); Validate(dashed);
    }
    private static void InvalidCommands()
    {
        Reject<InvalidDataException>(() => Protocol.Validate(null, "invalid.json", Session, Now));
        Reject<InvalidDataException>(() => Validate(Command(time: Now - 31)));
        Reject<InvalidDataException>(() => Validate(Command(time: Now + 6)));
        var command = Command(); command.session_id = Guid.NewGuid().ToString("N"); Reject<InvalidDataException>(() => Validate(command));
        command = Command(); command.schema_version = 2; Reject<InvalidDataException>(() => Validate(command));
        command = Command(); Reject<InvalidDataException>(() => Protocol.Validate(command, Guid.NewGuid().ToString("N") + ".json", Session, Now));
        foreach (var invalidId in new[] { "../request", "", "123", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\n", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" })
        {
            command = Command(); command.request_id = invalidId; Reject<InvalidDataException>(() => Validate(command));
        }
        foreach (var action in Protocol.Toggles) Reject<InvalidDataException>(() => Validate(Command(action)));
        Reject<InvalidDataException>(() => Validate(Command("MAX_MONEY")));
        Reject<InvalidDataException>(() => Validate(Command(null)));
        foreach (var action in new[] { "max_money", "disable_all" })
        {
            command = Command(action); command.enabled = true;
            Reject<InvalidDataException>(() => Validate(command));
        }
    }
    private static void AtomicStatus()
    {
        var folder = Path.Combine(Path.GetTempPath(), "HappyCheats.ProjectHospital.Checks." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "status.json");
            var status = new TrainerStatus { session_id = Session, process_id = 123, message = "测试 / ready", ready = true, updated_utc = Now, bindings_valid = true };
            status.values["hospital_money"] = "999999999";
            status.enabled["max_staff"] = true;
            Protocol.WriteAtomic(path, status);
            var roundtrip = JsonConvert.DeserializeObject<TrainerStatus>(File.ReadAllText(path));
            Equal(Session, roundtrip.session_id); Equal("测试 / ready", roundtrip.message);
            Equal("999999999", roundtrip.values["hospital_money"]); Equal(true, roundtrip.enabled["max_staff"]);
            var bytes = File.ReadAllBytes(path);
            Equal(false, bytes.Length > 2 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            status.message = "replaced"; Protocol.WriteAtomic(path, status);
            Equal("replaced", JsonConvert.DeserializeObject<TrainerStatus>(File.ReadAllText(path)).message);
            var cycle = new Cycle(); cycle.Self = cycle;
            Reject<InvalidDataException>(() => Protocol.WriteAtomic(path, cycle));
            Equal("replaced", JsonConvert.DeserializeObject<TrainerStatus>(File.ReadAllText(path)).message);
            Equal(1, Directory.GetFiles(folder).Length);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
    private static void WithQueue(Action<string, string> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), "HappyCheats.ProjectHospital.QueueChecks." + Guid.NewGuid().ToString("N"));
        var commands = Path.Combine(folder, "commands");
        var responses = Path.Combine(folder, "responses");
        Directory.CreateDirectory(commands); Directory.CreateDirectory(responses);
        try { test(commands, responses); }
        finally { Directory.Delete(folder, recursive: true); }
    }
    private static void QueueDrain()
    {
        WithQueue((commands, responses) =>
        {
            var handled = new HashSet<string>();
            var outcomes = new List<bool>();
            var rejected = new List<string>();
            int executions = 0;
            for (int index = 0; index < 8; index++)
            {
                var stale = Command(time: Protocol.Now() - 300);
                rejected.Add(stale.request_id);
                Protocol.WriteAtomic(Path.Combine(commands, stale.request_id + ".json"), stale);
            }
            CommandQueue.Poll(commands, responses, Session, handled, _ => { executions++; return "executed"; }, (_, success, _) => outcomes.Add(success));
            Equal(0, Directory.GetFiles(commands).Length);
            Equal(8, outcomes.Count); Equal(true, outcomes.All(success => !success)); Equal(0, executions);
            var malformedId = Guid.NewGuid().ToString("N");
            var oversizedId = Guid.NewGuid().ToString("N");
            rejected.Add(malformedId); rejected.Add(oversizedId);
            File.WriteAllText(Path.Combine(commands, malformedId + ".json"), "{not valid json");
            File.WriteAllText(Path.Combine(commands, oversizedId + ".json"), new string('x', 8193));
            CommandQueue.Poll(commands, responses, Session, handled, _ => { executions++; return "executed"; }, (_, success, _) => outcomes.Add(success));
            Equal(0, Directory.GetFiles(commands).Length);
            Equal(10, outcomes.Count); Equal(true, outcomes.All(success => !success)); Equal(0, executions);
            foreach (var id in rejected)
            {
                var response = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(responses, id + ".json")));
                Equal(false, (bool)response["success"]); Equal(id, (string)response["request_id"]);
            }
            var valid = Command(time: Protocol.Now());
            Protocol.WriteAtomic(Path.Combine(commands, valid.request_id + ".json"), valid);
            CommandQueue.Poll(commands, responses, Session, handled, _ => { executions++; return "executed"; }, (_, success, _) => outcomes.Add(success));
            Equal(1, executions); Equal(true, outcomes.Last()); Equal(0, Directory.GetFiles(commands).Length);
        });
    }
    private static void QueueReplay()
    {
        WithQueue((commands, responses) =>
        {
            var handled = new HashSet<string>();
            var command = Command(time: Protocol.Now());
            var path = Path.Combine(commands, command.request_id + ".json");
            var response = Path.Combine(responses, command.request_id + ".json");
            int executions = 0;
            void Poll() => CommandQueue.Poll(commands, responses, Session, handled, _ => { executions++; return "executed"; }, (_, _, _) => { });
            Protocol.WriteAtomic(path, command); Poll(); Equal(1, executions);
            Protocol.WriteAtomic(path, command); Poll(); Equal(1, executions); Equal(false, File.Exists(path));
            handled.Clear(); Protocol.WriteAtomic(path, command); Poll(); Equal(1, executions); Equal(false, File.Exists(path));
            File.Delete(response); Protocol.WriteAtomic(path, command); Poll(); Equal(1, executions); Equal(false, File.Exists(path));
            var failing = Command(time: Protocol.Now());
            path = Path.Combine(commands, failing.request_id + ".json");
            Protocol.WriteAtomic(path, failing);
            CommandQueue.Poll(commands, responses, Session, handled, _ => throw new InvalidOperationException("unavailable hospital"), (_, success, _) => Equal(false, success));
            Equal(false, File.Exists(path));
            var failure = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(responses, failing.request_id + ".json")));
            Equal(false, (bool)failure["success"]);
        });
    }
    private static void QueueResponseFailure()
    {
        WithQueue((commands, responses) =>
        {
            Directory.Delete(responses);
            var handled = new HashSet<string>();
            var command = Command(time: Protocol.Now());
            var path = Path.Combine(commands, command.request_id + ".json");
            Protocol.WriteAtomic(path, command);
            int executions = 0;
            void Poll() => CommandQueue.Poll(commands, responses, Session, handled, _ => { executions++; return "executed"; }, (_, _, _) => { });
            Reject<DirectoryNotFoundException>(Poll);
            Equal(1, executions);
            Equal(true, handled.Contains(command.request_id));
            Directory.CreateDirectory(responses);
            Poll();
            Equal(1, executions);
            Equal(false, File.Exists(path));
        });
    }
    private static void JsonRoundtrip()
    {
        var command = Command("freeze_time"); command.enabled = false;
        var encoded = JsonCodec.Serialize(command);
        var decoded = JsonCodec.ReadCommand(encoded);
        Validate(decoded);
        Equal(command.request_id, decoded.request_id);
        Equal(false, decoded.enabled);
        var escaped = JsonCodec.ReadCommand("{\"action\":\"\\u006dax_money\",\"session_id\":\"\\u6d4b\\u8bd5\\uD83D\\uDE00\",\"request_id\":\"\\\"\\\\\\/\\b\\f\\n\\r\\t\"}");
        Equal("max_money", escaped.action);
        Equal("测试😀", escaped.session_id);
        Equal("\"\\/\b\f\n\r\t", escaped.request_id);
        var controls = "\0\u0001\b\f\n\r\t\"\\测试😀";
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            var json = JsonCodec.Serialize(new { text = controls, value = 1234.75, list = new object[] { true, null, "x", 42 }, dictionary = new Dictionary<string, bool> { ["toggle"] = true } });
            var token = Newtonsoft.Json.Linq.JObject.Parse(json);
            Equal(controls, (string)token["text"]);
            Equal(1234.75, (double)token["value"]);
            Equal(true, (bool)token["list"][0]); Equal(42, (int)token["list"][3]);
            Equal(true, (bool)token["dictionary"]["toggle"]);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }
    private static void JsonRejects()
    {
        foreach (var invalid in new[]
        {
            "", "[]", "null", "{} trailing", "{}{}", "{\"enabled\":true,\"enabled\":false}",
            "{\"unknown\":1}", "{\"enabled\":\"true\"}", "{\"enabled\":1}", "{\"schema_version\":\"1\"}",
            "{\"session_id\":true}", "{\"created_utc\":1.2}", "{\"created_utc\":1e3}", "{\"created_utc\":01}",
            "{\"created_utc\":9223372036854775808}", "{\"created_utc\":-9223372036854775809}",
            "{\"enabled\":[]}", "{\"enabled\":{}}", "{\"action\":\"\\x\"}", "{\"action\":\"\\uZZZZ\"}",
            "{\"action\":\"\\u001\"}", "{\"action\":\"line\nfeed\"}", "{\"action\":\"unfinished}",
            "{\"enabled\":true,}", "{\"enabled\":True}", "{\"created_utc\":+1}", "{\"enabled\":truefalse}"
        }) Reject<InvalidDataException>(() => JsonCodec.ReadCommand(invalid));
        Reject<InvalidDataException>(() => JsonCodec.ReadCommand(null));
        Reject<InvalidDataException>(() => JsonCodec.ReadCommand(new string(' ', 8193)));
        Reject<OverflowException>(() => JsonCodec.ReadCommand("{\"schema_version\":2147483648}"));
        Reject<InvalidDataException>(() => JsonCodec.Serialize(double.NaN));
        Reject<InvalidDataException>(() => JsonCodec.Serialize(double.PositiveInfinity));
        Reject<InvalidDataException>(() => JsonCodec.Serialize(new System.Collections.Hashtable { [1] = "non-string key" }));
    }
    private sealed class Cycle { public Cycle Self { get; set; } }
}
