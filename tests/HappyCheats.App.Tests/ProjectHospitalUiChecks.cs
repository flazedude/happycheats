using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HappyCheats.App;
using HappyCheats.Core;

namespace HappyCheats.App.Tests;

internal static class ProjectHospitalUiChecks
{
    public static object Run(MainWindow main, string? artifacts)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        main.SelectGame("project-hospital");
        Check(main.Selected?.Id == "project-hospital", "Project Hospital is missing from the library.");
        var game = main.Selected!;
        var expected = new[] { "max_money", "freeze_time", "unlimited_workers", "unlock_campaigns", "max_prestige", "patient_patience" };
        Check(game.Module.Definition.AppId == "868360", "Project Hospital has the wrong Steam identity.");
        Check(main.Features.Select(f => f.Definition.Id).SequenceEqual(expected), "The six requested controls must appear in order.");
        Check(main.Features.Select(f => f.Definition.IsToggle).SequenceEqual(new[] { false, true, true, true, true, false }), "Project Hospital action/toggle types are incorrect.");
        Check(main.Features.Single(f => f.Definition.Id == "unlimited_workers").Definition.Description.Contains("Remove the staff hiring cap"), "Unlimited workers must describe removal of the cap.");
        Check(game.Module is IFeatureValueModule && main.Features.Single(f => f.Definition.Id == "patient_patience").Definition.Number is { Minimum: 1, Maximum: 100 }, "Patient patience must use the shared numeric contract.");
        foreach (var uri in new[] { GameArtwork.IconUri(game.Id), GameArtwork.LogoUri(game.Id) })
        {
            var image = BitmapFrame.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(image.PixelWidth > 0 && image.PixelHeight > 0, "Project Hospital artwork did not load.");
        }
        var busy = typeof(MainWindow).GetField("_busy", flags)!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", flags)!;
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
            Check(Clickable(main), "Project Hospital controls must remain clickable while disconnected.");
            game.Installation = new GameInstallation("C:/ProjectHospitalFixture", "unknown", true, false, false, false, "Unsupported build.");
            refresh.Invoke(main, null);
            Check(Clickable(main), "An uninspected Project Hospital build must keep installation clickable for an actionable error.");
            var menu = new TrainerStatus(true, true, "Main menu fixture", new string('a', 32),
                expected.Skip(1).ToDictionary(id => id, _ => false),
                expected.ToDictionary(id => "available_" + id, id => id == "unlock_campaigns" ? "True" : "False"));
            Update(menu);
            Check(Clickable(main), "All controls must stay clickable at the main menu; command-time availability is checked by the module.");
            var hospital = menu with { Values = expected.ToDictionary(id => "available_" + id, _ => "True") };
            Update(hospital);
            Check(Clickable(main), "The loaded-hospital fixture must enable all six controls.");
            Update(hospital, working: true);
            Check(Clickable(main), "Controls must remain clickable while busy; the command path rejects overlapping requests.");
            Update(hospital with { Ready = false });
            Check(Clickable(main), "An unready runtime must keep Project Hospital controls clickable.");
            Update(menu);
            foreach (var language in new[] { "en", "zh" })
            foreach (var theme in new[] { "dark", "light" })
            foreach (var size in new[] { (Width: 1180, Height: 820), (Width: 854, Height: 534) })
            {
                main.SetLanguage(language, persist: false);
                main.SetTheme(theme, persist: false);
                main.Width = size.Width; main.Height = size.Height;
                ((ScrollViewer)main.FindName("DetailScroll")).ScrollToTop();
                main.UpdateLayout();
                if (language == "zh")
                {
                    Check(main.Features.All(f => (f.Name != f.Definition.Name && f.Description != f.Definition.Description) || (f.Name.Contains(" / ") && f.Description.Contains(" / "))), "Project Hospital Chinese translations are missing.");
                }
                if (artifacts != null)
                {
                    ((ScrollViewer)main.FindName("DetailScroll")).ScrollToBottom();
                    main.UpdateLayout();
                    var content = (FrameworkElement)main.Content;
                    var target = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    target.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(target));
                    using var output = File.Create(Path.Combine(artifacts, $"project-hospital-{theme}-{language}-{size.Width}x{size.Height}.png"));
                    encoder.Save(output);
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
        return new { Screen = "project-hospital-controls", Controls = 6, NoGameCommands = true, UnsupportedBuildReportedAtClick = true,
            MenuAvailability = true, BusyAndOfflineClickable = true, EnglishAndChinese = true, LightAndDark = true };
    }

    private static bool Clickable(DependencyObject root) => Descendants<Control>(root)
        .Where(control => control is System.Windows.Controls.Primitives.ButtonBase or TextBox or ComboBox or ListBox)
        .All(control => control.IsEnabled);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
