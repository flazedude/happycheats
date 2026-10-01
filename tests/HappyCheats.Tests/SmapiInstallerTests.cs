using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class SmapiInstallerTests
{
    private static int passed, failed;
    private const string Prefix = "SMAPI 4.5.2 installer/internal/windows/";
    private static readonly string[] InstallerFiles = { "SMAPI.Installer.exe", "SMAPI.Installer.dll", "SMAPI.Installer.runtimeconfig.json", "install.dat", "hostfxr.dll" };
    private static readonly string[] RuntimeAssemblies = { "0Harmony.dll", "Markdig.dll", "Mono.Cecil.dll", "Mono.Cecil.Mdb.dll", "Mono.Cecil.Pdb.dll",
        "MonoMod.Common.dll", "Newtonsoft.Json.dll", "Pathoschild.Http.Client.dll", "Pintail.dll", "SMAPI.Toolkit.CoreInterfaces.dll", "SMAPI.Toolkit.dll",
        "System.Net.Http.Formatting.dll", "TMXTile.dll", "VdfConverter.dll" };

    public static int RunAll()
    {
        Run("SMAPI detects missing older current and newer versions using metadata", () => {
            using var f = new Fixture();
            Assert(SmapiInstaller.Inspect(f.Game).Status == SmapiInstaller.State.Missing);
            foreach (var (version, state) in new[] { (new Version(4, 5, 1, 0), SmapiInstaller.State.Outdated),
                (new Version(4, 5, 2, 0), SmapiInstaller.State.Ready), (new Version(4, 6, 0, 0), SmapiInstaller.State.Ready) }) {
                WriteLoader(f.Game, version);
                var installed = SmapiInstaller.Inspect(f.Game);
                Assert(installed.Status == state && installed.Version == version);
            }
        });
        Run("SMAPI rejects incomplete empty wrong-assembly and malformed installations", () => {
            using var f = new Fixture();
            f.GameFile("StardewModdingAPI.exe", "existing");
            Assert(SmapiInstaller.Inspect(f.Game).Status == SmapiInstaller.State.Invalid);
            WriteLoader(f.Game, SmapiInstaller.BundledVersion);
            f.GameFile("smapi-internal/0Harmony.dll", "");
            Assert(SmapiInstaller.Inspect(f.Game).Status == SmapiInstaller.State.Invalid);
            WriteLoader(f.Game, SmapiInstaller.BundledVersion);
            WriteAssembly(SafePath(f.Game, "StardewModdingAPI.dll"), "AnotherAssembly", SmapiInstaller.BundledVersion);
            Assert(SmapiInstaller.Inspect(f.Game).Status == SmapiInstaller.State.Invalid);
            f.GameFile("StardewModdingAPI.dll", "not an assembly");
            Assert(SmapiInstaller.Inspect(f.Game).Status == SmapiInstaller.State.Invalid);
        });
        Run("SMAPI incomplete installer output cannot omit game runtime metadata or loader configuration", () => {
            using var f = new Fixture();
            foreach (var path in new[] { "StardewModdingAPI.deps.json", "StardewModdingAPI.runtimeconfig.json", "smapi-internal/config.json", "smapi-internal/Newtonsoft.Json.dll" }) {
                WriteLoader(f.Game, SmapiInstaller.BundledVersion); File.Delete(SafePath(f.Game, path));
                Assert(SmapiInstaller.Inspect(f.Game).Status == SmapiInstaller.State.Invalid);
            }
        });
        Run("SMAPI requires every shipped runtime assembly to be present and nonempty", () => {
            using var f = new Fixture();
            foreach (var assembly in RuntimeAssemblies) {
                var relative = "smapi-internal/" + assembly;
                WriteLoader(f.Game, SmapiInstaller.BundledVersion); File.Delete(SafePath(f.Game, relative));
                Assert(SmapiInstaller.Inspect(f.Game).Status == SmapiInstaller.State.Invalid);
                f.GameFile(relative, "");
                Assert(SmapiInstaller.Inspect(f.Game).Status == SmapiInstaller.State.Invalid);
            }
        });
        Run("SMAPI keeps current and newer versions without needing the bundle", () => {
            using var f = new Fixture();
            foreach (var version in new[] { SmapiInstaller.BundledVersion, new Version(5, 0, 0, 0) }) {
                WriteLoader(f.Game, version); f.GameFile("Mods/OtherMod/manifest.json", "foreign");
                var before = Snapshot(f.Game); var checks = 0;
                SmapiInstaller.EnsureInstalled(f.Package, f.Game, () => checks++);
                Assert(checks == 1 && before.SequenceEqual(Snapshot(f.Game)));
            }
        });
        Run("SMAPI running-game guard and absent bundle prevent all setup writes", () => {
            using var f = new Fixture(); var before = Snapshot(f.Game);
            Assert(Fails(() => SmapiInstaller.EnsureInstalled(f.Package, f.Game, () => throw new IOException("game running"))).Message == "game running");
            Assert(Fails(() => SmapiInstaller.EnsureInstalled(f.Package, f.Game, () => { })).Message == SmapiInstaller.MissingBundle);
            Assert(before.SequenceEqual(Snapshot(f.Game)));
        });
        Run("SMAPI checksum failure prevents execution and target changes", () => {
            using var f = new Fixture(); f.PackageFile(SmapiInstaller.BundleFile, "not the official archive");
            var before = Snapshot(f.Game);
            Assert(Fails(() => SmapiInstaller.EnsureInstalled(f.Package, f.Game, () => { })).Message.Contains("checksum failed"));
            Assert(before.SequenceEqual(Snapshot(f.Game)));
        });
        Run("SMAPI incomplete existing installation is preserved before bundle access", () => {
            using var f = new Fixture(); f.GameFile("StardewModdingAPI.exe", "foreign"); var before = Snapshot(f.Game);
            Assert(Fails(() => SmapiInstaller.EnsureInstalled(f.Package, f.Game, () => { })).Message == SmapiInstaller.InvalidInstallation);
            Assert(before.SequenceEqual(Snapshot(f.Game)));
        });
        Run("SMAPI extraction selects Windows files and leaves other platforms untouched", () => {
            using var f = new Fixture();
            using var archive = Archive(InstallerFiles.Select(file => Prefix + file).Concat(new[] { "SMAPI 4.5.2 installer/internal/linux/do-not-run", "README.txt" }));
            SmapiInstaller.ExtractWindowsInstaller(archive, f.Extract);
            Assert(Directory.EnumerateFiles(f.Extract, "*", SearchOption.AllDirectories).Count() == InstallerFiles.Length);
            Assert(InstallerFiles.All(file => File.Exists(SafePath(f.Extract, file))));
        });
        Run("SMAPI extraction rejects traversal duplicates and incomplete archives before writing", () => {
            using var f = new Fixture();
            foreach (var extra in new[] { "../outside", "nested/../../outside", "nested\\outside", "file:stream", "INSTALL.DAT" }) {
                using var archive = Archive(InstallerFiles.Select(file => Prefix + file).Append(Prefix + extra));
                Fails(() => SmapiInstaller.ExtractWindowsInstaller(archive, f.Extract));
                Assert(!Directory.Exists(f.Extract));
            }
            using var missing = Archive(new[] { Prefix + "SMAPI.Installer.exe" });
            Fails(() => SmapiInstaller.ExtractWindowsInstaller(missing, f.Extract)); Assert(!Directory.Exists(f.Extract));
        });
        Run("SMAPI installer uses a hidden real console with a separated game-path argument", () => {
            using var f = new Fixture(); var directory = Path.Combine(f.Game, "spaces & symbols");
            var start = SmapiInstaller.InstallerStartInfo(f.Extract, directory);
            Assert(start.UseShellExecute && start.WindowStyle == ProcessWindowStyle.Hidden && !start.CreateNoWindow);
            Assert(!start.RedirectStandardOutput && !start.RedirectStandardError && !start.RedirectStandardInput);
            Assert(start.ArgumentList.SequenceEqual(new[] { "--install", "--no-prompt", "--game-path", Path.GetFullPath(directory) }));
        });
        Run("SMAPI verifies files after an apparently successful installer exit", () => {
            using var f = new Fixture(); var called = false;
            Fails(() => SmapiInstaller.ExecuteAndValidate(f.Extract, f.Game, () => { }, _ => { called = true; return 0; }));
            Assert(called && !StardewInstaller.Installed(f.Game));
            Fails(() => SmapiInstaller.ExecuteAndValidate(f.Extract, f.Game, () => { }, _ => { WriteLoader(f.Game, new Version(4, 5, 1, 0)); return 0; }));
            Assert(!StardewInstaller.Installed(f.Game));
        });
        Run("SMAPI rejects a failing installer even when files were copied", () => {
            using var f = new Fixture();
            Fails(() => SmapiInstaller.ExecuteAndValidate(f.Extract, f.Game, () => { }, _ => { WriteLoader(f.Game, SmapiInstaller.BundledVersion); return 1; }));
            Assert(!StardewInstaller.Installed(f.Game));
        });
        Run("SMAPI successful setup preserves other mods and checks game before and after", () => {
            using var f = new Fixture(); f.GameFile("Mods/OtherMod/manifest.json", "foreign"); var checks = 0;
            SmapiInstaller.ExecuteAndValidate(f.Extract, f.Game, () => checks++, _ => { WriteLoader(f.Game, SmapiInstaller.BundledVersion); return 0; });
            Assert(checks == 2 && SmapiInstaller.Inspect(f.Game).Status == SmapiInstaller.State.Ready);
            Assert(File.ReadAllText(SafePath(f.Game, "Mods/OtherMod/manifest.json")) == "foreign");
        });
        Run("SMAPI second phase never downgrades an installation completed concurrently", () => {
            using var f = new Fixture(); WriteLoader(f.Game, new Version(4, 6, 0, 0)); var before = Snapshot(f.Game);
            SmapiInstaller.ExecuteAndValidate(f.Extract, f.Game, () => { }, _ => throw new Exception("Must not execute"));
            Assert(before.SequenceEqual(Snapshot(f.Game)));
        });
        Run("Stardew discovery offers bundled setup and keeps invalid game versions gated", () => {
            using var f = new Fixture(); f.PackageFile(SmapiInstaller.BundleFile, "fixture existence only");
            var module = new StardewModule(f.Root);
            var found = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Assert(found.IsInstalled && found.Compatible && found.Message == StardewModule.SmapiGuidance);
            WriteAssembly(SafePath(f.Game, "Stardew Valley.dll"), "Stardew Valley", new Version(1, 6, 14, 0));
            var unsupported = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Assert(!unsupported.Compatible && unsupported.Message.Contains("compatibility check"));
        });
        Run("Stardew discovery reports missing bundle and reuses compatible external SMAPI", () => {
            using var f = new Fixture(); var module = new StardewModule(f.Root);
            var missing = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Assert(!missing.Compatible && missing.Message == SmapiInstaller.MissingBundle);
            WriteLoader(f.Game, new Version(4, 6, 0, 0));
            Assert(module.DiscoverAsync(f.Game).GetAwaiter().GetResult().Compatible);
            f.GameFile("StardewModdingAPI.dll", "invalid");
            var invalid = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Assert(!invalid.Compatible && invalid.Message == SmapiInstaller.InvalidInstallation);
        });
        Run("Stardew validates trainer payload before installing the loader", () => {
            using var f = new Fixture(); var module = new StardewModule(f.Root);
            f.PackageFile(SmapiInstaller.BundleFile, "invalid archive");
            WriteJson(SafePath(f.Package, "package.json"), new { files = Array.Empty<object>() });
            var installation = module.DiscoverAsync(f.Game).GetAwaiter().GetResult(); var before = Snapshot(f.Game);
            var failure = Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
            Assert(failure.Message.Contains("required mod file") && before.SequenceEqual(Snapshot(f.Game)));
        });
        Run("Stardew only installs its mod after loader setup has succeeded", () => {
            using var f = new Fixture(); var module = new StardewModule(f.Root);
            foreach (var path in StardewInstaller.RequiredFiles) f.PackageFile(path, "fixture " + path);
            WriteJson(SafePath(f.Package, "package.json"), new { files = StardewInstaller.RequiredFiles.Select(path => new { path, sha256 = Hash(SafePath(f.Package, path)) }) });
            f.PackageFile(SmapiInstaller.BundleFile, "invalid archive");
            var installation = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult()); Assert(!StardewInstaller.Installed(f.Game));
            WriteLoader(f.Game, SmapiInstaller.BundledVersion);
            module.InstallAsync(installation).GetAwaiter().GetResult(); Assert(StardewInstaller.Installed(f.Game));
            module.RemoveAsync(installation).GetAwaiter().GetResult();
            Assert(!StardewInstaller.Installed(f.Game) && SmapiInstaller.Inspect(f.Game).Status == SmapiInstaller.State.Ready);
        });
        Console.WriteLine($"SMAPI installer tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    internal static void WriteLoader(string root, Version version)
    {
        foreach (var relative in new[] { "StardewModdingAPI.exe", "StardewModdingAPI.runtimeconfig.json", "StardewModdingAPI.deps.json", "smapi-internal/config.json" }
            .Concat(RuntimeAssemblies.Select(name => "smapi-internal/" + name)))
            Put(root, relative, "fixture");
        WriteAssembly(SafePath(root, "StardewModdingAPI.dll"), "StardewModdingAPI", version);
    }

    private static void WriteAssembly(string path, string name, Version version)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), version, default, default, 0, AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var pe = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll), new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
        var bytes = new BlobBuilder(); pe.Serialize(bytes); File.WriteAllBytes(path, bytes.ToArray());
    }

    private static MemoryStream Archive(IEnumerable<string> names)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in names) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write("fixture"); }
        stream.Position = 0; return stream;
    }
    private static void Put(string root, string relative, string content) { var path = SafePath(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); }
    private static string[] Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order().Select(path => path + ":" + Hash(path)).ToArray();
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static Exception Fails(Action action) { try { action(); } catch (Exception error) { return error; } throw new Exception("Expected operation rejection."); }
    private static void Run(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-SMAPI-tests-" + Guid.NewGuid().ToString("N"));
        public string Game => Path.Combine(Root, "game");
        public string Package => Path.Combine(Root, "payload", "stardew");
        public string Extract => Path.Combine(Root, "extract");
        public Fixture() { Directory.CreateDirectory(Game); Directory.CreateDirectory(Package); GameFile("Stardew Valley.exe", "game"); WriteAssembly(SafePath(Game, "Stardew Valley.dll"), "Stardew Valley", new Version(1, 6, 15, 24356)); }
        public void GameFile(string path, string content) => Put(Game, path, content);
        public void PackageFile(string path, string content) => Put(Package, path, content);
        public void Dispose()
        {
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("HappyCheats-SMAPI-tests-", StringComparison.Ordinal)) throw new IOException("Invalid test cleanup path.");
            Directory.Delete(resolved, recursive: true);
        }
    }
}
