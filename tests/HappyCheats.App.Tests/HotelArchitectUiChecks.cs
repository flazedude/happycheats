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

internal static class HotelArchitectUiChecks
{
    public static object Run(MainWindow main, string? artifacts)
    {
        main.SelectGame("hotel-architect");
        Check(main.Selected?.Id == "hotel-architect", "Hotel Architect must be discoverable in the library.");
        var game = main.Selected!;
        Check(game.Module is IFeatureValueModule && game.Module.Definition.AppId == "1602000", "Hotel numeric editor/Steam identity is incorrect.");
        Check(main.Features.Count == 24 && main.Features.Count(f => f.Definition.Number != null) == 20,
            "Hotel Architect must expose the 24 requested controls, including 20 numeric editors.");
        foreach (var uri in new[] { GameArtwork.IconUri(game.Id), GameArtwork.LogoUri(game.Id) })
        {
            var image = BitmapFrame.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(image.PixelWidth > 0 && image.PixelHeight > 0, "Hotel artwork must load offline.");
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
        try
        {
            Update(TrainerStatus.Offline());
            Check(main.Features.All(f => !f.CanUse), "Disconnected hotel editors must be disabled.");
            game.Installation = new GameInstallation("C:/HotelFixture", "unknown", true, false, false, false, "Unsupported build.");
            refresh.Invoke(main, null);
            Check(!main.CanInstall, "An uninspected hotel build must not permit installation.");
            var enabled = main.Features.Where(f => f.Definition.IsToggle).ToDictionary(f => f.Definition.Id, _ => false);
            var values = main.Features.ToDictionary(f => "available_" + f.Definition.Id, _ => "True");
            values["available_ha_health"] = "False";
            values["ha_money"] = "12345";
            var ready = new TrainerStatus(true, true, "Hotel fixture", "hotel-ui-fixture", enabled, values);
            Update(ready);
            Check(main.Features.Single(f => f.Definition.Id == "ha_money").ConfirmedNumberLabel.EndsWith("12345", StringComparison.Ordinal),
                "Hotel numeric editors must display the value reported by the game.");
            Check(!main.Features.Single(f => f.Definition.Id == "ha_health").CanUse
                && main.Features.Where(f => f.Definition.Id != "ha_health").All(f => f.CanUse),
                "Person editors must follow the trainer's selected-object availability.");
            foreach (var feature in main.Features.Where(f => f.Definition.Number != null))
            {
                var range = feature.Definition.Number!;
                feature.NumberText = range.DefaultValue.ToString(CultureInfo.InvariantCulture);
                Check(feature.TryGetNumber(out var actual) && actual == range.DefaultValue, "A Hotel editor rejected its valid default.");
                feature.NumberText = (range.Maximum + 1).ToString(CultureInfo.InvariantCulture);
                Check(!feature.TryGetNumber(out _), "A Hotel editor accepted an out-of-range value.");
                feature.NumberText = "NaN";
                Check(!feature.TryGetNumber(out _), "A Hotel editor accepted nonnumeric input.");
                feature.NumberText = range.DefaultValue.ToString(CultureInfo.InvariantCulture);
                feature.TryGetNumber(out _);
            }
            Update(ready, working: true);
            Check(main.Features.All(f => !f.CanUse), "Hotel controls must wait for the pending acknowledgement.");
            Update(ready with { Ready = false, Enabled = enabled.ToDictionary(p => p.Key, _ => true) });
            Check(main.CanDisableAll && main.Features.Where(f => f.Definition.IsToggle).All(f => f.CanUse)
                && main.Features.Where(f => !f.Definition.IsToggle).All(f => !f.CanUse),
                "Hotel cleanup must remain available when selected objects disappear.");
            Update(TrainerStatus.Offline());
            foreach (var language in new[] { "en", "zh" })
            foreach (var theme in new[] { "dark", "light" })
            {
                main.SetLanguage(language, persist: false);
                main.SetTheme(theme, persist: false);
                main.Width = 1180; main.Height = 820;
                ((ScrollViewer)main.FindName("DetailScroll")).ScrollToTop();
                main.UpdateLayout();
                if (language == "zh")
                    Check(main.Features.All(f => f.Name != f.Definition.Name && f.Description != f.Definition.Description),
                        "All Hotel Architect controls need Chinese labels and descriptions.");
                if (artifacts != null)
                {
                    Capture(main, Path.Combine(artifacts, $"hotel-architect-{theme}-{language}.png"));
                    ((ScrollViewer)main.FindName("DetailScroll")).ScrollToBottom();
                    main.UpdateLayout();
                    Capture(main, Path.Combine(artifacts, $"hotel-architect-bottom-{theme}-{language}.png"));
                }
            }
        }
        finally
        {
            game.Installation = originalInstallation;
            game.Status = originalStatus;
            busy.SetValue(main, originalBusy);
            refresh.Invoke(main, null);
        }
        return new { Screen = "hotel-architect", Controls = 24, NumericEditors = 20, NoGameCommands = true,
            OfflineAndSelectionAvailability = true, EnglishAndChinese = true, LightAndDark = true };
    }
    private static void Capture(MainWindow main, string path)
    {
        var content = (FrameworkElement)main.Content;
        var target = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        target.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(path);
        encoder.Save(file);
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
