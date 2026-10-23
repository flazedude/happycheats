using System.IO;
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
        Check(definition.Features.Select(feature => feature.Id).SequenceEqual(new[] { "er_health", "er_fp", "er_stamina", "er_restore" })
            && definition.Features.Count(feature => feature.IsToggle) == 3, "expected three refill toggles and one restore action");
        Check(definition.Features.Select(feature => feature.Hotkey).SequenceEqual(new[] { "F1", "F2", "F3", "F4" }), "hotkey labels changed");
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
            Check(main.CanDisableAll && main.Features.Where(feature => feature.Definition.Id is "er_fp" or "er_stamina").All(feature => feature.CanUse)
                && main.Features.Where(feature => feature.Definition.Id is "er_health" or "er_restore").All(feature => !feature.CanUse),
                "only active toggle cleanup should remain available while gameplay is unavailable");
            Apply("F12");
            Check(module.Commands.Last() == "disable_all=" && module.Status.Enabled.Values.All(enabled => !enabled), "F12 failed to stop refill effects");
            foreach (var status in new[] { ready with { Connected = false, Ready = false }, ready }) {
                Update(status, working: status.Connected);
                var before = module.Commands.Count;
                Apply("F1"); Apply("F4"); Apply("F12");
                Check(!main.CanDisableAll && main.Features.All(feature => !feature.CanUse) && module.Commands.Count == before,
                    "disconnected/busy controls dispatched a command");
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
            fixture.Installation = module.Installation;
            Update(ready);

            foreach (var language in new[] { "en", "zh" })
            foreach (var theme in new[] { "dark", "light" }) {
                main.SetLanguage(language, persist: false); main.SetTheme(theme, persist: false);
                main.Width = 1180; main.Height = 820;
                ((ScrollViewer)main.FindName("DetailScroll")).ScrollToTop(); main.UpdateLayout();
                if (language == "zh") {
                    Check(fixture.Name == "艾尔登法环" && main.Features.All(feature => feature.Name != feature.Definition.Name && feature.Description != feature.Definition.Description),
                        "Chinese names or descriptions are missing");
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
                var buttons = Descendants<Button>(main).Where(button => button.Tag is FeatureItem).ToArray();
                Check(buttons.Length == 4 && buttons.All(button => button.IsEnabled), "four usable feature buttons must render");
                if (artifacts != null) {
                    Capture(main, Path.Combine(artifacts, $"elden-ring-{theme}-{language}.png"));
                    main.Width = 854; main.Height = 534; main.UpdateLayout();
                    Capture(main, Path.Combine(artifacts, $"elden-ring-handheld-{theme}-{language}.png"));
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
        return new { Screen = "elden-ring", Assertions = assertions, Controls = 4, ScopedShortcuts = true,
            OfflineLaunchGuards = true, EnglishAndChinese = true, LightAndDark = true, NoGameCommands = true };
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
    private sealed class FixtureModule(GameDefinition definition) : IGameModule
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
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => throw new NotSupportedException();
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new NotSupportedException();
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new NotSupportedException();
        public Task LaunchAsync(GameInstallation installation) => throw new NotSupportedException();
    }
}
