using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class Witcher3InstallerTests
{
    private static int passed, failed;

    public static int RunAll()
    {
        passed = failed = 0;
        Run("Witcher 3 generates an isolated hook and depot-relative bridge without changing game files", () => {
            using var f = new Fixture(); var before = f.BaseHashes(); f.Install();
            Check(Witcher3Installer.Installed(f.Game)); Check(f.Matches()); Check(f.BaseHashes().SequenceEqual(before));
            var script = File.ReadAllText(f.Path(Witcher3Installer.MainScript));
            Check(!script.Contains("__HC_BRIDGE_PATH__")); Check(script.Contains("\"" + Witcher3Installer.BridgeDepotPath + "\""));
            Check(!script.Contains(f.Game, StringComparison.OrdinalIgnoreCase));
            var hook = File.ReadAllText(f.Path(Witcher3Installer.PatchedScript));
            Check(hook.Contains("private var happyCheatsWitcher3 : HappyCheatsWitcher3;"));
            Check(hook.Contains("happyCheatsWitcher3 = new HappyCheatsWitcher3 in this;"));
            Check(hook.IndexOf("happyCheatsWitcher3.Tick();", StringComparison.Ordinal) < hook.IndexOf("OriginalTick();", StringComparison.Ordinal));
            Check(File.ReadAllText(f.Path(Witcher3Installer.CommandPath)) == "schema,session,request,action,enabled,issued\n");
            Check(Witcher3Installer.CommandPath == "content/content0/happycheats_witcher3/command.csv");
            Check(Witcher3Installer.LeasePath == "content/content0/happycheats_witcher3/lease.csv");
            Check(!File.Exists(f.Path(Witcher3Installer.LegacyCommandPath)));
            Check(File.ReadAllText(f.Path(Witcher3Installer.LeasePath)) == "session,issued\n");
            Check(File.ReadAllText(f.Path(Witcher3Installer.StatusPath)) == "[MODS]\nSnapshot=HCW3|pending\n");
        });
        Run("Witcher 3 repeated install is idempotent and resets recognized stale bridge state", () => {
            using var f = new Fixture(); f.Install(); var receipt = File.ReadAllText(f.Path(Witcher3Installer.ReceiptPath));
            f.GameFile(Witcher3Installer.CommandPath, "schema,session,request,action,enabled,issued\n1,session,request,witcher_health,true,123\n");
            f.GameFile(Witcher3Installer.StatusPath, "[MODS]\r\nSnapshot=HCW3|0.1.0|session|1|Ready\r\n");
            f.Install(); Check(f.Matches()); Check(File.ReadAllText(f.Path(Witcher3Installer.ReceiptPath)) == receipt);
            Check(File.ReadAllText(f.Path(Witcher3Installer.CommandPath)).Split('\n').Length == 2);
        });
        Run("Witcher 3 changed payload updates only owned scripts", () => {
            using var f = new Fixture(); f.Install(); f.Template(Fixture.TemplateText + "\n// Updated trainer\n"); f.Manifest();
            Check(!f.Matches()); f.Install(); Check(f.Matches());
            Check(File.ReadAllText(f.Path(Witcher3Installer.MainScript)).Contains("Updated trainer"));
        });
        Run("Witcher 3 missing mailbox makes the installation incomplete and is repaired", () => {
            using var f = new Fixture(); f.Install(); File.Delete(f.Path(Witcher3Installer.LeasePath));
            Check(!Witcher3Installer.Installed(f.Game)); Check(!f.Matches()); f.Install(); Check(f.Matches());
        });
        Run("Witcher 3 upgrade migrates owned mod mailboxes into the physical content depot", () => {
            using var f = new Fixture(); f.InstallLegacy(); var baseHashes = f.BaseHashes();
            Check(!Witcher3Installer.Installed(f.Game));
            f.GameFile(Witcher3Installer.LegacyCommandPath, "schema,session,request,action,enabled,issued\n1,old,request,witcher3_health,1,123\n");
            f.Install(); Check(f.Matches()); Check(f.BaseHashes().SequenceEqual(baseHashes));
            Check(!File.Exists(f.Path(Witcher3Installer.LegacyCommandPath))); Check(!File.Exists(f.Path(Witcher3Installer.LegacyLeasePath)));
            Check(File.ReadAllText(f.Path(Witcher3Installer.CommandPath)) == "schema,session,request,action,enabled,issued\n");
        });
        Run("Witcher 3 migration preserves unexpected edits in legacy mailboxes", () => {
            using var f = new Fixture(); f.InstallLegacy(); f.GameFile(Witcher3Installer.LegacyCommandPath, "user edited file");
            f.Install(); Check(f.Matches()); Check(File.ReadAllText(f.Path(Witcher3Installer.LegacyCommandPath)) == "user edited file");
            Witcher3Installer.Remove(f.Game, () => { }); Check(File.ReadAllText(f.Path(Witcher3Installer.LegacyCommandPath)) == "user edited file");
            Check(!File.Exists(f.Path(Witcher3Installer.CommandPath)));
        });
        Run("Witcher 3 migration restores legacy mailboxes if commit is interrupted", () => {
            using var f = new Fixture(); f.InstallLegacy(); var before = f.AllHashes();
            Fails(() => f.Install(() => {
                if (!File.Exists(f.Path(Witcher3Installer.LegacyCommandPath))) throw new IOException("Game started");
            }));
            Check(f.AllHashes().SequenceEqual(before));
        });
        Run("Witcher 3 removal accepts receipts from the legacy bridge location", () => {
            using var f = new Fixture(); f.InstallLegacy(); var baseHashes = f.BaseHashes();
            Witcher3Installer.Remove(f.Game, () => { }); Check(f.BaseHashes().SequenceEqual(baseHashes));
            Check(!File.Exists(f.Path(Witcher3Installer.LegacyCommandPath))); Check(!File.Exists(f.Path(Witcher3Installer.LegacyLeasePath)));
            Check(!File.Exists(f.Path(Witcher3Installer.MainScript)));
        });
        Run("Witcher 3 production profile rejects fixture executables", () => {
            using var f = new Fixture(); Fails(() => Witcher3Installer.Install(f.Package, f.Game, () => { })); f.CheckPristine();
        });
        Run("Witcher 3 rejects changed executable and vanilla script before writing", () => {
            foreach (var relative in new[] { Witcher3Installer.Executable, Witcher3Installer.VanillaScript }) {
                using var f = new Fixture(); f.GameFile(relative, "modified"); var before = f.AllHashes();
                Fails(f.Install); Check(f.AllHashes().SequenceEqual(before));
            }
        });
        Run("Witcher 3 verifies payload checksum and integration points", () => {
            using var f = new Fixture(); f.Template(Fixture.TemplateText + "corrupt"); Fails(f.Install); f.CheckPristine();
            f.Manifest(); f.GameFile(Witcher3Installer.VanillaScript, "import class CR4Game extends CCommonGame { public function Other() {} }");
            Fails(() => f.InstallWith(new(f.Profile.ExecutableSha256, Hash(f.Path(Witcher3Installer.VanillaScript))))); f.CheckPristine();
        });
        Run("Witcher 3 requires exactly one safe template manifest entry and correct identity", () => {
            using var f = new Fixture();
            foreach (var path in new[] { "../happycheats.ws", Witcher3Installer.VanillaScript, "./happycheats.ws", "HAPPYCHEATS.WS" }) {
                f.WriteManifest(new[] { new Witcher3Installer.Entry(path, new string('A', 64)) }); Fails(f.Install); f.CheckPristine();
            }
            f.WriteManifest(Array.Empty<Witcher3Installer.Entry>()); Fails(f.Install);
            var entry = new Witcher3Installer.Entry("happycheats.ws", Hash(System.IO.Path.Combine(f.Package, "happycheats.ws")));
            f.WriteManifest(new[] { entry, entry }); Fails(f.Install);
            WriteJson(System.IO.Path.Combine(f.Package, "package.json"), new { schemaVersion = 1, game = "another-game", version = Witcher3Module.TrainerVersion, files = new[] { entry } }); Fails(f.Install);
            f.CheckPristine();
        });
        Run("Witcher 3 rejects a missing bridge placeholder", () => {
            using var f = new Fixture(); f.Template("class HappyCheatsWitcher3 { function Tick() {} }"); f.Manifest(); Fails(f.Install); f.CheckPristine();
        });
        Run("Witcher 3 conflicting script mods are preserved and block installation", () => {
            foreach (var relative in new[] { "Mods/modOther/content/scripts/game/r4game.ws", "Mods/mod0000_MergedFiles/content/scripts/game/R4GAME.WS" }) {
                using var f = new Fixture(); f.GameFile(relative, "foreign override"); var before = f.AllHashes();
                Fails(f.Install); Check(f.AllHashes().SequenceEqual(before));
            }
        });
        Run("Witcher 3 foreign or modified target files are never overwritten", () => {
            foreach (var relative in new[] { Witcher3Installer.MainScript, Witcher3Installer.PatchedScript, Witcher3Installer.CommandPath, Witcher3Installer.LeasePath, Witcher3Installer.StatusPath }) {
                using var f = new Fixture(); f.GameFile(relative, "foreign"); var before = f.AllHashes(); Fails(f.Install); Check(f.AllHashes().SequenceEqual(before));
            }
            using var modified = new Fixture(); modified.Install(); modified.GameFile(Witcher3Installer.MainScript, "user edit"); var snapshot = modified.AllHashes();
            Fails(modified.Install); Check(modified.AllHashes().SequenceEqual(snapshot));
        });
        Run("Witcher 3 installation rolls back at every game-closed guard", () => {
            int checks = 0; using (var probe = new Fixture()) probe.Install(() => checks++);
            Check(checks > 5);
            for (var stop = 1; stop <= checks; stop++) {
                using var f = new Fixture(); var seen = 0; Fails(() => f.Install(() => { if (++seen == stop) throw new IOException("Game started"); })); f.CheckPristine();
            }
        });
        Run("Witcher 3 concurrent target appearance is preserved and prior writes roll back", () => {
            using var f = new Fixture(); bool edited = false;
            Fails(() => f.Install(() => {
                if (!edited && File.Exists(f.Path(Witcher3Installer.CommandPath))) { edited = true; f.GameFile(Witcher3Installer.MainScript, "concurrent foreign script"); }
            }));
            Check(edited); Check(File.ReadAllText(f.Path(Witcher3Installer.MainScript)) == "concurrent foreign script");
            Check(!File.Exists(f.Path(Witcher3Installer.CommandPath))); Check(!File.Exists(f.Path(Witcher3Installer.ReceiptPath)));
        });
        Run("Witcher 3 base update during installation is preserved and rolls back the generated mod", () => {
            using var f = new Fixture(); bool changed = false;
            Fails(() => f.Install(() => {
                if (!changed && File.Exists(f.Path(Witcher3Installer.PatchedScript))) {
                    changed = true; f.GameFile(Witcher3Installer.VanillaScript, "Concurrent game update");
                }
            }));
            Check(changed); f.CheckPristine(); Check(File.ReadAllText(f.Path(Witcher3Installer.VanillaScript)) == "Concurrent game update");
        });
        Run("Witcher 3 update rollback preserves concurrent edits and saves the previous script", () => {
            using var f = new Fixture(); f.Install(); var old = File.ReadAllText(f.Path(Witcher3Installer.MainScript));
            f.Template(Fixture.TemplateText + "\n// Update candidate\n"); f.Manifest(); bool edited = false;
            Fails(() => f.Install(() => {
                if (!edited && File.ReadAllText(f.Path(Witcher3Installer.MainScript)).Contains("Update candidate")) {
                    edited = true; f.GameFile(Witcher3Installer.MainScript, "concurrent edit"); throw new IOException("Game started");
                }
            }));
            Check(edited); Check(File.ReadAllText(f.Path(Witcher3Installer.MainScript)) == "concurrent edit");
            Check(Directory.EnumerateFiles(f.Path("happycheats-recovery"), "happycheats.ws", SearchOption.AllDirectories).Any(path => File.ReadAllText(path) == old));
        });
        Run("Witcher 3 owned removal preserves all unrelated game and mod files", () => {
            using var f = new Fixture(); f.Install(); var baseHashes = f.BaseHashes(); f.GameFile("Mods/modOther/content/scripts/local/other.ws", "other mod");
            f.GameFile(Witcher3Installer.BridgeRoot + "/keep.txt", "unowned");
            f.GameFile(Witcher3Installer.StatusPath, "[MODS]\nSnapshot=HCW3|0.1.0|session|1|Ready\n");
            Witcher3Installer.Remove(f.Game, () => { }); Check(!Witcher3Installer.Installed(f.Game));
            Check(!File.Exists(f.Path(Witcher3Installer.CommandPath))); Check(!File.Exists(f.Path(Witcher3Installer.StatusPath)));
            Check(f.BaseHashes().SequenceEqual(baseHashes)); Check(File.ReadAllText(f.Path(Witcher3Installer.BridgeRoot + "/keep.txt")) == "unowned");
            Check(File.Exists(f.Path("Mods/modOther/content/scripts/local/other.ws")));
        });
        Run("Witcher 3 modified scripts preserve both dependent files on removal", () => {
            foreach (var relative in new[] { Witcher3Installer.MainScript, Witcher3Installer.PatchedScript }) {
                using var f = new Fixture(); f.Install(); File.AppendAllText(f.Path(relative), "\n// User edit"); var before = f.AllHashes();
                Witcher3Installer.Remove(f.Game, () => { }); Check(f.AllHashes().SequenceEqual(before));
            }
        });
        Run("Witcher 3 identical unowned script remains unowned on removal", () => {
            using var f = new Fixture(); f.Install(); var receiptPath = f.Path(Witcher3Installer.ReceiptPath);
            var receipt = JsonSerializer.Deserialize<Witcher3Installer.Receipt>(File.ReadAllBytes(receiptPath))!;
            WriteJson(receiptPath, receipt with { Files = receipt.Files.Where(file => file.Path != Witcher3Installer.MainScript).ToList() });
            f.Install(); var before = f.AllHashes(); Witcher3Installer.Remove(f.Game, () => { }); Check(f.AllHashes().SequenceEqual(before));
        });
        Run("Witcher 3 modified dynamic file is preserved on removal", () => {
            using var f = new Fixture(); f.Install(); f.GameFile(Witcher3Installer.StatusPath, "user configuration");
            Witcher3Installer.Remove(f.Game, () => { }); Check(!Witcher3Installer.Installed(f.Game));
            Check(File.ReadAllText(f.Path(Witcher3Installer.StatusPath)) == "user configuration");
            Fails(f.Install);
        });
        Run("Witcher 3 removal rolls back at every game-closed guard", () => {
            int checks = 0; using (var probe = new Fixture()) { probe.Install(); Witcher3Installer.Remove(probe.Game, () => checks++); }
            for (var stop = 1; stop <= checks; stop++) {
                using var f = new Fixture(); f.Install(); var before = f.AllHashes(); var seen = 0;
                Fails(() => Witcher3Installer.Remove(f.Game, () => { if (++seen == stop) throw new IOException("Game started"); }));
                Check(f.AllHashes().SequenceEqual(before));
            }
        });
        Run("Witcher 3 forged receipts cannot delete arbitrary files", () => {
            using var f = new Fixture(); f.GameFile("keep.txt", "keep");
            WriteJson(f.Path(Witcher3Installer.ReceiptPath), new Witcher3Installer.Receipt(1, "witcher-3", new() { new("keep.txt", Hash(f.Path("keep.txt"))) }));
            Fails(() => Witcher3Installer.Remove(f.Game, () => { })); Check(File.ReadAllText(f.Path("keep.txt")) == "keep");
        });
        Run("Witcher 3 target directories and ownership directories are rejected", () => {
            foreach (var relative in new[] { Witcher3Installer.MainScript, Witcher3Installer.ReceiptPath }) {
                using var f = new Fixture(); Directory.CreateDirectory(f.Path(relative)); Fails(f.Install); f.CheckPristine();
            }
        });
        Run("Witcher 3 linked mod directory cannot redirect writes", () => {
            using var f = new Fixture(); var outside = System.IO.Path.Combine(f.BaseDirectory, "outside"); Directory.CreateDirectory(outside);
            var link = f.Path("Mods"); var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/c", "mklink", "/J", link, outside }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!; process.WaitForExit(); Check(process.ExitCode == 0);
            try { Fails(f.Install); Check(!Directory.EnumerateFileSystemEntries(outside).Any()); }
            finally { Directory.Delete(link); }
            f.CheckPristine();
        });
        Console.WriteLine($"Witcher 3 installer tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch (Exception error) when (error is IOException or AggregateException or JsonException) { return; } throw new Exception("Expected rejection."); }
    private static void Run(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }

    private sealed class Fixture : IDisposable
    {
        public const string TemplateText = "class HappyCheatsWitcher3 { public function Tick() { var bridge : string; bridge = \"__HC_BRIDGE_PATH__\"; } }\n";
        private const string VanillaText = "// Locally authored test fixture; no game source\r\nimport class CR4Game extends CCommonGame\r\n{\r\n\tpublic function OnTick()\r\n\t{\r\n\t\tOriginalTick();\r\n\t}\r\n}\r\n";
        public string BaseDirectory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HappyCheats-Witcher3-tests-" + Guid.NewGuid().ToString("N"));
        public string Game => System.IO.Path.Combine(BaseDirectory, "game folder");
        public string Package => System.IO.Path.Combine(BaseDirectory, "package");
        public Witcher3Installer.BuildProfile Profile { get; }
        public Fixture()
        {
            GameFile(Witcher3Installer.Executable, "Verified test binary"); GameFile(Witcher3Installer.VanillaScript, VanillaText);
            Profile = new(Hash(Path(Witcher3Installer.Executable)), Hash(Path(Witcher3Installer.VanillaScript)));
            Template(TemplateText); Manifest();
        }
        public string Path(string relative) => SafePath(Game, relative);
        public void GameFile(string relative, string text) => Put(Path(relative), text);
        public void Template(string text) => Put(SafePath(Package, "happycheats.ws"), text);
        private static void Put(string path, string text) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
        public void Manifest() => WriteManifest(new[] { new Witcher3Installer.Entry("happycheats.ws", Hash(SafePath(Package, "happycheats.ws"))) });
        public void WriteManifest(IEnumerable<Witcher3Installer.Entry> entries) => WriteJson(SafePath(Package, "package.json"), new { schemaVersion = 1, game = "witcher-3", version = Witcher3Module.TrainerVersion, files = entries.Select(file => new { path = file.Path, sha256 = file.Sha256 }) });
        public void Install() => Install(() => { });
        public void Install(Action check) => Witcher3Installer.Install(Package, Game, check, null, Profile);
        public void InstallLegacy()
        {
            Install();
            var receiptPath = Path(Witcher3Installer.ReceiptPath);
            var receipt = JsonSerializer.Deserialize<Witcher3Installer.Receipt>(File.ReadAllBytes(receiptPath))!;
            foreach (var (current, legacy) in new[] {
                (Witcher3Installer.CommandPath, Witcher3Installer.LegacyCommandPath),
                (Witcher3Installer.LeasePath, Witcher3Installer.LegacyLeasePath) })
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path(legacy))!);
                File.Move(Path(current), Path(legacy));
                receipt.Files[receipt.Files.FindIndex(file => file.Path == current)] = new(legacy, Hash(Path(legacy)));
            }
            WriteJson(receiptPath, receipt);
        }
        public void InstallWith(Witcher3Installer.BuildProfile profile) => Witcher3Installer.Install(Package, Game, () => { }, null, profile);
        public bool Matches() => Witcher3Installer.MatchesPackage(Package, Game, Profile);
        public string[] BaseHashes() => new[] { Hash(Path(Witcher3Installer.Executable)), Hash(Path(Witcher3Installer.VanillaScript)) };
        public string[] AllHashes() => Directory.EnumerateFiles(Game, "*", SearchOption.AllDirectories).Select(path => System.IO.Path.GetRelativePath(Game, path) + ":" + Hash(path)).Order(StringComparer.Ordinal).ToArray();
        public void CheckPristine() => Check(Directory.EnumerateFiles(Game, "*", SearchOption.AllDirectories).Count() == 2);
        public void Dispose()
        {
            var resolved = System.IO.Path.GetFullPath(BaseDirectory);
            var temporary = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(resolved).StartsWith("HappyCheats-Witcher3-tests-", StringComparison.Ordinal)) throw new IOException("Invalid fixture cleanup path.");
            Directory.Delete(resolved, true);
        }
    }
}
