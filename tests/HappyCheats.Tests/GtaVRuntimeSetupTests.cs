using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HappyCheats.Core;

namespace HappyCheats.Tests;

internal static class GtaVRuntimeSetupTests
{
    private static int passed, failed;
    public static async Task<int> RunAllAsync()
    {
        passed = failed = 0;
        await Run("fresh setup acquires only two verified runtime files", async () => {
            using var fixture = new Fixture();
            fixture.Put("NativeTrainer.asi", "foreign trainer"); fixture.Put("args.txt", "user arguments"); fixture.Put("xinput1_4.dll", "foreign proxy");
            var result = await fixture.Setup.EnsureAsync(fixture.Game, () => { });
            Assert(result.ChangedFiles.Count == 2 && result.BackupFiles.Count == 0 && fixture.Downloads == 1);
            Assert(fixture.Read("ScriptHookV.dll").SequenceEqual(fixture.Hook) && fixture.Read("dinput8.dll").SequenceEqual(fixture.Loader));
            Assert(fixture.Text("NativeTrainer.asi") == "foreign trainer" && fixture.Text("args.txt") == "user arguments" && fixture.Text("xinput1_4.dll") == "foreign proxy");
            Assert(Directory.GetFiles(fixture.Game).Length == 5 && File.Exists(result.CachedArchive));
        });
        await Run("matching runtime is reused without cache or network", async () => {
            using var fixture = new Fixture(); fixture.InstallMatching();
            var before = fixture.GameHashes();
            var result = await fixture.Setup.EnsureAsync(fixture.Game, () => { });
            Assert(!result.Changed && result.CachedArchive == null && fixture.Downloads == 0 && fixture.GameHashes().SequenceEqual(before));
        });
        await Run("read-only discovery distinguishes preparable missing/old files from unknown conflicts", () => {
            using var fixture = new Fixture();
            fixture.Setup.ValidateExisting(fixture.Game); Assert(!fixture.Setup.IsReady(fixture.Game));
            fixture.Put("ScriptHookV.dll", fixture.OldHook); fixture.Put("dinput8.dll", fixture.Loader);
            fixture.Setup.ValidateExisting(fixture.Game); Assert(!fixture.Setup.IsReady(fixture.Game) && fixture.Setup.IsReady(fixture.Game, requireScriptHook: false));
            fixture.Put("ScriptHookV.dll", "unknown hook"); Assert(!fixture.Setup.IsReady(fixture.Game));
            var conflict = false; try { fixture.Setup.ValidateExisting(fixture.Game); } catch (IOException) { conflict = true; }
            Assert(conflict); fixture.Setup.ValidateExisting(fixture.Game, requireScriptHook: false);
            fixture.InstallMatching(); Assert(fixture.Setup.IsReady(fixture.Game));
            Assert(fixture.Downloads == 0); return Task.CompletedTask;
        });
        await Run("loader-only setup preserves unknown hook and never installs optional archive files or a removal receipt", async () => {
            using var fixture = new Fixture(); fixture.Put("ScriptHookV.dll", "unrecognized existing hook preserved in loader-only mode");
            var result = await fixture.Setup.EnsureAsync(fixture.Game, () => { }, requireScriptHook: false);
            Assert(result.ChangedFiles.Count == 1 && Path.GetFileName(result.ChangedFiles[0]) == "dinput8.dll" && result.BackupFiles.Count == 0);
            Assert(fixture.Text("ScriptHookV.dll") == "unrecognized existing hook preserved in loader-only mode");
            Assert(fixture.Read("dinput8.dll").SequenceEqual(fixture.Loader) && Directory.GetFiles(fixture.Game).Length == 2);
            Assert(!File.Exists(Path.Combine(fixture.Game, "happycheats-gta-v-enhanced-install.json")));
        });
        await Run("loader-only matching runtime does not require or acquire ScriptHookV", async () => {
            using var fixture = new Fixture(); fixture.Put("dinput8.dll", fixture.Loader);
            var result = await fixture.Setup.EnsureAsync(fixture.Game, () => { }, requireScriptHook: false);
            Assert(!result.Changed && fixture.Downloads == 0 && !File.Exists(Path.Combine(fixture.Game, "ScriptHookV.dll")));
            var required = await fixture.Setup.EnsureAsync(fixture.Game, () => { });
            Assert(required.ChangedFiles.Count == 1 && fixture.Read("ScriptHookV.dll").SequenceEqual(fixture.Hook));
        });
        await Run("loader-only callback failure removes only the loader it added", async () => {
            using var fixture = new Fixture(); fixture.Put("ScriptHookV.dll", "foreign hook");
            await Fails(() => fixture.Setup.RunWithRuntimeAsync(fixture.Game, () => { }, () => throw new IOException("trainer failed"), requireScriptHook: false));
            Assert(fixture.Text("ScriptHookV.dll") == "foreign hook" && !File.Exists(Path.Combine(fixture.Game, "dinput8.dll")));
        });
        await Run("recognized old hook gets permanent verified backup and loader remains untouched", async () => {
            using var fixture = new Fixture(); fixture.Put("ScriptHookV.dll", fixture.OldHook); fixture.Put("dinput8.dll", fixture.Loader);
            var loaderTime = File.GetLastWriteTimeUtc(Path.Combine(fixture.Game, "dinput8.dll"));
            var result = await fixture.Setup.EnsureAsync(fixture.Game, () => { });
            Assert(result.ChangedFiles.Count == 1 && result.BackupFiles.Count == 1);
            Assert(File.ReadAllBytes(result.BackupFiles[0]).SequenceEqual(fixture.OldHook));
            Assert(result.BackupFiles[0].Contains(Hash(fixture.OldHook), StringComparison.Ordinal));
            Assert(File.GetLastWriteTimeUtc(Path.Combine(fixture.Game, "dinput8.dll")) == loaderTime);
        });
        await Run("unknown hook or loader blocks download and all game writes", async () => {
            foreach (var name in new[] { "ScriptHookV.dll", "dinput8.dll" }) {
                using var fixture = new Fixture(); fixture.Put(name, "unrecognized user file"); var before = fixture.GameHashes();
                await Fails(() => fixture.Setup.EnsureAsync(fixture.Game, () => { }));
                Assert(fixture.Downloads == 0 && fixture.GameHashes().SequenceEqual(before));
            }
        });
        await Run("running game or incompatible version callback blocks acquisition", async () => {
            using var fixture = new Fixture();
            await Fails(() => fixture.Setup.EnsureAsync(fixture.Game, () => throw new IOException("game is running or incompatible")));
            Assert(fixture.Downloads == 0 && fixture.GameHashes().Length == 0);
        });
        await Run("verified cache supports offline setup without invoking transport", async () => {
            using var fixture = new Fixture(); fixture.SeedCache(fixture.Archive);
            fixture.DownloadOverride = () => throw new IOException("offline");
            var result = await fixture.Setup.EnsureAsync(fixture.Game, () => { });
            Assert(result.Changed && fixture.Downloads == 0);
        });
        await Run("archive hash, corrupt cache, payload hashes and duplicate entries fail before game writes", async () => {
            using (var fixture = new Fixture()) {
                fixture.DownloadOverride = () => Encoding.UTF8.GetBytes("author returned HTML");
                await Fails(() => fixture.Setup.EnsureAsync(fixture.Game, () => { })); Assert(fixture.GameHashes().Length == 0);
            }
            using (var fixture = new Fixture()) {
                fixture.SeedCache(Encoding.UTF8.GetBytes("corrupt cache"));
                await Fails(() => fixture.Setup.EnsureAsync(fixture.Game, () => { })); Assert(fixture.GameHashes().Length == 0 && fixture.Downloads == 0);
            }
            using (var fixture = new Fixture()) {
                var damaged = Zip(new[] { ("bin/ScriptHookV.dll", Encoding.UTF8.GetBytes("wrong DLL")), ("bin/dinput8.dll", fixture.Loader) });
                var setup = fixture.Create(damaged);
                await Fails(() => setup.EnsureAsync(fixture.Game, () => { })); Assert(fixture.GameHashes().Length == 0);
            }
            using (var fixture = new Fixture()) {
                var duplicate = Zip(new[] { ("bin/ScriptHookV.dll", fixture.Hook), ("bin/SCRIPThookV.dll", fixture.Hook), ("bin/dinput8.dll", fixture.Loader) });
                await Fails(() => fixture.Create(duplicate).EnsureAsync(fixture.Game, () => { })); Assert(fixture.GameHashes().Length == 0);
            }
        });
        await Run("oversized download is rejected before game writes", async () => {
            using var fixture = new Fixture(); fixture.DownloadOverride = () => new byte[GtaVRuntimeSetup.MaximumArchiveBytes];
            await Fails(() => fixture.Setup.EnsureAsync(fixture.Game, () => { })); Assert(fixture.GameHashes().Length == 0);
        });
        await Run("archive traversal and optional entries are never extracted", async () => {
            using var fixture = new Fixture();
            var archive = Zip(new[] { ("bin/ScriptHookV.dll", fixture.Hook), ("bin/dinput8.dll", fixture.Loader), ("../../escape.dll", fixture.Hook), ("bin/NativeTrainer.asi", fixture.Hook), ("bin/args.txt", fixture.Hook) });
            await fixture.Create(archive).EnsureAsync(fixture.Game, () => { });
            Assert(Directory.GetFiles(fixture.Game).Length == 2 && !File.Exists(Path.Combine(fixture.Root, "escape.dll")));
        });
        await Run("foreign runtime appearing during download is preserved", async () => {
            using var fixture = new Fixture(); fixture.DownloadOverride = () => { fixture.Put("dinput8.dll", "concurrent user DLL"); return fixture.Archive; };
            await Fails(() => fixture.Setup.EnsureAsync(fixture.Game, () => { }));
            Assert(fixture.Text("dinput8.dll") == "concurrent user DLL" && !File.Exists(Path.Combine(fixture.Game, "ScriptHookV.dll")));
        });
        await Run("downstream trainer failure restores old hook and removes newly added loader", async () => {
            using var fixture = new Fixture(); fixture.Put("ScriptHookV.dll", fixture.OldHook);
            var ran = false;
            await Fails(() => fixture.Setup.RunWithRuntimeAsync(fixture.Game, () => { }, () => {
                ran = true; Assert(fixture.Read("ScriptHookV.dll").SequenceEqual(fixture.Hook) && fixture.Read("dinput8.dll").SequenceEqual(fixture.Loader));
                throw new IOException("trainer install failed after its own rollback");
            }));
            Assert(ran && fixture.Read("ScriptHookV.dll").SequenceEqual(fixture.OldHook) && !File.Exists(Path.Combine(fixture.Game, "dinput8.dll")));
            var backups = Directory.GetFiles(Path.Combine(fixture.Game, "happycheats-runtime-backups"), "*.bak", SearchOption.AllDirectories);
            Assert(backups.Length == 1 && File.ReadAllBytes(backups[0]).SequenceEqual(fixture.OldHook));
        });
        await Run("matching runtime still runs downstream trainer and remains on failure", async () => {
            using var fixture = new Fixture(); fixture.InstallMatching(); var before = fixture.GameHashes(); var ran = false;
            await Fails(() => fixture.Setup.RunWithRuntimeAsync(fixture.Game, () => { }, () => { ran = true; throw new IOException("trainer failure"); }));
            Assert(ran && fixture.Downloads == 0 && fixture.GameHashes().SequenceEqual(before));
        });
        await Run("successful trainer commit is not followed by fallible filesystem validation", async () => {
            using var fixture = new Fixture();
            var moved = Path.Combine(fixture.Root, "committed-game");
            var committed = false;
            var result = await fixture.Setup.RunWithRuntimeAsync(fixture.Game,
                () => { if (committed) throw new IOException("postcommit callback is forbidden"); },
                () => {
                    Directory.Move(fixture.Game, moved);
                    Directory.CreateSymbolicLink(fixture.Game, moved);
                    committed = true;
                });
            Assert(committed && result.ChangedFiles.Count == 2);
            Assert(File.ReadAllBytes(Path.Combine(moved, "ScriptHookV.dll")).SequenceEqual(fixture.Hook));
            Assert(File.ReadAllBytes(Path.Combine(moved, "dinput8.dll")).SequenceEqual(fixture.Loader));
        });
        await Run("mid-transaction closure check failure rolls committed runtime back", async () => {
            using var fixture = new Fixture(); var tripped = false;
            await Fails(() => fixture.Setup.EnsureAsync(fixture.Game, () => {
                if (!tripped && File.Exists(Path.Combine(fixture.Game, "ScriptHookV.dll"))) { tripped = true; throw new IOException("one-shot precommit failure"); }
            }));
            Assert(tripped && !File.Exists(Path.Combine(fixture.Game, "ScriptHookV.dll")) && !File.Exists(Path.Combine(fixture.Game, "dinput8.dll")));
        });
        await Run("concurrent modification during downstream failure is preserved with backup recovery details", async () => {
            using var fixture = new Fixture(); fixture.Put("ScriptHookV.dll", fixture.OldHook);
            var failure = await Fails(() => fixture.Setup.RunWithRuntimeAsync(fixture.Game, () => { }, () => { fixture.Put("ScriptHookV.dll", "external change"); throw new IOException("trainer failed"); }));
            Assert(failure is AggregateException && failure.ToString().Contains("Original backup:", StringComparison.Ordinal));
            Assert(fixture.Text("ScriptHookV.dll") == "external change" && !File.Exists(Path.Combine(fixture.Game, "dinput8.dll")));
        });
        await Run("persistent running-game transition blocks unsafe rollback and reports retained recovery", async () => {
            using var fixture = new Fixture(); fixture.Put("ScriptHookV.dll", fixture.OldHook); var running = false;
            var failure = await Fails(() => fixture.Setup.RunWithRuntimeAsync(fixture.Game, () => { if (running) throw new IOException("game started"); }, () => { running = true; throw new IOException("trainer failed"); }));
            Assert(failure is AggregateException && failure.ToString().Contains("Original backup:", StringComparison.Ordinal));
            Assert(fixture.Read("ScriptHookV.dll").SequenceEqual(fixture.Hook));
        });
        await Run("cancelled acquisition never changes game files", async () => {
            using var fixture = new Fixture(); using var cancel = new CancellationTokenSource();
            fixture.DownloadOverride = () => { cancel.Cancel(); return fixture.Archive; };
            await Fails(() => fixture.Setup.EnsureAsync(fixture.Game, () => { }, cancellationToken: cancel.Token));
            Assert(fixture.GameHashes().Length == 0);
        });
        await Run("directory and linked runtime paths are refused before acquisition", async () => {
            using (var fixture = new Fixture()) {
                Directory.CreateDirectory(Path.Combine(fixture.Game, "dinput8.dll"));
                await Fails(() => fixture.Setup.EnsureAsync(fixture.Game, () => { })); Assert(fixture.Downloads == 0);
            }
            using (var fixture = new Fixture()) {
                var external = Path.Combine(fixture.Root, "external"); Directory.CreateDirectory(external);
                Directory.CreateSymbolicLink(Path.Combine(fixture.Game, "happycheats-runtime-backups"), external);
                fixture.Put("ScriptHookV.dll", fixture.OldHook);
                await Fails(() => fixture.Setup.EnsureAsync(fixture.Game, () => { }));
                Assert(fixture.Read("ScriptHookV.dll").SequenceEqual(fixture.OldHook) && Directory.GetFiles(external).Length == 0);
            }
        });
        Console.WriteLine($"GTA runtime setup tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
    private static async Task Run(string name, Func<Task> test) { try { await test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }
    private static void Assert(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static async Task<Exception> Fails(Func<Task> task) { try { await task(); } catch (Exception error) { return error; } throw new Exception("Expected rejection."); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Zip(IEnumerable<(string Name, byte[] Data)> files) {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true)) foreach (var (name, data) in files) { using var file = archive.CreateEntry(name).Open(); file.Write(data); }
        return memory.ToArray();
    }
    private sealed class Fixture : IDisposable {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-gta-runtime-tests-" + Guid.NewGuid().ToString("N"));
        public string Game => Path.Combine(Root, "game");
        public string Cache => Path.Combine(Root, "cache");
        public byte[] Hook { get; } = Encoding.UTF8.GetBytes("verified synthetic current hook");
        public byte[] Loader { get; } = Encoding.UTF8.GetBytes("verified synthetic current loader");
        public byte[] OldHook { get; } = Encoding.UTF8.GetBytes("verified synthetic old hook");
        public byte[] Archive { get; }
        public GtaVRuntimeSetup Setup { get; }
        public int Downloads { get; private set; }
        public Func<byte[]>? DownloadOverride { get; set; }
        public Fixture() {
            Directory.CreateDirectory(Game);
            Archive = Zip(new[] { ("bin/ScriptHookV.dll", Hook), ("bin/dinput8.dll", Loader), ("bin/NativeTrainer.asi", Hook), ("bin/args.txt", Hook), ("bin/xinput1_4.dll", Hook) });
            Setup = Create(Archive);
        }
        public GtaVRuntimeSetup Create(byte[] archive) => new(new(Hash(archive), Hash(Hook), Hash(Loader), Hash(OldHook)), Cache, _ => { Downloads++; return Task.FromResult(DownloadOverride?.Invoke() ?? archive); });
        public void SeedCache(byte[] bytes) { Directory.CreateDirectory(Cache); File.WriteAllBytes(Path.Combine(Cache, Hash(Archive) + ".zip"), bytes); }
        public void Put(string name, string text) => Put(name, Encoding.UTF8.GetBytes(text));
        public void Put(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(Game, name), bytes);
        public byte[] Read(string name) => File.ReadAllBytes(Path.Combine(Game, name));
        public string Text(string name) => File.ReadAllText(Path.Combine(Game, name));
        public void InstallMatching() { Put("ScriptHookV.dll", Hook); Put("dinput8.dll", Loader); }
        public string[] GameHashes() => Directory.GetFiles(Game, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).Select(path => Path.GetRelativePath(Game, path) + ":" + Hash(File.ReadAllBytes(path))).ToArray();
        public void Dispose() {
            var resolved = Path.GetFullPath(Root);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("HappyCheats-gta-runtime-tests-", StringComparison.Ordinal)) throw new IOException("Unsafe fixture cleanup target.");
            // Directory.Delete removes a child directory symlink itself rather than traversing its target.
            Directory.Delete(resolved, recursive: true);
        }
    }
}
