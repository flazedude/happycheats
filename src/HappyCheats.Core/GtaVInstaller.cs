using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Installs only the owned GTA V Enhanced ASI, notices and source archive, with checksum validation and rollback.</summary>
public static class GtaVInstaller
{
    public const string TrainerRoot = "HappyCheatsGtaV/";
    public const string DllPath = "HappyCheats.GtaV.asi";
    public const string NoticePath = TrainerRoot + "ThirdPartyNotices.txt";
    public const string SourcePath = TrainerRoot + "Source.zip";
    public const string ReceiptPath = "happycheats-gta-v-enhanced-install.json";
    public static readonly IReadOnlyList<string> RequiredFiles = Array.AsReadOnly(new[] { DllPath, NoticePath, SourcePath });
    public sealed record Entry(string Path, string Sha256);
    public sealed record Receipt(int SchemaVersion, string Module, List<Entry> Files);
    private sealed record Change(string Path, byte[]? Before, string? AfterHash);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static bool PrerequisitesAvailable(string directory, bool requireScriptHook = true) => (requireScriptHook ? new[] { "ScriptHookV.dll", "dinput8.dll" } : new[] { "dinput8.dll" }).All(name => {
        var path = SafePath(directory, name);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    });

    public static void EnsurePrerequisites(string directory, bool requireScriptHook = true)
    {
        if (!PrerequisitesAvailable(directory, requireScriptHook))
            throw new IOException(requireScriptHook
                ? "GTA V Enhanced 1.0.1158.13 requires the verified Script Hook V and ASI loader. Choose Install / update to set them up."
                : "The GTA V Enhanced Story Mode backend requires the verified ASI loader. Choose Install / update to set it up.");
    }

