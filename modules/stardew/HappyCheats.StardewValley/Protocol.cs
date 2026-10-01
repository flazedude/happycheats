using System.Text.Json;

namespace HappyCheats.StardewValley;

internal sealed class TrainerCommand
{
    public int schema_version { get; set; }
    public string session_id { get; set; } = "";
    public string request_id { get; set; } = "";
    public string action { get; set; } = "";
    public bool? enabled { get; set; }
    public decimal? value { get; set; }
    public long created_utc { get; set; }
}

internal static class Protocol
{
    internal static readonly string[] ToggleIds = {
        "stardew_unlimited_health", "stardew_unlimited_energy", "stardew_easy_crafting",
        "stardew_unlimited_items", "stardew_unlimited_water", "stardew_freeze_time"
    };
    internal static readonly IReadOnlyDictionary<string, (int Min, int Max, int Default)> Numbers =
        new Dictionary<string, (int, int, int)> {
            ["stardew_add_money"] = (1, 100_000_000, 10_000),
            ["stardew_set_health"] = (1, 10_000, 100),
            ["stardew_set_max_health"] = (1, 10_000, 100),
            ["stardew_set_time"] = (600, 2550, 1200),
            ["stardew_set_speed"] = (0, 10, 0)
        };
    internal static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    internal static int Number(string action, decimal? value)
    {
        if (!Numbers.TryGetValue(action, out var range)) throw new InvalidDataException("Unknown numeric trainer control.");
        if (!value.HasValue || value.Value != decimal.Truncate(value.Value) || value < range.Min || value > range.Max)
            throw new InvalidDataException($"Enter a whole number between {range.Min} and {range.Max}.");
        int number = (int)value.Value;
        if (action == "stardew_set_time" && (number % 100 > 50 || number % 10 != 0))
            throw new InvalidDataException("Time must use HHMM in ten-minute steps, from 0600 to 2550.");
        return number;
    }
    internal static void Validate(TrainerCommand? command, string filename, string session, long now)
    {
        if (command is null || command.schema_version != 1) throw new InvalidDataException("Unsupported command schema.");
        if (!Guid.TryParseExact(command.request_id, "N", out _) || Path.GetFileNameWithoutExtension(filename) != command.request_id)
            throw new InvalidDataException("Invalid command request ID.");
        if (command.session_id != session) throw new InvalidDataException("Command belongs to another game session.");
        if (command.created_utc < now - 30 || command.created_utc > now + 5) throw new InvalidDataException("Command has expired or is dated in the future.");
        if (ToggleIds.Contains(command.action)) {
            if (!command.enabled.HasValue || command.value.HasValue) throw new InvalidDataException("A toggle requires only an enabled state.");
        } else if (Numbers.ContainsKey(command.action)) {
            if (command.enabled.HasValue) throw new InvalidDataException("A numeric control does not accept a toggle state.");
            Number(command.action, command.value);
        } else if (command.action is "disable_all" or "stardew_refill") {
            if (command.enabled.HasValue || command.value.HasValue) throw new InvalidDataException("This control does not accept a value.");
        } else throw new InvalidDataException("Unknown trainer control.");
    }
    internal static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
