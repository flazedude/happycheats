using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Installs the trainer and an optional verified author download in one rollback transaction; shared loaders are preserved on removal.</summary>
public static class Rdr2Installer
{
    public const string ReceiptPath = "happycheats-red-dead-redemption-2-install.json";
    public const string Plugin = "HappyCheats.Rdr2.asi";
    public const string BridgeReadme = "HappyCheatsRdr2/bridge/README.txt";
    internal static readonly string[] RequiredFiles = { Plugin, BridgeReadme };
    public sealed record Entry(string Path, string Sha256);
    public sealed record Receipt(string Module, List<Entry> Files);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static void Validate(IEnumerable<Entry> entries, Rdr2RuntimePackage.Specification? runtime = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            if (entry.Path == null || (!RequiredFiles.Contains(entry.Path, StringComparer.Ordinal)
                && (runtime == null || !Rdr2RuntimePackage.KnownComponent(entry.Path, entry.Sha256, runtime))) || !seen.Add(entry.Path)
                || entry.Sha256 == null || !Regex.IsMatch(entry.Sha256, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant))
                throw new IOException("Invalid Red Dead Redemption 2 package or ownership record.");
    }

    public static IReadOnlyList<Entry> ReadPackage(string package)
    {
        var json = ReadJson(SafePath(package, "package.json"));
        if (Text(json, "game_id") != "red-dead-redemption-2" || Text(json, "version") != Rdr2Module.TrainerVersion || Text(json, "app_id") != "1174180")
            throw new IOException("Incorrect Red Dead Redemption 2 package identity or version.");
        var array = Object(json, "files");
        if (array.ValueKind != JsonValueKind.Array) throw new IOException("Invalid Red Dead Redemption 2 package manifest.");
        var files = array.EnumerateArray().Select(file => new Entry(Text(file, "path"), Text(file, "sha256"))).ToList();
        Validate(files);
        if (files.Count != RequiredFiles.Length || RequiredFiles.Any(required => !files.Any(file => file.Path == required)))
            throw new IOException("The Red Dead Redemption 2 package is missing a required runtime file.");
        foreach (var file in files)
            if (!string.Equals(Hash(SafePath(package, file.Path)), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Payload checksum failed: " + file.Path);
        return files;
    }

    public static bool Installed(string root) => RequiredFiles.All(path => File.Exists(SafePath(root, path)));
    public static bool MatchesPackage(string package, string root) => ReadPackage(package).All(file => {
        var target = SafePath(root, file.Path);
        return File.Exists(target) && string.Equals(Hash(target), file.Sha256, StringComparison.OrdinalIgnoreCase);
    });

    private static Receipt? ReadReceipt(string root, Rdr2RuntimePackage.Specification specification)
    {
        var path = SafePath(root, ReceiptPath);
        if (!File.Exists(path)) return null;
        var receipt = JsonSerializer.Deserialize<Receipt>(ReadJson(path).GetRawText(), Json);
        if (receipt?.Module != "red-dead-redemption-2" || receipt.Files == null) throw new IOException("Invalid Red Dead Redemption 2 ownership receipt.");
        Validate(receipt.Files, specification);
        return receipt;
    }

    public static void Install(string package, string root, Action checkGameClosed, IProgress<string>? progress = null,
        Rdr2RuntimePackage.VerifiedRuntime? runtime = null) => Install(package, root, checkGameClosed, progress, runtime, Rdr2RuntimePackage.Official);

    internal static void Install(string package, string root, Action checkGameClosed, IProgress<string>? progress,
        Rdr2RuntimePackage.VerifiedRuntime? runtime, Rdr2RuntimePackage.Specification specification)
    {
        checkGameClosed();
        var payload = ReadPackage(package).ToList();
        runtime?.Validate(specification);
        if (runtime != null) payload.AddRange(runtime.Files.Select(file => new Entry(file.Path, file.Sha256)));
        var receipt = ReadReceipt(root, specification);
        var receiptPath = SafePath(root, ReceiptPath);
        if (Directory.Exists(receiptPath)) throw new IOException("The ownership receipt path is a directory.");
        var receiptBefore = File.Exists(receiptPath) ? File.ReadAllBytes(receiptPath) : null;
        var owned = receipt?.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase) ?? new();
        var nextOwned = receipt?.Files.ToList() ?? new List<Entry>();
        var writes = new List<Entry>();
        foreach (var file in payload) {
            var target = SafePath(root, file.Path);
            if (Directory.Exists(target)) throw new IOException("A trainer file path is a directory: " + file.Path);
            var exists = File.Exists(target);
            if (exists && string.Equals(Hash(target), file.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            // Shared runtime conflicts are never overwritten, even when a receipt claims ownership.
            if (exists && Rdr2RuntimePackage.IsRuntimePath(file.Path)) throw new IOException(Rdr2RuntimePackage.ConflictMessage);
            if (exists && (!owned.TryGetValue(file.Path, out var previous) || !string.Equals(Hash(target), previous.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Preserved an existing or modified mod file: " + file.Path);
            writes.Add(file);
            nextOwned.RemoveAll(previous => previous.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
            nextOwned.Add(file);
        }
        var backups = new Dictionary<string, byte[]?>();
        var applied = new Dictionary<string, string>();
        try {
            checkGameClosed();
            // Stage dependencies first, then our ASI; publish a new ASI loader only after all its inputs exist.
            foreach (var file in writes.OrderBy(file => file.Path == Rdr2RuntimePackage.LoaderPath ? 2 : file.Path == Plugin ? 1 : 0)) {
                var target = SafePath(root, file.Path);
                var original = File.Exists(target) ? File.ReadAllBytes(target) : null;
                if (original != null && (!owned.TryGetValue(file.Path, out var previous) || !string.Equals(Hash(target), previous.Sha256, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("A mod file changed during installation.");
                backups[target] = original;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = SafePath(root, file.Path + ".happycheats-stage");
                if (File.Exists(temporary) || Directory.Exists(temporary)) throw new IOException("A previous installation staging file needs inspection: " + temporary);
                try {
                    if (Rdr2RuntimePackage.IsRuntimePath(file.Path)) {
                        var source = runtime!.Files.Single(item => item.Path == file.Path);
                        using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        output.Write(source.Bytes);
                    } else File.Copy(SafePath(package, file.Path), temporary);
                    if (!string.Equals(Hash(temporary), file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Payload changed during installation.");
                    checkGameClosed();
                    if (original == null) File.Move(temporary, target);
                    else {
                        if (!SameBytes(target, original)) throw new IOException("A mod file changed before installation commit.");
                        File.Replace(temporary, target, null);
                    }
                    applied[target] = file.Sha256;
                    progress?.Report("Installing " + Path.GetFileName(file.Path));
                } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            checkGameClosed();
            if (!SameBytes(receiptPath, receiptBefore)) throw new IOException("The trainer ownership record changed during installation.");
            backups[receiptPath] = receiptBefore;
            WriteJson(receiptPath, new Receipt("red-dead-redemption-2", nextOwned));
            applied[receiptPath] = Hash(receiptPath);
        } catch (Exception failure) {
            var errors = new List<Exception> { failure };
            foreach (var item in applied.Reverse()) {
                try {
                    var original = backups[item.Key];
                    if (!File.Exists(item.Key) || !string.Equals(Hash(item.Key), item.Value, StringComparison.OrdinalIgnoreCase)) {
                        if (original != null) {
                            var recovery = SafePath(root, "happycheats-recovery/" + Guid.NewGuid().ToString("N") + "/" + Path.GetFileName(item.Key));
                            Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
                            File.WriteAllBytes(recovery, original);
                            errors.Add(new IOException("A changed file was preserved; its previous version was saved to " + recovery));
                        }
                        continue;
                    }
                    if (original == null) File.Delete(item.Key); else File.WriteAllBytes(item.Key, original);
                } catch (Exception rollback) { errors.Add(rollback); }
            }
            if (errors.Count > 1) throw new AggregateException("Red Dead Redemption 2 installation failed; some files need recovery.", errors);
            throw;
        }
        progress?.Report("Experimental Red Dead Redemption 2 Story Mode trainer installed. Gameplay validation is pending. Red Dead Online is unavailable.");
    }

    private static bool SameBytes(string path, byte[]? expected) => expected == null
        ? !File.Exists(path) && !Directory.Exists(path)
        : File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(expected);

    public static void Remove(string root, Action checkGameClosed, IProgress<string>? progress = null) =>
        Remove(root, checkGameClosed, progress, Rdr2RuntimePackage.Official);

    internal static void Remove(string root, Action checkGameClosed, IProgress<string>? progress, Rdr2RuntimePackage.Specification specification)
    {
        checkGameClosed();
        var receipt = ReadReceipt(root, specification) ?? throw new IOException("No HappyCheats ownership receipt was found. Existing files were preserved.");
        var preserved = new List<string>();
        foreach (var file in receipt.Files.ToArray()) {
            // Script Hook and its loader can serve other ASIs, including ones installed after HappyCheats.
            if (Rdr2RuntimePackage.IsRuntimePath(file.Path)) continue;
            var target = SafePath(root, file.Path);
            if (!File.Exists(target)) { receipt.Files.Remove(file); continue; }
            if (!string.Equals(Hash(target), file.Sha256, StringComparison.OrdinalIgnoreCase)) { preserved.Add(file.Path); continue; }
            checkGameClosed();
            if (!File.Exists(target) || !string.Equals(Hash(target), file.Sha256, StringComparison.OrdinalIgnoreCase)) { preserved.Add(file.Path); continue; }
            File.Delete(target);
            receipt.Files.Remove(file);
        }
        WriteJson(SafePath(root, ReceiptPath), receipt);
        progress?.Report(preserved.Count > 0 ? "Modified Red Dead Redemption 2 trainer files were preserved: " + string.Join(", ", preserved)
            : "Red Dead Redemption 2 trainer removed. Existing Script Hook, ASI loader and other mods were preserved.");
    }
}
