using System.Diagnostics;
using System.Text.Json;
using HappyCheats.Core.Updates;

namespace HappyCheats.Tests;

internal static class UpdateCacheCleanupTests
{
    public static int RunAll()
    {
        var passed = 0;
        var failed = 0;
        Run("removes old completed history and retains newest rollback and unrelated files", () =>
        {
            using var f = new Fixture();
            var old = f.Transaction(-3);
            var middle = f.Transaction(-2);
            var newest = f.Transaction(-1);
            var settings = Path.Combine(f.Root, "settings.json");
            var other = Path.Combine(f.Root, "another-bundle", ".happycheats-updates", "keep.txt");
            File.WriteAllText(settings, "user settings");
            Directory.CreateDirectory(Path.GetDirectoryName(other)!);
            File.WriteAllText(other, "other installation");
            var result = f.Prune();
            Assert(result.RemovedTransactions == 2 && !Directory.Exists(old) && !Directory.Exists(middle));
            Assert(File.Exists(Path.Combine(newest, "previous", "HappyCheats.exe")));
            Assert(File.ReadAllText(Path.Combine(f.App, "HappyCheats.exe")) == "current client");
            Assert(File.ReadAllText(settings) == "user settings" && File.ReadAllText(other) == "other installation");
            Assert(f.Prune().RemovedTransactions == 0);
        });
        Run("preserves single rollback and all equally newest transactions", () =>
        {
            using var f = new Fixture();
            var one = f.Transaction(-1);
            Assert(f.Prune().RemovedTransactions == 0 && Directory.Exists(one));
            var two = f.Transaction(-1);
            f.Transaction(-2);
            Assert(f.Prune().RemovedTransactions == 1 && Directory.Exists(one) && Directory.Exists(two));
        });
        Run("clock changes cannot replace the newest release rollback with an older version", () =>
        {
            using var f = new Fixture();
            var newestVersion = f.Transaction(-4, "1.2.3");
            var olderVersion = f.Transaction(-1, "1.2.2");
            var oldestVersion = f.Transaction(-2, "1.2.1");
            Assert(f.Prune().RemovedTransactions == 2 && Directory.Exists(newestVersion));
            Assert(!Directory.Exists(olderVersion) && !Directory.Exists(oldestVersion));
        });
        Run("an unrecognized latest transaction does not remove the last recognized rollback", () =>
        {
            using var f = new Fixture();
            var oldest = f.Transaction(-3);
            var recognized = f.Transaction(-2);
            var unrecognized = f.Transaction(-1);
            File.WriteAllText(Path.Combine(unrecognized, "personal.txt"), "preserve");
            Assert(f.Prune().RemovedTransactions == 1 && !Directory.Exists(oldest));
            Assert(Directory.Exists(recognized) && File.ReadAllText(Path.Combine(unrecognized, "personal.txt")) == "preserve");
        });
        Run("interrupted missing malformed duplicate and mismatched journals are preserved", () =>
        {
            foreach (var mutation in new Action<string>[]
            {
                path => File.Delete(path),
                path => File.WriteAllText(path, "not JSON"),
                path => File.WriteAllText(path, new string('x', 20_000)),
                path => File.WriteAllText(path, File.ReadAllText(path).Replace("\"complete\"", "\"replaced\"")),
                path => File.WriteAllText(path, File.ReadAllText(path).Replace("\"state\":", "\"state\":\"complete\",\"state\":")),
                path => { var doc = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path))!; doc["TargetDirectory"] = "../outside"; File.WriteAllText(path, JsonSerializer.Serialize(doc)); },
                path => { var doc = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path))!; doc["backup"] = Path.GetTempPath(); File.WriteAllText(path, JsonSerializer.Serialize(doc)); }
            })
            {
                using var f = new Fixture();
                var old = f.Transaction(-2);
                f.Transaction(-1);
                mutation(Path.Combine(old, "transaction.json"));
                Assert(f.Prune().RemovedTransactions == 0 && File.Exists(Path.Combine(old, "previous", "HappyCheats.exe")));
            }
        });
        Run("unrecognized transaction names files and missing layouts are preserved", () =>
        {
            foreach (var mutation in new Action<string>[]
            {
                path => File.WriteAllText(Path.Combine(path, "personal.txt"), "keep"),
                path => Directory.CreateDirectory(Path.Combine(path, "custom-backup")),
                path => File.Delete(Path.Combine(path, "previous", "HappyCheats.dll")),
                path => Directory.Delete(Path.Combine(path, "previous", "payload"), true),
                path => File.Delete(Path.Combine(path, "runner", "HappyCheats.Updater.exe")),
                path => File.WriteAllText(Path.Combine(path, "apply.json"), "{}"),
                path => File.WriteAllText(Path.Combine(path, "helper-ready.txt"), "not ready")
            })
            {
                using var f = new Fixture();
                var old = f.Transaction(-2);
                f.Transaction(-1);
                mutation(old);
                Assert(f.Prune().RemovedTransactions == 0 && Directory.Exists(old));
            }
            using var names = new Fixture();
            var original = names.Transaction(-2);
            var unrecognized = Path.Combine(names.History, "manual-backup");
            Directory.Move(original, unrecognized);
            names.Transaction(-1);
            Assert(names.Prune().RemovedTransactions == 0 && Directory.Exists(unrecognized));
        });
        Run("newer versions future timestamps and custom trust configurations survive", () =>
        {
            using var f = new Fixture();
            var newer = f.Transaction(-4, "2.0.0");
            var custom = f.Transaction(-3);
            File.WriteAllText(Path.Combine(custom, "previous", "update-settings.json"), "custom trust configuration");
            var future = f.Transaction(1);
            var newest = f.Transaction(-1);
            Assert(f.Prune().RemovedTransactions == 0);
            Assert(new[] { newer, custom, future, newest }.All(Directory.Exists));
        });
        Run("a busy file preserves the entire old transaction before any deletion", () =>
        {
            using var f = new Fixture();
            var old = f.Transaction(-2);
            f.Transaction(-1);
            var before = Directory.GetFiles(old, "*", SearchOption.AllDirectories);
            using (var busy = new FileStream(Path.Combine(old, "previous", "payload", "trainer.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert(f.Prune().RemovedTransactions == 0);
                Assert(before.All(File.Exists));
            }
            Assert(f.Prune().RemovedTransactions == 1);
        });
        Run("simultaneous cleanup sessions cannot delete each other's files", () =>
        {
            using var f = new Fixture();
            var old = f.Transaction(-2);
            f.Transaction(-1);
            using (var lease = new FileStream(Path.Combine(f.History, ".cleanup.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                Assert(f.Prune().RemovedTransactions == 0 && Directory.Exists(old));
            Assert(f.Prune().RemovedTransactions == 1);
        });
        Run("other clients updaters and activity appearing at preflight stop cleanup", () =>
        {
            using var f = new Fixture();
            var old = f.Transaction(-2);
            f.Transaction(-1);
            var busy = UpdateCacheCleanup.Prune(f.App, "1.2.3", () => true);
            Assert(busy.RetryNeeded && busy.RemovedTransactions == 0 && Directory.Exists(old));
            var checks = 0;
            var racing = UpdateCacheCleanup.Prune(f.App, "1.2.3", () => ++checks >= 3);
            Assert(racing.RetryNeeded && racing.RemovedTransactions == 0 && Directory.Exists(old));
            Assert(UpdateCacheCleanup.Prune(f.App, "1.2.3", () => throw new IOException("Unreadable processes")).RemovedTransactions == 0);
        });
        Run("linked transaction trees and linked history roots are preserved", () =>
        {
            using var f = new Fixture();
            var old = f.Transaction(-2);
            f.Transaction(-1);
            var outside = Path.Combine(f.Root, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "must survive");
            var link = Path.Combine(old, "previous", "payload", "linked");
            CreateDirectoryLink(link, outside);
            try
            {
                Assert(f.Prune().RemovedTransactions == 0 && Directory.Exists(old));
                Assert(File.ReadAllText(Path.Combine(outside, "keep.txt")) == "must survive");
            }
            finally { Directory.Delete(link); }
            var retained = f.History + "-retained";
            Directory.Move(f.History, retained);
            CreateDirectoryLink(f.History, retained);
            try { Assert(f.Prune().RemovedTransactions == 0 && Directory.Exists(Path.Combine(retained, Path.GetFileName(old)))); }
            finally { Directory.Delete(f.History); }
        });
        Run("startup retries busy updater handoff then cleans without blocking the caller", () =>
        {
            using var f = new Fixture();
            var old = f.Transaction(-2);
            f.Transaction(-1);
            var delays = 0;
            var checks = 0;
            UpdateCacheCleanup.RunAfterStartupAsync(f.App, "1.2.3", () => ++checks <= 2,
                _ => { delays++; return Task.CompletedTask; }).GetAwaiter().GetResult();
            Assert(delays == 3 && !Directory.Exists(old));
        });
        Run("startup retries are bounded and cancellation or maintenance errors never escape", () =>
        {
            using var f = new Fixture();
            var old = f.Transaction(-2);
            f.Transaction(-1);
            var delays = 0;
            UpdateCacheCleanup.RunAfterStartupAsync(f.App, "1.2.3", () => true,
                _ => { delays++; return Task.CompletedTask; }).GetAwaiter().GetResult();
            Assert(delays == 6 && Directory.Exists(old));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            UpdateCacheCleanup.RunAfterStartupAsync(f.App, "1.2.3", () => false,
                _ => Task.CompletedTask, canceled.Token).GetAwaiter().GetResult();
            UpdateCacheCleanup.RunAfterStartupAsync(f.App, "1.2.3", () => false,
                _ => throw new IOException("Delay failed")).GetAwaiter().GetResult();
            Assert(Directory.Exists(old));
        });
        Console.WriteLine($"Update cache cleanup tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;

        void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS update cache: " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL update cache: " + name + ": " + error); }
        }
    }

    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(target);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not create junction fixture: " + process.StandardError.ReadToEnd());
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-cache-cleanup-tests-" + Guid.NewGuid().ToString("N"));
        public string App => Path.Combine(Root, "HappyCheats");
        public string History => Path.Combine(Root, ".happycheats-updates");
        private readonly DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        public Fixture() { Client(App, "current client"); }
        public UpdateCacheCleanup.Result Prune() => UpdateCacheCleanup.Prune(App, "1.2.3", () => false);
        public string Transaction(int days, string version = "1.2.3")
        {
            var work = Path.Combine(History, Guid.NewGuid().ToString("N"));
            var previous = Path.Combine(work, "previous");
            Client(previous, "previous client");
            var runner = Path.Combine(work, "runner");
            Directory.CreateDirectory(runner);
            foreach (var name in new[] { "HappyCheats.Updater.exe", "HappyCheats.Updater.dll", "HappyCheats.Updater.runtimeconfig.json" })
                File.WriteAllText(Path.Combine(runner, name), "runner");
            File.WriteAllText(Path.Combine(work, "archive.zip"), "completed download");
            File.WriteAllText(Path.Combine(work, "helper-ready.txt"), "ready");
            File.WriteAllText(Path.Combine(work, "launch-ready.txt"), version);
            File.WriteAllText(Path.Combine(work, "transaction.json"), JsonSerializer.Serialize(new
            {
                state = "complete", TargetDirectory = App, PackageDirectory = Path.Combine(work, "HappyCheats"), backup = previous,
                updatedAt = timestamp.AddDays(days)
            }));
            File.WriteAllText(Path.Combine(work, "apply.json"), JsonSerializer.Serialize(new
            {
                TargetDirectory = App, PackageDirectory = Path.Combine(work, "HappyCheats"), WorkDirectory = work,
                ParentProcessId = 123, ParentStartTimeUtcTicks = 456L
            }));
            return work;
        }
        private static void Client(string directory, string content)
        {
            foreach (var relative in new[] { "HappyCheats.exe", "HappyCheats.dll", "HappyCheats.runtimeconfig.json", "update-settings.json",
                "updater/HappyCheats.Updater.exe", "updater/HappyCheats.Updater.dll", "updater/HappyCheats.Updater.runtimeconfig.json", "payload/trainer.dll" })
            {
                var path = Path.Combine(directory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, relative == "update-settings.json" ? "same trusted configuration" : content);
            }
        }
        public void Dispose()
        {
            var full = Path.GetFullPath(Root);
            var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("HappyCheats-cache-cleanup-tests-", StringComparison.Ordinal))
                throw new IOException("Unsafe test cleanup path.");
            Directory.Delete(full, true);
        }
    }
}
