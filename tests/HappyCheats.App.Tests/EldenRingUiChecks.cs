using System.IO;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HappyCheats.App;
using HappyCheats.Core;

namespace HappyCheats.App.Tests;

internal static class EldenRingUiChecks
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
        var game = main.Games.Single(game => game.Id == "elden-ring");
        var gameIndex = main.Games.IndexOf(game);
        var definition = game.Module.Definition;
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Elden Ring UI: " + message);
        }
        Check(game.Module is EldenRingModule && definition.AppId == "1245620", "the catalog must expose the Steam module");
        Check(definition.Features.Select(feature => feature.Id).SequenceEqual(new[] {
                "er_health", "er_fp", "er_stamina", "er_restore", "er_damage_protection", "er_consumables", "er_runes" })
            && definition.Features.Count(feature => feature.IsToggle) == 5, "expected five toggles, one restore action and one rune editor");
        Check(definition.Features.Select(feature => feature.Hotkey).SequenceEqual(new[] { "F1", "F2", "F3", "F4", "", "", "" }),
            "new controls must not advertise unsupported shortcuts on existing clients");
        var runeDefinition = definition.Features.Single(feature => feature.Id == "er_runes");
        Check(runeDefinition is { IsToggle: false, ChoiceValueKey: "Runes / 卢恩", Number: { Minimum: 0, Maximum: 999999999, Step: 1 } },
            "runes must expose a bounded whole-number one-shot editor through existing metadata");
        foreach (var uri in new[] { GameArtwork.IconUri(game.Id), GameArtwork.LogoUri(game.Id) }) {
            var image = BitmapFrame.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(image.PixelWidth > 0 && image.PixelHeight > 0, "bundled artwork did not load");
        }

        var scope = new Civ6HotkeyScope(123, "elden-ring-fixture", false, EldenRing: true);
        foreach (var shift in new[] { false, true })
        foreach (var key in Enumerable.Range(0x70, 12)) {
            var router = new Civ6HotkeyRouter();
            if (shift) router.Process(0xA0, true, scope, 123);
            var decision = router.Process(key, true, scope, 123);
            var expected = !shift && (key <= 0x73 || key == 0x7B);
            Check(decision.Suppress == expected && decision.Request.HasValue == expected, "the hook intercepted an unmapped Elden Ring shortcut");
            Check(router.Process(key, false, scope, 123).Suppress == expected, "key-up suppression differs from key-down");
        }
        var request = new Civ6HotkeyRequest(123, "elden-ring-fixture", "F1");
        Check(request.Matches(scope, 123) && !request.Matches(scope, 456) && !request.Matches(scope with { SessionId = "changed" }, 123)
            && !request.Matches(scope with { Busy = true }, 123), "focus, session or busy guards failed");
        Check(!new Civ6HotkeyRequest(123, "elden-ring-fixture", "F5").Matches(scope, 123), "unmapped queued request survived the game scope");

        // Commands target this fixture only; the installed game is never launched or opened.
        var module = new FixtureModule(definition);
        var fixture = new GameItem(module) { Installation = module.Installation, Status = module.Status };
        var ready = module.Status;
        void Update(TrainerStatus status, bool working = false)
        {
            fixture.Status = module.Status = status;
            busy.SetValue(main, working); refresh.Invoke(main, null); main.UpdateLayout();
        }
        void Apply(string shortcut) => main.ApplyCivilizationShortcutAsync(shortcut).GetAwaiter().GetResult();
        main.Games[gameIndex] = fixture;
        try {
            library.SelectedItem = fixture;
            main.SetLanguage("en", persist: false);
            Update(ready);
            Check(main.InstallLabel == "Connect trainer" && main.RemoveLabel == "Disconnect trainer" && main.LaunchLabel == "Offline launch",
                "external trainer actions must have accurate labels");
            Check(!main.CanLaunch && main.CanRemove && main.InstallationLabel == "No game files installed", "running runtime connection was treated as file installation");
            Check(main.Features.All(feature => feature.CanUse), "ready features were disabled");
            Apply("F1"); Apply("F1"); Apply("F2"); Apply("F3"); Apply("F4");
            Check(module.Commands.SequenceEqual(new[] { "er_health=True", "er_health=False", "er_fp=True", "er_stamina=True", "er_restore=" }),
                "shortcuts routed incorrectly or failed to use confirmed toggle state");
            Update(module.Status with { Ready = false });
            Check(main.CanDisableAll && FeatureButtons(main).All(button => button.IsEnabled),
                "gameplay readiness must not grey out client controls");
            Apply("F12");
            Check(module.Commands.Last() == "disable_all=" && module.Status.Enabled.Values.All(enabled => !enabled), "F12 failed to stop refill effects");
            foreach (var status in new[] { ready with { Connected = false, Ready = false }, ready }) {
                Update(status, working: status.Connected);
                var before = module.Commands.Count;
                Apply("F1"); Apply("F4"); Apply("F12");
                Check(FeatureButtons(main).All(button => button.IsEnabled) && module.Commands.Count == before,
                    "disconnected/busy controls must remain clickable while scoped shortcuts retain their guards");
            }
            Update(ready);
            handoff.SetValue(main, true);
            var commandCount = module.Commands.Count;
            Apply("F1"); Apply("F4"); Apply("F12");
            Check(module.Commands.Count == commandCount, "update handoff dispatched a command");
            handoff.SetValue(main, originalHandoff);
            Apply("F5"); Apply("Shift+F1");
            Check(module.Commands.Count == commandCount, "unmapped shortcut dispatched a command");
            fixture.Installation = module.Installation with { IsRunning = false };
            Update(ready with { Connected = false, Ready = false });
            Check(main.CanLaunch && !main.CanRemove, "offline launch/connection buttons ignored process status");
            fixture.Installation = fixture.Installation with { Compatible = false };
            Update(fixture.Status);
            Check(!main.CanLaunch && !main.CanInstall, "unsupported game build can launch/connect");
            Check(Descendants<Button>(main).All(button => button.IsEnabled), "an unsupported build greyed out a setup or trainer button");
            fixture.Installation = module.Installation;
            Update(ready);

            foreach (var featureId in new[] { "er_damage_protection", "er_consumables" }) {
                var button = FeatureButtons(main).Single(button => ((FeatureItem)button.Tag).Definition.Id == featureId);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(module.Commands.Last() == featureId + "=True", "the new toggle button did not dispatch its explicit On state");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(module.Commands.Last() == featureId + "=False", "the new toggle button did not dispatch its explicit Off state");
            }

            var runes = main.Features.Single(feature => feature.Definition.Id == "er_runes");
            var sendNumber = typeof(MainWindow).GetMethod("SendNumberAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void ApplyRunes() => ((Task)sendNumber.Invoke(main, new object[] { runes })!).GetAwaiter().GetResult();
            runes.NumberText = "123456";
            ApplyRunes();
            Check(module.Commands.Last() == "er_runes=123456" && module.Status.Values["Runes / 卢恩"] == "123456",
                "the client numeric path failed to dispatch the explicit rune value");
            var formattedRunes = MainWindow.FormatValues(module.Status.Values, "elden-ring");
            Check(formattedRunes.Contains("Runes / 卢恩", StringComparison.Ordinal) && formattedRunes.Contains("123456", StringComparison.Ordinal)
                && !formattedRunes.Contains("er_runes_value", StringComparison.Ordinal), "the frozen client exposed an internal rune value key");
            var afterRuneEdit = module.Commands.Count;
            foreach (var invalid in new[] { "-1", "1000000000", "1.5" }) {
                runes.NumberText = invalid;
                ApplyRunes();
                Check(module.Commands.Count == afterRuneEdit, "an invalid rune edit reached the module");
            }
            runes.NumberText = "37";
            CheckSharedMenu(fixture, ready, main.Features.ToArray(), Check);
            Update(module.Status);
            typeof(MainWindow).GetMethod("SetMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main,
                new object[] { "Synthetic UI fixture; no game launched or attached. Gameplay remains unverified.", false });
            commandCount = module.Commands.Count;

            foreach (var language in new[] { "en", "zh" })
            foreach (var theme in new[] { "dark", "light" }) {
                main.SetLanguage(language, persist: false); main.SetTheme(theme, persist: false);
                main.Width = 1180; main.Height = 820;
                ((ScrollViewer)main.FindName("DetailScroll")).ScrollToTop(); main.UpdateLayout();
                if (language == "zh") {
                    Check(fixture.Name == "艾尔登法环" && main.Features.Where(feature => feature.Definition.Id is "er_health" or "er_fp" or "er_stamina" or "er_restore")
                        .All(feature => feature.Name != feature.Definition.Name && feature.Description != feature.Definition.Description),
                        "legacy Chinese names or descriptions are missing");
                    Check(main.Features.Where(feature => feature.Definition.Id is "er_damage_protection" or "er_consumables" or "er_runes")
                        .All(feature => feature.Name.Any(character => character is >= '\u3400' and <= '\u9fff')
                            && feature.Description.Any(character => character is >= '\u3400' and <= '\u9fff')),
                        "new controls must carry bilingual metadata on frozen clients");
                    Check(fixture.HeroLine.Contains("离线", StringComparison.Ordinal) && fixture.HeroLine.Contains("不是无敌", StringComparison.Ordinal)
                        && fixture.HeroLine.Contains("尚待游戏内验证", StringComparison.Ordinal), "overview omitted scope or validation limits");
                    Check(main.LaunchLabel == "离线启动" && main.InstallLabel == "连接修改器" && main.RemoveLabel == "断开修改器", "Chinese action labels are missing");
                }
                else Check(main.Features.All(feature => feature.Name == feature.Definition.Name && feature.Description == feature.Definition.Description), "English labels differ from the module");
                Check(Localization.ValueLabel("er_stamina_value") == (language == "en" ? "Stamina" : "精力")
                    && Localization.ValueLabel("Stamina") == (language == "en" ? "Stamina" : "活力"),
                    "Elden Ring resource localization must preserve the existing Witcher stamina label");
                var resources = MainWindow.FormatValues(new Dictionary<string, string> { ["er_stamina_value"] = "30 / 90" });
                Check(resources.Contains(language == "en" ? "Stamina" : "精力", StringComparison.Ordinal)
                    && resources.Contains("30 / 90", StringComparison.Ordinal) && !resources.Contains("er_stamina_value", StringComparison.Ordinal),
                    "the game-specific stamina key must render as a translated resource label");
                var buttons = FeatureButtons(main);
                Check(buttons.Length == 7 && buttons.All(button => button.IsEnabled), "all seven clickable feature buttons must render");
                if (artifacts != null) {
                    Capture(main, Path.Combine(artifacts, $"elden-ring-{theme}-{language}.png"));
                    ((ScrollViewer)main.FindName("DetailScroll")).ScrollToBottom(); main.UpdateLayout();
                    Capture(main, Path.Combine(artifacts, $"elden-ring-controls-{theme}-{language}.png"));
                    main.Width = 854; main.Height = 534; main.UpdateLayout();
                    ((ScrollViewer)main.FindName("DetailScroll")).ScrollToTop(); main.UpdateLayout();
                    Capture(main, Path.Combine(artifacts, $"elden-ring-handheld-{theme}-{language}.png"));
                    ((ScrollViewer)main.FindName("DetailScroll")).ScrollToBottom(); main.UpdateLayout();
                    Capture(main, Path.Combine(artifacts, $"elden-ring-handheld-controls-{theme}-{language}.png"));
                }
            }
            library.SelectedItem = main.Games.First(item => item.Id is not ("elden-ring" or "civilization-vi" or "civilization-vii"));
            Apply("F1"); Apply("F12");
            Check(module.Commands.Count == commandCount, "shortcut dispatched after leaving the game page");
        }
        finally {
            busy.SetValue(main, originalBusy); handoff.SetValue(main, originalHandoff);
            main.Width = originalWidth; main.Height = originalHeight;
            main.SetLanguage(originalLanguage, persist: false); main.SetTheme(originalTheme, persist: false);
            main.Games[gameIndex] = game; library.SelectedItem = originalSelection; refresh.Invoke(main, null);
        }
        return new { Screen = "elden-ring", Assertions = assertions, Controls = 7, ScopedShortcuts = true,
            OfflineLaunchGuards = true, EnglishAndChinese = true, LightAndDark = true, NoGameCommands = true,
            NumericRuneEditor = true, SharedMenuMetadata = true };
    }

    private static Button[] FeatureButtons(DependencyObject root) => Descendants<Button>(root).Where(button => button.Tag is FeatureItem).ToArray();

    private static void CheckSharedMenu(GameItem game, TrainerStatus ready, FeatureItem[] features, Action<bool, string> check)
    {
        var sent = new List<string>();
        var overlay = new GameOverlay(feature => { sent.Add(feature.Id); return Task.CompletedTask; },
            _ => throw new InvalidOperationException("Elden Ring has no choice controls."), () => { }) { Left = -32000, Top = -32000 };
        try {
            overlay.Refresh(game, features, false, true, "Elden Ring synthetic menu fixture; no game connection");
            overlay.Show(); overlay.UpdateLayout();
            check(FeatureButtons(overlay).Select(button => ((FeatureItem)button.Tag).Definition.Id)
                .SequenceEqual(features.Select(feature => feature.Definition.Id)), "shared menu omitted an Elden Ring control");
            var editor = Descendants<TextBox>(overlay).Single(input => input.Tag is FeatureItem);
            var runes = (FeatureItem)editor.Tag;
            check(runes.Definition.Id == "er_runes" && editor.IsEnabled, "the shared menu must render one clickable rune editor");
            editor.SetCurrentValue(TextBox.TextProperty, "37");
            editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            foreach (var unavailable in new[] { false, true }) {
                foreach (var feature in features) feature.Update(ready with { Ready = !unavailable }, !unavailable);
                overlay.Refresh(game, features, unavailable, true, "Synthetic refresh");
                overlay.UpdateLayout();
                check(ReferenceEquals(editor, Descendants<TextBox>(overlay).Single(input => input.Tag is FeatureItem))
                    && editor.Text == "37" && runes.NumberText == "37", "menu refresh replaced the rune draft");
                check(editor.IsEnabled && FeatureButtons(overlay).All(button => button.IsEnabled), "menu status disabled an Elden Ring control");
            }
            var runeButton = FeatureButtons(overlay).Single(button => ((FeatureItem)button.Tag).Definition.Id == "er_runes");
            runeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(sent.SequenceEqual(new[] { "er_runes" }), "the shared menu did not route the rune action through its common callback");
        }
        finally { overlay.Close(); }
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
        public GameInstallation Installation { get; } = new("C:/EldenRingUiFixture", "fixture", true, false, true, true, "Fixture only");
        public TrainerStatus Status { get; set; } = new(true, true, "Fixture only", "elden-ring-ui-fixture",
            definition.Features.Where(feature => feature.IsToggle).ToDictionary(feature => feature.Id, _ => false),
            definition.Features.ToDictionary(feature => "available_" + feature.Id, _ => "true"));
        public List<string> Commands { get; } = new();
        public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
        {
            Commands.Add(featureId + "=" + enabled);
            var effects = new Dictionary<string, bool>(Status.Enabled);
            if (enabled.HasValue) effects[featureId] = enabled.Value;
            if (featureId == "disable_all") foreach (var key in effects.Keys) effects[key] = false;
            Status = Status with { Enabled = effects };
            return Task.CompletedTask;
        }
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.FromResult(Status);
        public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value)
        {
            if (featureId != "er_runes") throw new ArgumentException("Unknown Elden Ring fixture value.");
            Commands.Add(featureId + "=" + value.ToString(CultureInfo.InvariantCulture));
            Status = Status with { Values = new Dictionary<string, string>(Status.Values) { ["Runes / 卢恩"] = value.ToString(CultureInfo.InvariantCulture) } };
            return Task.CompletedTask;
        }
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => throw new NotSupportedException();
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new NotSupportedException();
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new NotSupportedException();
        public Task LaunchAsync(GameInstallation installation) => throw new NotSupportedException();
    }
}
