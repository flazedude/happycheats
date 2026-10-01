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
        var expected = new[] { "max_money", "freeze_time", "max_staff", "unlock_campaigns", "max_prestige" };
        Check(game.Module.Definition.AppId == "868360", "Project Hospital has the wrong Steam identity.");
        Check(main.Features.Select(f => f.Definition.Id).SequenceEqual(expected), "The five requested controls must appear in order.");
        Check(main.Features.Select(f => f.Definition.IsToggle).SequenceEqual(new[] { false, true, true, true, true }), "Project Hospital action/toggle types are incorrect.");
        Check(main.Features.Single(f => f.Definition.Id == "max_staff").Definition.Description.Contains("1,000", StringComparison.Ordinal)
            || main.Features.Single(f => f.Definition.Id == "max_staff").Definition.Description.Contains("1000", StringComparison.Ordinal), "Max Staff must describe the raised hiring limit.");
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
            Check(main.Features.All(f => !f.CanUse), "Project Hospital controls must remain disabled while disconnected.");
            game.Installation = new GameInstallation("C:/ProjectHospitalFixture", "unknown", true, false, false, false, "Unsupported build.");
            refresh.Invoke(main, null);
            Check(!main.CanInstall, "An uninspected Project Hospital build must not permit installation.");
            var menu = new TrainerStatus(true, true, "Main menu fixture", new string('a', 32),
                expected.Skip(1).ToDictionary(id => id, _ => false),
                expected.ToDictionary(id => "available_" + id, id => id == "unlock_campaigns" ? "True" : "False"));
            Update(menu);
            Check(main.Features.Single(f => f.Definition.Id == "unlock_campaigns").CanUse
                && main.Features.Where(f => f.Definition.Id != "unlock_campaigns").All(f => !f.CanUse), "Only campaign unlock should be available at the main menu.");
            var hospital = menu with { Values = expected.ToDictionary(id => "available_" + id, _ => "True") };
            Update(hospital);
            Check(main.Features.All(f => f.CanUse), "The loaded-hospital fixture must enable all five controls.");
            Update(hospital, working: true);
            Check(main.Features.All(f => !f.CanUse), "Commands must be gated while waiting for acknowledgement.");
            Update(hospital with { Ready = false });
            Check(main.Features.All(f => !f.CanUse), "An unready runtime must disable Project Hospital controls.");
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
                    Check(main.Features.All(f => f.Name != f.Definition.Name && f.Description != f.Definition.Description), "Project Hospital Chinese translations are missing.");
                }
                if (artifacts != null)
                {
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
        return new { Screen = "project-hospital-controls", Controls = 5, NoGameCommands = true, UnsupportedBuildBlocked = true,
            MenuAvailability = true, BusyAndOfflineGates = true, EnglishAndChinese = true, LightAndDark = true };
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
