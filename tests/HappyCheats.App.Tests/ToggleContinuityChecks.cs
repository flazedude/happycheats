using HappyCheats.App;
using HappyCheats.Core;
using System.Reflection;
using System.IO;
using System.Windows.Threading;

namespace HappyCheats.App.Tests;

internal static class ToggleContinuityChecks
{
    internal static object Run(MainWindow main, string root)
    {
        var checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception("Toggle continuity: " + message); }
        TrainerStatus Status(string session, string? active = null, bool ready = true, string? unavailable = null) =>
            new(true, ready, "fixture", session, active == null ? new Dictionary<string, bool>() : new() { [active] = true },
                unavailable == null ? new Dictionary<string, string>() : new() { ["available_" + unavailable] = "false" });
        var catalog = ModuleCatalog.Create(root).Select(x => x.Definition).Append(new("future", "Future game", "", "", "", "",
            [new("future_toggle", "Toggle", "", "F1", true), new("future_xp", "Add XP", "", "F2")])).ToArray();
        foreach (var game in catalog)
        foreach (var feature in game.Features.Where(x => x.IsToggle))
        {
            var state = new ToggleContinuity();
            state.Observe(game.Features, Status("before", feature.Id));
            Check(state.Requested(feature.Id), game.Id + " failed to adopt confirmed On");
            state.Observe(game.Features, Status("before", ready: false));
            Check(state.Requested(feature.Id), game.Id + " lost On during loading");
            Check(state.Next(game.Features, Status("before", ready: false)) == null, "Not-ready session received an automatic command");
            state.Observe(game.Features, Status("after"));
            var next = state.Next(game.Features, Status("after"));
            Check(next?.Id == feature.Id && next.IsToggle, game.Id + " failed to restore its toggle");
            Check(state.Next(game.Features, Status("after")) == null, "Failed restoration could run in an unbounded loop");
            state.Observe(game.Features, Status("after", feature.Id));
            Check(state.Next(game.Features, Status("after", feature.Id)) == null, "Confirmed On was redundantly applied");
            state.Observe(game.Features, Status("after"));
            Check(!state.Requested(feature.Id), "Stable native hotkey Off was overridden");
            state.Request(feature.Id, true);
            state.Observe(game.Features, TrainerStatus.Offline());
            state.Request(feature.Id, false);
            state.Observe(game.Features, Status("return"));
            Check(state.Next(game.Features, Status("return")) == null && !state.Requested(feature.Id), "Off while loading did not cancel restoration");
            state.Request(feature.Id, true);
            state.CancelAll(game.Features);
            state.Observe(game.Features, Status("another", feature.Id));
            Check(!state.Requested(feature.Id) && state.Next(game.Features, Status("another")) == null, "Disable all was undone by stale confirmation");
            state.Request(feature.Id, true);
            state.Observe(game.Features, Status("another", unavailable: feature.Id));
            Check(state.Next(game.Features, Status("another", unavailable: feature.Id)) == null, "Unavailable feature was automatically dispatched");
            state.Observe(game.Features, Status("another"));
            Check(state.Next(game.Features, Status("another"))?.Id == feature.Id, "Available feature did not resume");
            state.Observe(game.Features, Status("replacement"));
            Check(state.Next(game.Features, Status("replacement"))?.Id == feature.Id, "New validated session could not retry a failed restore");
            state.CancelAll(game.Features);
            foreach (var oneShot in game.Features.Where(x => !x.IsToggle)) state.Request(oneShot.Id, true);
            state.Observe(game.Features, Status("last"));
            Check(state.Next(game.Features, Status("last")) == null, "A one-shot action could be automatically repeated");
            var row = new FeatureItem(feature);
            foreach (var language in new[] { "en", "zh" })
            {
                Localization.Language = language;
                row.Update(TrainerStatus.Offline(), true, requestedOn: true);
                Check(row.ActionLabel == Localization.Text("turnOff"), "Pending toggle cannot be explicitly turned off");
                Check(row.StateLabel == Localization.Choose("On · waiting to restore", "已开启 · 等待恢复"), "Pending intent was mislabeled as a confirmed effect");
            }
        }
        Localization.Language = "en";
        var original = main.Selected?.Id;
        var context = SynchronizationContext.Current;
        var fixture = new Workflow();
        var item = new GameItem(fixture) { Installation = fixture.DiscoverAsync().Result, Status = fixture.Status };
        main.Games.Add(item);
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(main.Dispatcher));
            main.SelectGame(item.Id);
            Invoke("SendFeatureAsync", fixture.Definition.Features[0]);
            Check(fixture.Calls.SequenceEqual(new[] { "toggle:True" }), "Main window did not dispatch the explicit enable");
            fixture.Status = Status("load", ready: false); Invoke("PollAsync");
            Check(main.Features[0].ActionLabel == Localization.Text("turnOff"), "Loading UI lost its Off action");
            fixture.Status = Status("loaded"); Invoke("PollAsync");
            Check(fixture.Calls.SequenceEqual(new[] { "toggle:True", "toggle:True" }), "New session did not restore through the module path");
            fixture.Status = Status("load2", ready: false); Invoke("PollAsync");
            Invoke("SendFeatureAsync", fixture.Definition.Features[0]);
            var calls = fixture.Calls.Count;
            fixture.Status = Status("loaded2"); Invoke("PollAsync");
            Check(fixture.Calls.Count == calls && !item.Toggles.Requested("toggle"), "Off during loading was undone by automatic restoration");
            Invoke("SendFeatureAsync", fixture.Definition.Features[0]);
            fixture.Status = Status("failure"); fixture.Fail = true; Invoke("PollAsync");
            calls = fixture.Calls.Count; Invoke("PollAsync");
            Check(fixture.Calls.Count == calls, "Restoration failure was retried without a state change");
            Check(main.Message.Contains("Synthetic restore failure"), "Failed restoration did not produce an actionable error");
            Check(main.Features[0].StateLabel.Contains("waiting to restore"), "Failed restoration discarded the requested On state");
            fixture.Fail = false;
            Invoke("SendFeatureAsync", new FeatureDefinition("disable_all", "Disable all", "", ""));
            calls = fixture.Calls.Count;
            fixture.Status = Status("last-session"); Invoke("PollAsync");
            Check(fixture.Calls.Count == calls, "Disable all failed to cancel a queued restore");
            Check(!fixture.Calls.Any(x => x.StartsWith("xp:")), "A one-shot action was replayed by the shared client");
        }
        finally
        {
            main.Games.Remove(item);
            if (original != null) main.SelectGame(original);
            SynchronizationContext.SetSynchronizationContext(context);
        }
        Console.WriteLine($"Toggle continuity: {catalog.Length} catalog/future games, {checks} assertions; module dispatch, cancellation and bounded failure checks passed.");
        return new { type = "toggle-continuity", games = catalog.Length, assertions = checks, gameplay = false };

        void Invoke(string name, params object[] arguments)
        {
            var task = (Task)typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, arguments)!;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                var frame = new DispatcherFrame();
                main.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(1);
            }
            Check(task.IsCompleted, "Synthetic workflow timed out");
            task.GetAwaiter().GetResult();
        }
    }

    private sealed class Workflow : IGameModule
    {
        internal bool Fail;
        internal List<string> Calls = new();
        internal TrainerStatus Status = new(true, true, "fixture", "initial", new Dictionary<string, bool>(), new Dictionary<string, string>());
        public GameDefinition Definition { get; } = new("continuity-fixture", "Continuity fixture", "", "", "", "",
            [new("toggle", "Toggle", "", "F1", true), new("xp", "Add XP", "", "F2")]);
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.FromResult(new GameInstallation(Path.GetTempPath(), "fixture", true, true, false, true, "fixture"));
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.FromResult(Status);
        public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
        {
            Calls.Add(featureId + ":" + enabled);
            if (Fail) throw new InvalidOperationException("Synthetic restore failure; turn Off and On to retry.");
            if (!Status.Ready) throw new InvalidOperationException("Load the synthetic session first.");
            Status = Status with { Enabled = featureId == "disable_all" ? new Dictionary<string, bool>() : new() { [featureId] = enabled == true } };
            return Task.CompletedTask;
        }
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new Exception("Unexpected fixture install");
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new Exception("Unexpected fixture removal");
        public Task LaunchAsync(GameInstallation installation) => throw new Exception("A test must never launch a game");
    }
}
