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

internal static class Civ6UiChecks
{
    public static object Run(MainWindow main, string? artifacts)
    {
        var originalSelection = main.Selected?.Id;
        var originalLanguage = Localization.Language;
        var originalTheme = main.ThemeToggleLabel == Localization.Text("themeDark") ? "light" : "dark";
        var originalWidth = main.Width;
        var originalHeight = main.Height;
        main.SelectGame("civilization-vi");
        Check(main.Selected?.Id == "civilization-vi", "Civilization VI must appear in the game library.");
        var game = main.Selected!;
        Check(game.Module is IFeatureValueModule && game.Module.Definition.AppId == "289070", "Civilization VI must expose numeric controls for its Steam identity.");
        var expected = new[] { "civ6_movement", "civ6_units", "civ6_buildings", "civ6_districts", "civ6_projects", "civ6_research", "civ6_civics",
            "civ6_damage", "civ6_moves", "civ6_can_level", "civ6_xp", "civ6_level", "civ6_gold", "civ6_faith", "civ6_influence", "civ6_favor",
            "civ6_population", "civ6_amenities", "civ6_housing" };
        var unsupported = new HashSet<string>(new[] { "civ6_level", "civ6_influence", "civ6_amenities", "civ6_housing" }, StringComparer.Ordinal);
        Check(main.Features.Select(f => f.Definition.Id).SequenceEqual(expected), "Civilization VI must retain all 19 requested cards in picture order.");
        Check(main.Features.Count(f => f.Definition.IsToggle) == 7 && main.Features.Count(f => f.Definition.Number != null) == 12,
            "Civilization VI requires seven ongoing toggles and twelve distinct numeric cards.");
        Check(main.Features.Where(f => f.Definition.IsToggle).All(f => f.NumberVisibility == Visibility.Collapsed)
            && main.Features.Where(f => !f.Definition.IsToggle).All(f => f.NumberVisibility == Visibility.Visible && f.Definition.ChoiceValueKey == f.Definition.Id),
            "Numeric editors must bind their own confirmed values and must not appear as toggles.");
        Check(main.Features.Count(f => f.HotkeyVisibility == Visibility.Visible) == 15
            && main.Features.Where(f => unsupported.Contains(f.Definition.Id)).All(f => f.HotkeyVisibility == Visibility.Collapsed)
            && main.Features.All(f => f.ChoicesVisibility == Visibility.Collapsed),
            "Civilization VI must advertise its 15 mapped hotkeys and leave unsupported editors without a shortcut.");
        foreach (var uri in new[] { GameArtwork.IconUri(game.Id), GameArtwork.LogoUri(game.Id) }) {
            var image = BitmapFrame.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(image.PixelWidth > 0 && image.PixelHeight > 0, "Civilization VI artwork must load from the bundled application.");
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", flags)!;
        var busy = typeof(MainWindow).GetField("_busy", flags)!;
        var originalBusy = busy.GetValue(main);
        var originalStatus = game.Status;
        var originalInstallation = game.Installation;
        void Update(TrainerStatus status, bool working = false)
        {
            game.Status = status;
            busy.SetValue(main, working);
            refresh.Invoke(main, null);
            main.UpdateLayout();
        }
        try {
            Update(TrainerStatus.Offline());
            Check(!main.CanDisableAll && main.Features.All(f => !f.CanUse), "Disconnected Civilization VI controls and cleanup must be disabled.");
            game.Installation = new GameInstallation("C:/Civ6UiFixture", "unknown", true, false, false, false, "Unsupported fixture.");
            refresh.Invoke(main, null);
            Check(!main.CanInstall && !main.CanRemove, "Unsupported/uninstalled Civilization VI setup must not offer installation or removal.");
            game.Installation = new GameInstallation("C:/Civ6UiFixture", "fixture", true, true, true, true, "Simulated session only.");
            refresh.Invoke(main, null);
            Check(!main.CanRemove, "Civilization VI setup cannot be removed while a game is running.");

            var enabled = main.Features.Where(f => f.Definition.IsToggle).ToDictionary(f => f.Definition.Id, _ => false);
            var values = expected.ToDictionary(id => "available_" + id, id => unsupported.Contains(id) ? "false" : "true");
            foreach (var feature in main.Features.Where(f => f.Definition.Number != null))
                values[feature.Definition.Id] = feature.Definition.Number!.DefaultValue.ToString(CultureInfo.InvariantCulture);
            values["civ6_gold"] = "12345";
            values["selected_unit_id"] = "7";
            values["selected_city_id"] = "11";
            values["player_id"] = "0";
            var ready = new TrainerStatus(true, true, "Ready: local single-player turn (UI fixture).", "civ6-ui-fixture", enabled, values);
            Update(ready);
            Check(main.Features.Count(f => f.CanUse) == 15 && main.Features.Where(f => unsupported.Contains(f.Definition.Id)).All(f => !f.CanUse),
                "Only the 15 implemented Civilization VI controls may be available.");
            Check(main.Features.Count(f => f.CanUse && f.Definition.IsToggle) == 7 && main.Features.Count(f => f.CanUse && f.Definition.Number != null) == 8,
                "Implemented controls must remain seven toggles and eight numeric editors.");
            Check(main.Features.Single(f => f.Definition.Id == "civ6_gold").ConfirmedNumberLabel.EndsWith("12345", StringComparison.Ordinal),
                "Civilization VI numeric editors must show the last confirmed game value.");
            CheckRenderedControls(main, unsupported);

            foreach (var feature in main.Features.Where(f => f.Definition.Number != null)) {
                var range = feature.Definition.Number!;
                foreach (var valid in new[] { range.Minimum, range.DefaultValue, range.Maximum }) {
                    feature.NumberText = valid.ToString(CultureInfo.InvariantCulture);
                    Check(feature.TryGetNumber(out var actual) && actual == valid, "A Civilization VI editor rejected an advertised value.");
                }
                foreach (var invalid in new[] { "NaN", "Infinity", "1e3", "1,000", "", (range.Minimum - 1).ToString(CultureInfo.InvariantCulture),
                    (range.Maximum + 1).ToString(CultureInfo.InvariantCulture), (range.Minimum + range.Step / 2).ToString(CultureInfo.InvariantCulture) }) {
                    feature.NumberText = invalid;
                    Check(!feature.TryGetNumber(out _) && feature.NumberErrorVisibility == Visibility.Visible, "A Civilization VI editor accepted invalid or fractional input.");
                }
                feature.NumberText = range.DefaultValue.ToString(CultureInfo.InvariantCulture);
                Check(feature.TryGetNumber(out _), "Civilization VI numeric validation did not recover after correcting input.");
            }

            var missingObjects = new Dictionary<string, string>(values);
            foreach (var id in new[] { "civ6_damage", "civ6_moves", "civ6_can_level", "civ6_xp", "civ6_population", "civ6_favor" }) missingObjects["available_" + id] = "false";
            Update(ready with { Values = missingObjects });
            Check(main.Features.Where(f => f.Definition.Group is "Selected unit" or "Selected city").All(f => !f.CanUse)
                && !main.Features.Single(f => f.Definition.Id == "civ6_favor").CanUse
                && main.Features.Single(f => f.Definition.Id == "civ6_gold").CanUse,
                "Selection and ruleset availability must disable only the affected Civilization VI editors.");

            Update(ready, working: true);
            Check(!main.CanDisableAll && !main.CanInstall && !main.CanRemove && main.Features.All(f => !f.CanUse),
                "Civilization VI controls must wait while command acknowledgement or setup is pending.");
            var active = new Dictionary<string, bool>(enabled) { ["civ6_movement"] = true };
            Update(ready with { Ready = false, Enabled = active });
            Check(main.CanDisableAll && main.Features.Single(f => f.Definition.Id == "civ6_movement").CanUse
                && main.Features.Where(f => f.Definition.Id != "civ6_movement").All(f => !f.CanUse),
                "Between turns, only already active toggles may be turned off; numeric edits and new toggles must remain blocked.");
            Check(main.Features.Single(f => f.Definition.Id == "civ6_movement").ActionLabel == Localization.Text("turnOff"),
                "An active Civilization VI effect must offer Turn off even outside the local active turn.");
            Update(ready with { Ready = false, Enabled = enabled });
            Check(main.CanDisableAll && main.Features.All(f => !f.CanUse), "Inactive turns must allow global cleanup without activating any feature.");
            Update(ready with { Connected = false, Ready = false, Enabled = active });
            Check(!main.CanDisableAll && main.Features.All(f => !f.CanUse), "Stale active flags must not enable disconnected Civilization VI commands.");

            Update(ready);
            foreach (var language in new[] { "en", "zh" })
            foreach (var theme in new[] { "dark", "light" }) {
                main.SetLanguage(language, persist: false);
                main.SetTheme(theme, persist: false);
                main.Width = 1180; main.Height = 820;
                var scroll = (ScrollViewer)main.FindName("DetailScroll");
                scroll.ScrollToTop(); main.UpdateLayout();
                if (language == "zh") {
                    Check(game.Name.Contains("文明", StringComparison.Ordinal) && main.Features.All(f => f.Name != f.Definition.Name && f.Description != f.Definition.Description),
                        "All Civilization VI cards require Chinese names and explanations.");
                    Check(main.Features.All(f => f.Group != f.Definition.Group), "Civilization VI feature groups need Chinese labels.");
                    Check(main.Features.Where(f => unsupported.Contains(f.Definition.Id)).All(f => f.Description.Contains("不可用", StringComparison.Ordinal)),
                        "The four unavailable editors must explain their limitation in Chinese.");
                    Check(game.HeroLine.Contains("成就", StringComparison.Ordinal) && game.HeroLine.Contains("15", StringComparison.Ordinal)
                        && game.HeroLine.Contains("4", StringComparison.Ordinal) && game.HeroLine.Contains("验证", StringComparison.Ordinal),
                        "Chinese Civilization VI overview must disclose achievement impact, limited scope, and pending gameplay verification.");
                }
                else {
                    Check(main.Features.All(f => f.Name == f.Definition.Name && f.Description == f.Definition.Description), "English Civilization VI cards must use the complete module labels.");
                    Check(main.Features.Where(f => unsupported.Contains(f.Definition.Id)).All(f => f.Description.Contains("Unavailable", StringComparison.OrdinalIgnoreCase)),
                        "The four unavailable editors must explain their limitation in English.");
                    Check(game.HeroLine.Contains("achievement", StringComparison.OrdinalIgnoreCase) && game.HeroLine.Contains("15", StringComparison.Ordinal)
                        && game.HeroLine.Contains("four", StringComparison.OrdinalIgnoreCase) && game.HeroLine.Contains("pending", StringComparison.OrdinalIgnoreCase),
                        "English Civilization VI overview must disclose achievement impact, limited scope, and pending gameplay verification.");
                }
                CheckRenderedControls(main, unsupported);
                if (artifacts != null) {
                    Capture(main, Path.Combine(artifacts, $"civilization-vi-{theme}-{language}.png"));
                    scroll.ScrollToBottom(); main.UpdateLayout();
                    Capture(main, Path.Combine(artifacts, $"civilization-vi-bottom-{theme}-{language}.png"));
                }
            }
        }
        finally {
            game.Installation = originalInstallation;
            game.Status = originalStatus;
            busy.SetValue(main, originalBusy);
            main.Width = originalWidth; main.Height = originalHeight;
            main.SetLanguage(originalLanguage, persist: false);
            main.SetTheme(originalTheme, persist: false);
            if (originalSelection != null) main.SelectGame(originalSelection);
            refresh.Invoke(main, null);
        }
        return new { Screen = "civilization-vi", Controls = 19, Implemented = 15, Unavailable = 4, Toggles = 7, NumericCards = 12,
            ImplementedNumericEditors = 8, NoGameCommands = true, InactiveTurnCleanup = true, SelectionAndRulesetAvailability = true,
            RangeValidation = true, EnglishAndChinese = true, LightAndDark = true };
    }

    private static void CheckRenderedControls(MainWindow main, HashSet<string> unsupported)
    {
        var buttons = Descendants<Button>(main).Where(b => b.Tag is FeatureItem).ToArray();
        Check(buttons.Length == 19 && buttons.All(b => b.IsEnabled), "Civilization VI action buttons must always remain clickable.");
        var inputs = Descendants<TextBox>(main).Where(t => t.DataContext is FeatureItem f && f.Definition.Number != null).ToArray();
        Check(inputs.Length == 12 && inputs.All(t => t.IsEnabled), "Civilization VI must render twelve editable numeric inputs.");
        Check(inputs.Where(t => unsupported.Contains(((FeatureItem)t.DataContext).Definition.Id)).All(t => t.IsEnabled),
            "The four unavailable raw editors must remain editable and report failures on use.");
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
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
