using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HappyCheats.Core;

public static class ModuleIO
{
    public static string SafePath(string root, string relative)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Path escapes its game directory.");
        for (string? p = path; p != null; p = Path.GetDirectoryName(p))
        {
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked trainer paths are unsupported: " + p);
        }
        return path;
    }
    public static string Hash(string file) { using var s = File.OpenRead(file); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s)); }
    public static JsonElement ReadJson(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new IOException("Trainer status is unexpectedly large.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.Clone();
    }
    public static void WriteJson(string path, object data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(data)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string Text(JsonElement element, string key, string fallback = "") => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var v) ? v.ToString() : fallback;
    public static bool Bool(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    public static JsonElement Object(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var v) ? v : default;
    public static Dictionary<string, bool> Bools(JsonElement value) => value.ValueKind == JsonValueKind.Object ? value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.True) : new();
    public static Dictionary<string, string> Strings(JsonElement value) => value.ValueKind == JsonValueKind.Object ? value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString()) : new();
    public static (int Id, DateTime Started)[] Processes(string directory, string executable)
    {
        var expected = Path.GetFullPath(Path.Combine(directory, executable));
        var found = new List<(int, DateTime)>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try { if (!process.HasExited && string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)) found.Add((process.Id, process.StartTime.ToUniversalTime())); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { throw new IOException("Cannot inspect the running game. Close the game before changing trainer files."); }
            }
        }
        return found.ToArray();
    }
    public static Task LaunchSteam(string appId)
    {
        if (!Regex.IsMatch(appId, "^[0-9]+$")) throw new ArgumentException("Invalid Steam application.");
        Process.Start(new ProcessStartInfo("steam://rungameid/" + appId) { UseShellExecute = true });
        return Task.CompletedTask;
    }
    public static (string Directory, string Build) FindSteam(string appId, string executable, string? manual = null)
    {
        if (!string.IsNullOrWhiteSpace(manual))
        {
            var root = Path.GetFullPath(manual);
            if (!File.Exists(SafePath(root, executable))) throw new IOException("Choose the folder containing " + executable + ".");
            var manifest = Path.Combine(root, "..", "..", "appmanifest_" + appId + ".acf");
            return (root, File.Exists(manifest) ? VdfValue(File.ReadAllText(manifest), "buildid") : "Unknown");
        }
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steam = key?.GetValue("SteamPath") as string;
        if (string.IsNullOrEmpty(steam)) throw new IOException("Steam was not found. Browse to your game installation.");
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam };
        var libraryFile = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile)) foreach (Match m in Regex.Matches(File.ReadAllText(libraryFile), "\"path\"\\s*\"([^\"]+)\"")) libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        foreach (var library in libraries)
        {
            var manifest = Path.Combine(library, "steamapps", "appmanifest_" + appId + ".acf");
            if (!File.Exists(manifest)) continue;
            var text = File.ReadAllText(manifest);
            if (VdfValue(text, "appid") != appId || VdfValue(text, "StateFlags") != "4") continue;
            var install = SafePath(Path.Combine(library, "steamapps", "common"), VdfValue(text, "installdir"));
            if (File.Exists(SafePath(install, executable))) return (install, VdfValue(text, "buildid"));
        }
        throw new IOException("Game not found in your Steam libraries. Browse to the game folder.");
    }
    private static string VdfValue(string text, string key) => Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*\"([^\"]*)\"").Groups[1].Value;
}
