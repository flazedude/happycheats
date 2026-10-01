using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Hotel Architect's isolated UE4SS package, installed with verified ownership and rollback.</summary>
public static class HotelArchitectInstaller
{
    public const string ReceiptPath = "happycheats-hotel-architect-install.json";
    public const string NativeLoader = "HotelArchitect/Binaries/Win64/dwmapi.dll";
    public const string LoaderLibrary = "HotelArchitect/Binaries/Win64/ue4ss/UE4SS.dll";
    public const string ModRoot = "HotelArchitect/Binaries/Win64/ue4ss/Mods/HappyCheatsHotelArchitect/";
    public const string MainScript = ModRoot + "Scripts/main.lua";
    public sealed record Entry(string Path, string Sha256);
    public sealed record Receipt(string Module, List<Entry> Files);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) {
        NativeLoader, LoaderLibrary, "HotelArchitect/Binaries/Win64/ue4ss/UE4SS-settings.ini", "HotelArchitect/Binaries/Win64/ue4ss/Mods/mods.txt",
        MainScript, ModRoot + "Scripts/bindings.lua", ModRoot + "Scripts/protocol.lua",
        ModRoot + "enabled.txt", ModRoot + "bridge/README.txt"
    };
    internal static readonly string[] RequiredFiles = {
        NativeLoader, LoaderLibrary, "HotelArchitect/Binaries/Win64/ue4ss/UE4SS-settings.ini", "HotelArchitect/Binaries/Win64/ue4ss/Mods/mods.txt",
        MainScript, ModRoot + "Scripts/bindings.lua", ModRoot + "Scripts/protocol.lua", ModRoot + "bridge/README.txt"
    };

    private static void Validate(IEnumerable<Entry> files)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            var path = file.Path?.Replace('\\', '/');
            if (path == null || !Allowed.Contains(path) || !seen.Add(path) || file.Sha256 == null || !Regex.IsMatch(file.Sha256, "^[A-Fa-f0-9]{64}$"))
                throw new IOException("Invalid Hotel Architect package or ownership record.");
        }
    }

    public static IReadOnlyList<Entry> ReadPackage(string package)
    {
        var json = ReadJson(SafePath(package, "package.json"));
        if (Text(json, "schema_version") != "1" || Text(json, "game_id") != "hotel-architect"
            || Text(json, "version") != HotelArchitectModule.TrainerVersion
            || !string.Equals(Text(json, "game_executable_sha256"), HotelArchitectModule.SupportedExecutableHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Invalid Hotel Architect package identity or supported game build.");
        var array = Object(json, "files");
        if (array.ValueKind != JsonValueKind.Array) throw new IOException("Invalid Hotel Architect package manifest.");
        var files = array.EnumerateArray().Select(file => new Entry(Text(file, "path"), Text(file, "sha256"))).ToList();
        Validate(files);
        foreach (var required in RequiredFiles)
            if (!files.Any(file => file.Path.Replace('\\', '/') == required)) throw new IOException("The Hotel Architect package is missing a required runtime file: " + required);
        foreach (var file in files)
            if (!string.Equals(Hash(SafePath(package, file.Path)), file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Payload checksum failed: " + file.Path);
        return files;
    }

    public static bool Installed(string root) => RequiredFiles.All(path => File.Exists(SafePath(root, path)));

    public static bool MatchesPackage(string package, string root) => ReadPackage(package).All(file => {
        var target = SafePath(root, file.Path);
        return File.Exists(target) && string.Equals(Hash(target), file.Sha256, StringComparison.OrdinalIgnoreCase);
    });

    private static Receipt? ReadReceipt(string root)
    {
        var path = SafePath(root, ReceiptPath);
        if (!File.Exists(path)) return null;
        var receipt = JsonSerializer.Deserialize<Receipt>(ReadJson(path).GetRawText(), Json);
        if (receipt?.Module != "hotel-architect" || receipt.Files == null) throw new IOException("Invalid Hotel Architect ownership receipt.");
        Validate(receipt.Files);
        return receipt;
    }

    public static void Install(string package, string root, Action checkGameClosed, IProgress<string>? progress = null)
    {
        checkGameClosed();
        var payload = ReadPackage(package);
        var receipt = ReadReceipt(root);
        var owned = receipt?.Files.ToDictionary(file => file.Path.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase) ?? new();
        var nextOwned = receipt?.Files.ToList() ?? new List<Entry>();
        var writes = new List<Entry>();
        foreach (var file in payload) {
            var target = SafePath(root, file.Path);
            if (Directory.Exists(target)) throw new IOException("A trainer file path is a directory: " + file.Path);
            var exists = File.Exists(target);
            if (exists && string.Equals(Hash(target), file.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            if (exists && (!owned.TryGetValue(file.Path.Replace('\\', '/'), out var previous) || !string.Equals(Hash(target), previous.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Preserved an existing or modified mod file: " + file.Path);
            writes.Add(file);
            nextOwned.RemoveAll(previous => previous.Path.Replace('\\', '/').Equals(file.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            nextOwned.Add(file);
        }
        var backups = new Dictionary<string, byte[]?>();
        var applied = new Dictionary<string, string>();
        try {
            checkGameClosed();
            // Publish the native bootstrap only after its Lua code and loader tree are complete.
            foreach (var file in writes.OrderBy(file => file.Path.Replace('\\', '/') == NativeLoader ? 1 : 0)) {
                var target = SafePath(root, file.Path);
                var original = File.Exists(target) ? File.ReadAllBytes(target) : null;
                if (original != null && (!owned.TryGetValue(file.Path.Replace('\\', '/'), out var previous) || !string.Equals(Hash(target), previous.Sha256, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("A mod file changed during installation.");
                backups[target] = original;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = SafePath(root, file.Path + ".happycheats-stage");
                if (File.Exists(temporary) || Directory.Exists(temporary)) throw new IOException("A previous installation staging file needs inspection: " + temporary);
                try {
                    File.Copy(SafePath(package, file.Path), temporary);
                    if (!string.Equals(Hash(temporary), file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Payload changed during installation.");
                    checkGameClosed();
                    if (original == null) File.Move(temporary, target);
                    else {
                        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original));
                        if (!File.Exists(target) || !string.Equals(Hash(target), expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("A mod file changed before installation commit.");
                        File.Replace(temporary, target, null);
                    }
                    applied[target] = file.Sha256;
                    progress?.Report("Installing " + Path.GetFileName(file.Path));
                } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            checkGameClosed();
            Directory.CreateDirectory(SafePath(root, HotelArchitectModule.BridgeRoot));
            var receiptPath = SafePath(root, ReceiptPath);
            backups[receiptPath] = File.Exists(receiptPath) ? File.ReadAllBytes(receiptPath) : null;
            WriteJson(receiptPath, new Receipt("hotel-architect", nextOwned));
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
            if (errors.Count > 1) throw new AggregateException("Hotel Architect installation failed; some files need recovery.", errors);
            throw;
        }
        progress?.Report("Experimental Hotel Architect trainer installed. Gameplay validation is pending.");
    }

    public static void Remove(string root, Action checkGameClosed, IProgress<string>? progress = null)
    {
        checkGameClosed();
        var receipt = ReadReceipt(root) ?? throw new IOException("No HappyCheats ownership receipt was found. Existing files were preserved.");
        var preserved = new List<string>();
        var modsDirectory = SafePath(root, "HotelArchitect/Binaries/Win64/ue4ss/Mods");
        var sharedLoader = Directory.Exists(modsDirectory) && Directory.EnumerateDirectories(modsDirectory)
            .Any(directory => !string.Equals(Path.GetFileName(directory), "HappyCheatsHotelArchitect", StringComparison.OrdinalIgnoreCase));
        // Remove only unchanged files this installer wrote; a matching foreign file never becomes owned.
        foreach (var file in receipt.Files.ToArray()) {
            if (sharedLoader && !file.Path.Replace('\\', '/').StartsWith(ModRoot, StringComparison.Ordinal)) continue;
            var target = SafePath(root, file.Path);
            if (!File.Exists(target)) { receipt.Files.Remove(file); continue; }
            if (!string.Equals(Hash(target), file.Sha256, StringComparison.OrdinalIgnoreCase)) { preserved.Add(file.Path); continue; }
            checkGameClosed();
            if (!string.Equals(Hash(target), file.Sha256, StringComparison.OrdinalIgnoreCase)) { preserved.Add(file.Path); continue; }
            File.Delete(target);
            receipt.Files.Remove(file);
        }
        WriteJson(SafePath(root, ReceiptPath), receipt);
        progress?.Report(preserved.Count > 0 ? "Hotel Architect trainer files were modified and preserved: " + string.Join(", ", preserved)
            : sharedLoader ? "Hotel Architect trainer removed. The shared UE4SS loader was preserved for other mods."
            : "Hotel Architect trainer removed. Unowned files were preserved.");
    }
}
