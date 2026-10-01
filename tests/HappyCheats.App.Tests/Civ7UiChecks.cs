using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HappyCheats.App;
using HappyCheats.Core;

namespace HappyCheats.App.Tests;

internal static class Civ7UiChecks
{
    public static object Run(MainWindow main, string? artifacts)
    {
        var library = (ListBox)main.FindName("Library");
        var originalSelection = library.SelectedItem;
        var originalLanguage = Localization.Language;
        var originalTheme = ThemeManager.IsLight ? "light" : "dark";
        var originalWidth = main.Width;
        var originalHeight = main.Height;
        var busy = typeof(MainWindow).GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var handoff = typeof(MainWindow).GetField("_updateHandoff", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalBusy = busy.GetValue(main);
        var originalHandoff = handoff.GetValue(main);
        var game = main.Games.Single(game => game.Id == "civilization-vii");
        var gameIndex = main.Games.IndexOf(game);
        var definition = game.Module.Definition;
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Civilization VII UI: " + message);
        }
        Check(game.Module is CivilizationVIIModule && game.Module is IFeatureValueModule && definition.AppId == "1295660",
            "the library must expose the real Steam module and its numeric editors");
        Check(definition.Features.Count == 15 && definition.Features.Count(f => f.IsToggle) == 6
            && definition.Features.Count(f => f.Number != null) == 6 && definition.Features.Count(f => !f.IsToggle && f.Number == null) == 3,
            "expected six toggles, six numeric editors and three one-shot actions");
        Check(definition.Features.All(f => f.Id.StartsWith("civ7_", StringComparison.Ordinal))
            && definition.Features.Select(f => f.Hotkey).Distinct().Count() == 15 && definition.Features.All(f => f.Hotkey != "F12"),
            "feature IDs and hotkeys must be distinct, with F12 reserved for cleanup");
        foreach (var uri in new[] { GameArtwork.IconUri(game.Id), GameArtwork.LogoUri(game.Id) }) {
            var image = BitmapFrame.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(image.PixelWidth > 0 && image.PixelHeight > 0, "bundled artwork did not load");
        }

