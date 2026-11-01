using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using HappyCheats.App;
using HappyCheats.Core;
using HappyCheats.TestSupport;

namespace HappyCheats.App.Tests;
internal static class CheatUpdateUiChecks
{
    public static object Run(MainWindow main, string? artifacts = null)
    {
        var checks=0;
        var originalContext=SynchronizationContext.Current;
        void Check(bool condition,string message) { checks++; if (!condition) throw new Exception("Cheat update UI: "+message); }
        var update=(Button)main.FindName("CheatUpdatesButton");
        Check(update.IsEnabled && Grid.GetColumn(update)==3 && update.ActualHeight>=44,"Library update button is missing, disabled or not beside Sort");
        CheckWarnings(main, update, Check, artifacts);
        using var fixture=new CheatFixture();
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http=new HttpClient(new Handler(fixture,gate.Task));
        var reloads=0;
        var window=new CheatUpdateWindow(fixture.Configuration,fixture.Store,new Dictionary<string,string>(),[("other-game", "Other game")],null,false,()=>{reloads++;return Task.CompletedTask;},CancellationToken.None,http)
            {ShowActivated=false,ShowInTaskbar=false,Owner=main,WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000};
        try
        {
            window.FocusGame("future-game", "Synthetic runtime mismatch");
            window.Show(); window.UpdateLayout();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(main.Dispatcher));
            var check=window.CheckAsync(); Pump(()=>check.IsCompleted); check.GetAwaiter().GetResult();
            Check(window.Rows.Count==1,"catalog check failed: "+window.Error);
            Check(window.FocusReason == "Synthetic runtime mismatch" && window.WindowTitle.Contains("future-game"), "game warning context was lost");
            window.FocusGame(null); Check(window.Rows.Count == 2, "all-games update view did not restore other games");
            window.FocusGame("future-game");
            var row=window.Rows.Single(); Check(row.IsUpdate && row.Id=="future-game","a new game from the signed catalog is missing");
            var version=UpdateLauncher.CurrentVersion;
            var install=window.InstallAsync(row); Pump(()=>!install.IsCompleted);
            Check(((Button)window.FindName("CheckCheatsButton")).IsEnabled && ((Button)window.FindName("ReloadCheatsButton")).IsEnabled,"busy controls became disabled");
            var repeat=window.CheckAsync(); Pump(()=>repeat.IsCompleted); Check(window.Error.Length>0,"busy click was silently ignored");
            Check(main.IsEnabled && update.IsEnabled,"module download blocked the trainer window");
            gate.SetResult(); Pump(()=>install.IsCompleted); install.GetAwaiter().GetResult();
            Check(window.Rows.Single().InstalledVersion=="1.0.0" && UpdateLauncher.CurrentVersion==version,"per-game install changed client version or failed");
            var recheck=window.CheckAsync(); Pump(()=>recheck.IsCompleted);
            Check((window.Status.Contains("No newer") || window.Status.Contains("尚无更新")) && window.Rows.Count == 1, "current game wrongly claimed a newer or compatible trainer");
            ((Button)window.FindName("ReloadCheatsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(()=>reloads==1); Check(reloads==1,"Reload library did not dispatch");
            var module=fixture.Store.Load([],new Dictionary<string,string>(),version,e=>throw new Exception(e)).Single();
            var game=new GameItem(module); var features=module.Definition.Features.Select(x=>new FeatureItem(x)).ToArray();
            var overlay=new GameOverlay(_=>Task.CompletedTask,_=>Task.CompletedTask,()=>{});
            try
            {
                var installation=module.DiscoverAsync().GetAwaiter().GetResult(); game.Status=module.ReadStatusAsync(installation).GetAwaiter().GetResult();
                foreach(var feature in features) feature.Update(game.Status,true);
                overlay.Refresh(game,features,false,true,"Synthetic downloaded game");
                Check(overlay.CurrentGameId=="future-game","downloaded module did not use the shared overlay");
            }
            finally { overlay.Close(); }
            foreach(var language in new[]{"en","zh"})
            {
                main.SetLanguage(language,false); window.RefreshLanguage(); window.UpdateLayout();
                Check(window.WindowTitle.Length>0 && window.Rows.Single().ActionLabel.Length>0,"localized update window is incomplete");
            }
            return new {suite="individual-cheat-updates",assertions=checks,validation="Signed synthetic packages and WPF; no game or physical controller"};
        }
        finally { gate.TrySetResult(); window.Close(); SynchronizationContext.SetSynchronizationContext(originalContext); }
    }
    private static void CheckWarnings(MainWindow main, Button update, Action<bool,string> check, string? artifacts)
    {
        var game = main.Games.First();
        var installation = game.Installation; var status = game.Status;
        var loaded = game.LoadedCheatVersion; var downloaded = game.DownloadedCheatVersion; var available = game.AvailableCheatVersion;
        var library = (ListBox)main.FindName("Library"); var selected = library.SelectedItem;
        try
        {
            game.Installation = new("synthetic", "123", true, true, false, true, "Compatible build");
            game.Status = TrainerStatus.Offline();
            game.LoadedCheatVersion = game.DownloadedCheatVersion = game.AvailableCheatVersion = "1.0.0";
            check(!game.HasTrainerWarning, "ordinary offline game has a mismatch warning");
            game.AvailableCheatVersion = "0.9.0";
            check(!game.HasTrainerWarning, "an older catalog caused a false update warning");
            game.Installation = game.Installation with { Compatible = false, Message = "Unsupported game build" };
            check(game.HasTrainerWarning && game.TrainerWarningReason.Contains("Unsupported"), "unsupported build did not explain its warning");
            game.Installation = game.Installation with { Compatible = true };
            foreach (var message in new[] {"Restart the game to load the current trainer module.", "Restart The Blood of Dawnwalker to load the current trainer module.",
                "Project Hospital trainer version 0.0.1 does not match 0.1.0. Close the game and choose Install / update.",
                "Invalid Civilization VI trainer status schema or runtime version.", "Restart Stardew Valley with the current HappyCheats SMAPI mod.",
                "Crimson Desert trainer identity or heartbeat is invalid. Reconnect or restart the game."})
            {
                game.Status = TrainerStatus.Offline(message);
                check(game.HasTrainerWarning, "runtime mismatch omitted: " + message);
            }
            game.Status = TrainerStatus.Offline("Waiting for a fresh trainer heartbeat.");
            check(!game.HasTrainerWarning, "an ordinary stale heartbeat was reported as a version mismatch");
            game.AvailableCheatVersion = "1.1.0";
            check(game.HasCheatUpdate && game.HasTrainerWarning, "newer signed package is not flagged");
            game.DownloadedCheatVersion = "1.1.0";
            check(game.NeedsCheatReload && game.HasTrainerWarning, "download cleared a warning before library reload");
            game.LoadedCheatVersion = "1.1.0";
            check(!game.HasTrainerWarning, "warning did not clear after loading the current compatible package");
            game.Status = TrainerStatus.Offline("Restart the game to load the current trainer module."); game.Changed();
            library.ScrollIntoView(game); main.UpdateLayout();
            var button = Descendants(library).OfType<Button>().Single(x => x.Name == "TrainerWarningButton" && ReferenceEquals(x.Tag, game));
            foreach (var theme in new[] {"dark", "light"})
            foreach (var language in new[] {"en", "zh"})
            {
                main.SetTheme(theme, false); main.SetLanguage(language, false); main.UpdateLayout();
                library.ScrollIntoView(game); main.UpdateLayout();
                button = Descendants(library).OfType<Button>().Single(x => x.Name == "TrainerWarningButton" && ReferenceEquals(x.Tag, game));
                check(button.IsVisible && button.IsEnabled && button.ActualHeight >= 44 && button.Content.ToString() == "!", "warning is hidden, disabled or too small");
                check(((SolidColorBrush)button.Background).Color == Color.FromRgb(197,47,61) && game.TrainerWarningHint.Contains(game.Name), "warning lacks red styling or per-game accessible context");
                if (artifacts != null)
                {
                    var bitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(main);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(artifacts, $"trainer-warning-{theme}-{language}.png")); encoder.Save(file);
                }
            }
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var window = main.OwnedWindows.OfType<CheatUpdateWindow>().Single();
            try
            {
                check(window.Rows.Count == 1 && window.Rows[0].Id == game.Id, "warning opened updates for a different game");
                check(((Button)window.FindName("CheckCheatsButton")).IsEnabled && window.Error.Length > 0, "isolated check did not provide a clickable actionable error");
                check(ReferenceEquals(library.SelectedItem, selected), "warning changed the active trainer session");
                update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(window.Rows.Count == main.Games.Count, "Library Update did not restore all games");
            }
            finally { window.Close(); }
        }
        finally
        {
            game.Installation = installation; game.Status = status;
            game.LoadedCheatVersion = loaded; game.DownloadedCheatVersion = downloaded; game.AvailableCheatVersion = available; game.Changed();
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i=0; i<VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i); yield return child;
            foreach(var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Pump(Func<bool> done)
    {
        if(done()) return;
        var frame=new DispatcherFrame();var elapsed=Stopwatch.StartNew();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};
        timer.Tick+=(_,_)=>{if(done()||elapsed.Elapsed>TimeSpan.FromSeconds(20))frame.Continue=false;};timer.Start();
        try{Dispatcher.PushFrame(frame);}finally{timer.Stop();}
        if(!done())throw new TimeoutException("Cheat UI fixture timed out.");
    }
    private sealed class Handler(CheatFixture fixture,Task gate):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var package=request.RequestUri!.AbsolutePath.EndsWith(".zip");
            if(package)await gate.WaitAsync(token);
            return new(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(package?fixture.Archive:fixture.Proof())};
        }
    }
}
