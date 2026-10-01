using HappyCheats.Updater;

namespace HappyCheats.Tests;

internal static class UpdateInstallerTests
{
    public static int RunAll()
    {
        var failed = 0;
        Run("update swaps full release and keeps the trusted configuration", () =>
        {
            using var fixture = new Fixture();
            var launched = false;
            var backup = UpdateInstaller.Apply(fixture.Request, target =>
            {
                Assert(File.ReadAllText(Path.Combine(target, "HappyCheats.exe")) == "new");
                Assert(File.ReadAllText(Path.Combine(target, "update-settings.json")) == "trusted");
                Assert(!File.Exists(Path.Combine(target, "obsolete.dll")));
                launched = true;
            });
            Assert(launched && File.Exists(Path.Combine(backup, "obsolete.dll")));
            Assert(File.ReadAllText(Path.Combine(backup, "HappyCheats.exe")) == "old");
            Assert(File.ReadAllText(Path.Combine(fixture.Request.WorkDirectory, "transaction.json")).Contains("complete"));
        });
        Run("failed new-client startup restores every previous file", () =>
        {
            using var fixture = new Fixture();
            Fails(() => UpdateInstaller.Apply(fixture.Request, _ => throw new IOException("Startup failed")));
            Assert(File.ReadAllText(Path.Combine(fixture.Target, "HappyCheats.exe")) == "old");
            Assert(File.Exists(Path.Combine(fixture.Target, "obsolete.dll")));
            Assert(File.ReadAllText(Path.Combine(fixture.Target, "update-settings.json")) == "trusted");
            Assert(Directory.Exists(Path.Combine(fixture.Request.WorkDirectory, "failed-release")));
            Assert(File.ReadAllText(Path.Combine(fixture.Request.WorkDirectory, "transaction.json")).Contains("rolled-back"));
        });
        Run("missing required helper fails before replacing the client", () =>
        {
            using var fixture = new Fixture();
            File.Delete(Path.Combine(fixture.Package, "updater", "HappyCheats.Updater.exe"));
            Fails(() => UpdateInstaller.Apply(fixture.Request, _ => throw new Exception("Must not launch")));
            Assert(File.ReadAllText(Path.Combine(fixture.Target, "HappyCheats.exe")) == "old");
        });
        Run("installer rejects staging outside the application sibling folder", () =>
        {
            using var fixture = new Fixture();
            Fails(() => UpdateInstaller.Validate(fixture.Request with { WorkDirectory = fixture.Root }));
            Fails(() => UpdateInstaller.Validate(fixture.Request with { PackageDirectory = fixture.Target }));
            Assert(File.ReadAllText(Path.Combine(fixture.Target, "HappyCheats.exe")) == "old");
        });
        Run("missing existing trust configuration cannot adopt a downloaded key", () =>
        {
            using var fixture = new Fixture();
            File.Delete(Path.Combine(fixture.Target, "update-settings.json"));
            Fails(() => UpdateInstaller.Apply(fixture.Request, _ => { }));
            Assert(File.ReadAllText(Path.Combine(fixture.Target, "HappyCheats.exe")) == "old");
        });
        Run("interrupted transaction and existing backups are preserved", () =>
        {
            using var fixture = new Fixture();
            Directory.CreateDirectory(Path.Combine(fixture.Request.WorkDirectory, "previous"));
            File.WriteAllText(Path.Combine(fixture.Request.WorkDirectory, "previous", "valuable.txt"), "keep");
            Fails(() => UpdateInstaller.Apply(fixture.Request, _ => { }));
            Assert(File.ReadAllText(Path.Combine(fixture.Request.WorkDirectory, "previous", "valuable.txt")) == "keep");
        });
        Run("final journal failure never rolls back a confirmed running client", () =>
        {
            using var fixture = new Fixture();
            var backup = UpdateInstaller.Apply(fixture.Request, _ =>
                Directory.CreateDirectory(Path.Combine(fixture.Request.WorkDirectory, "transaction.json.tmp")));
            Assert(File.ReadAllText(Path.Combine(fixture.Target, "HappyCheats.exe")) == "new");
            Assert(File.ReadAllText(Path.Combine(backup, "HappyCheats.exe")) == "old");
            Assert(!Directory.Exists(Path.Combine(fixture.Request.WorkDirectory, "failed-release")));
        });
        Console.WriteLine($"Updater installation tests: {7 - failed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;

        void Run(string name, Action check)
        {
            try { check(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
        }
    }

    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected rejection."); }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-update-tests-" + Guid.NewGuid().ToString("N"));
        public string Target => Path.Combine(Root, "client");
        public string Package => Request.PackageDirectory;
        public ApplyRequest Request { get; }
        public Fixture()
        {
            var work = Path.Combine(Root, ".happycheats-updates", Guid.NewGuid().ToString("N"));
            Request = new ApplyRequest(Target, Path.Combine(work, "HappyCheats"), work, 0, 0);
            Directory.CreateDirectory(Target);
            Directory.CreateDirectory(Path.Combine(Package, "updater"));
            foreach (var name in new[] { "HappyCheats.exe", "HappyCheats.dll", "HappyCheats.runtimeconfig.json",
                         "updater/HappyCheats.Updater.exe", "updater/HappyCheats.Updater.dll", "updater/HappyCheats.Updater.runtimeconfig.json" })
                File.WriteAllText(Path.Combine(Package, name), "new");
            File.WriteAllText(Path.Combine(Target, "HappyCheats.exe"), "old");
            File.WriteAllText(Path.Combine(Target, "obsolete.dll"), "old");
            File.WriteAllText(Path.Combine(Target, "update-settings.json"), "trusted");
            File.WriteAllText(Path.Combine(Package, "update-settings.json"), "remote-key-must-not-be-adopted");
        }
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("HappyCheats-update-tests-")) throw new IOException("Invalid fixture path.");
            Directory.Delete(resolved, recursive: true);
        }
    }
}
