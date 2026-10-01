using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;

internal static class Rdr2RuntimeTests
{
    private static int passed, failed;
    public static int RunAll()
    {
        passed = failed = 0;
        Run("RDR2 download supplies official HTTPS destination and author referrer", () => {
            using var f = new Fixture();
            var calls = 0;
            using var http = Client(request => {
                calls++;
                Assert(request.RequestUri!.AbsoluteUri == Rdr2RuntimePackage.DownloadUrl);
                Assert(request.Headers.Referrer!.AbsoluteUri == Rdr2RuntimePackage.AuthorPage);
                return Response(f.Archive);
            });
            var result = Download(http, f.Spec);
            Assert(calls == 1 && result.Files.Count == 3 && result.Files.All(file => file.Path != "NativeTrainer.asi"));
        });
        Run("RDR2 download follows only bounded author HTTPS redirects", () => {
            using var f = new Fixture(); var calls = 0;
            using var http = Client(request => ++calls == 1 ? Redirect("https://www.dev-c.com/files/verified.zip") : Response(f.Archive));
            Assert(Download(http, f.Spec).Files.Count == 3 && calls == 2);
            using var loop = Client(_ => Redirect(Rdr2RuntimePackage.DownloadUrl));
            Fails(() => Download(loop, f.Spec));
        });
        Run("RDR2 download rejects downgraded foreign and credential redirects", () => {
            using var f = new Fixture();
            foreach (var target in new[] { "http://dev-c.com/runtime.zip", "https://example.com/runtime.zip", "https://dev-c.com.evil.test/runtime.zip",
                "https://user@dev-c.com/runtime.zip", "https://dev-c.com:444/runtime.zip", "https://dev-c.com/runtime.zip#bad" }) {
                var calls = 0; using var http = Client(_ => { calls++; return Redirect(target); });
                Fails(() => Download(http, f.Spec)); Assert(calls == 1);
            }
        });
        Run("RDR2 download rejects HTTP errors wrong lengths HTML and truncated data", () => {
            using var f = new Fixture();
            foreach (var response in new[] { new HttpResponseMessage(HttpStatusCode.Forbidden), Response(Encoding.UTF8.GetBytes("<html>redirect</html>")),
                Response(f.Archive[..^1]), Response(f.Archive.Concat(new byte[] { 0 }).ToArray()) }) {
                using var http = Client(_ => response); Fails(() => Download(http, f.Spec));
            }
            var damaged = f.Archive.ToArray(); damaged[^1] ^= 1;
            using var corrupt = Client(_ => Response(damaged)); Fails(() => Download(corrupt, f.Spec));
        });
        Run("RDR2 streamed download enforces size without a Content-Length header", () => {
            using var f = new Fixture();
            foreach (var bytes in new[] { f.Archive[..^1], f.Archive.Concat(new byte[] { 0 }).ToArray() }) {
                using var http = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnseekableStream(bytes)) });
                Fails(() => Download(http, f.Spec));
            }
        });
        Run("RDR2 canceled runtime download performs no network request", () => {
            using var f = new Fixture(); var calls = 0; using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            using var http = Client(_ => { calls++; return Response(f.Archive); });
            Fails(() => Rdr2RuntimePackage.DownloadAsync(http, f.Spec, token: cancelled.Token).GetAwaiter().GetResult());
            Assert(calls == 0);
        });
        Run("RDR2 runtime archive rejects unexpected missing and duplicate entries", () => {
            using var f = new Fixture();
            foreach (var entries in new[] { f.Items.Append(new Item("other.dll", new byte[] { 1 })).ToArray(), f.Items.Skip(1).ToArray(),
                f.Items.Append(f.Items[0]).ToArray() }) {
                var archive = Archive(entries); Fails(() => Rdr2RuntimePackage.ReadArchive(archive, f.Spec with { ArchiveLength = archive.Length, Sha256 = Digest(archive) }));
            }
        });
        Run("RDR2 runtime archive rejects traversal backslash absolute and symlink entries", () => {
            using var f = new Fixture();
            foreach (var path in new[] { "../outside.dll", "bin/../outside.dll", "bin\\outside.dll", "/outside.dll", "C:/outside.dll", "bin//outside.dll" }) {
                var items = f.Items.ToArray(); items[2] = items[2] with { Name = path };
                var archive = Archive(items); var spec = Spec(archive, items, f.Spec.Components);
                Fails(() => Rdr2RuntimePackage.ReadArchive(archive, spec));
            }
            var links = f.Items.ToArray(); links[1] = links[1] with { Attributes = unchecked((int)0xA0000000) };
            var linkArchive = Archive(links); Fails(() => Rdr2RuntimePackage.ReadArchive(linkArchive, Spec(linkArchive, links, f.Spec.Components)));
        });
        Run("RDR2 runtime archive enforces component hashes and x64 native exports", () => {
            using var f = new Fixture();
            var badHash = f.Spec.Components.Select((component, index) => index == 0 ? component with { Sha256 = new string('0', 64) } : component).ToArray();
            Fails(() => Rdr2RuntimePackage.ReadArchive(f.Archive, f.Spec with { Components = badHash }));
            foreach (var dll in new[] { NativeDll(Rdr2Module.HookExports, 0x14c), NativeDll(Rdr2Module.HookExports.Skip(1).ToArray()), Encoding.UTF8.GetBytes("not a PE") }) {
                var items = f.Items.ToArray(); items[3] = items[3] with { Bytes = dll };
                var components = f.Spec.Components.Select(component => component.Path == Rdr2RuntimePackage.HookPath ? component with { Length = dll.Length, Sha256 = Digest(dll) } : component).ToArray();
                var archive = Archive(items); Fails(() => Rdr2RuntimePackage.ReadArchive(archive, Spec(archive, items, components)));
            }
        });
        Run("RDR2 runtime inspection distinguishes partial missing ready and conflicts", () => {
            using var f = new Fixture(); Assert(f.Inspect().Status == Rdr2RuntimePackage.State.Missing);
            f.GameFile(Rdr2RuntimePackage.HookPath, f.Hook); Assert(f.Inspect().NeedsDownload);
            f.GameFile(Rdr2RuntimePackage.LoaderPath, f.Loader); Assert(f.Inspect().Status == Rdr2RuntimePackage.State.Ready);
            f.GameFile(Rdr2RuntimePackage.LoaderPath, new byte[] { 1 }); Assert(f.Inspect().Status == Rdr2RuntimePackage.State.Conflict);
            File.Delete(f.Path(Rdr2RuntimePackage.LoaderPath)); Directory.CreateDirectory(f.Path(Rdr2RuntimePackage.LoaderPath));
            Assert(f.Inspect().Status == Rdr2RuntimePackage.State.Conflict);
        });
        Run("RDR2 integrated install owns downloaded components and preserves shared runtime on removal", () => {
            using var f = new Fixture(); f.GameFile("Rampage.asi", Encoding.UTF8.GetBytes("foreign trainer"));
            var order = new List<string>(); f.Install(new ImmediateProgress(order.Add));
            Assert(Rdr2Installer.Installed(f.Game) && f.Inspect().Status == Rdr2RuntimePackage.State.Ready);
            Assert(File.ReadAllBytes(f.Path(Rdr2RuntimePackage.ReadmePath)).SequenceEqual(f.Readme));
            Assert(order.Last(value => value.StartsWith("Installing ")) == "Installing dinput8.dll");
            Assert(f.Receipt().Files.Count == 5);
            f.Remove();
            Assert(!Rdr2Installer.Installed(f.Game) && f.Inspect().Status == Rdr2RuntimePackage.State.Ready);
            Assert(File.Exists(f.Path("Rampage.asi")) && File.Exists(f.Path(Rdr2RuntimePackage.ReadmePath)) && f.Receipt().Files.Count == 3);
        });
        Run("RDR2 identical foreign runtime is reused without claiming ownership", () => {
            using var f = new Fixture(); f.GameFile(Rdr2RuntimePackage.HookPath, f.Hook); f.GameFile(Rdr2RuntimePackage.LoaderPath, f.Loader);
            f.Install(); Assert(f.Receipt().Files.All(file => file.Path is not (Rdr2RuntimePackage.HookPath or Rdr2RuntimePackage.LoaderPath)));
            f.Remove(); Assert(f.Inspect().Status == Rdr2RuntimePackage.State.Ready);
        });
        Run("RDR2 runtime conflict prevents every trainer write even with a forged ownership receipt", () => {
            foreach (var target in new[] { Rdr2RuntimePackage.HookPath, Rdr2RuntimePackage.LoaderPath, Rdr2RuntimePackage.ReadmePath }) {
                using var f = new Fixture(); f.GameFile(target, Encoding.UTF8.GetBytes("foreign"));
                Fails(() => f.Install()); Assert(f.GameFiles().Length == 1 && File.ReadAllText(f.Path(target)) == "foreign");
                var component = f.Spec.Components.Single(component => component.Path == target);
                WriteJson(f.Path(Rdr2Installer.ReceiptPath), new Rdr2Installer.Receipt("red-dead-redemption-2", new() { new(target, component.Sha256) }));
                Fails(() => f.Install()); Assert(f.GameFiles().Length == 2 && File.ReadAllText(f.Path(target)) == "foreign");
            }
        });
        Run("RDR2 interrupted full install rolls back prerequisites and trainer together", () => {
            for (var interrupt = 1; interrupt <= 8; interrupt++) {
                using var f = new Fixture(); var calls = 0; var stop = interrupt;
                Fails(() => f.Install(check: () => { if (++calls == stop) throw new IOException("game started"); }));
                Assert(f.GameFiles().Length == 0);
            }
        });
        Run("RDR2 runtime stage collision rolls back earlier trainer and hook writes", () => {
            using var f = new Fixture(); f.GameFile(Rdr2RuntimePackage.LoaderPath + ".happycheats-stage", Encoding.UTF8.GetBytes("old staging"));
            Fails(() => f.Install()); Assert(f.GameFiles().Length == 1 && File.ReadAllText(f.Path(Rdr2RuntimePackage.LoaderPath + ".happycheats-stage")) == "old staging");
        });
        Run("RDR2 rollback preserves a concurrent runtime change", () => {
            using var f = new Fixture();
            Fails(() => f.Install(new ImmediateProgress(message => {
                if (message == "Installing ScriptHookRDR2.dll") { f.GameFile(Rdr2RuntimePackage.HookPath, Encoding.UTF8.GetBytes("user edit")); throw new IOException("interrupted"); }
            })));
            Assert(f.GameFiles().Length == 1 && File.ReadAllText(f.Path(Rdr2RuntimePackage.HookPath)) == "user edit");
        });
        Run("RDR2 integrated setup rejects mutated runtime and corrupt trainer before writing", () => {
            using var f = new Fixture(); var runtime = f.Runtime(); runtime.Files[0].Bytes[0] ^= 1;
            Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => { }, null, runtime, f.Spec)); Assert(f.GameFiles().Length == 0);
            f.PayloadFile(Rdr2Installer.Plugin, "changed"); Fails(() => f.Install()); Assert(f.GameFiles().Length == 0);
        });
        Run("RDR2 public installer refuses a synthetic test specification", () => {
            using var f = new Fixture(); Fails(() => Rdr2Installer.Install(f.Package, f.Game, () => { }, runtime: f.Runtime()));
            Assert(f.GameFiles().Length == 0);
        });
        Run("RDR2 reinstallation retains downloaded ownership and modified runtime survives removal", () => {
            using var f = new Fixture(); f.Install(); f.Install(); Assert(f.Receipt().Files.Count == 5);
            f.GameFile(Rdr2RuntimePackage.HookPath, Encoding.UTF8.GetBytes("user updated hook"));
            f.Remove(); Assert(File.ReadAllText(f.Path(Rdr2RuntimePackage.HookPath)) == "user updated hook");
        });
        Run("RDR2 forged runtime receipt hashes remain invalid", () => {
            using var f = new Fixture(); f.GameFile(Rdr2RuntimePackage.LoaderPath, Encoding.UTF8.GetBytes("foreign"));
            WriteJson(f.Path(Rdr2Installer.ReceiptPath), new Rdr2Installer.Receipt("red-dead-redemption-2", new() { new(Rdr2RuntimePackage.LoaderPath, Hash(f.Path(Rdr2RuntimePackage.LoaderPath))) }));
            Fails(() => f.Install()); Fails(() => f.Remove()); Assert(File.ReadAllText(f.Path(Rdr2RuntimePackage.LoaderPath)) == "foreign");
        });
        Run("RDR2 supported game with missing runtime advertises integrated installation", () => {
            using var f = new Fixture(); f.GameFile(Rdr2Module.Executable, Encoding.UTF8.GetBytes("game fixture")); var downloads = 0;
            var module = f.Module(download: _ => { downloads++; throw new IOException("network unavailable"); });
            var installation = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Assert(installation.IsInstalled && installation.Compatible && !installation.TrainerInstalled && downloads == 0);
            Assert(installation.Message.Contains("download", StringComparison.OrdinalIgnoreCase));
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
            Assert(downloads == 1 && f.GameFiles().Length == 1);
        });
        Run("RDR2 unsupported and running games never download or install runtime", () => {
            foreach (var running in new[] { false, true }) {
                using var f = new Fixture(); f.GameFile(Rdr2Module.Executable, Encoding.UTF8.GetBytes("game fixture")); var downloads = 0;
                var module = f.Module(version: running ? Rdr2Module.SupportedExecutableVersion : "1.0.1491.99",
                    processes: _ => running ? new[] { (123, DateTime.UtcNow) } : Array.Empty<(int, DateTime)>(),
                    download: _ => { downloads++; throw new IOException("unexpected download"); });
                var installation = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
                Assert(running ? installation.IsRunning : !installation.Compatible);
                Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult()); Assert(downloads == 0 && f.GameFiles().Length == 1);
            }
        });
        Run("RDR2 conflicting runtime disables installation without requesting network", () => {
            using var f = new Fixture(); f.GameFile(Rdr2Module.Executable, Encoding.UTF8.GetBytes("game fixture"));
            f.GameFile(Rdr2RuntimePackage.LoaderPath, Encoding.UTF8.GetBytes("foreign loader")); var downloads = 0;
            var module = f.Module(download: _ => { downloads++; throw new IOException("unexpected download"); });
            var installation = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Assert(!installation.Compatible && installation.Message == Rdr2RuntimePackage.ConflictMessage);
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult()); Assert(downloads == 0 && f.GameFiles().Length == 2);
        });
        Run("RDR2 valid existing runtime skips download while installing trainer", () => {
            using var f = new Fixture(); f.GameFile(Rdr2Module.Executable, Encoding.UTF8.GetBytes("game fixture")); var downloads = 0;
            var module = f.Module(inspect: _ => new(Rdr2RuntimePackage.State.Ready, "Ready fixture"),
                download: _ => { downloads++; throw new IOException("unexpected download"); });
            var installation = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            module.InstallAsync(installation).GetAwaiter().GetResult();
            Assert(downloads == 0 && Rdr2Installer.Installed(f.Game));
        });
        Run("RDR2 corrupt trainer package prevents prerequisite download", () => {
            using var f = new Fixture(); f.GameFile(Rdr2Module.Executable, Encoding.UTF8.GetBytes("game fixture")); f.PayloadFile(Rdr2Installer.Plugin, "corrupt"); var downloads = 0;
            var module = f.Module(download: _ => { downloads++; throw new IOException("unexpected download"); });
            var installation = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult()); Assert(downloads == 0 && f.GameFiles().Length == 1);
        });
        Run("RDR2 rechecks game state after prerequisite download before writing", () => {
            using var f = new Fixture(); f.GameFile(Rdr2Module.Executable, Encoding.UTF8.GetBytes("game fixture")); var running = false;
            var module = f.Module(processes: _ => running ? new[] { (123, DateTime.UtcNow) } : Array.Empty<(int, DateTime)>(),
                download: _ => { running = true; return Task.FromResult(f.Runtime()); });
            var installation = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult()); Assert(f.GameFiles().Length == 1);
        });
        Run("RDR2 ready runtime removed during setup fails without trainer writes", () => {
            using var f = new Fixture(); f.GameFile(Rdr2Module.Executable, Encoding.UTF8.GetBytes("game fixture")); var inspections = 0;
            var module = f.Module(inspect: _ => ++inspections <= 2 ? new(Rdr2RuntimePackage.State.Ready, "Ready fixture") : new(Rdr2RuntimePackage.State.Missing, "Missing fixture"));
            var installation = module.DiscoverAsync(f.Game).GetAwaiter().GetResult();
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult()); Assert(f.GameFiles().Length == 1);
        });
        Console.WriteLine($"RDR2 integrated runtime tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new FakeHandler(respond));
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Redirect(string target) { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri(target); return response; }
    private static Rdr2RuntimePackage.VerifiedRuntime Download(HttpClient client, Rdr2RuntimePackage.Specification specification) => Rdr2RuntimePackage.DownloadAsync(client, specification).GetAwaiter().GetResult();
    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes) { public override bool CanSeek => false; }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation rejection."); }
    private static void Run(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }
    private sealed class ImmediateProgress(Action<string> report) : IProgress<string> { public void Report(string message) => report(message); }
    private sealed record Item(string Name, byte[] Bytes, int Attributes = 0);
    private static byte[] Archive(IEnumerable<Item> items) {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true)) foreach (var item in items) {
            var entry = archive.CreateEntry(item.Name); entry.ExternalAttributes = item.Attributes;
            using var stream = entry.Open(); stream.Write(item.Bytes);
        }
        return output.ToArray();
    }
    private static Rdr2RuntimePackage.Specification Spec(byte[] archive, IEnumerable<Item> items, IReadOnlyList<Rdr2RuntimePackage.Component> components) =>
        new(archive.Length, Digest(archive), items.ToDictionary(item => item.Name, item => (long)item.Bytes.Length, StringComparer.Ordinal), components);
    private static byte[] NativeDll(string[] exports, ushort machine = 0x8664) {
        var bytes = new byte[8192]; using var stream = new MemoryStream(bytes); using var writer = new BinaryWriter(stream);
        void At(int offset, Action write) { stream.Position = offset; write(); }
        At(0, () => writer.Write((ushort)0x5a4d)); At(0x3c, () => writer.Write(0x80));
        At(0x80, () => { writer.Write(0x00004550); writer.Write(machine); writer.Write((ushort)1); });
        At(0x94, () => { writer.Write((ushort)240); writer.Write((ushort)0x2022); });
        const int optional = 0x98;
        At(optional, () => writer.Write((ushort)0x20b)); At(optional + 32, () => { writer.Write(0x1000); writer.Write(0x200); });
        At(optional + 56, () => { writer.Write(0x3000); writer.Write(0x200); });
        At(optional + 108, () => { writer.Write(16); writer.Write(0x1000); writer.Write(40); });
        At(optional + 240, () => { writer.Write(Encoding.ASCII.GetBytes(".rdata\0\0")); writer.Write(0x1000); writer.Write(0x1000); writer.Write(0x1000); writer.Write(0x200); });
        At(0x200 + 24, () => writer.Write(exports.Length)); At(0x200 + 32, () => writer.Write(0x1100));
        var nextName = 0x400;
        for (var index = 0; index < exports.Length; index++) {
            At(0x300 + index * 4, () => writer.Write(nextName - 0x200 + 0x1000));
            At(nextName, () => { writer.Write(Encoding.ASCII.GetBytes(exports[index])); writer.Write((byte)0); });
            nextName += Encoding.ASCII.GetByteCount(exports[index]) + 1;
        }
        return bytes;
    }
    private sealed class Fixture : IDisposable {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HappyCheats-rdr2-runtime-tests-" + Guid.NewGuid().ToString("N"));
        internal readonly byte[] Hook = NativeDll(Rdr2Module.HookExports), Loader = NativeDll(new[] { "DirectInput8Create" }), Readme = Encoding.UTF8.GetBytes("Author runtime readme");
        internal readonly Item[] Items;
        internal readonly byte[] Archive;
        internal readonly Rdr2RuntimePackage.Specification Spec;
        internal string Game => System.IO.Path.Combine(root, "game");
        internal string Package => System.IO.Path.Combine(root, "payload", "red-dead-redemption-2");
        internal Fixture() {
            Directory.CreateDirectory(Game);
            Items = new[] { new Item("bin/", Array.Empty<byte>()), new Item("bin/dinput8.dll", Loader), new Item("bin/NativeTrainer.asi", new byte[] { 1 }),
                new Item("bin/ScriptHookRDR2.dll", Hook), new Item("readme.txt", Readme), new Item("www.dev-c.com.url", Encoding.UTF8.GetBytes("https://dev-c.com")) };
            Archive = Rdr2RuntimeTests.Archive(Items);
            Spec = Rdr2RuntimeTests.Spec(Archive, Items, new[] {
                new Rdr2RuntimePackage.Component("bin/ScriptHookRDR2.dll", Rdr2RuntimePackage.HookPath, Hook.Length, Digest(Hook)),
                new Rdr2RuntimePackage.Component("bin/dinput8.dll", Rdr2RuntimePackage.LoaderPath, Loader.Length, Digest(Loader)),
                new Rdr2RuntimePackage.Component("readme.txt", Rdr2RuntimePackage.ReadmePath, Readme.Length, Digest(Readme)) });
            PayloadFile(Rdr2Installer.Plugin, "trainer"); PayloadFile(Rdr2Installer.BridgeReadme, "bridge");
            WriteJson(SafePath(Package, "package.json"), new { game_id = "red-dead-redemption-2", app_id = 1174180, version = Rdr2Module.TrainerVersion,
                files = Rdr2Installer.RequiredFiles.Select(path => new { path, sha256 = Hash(SafePath(Package, path)) }).ToArray() });
        }
        internal string Path(string relative) => SafePath(Game, relative);
        internal string[] GameFiles() => Directory.GetFiles(Game, "*", SearchOption.AllDirectories);
        internal void GameFile(string relative, byte[] bytes) { var path = Path(relative); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
        internal void PayloadFile(string relative, string text) { var path = SafePath(Package, relative); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        internal Rdr2RuntimePackage.VerifiedRuntime Runtime() => Rdr2RuntimePackage.ReadArchive(Archive, Spec);
        internal Rdr2RuntimePackage.Installation Inspect() => Rdr2RuntimePackage.Inspect(Game, Spec);
        internal Rdr2Module Module(string version = Rdr2Module.SupportedExecutableVersion,
            Func<string, (int Id, DateTime Started)[]>? processes = null,
            Func<string, Rdr2RuntimePackage.Installation>? inspect = null,
            Func<IProgress<string>?, Task<Rdr2RuntimePackage.VerifiedRuntime>>? download = null) => new(root) {
                VersionProvider = _ => version,
                ProcessProvider = processes ?? (_ => Array.Empty<(int, DateTime)>()),
                RuntimeInspector = inspect ?? Rdr2RuntimePackage.Inspect,
                RuntimeDownloader = download ?? (_ => throw new IOException("Unexpected runtime download in offline test."))
            };
        internal void Install(IProgress<string>? progress = null, Action? check = null) => Rdr2Installer.Install(Package, Game, check ?? (() => { }), progress, Runtime(), Spec);
        internal void Remove() => Rdr2Installer.Remove(Game, () => { }, null, Spec);
        internal Rdr2Installer.Receipt Receipt() => JsonSerializer.Deserialize<Rdr2Installer.Receipt>(File.ReadAllText(Path(Rdr2Installer.ReceiptPath)))!;
        public void Dispose() {
            var path = System.IO.Path.GetFullPath(root); var temporary = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            if (!path.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(path).StartsWith("HappyCheats-rdr2-runtime-tests-", StringComparison.Ordinal)) throw new IOException("Invalid test cleanup directory.");
            Directory.Delete(path, recursive: true);
        }
    }
}
