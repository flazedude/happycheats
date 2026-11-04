using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class LivinCreamTests
{
    public static int RunAll()
    {
        var root = Path.Combine(Path.GetTempPath(), "HappyCheats-livin-installer-" + Guid.NewGuid().ToString("N"));
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        void Reject(Action action) { try { action(); } catch (Exception) { checks++; return; } throw new Exception("Expected rejection"); }
        try {
            var payload = Path.Combine(root, "payload", "livin-cream"); var game = Path.Combine(root, "game");
            Directory.CreateDirectory(payload); Directory.CreateDirectory(game);
            foreach (var file in LivinCreamInstaller.RequiredFiles) {
                var path = SafePath(payload, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "fixture: " + file);
            }
            var files = LivinCreamInstaller.RequiredFiles.Select(path => new { path, sha256 = Hash(SafePath(payload, path)) }).ToArray();
            void Manifest(object entries) => WriteJson(SafePath(payload, "package.json"), new { schema_version = 1, game_id = "livin-cream", steam_app_id = 4621810,
                version = LivinCreamModule.TrainerVersion, game_assembly_sha256 = LivinCreamModule.SupportedAssemblyHash, loader = new { architecture = "win-x64" }, files = entries });
            Manifest(files);
            var module = new LivinCreamModule(root);
            Check(ModuleCatalog.Create(root).Any(m => m is LivinCreamModule), "Real catalog registration");
            Check(module.GameExecutables.SequenceEqual(new[] { LivinCreamModule.Executable }) && module.Definition.AppId == "4621810", "Exact shared-menu executable target");
            Check(module.Definition.Features.Count == 6 && module.Definition.Features.Count(f => f.Number != null) == 3, "Six controls with numeric metadata");
            Check(module is IFeatureValueModule && LivinCreamModule.SupportsAssembly(LivinCreamModule.SupportedAssemblyHash.ToLowerInvariant()) && !LivinCreamModule.SupportsAssembly(new string('0', 64)), "Frozen value contract and exact build gate");
            var closedChecks = 0;
            LivinCreamInstaller.Install(payload, game, () => closedChecks++);
            Check(closedChecks > 3 && module.PackageMatches(game), "Installation verifies every owned file and rechecks game closure");
            File.WriteAllText(SafePath(game, "BepInEx/plugins/foreign.dll"), "preserve");
            var plugin = SafePath(game, LivinCreamInstaller.Plugin); File.WriteAllText(plugin, "foreign mutation");
            Check(!module.PackageMatches(game), "Trainer drift is detected");
            Reject(() => LivinCreamInstaller.Install(payload, game, () => {}));
            Check(File.ReadAllText(plugin) == "foreign mutation", "Modified plugin is preserved");
            File.Copy(SafePath(payload, LivinCreamInstaller.Plugin), plugin, true);
            LivinCreamInstaller.Remove(game, () => {});
            Check(!File.Exists(plugin) && File.Exists(SafePath(game, "winhttp.dll")) && File.Exists(SafePath(game, "BepInEx/plugins/foreign.dll")), "Removal preserves shared loader and unrelated mods");
            Manifest(files.Skip(1)); Reject(() => LivinCreamInstaller.ReadPackage(payload));
            Manifest(files.Append(new { path = "../outside.dll", sha256 = new string('a', 64) })); Reject(() => LivinCreamInstaller.ReadPackage(payload));
            Manifest(files); File.WriteAllText(SafePath(payload, LivinCreamInstaller.Plugin), "bad payload");
            Reject(() => LivinCreamInstaller.ReadPackage(payload));
            var installation = new GameInstallation(game, "fixture", true, false, false, true, "");
            var offline = TrainerStatus.Offline();
            Reject(() => module.SetValueAsync(installation, offline, "lc_money", 0.5m).GetAwaiter().GetResult());
            Reject(() => module.SendAsync(installation, offline, "lc_no_patience").GetAwaiter().GetResult());
            Reject(() => module.SendAsync(installation, offline, "unknown").GetAwaiter().GetResult());
            Reject(() => module.SetValueAsync(installation, offline, "lc_money", 100).GetAwaiter().GetResult());
            Check(!Directory.Exists(SafePath(game, "BepInEx/HappyCheats/livin-cream/commands")), "Offline controls fail without writing commands");
            Console.WriteLine($"Livin Cream module/installer: {checks} checks passed. Temporary fixtures only."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine("Livin Cream tests failed: " + ex); return 1; }
        finally { if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
