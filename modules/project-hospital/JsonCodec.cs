using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace HappyCheats.ProjectHospital
{
    // The game uses a stripped .NET 3.5 Mono profile. Keep this small wire codec on mscorlib only;
    // full JSON libraries reference System.Data/Runtime.Serialization which the game does not ship.
    internal static class JsonCodec
    {
        internal static string Serialize(object value)
        {
            var output = new StringBuilder();
            Write(output, value, new List<object>());
            return output.ToString();
        }
        private static void Write(StringBuilder output, object value, List<object> stack)
        {
            if (value == null) { output.Append("null"); return; }
            if (value is string) { Quote(output, (string)value); return; }
            if (value is bool) { output.Append((bool)value ? "true" : "false"); return; }
            if (value is int || value is long || value is short || value is byte || value is uint || value is ulong || value is decimal)
            { output.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
            if (value is float || value is double)
            {
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new IOException("Non-finite JSON number.");
                output.Append(number.ToString("R", CultureInfo.InvariantCulture)); return;
            }
            if (stack.Count >= 32 || stack.Contains(value)) throw new IOException("Cyclic or over-nested JSON value.");
            stack.Add(value);
            try
            {
                var dictionary = value as IDictionary;
                if (dictionary != null)
                {
                    output.Append('{'); var first = true;
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (!(entry.Key is string)) throw new IOException("JSON object keys must be strings.");
                        if (!first) output.Append(','); first = false;
                        Quote(output, (string)entry.Key); output.Append(':'); Write(output, entry.Value, stack);
                    }
                    output.Append('}'); return;
                }
                var sequence = value as IEnumerable;
                if (sequence != null)
                {
                    output.Append('['); var first = true;
                    foreach (var item in sequence) { if (!first) output.Append(','); first = false; Write(output, item, stack); }
                    output.Append(']'); return;
                }
                output.Append('{'); var initial = true;
                foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!initial) output.Append(','); initial = false;
                    Quote(output, field.Name); output.Append(':'); Write(output, field.GetValue(value), stack);
                }
                foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                    if (!initial) output.Append(','); initial = false;
                    Quote(output, property.Name); output.Append(':'); Write(output, property.GetValue(value, null), stack);
                }
                output.Append('}');
            }
            finally { stack.RemoveAt(stack.Count - 1); }
        }
        private static void Quote(StringBuilder output, string value)
        {
            output.Append('"');
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\f': output.Append("\\f"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default:
                        if (ch < 32 || char.IsSurrogate(ch)) output.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else output.Append(ch);
                        break;
                }
            }
            output.Append('"');
        }
        internal static TrainerCommand ReadCommand(string json)
        {
            var reader = new Reader(json);
            var values = reader.Object();
            var command = new TrainerCommand();
            foreach (var pair in values)
            {
                switch (pair.Key)
                {
                    case "schema_version": command.schema_version = checked((int)Integer(pair.Value)); break;
                    case "created_utc": command.created_utc = Integer(pair.Value); break;
                    case "session_id": command.session_id = Text(pair.Value); break;
                    case "request_id": command.request_id = Text(pair.Value); break;
                    case "action": command.action = Text(pair.Value); break;
                    case "enabled":
                        if (pair.Value != null && !(pair.Value is bool)) throw new IOException("enabled must be boolean or null.");
                        command.enabled = (bool?)pair.Value; break;
                    default: throw new IOException("Unknown command field: " + pair.Key);
                }
            }
            return command;
        }
        private static long Integer(object value) { if (!(value is long)) throw new IOException("Expected integer command field."); return (long)value; }
        private static string Text(object value) { if (value != null && !(value is string)) throw new IOException("Expected string command field."); return (string)value; }

        private sealed class Reader
        {
            private readonly string text;
            private int offset;
            internal Reader(string text) { if (text == null || text.Length > 8192) throw new IOException("Invalid command length."); this.text = text; }
            private void Space() { while (offset < text.Length && (text[offset] == ' ' || text[offset] == '\t' || text[offset] == '\r' || text[offset] == '\n')) offset++; }
            private bool Take(char ch) { Space(); if (offset < text.Length && text[offset] == ch) { offset++; return true; } return false; }
            private void Need(char ch) { if (!Take(ch)) throw new IOException("Malformed command JSON."); }
            internal Dictionary<string, object> Object()
            {
                var values = new Dictionary<string, object>(StringComparer.Ordinal);
                Need('{');
                if (!Take('}'))
                {
                    do
                    {
                        var key = String(); Need(':');
                        if (values.ContainsKey(key)) throw new IOException("Duplicate command field.");
                        values.Add(key, Value());
                        if (Take('}')) break;
                        Need(',');
                    } while (true);
                }
                Space(); if (offset != text.Length) throw new IOException("Trailing command JSON data.");
                return values;
            }
            private object Value()
            {
                Space(); if (offset >= text.Length) throw new IOException("Missing command field value.");
                if (text[offset] == '"') return String();
                if (Literal("true")) return true;
                if (Literal("false")) return false;
                if (Literal("null")) return null;
                var start = offset;
                if (text[offset] == '-') offset++;
                if (offset >= text.Length || text[offset] < '0' || text[offset] > '9') throw new IOException("Expected primitive command field value.");
                if (text[offset] == '0') offset++;
                else while (offset < text.Length && text[offset] >= '0' && text[offset] <= '9') offset++;
                long number;
                if (!long.TryParse(text.Substring(start, offset - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number)) throw new IOException("Command integer overflow.");
                return number;
            }
            private bool Literal(string value)
            {
                if (text.Length - offset < value.Length || string.CompareOrdinal(text, offset, value, 0, value.Length) != 0) return false;
                offset += value.Length; return true;
            }
            private string String()
            {
                Need('"'); var value = new StringBuilder();
                while (offset < text.Length)
                {
                    var ch = text[offset++];
                    if (ch == '"') return value.ToString();
                    if (ch < 32) throw new IOException("Unescaped JSON control character.");
                    if (ch != '\\') { value.Append(ch); continue; }
                    if (offset >= text.Length) throw new IOException("Incomplete JSON escape.");
                    switch (text[offset++])
                    {
                        case '"': value.Append('"'); break;
                        case '\\': value.Append('\\'); break;
                        case '/': value.Append('/'); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'u':
                            int code;
                            if (text.Length - offset < 4 || !int.TryParse(text.Substring(offset, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)) throw new IOException("Invalid JSON Unicode escape.");
                            value.Append((char)code); offset += 4; break;
                        default: throw new IOException("Unknown JSON escape.");
                    }
                }
                throw new IOException("Unterminated JSON string.");
            }
        }
    }
}
