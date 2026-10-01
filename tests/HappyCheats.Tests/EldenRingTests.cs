using HappyCheats.Core;
using HappyCheats.Core.EldenRing;

namespace HappyCheats.Tests;

internal static class EldenRingTests
{
    private static int passed, failed;

    public static int RunAll()
    {
        passed = failed = 0;
        Run("Elden Ring catalog advertises only implemented offline resource controls", () =>
        {
            using var module = new EldenRingModule(".");
            var definition = module.Definition;
            Assert(definition.Id == "elden-ring" && definition.AppId == "1245620");
            Assert(definition.Features.Select(feature => feature.Id).SequenceEqual(new[] { "er_health", "er_fp", "er_stamina", "er_restore" }));
            Assert(definition.Features.Take(3).All(feature => feature.IsToggle));
            Assert(!definition.Features.Last().IsToggle);
            Assert(definition.Features.Select(feature => feature.Hotkey).Distinct().Count() == 4);
            Assert(definition.Features[0].Description.Contains("lethal damage", StringComparison.OrdinalIgnoreCase));
        });
        Run("Elden Ring accepts root and Game folder selections without editing either", () =>
        {
            using var fixture = new Fixture();
            Assert(EldenRingModule.NormalizeDirectory(fixture.Game) == fixture.Game);
            Assert(EldenRingModule.NormalizeDirectory(fixture.Game + Path.DirectorySeparatorChar) == fixture.Game);
            Assert(EldenRingModule.NormalizeDirectory(Path.Combine(fixture.Game, "Game")) == fixture.Game);
            Assert(File.ReadAllText(fixture.Executable) == "fixture, not executable");
        });
        Run("Elden Ring missing payload blocks discovery readiness without writing game files", () =>
        {
            using var fixture = new Fixture();
            using var module = new EldenRingModule(fixture.Root);
            var installation = module.DiscoverAsync(fixture.Game).GetAwaiter().GetResult();
            Assert(installation.IsInstalled && !installation.Compatible && !installation.TrainerInstalled);
            Assert(Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Count() == 1);
            Assert(File.ReadAllText(fixture.Executable) == "fixture, not executable");
        });
        Run("Elden Ring rejected package cannot connect or write game files", () =>
        {
            using var fixture = new Fixture();
            using var module = new EldenRingModule(fixture.Root);
            var installation = new GameInstallation(fixture.Game, "fixture", true, false, false, true, "fixture");
            Fails(() => module.InstallAsync(installation).GetAwaiter().GetResult());
            Assert(module.ConnectedProcessId == 0);
            Assert(!module.ReadStatusAsync(installation).GetAwaiter().GetResult().Connected);
            Assert(Directory.EnumerateFiles(fixture.Game, "*", SearchOption.AllDirectories).Count() == 1);
        });
        Run("Elden Ring removal disconnects without deleting any installation file", () =>
        {
            using var fixture = new Fixture();
            using var module = new EldenRingModule(fixture.Root);
            var installation = new GameInstallation(fixture.Game, "fixture", true, false, false, true, "fixture");
            module.RemoveAsync(installation).GetAwaiter().GetResult();
            module.Stop();
            Assert(module.ConnectedProcessId == 0 && File.Exists(fixture.Executable));
            Assert(File.ReadAllText(fixture.Executable) == "fixture, not executable");
        });
        Run("Elden Ring initial read is observational and every toggle starts off", () =>
        {
            var runtime = new FakeRuntime();
            using var session = new EldenRingSession(runtime);
            var status = session.Poll();
            Assert(status.Connected && status.Ready && status.SessionId.Length > 0);
            AssertOff(status);
            Assert(runtime.Writes.Count == 0);
            Assert(status.Values["Health"] == "50 / 100" && status.Values["FP"] == "20 / 80" && status.Values["Stamina"] == "30 / 90");
        });
        Run("Elden Ring one-shot restores selected resources without arming periodic writes", () =>
        {
            var runtime = new FakeRuntime();
            using var session = new EldenRingSession(runtime);
            var status = session.Command(session.Poll(), "er_restore", null);
            Assert(runtime.Writes.Single() == (true, true, true));
            AssertOff(status);
            Assert(status.Values["Health"] == "100 / 100");
            session.Poll();
            Assert(runtime.Writes.Count == 1);
        });
        Run("Elden Ring enabled refills only target explicitly selected resources", () =>
        {
            var runtime = new FakeRuntime();
            using var session = new EldenRingSession(runtime);
            var status = session.Command(session.Poll(), "er_fp", true);
            Assert(status.Enabled["er_fp"] && !status.Enabled["er_health"] && !status.Enabled["er_stamina"]);
            Assert(runtime.Writes.Single() == (false, true, false));
            session.Poll();
            Assert(runtime.Writes.Last() == (false, true, false) && runtime.Writes.Count == 2);
        });
        Run("Elden Ring disabling one toggle preserves other toggles and makes no write", () =>
        {
            var runtime = new FakeRuntime();
            using var session = new EldenRingSession(runtime);
            var status = session.Command(session.Poll(), "er_health", true);
            status = session.Command(status, "er_stamina", true);
            var writes = runtime.Writes.Count;
            status = session.Command(status, "er_health", false);
            Assert(!status.Enabled["er_health"] && status.Enabled["er_stamina"] && runtime.Writes.Count == writes);
            status = session.Command(status, "disable_all", null);
            AssertOff(status);
            session.Poll();
            Assert(runtime.Writes.Count == writes);
        });
        Run("Elden Ring death and loading clear toggles, hide values and invalidate prior status", () =>
        {
            var runtime = new FakeRuntime();
            using var session = new EldenRingSession(runtime);
            var status = session.Command(session.Poll(), "er_health", true);
            runtime.State = Live() with { Ready = false, Health = 0 };
            var dead = session.Poll();
            Assert(!dead.Ready && dead.Values.Count == 0 && dead.SessionId != status.SessionId);
            AssertOff(dead);
            var writes = runtime.Writes.Count;
            Fails(() => session.Command(dead, "er_restore", null));
            AssertOff(session.Command(status, "disable_all", null));
            AssertOff(session.Command(status, "er_health", false));
            runtime.State = Live();
            Fails(() => session.Command(status, "er_health", true));
            AssertOff(session.Poll());
            Assert(runtime.Writes.Count == writes);
        });
        Run("Elden Ring character change cannot inherit active refills or queued commands", () =>
        {
            var runtime = new FakeRuntime();
            using var session = new EldenRingSession(runtime);
            var old = session.Command(session.Poll(), "er_health", true);
            runtime.State = Live("different-character");
            var current = session.Poll();
            Assert(current.Ready && current.SessionId != old.SessionId);
            AssertOff(current);
            var writes = runtime.Writes.Count;
            Fails(() => session.Command(old, "er_restore", null));
            Assert(runtime.Writes.Count == writes);
            Assert(session.Command(current, "er_fp", true).Enabled["er_fp"]);
        });
        Run("Elden Ring stale disconnected and unready requests cannot arm or restore", () =>
        {
            var runtime = new FakeRuntime();
            using var session = new EldenRingSession(runtime);
            var status = session.Poll();
            foreach (var request in new[] { status with { SessionId = "stale" }, status with { Ready = false }, status with { Connected = false } })
            {
                Fails(() => session.Command(request, "er_health", true));
                Fails(() => session.Command(request, "er_restore", null));
            }
            Assert(runtime.Writes.Count == 0);
            AssertOff(session.Poll());
        });
        Run("Elden Ring polling lapse clears toggles and rejects queued enables", () =>
        {
            var runtime = new FakeRuntime();
            var now = DateTime.UtcNow;
            using var session = new EldenRingSession(runtime) { Clock = () => now };
            var status = session.Command(session.Poll(), "er_health", true);
            now = now.AddSeconds(5);
            Fails(() => session.Command(status, "er_fp", true));
            var fresh = session.Poll();
            Assert(fresh.SessionId != status.SessionId);
            AssertOff(fresh);
            Assert(runtime.Writes.Count == 1);
        });
        Run("Elden Ring wall clock rollback also invalidates active session", () =>
        {
            var runtime = new FakeRuntime();
            var now = DateTime.UtcNow;
            using var session = new EldenRingSession(runtime) { Clock = () => now };
            var status = session.Command(session.Poll(), "er_health", true);
            now = now.AddSeconds(-1);
            var fresh = session.Poll();
            AssertOff(fresh);
            Assert(fresh.SessionId != status.SessionId && runtime.Writes.Count == 1);
        });
        Run("Elden Ring failed read clears every effect and requires a fresh status", () =>
        {
            var runtime = new FakeRuntime();
            using var session = new EldenRingSession(runtime);
            var status = session.Command(session.Poll(), "er_health", true);
            runtime.ReadError = true;
            Fails(() => session.Poll());
            runtime.ReadError = false;
            var fresh = session.Poll();
            AssertOff(fresh);
            Assert(fresh.SessionId != status.SessionId && runtime.Writes.Count == 1);
            Fails(() => session.Command(status, "er_restore", null));
        });
        Run("Elden Ring failed refill clears existing effects and never arms requested effect", () =>
        {
            var runtime = new FakeRuntime();
            using var session = new EldenRingSession(runtime);
            var status = session.Command(session.Poll(), "er_health", true);
            runtime.RefillError = true;
            Fails(() => session.Command(status, "er_fp", true));
            runtime.RefillError = false;
            var fresh = session.Poll();
            AssertOff(fresh);
            Assert(fresh.SessionId != status.SessionId && runtime.Writes.Count == 1);
        });
        Run("Elden Ring partial refill followed by context change stops all future writes", () =>
        {
            var runtime = new FakeRuntime();
            using var session = new EldenRingSession(runtime);
            var old = session.Command(session.Poll(), "er_health", true);
            runtime.AfterRefill = state => state with { Context = "changed-mid-write", FP = 10 };
            Fails(() => session.Command(old, "er_restore", null));
            var writes = runtime.Writes.Count;
            runtime.AfterRefill = null;
            var fresh = session.Poll();
            AssertOff(fresh);
            Assert(fresh.SessionId != old.SessionId && runtime.Writes.Count == writes);
        });
        Run("Elden Ring death returned from a periodic refill disarms that session", () =>
        {
            var runtime = new FakeRuntime();
            using var session = new EldenRingSession(runtime);
            session.Command(session.Poll(), "er_health", true);
            runtime.AfterRefill = state => state with { Ready = false, Health = 0 };
            Fails(() => session.Poll());
            runtime.AfterRefill = null;
            var status = session.Poll();
            Assert(!status.Ready);
            AssertOff(status);
        });
        Run("Elden Ring command schema rejects unknown, missing and extraneous toggle intent", () =>
        {
            foreach (var feature in new[] { "er_health", "er_fp", "er_stamina" })
            {
                EldenRingSession.ValidateCommand(feature, true);
                EldenRingSession.ValidateCommand(feature, false);
                Fails(() => EldenRingSession.ValidateCommand(feature, null));
            }
            foreach (var feature in new[] { "er_restore", "disable_all" })
            {
                EldenRingSession.ValidateCommand(feature, null);
                Fails(() => EldenRingSession.ValidateCommand(feature, true));
                Fails(() => EldenRingSession.ValidateCommand(feature, false));
            }
            foreach (var feature in new[] { "runes", "online", "er_health\ner_restore", "" })
                Fails(() => EldenRingSession.ValidateCommand(feature, true));
        });
        Run("Elden Ring disposal closes runtime once and prevents all further operations", () =>
        {
            var runtime = new FakeRuntime();
            var session = new EldenRingSession(runtime);
            var status = session.Command(session.Poll(), "er_health", true);
            session.Dispose();
            session.Dispose();
            Assert(runtime.Disposals == 1);
            Fails(() => session.Poll());
            Fails(() => session.Command(status, "er_restore", null));
            Assert(runtime.Writes.Count == 1);
        });
        Run("Elden Ring backend reads offline resources without any write", () =>
        {
            using var fixture = new MemoryFixture();
            var state = fixture.Runtime.Read();
            Assert(state.Ready && state.Health == 50 && state.MaxHealth == 100 && state.FP == 20 && state.MaxFP == 80 && state.Stamina == 30 && state.MaxStamina == 90);
            Assert(fixture.Memory.Checks == 1 && fixture.Memory.Writes.Count == 0);
            // Connection capability is distinct from the current online state.
            fixture.Memory.Put(MemoryFixture.Game + 0xBC9, [1]);
            Assert(fixture.Runtime.Read().Ready);
        });
        Run("Elden Ring backend writes only the three four-byte current resource fields", () =>
        {
            using var fixture = new MemoryFixture();
            var state = fixture.Runtime.Refill(fixture.Runtime.Read(), true, true, true);
            Assert(state.Health == 100 && state.FP == 80 && state.Stamina == 90);
            Assert(fixture.Memory.Writes.Select(write => write.Address).SequenceEqual(new[] { MemoryFixture.Data + 0x138, MemoryFixture.Data + 0x148, MemoryFixture.Data + 0x154 }));
            Assert(fixture.Memory.Writes.All(write => write.Bytes.Length == 4));
            Assert(fixture.Memory.Writes.Select(write => BitConverter.ToInt32(write.Bytes)).SequenceEqual(new[] { 100, 80, 90 }));
            Assert(fixture.Memory.Checks >= 8);
            Assert(state.MaxHealth == 100 && state.MaxFP == 80 && state.MaxStamina == 90);
        });
        Run("Elden Ring backend does not write resources that are already full", () =>
        {
            using var fixture = new MemoryFixture();
            fixture.SetResources(100, 100, 80, 80, 90, 90);
            fixture.Runtime.Refill(fixture.Runtime.Read(), true, true, true);
            Assert(fixture.Memory.Writes.Count == 0);
        });
        Run("Elden Ring backend requires positive offline flags and empty network state", () =>
        {
            Action<MemoryFixture>[] blockers = [
                f => f.Memory.Put(MemoryFixture.Game + 0xBC8, [1]),
                f => f.Memory.Put(MemoryFixture.Game + 0xBC8, [255]),
                f => f.Memory.Put(MemoryFixture.Game + 0xBC9, [2]),
                f => f.Memory.Put(MemoryFixture.Game + 0xBCA, [1]),
                f => f.Memory.Put(MemoryFixture.Game + 0xBCA, [255]),
                f => f.Memory.Int(MemoryFixture.Session + 0xC, 1),
                f => f.Memory.Int(MemoryFixture.Session + 0xC, 3),
                f => f.Memory.Int(MemoryFixture.Session + 0x10, 6),
                f => f.Memory.Int(MemoryFixture.Session + 0x10, -1),
                f => f.Memory.Ptr(MemoryFixture.Session + 0x80, 0xA00000),
                f => f.Memory.Ptr(MemoryFixture.Session + 0x60, 0),
                f => f.Memory.Int(MemoryFixture.Steam + 0x734, 1),
                f => f.Memory.Int(MemoryFixture.Steam + 0x734, -1)
            ];
            foreach (var block in blockers)
            {
                using var fixture = new MemoryFixture();
                var safe = fixture.Runtime.Read();
                block(fixture);
                Fails(() => fixture.Runtime.Read());
                Fails(() => fixture.Runtime.Refill(safe, true, true, true));
                Assert(fixture.Memory.Writes.Count == 0);
            }
        });
        Run("Elden Ring backend checks environment before reading character memory", () =>
        {
            using var fixture = new MemoryFixture();
            fixture.Memory.EnvironmentError = true;
            Fails(() => fixture.Runtime.Read());
            Assert(fixture.Memory.Reads == 0 && fixture.Memory.Writes.Count == 0);
        });
        Run("Elden Ring backend rejects missing global state and incomplete reads", () =>
        {
            foreach (var rva in new[] { EldenRingProfile.GameRva, EldenRingProfile.SessionRva })
            {
                using var fixture = new MemoryFixture();
                fixture.Memory.Ptr(MemoryFixture.Image + rva, 0);
                Fails(() => fixture.Runtime.Read());
                Assert(fixture.Memory.Writes.Count == 0);
            }
            using var missing = new MemoryFixture();
            missing.Memory.Remove(MemoryFixture.Data + 0x138);
            Fails(() => missing.Runtime.Read());
            Assert(missing.Memory.Writes.Count == 0);
        });
        Run("Elden Ring backend blocks death and waits for absent player data", () =>
        {
            using var fixture = new MemoryFixture();
            fixture.SetResources(0, 100, 0, 80, 0, 90);
            var dead = fixture.Runtime.Read();
            Assert(!dead.Ready);
            Fails(() => fixture.Runtime.Refill(dead, false, true, true));
            fixture.Memory.Ptr(MemoryFixture.World + 0x1E508, 0);
            Assert(!fixture.Runtime.Read().Ready);
            fixture.Memory.Ptr(MemoryFixture.Image + EldenRingProfile.WorldRva, 0);
            Assert(!fixture.Runtime.Read().Ready);
            Assert(fixture.Memory.Writes.Count == 0);
        });
        Run("Elden Ring backend rejects wrong local player, vtable and owner pointers", () =>
        {
            Action<MemoryFixture>[] corruptions = [
                f => f.Memory.Ptr(MemoryFixture.Entries, 0xB00000),
                f => f.Memory.Ptr(MemoryFixture.Player, MemoryFixture.Image + EldenRingProfile.PlayerVtableRva + 8),
                f => f.Memory.Ptr(MemoryFixture.Data + 8, 0xB00000),
                f => f.Memory.Ptr(MemoryFixture.Image + EldenRingProfile.WorldRva, 3),
                f => f.Memory.Ptr(MemoryFixture.Image + EldenRingProfile.WorldRva, 0x10001),
                f => f.Memory.Ptr(MemoryFixture.Image + EldenRingProfile.WorldRva, 0x0000800000000000)
            ];
            foreach (var corrupt in corruptions)
            {
                using var fixture = new MemoryFixture();
                corrupt(fixture);
                Fails(() => fixture.Runtime.Read());
                Assert(fixture.Memory.Writes.Count == 0);
            }
        });
        Run("Elden Ring backend rejects invalid ranges for each resource", () =>
        {
            foreach (var offset in new[] { 0x138, 0x148, 0x154 })
            foreach (var pair in new[] { (-1, 100), (101, 100), (0, 0), (1, 100001) })
            {
                using var fixture = new MemoryFixture();
                fixture.Memory.Int(MemoryFixture.Data + offset, pair.Item1);
                fixture.Memory.Int(MemoryFixture.Data + offset + 4, pair.Item2);
                Fails(() => fixture.Runtime.Read());
                Assert(fixture.Memory.Writes.Count == 0);
            }
        });
        Run("Elden Ring backend rereads current maxima instead of using stale status values", () =>
        {
            using var fixture = new MemoryFixture();
            var before = fixture.Runtime.Read();
            fixture.Memory.Int(MemoryFixture.Data + 0x13C, 200);
            var after = fixture.Runtime.Refill(before, true, false, false);
            Assert(after.Health == 200 && after.MaxHealth == 200);
            Assert(fixture.Memory.Writes.Single().Address == MemoryFixture.Data + 0x138);
        });
        Run("Elden Ring backend context change before write rejects every resource", () =>
        {
            using var fixture = new MemoryFixture();
            var before = fixture.Runtime.Read();
            fixture.ChangeResourceOwner();
            Fails(() => fixture.Runtime.Refill(before, true, true, true));
            Assert(fixture.Memory.Writes.Count == 0);
        });
        Run("Elden Ring backend stops after first write if character context changes", () =>
        {
            using var fixture = new MemoryFixture();
            var before = fixture.Runtime.Read();
            fixture.Memory.AfterWrite = fixture.ChangeResourceOwner;
            Fails(() => fixture.Runtime.Refill(before, true, true, true));
            Assert(fixture.Memory.Writes.Count == 1 && fixture.Memory.Writes[0].Address == MemoryFixture.Data + 0x138);
        });
        Run("Elden Ring backend stops after first write if online state appears", () =>
        {
            using var fixture = new MemoryFixture();
            var before = fixture.Runtime.Read();
            fixture.Memory.AfterWrite = () => fixture.Memory.Put(MemoryFixture.Game + 0xBC8, [1]);
            Fails(() => fixture.Runtime.Refill(before, true, true, true));
            Assert(fixture.Memory.Writes.Count == 1 && fixture.Memory.Writes[0].Address == MemoryFixture.Data + 0x138);
        });
        Run("Elden Ring backend propagates failed writes without writing remaining resources", () =>
        {
            using var fixture = new MemoryFixture();
            var before = fixture.Runtime.Read();
            fixture.Memory.WriteError = true;
            Fails(() => fixture.Runtime.Refill(before, true, true, true));
            Assert(fixture.Memory.Writes.Count == 0 && fixture.Runtime.Read().Health == 50);
        });
        Run("Elden Ring profile verifies both required package hashes", () =>
        {
            using var fixture = new Fixture();
            fixture.CreatePackage();
            EldenRingProfile.Load(fixture.Package);
            foreach (var name in new[] { "profile.json", "ThirdPartyNotices.txt" })
            {
                fixture.CreatePackage();
                File.AppendAllText(Path.Combine(fixture.Package, name), "corruption");
                FailsIo(() => EldenRingProfile.Load(fixture.Package));
            }
        });
        Run("Elden Ring profile rejects missing duplicate and escaping package paths", () =>
        {
            using var fixture = new Fixture();
            foreach (var paths in new[] { new[] { "profile.json" }, new[] { "profile.json", "profile.json", "ThirdPartyNotices.txt" }, new[] { "../outside.json", "ThirdPartyNotices.txt" }, Array.Empty<string>() })
            {
                fixture.CreatePackage();
                fixture.Manifest(paths);
                FailsIo(() => EldenRingProfile.Load(fixture.Package));
            }
        });
        Run("Elden Ring malformed metadata becomes a supported IO failure", () =>
        {
            using var fixture = new Fixture();
            foreach (var malformed in new[] { "{}", "[]", "{", "{\"schemaVersion\":\"bad\"}" })
            {
                fixture.CreatePackage();
                File.WriteAllText(Path.Combine(fixture.Package, "package.json"), malformed);
                FailsIo(() => EldenRingProfile.Load(fixture.Package));
                fixture.CreatePackage();
                File.WriteAllText(Path.Combine(fixture.Package, "profile.json"), malformed);
                fixture.Manifest();
                FailsIo(() => EldenRingProfile.Load(fixture.Package));
            }
        });
        Run("Elden Ring profile refuses another version and false gameplay verification claims", () =>
        {
            using var fixture = new Fixture();
            foreach (var version in new[] { "2.7.1.1", "2.7.2.0", "2.7.1", "" })
            {
                fixture.CreatePackage(version);
                FailsIo(() => EldenRingProfile.Load(fixture.Package));
            }
            fixture.CreatePackage(gameplayVerified: true);
            FailsIo(() => EldenRingProfile.Load(fixture.Package));
            fixture.CreatePackage();
            fixture.Manifest(version: "0.2.0");
            FailsIo(() => EldenRingProfile.Load(fixture.Package));
        });
        Run("Elden Ring executable gate accepts exact WW version and rejects JP or future layouts", () =>
        {
            using var fixture = new Fixture();
            fixture.CreatePackage();
            var profile = EldenRingProfile.Load(fixture.Package);
            fixture.VersionedExecutable(2, 7, 1, 0);
            profile.VerifyExecutable(fixture.Executable);
            foreach (var version in new[] { (2, 7, 1, 1), (2, 7, 2, 0), (2, 7, 0, 0), (1, 17, 1, 0) })
            {
                fixture.VersionedExecutable(version.Item1, version.Item2, version.Item3, version.Item4);
                FailsIo(() => profile.VerifyExecutable(fixture.Executable));
            }
            FailsIo(() => profile.VerifyExecutable(Path.Combine(fixture.Game, "Game", "start_protected_game.exe")));
        });
        Console.WriteLine($"Elden Ring tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static EldenRingState Live(string context = "fixture-character") => new(true, context, 50, 100, 20, 80, 30, 90);
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void AssertOff(TrainerStatus status) => Assert(status.Enabled.Values.All(enabled => !enabled));
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation rejection."); }
    private static void FailsIo(Action action) { try { action(); } catch (IOException) { return; } throw new Exception("Expected IO rejection."); }
    private static void Run(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
    }

    private sealed class FakeRuntime : EldenRingSession.IRuntime
    {
        public EldenRingState State = Live();
        public bool ReadError, RefillError;
        public int Disposals;
        public Func<EldenRingState, EldenRingState>? AfterRefill;
        public List<(bool Health, bool FP, bool Stamina)> Writes { get; } = [];
        public EldenRingState Read() => ReadError ? throw new IOException("fixture read failed") : State;
        public EldenRingState Refill(EldenRingState expected, bool health, bool fp, bool stamina)
        {
            if (RefillError) throw new IOException("fixture write failed");
            Writes.Add((health, fp, stamina));
            State = State with { Health = health ? State.MaxHealth : State.Health, FP = fp ? State.MaxFP : State.FP, Stamina = stamina ? State.MaxStamina : State.Stamina };
            if (AfterRefill != null) State = AfterRefill(State);
            return State;
        }
        public void Dispose() => Disposals++;
    }

    private sealed class MemoryFixture : IDisposable
    {
        public const long Image = 0x140000000, Game = 0x100000, Session = 0x200000, Steam = 0x300000,
            World = 0x400000, Player = 0x500000, Entries = 0x600000, Modules = 0x700000, Data = 0x800000;
        public FakeMemory Memory { get; } = new();
        public EldenRingMemory Runtime { get; }
        public MemoryFixture()
        {
            Memory.Ptr(Image + EldenRingProfile.GameRva, Game);
            Memory.Ptr(Image + EldenRingProfile.SessionRva, Session);
            Memory.Ptr(Image + EldenRingProfile.WorldRva, World);
            Memory.Put(Game + 0xBC8, [0, 0, 0]);
            Memory.Int(Session + 0xC, 0);
            Memory.Int(Session + 0x10, 0);
            Memory.Ptr(Session + 0x78, 0);
            Memory.Ptr(Session + 0x80, 0);
            Memory.Ptr(Session + 0x60, Steam);
            Memory.Int(Steam + 0x734, 0);
            Memory.Ptr(World + 0x1E508, Player);
            Memory.Ptr(World + 0x10EF8, Entries);
            Memory.Ptr(Entries, Player);
            Memory.Ptr(Player, Image + EldenRingProfile.PlayerVtableRva);
            Memory.Ptr(Player + 0x190, Modules);
            Memory.Ptr(Modules, Data);
            Memory.Ptr(Data + 8, Player);
            SetResources(50, 100, 20, 80, 30, 90);
            Runtime = new EldenRingMemory(Memory, Image);
        }
        public void SetResources(int hp, int maxHp, int fp, int maxFp, int stamina, int maxStamina, long target = Data)
        {
            Memory.Put(target + 0x138, new byte[0x24]);
            Memory.Int(target + 0x138, hp); Memory.Int(target + 0x13C, maxHp);
            Memory.Int(target + 0x148, fp); Memory.Int(target + 0x14C, maxFp);
            Memory.Int(target + 0x154, stamina); Memory.Int(target + 0x158, maxStamina);
        }
        public void ChangeResourceOwner()
        {
            const long next = 0x900000;
            Memory.Ptr(Modules, next);
            Memory.Ptr(next + 8, Player);
            SetResources(25, 100, 10, 80, 15, 90, next);
        }
        public void Dispose() => Runtime.Dispose();
    }

    private sealed class FakeMemory : EldenRingMemory.IMemory
    {
        private readonly Dictionary<long, byte> bytes = [];
        public List<(long Address, byte[] Bytes)> Writes { get; } = [];
        public int Reads, Checks;
        public bool EnvironmentError, WriteError;
        public Action? AfterWrite;
        public void Put(long address, byte[] value) { for (var i = 0; i < value.Length; i++) bytes[address + i] = value[i]; }
        public void Int(long address, int value) => Put(address, BitConverter.GetBytes(value));
        public void Ptr(long address, long value) => Put(address, BitConverter.GetBytes(value));
        public void Remove(long address) => bytes.Remove(address);
        public byte[] Read(long address, int length)
        {
            Reads++;
            var result = new byte[length];
            for (var i = 0; i < length; i++)
                if (!bytes.TryGetValue(address + i, out result[i])) throw new IOException("Fixture read outside initialized memory.");
            return result;
        }
        public void Write(long address, byte[] value)
        {
            if (WriteError) throw new IOException("Fixture write failed.");
            Writes.Add((address, value.ToArray()));
            Put(address, value);
            AfterWrite?.Invoke();
        }
        public void CheckEnvironment() { Checks++; if (EnvironmentError) throw new IOException("Fixture protected environment."); }
        public void Dispose() { }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-EldenRing-tests-" + Guid.NewGuid().ToString("N"));
        public string Game => Path.Combine(Root, "ELDEN RING");
        public string Package => Path.Combine(Root, "payload", "elden-ring");
        public string Executable => Path.Combine(Game, "Game", "eldenring.exe");
        public Fixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Executable)!);
            File.WriteAllText(Executable, "fixture, not executable");
        }
        public void CreatePackage(string fileVersion = "2.7.1.0", bool gameplayVerified = false)
        {
            Directory.CreateDirectory(Package);
            ModuleIO.WriteJson(Path.Combine(Package, "profile.json"), new {
                schemaVersion = 1, id = "elden-ring-ww-2.7.1.0-resources-v1", fileVersion,
                gameVersion = "1.17.1", gameplayVerified
            });
            File.WriteAllText(Path.Combine(Package, "ThirdPartyNotices.txt"), "Fixture notices.");
            Manifest();
        }
        public void Manifest(string[]? paths = null, string version = "0.1.0")
        {
            paths ??= ["profile.json", "ThirdPartyNotices.txt"];
            ModuleIO.WriteJson(Path.Combine(Package, "package.json"), new {
                schemaVersion = 1, game = "elden-ring", version,
                files = paths.Select(path => new { path, sha256 = File.Exists(Path.Combine(Package, path)) ? ModuleIO.Hash(Path.Combine(Package, path)) : new string('0', 64) })
            });
        }
        public void VersionedExecutable(int major, int minor, int build, int revision)
        {
            // Use our own assembly as a PE-resource fixture. This file is never launched and contains no game code.
            var bytes = File.ReadAllBytes(typeof(EldenRingModule).Assembly.Location);
            var matches = Enumerable.Range(0, bytes.Length - 52)
                .Where(i => bytes[i] == 0xBD && bytes[i + 1] == 0x04 && bytes[i + 2] == 0xEF && bytes[i + 3] == 0xFE).ToArray();
            Assert(matches.Length == 1);
            BitConverter.GetBytes((uint)((major << 16) | minor)).CopyTo(bytes, matches[0] + 8);
            BitConverter.GetBytes((uint)((build << 16) | revision)).CopyTo(bytes, matches[0] + 12);
            File.WriteAllBytes(Executable, bytes);
        }
        public void Dispose()
        {
            var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("HappyCheats-EldenRing-tests-", StringComparison.Ordinal))
                throw new IOException("Invalid fixture cleanup path.");
            Directory.Delete(resolved, true);
        }
    }
}
