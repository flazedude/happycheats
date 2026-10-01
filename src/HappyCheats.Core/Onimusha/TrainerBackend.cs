#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Win32;

namespace OnimushaTrainer
{
    public sealed class GameInstallation
    {
        public string Path { get; set; }
        public string BuildId { get; set; }
        public string SteamRoot { get; set; }
    }

    public sealed class OperationResult
    {
        public string Message { get; set; }
        public bool RequiresScriptReset { get; set; }
        public int ChangedFiles { get; set; }
        public int ReusedFiles { get; set; }
        public int RemovedFiles { get; set; }
    }

    public sealed class VerificationResult
    {
        public bool Installed { get; set; }
        public bool MatchesPackage { get; set; }
        public string Message { get; set; }
        public string[] Problems { get; set; }
    }

    public static class Backend
    {
        private const string AppId = "2638890";
        private const string Executable = "OnimushaWotS.exe";
        private const string ReceiptName = "onimusha-discovery-install.json";
        private static readonly string[] PayloadPaths = {
            "dinput8.dll", "reframework\\autorun\\onimusha_trainer.lua",
            "reframework\\autorun\\onimusha\\modules\\parry.lua",
            "reframework\\autorun\\onimusha\\modules\\stun.lua",
            "reframework\\autorun\\onimusha\\modules\\player_stun.lua"
        };
        // Accepted only in old ownership receipts, never in installable packages.
        private static readonly string[] LegacyPaths = {
            "reframework\\autorun\\onimusha_discovery.lua", "reframework\\autorun\\onimusha_probe.lua",
            "reframework\\autorun\\onimusha\\modules\\teleport.lua"
        };
        // Internal seams allow fixture tests without editing the registry or launching a game.
        internal static Func<string, int[]> RunningGameProvider = FindRunningGame;

        private sealed class FileRecord
        {
            public string RelativePath { get; set; }
            public string Sha256 { get; set; }
        }
        private sealed class PackageManifest
        {
            public string Version { get; set; }
            public string AppId { get; set; }
            public string TestedBuild { get; set; }
            public List<FileRecord> Files { get; set; }
        }
        private sealed class Receipt
        {
            public string Project { get; set; }
            public string Kind { get; set; }
            public string PackageVersion { get; set; }
            public string SteamBuildId { get; set; }
            public string InstalledUtc { get; set; }
            public List<FileRecord> Files { get; set; }
        }
        private sealed class CopyPlan
        {
            public FileRecord File;
            public string Source;
            public string Target;
            public string PreviousHash;
            public bool Reuse;
        }

        public static GameInstallation DiscoverGame()
        {
            string root = ReadSteamRoot();
            if (String.IsNullOrEmpty(root))
                throw new InvalidOperationException("Steam was not found in the current user's registry. Choose the Onimusha game folder manually.");
            return DiscoverGameFromSteamRoot(root);
        }

