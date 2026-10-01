using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HappyCheats.Bootstrap;
using HappyCheats.Updater;

namespace HappyCheats.Bootstrap.Tests;

internal static class Program
{
    private static readonly string[] RequiredFiles =
    [
        "HappyCheats.exe", "HappyCheats.dll", "HappyCheats.runtimeconfig.json",
        "updater/HappyCheats.Updater.exe", "updater/HappyCheats.Updater.dll",
        "updater/HappyCheats.Updater.runtimeconfig.json"
    ];
    private static int passed;
    private static int failed;

    private static int Main()
    {
        Run("extracts every app, helper and binary payload byte", () =>
        {
            using var fixture = new Fixture();
            var files = Release();
            files.Add(new Entry("HappyCheats/payload/中文/asset.bin", Enumerable.Range(0, 100_000).Select(i => (byte)i).ToArray()));
            var archive = Zip(files);
            var target = fixture.Ensure(archive);
            Assert(Path.GetFullPath(target) == Path.Combine(fixture.Cache, Hash(archive), "HappyCheats"), "Unexpected launch directory.");
            foreach (var entry in files.Where(entry => !entry.Name.EndsWith('/')))
                Assert(File.ReadAllBytes(Path.Combine(target, entry.Name["HappyCheats/".Length..])).SequenceEqual(entry.Bytes), entry.Name + " changed.");
        });
        Run("valid cached client is reused without opening the embedded archive", () =>
        {
            using var fixture = new Fixture();
            var archive = Zip(Release());
            var target = fixture.Ensure(archive);
            var again = BundleCache.Ensure(fixture.Cache, Hash(archive).ToUpperInvariant(), NeverOpen);
            Assert(target == again, "Cache path changed.");
        });
        Run("streamed bundles extract completely and close their input", () =>
        {
            using var fixture = new Fixture();
            var archive = Zip(Release());
            var source = new SequentialStream(archive);
            var target = BundleCache.Ensure(fixture.Cache, Hash(archive), () => source);
            Assert(source.Disposed, "Source stream was not disposed.");
            Assert(File.ReadAllBytes(Path.Combine(target, "payload", "module", "trainer.dll")).SequenceEqual(new byte[] { 0, 1, 2, 255, 128, 64 }), "Streamed payload changed.");
            Assert(!Directory.EnumerateFiles(fixture.Cache, "*.zip", SearchOption.AllDirectories).Any(), "Temporary archive remained after installation.");
        });
        Run("interrupted bundle read can retry without a partial client", () =>
        {
            using var fixture = new Fixture();
            var archive = Zip(Release());
            var interrupted = new SequentialStream(archive, failAfterBytes: 256);
            ExpectFailure(() => BundleCache.Ensure(fixture.Cache, Hash(archive), () => interrupted));
            Assert(interrupted.Disposed, "Failed input stream was not disposed.");
            Assert(!Directory.Exists(Path.Combine(fixture.Cache, Hash(archive), "HappyCheats")), "Interrupted stream committed a client.");
            var target = fixture.Ensure(archive);
            Assert(File.Exists(Path.Combine(target, "HappyCheats.exe")), "Retry did not recover.");
        });
        Run("successful updater replacement survives future launches", () =>
        {
            using var fixture = new Fixture();
            var archive = Zip(Release());
            var target = fixture.Ensure(archive);
            var request = PrepareUpdate(target);
            var backup = UpdateInstaller.Apply(request, updated =>
                Assert(File.ReadAllText(Path.Combine(updated, "HappyCheats.exe")) == "updated HappyCheats.exe", "Updated client was not launched."));
            var again = BundleCache.Ensure(fixture.Cache, Hash(archive), NeverOpen);
            Assert(again == target, "Update changed cache location.");
            Assert(File.ReadAllText(Path.Combine(again, "HappyCheats.exe")) == "updated HappyCheats.exe", "Bootstrap reverted updated client.");
            Assert(File.ReadAllText(Path.Combine(again, "update-settings.json")) == "trusted settings", "Updater changed trust settings.");
            Assert(File.ReadAllText(Path.Combine(backup, "HappyCheats.exe")) == "bundled HappyCheats.exe", "Update backup was lost.");
        });
        Run("failed updater replacement leaves the cached original usable", () =>
        {
            using var fixture = new Fixture();
            var archive = Zip(Release());
            var target = fixture.Ensure(archive);
            ExpectFailure(() => UpdateInstaller.Apply(PrepareUpdate(target), _ => throw new IOException("Simulated failed startup.")));
            Assert(BundleCache.Ensure(fixture.Cache, Hash(archive), NeverOpen) == target, "Original cache was not reused.");
            Assert(File.ReadAllText(Path.Combine(target, "HappyCheats.exe")) == "bundled HappyCheats.exe", "Original client was not restored.");
        });
        Run("wrong content hash cannot commit a runnable client", () =>
        {
            using var fixture = new Fixture();
            var archive = Zip(Release());
            var expected = new string(Hash(archive)[0] == 'a' ? 'b' : 'a', 64);
            ExpectFailure(() => BundleCache.Ensure(fixture.Cache, expected, () => new MemoryStream(archive, false)));
            Assert(!Directory.Exists(Path.Combine(fixture.Cache, expected, "HappyCheats")), "Corrupt content was committed.");
        });
        foreach (var invalidHash in new[] { "", "../escape", new string('a', 63), new string('g', 64) })
            Run("rejects invalid hash " + (invalidHash.Length == 0 ? "(empty)" : invalidHash), () =>
            {
                using var fixture = new Fixture();
                ExpectFailure(() => BundleCache.Ensure(fixture.Cache, invalidHash, NeverOpen));
                Assert(!Directory.Exists(fixture.Cache) || !Directory.EnumerateFileSystemEntries(fixture.Cache).Any(), "Invalid hash wrote cache data.");
            });
        foreach (var name in new[]
        {
            "HappyCheats/../escaped.txt", "../escaped.txt", "/HappyCheats/absolute.txt",
            "C:/HappyCheats/absolute.txt", "HappyCheats\\payload\\backslash.txt",
            "HappyCheats/payload/file.txt:stream", "HappyCheats/payload/CON",
            "HappyCheats/payload/aux.txt", "HappyCheats/payload/COM1.dll",
            "HappyCheats/payload/trailing.", "HappyCheats/payload/trailing ",
            "HappyCheats/payload//empty-segment.txt", "Elsewhere/payload.txt"
        })
            Run("unsafe ZIP entry rejected: " + name, () => AssertInvalidArchive(Release().Append(new Entry(name, Bytes("unsafe")))));
        Run("duplicate ZIP files cannot replace earlier extracted bytes", () =>
            AssertInvalidArchive(Release().Append(new Entry("HappyCheats/HappyCheats.exe", Bytes("replacement")))));
        Run("ZIP names collide case-insensitively on Windows", () =>
            AssertInvalidArchive(Release().Append(new Entry("happycheats/HAPPYCHEATS.EXE", Bytes("replacement")))));
        Run("ZIP file-directory conflict is rejected", () =>
            AssertInvalidArchive(Release().Append(new Entry("HappyCheats/payload", Bytes("file blocking directory")))));
        Run("ZIP symbolic links cannot redirect extraction", () =>
            AssertInvalidArchive(Release().Append(new Entry("HappyCheats/payload/link", Bytes("../../outside"), unchecked((int)0xA1FF0000)))));
        Run("ZIP reparse-point attributes are rejected", () =>
            AssertInvalidArchive(Release().Append(new Entry("HappyCheats/payload/link", Bytes("../../outside"), (int)FileAttributes.ReparsePoint))));
        foreach (var required in RequiredFiles)
        {
            Run("missing required file is rejected: " + required, () =>
                AssertInvalidArchive(Release().Where(entry => entry.Name != "HappyCheats/" + required)));
            Run("empty required file is rejected: " + required, () =>
                AssertInvalidArchive(Release().Select(entry => entry.Name == "HappyCheats/" + required ? entry with { Bytes = [] } : entry)));
        }
        Run("missing trainer payload is rejected", () =>
            AssertInvalidArchive(Release().Where(entry => !entry.Name.StartsWith("HappyCheats/payload/", StringComparison.Ordinal))));
        Run("empty trainer payload is rejected", () =>
            AssertInvalidArchive(Release().Select(entry => entry.Name.StartsWith("HappyCheats/payload/", StringComparison.Ordinal) ? entry with { Bytes = [] } : entry)));
        Run("truncated ZIP with correct hash leaves no committed runnable client", () =>
        {
            var archive = Zip(Release());
            foreach (var length in new[] { 4, archive.Length / 2, archive.Length - 22 })
            {
                using var fixture = new Fixture();
                var truncated = archive[..length];
                ExpectFailure(() => fixture.Ensure(truncated));
                Assert(!Directory.Exists(Path.Combine(fixture.Cache, Hash(truncated), "HappyCheats")), "Partial client was committed.");
            }
        });
        Run("concurrent starts share one committed extraction", () =>
        {
            using var fixture = new Fixture();
            var archive = Zip(Release());
            var opens = 0;
            using var start = new ManualResetEventSlim();
            var callers = Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
            {
                start.Wait();
                return BundleCache.Ensure(fixture.Cache, Hash(archive), () =>
                {
                    Interlocked.Increment(ref opens);
                    Thread.Sleep(40);
                    return new MemoryStream(archive, false);
                });
            })).ToArray();
            start.Set();
            var paths = Task.WhenAll(callers).GetAwaiter().GetResult();
            Assert(opens == 1, $"Archive opened {opens} times.");
            Assert(paths.Distinct().Count() == 1, "Concurrent callers received different clients.");
            Assert(File.Exists(Path.Combine(paths[0], "payload", "module", "trainer.dll")), "Committed trainer payload missing.");
        });
        Run("unrecognized pre-existing cache is preserved", () =>
        {
            using var fixture = new Fixture();
            var archive = Zip(Release());
            var target = Path.Combine(fixture.Cache, Hash(archive), "HappyCheats");
            Directory.CreateDirectory(target);
            var valuable = Path.Combine(target, "valuable.txt");
            File.WriteAllText(valuable, "keep me");
            ExpectFailure(() => fixture.Ensure(archive));
            Assert(File.ReadAllText(valuable) == "keep me", "Pre-existing content changed.");
            Assert(!File.Exists(Path.Combine(target, "HappyCheats.exe")), "Incomplete cache was silently replaced.");
        });
        Run("pre-existing hash file is preserved", () =>
        {
            using var fixture = new Fixture();
            var archive = Zip(Release());
            Directory.CreateDirectory(fixture.Cache);
            var path = Path.Combine(fixture.Cache, Hash(archive));
            File.WriteAllText(path, "keep me");
            ExpectFailure(() => fixture.Ensure(archive));
            Assert(File.ReadAllText(path) == "keep me", "Conflicting file changed.");
        });
        Run("broken completed cache fails without reverting an installed update", () =>
        {
            using var fixture = new Fixture();
            var archive = Zip(Release());
            var target = fixture.Ensure(archive);
            File.WriteAllText(Path.Combine(target, "HappyCheats.exe"), "updated client");
            File.Delete(Path.Combine(target, "HappyCheats.dll"));
            ExpectFailure(() => BundleCache.Ensure(fixture.Cache, Hash(archive), NeverOpen));
            Assert(File.ReadAllText(Path.Combine(target, "HappyCheats.exe")) == "updated client", "Bootstrap reverted damaged update.");
        });
        Run("linked cache root cannot write through to another directory", () =>
        {
            using var fixture = new Fixture();
            var destination = Path.Combine(fixture.Root, "outside-cache");
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "valuable.txt"), "keep me");
            CreateDirectoryLink(fixture.Cache, destination);
            try
            {
                ExpectFailure(() => fixture.Ensure(Zip(Release())));
                Assert(Directory.EnumerateFileSystemEntries(destination).Count() == 1, "Linked destination was changed.");
                Assert(File.ReadAllText(Path.Combine(destination, "valuable.txt")) == "keep me", "Linked destination lost data.");
            }
            finally { Directory.Delete(fixture.Cache); }
        });
        Run("linked files in an existing cache are preserved and rejected", () =>
        {
            using var fixture = new Fixture();
            var archive = Zip(Release());
            var target = fixture.Ensure(archive);
            var destination = Path.Combine(fixture.Root, "outside-cache");
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "valuable.txt"), "keep me");
            var link = Path.Combine(target, "payload", "linked");
            CreateDirectoryLink(link, destination);
            try
            {
                ExpectFailure(() => BundleCache.Ensure(fixture.Cache, Hash(archive), NeverOpen));
                Assert(File.ReadAllText(Path.Combine(destination, "valuable.txt")) == "keep me", "Linked destination changed.");
                Assert((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "Bootstrap removed pre-existing link.");
            }
            finally { Directory.Delete(link); }
        });
        Console.WriteLine($"Bootstrap tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void ExpectFailure(Action operation)
    {
        try { operation(); }
        catch (IOException) { return; }
        catch (InvalidDataException) { return; }
        catch (ArgumentException) { return; }
        throw new Exception("Expected invalid bundle/cache rejection.");
    }
    private static Stream NeverOpen() => throw new InvalidOperationException("Embedded archive must not reopen for an existing cache.");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static List<Entry> Release()
    {
        var files = RequiredFiles.Select(name => new Entry("HappyCheats/" + name, Bytes("bundled " + name))).ToList();
        files.Add(new Entry("HappyCheats/update-settings.json", Bytes("trusted settings")));
        files.Add(new Entry("HappyCheats/payload/module/trainer.dll", [0, 1, 2, 255, 128, 64]));
        return files;
    }
    private static byte[] Zip(IEnumerable<Entry> entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in entries)
            {
                var item = archive.CreateEntry(entry.Name, CompressionLevel.Fastest);
                if (entry.Attributes != 0) item.ExternalAttributes = entry.Attributes;
                using var content = item.Open();
                content.Write(entry.Bytes);
            }
        return memory.ToArray();
    }
    private static void AssertInvalidArchive(IEnumerable<Entry> entries)
    {
        using var fixture = new Fixture();
        var archive = Zip(entries);
        ExpectFailure(() => fixture.Ensure(archive));
        Assert(!Directory.Exists(Path.Combine(fixture.Cache, Hash(archive), "HappyCheats")), "Invalid archive committed a runnable client.");
        Assert(!File.Exists(Path.Combine(fixture.Root, "escaped.txt")), "ZIP escaped the cache.");
    }
    private static ApplyRequest PrepareUpdate(string target)
    {
        var work = Path.Combine(Directory.GetParent(target)!.FullName, ".happycheats-updates", Guid.NewGuid().ToString("N"));
        var package = Path.Combine(work, "HappyCheats");
        foreach (var entry in Release())
        {
            var file = Path.Combine(package, entry.Name["HappyCheats/".Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, entry.Bytes);
        }
        File.WriteAllText(Path.Combine(package, "HappyCheats.exe"), "updated HappyCheats.exe");
        File.WriteAllText(Path.Combine(package, "update-settings.json"), "untrusted downloaded settings");
        return new ApplyRequest(target, package, work, 0, 0);
    }
    private static void CreateDirectoryLink(string link, string destination)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, destination); return; }
        // Junctions do not require Windows developer mode or elevated symlink privileges.
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, destination }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Could not create junction fixture.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert(process.ExitCode == 0, "Could not create junction fixture: " + output + error);
    }
    private sealed record Entry(string Name, byte[] Bytes, int Attributes = 0);
    private sealed class SequentialStream(byte[] bytes, long failAfterBytes = long.MaxValue) : Stream
    {
        private readonly MemoryStream inner = new(bytes, writable: false);
        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (inner.Position >= failAfterBytes) throw new IOException("Simulated interrupted input.");
            return inner.Read(buffer, offset, (int)Math.Min(Math.Min(count, 128), failAfterBytes - inner.Position));
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { inner.Dispose(); Disposed = true; }
            base.Dispose(disposing);
        }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-bootstrap-tests-" + Guid.NewGuid().ToString("N"));
        public string Cache => Path.Combine(Root, "cache");
        public Fixture() => Directory.CreateDirectory(Root);
        public string Ensure(byte[] archive) => BundleCache.Ensure(Cache, Hash(archive), () => new MemoryStream(archive, writable: false));
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("HappyCheats-bootstrap-tests-", StringComparison.Ordinal))
                throw new IOException("Invalid fixture cleanup path.");
            Directory.Delete(resolved, recursive: true);
        }
    }
}
