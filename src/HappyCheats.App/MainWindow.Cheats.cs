using System.IO;
using System.Windows;
using System.Windows.Data;
using HappyCheats.Core;
using HappyCheats.Core.Cheats;

namespace HappyCheats.App;

public partial class MainWindow
{
    private void CheatUpdatesClicked(object sender, RoutedEventArgs e)
    {
        if (_cheatWindow != null) { _cheatWindow.Show(); _cheatWindow.Activate(); return; }
        var window = new CheatUpdateWindow(_cheatConfiguration, _cheatStore, _bundledCheatVersions,
            Games.Select(g => (g.Id, g.Name)), _cheatLoadError, _isolatedUpdates, ReloadCheatsAsync, _updateLifetime.Token)
            { Owner = this, ShowActivated = ShowActivated };
        _cheatWindow = window;
        window.Closed += (_, _) => { if (_cheatWindow == window) _cheatWindow = null; };
        window.Show();
        _ = window.CheckAsync();
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
