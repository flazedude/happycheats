using System.Net;
using HappyCheats.Core;
using HappyCheats.Core.Cheats;
using HappyCheats.Core.Updates;
using HappyCheats.TestSupport;

namespace HappyCheats.Tests;
internal static class CheatUpdateTests
{
    public static int RunAll()
    {
        var failures = 0; var passed = 0;
        void Run(string name, Func<Task> test)
        {
            try { test().GetAwaiter().GetResult(); passed++; Console.WriteLine("PASS cheat updates: " + name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL cheat updates: " + name + " · " + error); }
        }
        Run("new game loads outside built-in catalog and dispatches shared interfaces", async () =>
        {
            using var f = new CheatFixture(); Assert(!ModuleCatalog.Create().Any(x => x.Definition.Id == "future-game"));
            await f.Install(); var module = f.Store.Load([], new Dictionary<string, string>(), "0.3.65", e => throw new Exception(e)).Single();
            Assert(module.GameExecutables.Single() == "FutureGame.exe" && module.Definition.Features.Count == 3);
            var installation = await module.DiscoverAsync(); var status = await module.ReadStatusAsync(installation);
            await module.SendAsync(installation, status, "future_health", true);
            await ((IFeatureValueModule)module).SetValueAsync(installation, status, "future_amount", 42);
            await ((IFeatureChoiceModule)module).SelectAsync(installation, status, "future_speed", "2");
            var after = await module.ReadStatusAsync(installation);
            Assert(after.Enabled["future_health"] && after.Values["future_amount"] == "42" && after.Values["speed"] == "2");
            Assert(System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(module.GetType().Assembly) != System.Runtime.Loader.AssemblyLoadContext.Default);
        });
        Run("newer bundled game supersedes older downloaded version", async () =>
        {
            using var f = new CheatFixture(); await f.Install();
            Assert(f.Store.Load([], new Dictionary<string,string> { ["future-game"] = "2.0.0" }, "0.3.65", _ => { }).Count == 0);
            Assert(f.Store.InstalledVersions(new Dictionary<string,string> { ["future-game"] = "2.0.0" })["future-game"] == "2.0.0");
        });
        Run("tampered feed and wrong channel cannot activate", async () =>
        {
            using var f = new CheatFixture(); var bad = f.Proof(); bad[bad.Length / 2] ^= 1;
            await Fails(() => f.Install(proof: bad));
            await Fails(() => Task.Run(() => CheatSigning.Verify(f.Proof(), f.Configuration.PublicKeyPem, "beta")));
            Assert(!File.Exists(Path.Combine(f.Store.Root,"active","future-game.json")));
        });
        Run("unsupported client/API cannot mutate active receipt", async () =>
        {
            using var f = new CheatFixture(); await Fails(() => f.Install(f.Release with { MinimumClientVersion = "9.0.0" }));
            await Fails(() => f.Install(f.Release with { ContractVersion = 2 }));
            Assert(!Directory.Exists(Path.Combine(f.Store.Root, "active")));
        });
        Run("cancellation and bad archive preserve active package", async () =>
        {
            using var f = new CheatFixture(); await f.Install(); var path=Path.Combine(f.Store.Root,"active","future-game.json"); var active=File.ReadAllBytes(path);
            using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Fails(() => f.Install(token: canceled.Token));
            var next = f.Release with { Version = "1.0.1", PackageUrl = f.Release.PackageUrl.Replace("1.0.0", "1.0.1"), Sha256 = new('b',64) };
            await Fails(() => f.Install(next)); Assert(File.ReadAllBytes(path).SequenceEqual(active));
        });
        Run("downgrade and same-version substitution rejected", async () =>
        {
            using var f = new CheatFixture(); await f.Install(); await Fails(() => f.Install(f.Release with { Sha256 = new('c',64) }));
            await Fails(() => f.Install(f.Release with { Version = "0.9.0", PackageUrl = f.Release.PackageUrl.Replace("1.0.0", "0.9.0") }));
        });
        Run("corrupt installed payload blocked and explicitly repaired", async () =>
        {
            using var f = new CheatFixture(); await f.Install();
            var payload = Directory.GetFiles(f.Store.Root,"fixture.txt",SearchOption.AllDirectories).Single(); File.WriteAllText(payload,"corrupt");
            var errors = new List<string>(); var blocked = f.Store.Load([],new Dictionary<string,string>(),"0.3.65",errors.Add).Single();
            Assert(errors.Count == 1); await Fails(() => blocked.LaunchAsync(new("", "", false, false, false, false, "")));
            await f.Install(); Assert(f.Store.Load([],new Dictionary<string,string>(),"0.3.65", e => throw new Exception(e)).Single().Definition.Id == "future-game");
            Assert(Directory.Exists(Path.Combine(f.Store.Root,"recovery")));
        });
        Run("concurrent mutation reports busy", async () =>
        {
            using var f = new CheatFixture(); Directory.CreateDirectory(f.Store.Root);
            using var gate = new FileStream(Path.Combine(f.Store.Root,"install.lock"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None);
            await Fails(() => f.Install()); Assert(!Directory.Exists(Path.Combine(f.Store.Root,"active")));
        });
        Run("stale LAN cannot hide GitHub; only matching package fallback", async () =>
        {
            using var f = new CheatFixture(); var current = f.Proof(revision: 2);
            var stale = CheatSigning.Sign(new() {Revision=1, Games=[]},f.Key.ExportPkcs8PrivateKeyPem());
            using var handler = new Handler(url => url.EndsWith(".zip") ? (url.StartsWith("https:") ? Bytes(f.Archive) : new(HttpStatusCode.ServiceUnavailable)) : Bytes(url.Contains("192.168.2.76") ? stale : current));
            using var http = new HttpClient(handler); var client = new CheatUpdateClient(http,f.Configuration);
            var check = await client.CheckAsync(f.Store.Root); Assert(check.Catalog.Revision==2 && handler.Requests.Count==3);
            await client.InstallAsync(check,f.Release,f.Store,"0.3.65");
            Assert(!handler.Requests.Any(x=> x.Contains("192.168.2.76") && x.EndsWith(".zip")));
            Assert(handler.Requests.Any(x=> x.Contains("192.168.2.59") && x.EndsWith(".zip")) && handler.Requests.Contains(f.Release.PackageUrl));
        });
        Run("integrity failure stops without download fallback", async () =>
        {
            using var f = new CheatFixture(); using var handler = new Handler(url => url.EndsWith(".zip") ? Bytes(new byte[f.Archive.Length]) : Bytes(f.Proof()));
            using var http = new HttpClient(handler); var client = new CheatUpdateClient(http,f.Configuration);
            var check=await client.CheckAsync(f.Store.Root); await Fails(()=>client.InstallAsync(check,f.Release,f.Store,"0.3.65"));
            Assert(handler.Requests.Count(x=>x.EndsWith(".zip"))==1 && !handler.Requests.Contains(f.Release.PackageUrl));
        });
        Run("interrupted or stalled matching LAN transfer can use the next exact source", async () =>
        {
            foreach (var stall in new[] { false, true })
            {
                using var f = new CheatFixture();
                using var handler = new Handler(url => !url.EndsWith(".zip") ? Bytes(f.Proof()) : url.Contains("192.168.2.76")
                    ? new(HttpStatusCode.OK) { Content = new StreamContent(new InterruptedStream(f.Archive, stall)) } : Bytes(f.Archive));
                using var http = new HttpClient(handler); var client = new CheatUpdateClient(http, f.Configuration, TimeSpan.FromMilliseconds(100));
                var check = await client.CheckAsync(f.Store.Root); await client.InstallAsync(check, f.Release, f.Store, "0.3.65");
                Assert(handler.Requests.Count(x => x.EndsWith(".zip")) == 2 && !handler.Requests.Contains(f.Release.PackageUrl));
            }
        });
        Run("conflicting signed feeds rejected", async () =>
        {
            using var f = new CheatFixture(); var conflict=f.Proof(f.Release with { Sha256=new('d',64) },revision:2);
            using var handler=new Handler(url=>Bytes(url.Contains("192.168.2.76")?conflict:f.Proof()));
            using var http=new HttpClient(handler); await Fails(()=>new CheatUpdateClient(http,f.Configuration).CheckAsync(f.Store.Root));
        });
        Run("all bundled modules meet downloadable menu contract", () =>
        {
            foreach (var module in ModuleCatalog.Create()) CheatStore.ValidateModule(module,module.Definition.Id); return Task.CompletedTask;
        });
        Console.WriteLine($"Independent cheat updates: {passed} passed, {failures} failed."); return failures;
    }
    private static HttpResponseMessage Bytes(byte[] value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };
    private static void Assert(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static async Task Fails(Func<Task> action) { try { await action(); } catch { return; } throw new Exception("Expected failure."); }
    private sealed class Handler(Func<string,HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        { var url=request.RequestUri!.AbsoluteUri; Requests.Add(url); var result=response(url); result.RequestMessage=request; return Task.FromResult(result); }
    }
    private sealed class InterruptedStream(byte[] bytes, bool stall) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (stall) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new IOException("Synthetic disconnected transfer");
        }
    }
}
