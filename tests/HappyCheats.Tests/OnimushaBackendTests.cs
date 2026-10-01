#nullable disable
// Compile separately with TrainerBackend.cs; never distributed with the launcher.
// These tests touch only unique temporary fixtures and never launch the fake game.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace OnimushaTrainer
{
    internal static class BackendTests
    {
        private static readonly string[] PayloadFiles = {
            "dinput8.dll", "reframework/autorun/onimusha_trainer.lua",
            "reframework/autorun/onimusha/modules/parry.lua",
            "reframework/autorun/onimusha/modules/stun.lua",
            "reframework/autorun/onimusha/modules/player_stun.lua"
        };
        private const string ReceiptName = "onimusha-discovery-install.json";

        private static int passed;
        private static int failed;

        public static int Main()
        {
            Func<string, int[]> originalProvider = Backend.RunningGameProvider;
            try
            {
                Backend.RunningGameProvider = delegate { return new int[0]; };
                Run("clean install verifies all payload hashes", CleanInstall);
                Run("teleport payload is rejected before writes", TeleportPayloadRejected);
                Run("all five payload files remain required", RequiredPayload);
                Run("copied package is portable", PortablePackage);
                Run("foreign files cause no partial writes", ForeignFilePreserved);
                Run("modified owned file blocks update before writes", ModifiedFilePreserved);
                Run("identical foreign DLL remains unowned", IdenticalForeignFileRemains);
                Run("legacy migration removes only owned inspection scripts", LegacyMigration);
                Run("legacy teleport migration removes only unchanged owned files", LegacyTeleportMigration);
                Run("uninstall removes only verified owned files", UninstallOwnedOnly);
                Run("modified file survives uninstall", ModifiedUninstall);
                Run("bad package hash fails before writes", BadHash);
                Run("package traversal is rejected", PackageTraversal);
                Run("receipt traversal is rejected", ReceiptTraversal);
                Run("linked game target is rejected", TargetJunction);
                Run("linked package source is rejected", PackageJunction);
                Run("missing DLL cannot be installed into running game", RunningMissingDll);
                Run("owned scripts update while unchanged DLL is running", RunningScriptUpdate);
                Run("launch preferences preserve BOM CRLF and other settings without repeat writes", LaunchPreferencesPreserve);
                Run("launch preferences create config and append missing keys", LaunchPreferencesCreateAndAppend);
                Run("launch preferences never change a running game config", LaunchPreferencesRunning);
                Run("install prepares unowned launch preferences preserved by uninstall", LaunchPreferencesInstallAndUninstall);
                Run("launch preferences reject a linked config path", LaunchPreferencesJunction);
                Run("modern Steam library VDF finds secondary library", ModernSteamLibrary);
                Run("legacy Steam library VDF finds secondary library", LegacySteamLibrary);
            }
            finally { Backend.RunningGameProvider = originalProvider; }
            Console.WriteLine("Backend tests: {0} passed, {1} failed.", passed, failed);
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void ExpectFailure(Action action)
        {
            try { action(); }
            catch (Exception) { return; }
            throw new InvalidOperationException("Expected the backend to reject this operation.");
        }

        private static string At(string root, string relative)
        {
            return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private static string Hash(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
        }

        private static Dictionary<string, string> Snapshot(string root)
        {
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
                delegate(string path) { return path.Substring(root.Length + 1); }, Hash,
                StringComparer.OrdinalIgnoreCase);
        }

        private static void Unchanged(string root, Dictionary<string, string> before)
        {
            Dictionary<string, string> after = Snapshot(root);
            Check(before.Count == after.Count && before.All(delegate(KeyValuePair<string, string> entry) {
                string hash; return after.TryGetValue(entry.Key, out hash) && hash == entry.Value;
            }), "An operation rejected during preflight changed game files.");
        }

        private static void CleanInstall()
        {
            using (Fixture f = new Fixture())
            {
                Backend.Install(f.Package, f.Game);
                foreach (string relative in PayloadFiles)
                    Check(Hash(At(f.Game, relative)) == Hash(At(f.Package, "payload/" + relative)), "Installed hash mismatch: " + relative);
                VerificationResult result = Backend.VerifyInstalled(f.Package, f.Game);
                Check(result.Installed && result.MatchesPackage, "Installed package did not verify: " + result.Message);
                Check(File.Exists(At(f.Game, ReceiptName)), "No ownership receipt was recorded.");
                string config = At(f.Game, "re2_fw_config.txt");
                Check(File.Exists(config) && File.ReadAllLines(config).Contains("REFrameworkConfig_MenuOpen=false")
                    && File.ReadAllLines(config).Contains("REFrameworkConfig_RememberMenuState=true"),
                    "Clean install did not prepare launch preferences.");
            }
        }

        private static void PortablePackage()
        {
            using (Fixture f = new Fixture())
            {
                string copy = At(f.Root, "copied package");
                CopyTree(f.Package, copy);
                f.RemoveTree(f.Package);
                Backend.Install(copy, f.Game);
                Check(Backend.VerifyInstalled(copy, f.Game).MatchesPackage, "Copied package depended on its original location.");
            }
        }

        private static void TeleportPayloadRejected()
        {
            using (Fixture f = new Fixture())
            {
                const string teleport = "reframework/autorun/onimusha/modules/teleport.lua";
                Write(At(f.Package, "payload/" + teleport), "test teleport module, never executed");
                f.WriteManifest("0.5.0", PayloadFiles.Concat(new[] { teleport }));
                var before = Snapshot(f.Game);
                ExpectFailure(delegate { Backend.Install(f.Package, f.Game); });
                Unchanged(f.Game, before);
            }
        }

        private static void RequiredPayload()
        {
            using (Fixture f = new Fixture())
            {
                f.WriteManifest("0.4.0", PayloadFiles.Where(p => p != PayloadFiles[4]));
                var before = Snapshot(f.Game);
                ExpectFailure(delegate { Backend.Install(f.Package, f.Game); });
                Unchanged(f.Game, before);
            }
        }

        private static void ForeignFilePreserved()
        {
            using (Fixture f = new Fixture())
            {
                Write(At(f.Game, PayloadFiles[1]), "foreign trainer contents");
                Dictionary<string, string> before = Snapshot(f.Game);
                ExpectFailure(delegate { Backend.Install(f.Package, f.Game); });
                Unchanged(f.Game, before);
            }
        }

        private static void ModifiedFilePreserved()
        {
            using (Fixture f = new Fixture())
            {
                Backend.Install(f.Package, f.Game);
                Write(At(f.Game, PayloadFiles[2]), "user changed this module");
                f.MakePackage("2.0.0");
                Dictionary<string, string> before = Snapshot(f.Game);
                ExpectFailure(delegate { Backend.Install(f.Package, f.Game); });
                Unchanged(f.Game, before);
            }
        }

        private static void IdenticalForeignFileRemains()
        {
            using (Fixture f = new Fixture())
            {
                File.Copy(At(f.Package, "payload/dinput8.dll"), At(f.Game, "dinput8.dll"));
                string foreignHash = Hash(At(f.Game, "dinput8.dll"));
                Backend.Install(f.Package, f.Game);
                Backend.Uninstall(f.Game);
                Check(File.Exists(At(f.Game, "dinput8.dll")) && Hash(At(f.Game, "dinput8.dll")) == foreignHash,
                    "An identical but unowned DLL was claimed or removed.");
            }
        }

        private static void LegacyMigration()
        {
            using (Fixture f = new Fixture())
            {
                const string discovery = "reframework/autorun/onimusha_discovery.lua";
                const string probe = "reframework/autorun/onimusha_probe.lua";
                Write(At(f.Game, discovery), "old owned discovery script");
                Write(At(f.Game, probe), "unowned probe must remain");
                f.WriteReceipt(new[] { discovery });
                Backend.Install(f.Package, f.Game);
                Check(!File.Exists(At(f.Game, discovery)), "Owned obsolete discovery script was not removed.");
                Check(File.ReadAllText(At(f.Game, probe)) == "unowned probe must remain", "Migration removed an unowned inspection script.");
                Check(Backend.VerifyInstalled(f.Package, f.Game).MatchesPackage, "Migrated package did not verify.");
            }
        }

        private static void LegacyTeleportMigration()
        {
            const string teleport = "reframework/autorun/onimusha/modules/teleport.lua";
            foreach (string ownership in new[] { "owned", "modified", "unowned" })
            using (Fixture f = new Fixture())
            {
                string target = At(f.Game, teleport);
                Write(target, "old teleport script");
                if (ownership != "unowned") f.WriteReceipt(new[] { teleport });
                if (ownership == "modified") Write(target, "user modified this teleport script");
                string originalHash = Hash(target);
                OperationResult result = Backend.Install(f.Package, f.Game);
                bool shouldRemove = ownership == "owned";
                Check(result.RemovedFiles == (shouldRemove ? 1 : 0), "Unexpected legacy removal count for " + ownership + ".");
                Check(shouldRemove ? !File.Exists(target) : File.Exists(target) && Hash(target) == originalHash,
                    "Legacy teleport ownership was not respected for " + ownership + ".");
                Check(!File.ReadAllText(At(f.Game, ReceiptName)).Contains("teleport.lua", StringComparison.OrdinalIgnoreCase),
                    "The new ownership receipt retained a removed or preserved legacy teleport file.");
                Check(Backend.VerifyInstalled(f.Package, f.Game).MatchesPackage, "Combat payload did not verify after legacy cleanup.");
                Backend.Uninstall(f.Game);
                Check(shouldRemove ? !File.Exists(target) : File.Exists(target) && Hash(target) == originalHash,
                    "Uninstall changed a preserved legacy teleport file for " + ownership + ".");
            }
        }

        private static void UninstallOwnedOnly()
        {
            using (Fixture f = new Fixture())
            {
                Backend.Install(f.Package, f.Game);
                Write(At(f.Game, "reframework/autorun/another_mod.lua"), "keep this mod");
                Write(At(f.Game, "savegame.dat"), "keep this save");
                Backend.Uninstall(f.Game);
                Check(PayloadFiles.All(delegate(string relative) { return !File.Exists(At(f.Game, relative)); }), "An owned payload file remained.");
                Check(File.ReadAllText(At(f.Game, "reframework/autorun/another_mod.lua")) == "keep this mod", "Another mod was removed.");
                Check(File.ReadAllText(At(f.Game, "savegame.dat")) == "keep this save", "Save data was removed.");
                Check(File.Exists(At(f.Game, "OnimushaWotS.exe")), "The game executable was removed.");
            }
        }

        private static void ModifiedUninstall()
        {
            using (Fixture f = new Fixture())
            {
                Backend.Install(f.Package, f.Game);
                string changed = At(f.Game, PayloadFiles[3]);
                Write(changed, "user modified module");
                try { Backend.Uninstall(f.Game); } catch (Exception) { }
                Check(File.Exists(changed) && File.ReadAllText(changed) == "user modified module", "Uninstall removed a modified file.");
            }
        }

        private static void BadHash()
        {
            using (Fixture f = new Fixture())
            {
                Write(At(f.Package, "payload/" + PayloadFiles[4]), "tampered package payload");
                Dictionary<string, string> before = Snapshot(f.Game);
                ExpectFailure(delegate { Backend.Install(f.Package, f.Game); });
                Unchanged(f.Game, before);
            }
        }

        private static void PackageTraversal()
        {
            using (Fixture f = new Fixture())
            {
                Dictionary<string, object> manifest = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(At(f.Package, "package.json")));
                Dictionary<string, object> invalid = new Dictionary<string, object>();
                invalid["RelativePath"] = "../outside.txt";
                invalid["Sha256"] = new string('A', 64);
                manifest["Files"] = new[] { invalid };
                Write(At(f.Package, "package.json"), JsonSerializer.Serialize(manifest));
                Dictionary<string, string> before = Snapshot(f.Game);
                ExpectFailure(delegate { Backend.Install(f.Package, f.Game); });
                Unchanged(f.Game, before);
                Check(!File.Exists(At(f.Root, "outside.txt")), "Package traversal escaped the game folder.");
            }
        }

        private static void ReceiptTraversal()
        {
            using (Fixture f = new Fixture())
            {
                string outside = At(f.Root, "outside.txt");
                Write(outside, "outside sentinel");
                f.WriteReceipt(new[] { "../outside.txt" });
                Dictionary<string, string> before = Snapshot(f.Game);
                ExpectFailure(delegate { Backend.Uninstall(f.Game); });
                Unchanged(f.Game, before);
                Check(File.ReadAllText(outside) == "outside sentinel", "Receipt traversal removed an outside file.");
            }
        }

        private static void TargetJunction()
        {
            using (Fixture f = new Fixture())
            {
                string target = At(f.Root, "redirected-target");
                Write(At(target, "sentinel.txt"), "unchanged");
                CreateJunction(At(f.Game, "reframework"), target);
                ExpectFailure(delegate { Backend.Install(f.Package, f.Game); });
                Check(Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length == 1,
                    "The installer wrote through a target junction.");
                Check(!File.Exists(At(f.Game, "dinput8.dll")), "Junction rejection happened after a partial install.");
            }
        }

        private static void PackageJunction()
        {
            using (Fixture f = new Fixture())
            {
                string source = At(f.Package, "payload/reframework"), redirected = At(f.Root, "redirected-source");
                CopyTree(source, redirected);
                f.RemoveTree(source);
                CreateJunction(source, redirected);
                Dictionary<string, string> before = Snapshot(f.Game);
                ExpectFailure(delegate { Backend.Install(f.Package, f.Game); });
                Unchanged(f.Game, before);
            }
        }

        private static void RunningMissingDll()
        {
            using (Fixture f = new Fixture())
            {
                Func<string, int[]> previous = Backend.RunningGameProvider;
                try
                {
                    Backend.RunningGameProvider = delegate(string path) {
                        Check(Path.GetFullPath(path) == Path.GetFullPath(f.Game), "Unexpected real game process query.");
                        return new[] { 12345 };
                    };
                    Dictionary<string, string> before = Snapshot(f.Game);
                    ExpectFailure(delegate { Backend.Install(f.Package, f.Game); });
                    Unchanged(f.Game, before);
                }
                finally { Backend.RunningGameProvider = previous; }
            }
        }

        private static void RunningScriptUpdate()
        {
            using (Fixture f = new Fixture())
            {
                Backend.Install(f.Package, f.Game);
                string originalDllHash = Hash(At(f.Game, "dinput8.dll"));
                Write(At(f.Package, "payload/" + PayloadFiles[1]), "updated trainer script");
                f.WriteManifest("1.0.1");
                Func<string, int[]> previous = Backend.RunningGameProvider;
                try
                {
                    Backend.RunningGameProvider = delegate { return new[] { 12345 }; };
                    OperationResult result = Backend.Install(f.Package, f.Game);
                    Check(result.RequiresScriptReset, "Running script update did not request a script reset.");
                    Check(Hash(At(f.Game, "dinput8.dll")) == originalDllHash, "Running update changed the DLL.");
                    Check(File.ReadAllText(At(f.Game, PayloadFiles[1])) == "updated trainer script", "Owned script was not updated.");
                }
                finally { Backend.RunningGameProvider = previous; }
            }
        }

        private static void LaunchPreferencesPreserve()
        {
            using (Fixture f = new Fixture())
            {
                string config = At(f.Game, "re2_fw_config.txt");
                const string original = "# User preferences \u96EA\r\nREFrameworkConfig_MenuKey=45\r\n"
                    + "REFrameworkConfig_MenuOpen=true\r\nREFrameworkConfig_RememberMenuState=false\r\n"
                    + "REFrameworkConfig_MenuOpenExtra=true\r\nOtherSetting = untouched\r\n";
                UTF8Encoding encoding = new UTF8Encoding(true);
                File.WriteAllText(config, original, encoding);
                Check(Backend.PrepareLaunch(f.Game), "Changed launch preferences were not reported.");
                string expected = original.Replace("REFrameworkConfig_MenuOpen=true\r\n", "REFrameworkConfig_MenuOpen=false\r\n")
                    .Replace("REFrameworkConfig_RememberMenuState=false\r\n", "REFrameworkConfig_RememberMenuState=true\r\n");
                byte[] expectedBytes = encoding.GetPreamble().Concat(encoding.GetBytes(expected)).ToArray();
                Check(File.ReadAllBytes(config).SequenceEqual(expectedBytes), "Configuration text, BOM, CRLF, or unrelated keys changed.");
                DateTime sentinelTime = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(config, sentinelTime);
                DateTime before = File.GetLastWriteTimeUtc(config);
                Check(!Backend.PrepareLaunch(f.Game), "Unchanged preferences incorrectly reported a write.");
                Check(File.GetLastWriteTimeUtc(config) == before, "Repeated launch preparation rewrote the config.");
                Check(File.ReadAllBytes(config).SequenceEqual(expectedBytes), "Repeated preparation changed config bytes.");
            }
        }

        private static void LaunchPreferencesCreateAndAppend()
        {
            using (Fixture f = new Fixture())
            {
                string config = At(f.Game, "re2_fw_config.txt");
                Check(Backend.PrepareLaunch(f.Game), "Missing config was not created.");
                string[] lines = File.ReadAllLines(config);
                Check(lines.Length == 2
                    && lines.Contains("REFrameworkConfig_MenuOpen=false")
                    && lines.Contains("REFrameworkConfig_RememberMenuState=true"),
                    "New config did not contain exactly the two launch preferences.");
            }
            using (Fixture f = new Fixture())
            {
                string config = At(f.Game, "re2_fw_config.txt");
                const string prefix = "# Keep these preferences\nREFrameworkConfig_MenuKey=123\n";
                Write(config, prefix + "REFrameworkConfig_MenuOpen=true");
                Check(Backend.PrepareLaunch(f.Game), "Missing preference was not appended.");
                string text = File.ReadAllText(config);
                Check(text.StartsWith(prefix, StringComparison.Ordinal), "Appending a preference changed preceding text.");
                Check(!text.Contains("\r"), "Appending a preference changed LF line endings.");
                Check(!File.ReadAllBytes(config).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "Appending a preference added a BOM.");
                string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Check(lines.SequenceEqual(new[] {
                    "# Keep these preferences", "REFrameworkConfig_MenuKey=123",
                    "REFrameworkConfig_MenuOpen=false", "REFrameworkConfig_RememberMenuState=true"
                }), "The missing preference was not appended while preserving the existing keys.");
            }
        }

        private static void LaunchPreferencesRunning()
        {
            using (Fixture f = new Fixture())
            {
                string config = At(f.Game, "re2_fw_config.txt");
                Write(config, "REFrameworkConfig_MenuOpen=true\nREFrameworkConfig_RememberMenuState=false\n");
                Func<string, int[]> previous = Backend.RunningGameProvider;
                try
                {
                    Backend.RunningGameProvider = delegate(string path) {
                        Check(Path.GetFullPath(path) == Path.GetFullPath(f.Game), "Unexpected real game process query.");
                        return new[] { 12345 };
                    };
                    Dictionary<string, string> before = Snapshot(f.Game);
                    DateTime writeTime = File.GetLastWriteTimeUtc(config);
                    Check(!Backend.PrepareLaunch(f.Game), "A running game reported a config change.");
                    Unchanged(f.Game, before);
                    Check(File.GetLastWriteTimeUtc(config) == writeTime, "Running game preparation rewrote the config.");
                    File.Delete(config);
                    Check(!Backend.PrepareLaunch(f.Game), "Running game preparation reported a new config.");
                    Check(!File.Exists(config), "A config was created while the game was running.");
                }
                finally { Backend.RunningGameProvider = previous; }
            }
        }

        private static void LaunchPreferencesInstallAndUninstall()
        {
            using (Fixture f = new Fixture())
            {
                string config = At(f.Game, "re2_fw_config.txt");
                const string original = "REFrameworkConfig_MenuKey=45\nREFrameworkConfig_MenuOpen=true\n"
                    + "REFrameworkConfig_RememberMenuState=false\nOtherSetting=preserved\n";
                Write(config, original);
                Backend.Install(f.Package, f.Game);
                string expected = original.Replace("MenuOpen=true", "MenuOpen=false")
                    .Replace("RememberMenuState=false", "RememberMenuState=true");
                Check(File.ReadAllText(config) == expected, "Successful install did not prepare launch preferences.");
                Check(File.ReadAllText(At(f.Game, ReceiptName)).IndexOf("re2_fw_config.txt", StringComparison.OrdinalIgnoreCase) < 0,
                    "The shared REFramework configuration was claimed as an owned payload.");
                string hash = Hash(config);
                Backend.Uninstall(f.Game);
                Check(File.Exists(config) && Hash(config) == hash, "Uninstall removed or changed the shared configuration.");
            }
        }

        private static void LaunchPreferencesJunction()
        {
            using (Fixture f = new Fixture())
            {
                string target = At(f.Root, "redirected-config");
                Write(At(target, "sentinel.txt"), "keep this config target");
                CreateJunction(At(f.Game, "re2_fw_config.txt"), target);
                ExpectFailure(delegate { Backend.PrepareLaunch(f.Game); });
                Check(Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length == 1
                    && File.ReadAllText(At(target, "sentinel.txt")) == "keep this config target",
                    "Launch preparation wrote through a configuration junction.");
            }
        }

        private static void ModernSteamLibrary() { SteamLibrary(false); }
        private static void LegacySteamLibrary() { SteamLibrary(true); }
        private static string Vdf(string value) { return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        private static void SteamLibrary(bool legacy)
        {
            using (Fixture f = new Fixture())
            {
                string steam = At(f.Root, "Steam"), library = At(f.Root, "Secondary Library");
                string game = At(library, "steamapps/common/OnimushaWotS");
                Write(At(game, "OnimushaWotS.exe"), "fake executable, never launched");
                string entry = legacy ? "\"1\" " + Vdf(library) : "\"1\" { \"path\" " + Vdf(library) + " }";
                Write(At(steam, "steamapps/libraryfolders.vdf"), "// fixture\n\"libraryfolders\" { " + entry + " }");
                Write(At(library, "steamapps/appmanifest_2638890.acf"),
                    "\"AppState\" { \"appid\" \"2638890\" \"installdir\" \"OnimushaWotS\" \"buildid\" \"24769601\" \"name\" \"Onimusha\" }");
                GameInstallation result = Backend.DiscoverGameFromSteamRoot(steam);
                Check(result != null && String.Equals(Path.GetFullPath(result.Path), Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase),
                    "Steam discovery did not resolve the secondary library.");
                Check(result.BuildId == "24769601", "Steam build ID was lost.");
            }
        }

        private static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (string directory in Directory.GetDirectories(source)) CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        private sealed class Fixture : IDisposable
        {
            public readonly string Root;
            public readonly string Game;
            public readonly string Package;
            public Fixture()
            {
                Root = Path.Combine(Path.GetTempPath(), "OnimushaBackendTests-" + Guid.NewGuid().ToString("N"));
                Game = At(Root, "game"); Package = At(Root, "package");
                Write(At(Game, "OnimushaWotS.exe"), "fake executable, never launched");
                MakePackage("0.4.0");
            }

            public void MakePackage(string version)
            {
                foreach (string relative in PayloadFiles)
                {
                    string path = At(Package, "payload/" + relative);
                    Write(path, version + ":" + relative);
                }
                WriteManifest(version);
            }

            public void WriteManifest(string version, IEnumerable<string> relativePaths = null)
            {
                object[] files = (relativePaths ?? PayloadFiles).Select(delegate(string relative) {
                    return (object)new { RelativePath = relative, Sha256 = Hash(At(Package, "payload/" + relative)) };
                }).ToArray();
                Write(At(Package, "package.json"), JsonSerializer.Serialize(new {
                    Version = version, AppId = "2638890", TestedBuild = "24769601", Files = files
                }));
            }

            public void WriteReceipt(IEnumerable<string> relativePaths)
            {
                Write(At(Game, ReceiptName), JsonSerializer.Serialize(new {
                    Project = "cheats-onimusha", Kind = "trainer-development", SteamBuildId = "24769601",
                    Files = relativePaths.Select(delegate(string relative) {
                        return new { RelativePath = relative, Sha256 = Hash(At(Game, relative)) };
                    }).ToArray()
                }));
            }

            public void RemoveTree(string path)
            {
                string root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
                string target = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                Check(target.Equals(root, StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                    "Refusing to clean a path outside this test fixture.");
                DeleteContained(target);
            }

            private static void DeleteContained(string path)
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(path); }
                catch (FileNotFoundException) { return; }
                catch (DirectoryNotFoundException) { return; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) { Directory.Delete(path, false); return; }
                foreach (string file in Directory.GetFiles(path)) File.Delete(file);
                foreach (string directory in Directory.GetDirectories(path)) DeleteContained(directory);
                Directory.Delete(path, false);
            }

            public void Dispose() { RemoveTree(Root); }
        }

        // Directory junctions require no elevation. Build the documented mount
        // point reparse buffer directly, avoiding shell commands in test setup.
        private static void CreateJunction(string link, string target)
        {
            Directory.CreateDirectory(link);
            string substitute = "\\??\\" + Path.GetFullPath(target);
            byte[] substituteBytes = Encoding.Unicode.GetBytes(substitute);
            byte[] printBytes = Encoding.Unicode.GetBytes(Path.GetFullPath(target));
            byte[] buffer = new byte[16 + substituteBytes.Length + 2 + printBytes.Length + 2];
            using (MemoryStream memory = new MemoryStream(buffer))
            using (BinaryWriter writer = new BinaryWriter(memory))
            {
                writer.Write(unchecked((uint)0xA0000003));
                writer.Write((ushort)(buffer.Length - 8)); writer.Write((ushort)0);
                writer.Write((ushort)0); writer.Write((ushort)substituteBytes.Length);
                writer.Write((ushort)(substituteBytes.Length + 2)); writer.Write((ushort)printBytes.Length);
                writer.Write(substituteBytes); writer.Write((ushort)0); writer.Write(printBytes); writer.Write((ushort)0);
            }
            using (SafeFileHandle handle = CreateFile(link, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                int returned;
                if (!DeviceIoControl(handle, 0x000900A4, buffer, buffer.Length, IntPtr.Zero, 0, out returned, IntPtr.Zero))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            Check((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "Junction fixture was not created.");
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputLength,
            IntPtr output, int outputLength, out int returned, IntPtr overlapped);
    }
}

