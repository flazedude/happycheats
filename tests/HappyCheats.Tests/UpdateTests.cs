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
        RunFallbackTests(Run, privateKey, publicKey);
        Console.WriteLine($"Update tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void RunFallbackTests(Action<string, Action> run, string privateKey, string publicKey)
    {
        const string lanFeed = "http://192.168.2.76:17845/happycheats/latest.json";
        const string lanPackage = "http://192.168.2.76:17845/happycheats/HappyCheats-1.2.3-win-x64.zip";
        const string mirrorFeed = "https://github.com/example/releases/latest/download/latest.json";
        const string mirrorPackage = "https://github.com/example/releases/download/v1.2.3/HappyCheats-1.2.3-win-x64.zip";
        var config = Config(publicKey) with { FeedUrl = lanFeed, FallbackFeedUrl = mirrorFeed };
        var package = Package();
        var primary = Manifest(package) with { PackageUrl = lanPackage };
        var mirror = primary with { PackageUrl = mirrorPackage };
        byte[] Feed(ReleaseManifest release) => ReleaseSigning.SerializeEnvelope(ReleaseSigning.Sign(release, privateKey));
        HttpResponseMessage Reply(HttpRequestMessage request, HttpContent content, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { RequestMessage = request, Content = content };
        UpdateClient Client(HttpClient http) => new(http, config, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        void Save(Fixture fixture, string name, UpdateConfiguration value)
            => File.WriteAllText(Path.Combine(fixture.Root, name), JsonSerializer.Serialize(value, ReleaseSigning.JsonOptions));

        run("legacy settings serialization omits absent fallback and release schema is unchanged", () =>
        {
            Assert(!JsonSerializer.Serialize(Config(publicKey), ReleaseSigning.JsonOptions).Contains("fallbackFeedUrl"));
            using var json = JsonDocument.Parse(ReleaseSigning.SerializeManifest(primary));
            Assert(json.RootElement.EnumerateObject().Select(item => item.Name).Order().SequenceEqual(new[] {
                "schemaVersion", "version", "channel", "publishedAt", "packageUrl", "packageSize", "sha256", "changelogEnglish", "changelogChinese", "newGames"
            }.Order()));
        });
        run("bundled defaults cannot configure an unconfigured installation", () =>
        {
            using var fixture = new Fixture();
            Save(fixture, "update-defaults.json", config);
            Assert(UpdateConfiguration.Load(fixture.Root) == null);
        });
        run("matching defaults supplement legacy fallback without changing trusted file", () =>
        {
            using var fixture = new Fixture();
            var legacy = config with { FallbackFeedUrl = null };
            Save(fixture, "update-settings.json", legacy);
            var before = File.ReadAllBytes(Path.Combine(fixture.Root, "update-settings.json"));
            Save(fixture, "update-defaults.json", config with { PublicKeyPem = publicKey.Replace("\n", "\r\n") + "\n" });
            Assert(UpdateConfiguration.Load(fixture.Root) == config);
            Assert(before.SequenceEqual(File.ReadAllBytes(Path.Combine(fixture.Root, "update-settings.json"))));
        });
        run("defaults preserve explicit fallback and reject mismatched trust", () =>
        {
            using var fixture = new Fixture();
            var explicitConfig = config with { FallbackFeedUrl = "https://custom.example/latest.json" };
            Save(fixture, "update-settings.json", explicitConfig);
            Save(fixture, "update-defaults.json", config);
            Assert(UpdateConfiguration.Load(fixture.Root) == explicitConfig);
            Save(fixture, "update-settings.json", config with { FallbackFeedUrl = null });
            using var wrong = RSA.Create(2048);
            foreach (var defaults in new[] { config with { FeedUrl = "https://different.example/latest.json" },
                config with { Channel = "preview" }, config with { PublicKeyPem = wrong.ExportSubjectPublicKeyInfoPem() } })
            {
                Save(fixture, "update-defaults.json", defaults);
                Assert(UpdateConfiguration.Load(fixture.Root)?.FallbackFeedUrl == null);
            }
        });
        run("malformed optional defaults preserve valid trusted configuration", () =>
        {
            using var fixture = new Fixture();
            var legacy = config with { FallbackFeedUrl = null };
            Save(fixture, "update-settings.json", legacy);
            foreach (var invalid in new[] { "{", new string('x', 33 * 1024),
                JsonSerializer.Serialize(config with { FallbackFeedUrl = "http://untrusted.example/latest.json" }, ReleaseSigning.JsonOptions),
                JsonSerializer.Serialize(config with { PublicKeyPem = "invalid" }, ReleaseSigning.JsonOptions) })
            {
                File.WriteAllText(Path.Combine(fixture.Root, "update-defaults.json"), invalid);
                Assert(UpdateConfiguration.Load(fixture.Root) == legacy);
            }
            Save(fixture, "update-settings.json", config with { FallbackFeedUrl = "http://untrusted.example/latest.json" });
            Fails(() => UpdateConfiguration.Load(fixture.Root));
        });
        run("primary feed wins including equal or older releases", () =>
        {
            var requests = new List<string>();
            using var http = new HttpClient(new AsyncHandler((request, _) => {
                requests.Add(request.RequestUri!.AbsoluteUri);
                return Task.FromResult(Reply(request, new ByteArrayContent(Feed(primary))));
            }));
            var client = Client(http);
            Assert(client.CheckAsync("1.0.0").GetAwaiter().GetResult()?.PackageUrl == lanPackage);
            Assert(client.CheckAsync("1.2.3").GetAwaiter().GetResult() == null);
            Assert(client.CheckAsync("2.0.0").GetAwaiter().GetResult() == null);
            Assert(requests.SequenceEqual(new[] { lanFeed, lanFeed, lanFeed }));
        });
        run("unreachable HTTP and timed out primary feeds use verified fallback", () =>
        {
            foreach (var failure in new[] { "network", "http", "headers-timeout", "body-timeout" })
            {
                var requests = new List<string>();
                using var http = new HttpClient(new AsyncHandler(async (request, token) => {
                    var url = request.RequestUri!.AbsoluteUri; requests.Add(url);
                    if (url == mirrorFeed) return Reply(request, new ByteArrayContent(Feed(mirror)));
                    if (failure == "network") throw new HttpRequestException("fixture offline");
                    if (failure == "http") return Reply(request, new ByteArrayContent([]), HttpStatusCode.ServiceUnavailable);
                    if (failure == "headers-timeout") await Task.Delay(Timeout.Infinite, token);
                    return Reply(request, new StreamContent(new InterruptedStream(Feed(primary), stall: true)));
                }));
                Assert(Client(http).CheckAsync("1.0.0").GetAwaiter().GetResult()?.PackageUrl == mirrorPackage);
                Assert(requests.SequenceEqual(new[] { lanFeed, mirrorFeed }));
            }
        });
        run("caller cancellation never contacts fallback feed", () =>
        {
            using var cancel = new CancellationTokenSource();
            var requests = 0;
            using var http = new HttpClient(new AsyncHandler(async (request, token) => {
                requests++; cancel.Cancel(); await Task.Delay(Timeout.Infinite, token);
                return Reply(request, new ByteArrayContent([]));
            }));
            Throws<OperationCanceledException>(() => Client(http).CheckAsync("1.0.0", cancel.Token).GetAwaiter().GetResult());
            Assert(requests == 1);
        });
        run("secondary feed and archive body stalls are bounded without retries", () =>
        {
            foreach (var stage in new[] { "feed", "package" })
            {
                using var fixture = new Fixture();
                var requests = new List<string>();
                using var http = new HttpClient(new AsyncHandler((request, _) => {
                    var url = request.RequestUri!.AbsoluteUri; requests.Add(url);
                    if (url == lanFeed) throw new HttpRequestException("fixture offline");
                    HttpContent content = url == mirrorFeed && stage == "package" ? new ByteArrayContent(Feed(mirror))
                        : new StreamContent(new InterruptedStream(url == mirrorFeed ? Feed(mirror) : package, stall: true));
                    return Task.FromResult(Reply(request, content));
                }));
                var client = Client(http);
                if (stage == "feed")
                {
                    Throws<IOException>(() => client.CheckAsync("1.0.0").GetAwaiter().GetResult());
                    Assert(requests.SequenceEqual(new[] { lanFeed, mirrorFeed }));
                }
                else
                {
                    var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
                    Throws<IOException>(() => client.DownloadAndStageAsync(release, fixture.Staging).GetAwaiter().GetResult());
                    Assert(requests.SequenceEqual(new[] { lanFeed, mirrorFeed, mirrorPackage }));
                    Assert(!Directory.EnumerateFileSystemEntries(fixture.Staging).Any());
                }
            }
        });
        run("single-source feed body stall also has an inactivity deadline", () =>
        {
            var requests = 0;
            using var http = new HttpClient(new AsyncHandler((request, _) => {
                requests++;
                return Task.FromResult(Reply(request, new StreamContent(new InterruptedStream(Feed(primary), stall: true))));
            }));
            var client = new UpdateClient(http, config with { FallbackFeedUrl = null }, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
            Throws<IOException>(() => client.CheckAsync("1.0.0").GetAwaiter().GetResult());
            Assert(requests == 1);
        });
        run("both feed failures surface an error without looping", () =>
        {
            var requests = 0;
            using var http = new HttpClient(new AsyncHandler((_, _) => { requests++; throw new HttpRequestException("fixture offline"); }));
            Throws<IOException>(() => Client(http).CheckAsync("1.0.0").GetAwaiter().GetResult());
            Assert(requests == 2);
        });
        run("invalid signature metadata channel or redirect never triggers fallback", () =>
        {
            var signed = ReleaseSigning.Sign(primary, privateKey);
            var tampered = signed with { Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Convert.FromBase64String(signed.Payload)).Replace("1.2.3", "9.9.9"))) };
            foreach (var failure in new[] { "signature", "channel", "json", "redirect", "size" })
            {
                var requests = 0;
                using var http = new HttpClient(new AsyncHandler((request, _) => {
                    requests++;
                    var content = failure switch { "signature" => ReleaseSigning.SerializeEnvelope(tampered),
                        "channel" => Feed(primary with { Channel = "preview" }), "json" => Encoding.UTF8.GetBytes("{"),
                        "size" => new byte[300_000], _ => Feed(primary) };
                    var response = Reply(request, new ByteArrayContent(content));
                    if (failure == "redirect") response.RequestMessage = new(HttpMethod.Get, "http://public.example/latest.json");
                    return Task.FromResult(response);
                }));
                Fails(() => Client(http).CheckAsync("1.0.0").GetAwaiter().GetResult());
                Assert(requests == 1);
            }
        });
        run("fallback feed still requires the pinned signature", () =>
        {
            using var wrong = RSA.Create(2048);
            var requests = 0;
            using var http = new HttpClient(new AsyncHandler((request, _) => {
                requests++;
                if (requests == 1) throw new HttpRequestException("fixture offline");
                return Task.FromResult(Reply(request, new ByteArrayContent(ReleaseSigning.SerializeEnvelope(ReleaseSigning.Sign(mirror, wrong.ExportPkcs8PrivateKeyPem())))));
            }));
            Fails(() => Client(http).CheckAsync("1.0.0").GetAwaiter().GetResult());
            Assert(requests == 2);
        });
        run("interrupted or stalled primary package restarts from signed mirror", () =>
        {
            foreach (var stall in new[] { false, true })
            {
                using var fixture = new Fixture();
                var requests = new List<string>();
                var progress = new List<long>();
                using var http = new HttpClient(new AsyncHandler((request, _) => {
                    var url = request.RequestUri!.AbsoluteUri; requests.Add(url);
                    HttpContent content = url == lanFeed ? new ByteArrayContent(Feed(primary))
                        : url == mirrorFeed ? new ByteArrayContent(Feed(mirror))
                        : url == lanPackage ? new StreamContent(new InterruptedStream(package, stall)) : new ByteArrayContent(package);
                    return Task.FromResult(Reply(request, content));
                }));
                var client = Client(http);
                var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
                var staged = client.DownloadAndStageAsync(release, fixture.Staging, new DirectProgress(p => progress.Add(p.BytesReceived))).GetAwaiter().GetResult();
                Assert(File.Exists(Path.Combine(staged.PackageDirectory, "HappyCheats.exe")));
                Assert(requests.SequenceEqual(new[] { lanFeed, lanPackage, mirrorFeed, mirrorPackage }));
                Assert(progress.Count(value => value == 0) == 2 && progress.Contains(64) && progress[^1] == package.Length);
            }
        });
        run("progressing primary download is not limited by total timeout", () =>
        {
            using var fixture = new Fixture();
            var requests = 0;
            using var http = new HttpClient(new AsyncHandler((request, _) => {
                requests++;
                return Task.FromResult(Reply(request, request.RequestUri!.AbsoluteUri == lanFeed ? new ByteArrayContent(Feed(primary))
                    : new StreamContent(new PacedStream(package))));
            }));
            var client = Client(http);
            var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
            var staged = client.DownloadAndStageAsync(release, fixture.Staging).GetAwaiter().GetResult();
            Assert(File.Exists(Path.Combine(staged.PackageDirectory, "HappyCheats.exe")) && requests == 2);
        });
        run("canceled package does not contact mirror and cleans partial files", () =>
        {
            using var fixture = new Fixture();
            using var cancel = new CancellationTokenSource();
            var requests = 0;
            using var http = new HttpClient(new AsyncHandler((request, _) => {
                requests++; return Task.FromResult(Reply(request, new ByteArrayContent(request.RequestUri!.AbsoluteUri == lanFeed ? Feed(primary) : package)));
            }));
            var client = Client(http);
            var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
            Throws<OperationCanceledException>(() => client.DownloadAndStageAsync(release, fixture.Staging,
                new DirectProgress(p => { if (p.BytesReceived > 0) cancel.Cancel(); }), cancel.Token).GetAwaiter().GetResult());
            Assert(requests == 2 && !Directory.EnumerateFileSystemEntries(fixture.Staging).Any());
        });
        run("package integrity failures never contact the fallback", () =>
        {
            foreach (var failure in new[] { "hash", "length", "truncated", "oversized", "zip" })
            {
                using var fixture = new Fixture();
                var requests = 0;
                var bytes = failure == "zip" ? Package(omitUpdater: true) : package;
                var declared = failure == "zip" ? Manifest(bytes) with { PackageUrl = lanPackage } : primary;
                if (failure == "hash") declared = declared with { Sha256 = new string('0', 64) };
                if (failure is "length" or "truncated") declared = declared with { PackageSize = bytes.Length + 1 };
                if (failure == "oversized") declared = declared with { PackageSize = bytes.Length - 1 };
                using var http = new HttpClient(new AsyncHandler((request, _) => {
                    requests++;
                    HttpContent content = request.RequestUri!.AbsoluteUri == lanFeed ? new ByteArrayContent(Feed(declared))
                        : failure is "truncated" or "oversized" ? new StreamContent(new NonSeekStream(bytes)) : new ByteArrayContent(bytes);
                    return Task.FromResult(Reply(request, content));
                }));
                var client = Client(http);
                var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
                Fails(() => client.DownloadAndStageAsync(release, fixture.Staging).GetAwaiter().GetResult());
                Assert(requests == 2 && !Directory.EnumerateFileSystemEntries(fixture.Staging).Any());
            }
        });
        run("package mirror must verify and identify the exact accepted release", () =>
        {
            foreach (var failure in new[] { "version", "hash", "size", "channel", "signature", "same-url" })
            {
                using var fixture = new Fixture();
                var requests = new List<string>();
                var alternate = failure switch { "version" => mirror with { Version = "1.2.4" }, "hash" => mirror with { Sha256 = new string('0', 64) },
                    "size" => mirror with { PackageSize = package.Length + 1 }, "channel" => mirror with { Channel = "preview" },
                    "same-url" => mirror with { PackageUrl = lanPackage }, _ => mirror };
                var feed = Feed(alternate);
                if (failure == "signature")
                {
                    using var wrong = RSA.Create(2048);
                    feed = ReleaseSigning.SerializeEnvelope(SignRaw(alternate, wrong));
                }
                using var http = new HttpClient(new AsyncHandler((request, _) => {
                    var url = request.RequestUri!.AbsoluteUri; requests.Add(url);
                    if (url == lanPackage) throw new HttpRequestException("fixture offline");
                    return Task.FromResult(Reply(request, new ByteArrayContent(url == lanFeed ? Feed(primary) : feed)));
                }));
                var client = Client(http);
                var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
                Fails(() => client.DownloadAndStageAsync(release, fixture.Staging).GetAwaiter().GetResult());
                Assert(requests.SequenceEqual(new[] { lanFeed, lanPackage, mirrorFeed }));
                Assert(!Directory.EnumerateFileSystemEntries(fixture.Staging).Any());
            }
        });
        run("mirror package failure cleans staging without retry loop", () =>
        {
            using var fixture = new Fixture();
            var requests = new List<string>();
            using var http = new HttpClient(new AsyncHandler((request, _) => {
                var url = request.RequestUri!.AbsoluteUri; requests.Add(url);
                if (url == lanPackage || url == mirrorPackage) throw new HttpRequestException("fixture offline");
                return Task.FromResult(Reply(request, new ByteArrayContent(Feed(url == lanFeed ? primary : mirror))));
            }));
            var client = Client(http);
            var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
            Throws<IOException>(() => client.DownloadAndStageAsync(release, fixture.Staging).GetAwaiter().GetResult());
            Assert(requests.SequenceEqual(new[] { lanFeed, lanPackage, mirrorFeed, mirrorPackage }));
            Assert(!Directory.EnumerateFileSystemEntries(fixture.Staging).Any());
        });
        run("release discovered through fallback does not retry the same source", () =>
        {
            using var fixture = new Fixture();
            var requests = new List<string>();
            using var http = new HttpClient(new AsyncHandler((request, _) => {
                var url = request.RequestUri!.AbsoluteUri; requests.Add(url);
                if (url != mirrorFeed) throw new HttpRequestException("fixture offline");
                return Task.FromResult(Reply(request, new ByteArrayContent(Feed(mirror))));
            }));
            var client = Client(http);
            var release = client.CheckAsync("1.0.0").GetAwaiter().GetResult()!;
            Throws<IOException>(() => client.DownloadAndStageAsync(release, fixture.Staging).GetAwaiter().GetResult());
            Assert(requests.SequenceEqual(new[] { lanFeed, mirrorFeed, mirrorPackage }));
            Assert(!Directory.EnumerateFileSystemEntries(fixture.Staging).Any());
        });
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
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
    private sealed class InterruptedStream(byte[] bytes, bool stall) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position > 0)
            {
                if (stall) await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new IOException("fixture connection interrupted");
            }
            return await base.ReadAsync(buffer[..Math.Min(64, buffer.Length)], cancellationToken);
        }
    }
    private sealed class PacedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(30, cancellationToken);
            return await base.ReadAsync(buffer[..Math.Min(128, buffer.Length)], cancellationToken);
        }
    }
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
