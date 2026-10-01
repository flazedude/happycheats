using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HappyCheats.Core.Updates;

namespace HappyCheats.Tests;

internal static class UpdateTests
{
    public static int RunAll()
    {
        var passed = 0;
        var failed = 0;
        using var key = RSA.Create(2048);
        var privateKey = key.ExportPkcs8PrivateKeyPem();
        var publicKey = key.ExportSubjectPublicKeyInfoPem();
        void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS update: " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL update: " + name + ": " + ex); }
        }

        Run("strict release versions compare numerically", () =>
        {
            Assert(ReleaseVersion.Parse("1.10.0").CompareTo(ReleaseVersion.Parse("1.9.9")) > 0);
            foreach (var value in new[] { "", "1", "1.2", "1.2.3.4", "v1.2.3", "1.2.3-beta", "01.2.3", "1.-2.3", "1.2.3 ", "1.2.2147483648" })
                Assert(!ReleaseVersion.TryParse(value, out _));
        });
        Run("trusted signature verifies bilingual changelog", () =>
        {
            var manifest = Manifest(Package());
            var verified = ReleaseSigning.Verify(ReleaseSigning.Sign(manifest, privateKey), publicKey);
            Assert(verified.ChangelogChinese == "新增游戏和修复" && verified.NewGames.SequenceEqual(new[] { "Test Game" }));
        });
        Run("tampered payload is rejected", () =>
        {
            var signed = ReleaseSigning.Sign(Manifest(Package()), privateKey);
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(signed.Payload)).Replace("1.2.3", "9.9.9");
            Fails(() => ReleaseSigning.Verify(signed with { Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)) }, publicKey));
        });
        Run("wrong key and malformed signature are rejected", () =>
        {
            var signed = ReleaseSigning.Sign(Manifest(Package()), privateKey);
            using var wrong = RSA.Create(2048);
            Fails(() => ReleaseSigning.Verify(signed, wrong.ExportSubjectPublicKeyInfoPem()));
            Fails(() => ReleaseSigning.Verify(signed with { Signature = "not base64!" }, publicKey));
        });
        Run("small RSA keys are rejected", () =>
        {
            using var small = RSA.Create(1024);
            Fails(() => ReleaseSigning.Sign(Manifest(Package()), small.ExportPkcs8PrivateKeyPem()));
        });
        Run("signed malformed metadata is rejected", () =>
        {
            var manifest = Manifest(Package());
            foreach (var invalid in new[]
            {
                manifest with { Version = "1.2" }, manifest with { SchemaVersion = 2 },
                manifest with { PackageUrl = "http://example.com/release.zip" }, manifest with { PackageSize = 0 },
                manifest with { Sha256 = "broken" }, manifest with { NewGames = null! }, manifest with { PublishedAt = default }
            }) Fails(() => ReleaseSigning.Verify(SignRaw(invalid, key), publicKey));
        });
        Run("duplicate signed JSON properties are rejected", () =>
        {
            var payload = JsonSerializer.Serialize(Manifest(Package()), ReleaseSigning.JsonOptions).Replace("{", "{\"version\":\"8.8.8\",", StringComparison.Ordinal);
            Fails(() => ReleaseSigning.Verify(SignBytes(Encoding.UTF8.GetBytes(payload), key), publicKey));
        });
        Run("missing local configuration is disabled", () =>
        {
            using var fixture = new Fixture();
            Assert(UpdateConfiguration.Load(fixture.Root) is null);
        });
        Run("configuration permits HTTPS and loopback", () =>
        {
            using var fixture = new Fixture();
            foreach (var url in new[] { "http://example.com/latest.json", "file:///tmp/latest.json", "https://user:secret@example.com/latest.json" })
            {
                File.WriteAllText(Path.Combine(fixture.Root, "update-settings.json"), JsonSerializer.Serialize(Config(publicKey) with { FeedUrl = url }, ReleaseSigning.JsonOptions));
                Fails(() => UpdateConfiguration.Load(fixture.Root));
            }
            File.WriteAllText(Path.Combine(fixture.Root, "update-settings.json"), JsonSerializer.Serialize(Config(publicKey) with { FeedUrl = "http://127.0.0.1:8000/latest.json" }, ReleaseSigning.JsonOptions));
            Assert(UpdateConfiguration.Load(fixture.Root) is not null);
        });
        Run("LAN HTTP addresses have strict private network boundaries", () =>
        {
            foreach (var host in new[] { "10.0.0.1", "172.16.0.1", "172.31.255.254", "192.168.2.76", "127.0.0.1", "localhost" })
                Assert(UpdateAddress.Validate($"http://{host}:17845/happycheats/latest.json").Scheme == "http");
            foreach (var host in new[] { "8.8.8.8", "172.15.1.1", "172.32.1.1", "192.169.1.1", "169.254.1.1", "100.64.0.1", "0.0.0.0", "255.255.255.255", "server.local", "example.com" })
                Fails(() => UpdateAddress.Validate($"http://{host}/latest.json"));
            Fails(() => UpdateAddress.Validate("http://user:secret@192.168.2.76/latest.json"));
            Fails(() => UpdateAddress.Validate("http://192.168.2.76/latest.json#fragment"));
        });
        Run("LAN feeds retain signature and package verification", () =>
        {
            using var fixture = new Fixture();
            var package = Package();
            var manifest = Manifest(package) with { PackageUrl = "http://192.168.2.76:17845/happycheats/HappyCheats-1.2.3-win-x64.zip" };
            using var http = Http(ReleaseSigning.Sign(manifest, privateKey), package);
            var client = new UpdateClient(http, Config(publicKey) with { FeedUrl = "http://192.168.2.76:17845/happycheats/latest.json" });
            var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
            var staged = client.DownloadAndStageAsync(release, fixture.Staging).GetAwaiter().GetResult();
            Assert(File.Exists(Path.Combine(staged.PackageDirectory, "HappyCheats.exe")));
            using var wrongKey = RSA.Create(2048);
            Fails(() => new UpdateClient(http, Config(wrongKey.ExportSubjectPublicKeyInfoPem()) with { FeedUrl = "http://192.168.2.76:17845/happycheats/latest.json" }).CheckAsync("1.0.0").GetAwaiter().GetResult());
        });
        Run("check rejects channel mismatch", () =>
        {
            using var http = Http(ReleaseSigning.Sign(Manifest(Package()) with { Channel = "preview" }, privateKey), Package());
            Fails(() => new UpdateClient(http, Config(publicKey)).CheckAsync("1.0.0").GetAwaiter().GetResult());
        });
        Run("check ignores equal and older releases", () =>
        {
            using var http = Http(ReleaseSigning.Sign(Manifest(Package()), privateKey), Package());
            var client = new UpdateClient(http, Config(publicKey));
            Assert(client.CheckAsync("1.2.3").GetAwaiter().GetResult() is null);
            Assert(client.CheckAsync("2.0.0").GetAwaiter().GetResult() is null);
        });
        Run("oversized feed is rejected without content length", () =>
        {
            using var http = new HttpClient(new Handler(_ => new StreamContent(new NonSeekStream(new byte[300_000]))));
            Fails(() => new UpdateClient(http, Config(publicKey)).CheckAsync("1.0.0").GetAwaiter().GetResult());
        });
        Run("unsafe redirected feed is rejected", () =>
        {
            using var http = new HttpClient(new Handler(_ => new ByteArrayContent(ReleaseSigning.SerializeEnvelope(ReleaseSigning.Sign(Manifest(Package()), privateKey))), "http://example.com/latest.json"));
            Fails(() => new UpdateClient(http, Config(publicKey)).CheckAsync("1.0.0").GetAwaiter().GetResult());
        });
        Run("complete signed release stages required files and reports progress", () =>
        {
            using var fixture = new Fixture();
            var package = Package();
            using var http = Http(ReleaseSigning.Sign(Manifest(package), privateKey), package);
            var client = new UpdateClient(http, Config(publicKey));
            var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
            UpdateProgress? last = null;
            var stage = client.DownloadAndStageAsync(release, fixture.Staging, new DirectProgress(p => last = p)).GetAwaiter().GetResult();
            Assert(File.Exists(Path.Combine(stage.PackageDirectory, "HappyCheats.exe")));
            Assert(File.Exists(Path.Combine(stage.PackageDirectory, "payload", "test", "package.json")));
            Assert(Guid.TryParseExact(Path.GetFileName(stage.WorkDirectory), "N", out _));
            Assert(last is { } && last.BytesReceived == package.Length && last.TotalBytes == package.Length);
        });
        Run("unverified or altered manifests cannot download", () =>
        {
            using var fixture = new Fixture();
            var package = Package();
            using var http = Http(ReleaseSigning.Sign(Manifest(package), privateKey), package);
            var client = new UpdateClient(http, Config(publicKey));
            Fails(() => client.DownloadAndStageAsync(Manifest(package), fixture.Staging).GetAwaiter().GetResult());
            var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
            Fails(() => client.DownloadAndStageAsync(release with { PackageUrl = "https://example.com/changed.zip" }, fixture.Staging).GetAwaiter().GetResult());
        });
        Run("hash mismatch removes partial staging", () =>
        {
            var package = Package();
            StageFails(package, Manifest(package) with { Sha256 = new string('0', 64) }, privateKey, publicKey);
        });
        Run("declared download length mismatch removes staging", () =>
        {
            var package = Package();
            StageFails(package, Manifest(package) with { PackageSize = package.Length + 1 }, privateKey, publicKey);
        });
        Run("streamed download exceeds signed size is rejected", () =>
        {
            var package = Package();
            StageFails(package, Manifest(package) with { PackageSize = package.Length - 1 }, privateKey, publicKey, noLength: true);
        });
        Run("truncated streamed download is rejected", () =>
        {
            var package = Package();
            StageFails(package, Manifest(package) with { PackageSize = package.Length + 1 }, privateKey, publicKey, noLength: true);
        });
        Run("cancellation removes partial download", () =>
        {
            using var fixture = new Fixture();
            var package = Package();
            using var http = Http(ReleaseSigning.Sign(Manifest(package), privateKey), package);
            var client = new UpdateClient(http, Config(publicKey));
            var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
            using var cancel = new CancellationTokenSource();
            try
            {
                client.DownloadAndStageAsync(release, fixture.Staging, new DirectProgress(p => { if (p.BytesReceived > 0) cancel.Cancel(); }), cancel.Token).GetAwaiter().GetResult();
                throw new Exception("Expected cancellation.");
            }
            catch (OperationCanceledException) { }
            Assert(!Directory.EnumerateFileSystemEntries(fixture.Staging).Any());
        });
        Run("unsafe ZIP paths are rejected and cleaned", () =>
        {
            foreach (var path in new[] { "HappyCheats/../../outside.txt", "/HappyCheats/absolute.txt", "other/file.txt", "HappyCheats/folder\\escape.txt", "HappyCheats/data.txt:stream", "HappyCheats/NUL.txt", "HappyCheats/folder./file", "HappyCheats//file", "HappyCheats/COM¹.txt" })
            {
                var package = Package(path);
                StageFails(package, Manifest(package), privateKey, publicKey);
            }
        });
        Run("case-insensitive duplicate ZIP paths are rejected", () =>
        {
            var package = Package("HappyCheats/HAPPYCHEATS.EXE");
            StageFails(package, Manifest(package), privateKey, publicKey);
        });
        Run("ZIP symlinks are rejected", () =>
        {
            var package = Package("HappyCheats/link", 0xA1FF0000);
            StageFails(package, Manifest(package), privateKey, publicKey);
        });
        Run("file and directory conflicts are rejected", () =>
        {
            var package = Package("HappyCheats/HappyCheats.exe/nested");
            StageFails(package, Manifest(package), privateKey, publicKey);
        });
        Run("incomplete packages are rejected", () =>
        {
            var package = Package(omitUpdater: true);
            StageFails(package, Manifest(package), privateKey, publicKey);
        });
        Run("compression bombs are rejected before extraction", () =>
        {
            var package = Package("HappyCheats/giant.bin", additionalContents: new byte[12 * 1024 * 1024]);
            StageFails(package, Manifest(package), privateKey, publicKey);
        });
        Console.WriteLine($"Update tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static UpdateConfiguration Config(string publicKey) => new() { FeedUrl = "https://example.com/latest.json", PublicKeyPem = publicKey };
    private static ReleaseManifest Manifest(byte[] package) => new()
    {
        Version = "1.2.3", PublishedAt = DateTimeOffset.UtcNow, PackageUrl = "https://example.com/release.zip",
        PackageSize = package.Length, Sha256 = Convert.ToHexString(SHA256.HashData(package)),
        ChangelogEnglish = "New games and fixes", ChangelogChinese = "新增游戏和修复", NewGames = new[] { "Test Game" }
    };

    private static SignedReleaseEnvelope SignRaw(ReleaseManifest manifest, RSA key)
        => SignBytes(JsonSerializer.SerializeToUtf8Bytes(manifest, ReleaseSigning.JsonOptions), key);
    private static SignedReleaseEnvelope SignBytes(byte[] payload, RSA key) => new()
    {
        Payload = Convert.ToBase64String(payload),
        Signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
    };

    private static HttpClient Http(SignedReleaseEnvelope envelope, byte[] package, bool noLength = false)
        => new(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal)
            ? new ByteArrayContent(ReleaseSigning.SerializeEnvelope(envelope))
            : noLength ? new StreamContent(new NonSeekStream(package)) : new ByteArrayContent(package)));

    private static void StageFails(byte[] package, ReleaseManifest manifest, string privateKey, string publicKey, bool noLength = false)
    {
        using var fixture = new Fixture();
        using var http = Http(ReleaseSigning.Sign(manifest, privateKey), package, noLength);
        var client = new UpdateClient(http, Config(publicKey));
        var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
        Fails(() => client.DownloadAndStageAsync(release, fixture.Staging).GetAwaiter().GetResult());
        Assert(!Directory.Exists(fixture.Staging) || !Directory.EnumerateFileSystemEntries(fixture.Staging).Any());
        Assert(!File.Exists(Path.Combine(fixture.Root, "outside.txt")));
    }

    private static byte[] Package(string? additionalPath = null, uint attributes = 0, bool omitUpdater = false, byte[]? additionalContents = null)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var path in new[] { "HappyCheats.exe", "HappyCheats.dll", "HappyCheats.runtimeconfig.json", "payload/test/package.json", "update-settings.json" }) Add("HappyCheats/" + path, Encoding.UTF8.GetBytes("fixture " + path));
            if (!omitUpdater)
                foreach (var path in new[] { "HappyCheats.Updater.exe", "HappyCheats.Updater.dll", "HappyCheats.Updater.runtimeconfig.json" }) Add("HappyCheats/updater/" + path, Encoding.UTF8.GetBytes("fixture " + path));
            if (additionalPath is not null)
            {
                var entry = Add(additionalPath, additionalContents ?? Encoding.UTF8.GetBytes("extra"));
                if (attributes != 0) entry.ExternalAttributes = unchecked((int)attributes);
            }
            ZipArchiveEntry Add(string name, byte[] content)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.SmallestSize);
                using var stream = entry.Open();
                stream.Write(content);
                return entry;
            }
        }
        return output.ToArray();
    }

    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException or CryptographicException) { return; } throw new Exception("Expected update rejection."); }
    private sealed class DirectProgress(Action<UpdateProgress> callback) : IProgress<UpdateProgress>
    { public void Report(UpdateProgress value) => callback(value); }
    private sealed class Handler(Func<HttpRequestMessage, HttpContent> content, string? redirect = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = redirect is null ? request : new HttpRequestMessage(HttpMethod.Get, redirect), Content = content(request)
            });
    }
    private sealed class NonSeekStream(byte[] bytes) : MemoryStream(bytes)
    { public override bool CanSeek => false; }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-update-tests-" + Guid.NewGuid().ToString("N"));
        public string Staging => Path.Combine(Root, "staging");
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("HappyCheats-update-tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe test cleanup path.");
            Directory.Delete(resolved, recursive: true);
        }
    }
}
