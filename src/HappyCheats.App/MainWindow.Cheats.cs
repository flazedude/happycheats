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
            Games.Select(g => (g.Id, g.Name)), _cheatLoadError, _isolatedUpdates, ReloadCheatsAsync, _updateLifetime.Token,
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

    private async Task ReloadCheatsAsync()
    {
        if (_cheatStore == null || _isolatedUpdates) throw new IOException(Localization.Choose("Use a configured release client to load downloaded cheats.", "请使用已配置的发布客户端加载下载的修改器。"));
        if (_busy || _installingUpdate) throw new InvalidOperationException(Localization.Choose("Wait for the current trainer operation, then retry.", "请等待当前修改器操作完成后重试。"));
        _busy = true; _controlEpoch++; _pollTimer.Stop(); _controllerTimer.Stop(); _gameOverlay?.Hide();
        RefreshBindings();
        try
        {
            while (_polling) await Task.Delay(40, _updateLifetime.Token);
            await PrepareUpdateInstallAsync();
            _cheatLoadError = null;
            var loaded = await Task.Run(() => _cheatStore.Load(ModuleCatalog.Create(), _bundledCheatVersions, UpdateLauncher.CurrentVersion,
                error => { _cheatLoadError = error; ClientErrorLog.Write("cheat-load", error); }), _updateLifetime.Token);
            var selected = _selected?.Id;
            foreach (var game in Games) CheatStore.ReleaseModule(game.Module);
            Games.Clear(); Features.Clear(); _selected = null;
            foreach (var module in loaded)
            {
                var item = new GameItem(module);
                try
                {
                    item.Installation = await module.DiscoverAsync(_paths.GetValueOrDefault(item.Id));
                    item.Status = item.Installation.IsInstalled ? await module.ReadStatusAsync(item.Installation) : TrainerStatus.Offline(item.Installation.Message);
                }
                catch (Exception error) { item.Status = TrainerStatus.Offline(error.Message); ClientErrorLog.Write("cheat-discovery", error.Message); }
                Games.Add(item);
            }
            RefreshCheatVersions(loaded: true);
            CollectionViewSource.GetDefaultView(Games).Refresh();
            Library.SelectedItem = Games.FirstOrDefault(g => g.Id == selected) ?? Games.FirstOrDefault();
            SetLocalizedMessage(() => Localization.Choose("Cheat library reloaded. Use Install / update in the affected game to apply its trainer files.", "修改器库已重新加载。请在受影响的游戏中点击“安装 / 更新”以应用修改器文件。"));
        }
        finally
        {
            _busy = false; RefreshBindings();
            if (!_captureMode && !_updateLifetime.IsCancellationRequested) { _pollTimer.Start(); _controllerTimer.Start(); }
        }
    }
}
