using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HappyCheats.App;
using HappyCheats.Core;

namespace HappyCheats.App.Tests;

internal static class PhantomBladeUiChecks
{
    public static object Run(MainWindow main, string? artifacts)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var library = (ListBox)main.FindName("Library");
        var originalSelection = library.SelectedItem;
        var originalLanguage = Localization.Language;
        var originalTheme = ThemeManager.IsLight ? "light" : "dark";
        var originalWidth = main.Width; var originalHeight = main.Height;
        var busy = typeof(MainWindow).GetField("_busy", flags)!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", flags)!;
        var sendNumber = typeof(MainWindow).GetMethod("SendNumberAsync", flags)!;
        var sendFeature = typeof(MainWindow).GetMethod("SendFeatureAsync", flags)!;
        var originalBusy = busy.GetValue(main);
        var game = main.Games.Single(item => item.Id == "phantom-blade-zero");
        var gameIndex = main.Games.IndexOf(game);
        var definition = game.Module.Definition;
        var assertions = 0;
        void Check(bool condition, string message) { assertions++; if (!condition) throw new InvalidOperationException("Phantom Blade UI: " + message); }
        Check(game.Module is PhantomBladeModule && game.Module is IFeatureValueModule && definition.AppId == "4115450", "Steam module is missing");
        Check(game.Module.GameExecutables.SequenceEqual(new[] { "platform/steam/PBZ.exe" }), "menu target must be the real executable, not the launcher");
        Check(definition.Features.Select(f => f.Id).SequenceEqual(new[] { "pbz_health", "pbz_stamina", "pbz_damage" }), "requested controls differ");
        Check(definition.Features.Select(f => f.Hotkey).SequenceEqual(new[] { "F1", "F2", "" }), "damage editor must not advertise a fixed-value hotkey");
        Check(definition.Features[2].Number == new FeatureNumber(1, 20, 2, Unit: "×") && definition.Features[2].ChoiceValueKey == "pbz_damage", "multiplier range or confirmation key differs");
        Check(definition.Features[2].Description.Contains("Game stat recalculation may cancel it.", StringComparison.Ordinal), "CurrentValue recalculation limit must be visible");
        foreach (var uri in new[] { GameArtwork.IconUri(game.Id), GameArtwork.LogoUri(game.Id) }) {
            var bitmap = BitmapFrame.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "bundled artwork did not load offline");
        }

        var module = new FixtureModule(definition);
        var fixture = new GameItem(module) { Installation = module.Installation, Status = module.Status };
        var ready = module.Status;
        void Update(TrainerStatus status, bool working = false) {
            fixture.Status = module.Status = status; busy.SetValue(main, working); refresh.Invoke(main, null); main.UpdateLayout();
        }
        void Number(FeatureItem feature) => ((Task)sendNumber.Invoke(main, new object[] { feature })!).GetAwaiter().GetResult();
        void Feature(FeatureDefinition feature) => ((Task)sendFeature.Invoke(main, new object[] { feature })!).GetAwaiter().GetResult();
        main.Games[gameIndex] = fixture;
        try {
            library.SelectedItem = fixture; main.SetLanguage("en", persist: false); Update(ready);
            var health = main.Features.Single(f => f.Definition.Id == "pbz_health");
            var damage = main.Features.Single(f => f.Definition.Id == "pbz_damage");
            Check(main.Features.All(f => f.CanUse) && damage.HotkeyVisibility == Visibility.Collapsed, "ready state or numeric hotkey visibility is wrong");
            Check(damage.ConfirmedNumberLabel.EndsWith("1", StringComparison.Ordinal), "confirmed multiplier is missing");
            damage.NumberText = "7";
            Number(damage);
            Check(module.Values.SequenceEqual(new[] { ("pbz_damage", 7m) }) && module.Commands.Count == 0, "numeric editor dispatched a toggle or default amount");
            Check(damage.ConfirmedNumberLabel.EndsWith("1", StringComparison.Ordinal) && damage.NumberText == "7", "unconfirmed multiplier was displayed optimistically or draft was replaced");
            var confirmed = ready with { Values = new Dictionary<string, string>(ready.Values) { ["pbz_damage"] = "7" } };
            Update(confirmed);
            Check(ReferenceEquals(damage, main.Features.Single(f => f.Definition.Id == "pbz_damage")) && damage.NumberText == "7"
                && damage.ConfirmedNumberLabel.EndsWith("7", StringComparison.Ordinal), "polling replaced controls, lost the draft, or ignored confirmed value");
            damage.NumberText = "12";
            Update(confirmed);
            Check(damage.NumberText == "12" && damage.ConfirmedNumberLabel.EndsWith("7", StringComparison.Ordinal), "fresh status overwrote unfinished input");
            foreach (var invalid in new[] { "0", "21", "1.5", "NaN", "1,000" }) {
                damage.NumberText = invalid; var count = module.Values.Count; Number(damage);
                Check(module.Values.Count == count && damage.NumberErrorVisibility == Visibility.Visible, "invalid multiplier was sent: " + invalid);
            }
            damage.NumberText = "12";
            var unavailable = confirmed with { Values = new Dictionary<string, string>(confirmed.Values) { ["available_pbz_damage"] = "False" } };
            Update(unavailable); var blockedCount = module.Values.Count; Number(damage);
            Check(!damage.CanUse && health.CanUse && module.Values.Count == blockedCount, "damage availability leaked to health or allowed a command");
            Update(confirmed, working: true); Number(damage);
            Check(main.Features.All(f => !f.CanUse) && !main.CanDisableAll && module.Values.Count == blockedCount, "busy state permitted dispatch");
            Update(TrainerStatus.Offline()); Number(damage);
            Check(main.Features.All(f => !f.CanUse) && damage.ConfirmedNumberVisibility == Visibility.Collapsed && module.Values.Count == blockedCount, "disconnected state retained confirmation or allowed dispatch");
            Update(confirmed with { Ready = false, Enabled = new Dictionary<string, bool> { ["pbz_health"] = true, ["pbz_stamina"] = false } });
            Check(main.CanDisableAll && health.CanUse && main.Features.Where(f => f != health).All(f => !f.CanUse), "active-toggle cleanup disappeared when player was unavailable");
            Feature(health.Definition);
            Check(module.Commands.Last() == "pbz_health=False", "cleanup did not send explicit toggle off");
            Feature(new FeatureDefinition("disable_all", "Disable all", "", ""));
            Check(module.Commands.Last() == "disable_all=" && module.Status.Values["pbz_damage"] == "1", "disable all did not reach the fixture's multiplier restoration");
            Update(confirmed);

            foreach (var language in new[] { "en", "zh" })
            foreach (var theme in new[] { "dark", "light" }) {
                main.SetLanguage(language, persist: false); main.SetTheme(theme, persist: false);
                main.Width = 1180; main.Height = 820;
                ((ScrollViewer)main.FindName("DetailScroll")).ScrollToTop(); main.UpdateLayout();
                Check(damage.NumberText == "12" && damage.ConfirmedNumberLabel.EndsWith("7", StringComparison.Ordinal), "language/theme refresh lost draft or confirmed value");
                Check(Localization.ValueLabel("pbz_damage") == (language == "en" ? "Attack multiplier" : "攻击力倍率")
                    && Localization.ValueLabel("pbz_stamina") == (language == "en" ? "Sha-Chi" : "杀气"), "resource labels expose protocol identifiers");
                if (language == "zh") {
                    Check(fixture.Name == "影之刃零" && main.Features.All(f => f.Name != f.Definition.Name && f.Description != f.Definition.Description), "Chinese labels or descriptions are missing");
                    Check(damage.Description.Contains("游戏重新计算属性时可能取消倍率。", StringComparison.Ordinal), "Chinese multiplier description omits recalculation limit");
                    Check(Localization.Message("This Phantom Blade Zero build needs a compatibility check.") == "此影之刃零版本需要兼容性检查。", "Chinese compatibility status is missing");
                    Check(Localization.Message("Phantom Blade Zero trainer paused: fixture") == "影之刃零修改器已暂停：fixture", "Chinese diagnostic prefix lost its detail");
                    Check(Localization.Message("Phantom Blade Zero owned attribute set is unavailable.") == "无法确认影之刃零玩家的属性归属，当前不可用。"
                        && Localization.Message("The player's owned attribute set is unavailable.") == "无法确认玩家的属性归属，当前不可用。", "CurrentValue ownership failures are not translated");
                    Check(Localization.Message("Attack power multiplier is already applied.") == "此攻击力倍率已应用。"
                        && Localization.Message("Attack restoration is pending; keep HappyCheats connected.") == "正在等待恢复攻击力，请保持 HappyCheats 连接。", "multiplier state or pending restoration message is misleading");
                    Check(Localization.Message("Experimental Phantom Blade Zero trainer connected. Gameplay effects are unverified.") == "影之刃零实验性修改器已连接；游戏内效果尚未验证。", "runtime limitation must remain visible in Chinese");
                } else Check(main.Features.All(f => f.Name == f.Definition.Name && f.Description == f.Definition.Description), "English labels differ from the module");
                var buttons = Descendants<Button>(main).Where(button => button.Tag is FeatureItem).ToArray();
                Check(buttons.Length == 3 && buttons.All(button => button.IsEnabled), "three enabled control buttons must render");
                foreach (var size in new[] { (1180, 820), (854, 534) }) {
                    main.Width = size.Item1; main.Height = size.Item2; main.UpdateLayout();
                    if (artifacts != null) {
                        Capture(main, Path.Combine(artifacts, $"phantom-blade-{theme}-{language}-{size.Item1}.png"));
                        ((ScrollViewer)main.FindName("DetailScroll")).ScrollToBottom(); main.UpdateLayout();
                        Capture(main, Path.Combine(artifacts, $"phantom-blade-{theme}-{language}-{size.Item1}-bottom.png"));
                        ((ScrollViewer)main.FindName("DetailScroll")).ScrollToTop(); main.UpdateLayout();
                    }
                }
            }
        } finally {
            busy.SetValue(main, originalBusy); main.Width = originalWidth; main.Height = originalHeight;
            main.SetLanguage(originalLanguage, persist: false); main.SetTheme(originalTheme, persist: false);
            main.Games[gameIndex] = game; library.SelectedItem = originalSelection; refresh.Invoke(main, null);
        }
        return new { Screen = "phantom-blade-zero", Assertions = assertions, Controls = 3, NumericRouting = true, RetainedDraft = true,
            Cleanup = true, EnglishAndChinese = true, LightAndDark = true, NoGameCommands = true };
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Capture(MainWindow main, string path) {
        var content = (FrameworkElement)main.Content;
        var target = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        target.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class FixtureModule(GameDefinition definition) : IGameModule, IFeatureValueModule
    {
        public GameDefinition Definition { get; } = definition;
        public GameInstallation Installation { get; } = new("C:/PhantomBladeUiFixture", "fixture", true, true, true, true, "Fixture only");
        public TrainerStatus Status { get; set; } = new(true, true, "Fixture only", "phantom-blade-ui-fixture", new Dictionary<string, bool>(),
            new Dictionary<string, string> { ["available_pbz_health"] = "True", ["available_pbz_stamina"] = "True", ["available_pbz_damage"] = "True", ["pbz_damage"] = "1" });
        public List<string> Commands { get; } = new();
        public List<(string, decimal)> Values { get; } = new();
        public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value) { Values.Add((featureId, value)); return Task.CompletedTask; }
        public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) {
            Commands.Add(featureId + "=" + enabled);
            var effects = new Dictionary<string, bool>(Status.Enabled); var values = new Dictionary<string, string>(Status.Values);
            if (enabled.HasValue) effects[featureId] = enabled.Value;
            if (featureId == "disable_all") { foreach (var key in effects.Keys) effects[key] = false; values["pbz_damage"] = "1"; }
            Status = Status with { Enabled = effects, Values = values }; return Task.CompletedTask;
        }
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.FromResult(Status);
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => throw new NotSupportedException();
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new NotSupportedException();
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new NotSupportedException();
        public Task LaunchAsync(GameInstallation installation) => throw new NotSupportedException();
    }
}
