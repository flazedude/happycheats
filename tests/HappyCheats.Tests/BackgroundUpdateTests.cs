using System.IO.Compression;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using HappyCheats.Core.Updates;

namespace HappyCheats.Tests;

internal static class BackgroundUpdateTests
{
    public static int RunAll()
    {
        var passed = 0;
        var failed = 0;
        void Run(string name, Func<Task> test)
        {
            try { test().GetAwaiter().GetResult(); passed++; Console.WriteLine("PASS background update: " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL background update: " + name + ": " + error); }
        }

        Run("automatically verifies and stages without a progress window", async () =>
        {
            using var f = new Fixture();
            using var session = f.Session();
            var ready = 0;
            session.ReadyToInstall += (_, _) => ready++;
            await session.CheckAndDownloadAsync();
            Assert(session.State == BackgroundUpdateState.Staged && session.Staged != null && session.Error == null);
            Assert(File.Exists(Path.Combine(session.Staged!.PackageDirectory, "HappyCheats.exe")));
            Assert(ready == 1 && f.FeedRequests == 1 && f.PackageRequests == 1);
            Assert(session.BytesReceived == f.Package.Length && session.TotalBytes == f.Package.Length && !session.IsWorking);
            var staged = session.Staged;
            await session.CheckAndDownloadAsync();
            Assert(ReferenceEquals(staged, session.Staged) && ready == 1 && f.PackageRequests == 1 && f.FeedRequests == 1);
        });

        Run("concurrent requests share one check and download", async () =>
        {
            using var f = new Fixture();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.BeforePackage = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
            using var session = f.Session();
            var first = session.CheckAndDownloadAsync();
            await entered.Task;
            var second = session.CheckAndDownloadAsync();
            Assert(ReferenceEquals(first, second) && session.State == BackgroundUpdateState.Downloading);
            release.SetResult();
            await Task.WhenAll(first, second);
            Assert(f.FeedRequests == 1 && f.PackageRequests == 1 && session.Staged != null);
        });

        Run("notifications and the restart prompt use the captured UI context", () =>
        {
            var previous = SynchronizationContext.Current;
            using var context = new PumpContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                using var f = new Fixture();
                using var session = f.Session();
                var uiThread = Environment.CurrentManagedThreadId;
                var notifications = new List<int>();
                var promptThread = 0;
                session.PropertyChanged += (_, _) => notifications.Add(Environment.CurrentManagedThreadId);
                session.ReadyToInstall += (_, _) => promptThread = Environment.CurrentManagedThreadId;
                var operation = Task.Run(() => session.CheckAndDownloadAsync());
                context.UntilComplete(operation);
                Assert(notifications.Count > 0 && notifications.All(thread => thread == uiThread) && promptThread == uiThread);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            return Task.CompletedTask;
        });

        Run("the latest version is never downloaded", async () =>
        {
            using var f = new Fixture();
            using var session = f.Session("1.2.3");
            var ready = 0;
            session.ReadyToInstall += (_, _) => ready++;
            await session.CheckAndDownloadAsync();
            Assert(session.State == BackgroundUpdateState.Latest && session.Release == null && f.PackageRequests == 0 && ready == 0);
        });

        Run("corrupt packages fail quietly without a restart prompt", async () =>
        {
            using var f = new Fixture();
            f.CorruptPackage = true;
            using var session = f.Session();
            var ready = 0;
            session.ReadyToInstall += (_, _) => ready++;
            await session.CheckAndDownloadAsync();
            Assert(session.State == BackgroundUpdateState.Failed && session.Error is InvalidDataException && session.Staged == null && ready == 0);
            Assert(f.StagingEmpty);
        });

        Run("failed downloads can reverify and retry", async () =>
        {
            using var f = new Fixture();
            f.BeforePackage = _ => throw new HttpRequestException("fixture offline");
            using var session = f.Session();
            var ready = 0;
            session.ReadyToInstall += (_, _) => ready++;
            await session.CheckAndDownloadAsync();
            Assert(session.State == BackgroundUpdateState.Failed && session.Error != null && ready == 0 && f.StagingEmpty);
            f.BeforePackage = null;
            await session.CheckAndDownloadAsync();
            Assert(session.State == BackgroundUpdateState.Staged && session.Error == null && ready == 1 && f.FeedRequests == 2 && f.PackageRequests == 2);
        });

        Run("closing the client cancels download and removes only its staging", async () =>
        {
            using var f = new Fixture();
            Directory.CreateDirectory(f.Staging);
            var retained = Path.Combine(f.Staging, "retained-other-update");
            Directory.CreateDirectory(retained);
            File.WriteAllText(Path.Combine(retained, "keep.txt"), "preserve");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.BeforePackage = async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); };
            using var session = f.Session();
            var ready = 0;
            session.ReadyToInstall += (_, _) => ready++;
            var operation = session.CheckAndDownloadAsync();
            await entered.Task;
            session.Dispose();
            await operation;
            Assert(session.Staged == null && ready == 0 && Directory.GetDirectories(f.Staging).SequenceEqual(new[] { retained }));
            Assert(File.ReadAllText(Path.Combine(retained, "keep.txt")) == "preserve");
            await session.CheckAndDownloadAsync();
            Assert(f.PackageRequests == 1);
        });

        Run("installation failure retains verified package until explicit discard", async () =>
        {
            using var f = new Fixture();
            using var session = f.Session();
            Throws<InvalidOperationException>(() => session.BeginInstall());
            await session.CheckAndDownloadAsync();
            var stage = session.BeginInstall();
            Assert(session.State == BackgroundUpdateState.Installing && session.IsWorking);
            Throws<InvalidOperationException>(() => session.BeginInstall());
            session.InstallFailed(new IOException("Close supported games first."), discardStaged: false);
            Assert(session.State == BackgroundUpdateState.Failed && ReferenceEquals(stage, session.Staged) && Directory.Exists(stage.WorkDirectory));
            await session.CheckAndDownloadAsync();
            Assert(f.PackageRequests == 1);
            Assert(ReferenceEquals(stage, session.BeginInstall()));
            session.InstallFailed(new IOException("Updater could not start."), discardStaged: true);
            Assert(session.Staged == null && !Directory.Exists(stage.WorkDirectory));
            await session.CheckAndDownloadAsync();
            Assert(session.State == BackgroundUpdateState.Staged && f.PackageRequests == 2);
        });

        Run("shutdown removes staged data unless updater handoff owns it", async () =>
        {
            using var f = new Fixture();
            using (var session = f.Session())
            {
                await session.CheckAndDownloadAsync();
                var work = session.Staged!.WorkDirectory;
                session.Dispose();
                Assert(!Directory.Exists(work));
            }
            using (var session = f.Session())
            {
                await session.CheckAndDownloadAsync();
                Throws<InvalidOperationException>(() => session.PreserveForHandoff());
                var stage = session.BeginInstall();
                session.PreserveForHandoff();
                session.Dispose();
                Assert(Directory.Exists(stage.WorkDirectory));
            }
        });

        Console.WriteLine($"Background update tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

    private sealed class Fixture : IDisposable
    {
        private readonly RSA key = RSA.Create(2048);
        private readonly HttpClient http;
        private readonly byte[] feed;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-background-tests-" + Guid.NewGuid().ToString("N"));
        public string Staging => Path.Combine(Root, "staging");
        public byte[] Package { get; } = CreatePackage();
        public bool CorruptPackage;
        public int FeedRequests, PackageRequests;
        public Func<CancellationToken, Task>? BeforePackage;
        public bool StagingEmpty => !Directory.Exists(Staging) || !Directory.EnumerateFileSystemEntries(Staging).Any();
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            var manifest = new ReleaseManifest
            {
                Version = "1.2.3", PublishedAt = DateTimeOffset.UtcNow, PackageUrl = "https://example.com/release.zip",
                PackageSize = Package.Length, Sha256 = Convert.ToHexString(SHA256.HashData(Package)),
                ChangelogEnglish = "Fixture update", ChangelogChinese = "测试更新"
            };
            feed = ReleaseSigning.SerializeEnvelope(ReleaseSigning.Sign(manifest, key.ExportPkcs8PrivateKeyPem()));
            http = new(new Handler(async (request, token) =>
            {
                byte[] body;
                if (request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal)) { FeedRequests++; body = feed; }
                else
                {
                    PackageRequests++;
                    if (BeforePackage != null) await BeforePackage(token);
                    body = Package.ToArray();
                    if (CorruptPackage) body[0] ^= 1;
                }
                return new(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(body) };
            }));
        }
        public BackgroundUpdateSession Session(string current = "1.0.0") => new(new UpdateClient(http,
            new UpdateConfiguration { FeedUrl = "https://example.com/latest.json", PublicKeyPem = key.ExportSubjectPublicKeyInfoPem() }), current, Staging);
        public void Dispose()
        {
            http.Dispose(); key.Dispose();
            var path = Path.GetFullPath(Root);
            if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(path).StartsWith("HappyCheats-background-tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe test cleanup path.");
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }

    private sealed class PumpContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => queue.Add((callback, state));
        public void UntilComplete(Task operation)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!operation.IsCompleted)
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Fixture UI dispatch did not complete.");
                if (queue.TryTake(out var work, 50)) work.Callback(work.State);
            }
            operation.GetAwaiter().GetResult();
        }
        public void Dispose() => queue.Dispose();
    }

    private static byte[] CreatePackage()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "HappyCheats.exe", "HappyCheats.dll", "HappyCheats.runtimeconfig.json", "payload/test/package.json",
                "updater/HappyCheats.Updater.exe", "updater/HappyCheats.Updater.dll", "updater/HappyCheats.Updater.runtimeconfig.json" })
            {
                using var entry = archive.CreateEntry("HappyCheats/" + name).Open();
                entry.Write(Encoding.UTF8.GetBytes("fixture " + name));
            }
        }
        return output.ToArray();
    }
}
