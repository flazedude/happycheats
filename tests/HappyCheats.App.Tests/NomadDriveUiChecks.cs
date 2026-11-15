using System.Reflection;
using System.Windows.Controls;
using HappyCheats.App;

namespace HappyCheats.App.Tests;

internal static class NomadDriveUiChecks
{
    public static object Run(MainWindow main)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var language = Localization.Language;
        var selection = ((ListBox)main.FindName("Library")).SelectedItem;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", flags)!;
        var assertions = 0;
        void Check(bool condition, string reason) { assertions++; if (!condition) throw new InvalidOperationException("Nomad Drive localization: " + reason); }
        using var scope = new OverlayScope();
        try
        {
            main.SelectGame("nomad-drive");
            var game = main.Selected!;
            var definitions = game.Module.Definition.Features;
            Check(main.Features.Count == 11, "missing controls");
            foreach (var lang in new[] { "zh", "en", "zh" })
            {
                main.SetLanguage(lang, persist: false);
                refresh.Invoke(main, null);
                var chinese = lang == "zh";
                foreach (var feature in main.Features)
                {
                    Check(feature.GameId == game.Id, "selected game context was lost");
                    Check(chinese ? feature.Name != feature.Definition.Name && feature.Description != feature.Definition.Description
                        : feature.Name == feature.Definition.Name && feature.Description == feature.Definition.Description, "name/description did not follow language");
                    Check(feature.Hotkey == definitions.Single(f => f.Id == feature.Definition.Id).Hotkey, "wire metadata changed");
                }
                var energy = main.Features.Single(f => f.Definition.Id == "refill_energy");
                Check(energy.Name == (chinese ? "补满精力" : "Refill energy"), "another game's energy translation leaked");
                var parent = main.Games.Single(g => g.Id == "chinese-parents").Module.Definition.Features.Single(f => f.Id == "refill_energy");
                Check(!chinese || Localization.FeatureName(parent, "chinese-parents") != energy.Name, "Chinese Parents translation was overwritten");
                var reason = "Move within 15 metres of an initialized vehicle.";
                var translated = Localization.Message(reason, game.Id);
                Check(chinese ? translated.Contains("15 米") && translated != reason : translated == reason, "action failure remained in the wrong language");
                Check(Localization.Message(reason, "chinese-parents") == reason, "game-specific error leaked into another game");
                var applied = "Trainer paused: " + reason + "\nApplied refill energy.";
                Check(chinese ? !Localization.Message(applied, game.Id).Contains("Applied") : Localization.Message(applied, game.Id).Replace("\r\n", "\n") == applied, "nested message lost game context");
                scope.Menu.Refresh(game, main.Features, false, false, translated);
                var groups = (ComboBox)typeof(GameOverlay).GetField("groupSelector", flags)!.GetValue(scope.Menu)!;
                Check(groups.Items.Cast<ComboBoxItem>().Skip(1).Select(item => (string)item.Content)
                    .SequenceEqual(chinese ? new[] { "生存", "车辆", "生存资源保持" } : new[] { "Survival", "Vehicle", "Survival locks" }), "menu groups did not follow language");
                var rows = (System.Windows.Controls.StackPanel)typeof(GameOverlay).GetField("rows", flags)!.GetValue(scope.Menu)!;
                var texts = Descendants(rows).OfType<TextBlock>().Select(t => t.Text).ToArray();
                Check(main.Features.All(f => texts.Contains(f.Name) && texts.Contains(f.Description)), "shared menu rows did not use localized controls");
                var labels = MainWindow.FormatValues(new Dictionary<string, string> { ["refill_energy"] = "88", ["refill_food"] = "95", ["available_refuel"] = "False" }, game.Id);
                Check(labels.Contains(chinese ? "精力：88" : Localization.ValueLabel("refill_energy") + ": 88") && !labels.Contains("available_"), "resource labels or metadata filtering differ");
                Check(chinese ? !game.Subtitle.Contains("build") && game.HeroLine != game.Module.Definition.Description : game.HeroLine == game.Module.Definition.Description, "header remained in the wrong language");
            }
            return new { suite = "nomad-drive-localization", assertions };
        }
        finally
        {
            main.SetLanguage(language, persist: false);
            ((ListBox)main.FindName("Library")).SelectedItem = selection;
            refresh.Invoke(main, null);
        }
    }

    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(parent).OfType<System.Windows.DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class OverlayScope : IDisposable
    {
        public GameOverlay Menu { get; } = new(_ => Task.CompletedTask, _ => Task.CompletedTask, () => { }) { Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
        public void Dispose() => Menu.Close();
    }
}
