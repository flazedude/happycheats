using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Generates an isolated override from a verified local game script; never redistributes or edits the base script.</summary>
public static class Witcher3Installer
{
    public const string Executable = "bin/x64_dx12/witcher3.exe";
    public const string SupportedExecutableSha256 = "C272B2C2E61F84C758E28FAB69AB2915944DD1E539DBB435FAE9FC67494C7E25";
    public const string VanillaScript = "content/content0/scripts/game/r4game.ws";
    public const string SupportedVanillaSha256 = "D800CAAF40FFF71901F7C056B2FA1397B573710563A27EF766E466D5A2469431";
    public const string ModRoot = "Mods/modHappyCheats/content/";
    public const string MainScript = ModRoot + "scripts/local/happycheats.ws";
    public const string PatchedScript = ModRoot + "scripts/game/r4game.ws";
    public const string BridgeDepotPath = "happycheats_witcher3";
    public const string BridgeRoot = "content/content0/" + BridgeDepotPath;
    public const string CommandPath = BridgeRoot + "/command.csv";
    public const string LeasePath = BridgeRoot + "/lease.csv";
    public const string StatusPath = "bin/config/base/modsHappyCheatsWitcher3.ini";
    public const string ReceiptPath = "happycheats-witcher-3-install.json";
    public const string TemplatePath = "happycheats.ws";
    public sealed record Entry(string Path, string Sha256);
    public sealed record Receipt(int SchemaVersion, string Module, List<Entry> Files);
    internal sealed record BuildProfile(string ExecutableSha256, string VanillaSha256);
    internal static readonly BuildProfile SupportedBuild = new(SupportedExecutableSha256, SupportedVanillaSha256);
    private sealed record Change(string Relative, byte[]? Before, string? AfterHash);
    private sealed record Generated(string Relative, byte[] Bytes);
    private static readonly string[] InstalledFiles = { MainScript, PatchedScript };
    private static readonly string[] DynamicFiles = { CommandPath, LeasePath, StatusPath };
    internal const string LegacyCommandPath = ModRoot + BridgeDepotPath + "/command.csv";
    internal const string LegacyLeasePath = ModRoot + BridgeDepotPath + "/lease.csv";
    private static readonly string[] LegacyDynamicFiles = { LegacyCommandPath, LegacyLeasePath };
    private static bool IsDynamic(string relative) => DynamicFiles.Contains(relative, StringComparer.Ordinal) || LegacyDynamicFiles.Contains(relative, StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly UTF8Encoding Utf8 = new(false, true);

    private static List<Entry> Validate(IEnumerable<Entry> entries, bool package)
    {
        var files = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry == null || entry.Path == null || !(package ? entry.Path == TemplatePath : InstalledFiles.Contains(entry.Path, StringComparer.Ordinal) || IsDynamic(entry.Path))
                || !seen.Add(entry.Path) || entry.Sha256 == null || !Regex.IsMatch(entry.Sha256, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant))
                throw new IOException("Invalid The Witcher 3 package or ownership record.");
            files.Add(entry);
        }
        return files;
    }

    public static IReadOnlyList<Entry> ReadPackage(string package)
    {
        var json = ReadJson(SafePath(package, "package.json"));
        if (Text(json, "schemaVersion") != "1" || Text(json, "game") != "witcher-3" || Text(json, "version") != Witcher3Module.TrainerVersion)
            throw new IOException("Unsupported The Witcher 3 package identity.");
        var array = Object(json, "files");
        if (array.ValueKind != JsonValueKind.Array) throw new IOException("Missing The Witcher 3 package files.");
        var files = Validate(array.EnumerateArray().Select(file => new Entry(Text(file, "path"), Text(file, "sha256"))), true);
        if (files.Count != 1 || files[0].Path != TemplatePath) throw new IOException("The Witcher 3 package is missing its trainer script.");
        var path = SafePath(package, TemplatePath);
        if (!Matches(path, files[0].Sha256) || new FileInfo(path).Length == 0) throw new IOException("The Witcher 3 payload checksum failed.");
        return files;
    }

    public static bool Installed(string root) => InstalledFiles.Concat(DynamicFiles).All(path => File.Exists(SafePath(root, path)));
    public static bool MatchesPackage(string package, string root) => MatchesPackage(package, root, SupportedBuild);
    internal static bool MatchesPackage(string package, string root, BuildProfile profile) => Installed(root) && Generate(package, root, profile)
        .All(file => Matches(SafePath(root, file.Relative), HashBytes(file.Bytes)));

    public static void VerifySupportedGame(string root) => VerifySupportedGame(root, SupportedBuild);
    private static byte[] VerifySupportedGame(string root, BuildProfile profile)
    {
        if (!Matches(SafePath(root, Executable), profile.ExecutableSha256))
            throw new IOException("Unsupported The Witcher 3 executable. This trainer requires the verified Steam Remastered 5.0 DirectX 12 build.");
        var path = SafePath(root, VanillaScript);
        var bytes = File.ReadAllBytes(path);
        if (!string.Equals(HashBytes(bytes), profile.VanillaSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The Witcher 3 base script is modified or unsupported. Existing game files were preserved.");
        return bytes;
    }

    private static List<Generated> Generate(string package, string root, BuildProfile profile)
    {
        var entries = ReadPackage(package);
        var templateBytes = File.ReadAllBytes(SafePath(package, TemplatePath));
        if (!string.Equals(HashBytes(templateBytes), entries[0].Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The Witcher 3 payload changed during installation.");
        var original = Utf8.GetString(VerifySupportedGame(root, profile));
        var classPattern = new Regex(@"\bimport\s+class\s+CR4Game\s+extends\s+CCommonGame\s*\{", RegexOptions.CultureInvariant);
        var tickPattern = new Regex(@"\bpublic\s+function\s+OnTick\s*\(\s*\)\s*\{", RegexOptions.CultureInvariant);
        if (classPattern.Matches(original).Count != 1 || tickPattern.Matches(original).Count != 1 || original.Contains("happyCheatsWitcher3", StringComparison.Ordinal))
            throw new IOException("The verified Witcher 3 script does not have the expected integration points.");
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var patched = classPattern.Replace(original, match => match.Value + newline + "\tprivate var happyCheatsWitcher3 : HappyCheatsWitcher3;", 1);
        patched = tickPattern.Replace(patched, match => match.Value + newline
            + "\t\tif(!happyCheatsWitcher3)" + newline
            + "\t\t\thappyCheatsWitcher3 = new HappyCheatsWitcher3 in this;" + newline
            + "\t\thappyCheatsWitcher3.Tick();", 1);
        var template = Utf8.GetString(templateBytes);
        if (!template.Contains("__HC_BRIDGE_PATH__", StringComparison.Ordinal)) throw new IOException("The Witcher 3 script template has no bridge placeholder.");
        SafePath(root, BridgeRoot);
        // LoadCSVDirect's physical depot is content/content0; mod content directories are not searched.
        return new() { new(MainScript, Utf8.GetBytes(template.Replace("__HC_BRIDGE_PATH__", BridgeDepotPath, StringComparison.Ordinal))), new(PatchedScript, Utf8.GetBytes(patched)) };
    }

    private static IEnumerable<Generated> InitialBridgeFiles() => new Generated[] {
        new(CommandPath, Utf8.GetBytes("schema,session,request,action,enabled,issued\n")),
        new(LeasePath, Utf8.GetBytes("session,issued\n")),
        new(StatusPath, Utf8.GetBytes("[MODS]\nSnapshot=HCW3|pending\n"))
    };

    private static bool RecognizedBridge(string relative, byte[] bytes)
    {
        if (bytes.Length > 65536) return false;
        string content;
        try
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Utf8, detectEncodingFromByteOrderMarks: true);
            content = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException) { return false; }
        var lines = content.TrimEnd('\n').Split('\n');
        if (relative == StatusPath)
            return lines.Length == 2 && lines[0].Equals("[MODS]", StringComparison.OrdinalIgnoreCase) && lines[1].StartsWith("Snapshot=HCW3|", StringComparison.Ordinal)
                && !lines[1].Contains('\0');
        var command = relative is CommandPath or LegacyCommandPath;
        var header = command ? "schema,session,request,action,enabled,issued" : "session,issued";
        return lines.Length is 1 or 2 && lines[0] == header && (lines.Length == 1 || (lines[1].Length > 0 && !lines[1].Contains('\0')
            && lines[1].Split(',').Length == (command ? 6 : 2)));
    }

    private static Receipt? ReadReceipt(byte[]? bytes)
    {
        if (bytes == null) return null;
        if (bytes.Length > 1024 * 1024) throw new IOException("The Witcher 3 ownership receipt is unexpectedly large.");
        var receipt = JsonSerializer.Deserialize<Receipt>(bytes, Json);
        if (receipt?.SchemaVersion != 1 || receipt.Module != "witcher-3" || receipt.Files == null) throw new IOException("Invalid The Witcher 3 ownership receipt.");
        return receipt with { Files = Validate(receipt.Files, false) };
    }

    private static byte[]? ReadOptional(string root, string relative)
    {
        var path = SafePath(root, relative);
        if (Directory.Exists(path)) throw new IOException("A Witcher 3 trainer file path is a directory: " + relative);
        if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) throw new IOException("A Witcher 3 trainer file is unexpectedly large; it was preserved.");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static void CheckConflicts(string root)
    {
        var mods = SafePath(root, "Mods");
        if (!Directory.Exists(mods)) return;
        var pending = new Stack<string>();
        pending.Push(mods);
        while (pending.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                var path = SafePath(root, relative); // Do not follow linked mods or permit an override outside the game root.
                if (Directory.Exists(path)) { pending.Push(path); continue; }
                if (Path.GetFileName(path).Equals("r4game.ws", StringComparison.OrdinalIgnoreCase)
                    && !relative.Equals(PatchedScript, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Another Witcher 3 mod overrides r4game.ws. Disable or remove the conflicting override before installing HappyCheats: " + relative);
            }
    }

    public static void Install(string package, string root, Action checkGameClosed, IProgress<string>? progress = null) =>
        Install(package, root, checkGameClosed, progress, SupportedBuild);

    internal static void Install(string package, string root, Action checkGameClosed, IProgress<string>? progress, BuildProfile profile)
    {
        checkGameClosed();
        var files = Generate(package, root, profile);
        // Initialize every script-read mailbox so a fresh install never loads a missing CSV.
        files.InsertRange(0, InitialBridgeFiles());
        CheckConflicts(root);
        var receiptBefore = ReadOptional(root, ReceiptPath);
        var receipt = ReadReceipt(receiptBefore);
        var owned = receipt?.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase) ?? new();
        var nextOwned = receipt?.Files.ToList() ?? new List<Entry>();
        var plans = new List<(Generated File, byte[]? Before)>();
        var legacyPlans = new List<(string Relative, byte[] Before)>();
        foreach (var file in files)
        {
            var before = ReadOptional(root, file.Relative);
            var dynamic = DynamicFiles.Contains(file.Relative, StringComparer.Ordinal);
            if (before != null && dynamic && (!owned.ContainsKey(file.Relative) || !RecognizedBridge(file.Relative, before)))
                throw new IOException("Preserved an existing or modified Witcher 3 bridge file: " + file.Relative);
            if (before != null && HashBytes(before) == HashBytes(file.Bytes)) continue; // Identical foreign scripts never become owned.
            if (before != null && !dynamic && (!owned.TryGetValue(file.Relative, out var prior) || !string.Equals(HashBytes(before), prior.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Preserved an existing or modified Witcher 3 mod file: " + file.Relative);
            plans.Add((file, before));
            nextOwned.RemoveAll(prior => prior.Path == file.Relative);
            nextOwned.Add(new(file.Relative, HashBytes(file.Bytes)));
        }
        // Reset the new mailboxes rather than carrying a stale command across the upgrade.
        // Only retire legacy files recognized as our own; unexpected edits remain owned and preserved.
        foreach (var relative in LegacyDynamicFiles.Where(owned.ContainsKey))
        {
            var before = ReadOptional(root, relative);
            if (before != null && !RecognizedBridge(relative, before)) continue;
            if (before != null) legacyPlans.Add((relative, before));
            nextOwned.RemoveAll(prior => prior.Path == relative);
        }
        var changes = new List<Change>();
        try
        {
            // Commit the hook after the class it references, while repeatedly confirming the game remains closed.
            foreach (var plan in plans)
            {
                Replace(root, plan.File.Relative, plan.File.Bytes, plan.Before, checkGameClosed, changes);
                progress?.Report("Installing " + Path.GetFileName(plan.File.Relative));
            }
            foreach (var plan in legacyPlans)
            {
                checkGameClosed();
                var path = SafePath(root, plan.Relative);
                if (!Matches(path, HashBytes(plan.Before))) throw new IOException("A Witcher 3 legacy bridge file changed during migration; it was preserved.");
                File.Delete(path);
                changes.Add(new(plan.Relative, plan.Before, null));
            }
            checkGameClosed();
            CheckConflicts(root);
            VerifySupportedGame(root, profile);
            Directory.CreateDirectory(SafePath(root, BridgeRoot));
            Replace(root, ReceiptPath, JsonSerializer.SerializeToUtf8Bytes(new Receipt(1, "witcher-3", nextOwned.OrderBy(file => file.Path, StringComparer.Ordinal).ToList())), receiptBefore, checkGameClosed, changes);
        }
        catch (Exception failure) { Rollback(root, changes, failure); throw; }
        progress?.Report("The Witcher 3 trainer installed. Start the game and load a save. Gameplay validation is pending.");
    }

    public static void Remove(string root, Action checkGameClosed, IProgress<string>? progress = null)
    {
        checkGameClosed();
        var receiptBefore = ReadOptional(root, ReceiptPath);
        var receipt = ReadReceipt(receiptBefore) ?? throw new IOException("No Witcher 3 ownership receipt was found. Existing files were preserved.");
        var owned = receipt.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var preserved = InstalledFiles.Where(relative => File.Exists(SafePath(root, relative))
            && (!owned.TryGetValue(relative, out var entry) || !Matches(SafePath(root, relative), entry.Sha256))).ToArray();
        // The class and OnTick override depend on each other. Preserve the pair when either file belongs to someone else or was edited.
        if (preserved.Length > 0)
        {
            progress?.Report("Modified or unowned The Witcher 3 scripts were preserved together: " + string.Join(", ", preserved));
            return;
        }
        var changes = new List<Change>();
        var remaining = receipt.Files.ToList();
        var preservedBridge = new List<string>();
        try
        {
            foreach (var entry in receipt.Files.OrderBy(file => file.Path == PatchedScript ? 0 : 1))
            {
                var before = ReadOptional(root, entry.Path);
                if (before == null) { remaining.Remove(entry); continue; }
                var dynamic = IsDynamic(entry.Path);
                if (dynamic && !RecognizedBridge(entry.Path, before)) { preservedBridge.Add(entry.Path); continue; }
                checkGameClosed();
                var target = SafePath(root, entry.Path);
                if ((!dynamic && !string.Equals(HashBytes(before), entry.Sha256, StringComparison.OrdinalIgnoreCase)) || !Matches(target, HashBytes(before)))
                    throw new IOException("A Witcher 3 script changed during removal. Its files were preserved.");
                File.Delete(target);
                changes.Add(new(entry.Path, before, null));
                remaining.Remove(entry);
            }
            Replace(root, ReceiptPath, JsonSerializer.SerializeToUtf8Bytes(new Receipt(1, "witcher-3", remaining)), receiptBefore, checkGameClosed, changes);
        }
        catch (Exception failure) { Rollback(root, changes, failure); throw; }
        progress?.Report(preservedBridge.Count == 0 ? "The Witcher 3 trainer removed. Unowned files were preserved."
            : "The Witcher 3 trainer removed. Modified bridge files were preserved: " + string.Join(", ", preservedBridge));
    }

    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool Matches(string path, string hash) => File.Exists(path) && string.Equals(Hash(path), hash, StringComparison.OrdinalIgnoreCase);

    private static void Replace(string root, string relative, byte[] bytes, byte[]? before, Action checkGameClosed, List<Change> changes)
    {
        checkGameClosed();
        var path = SafePath(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryRelative = relative + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(SafePath(root, temporaryRelative), FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
            checkGameClosed();
            path = SafePath(root, relative);
            var temporary = SafePath(root, temporaryRelative);
            if (!Matches(temporary, HashBytes(bytes)) || (before == null ? File.Exists(path) || Directory.Exists(path) : !Matches(path, HashBytes(before))))
                throw new IOException("A Witcher 3 file changed before installation commit; it was preserved.");
            File.Move(temporary, path, before != null);
            changes.Add(new(relative, before, HashBytes(bytes)));
        }
        finally
        {
            var temporary = SafePath(root, temporaryRelative);
            if (File.Exists(temporary) && Matches(temporary, HashBytes(bytes))) File.Delete(temporary);
        }
    }

    private static void Rollback(string root, List<Change> changes, Exception failure)
    {
        var errors = new List<Exception> { failure };
        foreach (var change in changes.AsEnumerable().Reverse())
        {
            try
            {
                var path = SafePath(root, change.Relative);
                var unchanged = change.AfterHash == null ? !File.Exists(path) && !Directory.Exists(path) : Matches(path, change.AfterHash);
                if (!unchanged)
                {
                    if (change.Before != null)
                    {
                        var recovery = SafePath(root, "happycheats-recovery/" + Guid.NewGuid().ToString("N") + "/" + Path.GetFileName(path));
                        Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
                        File.WriteAllBytes(recovery, change.Before);
                        errors.Add(new IOException("A changed file was preserved. Its previous version was saved to " + recovery));
                    }
                    continue;
                }
                if (change.Before == null) File.Delete(path);
                else File.WriteAllBytes(path, change.Before);
            }
            catch (Exception rollback) { errors.Add(rollback); }
        }
        if (errors.Count > 1) throw new AggregateException("The Witcher 3 operation failed; review recovery details.", errors);
    }
}
