using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;
internal static class NomadDriveTests
{
    public static int RunAll()
    {
        var passed = 0;
        void Check(bool condition) { if (!condition) throw new Exception("Nomad Drive assertion failed."); passed++; }
        void Reject(Action action) { try { action(); } catch (IOException) { passed++; return; } throw new Exception("Unsafe Nomad Drive operation accepted."); }
        var root = Path.Combine(Path.GetTempPath(), "hc-nomad-" + Guid.NewGuid().ToString("N"));
        var package = Path.Combine(root, "package"); var game = Path.Combine(root, "game");
        Directory.CreateDirectory(game);
        try {
            var module = new NomadDriveModule(root);
            Check(module.GameExecutables.SequenceEqual(new[] { "Nomad Drive.exe" }));
            Check(module.Definition.AppId == "2223980" && module.Definition.Features.Count == 11);
            Check(module.Definition.Features.Count(f => f.IsToggle) == 4);
            Check(NomadDriveModule.SupportsAssembly(NomadDriveModule.SupportedAssemblyHash.ToLowerInvariant()));
            Check(!NomadDriveModule.SupportsAssembly(new string('0', 64)));
            var bootLog = Path.Combine(game, "preloader_test.log");
            File.WriteAllText(bootLog, "System.MissingMethodException: Method not found: Module.GetPEKind");
            Check(NomadDriveModule.ReadStartupFailure(game, DateTime.UtcNow.AddMinutes(-1))?.Contains("GetPEKind") == true);
            Check(NomadDriveModule.ReadStartupFailure(game, DateTime.UtcNow.AddMinutes(1)) == null);
            File.Delete(bootLog);
            void Manifest(string id = "nomad-drive") => WriteJson(Path.Combine(package, "package.json"), new {
                schema_version = 1, game_id = id, steam_app_id = 2223980, version = NomadDriveModule.TrainerVersion,
                game_assembly_sha256 = NomadDriveModule.SupportedAssemblyHash, loader = new { architecture = "win-x64" },
                files = NomadDriveInstaller.RequiredFiles.Select(p => new { path = p, sha256 = Hash(SafePath(package, p)) }).ToArray()
            });
            foreach (var path in NomadDriveInstaller.RequiredFiles) {
                var full = SafePath(package, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, path);
            }
            Manifest("wrong-game"); Reject(() => NomadDriveInstaller.ReadPackage(package)); Manifest();
            Reject(() => NomadDriveInstaller.Install(package, game, () => throw new IOException("Game running")));
            Check(!Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories).Any());
            File.WriteAllText(SafePath(game, "winhttp.dll"), "foreign");
            Reject(() => NomadDriveInstaller.Install(package, game, () => { }));
            Check(File.ReadAllText(SafePath(game, "winhttp.dll")) == "foreign"); File.Delete(SafePath(game, "winhttp.dll"));
            var calls = 0;
            Reject(() => NomadDriveInstaller.Install(package, game, () => { if (++calls == 5) throw new IOException("Launched during install"); }));
            Check(!Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories).Any());
            NomadDriveInstaller.Install(package, game, () => { }); Check(NomadDriveInstaller.Installed(game));
            Check(NomadDriveModule.MatchesInstalledPackage(package, game));
            // Exercise the existing ownership receipt upgrade used by the in-app updater.
            var oldPlugin = SafePath(game, NomadDriveInstaller.Plugin);
            File.WriteAllText(oldPlugin, "owned 1.0.0 plugin");
            var runtimeFiles = NomadDriveInstaller.RequiredFiles.Where(p => p.StartsWith("BepInEx/nomad-runtime/", StringComparison.Ordinal)).ToArray();
            var oldFiles = NomadDriveInstaller.RequiredFiles.Except(runtimeFiles).Select(p => new NomadDriveInstaller.Entry(p, Hash(SafePath(game, p)))).ToList();
            Check(oldFiles.Count == 16 && runtimeFiles.Length == 4);
            foreach (var path in runtimeFiles) File.Delete(SafePath(game, path));
            WriteJson(SafePath(game, NomadDriveInstaller.ReceiptPath), new NomadDriveInstaller.Receipt("nomad-drive", oldFiles));
            var progress = new List<string>();
            NomadDriveInstaller.Install(package, game, () => { }, new ReportProgress(progress.Add));
            Check(NomadDriveModule.MatchesInstalledPackage(package, game));
            Check(progress.Any(message => message.Contains("Verifying installed")));
            var upgradedReceipt = ReadJson(SafePath(game, NomadDriveInstaller.ReceiptPath));
            Check(Object(upgradedReceipt, "Files").GetArrayLength() == 20);
            // A second Update/Install must accept its own expanded ownership record.
            NomadDriveInstaller.Install(package, game, () => { });
            Check(NomadDriveModule.MatchesInstalledPackage(package, game));
            var receiptText = File.ReadAllText(SafePath(game, NomadDriveInstaller.ReceiptPath));
            File.WriteAllText(SafePath(game, NomadDriveInstaller.ReceiptPath), receiptText.Replace(runtimeFiles[0], "Nomad Drive_Data/Managed/mscorlib.dll"));
            Reject(() => NomadDriveInstaller.Install(package, game, () => { }));
            File.WriteAllText(SafePath(game, NomadDriveInstaller.ReceiptPath), receiptText);
            var verificationGame = Path.Combine(root, "verification-game"); Directory.CreateDirectory(verificationGame);
            Reject(() => NomadDriveInstaller.Install(package, verificationGame, () => { }, new ReportProgress(message => {
                if (message.StartsWith("Verifying installed")) File.AppendAllText(SafePath(verificationGame, NomadDriveInstaller.Plugin), "external change");
            })));
            Check(!File.Exists(SafePath(verificationGame, NomadDriveInstaller.ReceiptPath)));
            Check(File.ReadAllText(SafePath(verificationGame, NomadDriveInstaller.Plugin)).EndsWith("external change"));
            File.AppendAllText(SafePath(game, NomadDriveInstaller.Plugin), "user modification");
            Reject(() => NomadDriveInstaller.Remove(game, () => { }));
            Check(File.ReadAllText(SafePath(game, NomadDriveInstaller.Plugin)).EndsWith("user modification"));
            File.Copy(SafePath(package, NomadDriveInstaller.Plugin), SafePath(game, NomadDriveInstaller.Plugin), true);
            NomadDriveInstaller.Remove(game, () => { });
            Check(!File.Exists(SafePath(game, NomadDriveInstaller.Plugin)) && File.Exists(SafePath(game, "winhttp.dll")));
            File.AppendAllText(SafePath(package, NomadDriveInstaller.Plugin), "corruption");
            Reject(() => NomadDriveInstaller.ReadPackage(package));
            var absent = module.DiscoverAsync(game).GetAwaiter().GetResult(); Check(!absent.Compatible);
            try { module.SendAsync(absent, TrainerStatus.Offline(), "heal").GetAwaiter().GetResult(); throw new Exception("Disconnected command accepted"); }
            catch (InvalidOperationException) { passed++; }
            Console.WriteLine($"PASS Nomad Drive: {passed} metadata/installation/dispatch checks (offline fixtures).");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(root, true); }
    }
    private sealed class ReportProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
