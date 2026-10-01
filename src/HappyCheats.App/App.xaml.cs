using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HappyCheats.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var screenshotIndex = Array.IndexOf(e.Args, "--screenshot");
        var screenshot = screenshotIndex >= 0 && screenshotIndex + 1 < e.Args.Length
            ? Path.GetFullPath(e.Args[screenshotIndex + 1]) : null;
        var rootIndex = Array.IndexOf(e.Args, "--data-root");
        var dataRoot = rootIndex >= 0 && rootIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[rootIndex + 1]) : null;
        var window = new MainWindow(screenshot != null, dataRoot);
        var themeIndex = Array.IndexOf(e.Args, "--theme");
        if (themeIndex >= 0 && themeIndex + 1 < e.Args.Length) window.SetTheme(e.Args[themeIndex + 1], persist: false);
        var gameIndex = Array.IndexOf(e.Args, "--game");
        if (e.Args.Contains("--compact")) { window.Width = 1050; window.Height = 720; }
        if (e.Args.Contains("--handheld")) { window.Width = 854; window.Height = 534; }
        if (screenshot != null)
        {
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -32000;
            window.Top = -32000;
        }
        MainWindow = window;
        window.Show();
        await window.InitializeAsync(e.Args.Contains("--preview") || e.Args.Contains("--updates-preview"));
        UpdateLauncher.ConfirmStartup(e.Args);
        var languageIndex = Array.IndexOf(e.Args, "--language");
        if (languageIndex >= 0 && languageIndex + 1 < e.Args.Length) window.SetLanguage(e.Args[languageIndex + 1], persist: false);
        if (gameIndex >= 0 && gameIndex + 1 < e.Args.Length) window.SelectGame(e.Args[gameIndex + 1]);
        if (e.Args.Contains("--scroll-bottom")) window.ScrollToInstallation();
        Window captureWindow = window;
        if (e.Args.Contains("--updates-preview"))
        {
            captureWindow = window.OpenUpdatesPreview();
            if (screenshot != null)
            {
                captureWindow.ShowActivated = false;
                captureWindow.ShowInTaskbar = false;
                captureWindow.Left = -32000;
                captureWindow.Top = -32000;
            }
        }
        if (screenshot != null)
        {
            await window.Dispatcher.InvokeAsync(new Action(() =>
            {
                try
                {
                    captureWindow.UpdateLayout();
                    var visual = (FrameworkElement)captureWindow.Content;
                    var target = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),
                        (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    target.Render(visual);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(target));
                    Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
                    using var stream = File.Create(screenshot);
                    encoder.Save(stream);
                    Shutdown(0);
                }
                catch (Exception error)
                {
                    File.WriteAllText(screenshot + ".error.txt", error.ToString());
                    Shutdown(1);
                }
            }), DispatcherPriority.ApplicationIdle);
        }
    }
}
