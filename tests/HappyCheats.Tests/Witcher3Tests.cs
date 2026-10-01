using System.Globalization;
using HappyCheats.Core;

namespace HappyCheats.Tests;

internal static class Witcher3Tests
{
    public static int RunAll()
    {
        var failed = 0;
        void Check(string name, Action test) { try { test(); Console.WriteLine("PASS Witcher 3 " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL Witcher 3 " + name + ": " + ex.Message); } }
        void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
        void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
        var now = DateTime.UtcNow;
        var started = now.AddMinutes(-1);
        const string request = "0123456789abcdef0123456789abcdef";
        string Status(string session = "123-40.0", string ready = "1", string version = Witcher3Module.TrainerVersion, string time = "42.5", string ack = request) => $"[MODS]\r\nSnapshot=HCW3|1|{version}|{session}|{ready}|{time}|1|0|1|0|1200|100|10000|{ack}|1|applied\r\n";
        Witcher3Module.Snapshot Parse(string text) => Witcher3Module.ParseSnapshot(text, now, started, now);
        Check("fresh script acknowledgement and toggle state", () => {
            var state = Parse(Status());
            Assert(state.Status.Connected && state.Status.Ready && state.Success && state.Status.LastCommand == request);
            Assert(state.EngineTime == 42.5 && state.Status.Enabled["witcher3_health"] && !state.Status.Enabled["witcher3_stamina"]);
            Assert(state.Status.Values["Crowns"] == "10000" && state.Status.Values["available_witcher3_money"] == "True");
            Assert(Parse(Status().Replace("Snapshot=", "snapshot = ")).Status.Ready);
        });
        Check("heartbeat freshness tied to game lifetime", () => {
            Assert(Witcher3Module.ParseSnapshot(Status(), now.AddSeconds(-5), started, now).Status.Connected);
            Assert(!Witcher3Module.ParseSnapshot(Status(), started.AddMilliseconds(-1), started, now).Status.Connected);
            Assert(!Witcher3Module.ParseSnapshot(Status(), now.AddSeconds(3), started, now).Status.Connected);
        });
        Check("paused current-process state can queue controls without foreground or a newer heartbeat", () => {
            foreach (var background in new[] { false, true }) {
                var paused = Witcher3Module.ParseSnapshot(Status(), now.AddSeconds(-30), started, now, background);
                Assert(paused.Status.Connected && paused.Status.Ready && paused.WaitingForGame == background);
                Assert(paused.Modified == now.AddSeconds(-30));
                Assert(paused.Status.Values["witcher3_waiting_for_game"] == background.ToString());
                Assert(Witcher3Module.CreateCommand(paused.Status, paused, request, "witcher3_money", null).EndsWith(",witcher3_money,,42.500\r\n"));
                Assert(Witcher3Module.CreateCommand(paused.Status, paused, request, "witcher3_health", false).Contains(",0,42.500"));
                Assert(Witcher3Module.CreateCommand(paused.Status, paused, request, "disable_all", null).Contains(",disable_all,,"));
            }
        });
        Check("long pauses retain current-process state but reject old-process or malformed status", () => {
            var oldProcessStart = now.AddHours(-1);
            Assert(Witcher3Module.ParseSnapshot(Status(), now.AddMinutes(-30), oldProcessStart, now, background: true).Status.Connected);
            Assert(!Witcher3Module.ParseSnapshot(Status(), started.AddTicks(-1), started, now, background: true).Status.Connected);
            Assert(!Witcher3Module.ParseSnapshot(Status(), now.AddSeconds(3), started, now, background: true).Status.Connected);
            Assert(!Witcher3Module.ParseSnapshot(Status(version: "old"), now.AddSeconds(-30), started, now, background: true).Status.Connected);
        });
        Check("invalid protocol fails closed", () => {
            foreach (var value in new[] { Status(version:"0.0.0"), Status(ready:"yes"), Status(time:"NaN"), Status(time:"Infinity"), Status(time:"-1"), Status(session:"a,b\r\n"), Status(ack:"unknown"), Status() + Status(), new string('a',16385) })
                Assert(!Parse(value).Status.Connected);
        });
        Check("stale session cannot issue commands", () => {
            var old = Parse(Status()).Status;
            Reject(() => Witcher3Module.CreateCommand(old, Parse(Status(session:"987-50.0")), request, "witcher3_money", null));
            Reject(() => Witcher3Module.CreateCommand(old, new(TrainerStatus.Offline()), request, "witcher3_money", null));
        });
        Check("only declared actions and exact toggle semantics", () => {
            var state = Parse(Status());
            Reject(() => Witcher3Module.CreateCommand(state.Status, state, request, "witcher3_money", true));
            Reject(() => Witcher3Module.CreateCommand(state.Status, state, request, "witcher3_health", null));
            Reject(() => Witcher3Module.CreateCommand(state.Status, state, request, "exec arbitrary", null));
            Reject(() => Witcher3Module.CreateCommand(state.Status, state, "bad\n", "witcher3_money", null));
            Reject(() => Witcher3Module.CreateCommand(state.Status, state, request, "disable_all", false));
            Assert(Witcher3Module.CreateCommand(state.Status, state, request, "witcher3_money", null).EndsWith(",witcher3_money,,42.500\r\n"));
        });
        Check("menus reject changes but allow disabling", () => {
            var state = Parse(Status(ready:"0"));
            Reject(() => Witcher3Module.CreateCommand(state.Status, state, request, "witcher3_money", null));
            Reject(() => Witcher3Module.CreateCommand(state.Status, state, request, "witcher3_health", true));
            Assert(Witcher3Module.CreateCommand(state.Status, state, request, "witcher3_health", false).Contains(",0,42.500"));
            Assert(Witcher3Module.CreateCommand(state.Status, state, request, "disable_all", null).Contains(",disable_all,,"));
        });
        Check("paused click publishes immediately and accepts background acknowledgement", () => {
            var flow = new DispatchHarness();
            flow.Steps.Add(h => {
                Assert(h.Writes.Count == 1 && h.Renewals.Count == 1);
                Assert(h.Renewals[0].WaitingForGame && h.Renewals[0].EngineTime == 100);
                h.Current = h.State(waiting: true, time: 100.25, ack: DispatchHarness.Request);
            });
            flow.Send().GetAwaiter().GetResult();
            Assert(flow.Writes.Count == 1 && flow.Clears == 1 && flow.Delays == 1);
            Assert(flow.Writes[0].EndsWith(",witcher3_money,,100.000\r\n", StringComparison.Ordinal));
        });
        Check("paused command keeps its lease and waits up to sixty wall seconds without replay", () => {
            var flow = new DispatchHarness { StepDuration = TimeSpan.FromSeconds(15) };
            flow.Steps.Add(h => Assert(h.Writes.Count == 1 && h.Renewals.Count == 1));
            Expect<TimeoutException>(flow.Send());
            Assert(flow.Elapsed == TimeSpan.FromSeconds(60));
            Assert(flow.Writes.Count == 1 && flow.Renewals.Count == 4 && flow.Clears == 1);
        });
        Check("running game without acknowledgement times out after eight engine seconds", () => {
            var flow = new DispatchHarness();
            flow.Steps.Add(h => h.Current = h.State(waiting: true, time: 104));
            flow.Steps.Add(h => h.Current = h.State(waiting: true, time: 108));
            Expect<TimeoutException>(flow.Send());
            Assert(flow.Writes.Count == 1 && flow.Clears == 1 && flow.Delays == 2);
        });
        Check("unreadable command files report the installer repair instead of waiting", () => {
            var flow = new DispatchHarness();
            flow.Steps.Add(h => h.Current = h.State(waiting: true, time: 101) with { Result = "bridge_unavailable" });
            try { flow.Send().GetAwaiter().GetResult(); throw new Exception("Expected command-file error"); }
            catch (IOException error) { Assert(error.Message.Contains("Install / update", StringComparison.Ordinal)); }
            Assert(flow.Writes.Count == 1 && flow.Clears == 1 && flow.Delays == 1);
        });
        Check("invalid controls fail before waiting or publishing", () => {
            var unknown = new DispatchHarness(); Expect<ArgumentException>(unknown.Send("unknown", null));
            var toggle = new DispatchHarness(); Expect<ArgumentException>(toggle.Send("witcher3_health", null));
            Assert(unknown.Delays == 0 && unknown.Writes.Count == 0 && unknown.Clears == 0);
            Assert(toggle.Delays == 0 && toggle.Writes.Count == 0 && toggle.Clears == 0);
        });
        Check("late acknowledgement after scheduler suspension remains an uncertain timeout", () => {
            var flow = new DispatchHarness { StepDuration = TimeSpan.FromSeconds(61) };
            flow.Steps.Add(h => h.Current = h.State(waiting: true, time: 102, ack: DispatchHarness.Request));
            Expect<TimeoutException>(flow.Send());
            Assert(flow.Writes.Count == 1 && flow.Clears == 1 && flow.Delays == 1);
        });
        Check("transient status gaps and unrelated acknowledgements do not resend", () => {
            var flow = new DispatchHarness();
            flow.Steps.Add(h => h.Current = new(TrainerStatus.Offline()));
            flow.Steps.Add(h => { Assert(h.Writes.Count == 1 && h.Clears == 0); h.Current = h.State(waiting: true, time: 102, ack: new string('f', 32)); });
            flow.Steps.Add(h => { Assert(h.Writes.Count == 1 && h.Clears == 0); h.Current = h.State(waiting: true, time: 103, ack: DispatchHarness.Request); });
            flow.Send().GetAwaiter().GetResult();
            Assert(flow.Writes.Count == 1 && flow.Clears == 1 && flow.Delays == 3);
        });
        Check("session change after publication clears the command without replay", () => {
            var flow = new DispatchHarness();
            flow.Steps.Add(h => h.Current = h.State(waiting: true, time: 102, session: "987-50.0", ack: DispatchHarness.Request));
            Expect<IOException>(flow.Send());
            Assert(flow.Writes.Count == 1 && flow.Clears == 1 && flow.Delays == 1);
        });
        Check("trainer rejection clears the mailbox and never repeats a one-shot", () => {
            var flow = new DispatchHarness();
            flow.Steps.Add(h => h.Current = h.State(waiting: true, time: 102, ack: DispatchHarness.Request, success: false));
            Expect<InvalidOperationException>(flow.Send());
            Assert(flow.Writes.Count == 1 && flow.Clears == 1);
        });
        Check("uncertain publication failure is cleared without retry", () => {
            var flow = new DispatchHarness { WriteFailure = new IOException("Simulated failure after atomic mailbox publication") };
            Expect<IOException>(flow.Send());
            Assert(flow.Writes.Count == 1 && flow.Clears == 1 && flow.Delays == 0);
        });
        return failed;
    }

    private static void Expect<T>(Task task) where T : Exception
    {
        try { task.GetAwaiter().GetResult(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private sealed class DispatchHarness
    {
        public const string Request = "0123456789abcdef0123456789abcdef";
        private static readonly DateTime Epoch = new(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc);
        public List<Action<DispatchHarness>> Steps { get; } = new();
        public List<string> Writes { get; } = new();
        public List<Witcher3Module.Snapshot> Renewals { get; } = new();
        public Witcher3Module.Snapshot Initial { get; }
        public Witcher3Module.Snapshot Current { get; set; }
        public TimeSpan StepDuration { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan Elapsed { get; private set; }
        public int Delays { get; private set; }
        public int Clears { get; private set; }
        public Exception? WriteFailure { get; init; }

        public DispatchHarness()
        {
            Initial = State(waiting: true, time: 100);
            Current = Initial;
        }

        public Witcher3Module.Snapshot State(bool waiting, double time, string session = "123-40.0", bool ready = true, string ack = "", bool success = true)
        {
            var now = Epoch + Elapsed;
            var text = "[MODS]\r\nSnapshot=HCW3|1|" + Witcher3Module.TrainerVersion + "|" + session + "|" + (ready ? "1" : "0") + "|"
                + time.ToString(CultureInfo.InvariantCulture) + "|0|0|0|0|1200|100|10000|" + ack + "|" + (success ? "1|applied" : "0|rejected") + "\r\n";
            return Witcher3Module.ParseSnapshot(text, now, Epoch.AddHours(-1), now, background: waiting);
        }

        public Task Send(string feature = "witcher3_money", bool? enabled = null) => Witcher3Module.DispatchAsync(Initial.Status, Initial, Request, feature, enabled,
            () => Current,
            snapshot => Renewals.Add(snapshot),
            command => { Writes.Add(command); if (WriteFailure != null) throw WriteFailure; },
            () => Clears++,
            () => {
                Elapsed += StepDuration;
                Delays++;
                if (Delays > 1000) throw new Exception("Dispatch did not obey its bounded timeout.");
                if (Delays <= Steps.Count) Steps[Delays - 1](this);
                return Task.CompletedTask;
            },
            () => Elapsed);
    }
}
