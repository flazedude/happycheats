using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HappyCheats.App;
using HappyCheats.Core;
using HappyCheats.TestSupport;

namespace HappyCheats.App.Tests;
internal static class CheatUpdateUiChecks
{
    public static object Run(MainWindow main)
    {
        var checks=0;
        var originalContext=SynchronizationContext.Current;
        void Check(bool condition,string message) { checks++; if (!condition) throw new Exception("Cheat update UI: "+message); }
        var update=(Button)main.FindName("CheatUpdatesButton");
        Check(update.IsEnabled && Grid.GetColumn(update)==3 && update.ActualHeight>=44,"Library update button is missing, disabled or not beside Sort");
        using var fixture=new CheatFixture();
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http=new HttpClient(new Handler(fixture,gate.Task));
        var reloads=0;
        var window=new CheatUpdateWindow(fixture.Configuration,fixture.Store,new Dictionary<string,string>(),[],null,false,()=>{reloads++;return Task.CompletedTask;},CancellationToken.None,http)
            {ShowActivated=false,ShowInTaskbar=false,Owner=main,WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000};
        try
        {
            window.Show(); window.UpdateLayout();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(main.Dispatcher));
            var check=window.CheckAsync(); Pump(()=>check.IsCompleted); check.GetAwaiter().GetResult();
            Check(window.Rows.Count==1,"catalog check failed: "+window.Error);
            var row=window.Rows.Single(); Check(row.IsUpdate && row.Id=="future-game","a new game from the signed catalog is missing");
            var version=UpdateLauncher.CurrentVersion;
            var install=window.InstallAsync(row); Pump(()=>!install.IsCompleted);
            Check(((Button)window.FindName("CheckCheatsButton")).IsEnabled && ((Button)window.FindName("ReloadCheatsButton")).IsEnabled,"busy controls became disabled");
            var repeat=window.CheckAsync(); Pump(()=>repeat.IsCompleted); Check(window.Error.Length>0,"busy click was silently ignored");
            Check(main.IsEnabled && update.IsEnabled,"module download blocked the trainer window");
            gate.SetResult(); Pump(()=>install.IsCompleted); install.GetAwaiter().GetResult();
            Check(window.Rows.Single().InstalledVersion=="1.0.0" && UpdateLauncher.CurrentVersion==version,"per-game install changed client version or failed");
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