    private static List<Entry> Validate(IEnumerable<Entry> entries)
    {
        var result = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var path = entry.Path?.Replace('\\', '/');
            if (path == null || !RequiredFiles.Contains(path, StringComparer.Ordinal) || !seen.Add(path)
                || entry.Sha256 == null || !Regex.IsMatch(entry.Sha256, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant))
                throw new IOException("Invalid GTA V Enhanced package or ownership record.");
            result.Add(new Entry(path, entry.Sha256));
        }
        return result;
    }

    public static IReadOnlyList<Entry> ReadPackage(string package)
    {
        var json = ReadJson(SafePath(package, "package.json"));
        if (Text(json, "schemaVersion") != "1" || Text(json, "game") != "gta-v-enhanced" || Text(json, "version") != GtaVModule.TrainerVersion)
            throw new IOException("Unsupported GTA V Enhanced package identity.");
        var array = Object(json, "files");
        if (array.ValueKind != JsonValueKind.Array) throw new IOException("Missing GTA V Enhanced package files.");
        var entries = Validate(array.EnumerateArray().Select(file => new Entry(Text(file, "path"), Text(file, "sha256"))));
        if (entries.Count != RequiredFiles.Count || RequiredFiles.Any(path => entries.All(entry => entry.Path != path)))
            throw new IOException("The GTA V Enhanced package is missing a required runtime file.");
        foreach (var entry in entries)
        {
            var path = SafePath(package, entry.Path);
            if (!File.Exists(path) || new FileInfo(path).Length == 0 || !Matches(path, entry.Sha256))
                throw new IOException("GTA V Enhanced payload checksum failed: " + entry.Path);
        }
        return entries;
    }

    // Keep older installations manageable when the newly required source archive is absent.
    public static bool Installed(string directory) => File.Exists(SafePath(directory, DllPath));
    public static bool MatchesPackage(string package, string directory) =>
        ReadPackage(package).All(entry => Matches(SafePath(directory, entry.Path), entry.Sha256));

    private static bool Matches(string path, string hash) => File.Exists(path) && string.Equals(Hash(path), hash, StringComparison.OrdinalIgnoreCase);

    private static Receipt? ReadReceipt(string directory)
    {
        var path = SafePath(directory, ReceiptPath);
        if (!File.Exists(path)) return null;
        var receipt = JsonSerializer.Deserialize<Receipt>(ReadJson(path).GetRawText(), Json);
        if (receipt?.SchemaVersion != 1 || receipt.Module != "gta-v-enhanced" || receipt.Files == null)
            throw new IOException("Invalid GTA V Enhanced ownership receipt.");
        return receipt with { Files = Validate(receipt.Files) };
    }

    private sealed record InstallPlan(List<Entry> Ownership, List<(Entry Entry, string Target, byte[]? Before)> Files);

    public static void ValidateInstall(string package, string directory) => _ = PrepareInstall(package, directory);

    private static InstallPlan PrepareInstall(string package, string directory)
    {
        var entries = ReadPackage(package);
        if (Directory.Exists(SafePath(directory, ReceiptPath))) throw new IOException("The GTA V Enhanced ownership receipt path is a directory.");
        var receipt = ReadReceipt(directory);
        var owned = receipt?.Files.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase) ?? new();
        var next = receipt?.Files.ToList() ?? new List<Entry>();
        var plans = new List<(Entry Entry, string Target, byte[]? Before)>();
        foreach (var entry in entries)
        {
            var target = SafePath(directory, entry.Path);
            for (var parent = Path.GetDirectoryName(target); parent != null && !string.Equals(parent, Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase); parent = Path.GetDirectoryName(parent))
                if (File.Exists(parent)) throw new IOException("A GTA V Enhanced trainer directory path is a file.");
            if (Directory.Exists(target)) throw new IOException("A GTA V Enhanced trainer file path is a directory.");
            if (Matches(target, entry.Sha256)) continue; // Identical foreign files remain unowned.
            if (File.Exists(target) && (!owned.TryGetValue(entry.Path, out var prior) || !Matches(target, prior.Sha256)))
                throw new IOException("Preserved an existing or modified GTA V Enhanced trainer file: " + entry.Path);
            plans.Add((entry, target, File.Exists(target) ? File.ReadAllBytes(target) : null));
            next.RemoveAll(prior => prior.Path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase));
            next.Add(entry);
        }
        return new(next, plans);
    }

    public static void Install(string package, string directory, Action checkGameClosed, IProgress<string>? progress = null, bool requireScriptHook = true)
    {
        checkGameClosed();
        EnsurePrerequisites(directory, requireScriptHook);
        var plan = PrepareInstall(package, directory);
        var changes = new List<Change>();
        try
        {
            foreach (var file in plan.Files.OrderBy(file => file.Entry.Path == DllPath ? 1 : 0))
            {
                checkGameClosed();
                var source = SafePath(package, file.Entry.Path);
                var bytes = File.ReadAllBytes(source);
                if (!string.Equals(HashBytes(bytes), file.Entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("GTA V Enhanced payload changed during installation.");
                Replace(file.Target, bytes, file.Before, checkGameClosed, changes);
                progress?.Report("Installing " + Path.GetFileName(file.Target));
            }
            var receiptPath = SafePath(directory, ReceiptPath);
            var original = File.Exists(receiptPath) ? File.ReadAllBytes(receiptPath) : null;
            Replace(receiptPath, JsonSerializer.SerializeToUtf8Bytes(new Receipt(1, "gta-v-enhanced", plan.Ownership)), original, checkGameClosed, changes);
        }
        catch (Exception failure) { Rollback(directory, changes, failure); throw; }
        // The receipt commit is final: a failing progress callback here must not
        // cause the surrounding runtime transaction to roll back prerequisites.
    }

    public static void Remove(string directory, Action checkGameClosed, IProgress<string>? progress = null)
    {
        checkGameClosed();
        var receipt = ReadReceipt(directory) ?? throw new IOException("No GTA V Enhanced ownership receipt was found. Existing files were preserved.");
        var next = receipt.Files.ToList();
        var preserved = new List<string>();
        var changes = new List<Change>();
        var removedOwnedDll = false;
        try
        {
            foreach (var entry in receipt.Files)
            {
                var target = SafePath(directory, entry.Path);
                if (!File.Exists(target)) { next.Remove(entry); continue; }
                if (!Matches(target, entry.Sha256)) { preserved.Add(entry.Path); continue; }
                checkGameClosed();
                if (!Matches(target, entry.Sha256)) { preserved.Add(entry.Path); continue; }
                var before = File.ReadAllBytes(target);
                if (!string.Equals(HashBytes(before), entry.Sha256, StringComparison.OrdinalIgnoreCase)) { preserved.Add(entry.Path); continue; }
                File.Delete(target);
                changes.Add(new Change(target, before, null));
                next.Remove(entry);
                removedOwnedDll |= entry.Path == DllPath;
            }
            // Generated mailbox files are ours only when removing our unchanged ASI.
            if (removedOwnedDll)
                foreach (var name in new[] { "status.json", "command.json" })
                {
                    var path = SafePath(directory, TrainerRoot + name);
                    if (!File.Exists(path)) continue;
                    checkGameClosed();
                    byte[] before;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var buffer = new byte[65537];
                        var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                        if (count > 65536) throw new IOException("Unexpected GTA V Enhanced mailbox size; preserved for inspection.");
                        before = buffer[..count];
                    }
                    File.Delete(path);
                    changes.Add(new Change(path, before, null));
                }
            var receiptPath = SafePath(directory, ReceiptPath);
            var original = File.ReadAllBytes(receiptPath);
            Replace(receiptPath, JsonSerializer.SerializeToUtf8Bytes(new Receipt(1, "gta-v-enhanced", next)), original, checkGameClosed, changes);
        }
        catch (Exception failure) { Rollback(directory, changes, failure); throw; }
        progress?.Report(preserved.Count > 0 ? "Modified GTA V Enhanced files were preserved: " + string.Join(", ", preserved)
            : "GTA V Enhanced trainer removed. Unowned files were preserved.");
    }

    private static string HashBytes(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    private static void Replace(string path, byte[] bytes, byte[]? before, Action checkGameClosed, List<Change> changes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            checkGameClosed();
            if (before == null ? File.Exists(path) || Directory.Exists(path) : !Matches(path, HashBytes(before)))
                throw new IOException("A GTA V Enhanced file changed before installation commit; it was preserved.");
            File.Move(temporary, path, before != null);
            changes.Add(new Change(path, before, HashBytes(bytes)));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Rollback(string directory, List<Change> changes, Exception failure)
    {
        var errors = new List<Exception> { failure };
        foreach (var change in changes.AsEnumerable().Reverse())
        {
            try
            {
                var unchanged = change.AfterHash == null ? !File.Exists(change.Path) && !Directory.Exists(change.Path) : Matches(change.Path, change.AfterHash);
                if (!unchanged)
                {
                    if (change.Before != null)
                    {
                        var recovery = SafePath(directory, "happycheats-recovery/" + Guid.NewGuid().ToString("N") + "/" + Path.GetFileName(change.Path));
                        Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
                        File.WriteAllBytes(recovery, change.Before);
                        errors.Add(new IOException("A changed file was preserved. Its previous version was saved to " + recovery));
                    }
                    continue;
                }
                if (change.Before == null) File.Delete(change.Path);
                else File.WriteAllBytes(change.Path, change.Before);
            }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count > 1) throw new AggregateException("GTA V Enhanced operation failed; review recovery details.", errors);
    }
}
