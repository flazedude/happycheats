using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using HappyCheats.App;
using HappyCheats.Core;

namespace HappyCheats.App.Tests;

internal static class ClickableControlsChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static object Run(MainWindow main, string root)
    {
        var original = main.Selected?.Id;
        var language = Localization.Language;
        var busy = typeof(MainWindow).GetField("_busy", Private)!;
        var selected = typeof(MainWindow).GetField("_selected", Private)!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", Private)!;
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Always clickable controls: " + message);
        }
        try
        {
            main.SetLanguage("en", persist: false);
            foreach (var game in main.Games)
            {
                var previousStatus = game.Status;
                main.SelectGame(game.Id);
                foreach (var working in new[] { false, true })
                {
                    game.Status = TrainerStatus.Offline("Synthetic disconnected session");
                    busy.SetValue(main, working);
                    refresh.Invoke(main, null);
                    main.UpdateLayout();
                    Check(Descendants<Control>(main).Where(control => control is ButtonBase or TextBox or ComboBox or ListBox)
                        .All(control => control.IsEnabled), game.Id + " disabled a desktop control");
                }
                busy.SetValue(main, false);
                game.Status = previousStatus;
            }

            var module = new Fixture();
            var gameFixture = new GameItem(module) {
                Installation = new GameInstallation("synthetic-no-files", "fixture", true, false, false, false, "Synthetic"),
                Status = TrainerStatus.Offline("Synthetic disconnected session")
            };
            selected.SetValue(main, gameFixture);
            main.Features.Clear();
            foreach (var definition in module.Definition.Features) main.Features.Add(new FeatureItem(definition));
            refresh.Invoke(main, null);
            main.UpdateLayout();
            var toggle = main.Features.First();
            var action = Descendants<Button>(main).Single(button => ReferenceEquals(button.Tag, toggle));
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(module.Calls.SequenceEqual(new[] { "toggle" }) && main.Message.Contains(Fixture.Error),
                "disconnected click did not reach the module and report its error");
            gameFixture.Status = new TrainerStatus(true, false, "Synthetic unready session", "fixture", new Dictionary<string, bool>(),
                new Dictionary<string, string> { ["available_toggle"] = "false" });
            refresh.Invoke(main, null);
            InvokeTask("SendFeatureAsync", toggle.Definition);
            Check(module.Calls.Count == 2 && main.Message.Contains(Fixture.Error), "unready click was silently gated");
            InvokeTask("SendFeatureAsync", new FeatureDefinition("disable_all", "Disable all", "", ""));
            Check(module.Calls.Last() == "disable_all", "cleanup was silently gated");
            InvokeTask("SendNumberAsync", main.Features.Single(feature => feature.Definition.Id == "number"));
            Check(module.Calls.Last() == "number:5", "numeric input did not reach the module");
            InvokeTask("SendChoiceAsync", main.Features.Single(feature => feature.Definition.Id == "choice").Choices.First());
            Check(module.Calls.Last() == "choice:a", "selection did not reach the module");
            var count = module.Calls.Count;
            busy.SetValue(main, true);
            refresh.Invoke(main, null);
            Check(action.IsEnabled, "pending operation greyed out the action");
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(module.Calls.Count == count && main.Message.Contains("operation is still running"),
                "busy click did not report an error, or overlapped a command");
            busy.SetValue(main, false);
            gameFixture.Installation = null;
            InvokeTask("SendFeatureAsync", toggle.Definition);
            Check(module.Calls.Count == count && main.Message.Contains("Game installation not found"),
                "missing installation did not report an actionable error");

            using var http = new System.Net.Http.HttpClient();
            var updates = UpdateWindow.CreatePreview(http);
            updates.Show(); updates.UpdateLayout();
            try
            {
                Check(Descendants<Button>(updates).All(button => button.IsEnabled), "update controls were greyed out");
                typeof(UpdateWindow).GetMethod("CheckClicked", Private)!.Invoke(updates, new object[] { updates, new RoutedEventArgs() });
                Check(updates.ErrorVisibility == Visibility.Visible && updates.ErrorDetails.Length > 0,
                    "unavailable update click did not explain its error");
            }
            finally { updates.Close(); }
            var logDirectory = Path.Combine(Path.GetTempPath(), "HappyCheats-clickable-" + Guid.NewGuid().ToString("N"));
            ClientErrorLog.Write("fixture", Fixture.Error, logDirectory);
            Check(File.ReadAllText(Path.Combine(logDirectory, "client-errors.log")).Contains("[fixture] " + Fixture.Error),
                "client error log did not retain the error");
        }
        finally
        {
            busy.SetValue(main, false);
            main.SetLanguage(language, persist: false);
            if (original != null) main.SelectGame(original);
            refresh.Invoke(main, null);
        }
        return new { Screen = "always-clickable", Assertions = assertions, CatalogGames = main.Games.Count,
            ErrorsOnClick = true, NumericAndChoiceDispatch = true, NoGameCommands = true };

        void InvokeTask(string method, object argument) =>
            ((Task)typeof(MainWindow).GetMethod(method, Private)!.Invoke(main, new[] { argument })!).GetAwaiter().GetResult();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class Fixture : IGameModule, IFeatureChoiceModule, IFeatureValueModule
    {
        internal const string Error = "Synthetic command rejected; no game was accessed.";
        internal readonly List<string> Calls = new();
        public GameDefinition Definition { get; } = new("clickable-fixture", "Fixture", "", "", "", "", new[] {
            new FeatureDefinition("toggle", "Toggle", "", "", true),
            new FeatureDefinition("number", "Number", "", "", Number: new FeatureNumber(0, 10, 5)),
            new FeatureDefinition("choice", "Choice", "", "", Choices: new[] { new FeatureChoice("a", "A") }) });
        private Task Reject(string action) { Calls.Add(action); return Task.FromException(new InvalidOperationException(Error)); }
        public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) => Reject(featureId);
        public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value) => Reject(featureId + ":" + value);
        public Task SelectAsync(GameInstallation installation, TrainerStatus status, string featureId, string choice) => Reject(featureId + ":" + choice);
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.FromResult(TrainerStatus.Offline(Error));
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => throw new InvalidOperationException("Unexpected discovery");
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new InvalidOperationException("Unexpected installation");
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new InvalidOperationException("Unexpected removal");
        public Task LaunchAsync(GameInstallation installation) => throw new InvalidOperationException("Unexpected launch");
    }
}
