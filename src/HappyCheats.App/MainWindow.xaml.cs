using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HappyCheats.Core;
using HappyCheats.Core.Updates;
using Microsoft.Win32;

namespace HappyCheats.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _controllerTimer = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private readonly ControllerInput _controller = new();
    private readonly HashSet<int> _controllerProcessIds = new();
    private GameOverlay? _gameOverlay;
    private Civ6KeyboardHook? _civ6KeyboardHook;
    private bool _overlayRequested;
    private readonly GameMenuShortcut _menuShortcut = new();
    private IntPtr _gameWindow;
    private bool _controllerEnabled = true;
    private string _controllerLabel = "Connect an Xbox controller";
    private readonly bool _captureMode;
    private bool _isolatedUpdates;
    private static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly CancellationTokenSource _updateLifetime = new();
    private CancellationTokenSource? _startupUpdateCheck;
    private UpdateConfiguration? _updateConfiguration;
    private string? _updateConfigurationError;
    private ReleaseManifest? _availableRelease;
    private UpdateWindow? _updateWindow;
    private bool _updateHandoff;
    private bool _busy, _polling, _initialized;
    private bool _witcherControlPending;
    private int _pollNumber;
    private long _controlEpoch;
    private string _message = "Finding your games in Steam…";
    private Func<string>? _localizedMessage;
    private bool _messageIsError;
    private GameItem? _selected;
    private string _librarySort = "az";
    private Dictionary<string, string> _paths = new();
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HappyCheats", "settings.json");
    public ObservableCollection<GameItem> Games { get; } = new();
    public bool LibrarySortAscending => _librarySort == "az";
    public bool LibrarySortDescending => _librarySort == "za";
    public ObservableCollection<FeatureItem> Features { get; } = new();
    public UiLabels Labels { get; } = new();
    public bool IsCompactLayout => ActualWidth < 1100;
    public GridLength SidebarWidth => new(IsCompactLayout ? 200 : 238);
    public Brush ChineseLanguageBrush => Localization.Chinese ? ThemeManager.Brush("SelectionBrush") : Brushes.Transparent;
    public Brush EnglishLanguageBrush => !Localization.Chinese ? ThemeManager.Brush("SelectionBrush") : Brushes.Transparent;
    public Brush ChineseLanguageInk => ThemeManager.Brush(Localization.Chinese ? "SelectionInkBrush" : "MutedBrush");
    public Brush EnglishLanguageInk => ThemeManager.Brush(!Localization.Chinese ? "SelectionInkBrush" : "MutedBrush");
    public string ThemeToggleLabel => ThemeManager.IsLight ? Localization.Text("themeDark") : Localization.Text("themeLight");
    public string ThemeToggleHint => ThemeManager.IsLight ? Localization.Text("switchDark") : Localization.Text("switchLight");
    public GameItem? Selected => _selected;
    public string Message => _localizedMessage?.Invoke() ?? Localization.Message(_message);
    public Brush MessageBrush => ThemeManager.Brush(_messageIsError ? "ErrorBrush" : "MutedBrush");
    public bool CanManage => !_busy;
    public string ClientVersionLabel => "HappyCheats  /  " + UpdateLauncher.CurrentVersion;
    public Visibility UpdateBadgeVisibility => _availableRelease == null ? Visibility.Collapsed : Visibility.Visible;
    public bool CanLaunch => !_busy && _selected?.Installation?.IsInstalled == true
        && (_selected.Id != "elden-ring" || _selected.Installation is { Compatible: true, IsRunning: false });
    public bool CanInstall => !_busy && _selected?.Installation is { IsInstalled: true, Compatible: true };
    public bool CanRemove => !_busy && (_selected?.Id == "elden-ring" ? _selected.Status.Connected
        : _selected?.Installation is { TrainerInstalled: true, IsRunning: false });
    public string LaunchLabel => _selected?.Id == "elden-ring" ? Localization.Choose("Offline launch", "离线启动") : Localization.Text("launch");
    public string InstallLabel => _selected?.Id == "elden-ring" ? Localization.Choose("Connect trainer", "连接修改器") : Localization.Text("install");
    public string RemoveLabel => _selected?.Id == "elden-ring" ? Localization.Choose("Disconnect trainer", "断开修改器") : Localization.Text("remove");
    private bool GtaStorySession => _selected?.Id == "gta-v-enhanced" && _selected.Status.Connected
        && _selected.Status.Values.TryGetValue("gta_online", out var online) && online == bool.FalseString;
    public bool CanDisableAll => !_busy && (_selected?.Id != "gta-v-enhanced" || GtaStorySession)
        && (_selected?.Status.Ready == true || GtaStorySession || _selected?.Id is "dawnwalker" or "black-myth-wukong" or "phantom-blade-zero" or "red-dead-redemption-2" or "hotel-architect" or "civilization-vi" or "civilization-vii" or "witcher-3" or "elden-ring" or "astroneer" && _selected.Status.Connected) && Features.Any(f => f.Definition.IsToggle);
    private bool CrimsonInitializationFailed => _selected?.Id == "crimson-desert" && _selected.Status.Connected && !_selected.Status.Ready
        && _selected.Status.Values.TryGetValue("diagnostic_crimson_initialization_failed", out var failed) && failed == bool.TrueString;
    private bool WitcherPaused => _selected?.Id == "witcher-3" && _selected.Status.Connected
        && _selected.Status.Values.TryGetValue("witcher3_waiting_for_game", out var waiting) && waiting == bool.TrueString;
    public string ConnectionLabel => WitcherPaused ? Localization.Choose("Game paused", "游戏已暂停")
        : Localization.Text(_busy ? "working" : CrimsonInitializationFailed ? "initializationFailed" : _selected?.Status.Connected == true ? (_selected.Status.Ready ? "connected" : "waitingGameplay") : "disconnected");
    public Brush ConnectionBrush => ThemeManager.Brush(_selected?.Status.Ready == true && !_busy && !WitcherPaused ? "SuccessBrush" : "MutedBrush");
    public string FeatureCount => Features.Count.ToString("00");
    public string ControlsHint
    {
        get
        {
            if (WitcherPaused) return Localization.Message(_selected!.Status.Message);
            if (_selected?.Id == "elden-ring" && _selected.Status.Ready) return Localization.Choose(
                "Keep this page open for refills. F1–F4 apply controls; F12 stops refills. Gameplay effects remain unverified.",
                "持续恢复时请保持本页开启。F1–F4 使用功能，F12 停止持续恢复。尚待游戏内验证。");
            if (_selected?.Status.Ready == true) return Localization.Text("readyHint");
            var reason = _selected?.Status.Message;
            return string.IsNullOrWhiteSpace(reason) ? Localization.Text("offlineHint") : Localization.Message(reason);
        }
    }
    public string InstallationPath => _selected?.Installation is { IsInstalled: true } installation ? installation.Directory : Localization.Text("folderMissing");
    public string InstallationLabel => _selected?.Id == "elden-ring" ? Localization.Choose("No game files installed", "不安装游戏文件")
        : Localization.Text(_selected?.Installation?.TrainerInstalled == true ? "trainerInstalled" : "trainerMissing");
    public string BuildLabel => _selected?.Installation is { IsInstalled: true } installation ? $"{Localization.Choose("Steam build", "Steam 版本")} {Localization.Message(installation.Build)}  ·  {Localization.Message(installation.Message)}" : Localization.Text("steamDiscovery");
    public string ValuesLine => _selected == null ? "" : FormatValues(_selected.Status.Values);
    public Visibility ValuesVisibility => string.IsNullOrEmpty(ValuesLine) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ControllerVisibility => _selected != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DawnwalkerVisibility => _selected?.Id == "dawnwalker" ? Visibility.Visible : Visibility.Collapsed;
    public string ControllerDirections => _selected?.Id == "dawnwalker"
        ? DawnwalkerControllerHints.Directions(_controller.DawnwalkerPage)
        : _selected?.Id == "onimusha" ? Localization.Text("controllerDirections")
        : Localization.Choose("Hold Menu (☰) for 0.35 seconds, then tap RB to show / hide the menu.", "按住 Menu（☰）0.35 秒，再按 RB 显示 / 隐藏菜单。");
    public string ControllerBumpers => _selected?.Id == "dawnwalker"
        ? Localization.Choose("Hold Menu: X  Auto teleport · Y  Teleport · View  Next page · LB  Disable all · RB  Menu", "按住 Menu：X 自动传送 · Y 传送 · View 下一页 · LB 全部关闭 · RB 菜单")
        : _selected?.Id == "onimusha" ? Localization.Text("controllerBumpers") : "";
    public string OverlayLabel => Localization.Choose("In-game menu · Ctrl+Del", "游戏内菜单 · Ctrl+Del");
    public string OverlayHint => Localization.Choose("Select this game and keep HappyCheats open. Use windowed or borderless mode. Click the menu to edit; close it to return to the game.", "选择本游戏并保持 HappyCheats 运行。使用窗口或无边框模式。点击菜单进行编辑，关闭菜单返回游戏。");
    public string ControllerLabel => !_controllerEnabled ? Localization.Text("controllerDisabled") : _controller.Connected
        ? Localization.Choose($"Controller {_controller.Slot + 1} connected", $"手柄 {_controller.Slot + 1} 已连接") : Localization.Text("controllerMissing");
    public bool ControllerEnabled
    {
        get => _controllerEnabled;
        set
        {
            _controllerEnabled = value;
            _controller.Poll(false);
            _paths["__xbox_shortcuts"] = value.ToString();
            SaveSettings();
            Notify();
        }
    }

    public MainWindow(bool captureMode = false, string? dataRoot = null, string? settingsPath = null)
    {
        _captureMode = captureMode;
        if (settingsPath != null) _settingsPath = Path.GetFullPath(settingsPath);
        _isolatedUpdates = captureMode || dataRoot != null;
        foreach (var module in ModuleCatalog.Create(dataRoot)) Games.Add(new GameItem(module));
        InitializeComponent();
        if (!captureMode) WindowLayout.Attach(this);
        SizeChanged += (_, _) => { Notify(nameof(IsCompactLayout)); Notify(nameof(SidebarWidth)); };
        DataContext = this;
        try
        {
            if (File.Exists(_settingsPath)) _paths = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_settingsPath)) ?? new();
        }
        catch { /* A saved folder is optional. Discovery can recover stale settings. */ }
        ThemeManager.Apply(_paths.GetValueOrDefault("__theme", "dark"));
        ThemeManager.Attach(this);
        ThemeManager.Changed += ThemeChanged;
        Localization.Language = _paths.TryGetValue("__language", out var language) && language == "en" ? "en" : "zh";
        Labels.Refresh();
        if (_paths.TryGetValue("__xbox_shortcuts", out var controllerSetting) && bool.TryParse(controllerSetting, out var enabled)) _controllerEnabled = enabled;
        SetLibrarySort(_paths.GetValueOrDefault("__library_sort", "az"), persist: false);
        Library.SelectedIndex = 0;
        _pollTimer.Tick += async (_, _) => await PollAsync();
        _controllerTimer.Tick += async (_, _) => await PollControllerAsync();
        Closing += (_, e) => { if (_updateWindow?.IsWorking == true && !_updateHandoff) { e.Cancel = true; _updateWindow.Activate(); } };
        Closed += (_, _) => { ThemeManager.Changed -= ThemeChanged; _pollTimer.Stop(); _controllerTimer.Stop(); _civ6KeyboardHook?.Dispose(); _gameOverlay?.Close(); _updateLifetime.Cancel(); _updateWindow?.CancelPending(); foreach (var game in Games) if (game.Module is EldenRingModule module) module.Stop(); };
    }

    public async Task InitializeAsync(bool preview = false)
    {
        if (_initialized) return;
        _initialized = true;
        _isolatedUpdates |= preview;
        try { _updateConfiguration = UpdateConfiguration.Load(AppContext.BaseDirectory); }
        catch (Exception error) { _updateConfigurationError = error.Message; }
        if (preview) { SetMessage("Preview: launch a game to connect its trainer."); return; }
        _busy = true;
        RefreshBindings();
        await Task.WhenAll(Games.Select(async game =>
        {
            try
            {
                _paths.TryGetValue(game.Id, out var path);
                try { game.Installation = await game.Module.DiscoverAsync(path); }
                catch when (path != null) { game.Installation = await game.Module.DiscoverAsync(); }
                if (!game.Installation.IsInstalled && path != null) game.Installation = await game.Module.DiscoverAsync();
                game.Status = game.Installation.IsInstalled ? await game.Module.ReadStatusAsync(game.Installation) : TrainerStatus.Offline(game.Installation.Message);
                game.Changed();
            }
            catch (Exception error) { game.Status = TrainerStatus.Offline(error.Message); }
        }));
        _busy = false;
        SetMessage(_selected?.Status.Message ?? "Choose a game to get started.");
        RefreshBindings();
        if (!_captureMode)
        {
            _pollTimer.Start(); _controllerTimer.Start();
            try
            {
                _civ6KeyboardHook = new Civ6KeyboardHook(CivilizationShortcutScope,
                    request => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(async () => await DispatchCivilizationShortcutAsync(request))));
            }
            catch (Win32Exception error)
            {
                SetLocalizedMessage(() => Localization.Choose(
                    $"In-game Civilization VI / VII hotkeys could not start ({error.Message}). Function keys still work in the HappyCheats window.",
                    $"无法启用《文明 VI / VII》游戏内快捷键（{error.Message}）。功能键仍可在 HappyCheats 窗口中使用。"));
            }
        }
        if (!_isolatedUpdates && _updateConfiguration != null) _ = CheckUpdatesInBackgroundAsync();
    }

    private async Task CheckUpdatesInBackgroundAsync()
    {
        _startupUpdateCheck = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
        try
        {
            var client = new UpdateClient(UpdateHttp, _updateConfiguration!);
            _startupUpdateCheck.CancelAfter(client.CheckTimeout);
            var release = await client.CheckAsync(UpdateLauncher.CurrentVersion, _startupUpdateCheck.Token);
            if (!_startupUpdateCheck.IsCancellationRequested) UpdateReleaseChanged(release);
        }
        catch { /* A failed background check stays quiet; the Updates dialog offers retry and details. */ }
    }

    private void UpdateReleaseChanged(ReleaseManifest? release)
    {
        _availableRelease = release;
        Notify(nameof(UpdateBadgeVisibility));
    }

    private async void UpdatesClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _controlEpoch++;
        _busy = true;
        _pollTimer.Stop();
        _controllerTimer.Stop();
        _gameOverlay?.Hide();
        _controller.Poll(false);
        _startupUpdateCheck?.Cancel();
        RefreshBindings();
        try
        {
            // Let an already-running read finish before the updater can hand off the client.
            while (_polling) await Task.Delay(40, _updateLifetime.Token);
            _updateWindow = new UpdateWindow(UpdateHttp, _updateConfiguration, _availableRelease,
                _updateConfigurationError, _isolatedUpdates, PrepareUpdateInstallAsync, UpdateReleaseChanged) { Owner = this };
            _updateWindow.ShowDialog();
        }
        catch (OperationCanceledException) { }
        finally
        {
            _updateWindow = null;
            if (!_updateHandoff && !_updateLifetime.IsCancellationRequested)
            {
                _busy = false;
                RefreshBindings();
                if (!_captureMode) { _pollTimer.Start(); _controllerTimer.Start(); }
            }
        }
    }

    private async Task PrepareUpdateInstallAsync()
    {
        var running = new List<string>();
        foreach (var game in Games)
        {
            var path = game.Installation?.IsInstalled == true ? game.Installation.Directory
                : _paths.GetValueOrDefault(game.Id);
            GameInstallation current;
            try
            {
                current = await game.Module.DiscoverAsync(path);
                if (!current.IsInstalled && path != null) current = await game.Module.DiscoverAsync();
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(Localization.Choose(
                    $"Could not check whether {game.Name} is running. Close supported games and retry.",
                    $"无法确认《{game.Name}》是否正在运行。请关闭支持的游戏后重试。"), error);
            }
            if (current.IsRunning) running.Add(game.Name);
        }
        if (running.Count != 0) throw new InvalidOperationException(Localization.Choose(
            "Close these games before updating HappyCheats: ", "更新 HappyCheats 前，请先关闭这些游戏：") + string.Join("、", running));
    }

    internal void BeginUpdateHandoff(StagedUpdate update)
    {
        UpdateLauncher.Start(update);
        _updateHandoff = true;
        Application.Current.Shutdown();
    }

    public Window OpenUpdatesPreview()
    {
        _isolatedUpdates = true;
        _startupUpdateCheck?.Cancel();
        var preview = UpdateWindow.CreatePreview(UpdateHttp);
        preview.Owner = this;
        preview.ShowActivated = ShowActivated;
        preview.ShowInTaskbar = ShowInTaskbar;
        if (_captureMode)
        {
            preview.WindowStartupLocation = WindowStartupLocation.Manual;
            preview.Left = -32000;
            preview.Top = -32000;
        }
        preview.Show();
        return preview;
    }

    public void SelectGame(string id)
    {
        var game = Games.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
        if (game != null) Library.SelectedItem = game;
    }

    public void SetLanguage(string language, bool persist = true)
    {
        Localization.Language = language == "en" ? "en" : "zh";
        if (persist) { _paths["__language"] = Localization.Language; SaveSettings(); }
        Labels.Refresh();
        foreach (var game in Games) game.Changed();
        RefreshBindings();
        CollectionViewSource.GetDefaultView(Games).Refresh();
    }

    private void LanguageClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string language }) SetLanguage(language);
    }

    public void SetTheme(string theme, bool persist = true)
    {
        var selected = theme == "light" ? "light" : "dark";
        ThemeManager.Apply(selected);
        if (persist) { _paths["__theme"] = selected; SaveSettings(); }
    }

    private void ThemeClicked(object sender, RoutedEventArgs e) => SetTheme(ThemeManager.IsLight ? "dark" : "light");

    private void ThemeChanged(object? sender, EventArgs e)
    {
        foreach (var game in Games) game.Changed();
        RefreshBindings();
    }

    public void ScrollToInstallation() { UpdateLayout(); DetailScroll.ScrollToEnd(); }

    private void GameSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Library.SelectedItem is not GameItem game) return;
        _controlEpoch++;
        if (_selected != game && _selected?.Module is EldenRingModule previous)
        {
            previous.Stop();
            _selected.Status = TrainerStatus.Offline("Elden Ring trainer disconnected. Connect again to enable controls.");
        }
        _selected = game;
        _overlayRequested = false;
        _gameWindow = IntPtr.Zero;
        _gameOverlay?.Hide();
        _controller.Profile = game.Id is "onimusha" or "dawnwalker" ? game.Id : "shared";
        _controllerProcessIds.Clear();
        _controller.Poll(false);
        DetailScroll.ScrollToTop();
        Features.Clear();
        foreach (var definition in game.Module.Definition.Features.Where(f => f.Id != "disable_all"))
        {
            var feature = new FeatureItem(definition);
            Features.Add(feature);
        }
        SetMessage(_initialized ? game.Status.Message : "Finding your games in Steam…");
        RefreshBindings();
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (Library == null) return;
        var view = CollectionViewSource.GetDefaultView(Games);
        view.Filter = item => item is GameItem game && game.SearchText.Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void SetLibrarySort(string sort, bool persist = true)
    {
        _librarySort = sort == "za" ? "za" : "az";
        var view = CollectionViewSource.GetDefaultView(Games);
        using (view.DeferRefresh())
        {
            // English titles give A–Z the same meaning in either interface language.
            view.Culture = CultureInfo.InvariantCulture;
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(GameItem.SortName),
                LibrarySortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription(nameof(GameItem.Id), ListSortDirection.Ascending));
        }
        if (persist) { _paths["__library_sort"] = _librarySort; SaveSettings(); }
        Notify(nameof(LibrarySortAscending));
        Notify(nameof(LibrarySortDescending));
    }

    private void LibrarySortChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string sort } && sort != _librarySort && CanManage) SetLibrarySort(sort);
    }

    private async Task PollAsync()
    {
        if (_busy || _polling || _selected?.Installation is not { IsInstalled: true } installation) return;
        _polling = true;
        var game = _selected;
        var epoch = _controlEpoch;
        try
        {
            if (++_pollNumber % 10 == 0)
            {
                var discovered = await game.Module.DiscoverAsync(installation.Directory);
                if (_busy || epoch != _controlEpoch) return;
                game.Installation = discovered; game.Changed();
            }
            var previous = game.Status;
            var current = await game.Module.ReadStatusAsync(installation);
            if (_busy || epoch != _controlEpoch) return;
            if (game == _selected)
            {
                var controls = await GameWindowLocator.ReadAsync(game.Module, installation);
                if (_busy || epoch != _controlEpoch) return;
                if (game == _selected)
                {
                    _controllerProcessIds.Clear();
                    _controllerProcessIds.UnionWith(controls.ProcessIds);
                    _gameWindow = controls.Window;
                }
            }
            game.Status = current;
            if (game == _selected)
            {
                if (previous.Connected != game.Status.Connected || previous.Ready != game.Status.Ready
                    || game.Id is "crimson-desert" or "astroneer" && previous.SessionId != game.Status.SessionId
                    || game.Id is "civilization-vi" or "civilization-vii" or "witcher-3" or "elden-ring" or "astroneer" && previous.Message != game.Status.Message) SetMessage(game.Status.Message);
                else if (game.Id == "crimson-desert" && game.Status.Connected
                    && game.Status.Values.TryGetValue("crimson_teleport_status", out var teleportStatus)
                    && (!previous.Values.TryGetValue("crimson_teleport_status", out var previousTeleport) || teleportStatus != previousTeleport))
                    ShowCrimsonTeleportStatus(game.Status);
                RefreshBindings();
            }
        }
        catch (Exception error)
        {
            if (epoch != _controlEpoch) return;
            if (game == _selected) { _controllerProcessIds.Clear(); _gameWindow = IntPtr.Zero; }
            game.Status = TrainerStatus.Offline(error.Message);
            if (game == _selected) { SetMessage(error.Message, true); RefreshBindings(); }
        }
        finally { _polling = false; }
    }

    private async Task RunOperationAsync(Func<GameItem, Task> action, string working)
    {
        if (_busy || _selected == null) return;
        var game = _selected;
        _controlEpoch++;
        _busy = true;
        SetMessage(working);
        RefreshBindings();
        try { await action(game); }
        catch (Exception error) { SetMessage(error.Message, true); }
        finally { _busy = false; RefreshBindings(); }
    }

    private async Task DiscoverAsync(GameItem game, string? directory = null)
    {
        game.Installation = await game.Module.DiscoverAsync(directory);
        game.Status = game.Installation.IsInstalled ? await game.Module.ReadStatusAsync(game.Installation) : TrainerStatus.Offline(game.Installation.Message);
        if (game.Installation.IsInstalled && !_captureMode)
        {
            _paths[game.Id] = game.Installation.Directory;
            SaveSettings();
        }
        game.Changed();
    }

    private void SaveSettings()
    {
        if (_captureMode) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!); File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_paths)); } catch { }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    private void OverlayClicked(object sender, RoutedEventArgs e) => ToggleGameOverlay();

    private void ToggleGameOverlay()
    {
        if (_selected == null || _captureMode || _updateHandoff || _updateWindow != null) return;
        if (_overlayRequested) { DismissGameOverlay(); return; }
        if (_gameWindow == IntPtr.Zero)
        {
            SetLocalizedMessage(() => Localization.Choose("No unique game window found. Launch the selected game in windowed or borderless mode, then press Ctrl+Del.", "未找到唯一的游戏窗口。请以窗口或无边框模式启动所选游戏，然后按 Ctrl+Del。"));
            return;
        }
        _overlayRequested = !_overlayRequested;
        if (_overlayRequested)
        {
            if (_gameOverlay == null)
            {
                _gameOverlay = new GameOverlay(SendOverlayFeatureAsync, SendChoiceAsync, DismissGameOverlay);
                _gameOverlay.PreviewKeyDown += WindowKeyDown;
                _gameOverlay.Closed += (_, _) => { _gameOverlay = null; _overlayRequested = false; };
            }
            RefreshOverlay();
        }
    }

    private void DismissGameOverlay()
    {
        var returnFocus = _gameOverlay?.IsActive == true;
        _overlayRequested = false;
        _gameOverlay?.Hide();
        // Return focus only after the user interacted with this menu, never on a background poll.
        if (returnFocus && _gameWindow != IntPtr.Zero) SetForegroundWindow(_gameWindow);
    }

    private Task SendOverlayFeatureAsync(FeatureDefinition feature)
    {
        if (_gameOverlay?.CurrentGameId != _selected?.Id) return Task.CompletedTask;
        if (feature.Id == "disable_all") return CanDisableAll ? SendFeatureAsync(feature) : Task.CompletedTask;
        return Features.Any(item => ReferenceEquals(item.Definition, feature) && item.CanUse)
            ? SendFeatureAsync(feature) : Task.CompletedTask;
    }

    private void RefreshOverlay()
    {
        if (_selected != null) _gameOverlay?.Refresh(_selected, Features, _busy, CanDisableAll, Message,
            _selected.Id == "dawnwalker" ? ControllerDirections + "\n" + ControllerBumpers : "");
    }

    private void SelectDawnwalkerPage(int page)
    {
        _controller.SelectDawnwalkerPage(page);
        Notify(nameof(ControllerDirections));
        RefreshOverlay();
    }

    internal bool IsControllerMenuScope(IntPtr foregroundWindow)
    {
        if (_captureMode || _selected == null || _gameWindow == IntPtr.Zero || foregroundWindow == IntPtr.Zero
            || _updateHandoff || _updateWindow != null) return false;
        var overlayHandle = _gameOverlay == null ? IntPtr.Zero : new WindowInteropHelper(_gameOverlay).Handle;
        return foregroundWindow == _gameWindow || foregroundWindow == overlayHandle
            || foregroundWindow == new WindowInteropHelper(this).Handle;
    }

    private async Task PollControllerAsync()
    {
        if (_captureMode) return;
        var foregroundWindow = GetForegroundWindow();
        GetWindowThreadProcessId(foregroundWindow, out var foreground);
        bool focused = foreground == Environment.ProcessId || _controllerProcessIds.Contains((int)foreground);
        var isDawnwalker = _selected?.Id == "dawnwalker";
        var menuScope = IsControllerMenuScope(foregroundWindow);
        if (_menuShortcut.Poll((GetAsyncKeyState(0x11) & 0x8000) != 0, (GetAsyncKeyState(GameMenuShortcut.VirtualKey) & 0x8000) != 0,
            (GetAsyncKeyState(0x75) & 0x8000) != 0, menuScope, isDawnwalker)) ToggleGameOverlay();
        _gameOverlay?.Follow(_gameWindow, _overlayRequested && menuScope);
        var action = _controller.Poll(_controllerEnabled && menuScope,
            _controllerEnabled && _selected?.Id is "onimusha" or "dawnwalker"
            && (_selected.Status.Ready || isDawnwalker) && !_busy && !_updateHandoff && _updateWindow == null && focused);
        var label = !_controllerEnabled ? "Shortcuts disabled" : _controller.Connected ? $"Controller {_controller.Slot + 1} connected" : "Connect an Xbox controller";
        if (_controllerLabel != label) { _controllerLabel = label; Notify(nameof(ControllerLabel)); }
        if (action != null) await DispatchControllerActionAsync(action);
    }

    internal async Task DispatchControllerActionAsync(string action)
    {
        if (!_controllerEnabled || _captureMode || _selected == null || _updateHandoff || _updateWindow != null) return;
        // Closing the menu must stay available while a command is waiting on the game.
        if (action == "game_menu")
        {
            if (_gameWindow != IntPtr.Zero) ToggleGameOverlay();
            return;
        }
        if (_busy || _selected.Id is not ("onimusha" or "dawnwalker")) return;
        var isDawnwalker = _selected.Id == "dawnwalker";
        if (!_selected.Status.Ready && !isDawnwalker) return;
        if (isDawnwalker && action == "dawnwalker_page")
        {
            Notify(nameof(ControllerDirections));
            if (!_overlayRequested) ToggleGameOverlay();
            else RefreshOverlay();
            return;
        }
        var feature = _selected?.Module.Definition.Features.FirstOrDefault(f => f.Id == action);
        if (feature != null && Features.Any(f => f.Definition.Id == action && f.CanUse)) await SendFeatureAsync(feature);
        else if (action == "disable_all") await SendFeatureAsync(new FeatureDefinition("disable_all", "Disable all", "", ""));
    }

    private async void RescanClicked(object sender, RoutedEventArgs e) => await RunOperationAsync(async game =>
    {
        await DiscoverAsync(game);
        SetMessage(game.Installation!.IsInstalled ? "Game found. " + game.Status.Message : game.Installation.Message);
    }, "Scanning your Steam libraries…");

    private async void BrowseClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Localization.Text("folderDialog"), Multiselect = false };
        if (_selected?.Installation is { IsInstalled: true } installation) dialog.InitialDirectory = installation.Directory;
        if (dialog.ShowDialog(this) != true) return;
        await RunOperationAsync(async game => { await DiscoverAsync(game, dialog.FolderName); SetMessage(game.Status.Message); }, "Checking the selected game folder…");
    }

    private async void InstallClicked(object sender, RoutedEventArgs e) => await RunOperationAsync(async game =>
    {
        if (game.Installation == null) return;
        await game.Module.InstallAsync(game.Installation, new Progress<string>(text => SetMessage(text)));
        await DiscoverAsync(game, game.Installation.Directory);
        SetMessage(game.Id == "elden-ring" ? game.Status.Message : "Trainer installed. " + game.Status.Message);
    }, _selected?.Id == "elden-ring" ? "Connecting the offline trainer…" : "Installing trainer files…");

    private async void RemoveClicked(object sender, RoutedEventArgs e) => await RunOperationAsync(async game =>
    {
        if (game.Installation == null) return;
        await game.Module.RemoveAsync(game.Installation, new Progress<string>(text => SetMessage(text)));
        await DiscoverAsync(game, game.Installation.Directory);
        SetMessage(game.Id == "elden-ring" ? "Elden Ring trainer disconnected. Connect again to enable controls." : game.Installation!.TrainerInstalled
            ? "Existing trainer files were preserved. This launcher can remove only files it owns."
            : "Trainer removed from the game folder.");
    }, _selected?.Id == "elden-ring" ? "Disconnecting the trainer…" : "Removing trainer files…");

    private async void LaunchClicked(object sender, RoutedEventArgs e) => await RunOperationAsync(async game =>
    {
        if (game.Installation == null) return;
        await game.Module.LaunchAsync(game.Installation);
        SetMessage(game.Id == "elden-ring" ? "Offline Elden Ring launch requested. Load a single-player save, then connect the trainer." : game.Id == "stardew" ? "Launching with SMAPI. Load a single-player save to connect." : "Launching through Steam. Load your game to connect the trainer.");
    }, _selected?.Id == "elden-ring" ? "Starting Elden Ring offline…" : _selected?.Id == "stardew" ? "Launching with SMAPI…" : "Opening your game in Steam…");

    private async void FeatureClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FeatureItem feature }) await SendFeatureAsync(feature.Definition);
    }

    private async void FeatureChoiceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FeatureChoiceItem choice }) await SendChoiceAsync(choice);
    }

    private async Task SendChoiceAsync(FeatureChoiceItem choice)
    {
        if (_busy || !choice.CanUse || !Features.Contains(choice.Owner)
            || choice.IsSelected || _selected?.Module is not IFeatureChoiceModule) return;
        await RunOperationAsync(async game =>
        {
            if (game.Installation == null || game.Module is not IFeatureChoiceModule selector) return;
            try
            {
                await selector.SelectAsync(game.Installation, game.Status, choice.Owner.Definition.Id, choice.Definition.Value);
            }
            catch
            {
                // A timed-out request may have applied. Always prefer a fresh confirmed value.
                try { game.Status = await game.Module.ReadStatusAsync(game.Installation); } catch { }
                throw;
            }
            game.Status = await game.Module.ReadStatusAsync(game.Installation);
            var valueKey = choice.Owner.Definition.ChoiceValueKey;
            var confirmed = game.Status.Connected && valueKey != null
                && game.Status.Values.TryGetValue(valueKey, out var value) && value == choice.Definition.Value;
            SetLocalizedMessage(() => confirmed
                ? Localization.Choose($"{choice.Owner.Name}: {choice.Definition.Label} selected.", $"{choice.Owner.Name}：已选择 {choice.Definition.Label}。")
                : Localization.Text("choicePending"));
        }, "Applying trainer selection…");
    }

    private async Task SendFeatureAsync(FeatureDefinition feature)
    {
        var cleanup = _selected?.Id is "dawnwalker" or "black-myth-wukong" or "phantom-blade-zero" or "red-dead-redemption-2" or "hotel-architect" or "civilization-vi" or "civilization-vii" or "witcher-3" or "elden-ring" or "astroneer" && _selected.Status.Connected
            && (feature.Id == "disable_all" || feature.IsToggle && _selected.Status.Enabled.TryGetValue(feature.Id, out var activeCleanup) && activeCleanup);
        cleanup |= GtaStorySession && (feature.Id == "disable_all" || feature.IsToggle && _selected!.Status.Enabled.TryGetValue(feature.Id, out var gtaActive) && gtaActive);
        cleanup |= _selected?.Id == "crimson-desert" && _selected.Status.Connected
            && feature.Id == "crimson_auto_teleport" && _selected.Status.Enabled.TryGetValue(feature.Id, out var crimsonActive) && crimsonActive;
        if (_busy || _selected?.Status.Ready != true && !cleanup) return;
        if (feature.Number != null)
        {
            var item = Features.FirstOrDefault(item => item.Definition.Id == feature.Id);
            if (item != null) await SendNumberAsync(item);
            return;
        }
        _witcherControlPending = _selected?.Id == "witcher-3";
        try
        {
            await RunOperationAsync(async game =>
            {
                if (game.Installation == null) return;
                bool? enable = feature.IsToggle ? !(game.Status.Enabled.TryGetValue(feature.Id, out var active) && active) : null;
                var requestSession = game.Status.SessionId;
                await game.Module.SendAsync(game.Installation, game.Status, feature.Id, enable);
                game.Status = await game.Module.ReadStatusAsync(game.Installation);
                if (game.Id == "elden-ring")
                {
                    if (!game.Status.Connected || game.Status.SessionId != requestSession)
                        SetMessage(game.Status.Message, true);
                    else SetMessage(feature.Id == "disable_all" ? "All Elden Ring refills stopped."
                        : feature.IsToggle ? "Refill setting updated. Gameplay effects remain unverified."
                        : "Refill command completed. Gameplay effects remain unverified.");
                    return;
                }
                if (game.Id == "crimson-desert" && feature.Id is "crimson_teleport" or "crimson_auto_teleport")
                {
                    // Acknowledgement accepts the request; movement runs on a later game tick.
                    // Report its latest native state rather than claiming the character moved.
                    if (!game.Status.Connected || game.Status.SessionId != requestSession)
                        SetMessage(game.Status.Message, true);
                    else ShowCrimsonTeleportStatus(game.Status);
                    return;
                }
                if (feature.Id == "crimson_connect")
                {
                    SetMessage(game.Status.Message, game.Status.Connected && !game.Status.Ready);
                    return;
                }
                SetLocalizedMessage(() => feature.IsToggle
                    ? Localization.Choose($"{feature.Name}: request sent to the game.", $"{Localization.FeatureName(feature)}：请求已发送，等待游戏确认状态。")
                    : Localization.Choose($"{feature.Name}: applied in game.", $"{Localization.FeatureName(feature)}：已在游戏中应用。"));
            }, _witcherControlPending ? "Control queued. Resume The Witcher 3 to apply it. Waiting for confirmation…" : "Applying trainer control…");
        }
        finally { _witcherControlPending = false; RefreshBindings(); }
    }

    private async Task SendNumberAsync(FeatureItem feature)
    {
        if (_busy || !feature.CanUse || _selected?.Module is not IFeatureValueModule) return;
        if (!feature.TryGetNumber(out var value))
        {
            SetMessage(feature.NumberError, true);
            return;
        }
        await RunOperationAsync(async game =>
        {
            if (game.Installation == null || game.Module is not IFeatureValueModule editor) return;
            try
            {
                await editor.SetValueAsync(game.Installation, game.Status, feature.Definition.Id, value);
            }
            catch
            {
                // A request can apply before its acknowledgement is lost. Refresh without retrying it.
                try { game.Status = await game.Module.ReadStatusAsync(game.Installation); } catch { }
                throw;
            }
            game.Status = await game.Module.ReadStatusAsync(game.Installation);
            SetLocalizedMessage(() => Localization.Choose(
                $"{feature.Name}: {value.ToString(CultureInfo.CurrentCulture)} applied in game.",
                $"{feature.Name}：已在游戏中应用数值 {value.ToString(CultureInfo.CurrentCulture)}。"));
        }, "Applying trainer value…");
    }

    private async void DisableAllClicked(object sender, RoutedEventArgs e) => await SendFeatureAsync(new FeatureDefinition("disable_all", "Disable all", "", ""));

    private Civ6HotkeyScope CivilizationShortcutScope()
    {
        if (_selected?.Status.Connected != true || _updateHandoff || _updateLifetime.IsCancellationRequested) return default;
        var processId = _selected.Module switch
        {
            CivilizationVIModule module when _selected.Id == "civilization-vi" => module.ConnectedProcessId,
            CivilizationVIIModule module when _selected.Id == "civilization-vii" => module.ConnectedProcessId,
            EldenRingModule module when _selected.Id == "elden-ring" => module.ConnectedProcessId,
            _ => 0
        };
        var editingMenu = _gameOverlay?.IsActive == true && Keyboard.FocusedElement is TextBox or ComboBox;
        return new(processId, _selected.Status.SessionId, _busy || _updateWindow != null || editingMenu, _selected.Id == "elden-ring");
    }

    private async Task DispatchCivilizationShortcutAsync(Civ6HotkeyRequest request)
    {
        // A queued press is discarded if the player changed focus, page or game session.
        if (!request.Matches(CivilizationShortcutScope(), Civ6KeyboardHook.ForegroundProcessId())) return;
        await ApplyCivilizationShortcutAsync(request.Shortcut);
    }

    internal async Task ApplyCivilizationShortcutAsync(string shortcut)
    {
        if (_busy || _updateHandoff || _updateWindow != null || _updateLifetime.IsCancellationRequested
            || _selected?.Id is not ("civilization-vi" or "civilization-vii" or "elden-ring") || !_selected.Status.Connected) return;
        if (shortcut == "F12")
        {
            if (CanDisableAll) await SendFeatureAsync(new FeatureDefinition("disable_all", "Disable all", "", "F12"));
            return;
        }
        var item = Features.FirstOrDefault(feature => feature.Definition.Hotkey == shortcut);
        if (item?.CanUse == true) await SendFeatureAsync(item.Definition);
    }

    private async void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat) return;
        if (sender is GameOverlay && Keyboard.FocusedElement is TextBox or ComboBox) return;
        if (_selected?.Id is "civilization-vi" or "civilization-vii" or "elden-ring")
        {
            var modifiers = Keyboard.Modifiers;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var civShortcut = Civ6HotkeyRouter.Shortcut(KeyInterop.VirtualKeyFromKey(key), modifiers.HasFlag(ModifierKeys.Shift),
                modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Alt), modifiers.HasFlag(ModifierKeys.Windows));
            if (civShortcut != null && (_selected.Id != "elden-ring" || Civ6HotkeyScope.EldenRingShortcut(civShortcut)))
            {
                e.Handled = true;
                await ApplyCivilizationShortcutAsync(civShortcut);
            }
            return;
        }
        if (Keyboard.FocusedElement is TextBox) return;
        var shortcut = (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "")
            + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "")
            + (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + (e.Key == Key.System ? e.SystemKey : e.Key);
        if (_selected?.Id is "gta-v-enhanced" or "astroneer" && shortcut == "F10" && CanDisableAll)
        {
            e.Handled = true;
            await SendFeatureAsync(new FeatureDefinition("disable_all", "Disable all", "", "F10"));
            return;
        }
        var feature = Features.FirstOrDefault(f => string.Equals(f.Definition.Hotkey, shortcut, StringComparison.OrdinalIgnoreCase));
        if (feature == null || !feature.CanUse) return;
        e.Handled = true;
        await SendFeatureAsync(feature.Definition);
    }

    private void ShowCrimsonTeleportStatus(TrainerStatus status)
    {
        var message = status.Values.TryGetValue("crimson_teleport_status", out var nativeMessage) && !string.IsNullOrWhiteSpace(nativeMessage)
            ? nativeMessage : "Marker teleport request accepted; waiting for a game status update.";
        var failed = message.StartsWith("Marker teleport rejected:", StringComparison.Ordinal)
            || message.StartsWith("Marker teleport not retained ", StringComparison.Ordinal)
            || message.StartsWith("Marker teleport canceled:", StringComparison.Ordinal)
            || message.StartsWith("Marker teleport stopped:", StringComparison.Ordinal)
            || message.StartsWith("Marker teleport expired ", StringComparison.Ordinal)
            || message.StartsWith("Automatic marker teleport disarmed:", StringComparison.Ordinal)
            || message == "Automatic marker teleport is unavailable for the current player.";
        SetMessage(CrimsonTeleportMessage(message), failed);
    }

    private static string CrimsonTeleportMessage(string message) =>
        // The legacy native message only confirms a proxy write/readback, not character travel.
        message == "Marker teleport applied; terrain height and landing are unverified."
            ? "Marker position update sent; character movement and landing have not been verified."
            : message;

    private void SetMessage(string message, bool error = false)
    {
        _localizedMessage = null;
        _message = message;
        _messageIsError = error;
        Notify(nameof(Message)); Notify(nameof(MessageBrush));
    }

    private void SetLocalizedMessage(Func<string> message)
    {
        _localizedMessage = message;
        _messageIsError = false;
        Notify(nameof(Message)); Notify(nameof(MessageBrush));
    }

    private void RefreshBindings()
    {
        foreach (var feature in Features) feature.Update(_selected?.Status, !_busy, _witcherControlPending);
        RefreshOverlay();
        Notify("");
    }

    internal static string FormatValues(IReadOnlyDictionary<string, string> values)
    {
        return string.Join("    ·    ", values
            .Where(pair => (pair.Key != "energy_max" || !values.ContainsKey("energy"))
                && (pair.Key != "blood_capacity" || !values.ContainsKey("blood"))
                && !pair.Key.StartsWith("available_", StringComparison.OrdinalIgnoreCase)
                && !pair.Key.StartsWith("reason_", StringComparison.OrdinalIgnoreCase)
                && !pair.Key.StartsWith("diagnostic_", StringComparison.OrdinalIgnoreCase)
                && !new[] { "dryMultiplier", "washAvailable", "dryAvailable", "shopAvailable", "collection_hook_registered", "collection_last_resource", "collection_scope", "soil_status", "selected_collection_multiplier", "selected_run_speed_multiplier", "witcher3_waiting_for_game" }.Contains(pair.Key))
            .Select(pair =>
            {
                var label = Localization.ValueLabel(pair.Key);
                var value = Localization.Message(pair.Key == "crimson_teleport_status" ? CrimsonTeleportMessage(pair.Value) : pair.Value);
                if (pair.Key is "health_percent" or "stamina_percent" or "blood"
                    && double.TryParse(pair.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var resource))
                    value = resource.ToString("0.#", CultureInfo.CurrentCulture) + (pair.Key.EndsWith("_percent", StringComparison.Ordinal) ? "%" : "");
                if (pair.Key == "blood" && values.TryGetValue("blood_capacity", out var capacity)) value += " / " + capacity;
                if (pair.Key == "energy" && values.TryGetValue("energy_max", out var maximum)) value += " / " + maximum;
                return label + Localization.Choose(": ", "：") + value;
            }));
    }

    internal static Brush Paint(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GameItem : INotifyPropertyChanged
{
    public IGameModule Module { get; }
    public GameInstallation? Installation { get; set; }
    public TrainerStatus Status { get; set; } = TrainerStatus.Offline();
    public string Id => Module.Definition.Id;
    public string Name => Localization.GameName(Module.Definition);
    public string SortName => Module.Definition.Name;
    public string SearchText => Module.Definition.Name + " " + Localization.GameName(Module.Definition, "zh") + " " + Module.Definition.Id;
    public string Subtitle => Localization.Subtitle(Module.Definition);
    public string Initials => Module.Definition.Initials;
    public string IconUri => GameArtwork.IconUri(Id).AbsoluteUri;
    public string LogoUri => GameArtwork.LogoUri(Id).AbsoluteUri;
    public string HeroLine => Localization.GameDescription(Module.Definition);
    public string LibraryDetail => Installation?.IsInstalled == true
        ? Localization.Choose($"{Module.Definition.Features.Count(f => f.Id != "disable_all")} controls · Installed", $"{Module.Definition.Features.Count(f => f.Id != "disable_all")} 项功能 · 游戏已安装")
        : Localization.Choose($"{Module.Definition.Features.Count(f => f.Id != "disable_all")} trainer controls", $"{Module.Definition.Features.Count(f => f.Id != "disable_all")} 项修改器功能");
    public Brush TileBrush => ThemeManager.Brush("RaisedBrush");
    public GameItem(IGameModule module) => Module = module;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
}

public sealed class FeatureItem : INotifyPropertyChanged
{
    public FeatureDefinition Definition { get; }
    public IReadOnlyList<FeatureChoiceItem> Choices { get; }
    private bool _active;
    private bool _unavailable, _connected, _working, _initializationFailed;
    private bool _witcherControlPending;
    private string _numberText;
    private string? _confirmedNumber;
    private string? _numberErrorKey;
    public bool CanUse { get; private set; }
    public string Name => Localization.FeatureName(Definition);
    public string Description => Localization.FeatureDescription(Definition);
    public string Group => Localization.Group(Definition.Group);
    public string Hotkey => Definition.Hotkey;
    public Visibility HotkeyVisibility => string.IsNullOrEmpty(Hotkey) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ChoicesVisibility => Choices.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    public string ChoiceCaption => Localization.Text("multiplier");
    public Visibility NumberVisibility => Definition.Number == null ? Visibility.Collapsed : Visibility.Visible;
    public string NumberCaption => Localization.Text(Definition.Id is "stardew_add_money" or "wukong_xp" or "wukong_sparks" or "wukong_will" ? "amount" : "value");
    public string NumberUnit => Definition.Number?.Unit is { } unit ? Localization.NumberUnit(unit) : "";
    public string NumberText
    {
        get => _numberText;
        set
        {
            if (_numberText == value) return;
            _numberText = value;
            _numberErrorKey = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NumberText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NumberError)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NumberErrorVisibility)));
        }
    }
    public string NumberHint => Definition.Number is not { } number ? "" : Definition.Id == "stardew_set_time"
        ? Localization.Text("timeValueHint")
        : Localization.Choose($"{number.Minimum}–{number.Maximum} · step {number.Step}", $"范围 {number.Minimum}–{number.Maximum} · 步长 {number.Step}");
    public string ConfirmedNumberLabel => _confirmedNumber == null ? "" : Localization.Text("currentValue") + Localization.Choose(": ", "：") + _confirmedNumber;
    public Visibility ConfirmedNumberVisibility => _confirmedNumber == null ? Visibility.Collapsed : Visibility.Visible;
    public string NumberError => _numberErrorKey switch
    {
        "range" when Definition.Number is { } number => Localization.Choose($"Enter a value from {number.Minimum} to {number.Maximum}.", $"请输入 {number.Minimum} 至 {number.Maximum} 范围内的数值。"),
        "step" when Definition.Number is { } number => Localization.Choose($"Use increments of {number.Step} from {number.Minimum}.", $"请以 {number.Minimum} 为起点，按 {number.Step} 的步长输入数值。"),
        null => "",
        _ => Localization.Text(_numberErrorKey)
    };
    public Visibility NumberErrorVisibility => _numberErrorKey == null ? Visibility.Collapsed : Visibility.Visible;
    public string StateLabel => _witcherControlPending && _working
        ? Localization.Choose("Queued", "已排队")
        : Localization.Text(_working ? "pleaseWait" : _unavailable ? "sceneUnavailable" : !CanUse ? (_connected ? (_initializationFailed ? "initializationFailed" : "waitingGameplay") : "waitingGame") : Definition.IsToggle ? (_active ? "enabled" : "off") : "ready");
    public string ActionLabel => Localization.Text(Definition.IsToggle ? (_active ? "turnOff" : "turnOn") : "apply");
    public Brush StateBrush => ThemeManager.Brush(_active && CanUse ? "SuccessBrush" : "MutedBrush");
    public Brush ActionBrush => ThemeManager.Brush(_active && CanUse ? "SuccessSurfaceBrush" : "ButtonBrush");
    public Brush ActionBorderBrush => ThemeManager.Brush(_active && CanUse ? "SuccessBorderBrush" : "ButtonBorderBrush");
    public FeatureItem(FeatureDefinition definition)
    {
        Definition = definition;
        _numberText = definition.Number?.DefaultValue.ToString(CultureInfo.CurrentCulture) ?? "";
        Choices = definition.Choices?.Select(choice => new FeatureChoiceItem(this, choice)).ToArray() ?? Array.Empty<FeatureChoiceItem>();
    }
    public bool TryGetNumber(out decimal value)
    {
        value = 0;
        if (Definition.Number is not { } number) return false;
        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        var text = NumberText.Trim();
        if (!decimal.TryParse(text, style, CultureInfo.CurrentCulture, out value)
            && !decimal.TryParse(text, style, CultureInfo.InvariantCulture, out value)) _numberErrorKey = "invalidNumber";
        else if (value < number.Minimum || value > number.Maximum) _numberErrorKey = "range";
        else if (number.Step > 0 && (value - number.Minimum) % number.Step != 0) _numberErrorKey = "step";
        else if (Definition.Id == "stardew_set_time" && (value % 10 != 0 || value % 100 >= 60)) _numberErrorKey = "invalidTime";
        else _numberErrorKey = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NumberError)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NumberErrorVisibility)));
        return _numberErrorKey == null;
    }
    public void Update(TrainerStatus? status, bool available, bool witcherControlPending = false)
    {
        _active = status?.Enabled.TryGetValue(Definition.Id, out var active) == true && active;
        _connected = status?.Connected == true;
        _working = !available;
        _witcherControlPending = witcherControlPending && Definition.Id.StartsWith("witcher3_", StringComparison.Ordinal);
        _initializationFailed = Definition.Id.StartsWith("crimson_", StringComparison.Ordinal) && status?.Ready == false
            && status.Values.TryGetValue("diagnostic_crimson_initialization_failed", out var failed) && failed == bool.TrueString;
        var availabilityKey = Definition.Id switch { "wash" => "washAvailable", "dry" or "dry_slower" or "dry_faster" => "dryAvailable", "shop" => "shopAvailable", _ => "available_" + Definition.Id };
        _unavailable = status?.Ready == true && status.Values.TryGetValue(availabilityKey, out var value) && string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        var cleanup = (Definition.Id.StartsWith("dawnwalker_", StringComparison.Ordinal) || Definition.Id.StartsWith("wukong_", StringComparison.Ordinal) || Definition.Id.StartsWith("pbz_", StringComparison.Ordinal) || Definition.Id.StartsWith("rdr2_", StringComparison.Ordinal) || Definition.Id.StartsWith("ha_", StringComparison.Ordinal) || Definition.Id.StartsWith("civ6_", StringComparison.Ordinal) || Definition.Id.StartsWith("civ7_", StringComparison.Ordinal) || Definition.Id.StartsWith("witcher3_", StringComparison.Ordinal) || Definition.Id.StartsWith("er_", StringComparison.Ordinal) || Definition.Id == "crimson_auto_teleport") && Definition.IsToggle && _active;
        var gtaStory = !Definition.Id.StartsWith("gta_", StringComparison.Ordinal)
            || status?.Values.TryGetValue("gta_online", out var gtaOnline) == true && gtaOnline == bool.FalseString;
        cleanup |= Definition.Id.StartsWith("gta_", StringComparison.Ordinal) && gtaStory && Definition.IsToggle && _active;
        if (cleanup) _unavailable = false;
        CanUse = available && _connected && gtaStory && (cleanup || status?.Ready == true && !_unavailable);
        var confirmedChoice = status?.Connected == true && Definition.ChoiceValueKey is { } key
            && status.Values.TryGetValue(key, out var selected) ? selected : null;
        _confirmedNumber = Definition.Number != null ? confirmedChoice : null;
        foreach (var choice in Choices) choice.Update(confirmedChoice);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class FeatureChoiceItem : INotifyPropertyChanged
{
    public FeatureItem Owner { get; }
    public FeatureChoice Definition { get; }
    public bool IsSelected { get; private set; }
    public bool CanUse => Owner.CanUse;
    public string Label => (IsSelected ? "✓  " : "") + Definition.Label;
    public Brush Background => ThemeManager.Brush(IsSelected ? "SelectionBrush" : "ButtonBrush");
    public Brush BorderBrush => ThemeManager.Brush(IsSelected ? "SelectionBorderBrush" : "ButtonBorderBrush");
    public Brush Foreground => ThemeManager.Brush(IsSelected ? "SelectionInkBrush" : "InkBrush");
    public FeatureChoiceItem(FeatureItem owner, FeatureChoice definition) { Owner = owner; Definition = definition; }
    public void Update(string? confirmedValue)
    {
        IsSelected = confirmedValue == Definition.Value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