        // A fixture module exercises actual window dispatch without touching the game or Tuner.
        var module = new FixtureModule(definition);
        var fixture = new GameItem(module) { Installation = module.Installation, Status = module.Status };
        var ready = module.Status;
        void Update(TrainerStatus status, bool working = false)
        {
            fixture.Status = module.Status = status;
            busy.SetValue(main, working);
            refresh.Invoke(main, null);
            main.UpdateLayout();
        }
        void Apply(string shortcut) => main.ApplyCivilizationShortcutAsync(shortcut).GetAwaiter().GetResult();
        main.Games[gameIndex] = fixture;
        try {
            library.SelectedItem = fixture;
            Update(ready);
            Check(main.Features.All(f => f.CanUse && f.HotkeyVisibility == Visibility.Visible), "ready controls must be available with visible shortcuts");
            Check(main.Features.Count(f => f.NumberVisibility == Visibility.Visible) == 6, "only numeric editors should show an input");
            Check(main.Features.Single(f => f.Definition.Id == "civ7_gold").ConfirmedNumberLabel.EndsWith("321", StringComparison.Ordinal),
                "editors must show game-confirmed values");
            foreach (var feature in main.Features.Where(f => f.Definition.Number != null)) {
                var number = feature.Definition.Number!;
                foreach (var value in new[] { number.Minimum, number.DefaultValue, number.Maximum }) {
                    feature.NumberText = value.ToString(CultureInfo.InvariantCulture);
                    Check(feature.TryGetNumber(out var actual) && actual == value, "an advertised numeric boundary was rejected");
                }
                foreach (var invalid in new[] { "NaN", "1e3", "1,000", "", (number.Minimum - 1).ToString(CultureInfo.InvariantCulture),
                    (number.Maximum + 1).ToString(CultureInfo.InvariantCulture), (number.Minimum + .5m).ToString(CultureInfo.InvariantCulture) }) {
                    feature.NumberText = invalid;
                    Check(!feature.TryGetNumber(out _), "an invalid/fractional numeric value was accepted");
                }
                feature.NumberText = number.DefaultValue.ToString(CultureInfo.InvariantCulture);
            }

            Apply("F1"); Apply("F1");
            Check(module.Commands.SequenceEqual(new[] { "civ7_movement=True", "civ7_movement=False" }), "toggle shortcuts must use confirmed state");
            foreach (var item in main.Features.Where(f => f.Definition.Number != null)) {
                item.NumberText = item.Definition.Number!.DefaultValue.ToString(CultureInfo.InvariantCulture);
                Apply(item.Definition.Hotkey);
                Check(module.Numbers.Last() == (item.Definition.Id, item.Definition.Number.DefaultValue), "numeric shortcut routed to the wrong editor");
            }
            foreach (var item in main.Features.Where(f => !f.Definition.IsToggle && f.Definition.Number == null)) {
                Apply(item.Definition.Hotkey);
                Check(module.Commands.Last() == item.Definition.Id + "=", "one-shot shortcut routed to the wrong action");
            }
            var gold = main.Features.Single(f => f.Definition.Id == "civ7_gold");
            var numberCount = module.Numbers.Count;
            gold.NumberText = "NaN"; Apply("Shift+F1");
            Check(module.Numbers.Count == numberCount && gold.NumberErrorVisibility == Visibility.Visible, "numeric shortcut bypassed input validation");
            gold.NumberText = "500";
            var unavailable = new Dictionary<string, string>(ready.Values) { ["available_civ7_gold"] = "false", ["available_civ7_restore_moves"] = "false" };
            Update(ready with { Values = unavailable });
            var commandCount = module.Commands.Count;
            Apply("Shift+F1"); Apply("F8");
            Check(module.Numbers.Count == numberCount && module.Commands.Count == commandCount && !gold.CanUse,
                "runtime availability must guard both actions and numeric shortcuts");
            var active = new Dictionary<string, bool>(ready.Enabled) { ["civ7_movement"] = true };
            Update(ready with { Ready = false, Enabled = active });
            Check(main.CanDisableAll && main.Features.Single(f => f.Definition.Id == "civ7_movement").CanUse
                && main.Features.Where(f => f.Definition.Id != "civ7_movement").All(f => !f.CanUse), "between turns only cleanup should be available");
            Apply("F1"); Apply("F12");
            Check(module.Commands.TakeLast(2).SequenceEqual(new[] { "civ7_movement=False", "disable_all=" }), "cleanup hotkeys must work between turns");
            foreach (var status in new[] { ready with { Connected = false, Ready = false }, ready }) {
                Update(status, working: status.Connected);
                commandCount = module.Commands.Count;
                Apply("F1"); Apply("F12"); Apply("Shift+F1");
                Check(!main.CanDisableAll && main.Features.All(f => !f.CanUse) && module.Commands.Count == commandCount
                    && module.Numbers.Count == numberCount, "disconnected or busy state dispatched a command");
            }
            Update(ready);
            handoff.SetValue(main, true);
            Apply("F1"); Apply("F12"); Apply("Shift+F1");
            Check(module.Commands.Count == commandCount && module.Numbers.Count == numberCount, "update handoff dispatched a command");
            handoff.SetValue(main, originalHandoff);
            Apply("F99"); Apply("Shift+F12");
            Check(module.Commands.Count == commandCount, "an unmapped shortcut dispatched a command");

            foreach (var language in new[] { "en", "zh" })
            foreach (var theme in new[] { "dark", "light" }) {
                main.SetLanguage(language, persist: false); main.SetTheme(theme, persist: false);
                main.Width = 1180; main.Height = 820;
                var scroll = (ScrollViewer)main.FindName("DetailScroll");
                scroll.ScrollToTop(); main.UpdateLayout();
                if (language == "zh") {
                    Check(fixture.Name == "文明 VII" && main.Features.All(f => f.Name != f.Definition.Name && f.Description != f.Definition.Description
                        && f.Group != f.Definition.Group), "Chinese names, descriptions and groups must be complete");
                    Check(fixture.HeroLine.Contains("15", StringComparison.Ordinal) && fixture.HeroLine.Contains("尚待游戏内验证", StringComparison.Ordinal)
                        && fixture.HeroLine.Contains("成就的影响尚未验证", StringComparison.Ordinal), "overview must disclose validation limits without claiming achievements are disabled");
                    Check(main.Features.Single(f => f.Definition.Id == "civ7_growth").Name == "完成食物队列", "food queue must not be mislabeled as population-only growth");
                }
                else Check(main.Features.All(f => f.Name == f.Definition.Name && f.Description == f.Definition.Description), "English labels must match the module");
                var buttons = Descendants<Button>(main).Where(b => b.Tag is FeatureItem).ToArray();
                Check(buttons.Length == 15 && buttons.All(b => b.IsEnabled == ((FeatureItem)b.Tag).CanUse), "rendered buttons must bind to guarded availability");
                Check(Descendants<TextBox>(main).Count(t => t.DataContext is FeatureItem f && f.Definition.Number != null) == 6, "six numeric inputs must render");
                if (artifacts != null) {
                    Capture(main, Path.Combine(artifacts, $"civilization-vii-{theme}-{language}.png"));
                    scroll.ScrollToBottom(); main.UpdateLayout();
                    Capture(main, Path.Combine(artifacts, $"civilization-vii-bottom-{theme}-{language}.png"));
                }
            }
            library.SelectedItem = main.Games.First(item => item.Id is not ("civilization-vi" or "civilization-vii"));
            Apply("F1"); Apply("F12");
            Check(module.Commands.Count == commandCount, "Civilization shortcuts leaked onto an unrelated game page");
        }
        finally {
            busy.SetValue(main, originalBusy); handoff.SetValue(main, originalHandoff);
            main.Width = originalWidth; main.Height = originalHeight;
            main.SetLanguage(originalLanguage, persist: false); main.SetTheme(originalTheme, persist: false);
            main.Games[gameIndex] = game; library.SelectedItem = originalSelection; refresh.Invoke(main, null);
        }
        return new { Screen = "civilization-vii", Assertions = assertions, Controls = 15, Toggles = 6, NumericEditors = 6, Actions = 3,
            InactiveTurnCleanup = true, ScopedDispatch = true, EnglishAndChinese = true, LightAndDark = true, NoGameCommands = true };
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Capture(MainWindow main, string path)
    {
        var content = (FrameworkElement)main.Content;
        var target = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        target.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class FixtureModule(GameDefinition definition) : IGameModule, IFeatureValueModule
    {
        public GameDefinition Definition { get; } = definition;
        public GameInstallation Installation { get; } = new("C:/Civ7UiFixture", "fixture", true, true, true, true, "Fixture only");
        public TrainerStatus Status { get; set; } = new(true, true, "Fixture only", "civ7-ui-fixture",
            definition.Features.Where(f => f.IsToggle).ToDictionary(f => f.Id, _ => false),
            definition.Features.ToDictionary(f => "available_" + f.Id, _ => "true").Concat(new[] { new KeyValuePair<string, string>("civ7_gold", "321") }).ToDictionary(pair => pair.Key, pair => pair.Value));
        public List<string> Commands { get; } = new();
        public List<(string Id, decimal Value)> Numbers { get; } = new();
        public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
        {
            Commands.Add(featureId + "=" + enabled);
            var effects = new Dictionary<string, bool>(Status.Enabled);
            if (enabled.HasValue) effects[featureId] = enabled.Value;
            if (featureId == "disable_all") foreach (var key in effects.Keys) effects[key] = false;
            Status = Status with { Enabled = effects };
            return Task.CompletedTask;
        }
        public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value)
        { Numbers.Add((featureId, value)); return Task.CompletedTask; }
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.FromResult(Status);
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => throw new NotSupportedException();
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new NotSupportedException();
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new NotSupportedException();
        public Task LaunchAsync(GameInstallation installation) => throw new NotSupportedException();
    }
}
