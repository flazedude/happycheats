using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace HappyCheats.LivinCream;

public sealed class TrainerCommand
{
    public int schema_version;
    public string session_id, request_id, action;
    public long created_utc;
    public bool? enabled;
    public decimal? value;
}

public static class Protocol
{
    public static readonly HashSet<string> Toggles = new HashSet<string> { "lc_no_patience", "lc_unlimited_softserve" };
    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public static void Validate(TrainerCommand command, string file, string session, long now)
    {
        if (command == null || command.schema_version != 1 || command.session_id != session
            || !Guid.TryParseExact(command.request_id, "N", out _)
            || Path.GetFileNameWithoutExtension(file) != command.request_id)
            throw new InvalidDataException("Invalid or stale trainer session/request. Reconnect and retry.");
        if (command.created_utc < now - 8 || command.created_utc > now + 2)
            throw new InvalidDataException("Trainer command expired. Check the value before retrying.");
        if (Toggles.Contains(command.action ?? "")) {
            if (!command.enabled.HasValue || command.value.HasValue) throw new InvalidDataException("A toggle requires an explicit On/Off state.");
            return;
        }
        if (command.enabled.HasValue) throw new InvalidDataException("Unexpected toggle state.");
        switch (command.action) {
            case "lc_money": Number(command.value, 0, 100000000); break;
            case "lc_add_xp": Number(command.value, 1, 10000); break;
            case "lc_add_skills": Number(command.value, 1, 1000); break;
            case "lc_refill_softserve": case "disable_all":
                if (command.value.HasValue) throw new InvalidDataException("Unexpected numeric value.");
                break;
            default: throw new InvalidDataException("Unknown trainer control.");
        }
    }
    private static void Number(decimal? value, decimal min, decimal max)
    {
        if (!value.HasValue || value < min || value > max || decimal.Truncate(value.Value) != value)
            throw new InvalidDataException("Enter a whole number in the control's allowed range.");
    }
    public static void WriteAtomic(string path, object value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temporary, JsonConvert.SerializeObject(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
