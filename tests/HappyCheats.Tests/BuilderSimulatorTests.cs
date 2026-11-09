using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;
internal static class BuilderSimulatorTests
{
    public static int RunAll()
    {
        var passed = 0;
        void Check(bool condition) { if (!condition) throw new Exception("Builder Simulator assertion failed."); passed++; }
        void Reject(Action action) { try { action(); } catch (IOException) { passed++; return; } throw new Exception("Unsafe Builder Simulator operation accepted."); }
        var root = Path.Combine(Path.GetTempPath(), "hc-builder-" + Guid.NewGuid().ToString("N"));
        var package = Path.Combine(root, "package"); var game = Path.Combine(root, "game");
        Directory.CreateDirectory(game);
        try {
            var module = new BuilderSimulatorModule(root);
            Check(module.GameExecutables.SequenceEqual(new[] { "Builder Simulator.exe" }));
            Check(module.Definition.AppId == "1120320" && module.Definition.Features.Count == 7);
            Check(module.Definition.Features.Count(f => f.IsToggle) == 6);
            Check(module is IFeatureChoiceModule);
            Check(module.Definition.Features.Where(f => f.Choices != null).Count() == 3);
            Check(module.Definition.Features.Single(f => f.Id == "game_speed").Choices!.Select(c => c.Value).SequenceEqual(new[] { "0.5", "1.5", "2", "3" }));
            Check(module.Definition.Features.All(f => f.Name.Contains(" / ") && f.Description.Contains(" / ")));
            Check(BuilderSimulatorModule.SupportsAssembly(BuilderSimulatorModule.SupportedAssemblyHash.ToLowerInvariant()));
            Check(!BuilderSimulatorModule.SupportsAssembly(new string('0', 64)));
            Check(!BuilderSimulatorModule.SupportsAssembly("364320218449C22DB4A6E851276F4236CDC51DC74FCBF8EBA4AFB7FDFC1556F0"));
            void Manifest(string id = "builder-simulator") => WriteJson(Path.Combine(package, "package.json"), new {
                schema_version = 1, game_id = id, steam_app_id = 1120320, version = BuilderSimulatorModule.TrainerVersion,
                game_assembly_sha256 = BuilderSimulatorModule.SupportedAssemblyHash, loader = new { architecture = "win-x64" },
                files = BuilderSimulatorInstaller.RequiredFiles.Select(p => new { path = p, sha256 = Hash(SafePath(package, p)) }).ToArray()
            });
            foreach (var path in BuilderSimulatorInstaller.RequiredFiles) {
                var full = SafePath(package, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, path);
            }
            Manifest("wrong-game"); Reject(() => BuilderSimulatorInstaller.ReadPackage(package)); Manifest();
            Reject(() => BuilderSimulatorInstaller.Install(package, game, () => throw new IOException("Game running")));
            Check(!Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories).Any());
            File.WriteAllText(SafePath(game, "winhttp.dll"), "foreign");
            Reject(() => BuilderSimulatorInstaller.Install(package, game, () => { }));
            Check(File.ReadAllText(SafePath(game, "winhttp.dll")) == "foreign"); File.Delete(SafePath(game, "winhttp.dll"));
            var calls = 0;
            Reject(() => BuilderSimulatorInstaller.Install(package, game, () => { if (++calls == 5) throw new IOException("Launched during install"); }));
            Check(!Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories).Any());
            BuilderSimulatorInstaller.Install(package, game, () => { }); Check(BuilderSimulatorInstaller.Installed(game));
            Check(BuilderSimulatorModule.MatchesInstalledPackage(package, game));
            File.AppendAllText(SafePath(game, BuilderSimulatorInstaller.Plugin), "user modification");
            Reject(() => BuilderSimulatorInstaller.Remove(game, () => { }));
            Check(File.ReadAllText(SafePath(game, BuilderSimulatorInstaller.Plugin)).EndsWith("user modification"));
            File.Copy(SafePath(package, BuilderSimulatorInstaller.Plugin), SafePath(game, BuilderSimulatorInstaller.Plugin), true);
            BuilderSimulatorInstaller.Remove(game, () => { });
            Check(!File.Exists(SafePath(game, BuilderSimulatorInstaller.Plugin)) && File.Exists(SafePath(game, "winhttp.dll")));
            File.AppendAllText(SafePath(package, BuilderSimulatorInstaller.Plugin), "corruption");
            Reject(() => BuilderSimulatorInstaller.ReadPackage(package));
            try { module.SelectAsync(new GameInstallation(game, "", false, false, false, false, ""), TrainerStatus.Offline(), "game_speed", "NaN"); throw new Exception("Invalid multiplier accepted"); }
            catch (ArgumentException) { passed++; }
            try { module.SendAsync(new GameInstallation(game, "", false, false, false, false, ""), TrainerStatus.Offline(), "unlimited_money"); throw new Exception("Implicit toggle accepted"); }
            catch (ArgumentException) { passed++; }
            var absent = module.DiscoverAsync(game).GetAwaiter().GetResult(); Check(!absent.Compatible);
            try { module.SendAsync(absent, TrainerStatus.Offline(), "max_skill_level").GetAwaiter().GetResult(); throw new Exception("Disconnected command accepted"); }
            catch (InvalidOperationException) { passed++; }
            var gate = (SemaphoreSlim)typeof(BuilderSimulatorModule).GetField("commandLock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(module)!;
            gate.Wait();
            try {
                foreach (var operation in new Func<Task>[] { () => module.InstallAsync(absent), () => module.RemoveAsync(absent), () => module.SendAsync(absent, TrainerStatus.Offline(), "max_skill_level") }) {
                    try { operation().GetAwaiter().GetResult(); throw new Exception("Overlapping mutation accepted"); }
                    catch (InvalidOperationException) { passed++; }
                }
            } finally { gate.Release(); }
            Console.WriteLine($"PASS Builder Simulator: {passed} metadata/installation/dispatch checks (offline fixtures).");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
