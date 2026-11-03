using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using HappyCheats.Core;
using HappyCheats.Core.Cheats;

namespace HappyCheats.App;

public partial class MainWindow
{
    private void CheatUpdatesClicked(object sender, RoutedEventArgs e) => ShowCheatUpdates(null);

    private void TrainerWarningClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameItem game }) return;
        e.Handled = true;
        ShowCheatUpdates(game);
    }

    private void ShowCheatUpdates(GameItem? game)
    {
        if (_cheatWindow != null)
        {
            _cheatWindow.FocusGame(game?.Id, game?.TrainerWarningReason);
            _cheatWindow.Show();
            if (_cheatWindow.WindowState == WindowState.Minimized) _cheatWindow.WindowState = WindowState.Normal;
            _cheatWindow.Activate();
            _ = _cheatWindow.CheckAsync();
            return;
        }
        var window = new CheatUpdateWindow(_cheatConfiguration, _cheatStore, _bundledCheatVersions,
            () => Games, _cheatLoadError, _isolatedUpdates,
            (release, download) => ApplyCheatUpdateAsync(_cheatStore!, release, download), _updateLifetime.Token,
            checkForUpdates: CheckCheatCatalogAsync)
            { Owner = this, ShowActivated = ShowActivated };
        _cheatWindow = window;
        window.PackageInstalled += () => RefreshCheatVersions();
        window.Closed += (_, _) => { if (_cheatWindow == window) _cheatWindow = null; };
        window.FocusGame(game?.Id, game?.TrainerWarningReason);
        window.Show();
        _ = window.CheckAsync();
    }

    private Task<CheatUpdateCheck> CheckCheatCatalogAsync()
    {
        // A foreground request can join the existing read-only check. Only one check writes the catalog cache.
        if (_cheatCheckTask is { IsCompleted: false }) return _cheatCheckTask;
        return _cheatCheckTask = CheckCoreAsync();
    }

    private async Task<CheatUpdateCheck> CheckCoreAsync()
    {
        if (_isolatedUpdates || _cheatConfiguration == null || _cheatStore == null)
            throw new IOException(Localization.Choose("Open the configured release client to check trainer updates.", "请打开已配置的发布客户端检查修改器更新。"));
        var check = await new CheatUpdateClient(UpdateHttp, _cheatConfiguration).CheckAsync(_cheatStore.Root, _updateLifetime.Token);
        _cheatCatalog = check.Catalog;
        RefreshCheatVersions();
        return check;
    }

    private async Task CheckCheatsInBackgroundAsync()
    {
        if (_cheatConfiguration == null || _cheatStore == null || _isolatedUpdates) return;
        try { await CheckCheatCatalogAsync(); }
        catch (OperationCanceledException) when (_updateLifetime.IsCancellationRequested) { }
        catch (Exception error) { ClientErrorLog.Write("cheat-version-check", error.Message); }
    }

    private void RefreshCheatVersions(bool loaded = false)
    {
        try
        {
            var installed = _cheatStore?.InstalledVersions(_bundledCheatVersions) ?? _bundledCheatVersions;
            foreach (var game in Games)
            {
                if (loaded) game.LoadedCheatVersion = installed.GetValueOrDefault(game.Id);
                game.DownloadedCheatVersion = installed.GetValueOrDefault(game.Id);
                game.AvailableCheatVersion = _cheatCatalog?.Games.SingleOrDefault(x => x.Id == game.Id)?.Version;
                game.Changed();
            }
        }
        catch (Exception error) { _cheatLoadError = error.Message; ClientErrorLog.Write("cheat-version-inventory", error.Message); }
    }

    internal async Task<string> ApplyCheatUpdateAsync(CheatStore store, CheatRelease release, Func<Task> download)
    {
        if (_busy || _installingUpdate || _updateHandoff) throw new InvalidOperationException(Localization.Choose("Wait for the current trainer operation, then retry Update.", "请等待当前修改器操作完成后重新点击“更新”。"));
        _busy = true; _controlEpoch++; _pollTimer.Stop(); _controllerTimer.Stop(); _gameOverlay?.Hide();
        RefreshBindings();
        IGameModule? replacement = null;
        var previous = Games.SingleOrDefault(g => g.Id == release.Id);
        var transferred = false;
        try
        {
            while (_polling) await Task.Delay(40, _updateLifetime.Token);
            var path = previous?.Installation?.IsInstalled == true ? previous.Installation.Directory : _paths.GetValueOrDefault(release.Id);
            if (previous != null)
            {
                var before = await previous.Module.DiscoverAsync(path);
                RequireClosed(before, previous.Name);
            }
            await download();
            replacement = await Task.Run(() => store.LoadInstalled(release, UpdateLauncher.CurrentVersion), _updateLifetime.Token);
            var installation = await replacement.DiscoverAsync(path);
            if (!installation.IsInstalled && path != null) installation = await replacement.DiscoverAsync();
            if (!installation.IsInstalled)
            {
                if (previous == null)
                {
                    Games.Add(new GameItem(replacement) { Installation = installation,
                        LoadedCheatVersion = release.Version, Status = TrainerStatus.Offline(installation.Message) });
                    transferred = true;
                }
                throw new IOException(Localization.Choose(
                $"{release.Name} downloaded, but its game folder was not found. Select the game in Library, use Find in Steam or Browse folder, then click Update again to finish.",
                $"《{release.Name}》修改器已下载，但未找到游戏目录。请在游戏库中选择游戏，点击“在 Steam 中查找”或“浏览目录”，然后再次点击“更新”完成安装。"));
            }
            RequireClosed(installation, release.Name);
            if (!installation.Compatible) throw new IOException(Localization.Message(installation.Message));
            _updateLifetime.Token.ThrowIfCancellationRequested();
            // Updating one game never reloads/deactivates unrelated modules.
            previous?.Module.Deactivate();
            if (release.Id != "elden-ring")
            {
                // Elden Ring's Install is a live offline-memory connection, not file setup.
                // Updating it prepares the module; connection remains an explicit action after launch.
                await replacement.InstallAsync(installation, new Progress<string>(text => SetMessage(text)));
                installation = await replacement.DiscoverAsync(installation.Directory);
                if (!installation.TrainerInstalled || !installation.Compatible || TrainerVersionWarning.InstallationMismatch(installation))
                    throw new IOException(Localization.Choose("Trainer installation did not verify. ", "修改器安装验证未通过。") + Localization.Message(installation.Message));
            }
            var item = new GameItem(replacement) { Installation = installation,
                Status = await replacement.ReadStatusAsync(installation), LoadedCheatVersion = release.Version };
            var selected = _selected == previous && previous != null;
            if (previous != null) Games[Games.IndexOf(previous)] = item;
            else Games.Add(item);
            transferred = true;
            _cheatLoadError = null;
            if (previous != null) CheatStore.ReleaseModule(previous.Module);
            if (!_captureMode) { _paths[item.Id] = installation.Directory; SaveSettings(); }
            RefreshCheatVersions();
            item.DownloadedCheatVersion = release.Version;
            item.Changed();
            CollectionViewSource.GetDefaultView(Games).Refresh();
            if (selected) Library.SelectedItem = item;
            var message = release.Id == "elden-ring" ? Localization.Choose(
                "Elden Ring trainer updated. Use Offline launch, then Connect trainer after loading your game.", "艾尔登法环修改器已更新。请离线启动游戏，加载后点击“连接修改器”。")
                : Localization.Choose($"{release.Name} {release.Version} updated and installed. Ready to play — launch the game from Library.",
                    $"《{release.Name}》修改器 {release.Version} 已更新并安装。准备就绪，请从游戏库启动游戏。");
            SetMessage(message);
            return message;
        }
        finally
        {
            if (!transferred && replacement != null) CheatStore.ReleaseModule(replacement);
            RefreshCheatVersions();
            _busy = false; RefreshBindings();
            if (!_captureMode && !_updateLifetime.IsCancellationRequested) { _pollTimer.Start(); _controllerTimer.Start(); }
        }
    }

    private static void RequireClosed(GameInstallation installation, string name)
    {
        if (installation.IsRunning) throw new IOException(Localization.Choose(
            $"Close {name}, then click Update again. No game was closed automatically.",
            $"请关闭《{name}》后再次点击“更新”。程序不会自动关闭游戏。"));
    }
}
