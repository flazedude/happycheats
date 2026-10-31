using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HappyCheats.ProjectHospital
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
        public string version = Plugin.Version;
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
        internal static readonly HashSet<string> Toggles = new HashSet<string>(new[] { "freeze_time", "max_staff", "unlock_campaigns", "max_prestige" }, StringComparer.Ordinal);
        internal static readonly HashSet<string> Actions = new HashSet<string>(new[] { "max_money", "freeze_time", "max_staff", "unlock_campaigns", "max_prestige", "disable_all" }, StringComparer.Ordinal);
        internal static bool IsGuid(string value)
        {
            if (value == null || (value.Length != 32 && value.Length != 36)) return false;
            try { var parsed = new Guid(value); return parsed.ToString("N") == value || parsed.ToString("D") == value; }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
        }
        internal static void Validate(TrainerCommand command, string filename, string session, long now)
        {
            if (command == null || command.schema_version != 1) throw new IOException("Unsupported command schema.");
            if (!IsGuid(command.request_id)) throw new IOException("request_id must be a GUID.");
            if (!string.Equals(Path.GetFileNameWithoutExtension(filename), command.request_id, StringComparison.Ordinal)) throw new IOException("Command filename and request_id differ.");
            if (!string.Equals(command.session_id, session, StringComparison.Ordinal)) throw new IOException("Command belongs to another game session.");
            if (command.created_utc < now - 30 || command.created_utc > now + 5) throw new IOException("Command has expired or is dated in the future.");
            if (!Actions.Contains(command.action ?? "")) throw new IOException("Unknown trainer action.");
            if (Toggles.Contains(command.action) && !command.enabled.HasValue) throw new IOException("Toggle commands require enabled=true or false.");
            if (!Toggles.Contains(command.action) && command.enabled.HasValue) throw new IOException("One-shot actions do not accept a toggle state.");
        }
        internal static long Now() { return (DateTime.UtcNow.Ticks - 621355968000000000L) / TimeSpan.TicksPerSecond; }
        internal static void WriteAtomic(string path, object value)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonCodec.Serialize(value), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    // Kept independent of Unity so malformed/stale queue cleanup and duplicate delivery can be tested offline.
    internal static class CommandQueue
    {
        internal static void Poll(string commandsPath, string responsesPath, string session, HashSet<string> handled,
            Func<TrainerCommand, string> execute, Action<string, bool, string> observed)
        {
            if (!Directory.Exists(commandsPath)) return;
            foreach (var path in Directory.GetFiles(commandsPath, "*.json").Where(p => Protocol.IsGuid(Path.GetFileNameWithoutExtension(p))).OrderBy(File.GetCreationTimeUtc).Take(8))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                if (handled.Contains(id) || File.Exists(Path.Combine(responsesPath, id + ".json"))) { handled.Add(id); TryDelete(path); continue; }
                string raw;
                // Only read-sharing failures retry. Invalid commands always get a terminal response.
                try { raw = new FileInfo(path).Length > 8192 ? null : File.ReadAllText(path); }
                catch (IOException) { continue; }
                var success = false;
                string result;
                try
                {
                    if (raw == null) throw new IOException("Command exceeds the size limit.");
                    var command = JsonCodec.ReadCommand(raw);
                    Protocol.Validate(command, path, session, Protocol.Now());
                    result = execute(command);
                    success = true;
                }
                catch (Exception exception) { result = exception.GetType().Name + ": " + exception.Message; }
                handled.Add(id); // Mark before any response write, so failed disk writes cannot replay mutations.
                Protocol.WriteAtomic(Path.Combine(responsesPath, id + ".json"), new { schema_version = 1, request_id = id, session_id = session, success, message = result, updated_utc = Protocol.Now() });
                TryDelete(path);
                observed(id, success, result);
            }
        }
        private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