        internal static GameInstallation DiscoverGameFromSteamRoot(string steamRoot)
        {
            steamRoot = FullPath(steamRoot);
            var libraries = new List<string> { steamRoot };
            string libraryFile = System.IO.Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFile))
            {
                var libraryData = ReadVdf(libraryFile);
                Dictionary<string, object> entries = ObjectValue(libraryData, "libraryfolders");
                if (entries != null)
                    foreach (var entry in entries)
                    {
                        int index;
                        if (!Int32.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out index)) continue;
                        var nested = entry.Value as Dictionary<string, object>;
                        string value = nested == null ? entry.Value as string : StringValue(nested, "path");
                        if (!String.IsNullOrWhiteSpace(value))
                        {
                            string library = FullPath(value);
                            if (!libraries.Contains(library, StringComparer.OrdinalIgnoreCase)) libraries.Add(library);
                        }
                    }
            }
            foreach (string library in libraries)
            {
                string manifest = System.IO.Path.Combine(library, "steamapps", "appmanifest_" + AppId + ".acf");
                if (!File.Exists(manifest)) continue;
                var app = ObjectValue(ReadVdf(manifest), "AppState");
                if (app == null || StringValue(app, "appid") != AppId) continue;
                string installDirectory = StringValue(app, "installdir");
                if (String.IsNullOrWhiteSpace(installDirectory)) continue;
                string common = System.IO.Path.Combine(library, "steamapps", "common");
                string candidate = ContainedPath(common, installDirectory);
                if (File.Exists(System.IO.Path.Combine(candidate, Executable)))
                    return InspectGameCore(candidate, steamRoot);
            }
            throw new InvalidOperationException("Onimusha: Way of the Sword was not found in registered Steam libraries. Choose its game folder manually.");
        }

        public static GameInstallation InspectGame(string path)
        {
            return InspectGameCore(path, ReadSteamRoot());
        }

        private static GameInstallation InspectGameCore(string path, string steamRoot)
        {
            string root = FullPath(path);
            if (!Directory.Exists(root) || !File.Exists(System.IO.Path.Combine(root, Executable)))
                throw new InvalidOperationException("Choose the game folder containing " + Executable + ".");
            string build = null;
            DirectoryInfo common = Directory.GetParent(root);
            DirectoryInfo steamapps = common == null ? null : common.Parent;
            if (common != null && steamapps != null && Same(common.Name, "common") && Same(steamapps.Name, "steamapps"))
            {
                string manifest = System.IO.Path.Combine(steamapps.FullName, "appmanifest_" + AppId + ".acf");
                if (File.Exists(manifest))
                {
                    var app = ObjectValue(ReadVdf(manifest), "AppState");
                    if (app == null || StringValue(app, "appid") != AppId ||
                        !Same(ContainedPath(common.FullName, StringValue(app, "installdir")), root))
                        throw new InvalidOperationException("The Steam app manifest does not match the selected Onimusha folder.");
                    build = StringValue(app, "buildid");
                }
            }
            return new GameInstallation { Path = root, BuildId = build, SteamRoot = steamRoot };
        }

        public static int[] GetRunningGame(string path)
        {
            return (RunningGameProvider ?? FindRunningGame)(FullPath(path));
        }

        private static int[] FindRunningGame(string path)
        {
            string expected = System.IO.Path.Combine(path, Executable);
            var result = new List<int>();
            foreach (Process process in Process.GetProcessesByName("OnimushaWotS"))
            {
                using (process)
                {
                    try
                    {
                        if (!process.HasExited && Same(FullPath(process.MainModule.FileName), expected)) result.Add(process.Id);
                    }
                    catch (InvalidOperationException) { /* Process exited during inspection. */ }
                    catch (System.ComponentModel.Win32Exception error)
                    {
                        throw new InvalidOperationException("A running Onimusha process could not be inspected. Close the game before changing trainer files.", error);
                    }
                }
            }
            return result.ToArray();
        }

        public static VerificationResult VerifyInstalled(string packageRoot, string gamePath)
        {
            var game = InspectGame(gamePath);
            var package = LoadPackage(packageRoot);
            var problems = new List<string>();
            bool present = true;
            foreach (var file in package.Files)
            {
                string target = CheckedPath(game.Path, file.RelativePath);
                if (!File.Exists(target)) { present = false; problems.Add("Missing: " + file.RelativePath); }
                else if (!Same(HashFile(target), file.Sha256)) problems.Add("Different version: " + file.RelativePath);
            }
            return new VerificationResult { Installed = present, MatchesPackage = problems.Count == 0,
                Problems = problems.ToArray(), Message = problems.Count == 0 ? "Installed files match this trainer package." : String.Join(Environment.NewLine, problems) };
        }

        public static OperationResult Install(string packageRoot, string gamePath)
        {
            var game = InspectGame(gamePath);
            var package = LoadPackage(packageRoot); // Validate every payload before making any game-folder write.
            string payload = CheckedPath(FullPath(packageRoot), "payload");
            string receiptPath = CheckedPath(game.Path, ReceiptName);
            EnsureWritableFile(receiptPath);
            var oldReceipt = ReadReceipt(game.Path);
            var owned = Records(oldReceipt == null ? null : oldReceipt.Files);
            var plans = new List<CopyPlan>();
            foreach (var file in package.Files)
            {
                string target = CheckedPath(game.Path, file.RelativePath);
                if (Directory.Exists(target)) throw new InvalidOperationException("A directory occupies an expected trainer filename: " + target);
                string existing = File.Exists(target) ? HashFile(target) : null;
                FileRecord old;
                bool haveOwnership = owned.TryGetValue(file.RelativePath, out old) && Same(old.Sha256, existing);
                bool reuse = existing != null && Same(existing, file.Sha256);
                if (existing != null && !reuse && !haveOwnership)
                    throw new InvalidOperationException("Existing foreign or modified file will not be overwritten: " + target);
                if (!reuse) EnsureWritableFile(target);
                if (reuse && !haveOwnership) owned.Remove(file.RelativePath);
                plans.Add(new CopyPlan { File = file, Source = CheckedPath(payload, file.RelativePath),
                    Target = target, PreviousHash = existing, Reuse = reuse });
            }
            var obsolete = new List<CopyPlan>();
            int preservedLegacy = 0;
            foreach (string relative in LegacyPaths)
            {
                FileRecord old;
                if (!owned.TryGetValue(relative, out old)) continue;
                string target = CheckedPath(game.Path, relative);
                if (Directory.Exists(target)) throw new InvalidOperationException("A directory occupies an owned legacy filename: " + target);
                if (!File.Exists(target)) { owned.Remove(relative); continue; }
                string actual = HashFile(target);
                if (!Same(actual, old.Sha256)) { owned.Remove(relative); preservedLegacy++; continue; }
                EnsureWritableFile(target);
                obsolete.Add(new CopyPlan { File = old, Target = target, PreviousHash = actual });
            }
            bool running = GetRunningGame(game.Path).Length != 0;
            if (running && plans.Any(p => Same(p.File.RelativePath, "dinput8.dll") && !p.Reuse))
                throw new InvalidOperationException("Close Onimusha before installing or replacing dinput8.dll. Script-only updates require the package's DLL to be installed already.");
            var result = new OperationResult();
            foreach (var plan in plans)
            {
                if (plan.Reuse) { result.ReusedFiles++; continue; }
                AtomicCopy(game.Path, plan);
                owned[plan.File.RelativePath] = plan.File;
                result.ChangedFiles++;
                WriteReceipt(game.Path, game.BuildId, package.Version, owned.Values);
            }
            foreach (var plan in obsolete)
            {
                AssertUnchanged(game.Path, plan);
                File.Delete(plan.Target);
                owned.Remove(plan.File.RelativePath);
                result.RemovedFiles++;
                WriteReceipt(game.Path, game.BuildId, package.Version, owned.Values);
            }
            WriteReceipt(game.Path, game.BuildId, package.Version, owned.Values);
            result.RequiresScriptReset = running && (result.ChangedFiles != 0 || result.RemovedFiles != 0);
            PrepareLaunch(game.Path);
            result.Message = result.RequiresScriptReset ? "Trainer scripts updated. Use REFramework > ScriptRunner > Reset scripts, or restart the game." :
                "Trainer package is installed. Features start disabled when scripts load.";
            if (preservedLegacy != 0) result.Message += " Modified legacy trainer files were preserved and are no longer owned by this installer.";
            return result;
        }


        // REFramework applies MenuOpen on startup only when RememberMenuState is true.
        // This generated preference is not installer-owned and never controls the Lua HUD.
        public static bool PrepareLaunch(string gamePath)
        {
            var game = InspectGame(gamePath);
            if (GetRunningGame(game.Path).Length != 0) return false;
            string target = CheckedPath(game.Path, "re2_fw_config.txt");
            if (Directory.Exists(target)) throw new InvalidOperationException("The REFramework preference path is a directory.");
            if (File.Exists(target) && new FileInfo(target).Length > 1024 * 1024)
                throw new InvalidOperationException("The REFramework preference file is unexpectedly large.");
            byte[] original = File.Exists(target) ? File.ReadAllBytes(target) : null;
            Encoding encoding = new UTF8Encoding(false, true);
            string contents = "";
            if (original != null)
            {
                using (var reader = new StreamReader(new MemoryStream(original), encoding, true))
                {
                    contents = reader.ReadToEnd();
                    encoding = reader.CurrentEncoding;
                }
                if (!original.SequenceEqual(encoding.GetPreamble().Concat(encoding.GetBytes(contents))))
                    throw new InvalidOperationException("The REFramework preference encoding could not be preserved.");
            }
            string updated = SetPreference(contents, "REFrameworkConfig_MenuOpen", "false");
            updated = SetPreference(updated, "REFrameworkConfig_RememberMenuState", "true");
            if (updated == contents) return false;
            EnsureWritableFile(target);
            byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes(updated)).ToArray();
            string temporary = CheckedPath(game.Path, ".onimusha-menu-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.Write(bytes, 0, bytes.Length);
                CheckedPath(game.Path, "re2_fw_config.txt");
                if (Directory.Exists(target) || (original == null ? File.Exists(target) :
                    !File.Exists(target) || !original.SequenceEqual(File.ReadAllBytes(target))))
                    throw new InvalidOperationException("The REFramework preferences changed during this operation; they were preserved.");
                if (GetRunningGame(game.Path).Length != 0) return false;
                if (original == null) File.Move(temporary, target); else File.Replace(temporary, target, null);
                return true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string SetPreference(string contents, string key, string value)
        {
            var pattern = new Regex(@"(?m)^([\t ]*" + Regex.Escape(key) + @"[\t ]*=[\t ]*)[^\r\n]*");
            if (pattern.IsMatch(contents)) return pattern.Replace(contents, match => match.Groups[1].Value + value);
            string newline = Regex.Match(contents, @"\r\n|\n|\r").Value;
            if (newline.Length == 0) newline = Environment.NewLine;
            if (contents.Length > 0 && !contents.EndsWith("\n", StringComparison.Ordinal) && !contents.EndsWith("\r", StringComparison.Ordinal))
                contents += newline;
            return contents + key + "=" + value + newline;
        }

        public static OperationResult Uninstall(string gamePath)
        {
            string root = FullPath(gamePath);
            if (!Directory.Exists(root)) throw new InvalidOperationException("The selected game folder does not exist.");
            if (GetRunningGame(root).Length != 0) throw new InvalidOperationException("Close Onimusha before uninstalling the trainer.");
            var receipt = ReadReceipt(root);
            if (receipt == null) throw new InvalidOperationException("No trainer ownership receipt was found. No files were removed.");
            string receiptPath = CheckedPath(root, ReceiptName);
            EnsureWritableFile(receiptPath);
            var plans = new List<CopyPlan>();
            foreach (var file in receipt.Files)
            {
                string target = CheckedPath(root, file.RelativePath);
                if (Directory.Exists(target)) throw new InvalidOperationException("An owned trainer filename is now a directory: " + target);
                if (!File.Exists(target)) continue;
                string actual = HashFile(target);
                if (!Same(actual, file.Sha256)) throw new InvalidOperationException("An installed file was modified; no files were removed: " + target);
                EnsureWritableFile(target);
                plans.Add(new CopyPlan { File = file, Target = target, PreviousHash = actual });
            }
            // Complete hash preflight precedes every deletion. Keep the receipt on any failure for a safe retry.
            foreach (var plan in plans) { AssertUnchanged(root, plan); File.Delete(plan.Target); }
            File.Delete(receiptPath);
            return new OperationResult { RemovedFiles = plans.Count, Message = "Owned trainer files removed. Other mods and generated game files were preserved." };
        }

        private static PackageManifest LoadPackage(string packageRoot)
        {
            string root = FullPath(packageRoot);
            var package = ReadJson<PackageManifest>(CheckedPath(root, "package.json"));
            if (package == null || package.AppId != AppId || String.IsNullOrWhiteSpace(package.Version) ||
                String.IsNullOrWhiteSpace(package.TestedBuild) || package.Files == null)
                throw new InvalidOperationException("The trainer package manifest is incomplete or belongs to a different game.");
            ValidateRecords(package.Files, false);
            if (PayloadPaths.Any(required => !package.Files.Any(file => Same(file.RelativePath, required))))
                throw new InvalidOperationException("The trainer package manifest is missing a required payload file.");
            string payload = CheckedPath(root, "payload");
            foreach (var file in package.Files)
            {
                string source = CheckedPath(payload, file.RelativePath);
                if (!File.Exists(source) || !Same(HashFile(source), file.Sha256))
                    throw new InvalidOperationException("Package file is missing or failed SHA-256 verification: " + file.RelativePath);
            }
            return package;
        }

        private static Receipt ReadReceipt(string root)
        {
            string filename = CheckedPath(root, ReceiptName);
            if (Directory.Exists(filename)) throw new InvalidOperationException("The ownership receipt path is a directory.");
            if (!File.Exists(filename)) return null;
            var receipt = ReadJson<Receipt>(filename);
            if (receipt == null || receipt.Project != "cheats-onimusha" ||
                (receipt.Kind != "trainer-development" && receipt.Kind != "read-only-discovery") || receipt.Files == null)
                throw new InvalidOperationException("The trainer ownership receipt is invalid. No files were changed.");
            ValidateRecords(receipt.Files, true);
            return receipt;
        }

        private static void ValidateRecords(List<FileRecord> files, bool allowLegacy)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                if (file == null) throw new InvalidOperationException("A file record is missing.");
                string normalized = RelativePath(file.RelativePath);
                string canonical = PayloadPaths.Concat(allowLegacy ? LegacyPaths : new string[0]).FirstOrDefault(p => Same(p, normalized));
                if (canonical == null || !seen.Add(canonical) || file.Sha256 == null || !Regex.IsMatch(file.Sha256, "\\A[0-9a-fA-F]{64}\\z"))
                    throw new InvalidOperationException("A file record has an unsupported path, duplicate name, or invalid SHA-256 hash.");
                file.RelativePath = canonical;
            }
        }

        private static Dictionary<string, FileRecord> Records(IEnumerable<FileRecord> files)
        {
            var result = new Dictionary<string, FileRecord>(StringComparer.OrdinalIgnoreCase);
            if (files != null) foreach (var file in files) result.Add(file.RelativePath, file);
            return result;
        }

        private static void WriteReceipt(string root, string build, string version, IEnumerable<FileRecord> owned)
        {
            var receipt = new Receipt { Project = "cheats-onimusha", Kind = "trainer-development", PackageVersion = version,
                SteamBuildId = build, InstalledUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Files = owned.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList() };
            string target = CheckedPath(root, ReceiptName);
            string temporary = CheckedPath(root, ".onimusha-receipt-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(receipt), new UTF8Encoding(false));
                if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void AtomicCopy(string root, CopyPlan plan)
        {
            AssertUnchanged(root, plan);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(plan.Target));
            string temporary = CheckedPath(root, plan.File.RelativePath + ".onimusha-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(plan.Source, temporary, false);
                if (!Same(HashFile(temporary), plan.File.Sha256)) throw new InvalidOperationException("Package file changed during installation: " + plan.File.RelativePath);
                AssertUnchanged(root, plan);
                if (File.Exists(plan.Target)) File.Replace(temporary, plan.Target, null); else File.Move(temporary, plan.Target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void AssertUnchanged(string root, CopyPlan plan)
        {
            CheckedPath(root, plan.File.RelativePath);
            string current = File.Exists(plan.Target) ? HashFile(plan.Target) : null;
            if (Directory.Exists(plan.Target) || !Same(current, plan.PreviousHash))
                throw new InvalidOperationException("A trainer file changed during this operation; it was preserved: " + plan.Target);
        }

        private static void EnsureWritableFile(string path)
        {
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                throw new InvalidOperationException("An installation target is read-only: " + path);
        }

        private static string HashFile(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
        }


        private static T ReadJson<T>(string path)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("Required file not found: " + path);
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidOperationException("JSON metadata file is unexpectedly large: " + path);
            try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 32 }); }
            catch (Exception error) { throw new InvalidOperationException("JSON metadata could not be read: " + System.IO.Path.GetFileName(path), error); }
        }

        private static string FullPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("A folder path is required.");
            string full = System.IO.Path.GetFullPath(path.Replace('/', '\\'));
            string volume = System.IO.Path.GetPathRoot(full);
            return full.Length == volume.Length ? full : full.TrimEnd('\\');
        }

        private static string RelativePath(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || System.IO.Path.IsPathRooted(path)) throw new InvalidOperationException("An installation path must be relative.");
            string value = path.Replace('/', '\\');
            foreach (string segment in value.Split('\\'))
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith(".", StringComparison.Ordinal) ||
                    segment.EndsWith(" ", StringComparison.Ordinal) || segment.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidOperationException("An installation path contains an unsafe segment.");
            return value;
        }

        private static string ContainedPath(string root, string relative)
        {
            root = FullPath(root);
            string result = FullPath(System.IO.Path.Combine(root, RelativePath(relative)));
            if (!result.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An installation path escapes its folder.");
            return result;
        }

        private static string CheckedPath(string root, string relative)
        {
            string result = ContainedPath(root, relative);
            string current = result;
            while (!String.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("Linked files or folders are not supported for installation: " + current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                current = System.IO.Path.GetDirectoryName(current);
            }
            return result;
        }

        private static bool Same(string left, string right) { return String.Equals(left, right, StringComparison.OrdinalIgnoreCase); }

        private static string ReadSteamRoot()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                string path = key == null ? null : key.GetValue("SteamPath") as string;
                return String.IsNullOrWhiteSpace(path) ? null : FullPath(path);
            }
        }

        private static Dictionary<string, object> ObjectValue(Dictionary<string, object> dictionary, string key)
        {
            object value;
            return dictionary != null && dictionary.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static string StringValue(Dictionary<string, object> dictionary, string key)
        {
            object value;
            return dictionary != null && dictionary.TryGetValue(key, out value) ? value as string : null;
        }

        private static Dictionary<string, object> ReadVdf(string filename)
        {
            if (new FileInfo(filename).Length > 4 * 1024 * 1024) throw new InvalidOperationException("Steam metadata is unexpectedly large.");
            try { return new VdfReader(File.ReadAllText(filename)).ReadObject(false); }
            catch (FormatException error) { throw new InvalidOperationException("Steam metadata is malformed: " + System.IO.Path.GetFileName(filename), error); }
        }

        private sealed class VdfReader
        {
            private readonly string text;
            private int position;
            public VdfReader(string value) { text = value; }
            public Dictionary<string, object> ReadObject(bool nested)
            {
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (;;)
                {
                    string key = Token();
                    if (key == null) { if (nested) throw new FormatException("Unclosed object."); return result; }
                    if (key == "}") { if (!nested) throw new FormatException("Unexpected closing brace."); return result; }
                    if (key == "{") throw new FormatException("Expected metadata key.");
                    string value = Token();
                    if (value == null || value == "}") throw new FormatException("Missing metadata value.");
                    result[key] = value == "{" ? (object)ReadObject(true) : value;
                }
            }
            private string Token()
            {
                while (position < text.Length)
                {
                    char c = text[position];
                    if (Char.IsWhiteSpace(c) || c == '\uFEFF') { position++; continue; }
                    if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
                    { while (position < text.Length && text[position] != '\n') position++; continue; }
                    break;
                }
                if (position >= text.Length) return null;
                char first = text[position++];
                if (first == '{' || first == '}') return first.ToString();
                var token = new StringBuilder();
                if (first == '"')
                {
                    while (position < text.Length)
                    {
                        char c = text[position++];
                        if (c == '"') return token.ToString();
                        if (c == '\\' && position < text.Length)
                        {
                            char escaped = text[position++];
                            switch (escaped)
                            {
                                case '\\': token.Append('\\'); break;
                                case '"': token.Append('"'); break;
                                case 'n': token.Append('\n'); break;
                                case 'r': token.Append('\r'); break;
                                case 't': token.Append('\t'); break;
                                default: token.Append('\\').Append(escaped); break;
                            }
                        }
                        else token.Append(c);
                    }
                    throw new FormatException("Unclosed quoted string.");
                }
                token.Append(first);
                while (position < text.Length && !Char.IsWhiteSpace(text[position]) && text[position] != '{' && text[position] != '}')
                    token.Append(text[position++]);
                return token.ToString();
            }
        }
    }
}

