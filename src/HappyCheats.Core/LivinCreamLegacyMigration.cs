using System.Text.Json;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Retire only the unchanged, receipt-owned 1.0.0 Mono entry points; keep recoverable bytes.</summary>
internal sealed class LivinCreamLegacyMigration : IDisposable
{
    internal const string Receipt = "happycheats-livin-cream-install.json";
    internal const string Plugin = "BepInEx/plugins/HappyCheats.LivinCream/HappyCheats.LivinCream.dll";
    private readonly List<(string Original, string Backup, string Hash)> moved = [];
    private readonly Action checkClosed;
    private bool complete;
    private LivinCreamLegacyMigration(Action closed) => checkClosed = closed;
    internal static LivinCreamLegacyMigration Begin(string directory, Action closed)
    {
        var migration = new LivinCreamLegacyMigration(closed);
        try
        {
            var receipt = SafePath(directory, Receipt);
            if (!File.Exists(receipt))
            {
                if (File.Exists(SafePath(directory, "winhttp.dll")) && File.Exists(SafePath(directory, "BepInEx/core/BepInEx.Preloader.dll"))
                    && !File.Exists(SafePath(directory, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")))
                    throw new IOException("An unowned Mono loader is present. Migrate it to IL2CPP before installing; existing files were preserved.");
                return migration;
            }
            var data = ReadJson(receipt);
            if (Text(data, "Module") != "livin-cream" || Object(data, "Files").ValueKind != JsonValueKind.Array)
                throw new IOException("Invalid legacy Livin' the Cream ownership receipt. Existing files were preserved.");
            var entries = Object(data, "Files").EnumerateArray().Select(x => (Path: Text(x, "Path"), Hash: Text(x, "Sha256"))).ToArray();
            if (entries.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length
                || entries.Any(x => !System.Text.RegularExpressions.Regex.IsMatch(x.Hash, "^[A-Fa-f0-9]{64}$")
                    || !(x.Path is "winhttp.dll" or "doorstop_config.ini" or ".doorstop_version" or Plugin
                        || x.Path.StartsWith("BepInEx/core/", StringComparison.Ordinal) && x.Path.Split('/').Length == 3 && x.Path.EndsWith(".dll", StringComparison.Ordinal))))
                throw new IOException("Invalid legacy trainer ownership paths. Existing files were preserved.");
            foreach (var entry in entries) _ = SafePath(directory, entry.Path);
            var bootstrap = entries.SingleOrDefault(x => x.Path == "winhttp.dll");
            if (bootstrap.Path == null && File.Exists(SafePath(directory, "winhttp.dll"))
                && File.Exists(SafePath(directory, "BepInEx/core/BepInEx.Preloader.dll"))
                && !File.Exists(SafePath(directory, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")))
                throw new IOException("The old receipt does not own the Mono loader. Migrate that loader to IL2CPP before retrying; existing files were preserved.");
            if (bootstrap.Path != null && File.Exists(SafePath(directory, bootstrap.Path)))
            {
                if (!Matches(SafePath(directory, bootstrap.Path), bootstrap.Hash)) throw new IOException("The previous Mono loader was modified. Preserve your mods and migrate that loader to IL2CPP before retrying.");
                foreach (var folder in new[] { "BepInEx/plugins", "BepInEx/patchers" })
                {
                    var path = SafePath(directory, folder);
                    if (Directory.Exists(path) && Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                        .Any(file => !string.Equals(Path.GetFullPath(file), SafePath(directory, Plugin), StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("Other mods use the previous Mono loader. Migrate those mods to IL2CPP first; HappyCheats preserved every file.");
                }
            }
            foreach (var name in new[] { Plugin, "winhttp.dll" })
            {
                var owned = entries.SingleOrDefault(x => x.Path == name);
                if (owned.Path == null) continue;
                var path = SafePath(directory, name);
                if (!File.Exists(path)) continue;
                if (!Matches(path, owned.Hash)) throw new IOException("The old Mono trainer was modified and preserved: " + name);
                var backup = SafePath(directory, "happycheats-recovery/livin-cream-mono-" + Guid.NewGuid().ToString("N") + "/" + Path.GetFileName(path));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                closed();
                if (!Matches(path, owned.Hash)) throw new IOException("The old trainer changed during migration; it was preserved.");
                File.Move(path, backup);
                migration.moved.Add((path, backup, owned.Hash));
            }
            return migration;
        }
        catch { migration.Dispose(); throw; }
    }
    private static bool Matches(string path, string hash) => File.Exists(path) && string.Equals(Hash(path), hash, StringComparison.OrdinalIgnoreCase);
    internal void Complete() => complete = true;
    public void Dispose()
    {
        if (complete) return;
        var failures = new List<Exception>();
        foreach (var item in moved.AsEnumerable().Reverse())
            try
            {
                checkClosed();
                if (File.Exists(item.Original) || Directory.Exists(item.Original) || !Matches(item.Backup, item.Hash))
                    throw new IOException("A changed file was preserved. The previous trainer remains recoverable at " + item.Backup);
                File.Move(item.Backup, item.Original);
            }
            catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Legacy trainer recovery needs inspection after the game is closed.", failures);
    }
}
