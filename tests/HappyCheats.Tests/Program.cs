using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;
internal static class Program
{
    private static int passed, failed;
    private static int Main(string[] args)
    {
        if (args.Contains("--witcher3")) return Witcher3Tests.RunAll() == 0 ? 0 : 1;
        if (args.Contains("--witcher3-installer")) return Witcher3InstallerTests.RunAll();
        if (args.Contains("--elden-ring")) return EldenRingTests.RunAll();
        if (args.Contains("--cache-cleanup")) return UpdateCacheCleanupTests.RunAll();
        if (args.Contains("--updates")) return UpdateTests.RunAll() | BackgroundUpdateTests.RunAll() | UpdateInstallerTests.RunAll() | UpdateCacheCleanupTests.RunAll();
        var legacy = OnimushaTrainer.BackendTests.Main();
        var controllers = HappyCheats.App.ControllerTests.Main();
        var onimusha = OnimushaModuleTests.RunAll();
        var astroneer = AstroneerTests.RunAll();
        var dawnwalker = DawnwalkerTests.RunAll();
        var wukong = WukongTests.RunAll();
        var phantomBlade = PhantomBladeTests.RunAll();
        var rdr2 = Rdr2Tests.RunAll();
        var gta = GtaVTests.RunAll();
        var gtaRuntime = GtaVRuntimeSetupTests.RunAllAsync().GetAwaiter().GetResult();
        var hospital = ProjectHospitalTests.RunAll();
        var hotel = HotelArchitectTests.RunAll();
        var civ6Setup = Civ6SetupTests.RunAll();
        var civ6Module = Civ6ModuleTests.RunAll();
        var civ7Setup = Civ7SetupTests.RunAll();
        var civ7Module = Civ7ModuleTests.RunAll();
        var eldenRing = EldenRingTests.RunAll();
        var crimson = CrimsonDesertTests.RunAll();
        var stardew = StardewTests.RunAll();
        var smapi = SmapiInstallerTests.RunAll();
        var updates = UpdateTests.RunAll();
        var backgroundUpdates = BackgroundUpdateTests.RunAll();
        var updateInstaller = UpdateInstallerTests.RunAll();
        var updateCacheCleanup = UpdateCacheCleanupTests.RunAll();
        var witcher3 = Witcher3Tests.RunAll();
        var witcher3Installer = Witcher3InstallerTests.RunAll();
        Run("Chinese Parents install and plugin removal preserve shared loader", () => {
            using var f = new Fixture(); OwnedInstaller.Install(f.Package, f.Game, () => { });
            Assert(OwnedInstaller.Installed(f.Game)); OwnedInstaller.Remove(f.Game, () => { });
            Assert(!OwnedInstaller.Installed(f.Game)); Assert(File.Exists(Path.Combine(f.Game, "winhttp.dll")));
        });
        Run("foreign loader blocks all writes", () => {
            using var f = new Fixture(); File.WriteAllText(Path.Combine(f.Game, "winhttp.dll"), "foreign");
            Fails(() => OwnedInstaller.Install(f.Package, f.Game, () => { }));
            Assert(!OwnedInstaller.Installed(f.Game)); Assert(File.ReadAllText(Path.Combine(f.Game, "winhttp.dll")) == "foreign");
        });
        Run("corrupt payload blocks all writes", () => {
            using var f = new Fixture(); File.AppendAllText(Path.Combine(f.Package, Fixture.Plugin), "corrupt");
            Fails(() => OwnedInstaller.Install(f.Package, f.Game, () => { })); Assert(!Directory.EnumerateFiles(f.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("modified plugin survives removal", () => {
            using var f = new Fixture(); OwnedInstaller.Install(f.Package, f.Game, () => { }); File.AppendAllText(Path.Combine(f.Game, Fixture.Plugin), "user change");
            Fails(() => OwnedInstaller.Remove(f.Game, () => { })); Assert(OwnedInstaller.Installed(f.Game));
        });
        Run("identical foreign plugin remains unowned", () => {
            using var f = new Fixture(); Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(f.Game, Fixture.Plugin))!); File.Copy(Path.Combine(f.Package, Fixture.Plugin), Path.Combine(f.Game, Fixture.Plugin));
            OwnedInstaller.Install(f.Package, f.Game, () => { }); OwnedInstaller.Remove(f.Game, () => { }); Assert(OwnedInstaller.Installed(f.Game));
        });
        Run("interrupted multi-file install rolls back", () => {
            using var f = new Fixture(); var calls = 0;
            Fails(() => OwnedInstaller.Install(f.Package, f.Game, () => { if (++calls == 4) throw new IOException("Game started"); }));
            Assert(!Directory.EnumerateFiles(f.Game, "*", SearchOption.AllDirectories).Any());
        });
        Run("running game blocks install", () => {
            using var f = new Fixture(); Fails(() => OwnedInstaller.Install(f.Package, f.Game, () => throw new IOException("running"))); Assert(!OwnedInstaller.Installed(f.Game));
        });
        Run("owned plugin updates with verified receipt", () => {
            using var f = new Fixture(); OwnedInstaller.Install(f.Package, f.Game, () => { }); File.AppendAllText(Path.Combine(f.Package, Fixture.Plugin), "v2"); f.Manifest();
            OwnedInstaller.Install(f.Package, f.Game, () => { }); Assert(Hash(Path.Combine(f.Game, Fixture.Plugin)) == Hash(Path.Combine(f.Package, Fixture.Plugin)));
        });
        Run("package traversal rejected", () => {
            using var f = new Fixture(); WriteJson(Path.Combine(f.Package, "package.json"), new { files = new[] { new { path = "BepInEx/core/../../outside", sha256 = new string('A', 64) } } });
            Fails(() => OwnedInstaller.Install(f.Package, f.Game, () => { }));
        });
        Run("safe path rejects absolute escape", () => { using var f = new Fixture(); Fails(() => SafePath(f.Game, f.Package)); });
        Run("empty plugin payload rejected", () => { using var f = new Fixture(); WriteJson(Path.Combine(f.Package, "package.json"), new { files = Array.Empty<object>() }); Fails(() => OwnedInstaller.ReadPackage(f.Package)); });
        Run("module catalog has distinct trainer controls", () => {
            var modules = ModuleCatalog.Create(); Assert(modules.Count == 17);
            Assert(modules.Select(module => module.Definition.Id).Distinct().Count() == modules.Count);
            Assert(modules.Single(module => module.Definition.Id == "civilization-vii") is CivilizationVIIModule);
            foreach (var module in modules) Assert(module.Definition.Features.Select(f => f.Id).Distinct().Count() == module.Definition.Features.Count);
        });
        Console.WriteLine($"HappyCheats tests: {passed} passed, {failed} failed.");
        return eldenRing != 0 || witcher3 != 0 || witcher3Installer != 0 || legacy != 0 || controllers != 0 || onimusha != 0 || astroneer != 0 || dawnwalker != 0 || wukong != 0 || phantomBlade != 0 || rdr2 != 0 || gta != 0 || gtaRuntime != 0 || hospital != 0 || hotel != 0 || civ6Setup != 0 || civ6Module != 0 || civ7Setup != 0 || civ7Module != 0 || crimson != 0 || stardew != 0 || smapi != 0 || updates != 0 || backgroundUpdates != 0 || updateInstaller != 0 || updateCacheCleanup != 0 || failed > 0 ? 1 : 0;
    }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation rejection."); }
    private static void Run(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); } }
    private sealed class Fixture : IDisposable
    {
        public const string Plugin = "BepInEx/plugins/HappyCheats.ChineseParents/HappyCheats.ChineseParents.dll";
        private readonly string root = Path.Combine(Path.GetTempPath(), "HappyCheats-tests-" + Guid.NewGuid().ToString("N"));
        public string Package => Path.Combine(root, "package"); public string Game => Path.Combine(root, "game");
        public Fixture() { Directory.CreateDirectory(Game); Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Package, Plugin))!); File.WriteAllText(Path.Combine(Package, Plugin), "test plugin"); File.WriteAllText(Path.Combine(Package, "winhttp.dll"), "test loader"); Manifest(); }
        public void Manifest() => WriteJson(Path.Combine(Package, "package.json"), new { files = new[] { "winhttp.dll", Plugin }.Select(p => new { path = p, sha256 = Hash(Path.Combine(Package, p)) }) });
        public void Dispose() { if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("HappyCheats-tests-")) Directory.Delete(root, true); }
    }
}
