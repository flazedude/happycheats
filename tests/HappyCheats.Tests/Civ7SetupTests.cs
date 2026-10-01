using System.Text;
using HappyCheats.Core;
using HappyCheats.Core.CivilizationVII;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class Civ7SetupTests
{
    private static int passed, failed;
    public static int RunAll()
    {
        Run("Civ VII shipped commented default and supported values round-trip exact bytes", () => {
            foreach (var line in new[] { ";EnableTuner -1", "  ; EnableTuner 1 ; example", "EnableTuner -1", "EnableTuner 2", "\tEnableTuner = 2  ; disabled" }) {
                using var f = new Fixture();
                File.WriteAllText(f.Options, ";Enable FireTuner. 1 : Enable, 2: Disable, -1 : Default\r\n" + line + "\r\n", new UTF8Encoding(true));
                var bytes = File.ReadAllBytes(f.Options);
                Assert(!Civ7Setup.IsTunerEnabled(f.Options));
                Civ7Setup.Install(f.Package, f.Options, Closed);
                Assert(Civ7Setup.IsTunerEnabled(f.Options) && Civ7Setup.Installed(f.Options));
                Civ7Setup.Remove(f.Options, Closed);
                Assert(File.ReadAllBytes(f.Options).SequenceEqual(bytes));
            }
        });
        Run("Civ VII active override takes precedence over commented defaults", () => {
            using var f = new Fixture();
            const string original = ";EnableTuner -1\nEnableTuner 2\n; EnableTuner 1\n";
            File.WriteAllText(f.Options, original);
            Civ7Setup.Install(f.Package, f.Options, Closed);
            Assert(File.ReadAllText(f.Options) == original.Replace("EnableTuner 2", "EnableTuner 1"));
            Civ7Setup.Remove(f.Options, Closed);
            Assert(File.ReadAllText(f.Options) == original);
        });
        Run("Civ VII saved Tuner status accepts manual enablement without creating ownership", () => {
            using var f = new Fixture();
            Assert(!Civ7Setup.IsTunerEnabled(f.Options));
            File.WriteAllText(f.Options, "Language zh_Hans\r\nEnableTuner 1\r\n");
            var original = File.ReadAllBytes(f.Options);
            Assert(Civ7Setup.IsTunerEnabled(f.Options) && !Civ7Setup.Installed(f.Options));
            Assert(File.ReadAllBytes(f.Options).SequenceEqual(original));
            Assert(!Directory.Exists(Path.Combine(f.Profile, "HappyCheats")));
            Fails(() => Civ7Setup.Remove(f.Options, Closed));
            Assert(File.ReadAllBytes(f.Options).SequenceEqual(original));
        });
        Run("Civ VII saved Tuner status ignores missing or damaged setup metadata", () => {
            using var f = new Fixture(); Civ7Setup.Install(f.Package, f.Options, Closed);
            File.AppendAllText(f.Backups().Single(), "changed backup");
            var options = File.ReadAllBytes(f.Options);
            var backup = File.ReadAllBytes(f.Backups().Single());
            Assert(Civ7Setup.IsTunerEnabled(f.Options));
            Fails(() => Civ7Setup.Installed(f.Options)); Fails(() => Civ7Setup.Remove(f.Options, Closed));
            File.WriteAllText(f.Receipt, "invalid ownership JSON");
            Assert(Civ7Setup.IsTunerEnabled(f.Options));
            Assert(File.ReadAllText(f.Receipt) == "invalid ownership JSON");
            File.Delete(f.Receipt);
            Assert(Civ7Setup.IsTunerEnabled(f.Options) && !Civ7Setup.Installed(f.Options));
            Assert(File.ReadAllBytes(f.Options).SequenceEqual(options) && File.ReadAllBytes(f.Backups().Single()).SequenceEqual(backup));
        });
        Run("Civ VII saved Tuner status never creates missing configuration", () => {
            using var f = new Fixture(); File.Delete(f.Options);
            Assert(!Civ7Setup.IsTunerEnabled(f.Options));
            var absentProfile = Path.Combine(f.Root, "not-created");
            Assert(!Civ7Setup.IsTunerEnabled(Path.Combine(absentProfile, "AppOptions.txt")));
            Assert(!File.Exists(f.Options) && !Directory.Exists(absentProfile) && !Directory.Exists(Path.Combine(f.Profile, "HappyCheats")));
        });
        Run("Civ VII malformed saved Tuner status remains diagnostic and read-only", () => {
            foreach (var content in new[] { "; EnableTuner 1\n;EnableTuner -1\n", "EnableTuner 1\nEnableTuner 0\n", "EnableTuner unexpected\n" }) {
                using var f = new Fixture(); File.WriteAllText(f.Options, content);
                Fails(() => Civ7Setup.IsTunerEnabled(f.Options));
                Assert(File.ReadAllText(f.Options) == content && !Directory.Exists(Path.Combine(f.Profile, "HappyCheats")));
            }
        });
        Run("Civ VII profile detection requires one existing profile and creates nothing", () => {
            using var f = new Fixture();
            var missing = Path.Combine(f.Root, "missing");
            Fails(() => Civ7Setup.FindOptions(new[] { missing }));
            Assert(!Directory.Exists(missing));
            Assert(Civ7Setup.FindOptions(new[] { missing, f.Profile, f.Profile }) == f.Options);
            var alternate = Path.Combine(f.Root, "alternate"); Directory.CreateDirectory(alternate);
            File.Copy(f.Options, Path.Combine(alternate, "AppOptions.txt"));
            Fails(() => Civ7Setup.FindOptions(new[] { f.Profile, alternate }));
        });
        Run("Civ VII setup enables only the exact setting and removal restores its original bytes", () => {
            using var f = new Fixture();
            var original = "; EnableTuner 1\r\nSound 99\r\n  EnableTuner\t0  ; local test\r\nLanguage zh_Hans\r\n";
            File.WriteAllText(f.Options, original, new UTF8Encoding(true));
            var bytes = File.ReadAllBytes(f.Options);
            Assert(!Civ7Setup.Installed(f.Options));
            Civ7Setup.Install(f.Package, f.Options, Closed);
            Assert(Civ7Setup.Installed(f.Options));
            Assert(File.ReadAllText(f.Options) == original.Replace("EnableTuner\t0", "EnableTuner\t1"));
            Assert(f.Backups().Length == 1 && File.ReadAllBytes(f.Backups()[0]).SequenceEqual(bytes));
            Civ7Setup.Remove(f.Options, Closed);
            Assert(!Civ7Setup.Installed(f.Options));
            Assert(File.ReadAllBytes(f.Options).SequenceEqual(bytes));
            Assert(f.Backups().Length == 1);
        });
        Run("Civ VII removal preserves later unrelated configuration changes", () => {
            using var f = new Fixture(); Civ7Setup.Install(f.Package, f.Options, Closed);
            File.AppendAllText(f.Options, "\nAudioVolume 22\n");
            Civ7Setup.Remove(f.Options, Closed);
            Assert(File.ReadAllText(f.Options) == Fixture.Initial + "\nAudioVolume 22\n");
        });
        Run("Civ VII setup preserves existing enabled Tuner and does not claim it", () => {
            using var f = new Fixture(); File.WriteAllText(f.Options, "EnableTuner 1\n");
            Civ7Setup.Install(f.Package, f.Options, Closed); Assert(Civ7Setup.Installed(f.Options));
            Civ7Setup.Remove(f.Options, Closed); Assert(File.ReadAllText(f.Options) == "EnableTuner 1\n");
            Assert(!Civ7Setup.Installed(f.Options));
            Civ7Setup.Install(f.Package, f.Options, Closed);
            File.WriteAllText(f.Options, "EnableTuner 0\nThirdPartySetting 3\n");
            Civ7Setup.Remove(f.Options, Closed);
            Assert(File.ReadAllText(f.Options) == "EnableTuner 0\nThirdPartySetting 3\n");
        });
        Run("Civ VII setup is idempotent and repeated installs retain immutable backups", () => {
            using var f = new Fixture(); Civ7Setup.Install(f.Package, f.Options, Closed);
            var receipt = File.ReadAllBytes(f.Receipt);
            Civ7Setup.Install(f.Package, f.Options, Closed);
            Assert(File.ReadAllBytes(f.Receipt).SequenceEqual(receipt) && f.Backups().Length == 1);
            Civ7Setup.Remove(f.Options, Closed); Civ7Setup.Install(f.Package, f.Options, Closed);
            Assert(f.Backups().Length == 2 && Civ7Setup.Installed(f.Options));
        });
        Run("Civ VII modified owned setting blocks removal and reinstall", () => {
            using var f = new Fixture(); Civ7Setup.Install(f.Package, f.Options, Closed);
            File.WriteAllText(f.Options, "EnableTuner 0\nUserChange 42\n");
            var bytes = File.ReadAllBytes(f.Options);
            Fails(() => Civ7Setup.Remove(f.Options, Closed)); Fails(() => Civ7Setup.Install(f.Package, f.Options, Closed));
            Assert(File.ReadAllBytes(f.Options).SequenceEqual(bytes) && File.Exists(f.Receipt));
        });
        Run("Civ VII configuration encoding and line endings survive setting edits", () => {
            foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true) }) {
                using var f = new Fixture();
                File.WriteAllText(f.Options, "Language 中文\r\nEnableTuner 0", encoding);
                var bytes = File.ReadAllBytes(f.Options);
                Civ7Setup.Install(f.Package, f.Options, Closed); Civ7Setup.Remove(f.Options, Closed);
                Assert(File.ReadAllBytes(f.Options).SequenceEqual(bytes));
            }
        });
        Run("Civ VII missing duplicate and malformed settings fail without writes", () => {
            foreach (var content in new[] { "; Missing EnableTuner\n", "EnableTuner 0\nEnableTuner 1\n", ";EnableTuner -1\n;EnableTuner 2\n", "EnableTuner true\n", "EnableTuner 0 unexpected\n" }) {
                using var f = new Fixture(); File.WriteAllText(f.Options, content);
                Fails(() => Civ7Setup.Install(f.Package, f.Options, Closed));
                Assert(File.ReadAllText(f.Options) == content && !Directory.Exists(Path.Combine(f.Profile, "HappyCheats")));
            }
        });
        Run("Civ VII corrupt payload cannot change settings or create metadata", () => {
            using var f = new Fixture(); File.AppendAllText(Path.Combine(f.Package, "runtime.js"), "changed");
            Fails(() => Civ7Setup.Install(f.Package, f.Options, Closed));
            Assert(File.ReadAllText(f.Options) == Fixture.Initial && !File.Exists(f.Receipt) && f.Backups().Length == 0);
        });
        Run("Civ VII runtime manifest rejects foreign app versions and extra paths", () => {
            using var f = new Fixture();
            Assert(Civ7Setup.ReadRuntime(f.Package) == "'fixture'\n");
            foreach (var app in new[] { "12345", "" }) {
                f.Manifest(app); Fails(() => Civ7Setup.ReadRuntime(f.Package));
            }
            f.Manifest(version: "0.2.0"); Fails(() => Civ7Setup.ReadRuntime(f.Package));
            WriteJson(Path.Combine(f.Package, "package.json"), new { app_id = "1295660", version = "0.1.0", files = new[] {
                new { path = "runtime.js", sha256 = Hash(Path.Combine(f.Package, "runtime.js")) }, new { path = "../other.lua", sha256 = new string('A', 64) }
            } });
            Fails(() => Civ7Setup.ReadRuntime(f.Package));
        });
        Run("Civ VII running-game guards reject early and final config writes", () => {
            using var f = new Fixture();
            Fails(() => Civ7Setup.Install(f.Package, f.Options, () => throw new IOException("running")));
            Assert(f.Backups().Length == 0 && File.ReadAllText(f.Options) == Fixture.Initial);
            var checks = 0;
            Fails(() => Civ7Setup.Install(f.Package, f.Options, () => { if (++checks == 3) throw new IOException("started"); }));
            Assert(checks == 3 && File.ReadAllText(f.Options) == Fixture.Initial && !File.Exists(f.Receipt));
            Assert(!Directory.EnumerateFiles(f.Profile, "*.tmp", SearchOption.AllDirectories).Any());
        });
        Run("Civ VII concurrent config edits are preserved before commit", () => {
            using var f = new Fixture(); var checks = 0;
            Fails(() => Civ7Setup.Install(f.Package, f.Options, () => { if (++checks == 3) File.AppendAllText(f.Options, "UserOption 7\n"); }));
            Assert(File.ReadAllText(f.Options) == Fixture.Initial + "UserOption 7\n" && !File.Exists(f.Receipt));
        });
        Run("Civ VII failed receipt commit rolls back only the unchanged trainer setting", () => {
            using var f = new Fixture(); var checks = 0;
            Fails(() => Civ7Setup.Install(f.Package, f.Options, () => { if (++checks == 3) File.WriteAllText(f.Receipt, "foreign receipt"); }));
            Assert(File.ReadAllText(f.Options) == Fixture.Initial && File.ReadAllText(f.Receipt) == "foreign receipt");
        });
        Run("Civ VII damaged original backup blocks changes", () => {
            using var f = new Fixture(); Civ7Setup.Install(f.Package, f.Options, Closed);
            File.AppendAllText(f.Backups().Single(), "altered");
            Fails(() => Civ7Setup.Remove(f.Options, Closed));
            Assert(File.ReadAllText(f.Options).Contains("EnableTuner 1") && File.Exists(f.Receipt));
        });
        Run("Civ VII missing profile and unowned removal never create a config", () => {
            using var f = new Fixture(); Fails(() => Civ7Setup.Remove(f.Options, Closed));
            File.Delete(f.Options); Fails(() => Civ7Setup.Install(f.Package, f.Options, Closed));
            Assert(!File.Exists(f.Options) && !File.Exists(f.Receipt));
        });
        Console.WriteLine($"Civilization VII setup tests: {passed} passed, {failed} failed.");
        return failed > 0 ? 1 : 0;
    }
    private static void Closed() { }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation rejection."); }
    private static void Run(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); } }
    private sealed class Fixture : IDisposable
    {
        public const string Initial = "; fixture only\n;EnableTuner -1\nSound 50\n";
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-civ7-setup-tests-" + Guid.NewGuid().ToString("N"));
        public string Profile => Path.Combine(Root, "profile");
        public string Options => Path.Combine(Profile, "AppOptions.txt");
        public string Receipt => Path.Combine(Profile, Civ7Setup.ReceiptRelativePath);
        public string Package => Path.Combine(Root, "package");
        public Fixture() {
            Directory.CreateDirectory(Profile); Directory.CreateDirectory(Package);
            File.WriteAllText(Options, Initial); File.WriteAllText(Path.Combine(Package, "runtime.js"), "'fixture'\n"); Manifest();
        }
        public string[] Backups() => Directory.EnumerateFiles(Profile, "AppOptions.original.*.txt", SearchOption.AllDirectories).ToArray();
        public void Manifest(string app = "1295660", string version = "0.1.0") => WriteJson(Path.Combine(Package, "package.json"), new { app_id = app, version, files = new[] { new { path = "runtime.js", sha256 = Hash(Path.Combine(Package, "runtime.js")) } } });
        public void Dispose() {
            var path = Path.GetFullPath(Root); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("HappyCheats-civ7-setup-tests-", StringComparison.Ordinal)) throw new IOException("Unexpected test cleanup path.");
            Directory.Delete(path, true);
        }
    }
}
