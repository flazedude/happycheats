using System.Text.Json;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Install only verified payloads; preserve foreign files and roll back failed updates.</summary>
public static class ProjectHospitalInstaller
{
    public sealed record Entry(string Path, string Sha256);
    public sealed record Receipt(string Module, List<Entry> Files);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public const string ReceiptPath = "happycheats-project-hospital-install.json";
    public const string Plugin = "BepInEx/plugins/HappyCheats.ProjectHospital/HappyCheats.ProjectHospital.dll";
    internal static readonly string[] RequiredFiles = {
        "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "BepInEx/core/0Harmony.dll",
        "BepInEx/core/0Harmony20.dll", "BepInEx/core/BepInEx.dll", "BepInEx/core/BepInEx.Harmony.dll",
        "BepInEx/core/BepInEx.Preloader.dll", "BepInEx/core/HarmonyXInterop.dll", "BepInEx/core/Mono.Cecil.dll",
        "BepInEx/core/Mono.Cecil.Mdb.dll", "BepInEx/core/Mono.Cecil.Pdb.dll", "BepInEx/core/Mono.Cecil.Rocks.dll",
        "BepInEx/core/MonoMod.RuntimeDetour.dll", "BepInEx/core/MonoMod.Utils.dll", Plugin
    };
    public static IReadOnlyList<Entry> ReadPackage(string package)
    {
        var json = ReadJson(SafePath(package, "package.json"));
        if (Text(json, "schema_version") != "1" || Text(json, "game_id") != "project-hospital" || Text(json, "steam_app_id") != "868360" ||
            Text(json, "version") != ProjectHospitalModule.TrainerVersion ||
            !string.Equals(Text(json, "game_assembly_sha256"), ProjectHospitalModule.SupportedAssemblyHash, StringComparison.OrdinalIgnoreCase) ||
            Text(Object(json, "loader"), "architecture") != "win-x64")
            throw new IOException("The Project Hospital package does not match the supported game build.");
        var array = Object(json, "files");
        if (array.ValueKind != JsonValueKind.Array) throw new IOException("Invalid Project Hospital package manifest.");
        var files = array.EnumerateArray().Select(p => new Entry(Text(p, "path"), Text(p, "sha256"))).ToList();
        Validate(files);
        foreach (var required in RequiredFiles)
            if (!files.Any(f => f.Path.Replace('\\', '/') == required)) throw new IOException("The Project Hospital package is missing a required runtime file: " + required);
        foreach (var file in files) if (!string.Equals(Hash(SafePath(package, file.Path)), file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Payload checksum failed: " + file.Path);
        return files;
    }
    public static bool Installed(string root) => RequiredFiles.All(path => File.Exists(SafePath(root, path)));
    private static void Validate(IEnumerable<Entry> files)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var p = f.Path?.Replace('\\', '/');
            if (p == null || !RequiredFiles.Contains(p, StringComparer.Ordinal) || !seen.Add(p) || f.Sha256 == null ||
                !System.Text.RegularExpressions.Regex.IsMatch(f.Sha256, "^[0-9a-fA-F]{64}$")) throw new IOException("Invalid trainer package or ownership record.");
        }
    }
    private static Receipt? ReadReceipt(string root)
    {
        var path = SafePath(root, ReceiptPath);
        if (!File.Exists(path)) return null;
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path), Json);
        if (receipt == null || receipt.Module != "project-hospital" || receipt.Files == null) throw new IOException("Invalid trainer ownership receipt.");
        Validate(receipt.Files); return receipt;
    }
    public static void Install(string package, string root, Action checkGameClosed, IProgress<string>? progress = null)
    {
        checkGameClosed();
        var payload = ReadPackage(package);
        var previous = ReadReceipt(root);
        var owned = previous?.Files.ToDictionary(f => f.Path.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase) ?? new();
        var writes = new List<Entry>();
        var nextOwned = previous?.Files.ToList() ?? new List<Entry>();
        foreach (var file in payload)
        {
            var target = SafePath(root, file.Path);
            if (Directory.Exists(target)) throw new IOException("A trainer file path is a directory: " + target);
            var exists = File.Exists(target);
            if (exists && string.Equals(Hash(target), file.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            if (exists && (!owned.TryGetValue(file.Path.Replace('\\', '/'), out var old) || !string.Equals(Hash(target), old.Sha256, StringComparison.OrdinalIgnoreCase))) throw new IOException("Preserved an existing or modified mod file: " + file.Path);
            writes.Add(file); nextOwned.RemoveAll(f => f.Path.Replace('\\', '/').Equals(file.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)); nextOwned.Add(file);
        }
        var backups = new Dictionary<string, byte[]?>();
        var applied = new Dictionary<string, string>();
        var receiptPath = SafePath(root, ReceiptPath);
        try
        {
            checkGameClosed();
            // Publish the native bootstrap last so a launch cannot see a partial loader tree.
            foreach (var file in writes.OrderBy(f => f.Path.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            {
                var target = SafePath(root, file.Path);
                backups[target] = File.Exists(target) ? File.ReadAllBytes(target) : null;
                if (backups[target] != null && (!owned.TryGetValue(file.Path.Replace('\\', '/'), out var old) || !string.Equals(Hash(target), old.Sha256, StringComparison.OrdinalIgnoreCase))) throw new IOException("A mod file changed during installation.");
                progress?.Report("Installing " + Path.GetFileName(file.Path));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var stage = SafePath(root, file.Path + ".happycheats-stage");
                if (File.Exists(stage) || Directory.Exists(stage)) throw new IOException("A previous installation staging file needs inspection: " + stage);
                try {
                    File.Copy(SafePath(package, file.Path), stage);
                    if (!string.Equals(Hash(stage), file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Payload changed during installation.");
                    checkGameClosed();
                    var original = backups[target];
                    if (original == null) File.Move(stage, target); // Never overwrite a file created by another installer.
                    else {
                        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original));
                        if (!File.Exists(target) || !string.Equals(Hash(target), expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("A mod file changed before installation commit.");
                        File.Replace(stage, target, null);
                    }
                    applied[target] = file.Sha256;
                } finally { if (File.Exists(stage)) File.Delete(stage); }
            }
            checkGameClosed();
            backups[receiptPath] = File.Exists(receiptPath) ? File.ReadAllBytes(receiptPath) : null;
            WriteJson(receiptPath, new Receipt("project-hospital", nextOwned));
            applied[receiptPath] = Hash(receiptPath);
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var item in applied.Reverse()) {
                try {
                    var original = backups[item.Key];
                    if (!File.Exists(item.Key) || !string.Equals(Hash(item.Key), item.Value, StringComparison.OrdinalIgnoreCase)) {
                        if (original != null) {
                            var recovery = SafePath(root, "happycheats-recovery/" + Guid.NewGuid().ToString("N") + "/" + Path.GetFileName(item.Key));
                            Directory.CreateDirectory(Path.GetDirectoryName(recovery)!); File.WriteAllBytes(recovery, original);
                            errors.Add(new IOException("A changed file was preserved; its previous version was saved to " + recovery));
                        }
                        continue;
                    }
                    if (original == null) File.Delete(item.Key); else File.WriteAllBytes(item.Key, original);
                } catch (Exception rollbackFailure) { errors.Add(rollbackFailure); }
            }
            if (errors.Count > 1) throw new AggregateException("Installation failed; some files need recovery. " + string.Join(" ", errors.Select(e => e.Message)), errors);
            throw;
        }
        progress?.Report("Project Hospital trainer installed. Launch the game to connect.");
    }
    public static void Remove(string root, Action checkGameClosed, IProgress<string>? progress = null)
    {
        checkGameClosed(); var receipt = ReadReceipt(root) ?? throw new IOException("No HappyCheats ownership receipt was found. Existing files were preserved.");
        // BepInEx may now serve other mods. Remove only our plugin and keep the shared loader.
        var plugin = receipt.Files.Where(f => f.Path.Replace('\\', '/').StartsWith("BepInEx/plugins/HappyCheats.ProjectHospital/", StringComparison.Ordinal)).ToList();
        foreach (var f in plugin) {
            var target = SafePath(root, f.Path);
            if (File.Exists(target) && !string.Equals(Hash(target), f.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("The trainer plugin was modified and has been preserved.");
        }
        checkGameClosed();
        foreach (var f in plugin) {
            checkGameClosed();
            var target = SafePath(root, f.Path);
            if (File.Exists(target) && !string.Equals(Hash(target), f.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("The trainer plugin was modified and has been preserved.");
            File.Delete(target); receipt.Files.Remove(f);
        }
        WriteJson(SafePath(root, ReceiptPath), receipt);
        progress?.Report("Trainer removed. The shared BepInEx loader is preserved for other mods.");
    }
}

