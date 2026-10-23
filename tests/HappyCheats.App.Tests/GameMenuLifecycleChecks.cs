using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HappyCheats.App;
using HappyCheats.Core;

namespace HappyCheats.App.Tests;

internal static class GameMenuLifecycleChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static object Run(string root)
    {
        var settings = Path.Combine(Path.GetTempPath(), "HappyCheats.App.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        var language = Localization.Language;
        var theme = ThemeManager.IsLight ? "light" : "dark";
        var first = new FixtureModule("chinese-parents");
        var second = new FixtureModule("laundry");
        // Deliberately do not Show or InitializeAsync this MainWindow. Its timers,
        // game discovery, keyboard hook, update checks and launch paths never run.
        var main = new MainWindow(dataRoot: root, settingsPath: settings);
        var host = new Window { Width = 1280, Height = 720, Left = -32000, Top = -32000,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            Drain(main);
            main.Games.Clear();
            var firstGame = Item(first);
            var secondGame = Item(second);
            main.Games.Add(firstGame);
            main.Games.Add(secondGame);
            Drain(main);
            main.SetLanguage("en", persist: false);
            main.SelectGame(firstGame.Id);
            Drain(main);
            Check(ReferenceEquals(main.Selected, firstGame), "Lifecycle fixture did not select the inert first module.");
            Set(main, "_gameWindow", new WindowInteropHelper(host).EnsureHandle());
            Invoke(main, "ToggleGameOverlay");
            var overlay = (GameOverlay)Get(main, "_gameOverlay")!;
            ShowOffscreen(overlay);

            // Poll starts first, but its old snapshot arrives after a real menu
            // command has completed and has already published confirmed state.
            var oldStatus = first.Status;
            var delayed = first.DelayNextRead();
            var poll = InvokeTask(main, "PollAsync");
            Check(!poll.IsCompleted && first.Reads == 1, "The delayed trainer poll did not reach the fake status boundary.");
            ClickFeature(main, overlay, "test_toggle");
            Check(first.Commands.SequenceEqual(new[] { "test_toggle:True" }) && firstGame.Status.Enabled["test_toggle"],
                "Menu toggle did not complete through the selected IGameModule.");
            var confirmed = firstGame.Status;
            delayed.SetResult(oldStatus);
            Finish(main, poll);
            Check(ReferenceEquals(firstGame.Status, confirmed) && firstGame.Status.Enabled["test_toggle"],
                "An old status poll overwrote the command's confirmed result after busy cleared.");
            Check(!(bool)Get(main, "_polling")!, "Discarding an old poll left polling permanently active.");

            var number = main.Features.Single(feature => feature.Definition.Id == "test_number");
            number.NumberText = "42";
            ClickFeature(main, overlay, number.Definition.Id);
            Check(first.Numbers.SequenceEqual(new[] { 42m }) && number.ConfirmedNumberLabel.EndsWith("42", StringComparison.Ordinal),
                "Menu numeric input did not use IFeatureValueModule and refresh its confirmed value.");
            number.NumberText = "1000";
            ClickFeature(main, overlay, number.Definition.Id);
            Check(first.Numbers.Count == 1 && number.NumberErrorVisibility == Visibility.Visible,
                "Out-of-range menu input reached the game value module.");

            var choice = main.Features.Single(feature => feature.Definition.Id == "test_choice").Choices[1];
            var choiceButton = Descendants(overlay).OfType<Button>().Single(button => ReferenceEquals(button.Tag, choice));
            main.Dispatcher.Invoke(() => choiceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            Drain(main);
            Check(first.Choices.SequenceEqual(new[] { "20" }) && choice.IsSelected,
                "Menu choice did not use IFeatureChoiceModule and refresh its confirmed selection.");

            // A third choice uses the same module-selection path. Until its
            // readback arrives, the old confirmed selection must remain visible.
            var hundred = choice.Owner.Choices.Single(item => item.Definition.Value == "100");
            var commandsBeforeChoice = first.Commands.Count;
            var delayedChoice = first.DelayNextRead();
            var hundredSelection = InvokeTask(main, "SendChoiceAsync", hundred);
            Check(!hundredSelection.IsCompleted && first.Choices.SequenceEqual(new[] { "20", "100" })
                && choice.IsSelected && !hundred.IsSelected && !hundred.CanUse,
                "A pending 100x selection replaced confirmed state or left menu controls enabled.");
            Check(first.Commands.Count == commandsBeforeChoice && !first.Status.Enabled["test_choice"],
                "Selecting 100x implicitly enabled the collection toggle.");
            delayedChoice.SetResult(first.Status);
            Finish(main, hundredSelection);
            Check(hundred.IsSelected && !choice.IsSelected && hundred.CanUse
                && !firstGame.Status.Enabled["test_choice"],
                "Confirmed 100x selection failed to update or enabled collection.");
            first.NextSelectionError = new IOException("Rejected fixture multiplier");
            Finish(main, InvokeTask(main, "SendChoiceAsync", choice));
            Check(hundred.IsSelected && !choice.IsSelected && main.Message == "Rejected fixture multiplier"
                && first.Commands.Count == commandsBeforeChoice,
                "A rejected selection failed to retain confirmed 100x state or dispatched a toggle.");

            var speed = main.Features.Single(feature => feature.Definition.Id == "test_speed");
            var fiveSpeed = speed.Choices.Single(item => item.Definition.Value == "5");
            first.Selections.Clear();
            var delayedSpeed = first.DelayNextRead();
            var speedSelection = InvokeTask(main, "SendChoiceAsync", fiveSpeed);
            Check(!speedSelection.IsCompleted && speed.Choices[0].IsSelected && !fiveSpeed.IsSelected
                && hundred.IsSelected && !fiveSpeed.CanUse && !hundred.CanUse,
                "A pending speed selection changed confirmed values or left either selector enabled.");
            Check(first.Selections.SequenceEqual(new[] { ("test_speed", "5") })
                && first.Commands.Count == commandsBeforeChoice && !first.Status.Enabled["test_speed"]
                && first.Status.Values["test_choice"] == "100",
                "Off-state speed selection used the wrong feature ID, enabled its toggle, or modified collection.");
            delayedSpeed.SetResult(first.Status);
            Finish(main, speedSelection);
            Check(fiveSpeed.IsSelected && hundred.IsSelected && !firstGame.Status.Enabled["test_speed"],
                "Confirmed 5x speed selection did not remain independent while off.");
            ClickFeature(main, overlay, speed.Definition.Id);
            var commandsWhileRunning = first.Commands.Count;
            Check(firstGame.Status.Enabled["test_speed"] && first.Commands.Last() == "test_speed:True",
                "Speed toggle did not use its separate game-module action.");
            foreach (var selectedSpeed in new[] { "10", "3" })
            {
                var speedChoice = speed.Choices.Single(item => item.Definition.Value == selectedSpeed);
                var button = Descendants(overlay).OfType<Button>().Single(item => ReferenceEquals(item.Tag, speedChoice));
                main.Dispatcher.Invoke(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                Drain(main);
                Check(first.Selections.Last() == ("test_speed", selectedSpeed) && speedChoice.IsSelected
                    && firstGame.Status.Enabled["test_speed"] && !firstGame.Status.Enabled["test_choice"]
                    && first.Commands.Count == commandsWhileRunning && hundred.IsSelected,
                    "Changing active run speed failed to confirm, retoggled speed, or changed collection state.");
            }

            // Selection invalidates both successful and failed in-flight polls.
            // Neither may replace another game's message, status or window target.
            var firstBeforeSelection = firstGame.Status;
            var lateResult = first.DelayNextRead();
            var selectionPoll = InvokeTask(main, "PollAsync");
            main.SelectGame(secondGame.Id);
            Drain(main);
            var selectedStatus = secondGame.Status;
            var selectedMessage = main.Message;
            var markerWindow = new WindowInteropHelper(host).Handle;
            Set(main, "_gameWindow", markerWindow);
            lateResult.SetResult(TrainerStatus.Offline("Stale first-game result"));
            Finish(main, selectionPoll);
            Check(ReferenceEquals(secondGame.Status, selectedStatus) && main.Message == selectedMessage
                && ReferenceEquals(firstGame.Status, firstBeforeSelection),
                "A prior game's status poll changed the selected game or reset confirmed first-game state.");
            Check((IntPtr)Get(main, "_gameWindow")! == markerWindow, "A prior game's poll replaced the selected window target.");

            main.SelectGame(firstGame.Id);
            Drain(main);
            var lateFailure = first.DelayNextRead();
            var failedPoll = InvokeTask(main, "PollAsync");
            main.SelectGame(secondGame.Id);
            Drain(main);
            selectedMessage = main.Message;
            Set(main, "_gameWindow", markerWindow);
            lateFailure.SetException(new IOException("Stale first-game inspection failure"));
            Finish(main, failedPoll);
            Check(main.Message == selectedMessage && secondGame.Status.Connected && firstGame.Status.Connected
                && (IntPtr)Get(main, "_gameWindow")! == markerWindow,
                "A prior game's failed poll cleared the current target or exposed its stale error.");

            // Retained callbacks from the old menu must not dispatch to a new game.
            Finish(main, InvokeTask(main, "SendOverlayFeatureAsync", number.Definition));
            Finish(main, InvokeTask(main, "SendChoiceAsync", choice));
            Check(second.Commands.Count == 0 && second.Numbers.Count == 0 && second.Choices.Count == 0,
                "A retained menu action dispatched an old game's feature to the new module.");

            // Window.Close is the lifecycle invoked by Alt+F4; recreate through
            // MainWindow's normal toggle rather than constructing the replacement.
            Invoke(main, "ToggleGameOverlay");
            overlay = (GameOverlay)Get(main, "_gameOverlay")!;
            ShowOffscreen(overlay);
            overlay.Close();
            Drain(main);
            Check(Get(main, "_gameOverlay") == null && !(bool)Get(main, "_overlayRequested")!,
                "Closing the menu retained a closed window or an open-menu request.");
            Invoke(main, "ToggleGameOverlay");
            var replacement = (GameOverlay)Get(main, "_gameOverlay")!;
            Check(!ReferenceEquals(replacement, overlay), "Menu toggle reused the permanently closed window.");
            ShowOffscreen(replacement);
            Check(replacement.IsVisible && replacement.CurrentGameId == secondGame.Id,
                "The replacement menu did not show the currently selected game.");
            Check(first.SetupCalls == 0 && second.SetupCalls == 0, "Lifecycle checks invoked game setup or launch.");
            Check(!File.Exists(settings), "Lifecycle checks wrote their isolated settings file.");
            return new { Screen = "game-menu-lifecycle", ConfirmedCommandSurvivesOldPoll = true,
                SelectionDiscardsOldResultsAndErrors = true, NumericAndChoiceModuleDispatch = true, HundredChoiceConfirmedOnly = true,
                IndependentSpeedChoicesWhileOffAndOn = true,
                StaleCallbacksRejected = true, ClosedWindowRecreated = true, NoGameOrNetworkActions = true };
        }
        finally
        {
            main.Close();
            host.Close();
            Localization.Language = language;
            ThemeManager.Apply(theme);
        }
    }

    private static GameItem Item(FixtureModule module) => new(module) {
        Installation = new GameInstallation("C:/HappyCheats-Inert-Menu-Fixture", "fixture", true, true, true, true, "Inert fixture only."),
        Status = module.Status
    };

    private static void ClickFeature(MainWindow main, Window overlay, string featureId)
    {
        var feature = main.Features.Single(item => item.Definition.Id == featureId);
        var button = Descendants(overlay).OfType<Button>().Single(item => ReferenceEquals(item.Tag, feature));
        main.Dispatcher.Invoke(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Drain(main);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void ShowOffscreen(Window window)
    {
        window.ShowActivated = false;
        window.Left = window.Top = -32000;
        window.Show();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Drain(MainWindow main) => main.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static object? Get(MainWindow main, string name) => typeof(MainWindow).GetField(name, Private)!.GetValue(main);
    private static void Set(MainWindow main, string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(main, value);
    private static object? Invoke(MainWindow main, string name, params object[] arguments) =>
        main.Dispatcher.Invoke(() => typeof(MainWindow).GetMethod(name, Private)!.Invoke(main, arguments));
    private static Task InvokeTask(MainWindow main, string name, params object[] arguments) => (Task)Invoke(main, name, arguments)!;

    private static void Finish(MainWindow main, Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer(DispatcherPriority.Send, main.Dispatcher) { Interval = TimeSpan.FromSeconds(5) };
            timeout.Tick += (_, _) => frame.Continue = false;
            task.ContinueWith(_ => main.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            timeout.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timeout.Stop(); }
        }
        Check(task.IsCompleted, "The inert menu lifecycle operation timed out.");
        task.GetAwaiter().GetResult();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixtureModule : IGameModule, IFeatureChoiceModule, IFeatureValueModule
    {
        private readonly Queue<TaskCompletionSource<TrainerStatus>> delayedReads = new();
        public GameDefinition Definition { get; }
        public TrainerStatus Status { get; private set; }
        public int Reads { get; private set; }
        public int SetupCalls { get; private set; }
        public List<string> Commands { get; } = new();
        public List<decimal> Numbers { get; } = new();
        public List<string> Choices { get; } = new();
        public List<(string Id, string Value)> Selections { get; } = new();
        public Exception? NextSelectionError { get; set; }

        public FixtureModule(string id)
        {
            Definition = new GameDefinition(id, "Inert menu fixture", "Fixture", "0", "T", "No game calls.", [
                new("test_toggle", "Test toggle", "Inert toggle", "F1", true),
                new("test_number", "Test number", "Inert number", "F2", ChoiceValueKey: "test_number", Number: new(0, 100, 5)),
                new("test_choice", "Test choice", "Inert choice", "F3", true, Choices: [new("10", "10×"), new("20", "20×"), new("100", "100×")], ChoiceValueKey: "test_choice"),
                new("test_speed", "Test run speed", "Independent speed choice", "F4", true, Choices: [new("3", "3×"), new("5", "5×"), new("10", "10×")], ChoiceValueKey: "test_speed")
            ]);
            Status = new TrainerStatus(true, true, "Inert connected fixture", id + "-session",
                new Dictionary<string, bool> { ["test_toggle"] = false, ["test_choice"] = false, ["test_speed"] = false },
                new Dictionary<string, string> { ["test_number"] = "5", ["test_choice"] = "10", ["test_speed"] = "3" });
        }

        public TaskCompletionSource<TrainerStatus> DelayNextRead()
        {
            var source = new TaskCompletionSource<TrainerStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            delayedReads.Enqueue(source);
            return source;
        }
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation)
        {
            Reads++;
            return delayedReads.Count > 0 ? delayedReads.Dequeue().Task : Task.FromResult(Status);
        }
        public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
        {
            Commands.Add(featureId + ":" + enabled);
            Status = Status with { Enabled = new Dictionary<string, bool>(Status.Enabled) { [featureId] = enabled == true } };
            return Task.CompletedTask;
        }
        public Task SelectAsync(GameInstallation installation, TrainerStatus status, string featureId, string choice)
        {
            Choices.Add(choice);
            Selections.Add((featureId, choice));
            if (NextSelectionError is { } error) { NextSelectionError = null; return Task.FromException(error); }
            Status = Status with { Values = new Dictionary<string, string>(Status.Values) { [featureId] = choice } };
            return Task.CompletedTask;
        }
        public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value)
        {
            Numbers.Add(value);
            Status = Status with { Values = new Dictionary<string, string>(Status.Values) { [featureId] = value.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
            return Task.CompletedTask;
        }
        public Task<GameInstallation> DiscoverAsync(string? directory = null) { SetupCalls++; throw new InvalidOperationException("Unexpected fixture discovery."); }
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) { SetupCalls++; throw new InvalidOperationException("Unexpected fixture install."); }
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) { SetupCalls++; throw new InvalidOperationException("Unexpected fixture removal."); }
        public Task LaunchAsync(GameInstallation installation) { SetupCalls++; throw new InvalidOperationException("Unexpected fixture launch."); }
    }
}
