using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Installs only HappyCheats' mod; SMAPI and other mods remain independently owned.</summary>
public static class StardewInstaller
{
    public const string ModRoot = "Mods/HappyCheats.StardewValley/";
    public const string Plugin = ModRoot + "HappyCheats.StardewValley.dll";
    public const string ReceiptPath = ModRoot + "install-receipt.json";
    public sealed record Entry(string Path, string Sha256);
    public sealed record Receipt(string Module, List<Entry> Files);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    internal static readonly string[] RequiredFiles = { Plugin, ModRoot + "manifest.json", ModRoot + "bridge/README.txt" };
    private static readonly HashSet<string> Allowed = new(RequiredFiles.Concat(new[] {
        ModRoot + "HappyCheats.StardewValley.pdb", ModRoot + "HappyCheats.StardewValley.deps.json"
    }), StringComparer.Ordinal);

    private static string Normalize(string path) => path.Replace('\\', '/');
    private static bool SameHash(string path, string hash) => string.Equals(Hash(path), hash, StringComparison.OrdinalIgnoreCase);

    private static void Validate(IEnumerable<Entry> files)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            if (file == null || file.Path == null || !Allowed.Contains(Normalize(file.Path)) || !seen.Add(Normalize(file.Path))
                || file.Sha256 == null || !Regex.IsMatch(file.Sha256, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant))
                throw new IOException("Invalid Stardew Valley package or ownership record.");
        }
    }

    public static IReadOnlyList<Entry> ReadPackage(string package)
    {
        var array = Object(ReadJson(SafePath(package, "package.json")), "files");
        if (array.ValueKind != JsonValueKind.Array) throw new IOException("Invalid Stardew Valley package manifest.");
        var files = array.EnumerateArray().Select(file => new Entry(Text(file, "path"), Text(file, "sha256"))).ToList();
        Validate(files);
        foreach (var required in RequiredFiles)
            if (!files.Any(file => Normalize(file.Path) == required)) throw new IOException("The Stardew Valley package is missing a required mod file: " + required);
        foreach (var file in files)
            if (!SameHash(SafePath(package, file.Path), file.Sha256)) throw new IOException("Payload checksum failed: " + file.Path);
        return files;
    }

    public static bool Installed(string root) => RequiredFiles.All(path => File.Exists(SafePath(root, path)));

    public static bool MatchesPackage(string package, string root) => ReadPackage(package).All(file => {
        var target = SafePath(root, file.Path);
        return File.Exists(target) && SameHash(target, file.Sha256);
    });

    private static Receipt? ReadReceipt(string root)
    {
        var path = SafePath(root, ReceiptPath);
        if (Directory.Exists(path)) throw new IOException("The Stardew Valley ownership receipt path is a directory.");
        if (!File.Exists(path)) return null;
        var receipt = JsonSerializer.Deserialize<Receipt>(ReadJson(path).GetRawText(), Json);
        if (receipt?.Module != "stardew" || receipt.Files == null) throw new IOException("Invalid Stardew Valley ownership receipt.");
        Validate(receipt.Files);
        return receipt;
    }

    public static void Install(string package, string root, Action checkGameClosed, IProgress<string>? progress = null)
    {
        checkGameClosed();
        var payload = ReadPackage(package);
        var receipt = ReadReceipt(root);
        var receiptPath = SafePath(root, ReceiptPath);
        var originalReceipt = File.Exists(receiptPath) ? File.ReadAllBytes(receiptPath) : null;
        var owned = receipt?.Files.ToDictionary(file => Normalize(file.Path), StringComparer.OrdinalIgnoreCase) ?? new();
        var nextOwned = receipt?.Files.ToList() ?? new List<Entry>();
        var writes = new List<Entry>();
        foreach (var file in payload) {
            var target = SafePath(root, file.Path);
            if (Directory.Exists(target)) throw new IOException("A trainer file path is a directory: " + file.Path);
            if (File.Exists(target)) {
                if (SameHash(target, file.Sha256)) continue; // Matching foreign files never become owned.
                if (!owned.TryGetValue(Normalize(file.Path), out var previous) || !SameHash(target, previous.Sha256))
                    throw new IOException("Preserved an existing or modified mod file: " + file.Path);
            }
            writes.Add(file);
            nextOwned.RemoveAll(previous => Normalize(previous.Path).Equals(Normalize(file.Path), StringComparison.OrdinalIgnoreCase));
            nextOwned.Add(file);
        }
        var transaction = new List<Change>();
        try {
            checkGameClosed();
            // Write the SMAPI manifest last, after the assembly and bridge are complete.
            foreach (var file in writes.OrderBy(file => Normalize(file.Path) == ModRoot + "manifest.json" ? 1 : 0)) {
                var target = SafePath(root, file.Path);
                var original = File.Exists(target) ? File.ReadAllBytes(target) : null;
                if (original != null && (!owned.TryGetValue(Normalize(file.Path), out var previous) || !SameHash(target, previous.Sha256)))
                    throw new IOException("A mod file changed during installation.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = SafePath(root, file.Path + ".happycheats-" + Guid.NewGuid().ToString("N") + ".tmp");
                try {
                    File.Copy(SafePath(package, file.Path), temporary);
                    if (!SameHash(temporary, file.Sha256)) throw new IOException("The payload changed during installation.");
                    checkGameClosed();
                    EnsureUnchanged(target, original);
                    if (original == null) File.Move(temporary, target);
                    else File.Replace(temporary, target, null);
                    transaction.Add(new Change(target, original, file.Sha256));
                    progress?.Report("Installing " + Path.GetFileName(file.Path));
                } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            checkGameClosed();
            EnsureUnchanged(receiptPath, originalReceipt);
            Directory.CreateDirectory(SafePath(root, StardewModule.BridgeRoot));
            WriteJson(receiptPath, new Receipt("stardew", nextOwned));
            transaction.Add(new Change(receiptPath, originalReceipt, Hash(receiptPath)));
        } catch (Exception failure) {
            Rollback(root, transaction, failure);
            throw;
        }
        progress?.Report("HappyCheats mod installed. Launch Stardew Valley with SMAPI. Gameplay validation is pending.");
    }

    public static void Remove(string root, Action checkGameClosed, IProgress<string>? progress = null)
    {
        checkGameClosed();
        var receipt = ReadReceipt(root) ?? throw new IOException("No HappyCheats ownership receipt was found. Existing mod files were preserved.");
        var receiptPath = SafePath(root, ReceiptPath);
        var originalReceipt = File.ReadAllBytes(receiptPath);
        var removals = new List<(Entry File, string Target, byte[] Original)>();
        // Preflight the whole removal before changing anything.
        foreach (var file in receipt.Files) {
            var target = SafePath(root, file.Path);
            if (Directory.Exists(target)) throw new IOException("Preserved an unexpected directory: " + file.Path);
            if (!File.Exists(target)) continue;
            if (!SameHash(target, file.Sha256)) throw new IOException("Preserved a modified mod file: " + file.Path);
            removals.Add((file, target, File.ReadAllBytes(target)));
        }
        var transaction = new List<Change>();
        try {
            foreach (var removal in removals.OrderBy(item => Normalize(item.File.Path) == ModRoot + "manifest.json" ? 0 : 1)) {
                checkGameClosed();
                EnsureUnchanged(removal.Target, removal.Original);
                File.Delete(removal.Target);
                transaction.Add(new Change(removal.Target, removal.Original, null));
            }
            checkGameClosed();
            EnsureUnchanged(receiptPath, originalReceipt);
            File.Delete(receiptPath);
            transaction.Add(new Change(receiptPath, originalReceipt, null));
        } catch (Exception failure) {
            Rollback(root, transaction, failure);
            throw;
        }
        progress?.Report("HappyCheats mod removed. SMAPI, other mods and unowned files were preserved.");
    }

    private sealed record Change(string Path, byte[]? Original, string? AppliedHash);

    private static void EnsureUnchanged(string path, byte[]? original)
    {
        if (Directory.Exists(path) || (original == null ? File.Exists(path) : !File.Exists(path) || !SameHash(path, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)))))
            throw new IOException("A mod file changed before the operation could finish: " + Path.GetFileName(path));
    }

    private static void Rollback(string root, List<Change> changes, Exception failure)
    {
        var errors = new List<Exception> { failure };
        foreach (var change in changes.AsEnumerable().Reverse()) {
            try {
                // Recheck links and preserve edits made by another application during rollback.
                var target = SafePath(root, Path.GetRelativePath(root, change.Path));
                var unchanged = !Directory.Exists(target) && (change.AppliedHash == null ? !File.Exists(target) : File.Exists(target) && SameHash(target, change.AppliedHash));
                if (!unchanged) {
                    if (change.Original != null) {
                        var recovery = SafePath(root, ModRoot + "recovery/" + Guid.NewGuid().ToString("N") + "/" + Path.GetFileName(target));
                        Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
                        File.WriteAllBytes(recovery, change.Original);
                        errors.Add(new IOException("A changed file was preserved. Its previous version is saved at " + recovery));
                    }
                    continue;
                }
                if (change.Original == null) File.Delete(target);
                else File.WriteAllBytes(target, change.Original);
            } catch (Exception rollback) { errors.Add(rollback); }
        }
        if (errors.Count > 1) throw new AggregateException("Stardew Valley mod operation failed; some files need recovery.", errors);
    }
}
