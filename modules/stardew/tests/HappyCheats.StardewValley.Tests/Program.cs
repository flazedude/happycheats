using System.Text.Json;
using HappyCheats.StardewValley;

internal static class Program
{
    private const long Now = 1_790_000_000;
    private const string Session = "c49214f524e7455eb8ac880e3f242841";
    private const string Request = "8f00a31e0c4b414c9350d6a6e31673da";
    private static int assertions;

    private static int Main()
    {
        (string Name, Action Run)[] cases =
        [
            ("All action shapes survive the launcher JSON boundary", ActionShapes),
            ("Command identity, schema and session prevent misrouting", CommandIdentity),
            ("Command lifetime rejects delayed and future requests", CommandLifetime),
            ("Numeric requests enforce whole values and documented bounds", NumericBounds),
            ("Clock accepts only valid ten-minute HHMM values", ClockValues),
            ("Malformed JSON types cannot become valid commands", JsonTypes),
            ("Status writes replace complete JSON and clean temporary files", AtomicWrites)
        ];
        int failures = 0;
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
        }
        Console.WriteLine($"{cases.Length - failures}/{cases.Length} groups passed; {assertions} assertions.");
        return failures == 0 ? 0 : 1;
    }

    private static TrainerCommand Command(string action = "stardew_refill", bool? enabled = null, decimal? value = null) => new()
    {
        schema_version = 1, session_id = Session, request_id = Request,
        created_utc = Now, action = action, enabled = enabled, value = value
    };

    private static void Valid(TrainerCommand command, string? filename = null, string session = Session, long now = Now)
    {
        // Exercise the same serialization boundary as the bridge, not just hand-built DTOs.
        var parsed = JsonSerializer.Deserialize<TrainerCommand>(JsonSerializer.Serialize(command));
        Protocol.Validate(parsed, filename ?? Request + ".json", session, now);
        assertions++;
    }

    private static void Invalid(TrainerCommand? command, string reason, string? filename = null, string session = Session, long now = Now)
        => Throws<InvalidDataException>(() => Protocol.Validate(command, filename ?? Request + ".json", session, now), reason);

    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
        assertions++;
    }

    private static void Throws<T>(Action action, string reason) where T : Exception
    {
        try { action(); }
        catch (T) { assertions++; return; }
        throw new Exception(reason);
    }

    private static void ActionShapes()
    {
        string[] toggles = ["stardew_unlimited_health", "stardew_unlimited_energy", "stardew_easy_crafting",
            "stardew_unlimited_items", "stardew_unlimited_water", "stardew_freeze_time"];
        foreach (string action in toggles)
        {
            Valid(Command(action, true));
            Valid(Command(action, false));
            Invalid(Command(action), $"{action} accepted an absent enabled state");
            Invalid(Command(action, true, 1), $"{action} accepted an extra numeric payload");
        }
        foreach (var (action, value) in new (string, int)[] {
            ("stardew_add_money", 10000), ("stardew_set_health", 100), ("stardew_set_max_health", 100),
            ("stardew_set_time", 1200), ("stardew_set_speed", 0) })
        {
            Valid(Command(action, value: value));
            Invalid(Command(action), $"{action} accepted an absent numeric value");
            Invalid(Command(action, false, value), $"{action} accepted an extra false toggle state");
            Invalid(Command(action, true, value), $"{action} accepted an extra true toggle state");
        }
        foreach (string action in new[] { "disable_all", "stardew_refill" })
        {
            Valid(Command(action));
            Invalid(Command(action, false), $"{action} accepted a toggle state");
            Invalid(Command(action, value: 0), $"{action} accepted a numeric payload");
        }
        foreach (string action in new[] { "", "unknown", "STARDEW_REFILL", "stardew_refill " })
            Invalid(Command(action), "Unknown action was accepted: " + action);
    }

    private static void CommandIdentity()
    {
        Invalid(null, "Null document was accepted");
        foreach (int version in new[] { 0, 2, -1, int.MaxValue })
        {
            var command = Command(); command.schema_version = version;
            Invalid(command, "An unsupported schema version was accepted");
        }
        foreach (string id in new[] { "", "not-a-guid", Guid.Parse(Request).ToString("D"), "../" + Request })
        {
            var command = Command(); command.request_id = id;
            Invalid(command, "A malformed request ID was accepted");
        }
        var nullId = Command(); nullId.request_id = null!;
        Invalid(nullId, "A null request ID was accepted");
        Invalid(Command(), "Request filename mismatch was accepted", filename: Session + ".json");
        Valid(Command(), Path.Combine(Path.GetTempPath(), "stardew-commands", Request + ".json"));
        foreach (string? session in new string?[] { "", null, "a53a15ce7367464d8e3f933893ccbd90" })
        {
            var command = Command(); command.session_id = session!;
            Invalid(command, "A stale or missing game session was accepted");
        }
        Invalid(Command(), "A command survived a game-session rotation", session: Guid.NewGuid().ToString("N"));
    }

    private static void CommandLifetime()
    {
        foreach (long age in new long[] { -30, -1, 0, 1, 5 })
        {
            var command = Command(); command.created_utc = Now + age;
            Valid(command);
        }
        foreach (long created in new long[] { Now - 31, Now + 6, 0, long.MinValue, long.MaxValue })
        {
            var command = Command(); command.created_utc = created;
            Invalid(command, "Expired or excessively future-dated command was accepted");
        }
    }

    private static void NumericBounds()
    {
        // Limits form the public command contract; huge values must fail before conversion to int.
        (string Action, int Min, int Max)[] ranges = [
            ("stardew_add_money", 1, 100_000_000), ("stardew_set_health", 1, 10_000),
            ("stardew_set_max_health", 1, 10_000), ("stardew_set_time", 600, 2550),
            ("stardew_set_speed", 0, 10)
        ];
        foreach (var range in ranges)
        {
            Valid(Command(range.Action, value: range.Min));
            Valid(Command(range.Action, value: range.Max));
            Check(Protocol.Number(range.Action, range.Max) == range.Max, "Valid integer payload changed value");
            foreach (decimal value in new decimal[] { range.Min - 1m, range.Max + 1m, range.Min + 0.5m,
                int.MaxValue, (decimal)int.MaxValue + 1, decimal.MaxValue, decimal.MinValue })
                Invalid(Command(range.Action, value: value), $"{range.Action} accepted out-of-contract value {value}");
            Throws<InvalidDataException>(() => Protocol.Number(range.Action, null), "Missing numeric payload was accepted");
        }
        Throws<InvalidDataException>(() => Protocol.Number("unknown", 1), "Unknown numeric control was accepted");
    }

    private static void ClockValues()
    {
        // 06:00 through 25:50 is one game day; 24:00 and 25:00 remain valid after midnight.
        for (int hour = 6; hour <= 25; hour++)
            for (int minute = 0; minute <= 50; minute += 10)
                Valid(Command("stardew_set_time", value: hour * 100 + minute));
        foreach (int time in new[] { 559, 560, 599, 601, 609, 1255, 1260, 1265, 1290, 2360, 2490, 2551, 2560, 2600 })
            Invalid(Command("stardew_set_time", value: time), $"Invalid clock value {time} was accepted");
    }

    private static void JsonTypes()
    {
        var valid = JsonSerializer.SerializeToNode(Command("stardew_add_money", value: 10000))!;
        (string Field, string InvalidJson)[] invalidTypes = [
            ("schema_version", "\"1\""), ("schema_version", "1.5"), ("session_id", "123"),
            ("request_id", "true"), ("action", "[]"), ("created_utc", "\"1790000000\""),
            ("created_utc", "1e100"), ("value", "\"10000\""), ("value", "true"),
            ("value", "1e100"), ("enabled", "1"), ("enabled", "\"true\"")
        ];
        foreach (var (field, invalidJson) in invalidTypes)
        {
            var document = valid.DeepClone(); document[field] = System.Text.Json.Nodes.JsonNode.Parse(invalidJson);
            Throws<JsonException>(() => JsonSerializer.Deserialize<TrainerCommand>(document.ToJsonString()),
                $"Wrong JSON type in {field} was silently coerced");
        }
        Throws<JsonException>(() => JsonSerializer.Deserialize<TrainerCommand>("{broken"), "Malformed JSON was accepted");
        Invalid(JsonSerializer.Deserialize<TrainerCommand>("{}"), "Missing required command fields were accepted");
        Invalid(JsonSerializer.Deserialize<TrainerCommand>("null"), "JSON null was accepted");
    }

    private static void AtomicWrites()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HappyCheats-StardewProtocol-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(directory, "bridge", "status.json");
            Protocol.Write(path, new { ready = false, message = "First status" });
            Protocol.Write(path, new { ready = true, message = "Farm loaded 🌱", value = 100_000_000 });
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Check(doc.RootElement.GetProperty("ready").GetBoolean(), "Status overwrite retained stale readiness");
            Check(doc.RootElement.GetProperty("message").GetString() == "Farm loaded 🌱", "Status text did not roundtrip");
            Check(doc.RootElement.GetProperty("value").GetInt32() == 100_000_000, "Numeric status was corrupted");
            Check(Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "Atomic write left temporary files behind");
        }
        finally
        {
            // This path is generated locally under Temp; no user or command payload controls it.
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
