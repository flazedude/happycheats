using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace HappyCheats.ChineseParents
{
    internal sealed class TrainerCommand
    {
        public int schema_version { get; set; }
        public string session_id { get; set; }
        public string request_id { get; set; }
        public string action { get; set; }
        public bool? enabled { get; set; }
        public long created_utc { get; set; }
    }

    internal sealed class TrainerStatus
    {
        public int schema_version = 1;
        public string plugin_version = Plugin.Version;
        public string session_id;
        public int process_id;
        public bool ready;
        public string message;
        public Dictionary<string, string> values = new Dictionary<string, string>();
        public Dictionary<string, bool> enabled = new Dictionary<string, bool>();
        public string last_command;
        public long updated_utc;
        public string assembly_sha256;
        public bool bindings_valid;
        public string initialization_error;
    }

    internal static class Protocol
    {
        internal static readonly HashSet<string> Actions = new HashSet<string>(StringComparer.Ordinal)
        {
            "add_money", "add_knowledge", "refill_energy", "clear_stress", "clear_shadow",
            "add_attributes", "add_face", "unlimited_energy", "no_stress", "disable_all"
        };

        public static void Validate(TrainerCommand command, string filename, string session, long now)
        {
            if (command == null || command.schema_version != 1) throw new InvalidDataException("Unsupported command schema.");
            Guid request;
            if (!Guid.TryParseExact(command.request_id, "N", out request) && !Guid.TryParseExact(command.request_id, "D", out request))
                throw new InvalidDataException("request_id must be a GUID.");
            if (!string.Equals(Path.GetFileNameWithoutExtension(filename), command.request_id, StringComparison.Ordinal))
                throw new InvalidDataException("Command filename and request_id differ.");
            if (!string.Equals(command.session_id, session, StringComparison.Ordinal)) throw new InvalidDataException("Command belongs to another game session.");
            if (command.created_utc < now - 30 || command.created_utc > now + 5) throw new InvalidDataException("Command has expired or is dated in the future.");
            if (!Actions.Contains(command.action ?? "")) throw new InvalidDataException("Unknown trainer action.");
            if ((command.action == "unlimited_energy" || command.action == "no_stress") && !command.enabled.HasValue)
                throw new InvalidDataException("Toggle commands require enabled=true or false.");
        }

        public static long Now() { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }

        public static void WriteAtomic(string path, object value)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(value, Formatting.Indented), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
