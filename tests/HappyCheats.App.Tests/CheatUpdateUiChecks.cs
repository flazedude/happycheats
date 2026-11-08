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
        var originalSelection=((ListBox)main.FindName("Library")).SelectedItem;
        void Check(bool condition,string message) { checks++; if (!condition) throw new Exception("Cheat update UI: "+message); }
        var update=(Button)main.FindName("CheatUpdatesButton");
        Check(update.IsEnabled && Grid.GetColumn(update)==3 && update.ActualHeight>=44,"Library update button is missing, disabled or not beside Sort");
        CheckWarnings(main, update, Check, artifacts);
        CheckFiltering(main, Check);
        CheckNewTrainers(main, Check);
        using var fixture=new CheatFixture();
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http=new HttpClient(new Handler(fixture,gate.Task));
        var gameRoot=Path.Combine(fixture.Root,"game"); Directory.CreateDirectory(gameRoot);
        File.WriteAllText(Path.Combine(gameRoot, "FutureGame.exe"), "synthetic executable");
        var oldModule=new WorkflowGame(gameRoot);
        var oldGame=new GameItem(oldModule){Installation=oldModule.DiscoverAsync().Result,LoadedCheatVersion="0.9.0"};
        main.Games.Add(oldGame);
        var other=main.Games.First(); var otherModule=other.Module;
        var window=new CheatUpdateWindow(fixture.Configuration,fixture.Store,new Dictionary<string,string>(),() => main.Games,null,false,
            (release,download)=>main.ApplyCheatUpdateAsync(fixture.Store,release,download),CancellationToken.None,http)
            {ShowActivated=false,ShowInTaskbar=false,Owner=main,WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000};
        try
        {
            window.FocusGame("future-game", "Synthetic runtime mismatch");
            window.Show(); window.UpdateLayout();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(main.Dispatcher));
            var check=window.CheckAsync(); Pump(()=>check.IsCompleted); check.GetAwaiter().GetResult();
            Check(window.Rows.Count==1,"catalog check failed: "+window.Error);
            Check(window.FocusReason == "Synthetic runtime mismatch" && window.WindowTitle.Contains("Future Game"), "game warning context was lost");
            window.FocusGame(null); Check(window.Rows.Count == 1, "all-games view included games without updates");
            window.FocusGame("future-game");
            var row=window.Rows.Single(); Check(row.IsUpdate && row.Id=="future-game","a new game from the signed catalog is missing");
            var version=UpdateLauncher.CurrentVersion;
            var install=window.InstallAsync(row); Pump(()=>!install.IsCompleted);
            Check(((Button)window.FindName("CheckCheatsButton")).IsEnabled,"busy controls became disabled");
            Check(window.FindName("ReloadCheatsButton")==null,"manual reload step is still exposed");
            var repeat=window.CheckAsync(); Pump(()=>repeat.IsCompleted); Check(window.Error.Length>0,"busy click was silently ignored");
            Check(main.IsEnabled && update.IsEnabled,"module download blocked the trainer window");
            gate.SetResult(); Pump(()=>install.IsCompleted); install.GetAwaiter().GetResult();
            Check(window.Rows.Count==0 && UpdateLauncher.CurrentVersion==version,"completed update stayed listed or changed client version");
            var installedFile=Path.Combine(gameRoot,"installed-trainer.txt");
            Check(File.ReadAllText(installedFile)=="synthetic payload","Update did not copy the trainer into the game");
            var activeGame=main.Games.Single(x=>x.Id=="future-game");
            Check(!ReferenceEquals(activeGame.Module,oldModule) && activeGame.LoadedCheatVersion=="1.0.0","Update did not load the new module automatically");
            Check(ReferenceEquals(other.Module,otherModule),"Update replaced an unrelated module");
            Check(window.Status.Contains("1.0.0") && window.Error.Length==0,"success did not show a completed installation");
            ((ListBox)main.FindName("Library")).SelectedItem=activeGame;
            File.WriteAllText(installedFile,"outdated game trainer");
            var repair=window.InstallAsync(row); Pump(()=>repair.IsCompleted);
            Check(File.ReadAllText(installedFile)=="synthetic payload","healthy cache made repair skip installation into the game");
            Check(((ListBox)main.FindName("Library")).SelectedItem is GameItem { Id: "future-game" } && main.Features.Count==3,
                "updating the selected game lost its selection or shared controls");
            var payload=Directory.GetFiles(fixture.Store.Root,"fixture.txt",SearchOption.AllDirectories).Single();
            File.WriteAllText(payload,"damaged cached payload"); File.Delete(installedFile);
            repair=window.InstallAsync(row); Pump(()=>repair.IsCompleted);
            Check(window.Error.Length==0 && File.ReadAllText(installedFile)=="synthetic payload","one click did not repair cache, reload and install together");
            Check(Directory.Exists(Path.Combine(fixture.Store.Root,"recovery")),"damaged cache was not retained for recovery");
            File.WriteAllText(Path.Combine(gameRoot,"simulate-running"),"");
            repair=window.InstallAsync(row); Pump(()=>repair.IsCompleted);
            Check(window.Error.Contains("Close") || window.Error.Contains("关闭"),"running game did not get an actionable Update error");
            File.Delete(Path.Combine(gameRoot,"simulate-running"));
            File.WriteAllText(Path.Combine(gameRoot,"simulate-install-error"),"");
            repair=window.InstallAsync(row); Pump(()=>repair.IsCompleted);
            Check(window.Error.Contains("Synthetic installation failed") && window.Rows.Single().PendingSetup,"failed installation was silently reported ready");
            File.Delete(Path.Combine(gameRoot,"simulate-install-error"));
            repair=window.InstallAsync(window.Rows.Single()); Pump(()=>repair.IsCompleted);
            Check(window.Error.Length==0 && window.Rows.Count==0,"Update retry did not finish installation or clear the row");
            var recheck=window.CheckAsync(); Pump(()=>recheck.IsCompleted);
            Check((window.Status.Contains("No new trainers") || window.Status.Contains("暂无新修改器")) && window.Rows.Count == 0, "current game wrongly claimed a newer trainer");
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
                var localizedInstall=window.InstallAsync(row); Pump(()=>localizedInstall.IsCompleted); window.UpdateLayout();
                Check(window.WindowTitle.Length>0 && window.Rows.Count==0,"localized update window retained a completed update");
                Check(window.Status.Contains(language=="en" ? "Ready to play" : "准备就绪"),"localized installation completion is missing");
                if(artifacts!=null)
                {
                    var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
                    bitmap.Render(window); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file=File.Create(Path.Combine(artifacts,$"one-click-update-{language}.png")); encoder.Save(file);
                }
            }
            return new {suite="individual-cheat-updates",assertions=checks,validation="Signed synthetic packages and WPF; no game or physical controller"};
        }
        finally
        {
            gate.TrySetResult(); window.Close();
            ((ListBox)main.FindName("Library")).SelectedItem=originalSelection;
            var game=main.Games.SingleOrDefault(x=>x.Id=="future-game");
            if(game!=null){HappyCheats.Core.Cheats.CheatStore.ReleaseModule(game.Module);main.Games.Remove(game);}
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }
    private static void CheckWarnings(MainWindow main, Button update, Action<bool,string> check, string? artifacts)
    {
        var showActivated = main.ShowActivated;
        var game = main.Games.First();
        var installation = game.Installation; var status = game.Status;
        var loaded = game.LoadedCheatVersion; var downloaded = game.DownloadedCheatVersion; var available = game.AvailableCheatVersion;
        var library = (ListBox)main.FindName("Library"); var selected = library.SelectedItem;
        try
        {
            // These are foreground button clicks, including reopening an activated window.
            // WPF rejects Show() for a maximized window with ShowActivated=false.
            main.ShowActivated = true;
            game.Installation = new("synthetic", "123", true, true, false, true, "Compatible build");
            game.Status = TrainerStatus.Offline();
            game.LoadedCheatVersion = game.DownloadedCheatVersion = game.AvailableCheatVersion = "1.0.0";
            check(!game.HasTrainerWarning, "ordinary offline game has a mismatch warning");
            game.AvailableCheatVersion = "0.9.0";
            check(!game.HasTrainerWarning, "an older catalog caused a false update warning");
            game.Installation = game.Installation with { Compatible = false, Message = "Unsupported game build" };
            check(game.HasTrainerWarning && game.TrainerWarningReason.Contains("Unsupported"), "unsupported build did not explain its warning");
            game.Installation = game.Installation with { Compatible = true };
            foreach (var message in new[] { "Trainer update required · installed files differ from module 0.4.0",
                "The Blood of Dawnwalker trainer update required. Installed files differ from the current module.",
                "Stardew Valley trainer update required. Installed mod files differ from this module." })
            {
                game.Installation = game.Installation with { Message = message };
                check(game.HasTrainerWarning && game.TrainerWarningReason == Localization.Message(message), "installed payload mismatch was missed");
            }
            game.Installation = game.Installation with { Message = "Compatible build" };
            foreach (var message in new[] {"Restart the game to load the current trainer module.", "Restart The Blood of Dawnwalker to load the current trainer module.",
                "Project Hospital trainer version 0.0.1 does not match 0.1.0. Close the game and choose Install / update.",
                "Invalid Civilization VI trainer status schema or runtime version.", "Restart Stardew Valley with the current HappyCheats SMAPI mod.",
                "Crimson Desert trainer identity or heartbeat is invalid. Reconnect or restart the game.",
                "Restart Red Dead Redemption 2 to load the current Story Mode trainer module.",
                "Invalid Witcher 3 trainer status. Restart after installing the current trainer."})
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
                check(window.Rows.Count == 0 && window.FocusReason.Length > 0, "warning lost its diagnostic context or listed an unchecked update");
                check(((Button)window.FindName("CheckCheatsButton")).IsEnabled && window.Error.Length > 0, "isolated check did not provide a clickable actionable error");
                check(ReferenceEquals(library.SelectedItem, selected), "warning changed the active trainer session");
                update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(window.Rows.Count == 0 && window.FocusReason.Length == 0, "Library Update listed unchecked games or retained a game-specific warning");
            }
            finally { window.Close(); }
        }
        finally
        {
            main.ShowActivated = showActivated;
            game.Installation = installation; game.Status = status;
            game.LoadedCheatVersion = loaded; game.DownloadedCheatVersion = downloaded; game.AvailableCheatVersion = available; game.Changed();
        }
    }
    private static void CheckFiltering(MainWindow main, Action<bool,string> check)
    {
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(main.Dispatcher));
        using var fixture = new CheatFixture();
        using var http = new HttpClient(new Handler(fixture, Task.CompletedTask));
        var directory = Path.Combine(fixture.Root, "game"); Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "FutureGame.exe"); File.WriteAllText(executable, "synthetic executable");
        var module = new WorkflowGame(directory);
        var game = new GameItem(module) { Installation = module.DiscoverAsync().Result, LoadedCheatVersion = "0.9.0" };
        var games = new List<GameItem> { game };
        var bundled = new Dictionary<string,string> { [game.Id] = "0.9.0" };
        var window = new CheatUpdateWindow(fixture.Configuration, fixture.Store, bundled, () => games, null, false,
            (_, _) => throw new Exception("Filtering must never install a package"), CancellationToken.None, http);
        try
        {
            var task = window.CheckAsync(); Pump(() => task.IsCompleted);
            check(window.Error.Length == 0 && window.Rows.Single().IsUpdate, "installed game with older trainer was hidden: " + window.Error);
            var offered = window.Rows.Single();
            bundled[game.Id] = "1.0.0"; window.FocusGame(null);
            check(window.Rows.Count == 0, "latest bundled package appeared as an update");
            bundled[game.Id] = "1.1.0"; window.FocusGame(game.Id);
            check(window.Rows.Count == 0, "older catalog package appeared in focused updates");
            bundled[game.Id] = "0.9.0"; game.LoadedCheatVersion = "1.0.0"; window.FocusGame(null);
            check(window.Rows.Count == 0, "latest loaded module appeared as an update");
            game.LoadedCheatVersion = "0.9.0";
            game.Installation = game.Installation! with { IsInstalled = false }; window.FocusGame(null);
            check(window.Rows.Count == 0, "uninstalled game appeared as an update");
            game.AvailableCheatVersion = game.DownloadedCheatVersion = "1.0.0";
            check(!game.HasCheatUpdate && !game.NeedsCheatReload, "uninstalled game retained a package update warning");
            game.Installation = module.DiscoverAsync().Result;
            File.Delete(executable); window.FocusGame(null);
            check(window.Rows.Count == 0, "game with a missing executable appeared as an update");
            task = window.InstallAsync(offered); Pump(() => task.IsCompleted);
            check(window.Error.Contains("Game installation not found") || window.Error.Contains("未找到游戏安装目录"), "stale update click did not check the game before downloading");
            Directory.Delete(directory); window.FocusGame(null);
            check(window.Rows.Count == 0, "stale discovery for a deleted game folder appeared as an update");
            Directory.CreateDirectory(directory);
            File.WriteAllText(executable, "synthetic executable");
            games.Clear(); window.FocusGame(null);
            check(window.Rows.Count == 1 && window.Rows.Single().ActionLabel == Localization.Choose("Add trainer", "添加修改器"), "catalog-only game could not be added");
            games.Add(game); game.DownloadedCheatVersion = null;
            task = fixture.Install(); Pump(() => task.IsCompleted); task.GetAwaiter().GetResult(); window.FocusGame(null);
            check(window.Rows.Count == 0, "latest downloaded package appeared as a new update");
            game.DownloadedCheatVersion = "1.0.0"; window.FocusGame(null);
            check(window.Rows.Single().PendingSetup && !window.Rows.Single().IsUpdate, "unfinished downloaded update could not be retried");
            game.LoadedCheatVersion = "1.0.0"; window.FocusGame(null);
            check(window.Rows.Count == 0, "completed downloaded update stayed listed");
            foreach (var language in new[] { "en", "zh" })
            {
                main.SetLanguage(language, false);
                task = window.CheckAsync(); Pump(() => task.IsCompleted);
                check(window.Rows.Count == 0 && window.Status.Contains(language == "en" ? "No new trainers" : "暂无新修改器"), "localized empty state is missing");
            }
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(context); }
    }

    private static void CheckNewTrainers(MainWindow main, Action<bool,string> check)
    {
        var context=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(main.Dispatcher));
        try
        {
            foreach(var scenario in new[]{"success","simulate-missing","simulate-running","simulate-install-error"})
            {
                using var fixture=new CheatFixture();
                using var http=new HttpClient(new Handler(fixture,Task.CompletedTask));
                var root=Path.Combine(fixture.Root,"game");Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"FutureGame.exe"),"fixture");
                if(scenario!="success")File.WriteAllText(Path.Combine(root,scenario),"");
                check(main.Games.All(x=>x.Id!="future-game"),"new trainer test began with a Library placeholder");
                var unrelated=main.Games.First().Module;
                CheatUpdateWindow Create()=>new(fixture.Configuration,fixture.Store,new Dictionary<string,string>(),()=>main.Games,null,false,
                    (release,download)=>main.ApplyCheatUpdateAsync(fixture.Store,release,download),CancellationToken.None,http);
                var window=Create();
                try
                {
                    var task=window.CheckAsync();Pump(()=>task.IsCompleted);
                    check(window.Rows.Count==1 && window.Error.Length==0,"unknown signed trainer was filtered out");
                    var row=window.Rows.Single();
                    check(row.InstalledVersion==null && row.ActionLabel==Localization.Choose("Add trainer","添加修改器"),"new trainer is mislabeled as an update");
                    foreach(var language in new[]{"en","zh"}) {
                        main.SetLanguage(language,false);window.RefreshLanguage();
                        check(window.Rows.Single().ActionLabel==(language=="en"?"Add trainer":"添加修改器"),"new trainer action is not localized");
                    }
                    window.FocusGame("future-game");check(window.WindowTitle.Contains("Future Game"),"new trainer lacks its catalog title");
                    task=window.InstallAsync(row);Pump(()=>task.IsCompleted);
                    check(main.Games.Count(x=>x.Id=="future-game")==1,"new trainer was not added exactly once");
                    check(ReferenceEquals(main.Games.First().Module,unrelated),"adding a trainer changed an unrelated module");
                    if(scenario!="success")
                    {
                        check(window.Error.Length>0 && window.Rows.Single().PendingSetup && !File.Exists(Path.Combine(root,"installed-trainer.txt")),"failed new trainer setup was hidden or reported complete");
                        window.Close();window=Create();task=window.CheckAsync();Pump(()=>task.IsCompleted);
                        check(window.Rows.Single().PendingSetup,"unfinished new trainer disappeared when reopening updates");
                        File.Delete(Path.Combine(root,scenario));
                        task=window.InstallAsync(window.Rows.Single());Pump(()=>task.IsCompleted);
                    }
                    check(window.Error.Length==0 && window.Rows.Count==0 && File.ReadAllText(Path.Combine(root,"installed-trainer.txt"))=="synthetic payload",$"new trainer did not finish installation ({scenario}; rows={window.Rows.Count}; path={main.Games.Single(x=>x.Id=="future-game").Installation?.Directory}): "+window.Error);
                    check(main.Games.Single(x=>x.Id=="future-game").LoadedCheatVersion=="1.0.0","added module was not active in Library");
                }
                finally
                {
                    window.Close();var added=main.Games.SingleOrDefault(x=>x.Id=="future-game");
                    if(added!=null){HappyCheats.Core.Cheats.CheatStore.ReleaseModule(added.Module);main.Games.Remove(added);}
                }
            }
        }
        finally{SynchronizationContext.SetSynchronizationContext(context);}
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
    private sealed class WorkflowGame(string directory):IGameModule
    {
        public IReadOnlyList<string> GameExecutables => ["FutureGame.exe"];
        public GameDefinition Definition=>new("future-game","Future Game","","","FG","",[]);
        public Task<GameInstallation> DiscoverAsync(string? path=null)=>Task.FromResult(new GameInstallation(directory,"synthetic",true,false,false,true,"Synthetic fixture"));
        public Task InstallAsync(GameInstallation installation,IProgress<string>? progress=null)=>Task.CompletedTask;
        public Task RemoveAsync(GameInstallation installation,IProgress<string>? progress=null)=>Task.CompletedTask;
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation)=>Task.FromResult(TrainerStatus.Offline());
        public Task SendAsync(GameInstallation installation,TrainerStatus status,string featureId,bool? enabled=null)=>Task.CompletedTask;
        public Task LaunchAsync(GameInstallation installation)=>Task.CompletedTask;
    }
}
