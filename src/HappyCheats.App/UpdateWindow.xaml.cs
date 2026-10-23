using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using HappyCheats.Core.Updates;

namespace HappyCheats.App;

public partial class UpdateWindow : Window, INotifyPropertyChanged
{
    private enum UpdateState { Ready, Checking, Available, Latest, Downloading, Staged, Installing, Cancelled, Failed, Unconfigured, Disabled }
    private readonly UpdateClient? _client;
    private readonly UpdateConfiguration? _configuration;
    private readonly Func<Task> _prepareInstall;
    private readonly Action<ReleaseManifest?> _releaseChanged;
    private readonly bool _isolated;
    private readonly bool _preview;
    private CancellationTokenSource? _operation;
    private ReleaseManifest? _release;
    private StagedUpdate? _staged;
    private UpdateState _state;
    private string? _error;
    private bool _closeRequested, _handoff, _userCancelled, _releaseVerified;
    private long _bytesReceived, _totalBytes;

    public UpdateWindow(HttpClient http, UpdateConfiguration? configuration, ReleaseManifest? release,
        string? configurationError, bool isolated, Func<Task> prepareInstall, Action<ReleaseManifest?> releaseChanged,
        bool preview = false)
    {
        _client = configuration == null ? null : new UpdateClient(http, configuration);
        _configuration = configuration;
        _release = release;
        _isolated = isolated;
        _preview = preview;
        _prepareInstall = prepareInstall;
        _releaseChanged = releaseChanged;
        _error = configurationError;
        // Cached release notes may be shown, but every dialog verifies the feed with its own client
        // before enabling download. This also gives the user fresh notes if a release has changed.
        _state = preview ? UpdateState.Available : isolated ? UpdateState.Disabled : configuration == null ? UpdateState.Unconfigured : UpdateState.Ready;
        InitializeComponent();
        ThemeManager.Attach(this);
        if (!preview) WindowLayout.Attach(this);
        DataContext = this;
        Loaded += async (_, _) => { if (_state == UpdateState.Ready) await CheckAsync(); };
        Closing += WindowClosing;
        Closed += (_, _) => { CancelPending(); DiscardStagedUpdate(); };
    }

    public static UpdateWindow CreatePreview(HttpClient http) => new(http, null, null, null, true, () => Task.CompletedTask, _ => { }, preview: true);
    public bool IsWorking => _state is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;
    public string WindowTitle => Localization.Choose("HappyCheats updates", "HappyCheats 客户端更新");
    public string CurrentVersionLabel => Localization.Choose("Installed version  ", "当前版本  ") + UpdateLauncher.CurrentVersion;
    public string ChannelLabel => (_preview ? "stable" : _configuration?.Channel ?? "stable").ToUpperInvariant();
    public string CheckLabel => Localization.Choose(_state == UpdateState.Failed ? "Retry check" : "Check again", _state == UpdateState.Failed ? "重新检查" : "检查更新");
    public string CloseLabel => Localization.Choose(IsWorking ? "Cancel" : "Close", IsWorking ? "取消" : "关闭");
    public bool CanClose => _state != UpdateState.Installing;
    public bool CanCheck => !IsWorking && !_isolated && _configuration != null;
    public bool CanAct => !IsWorking && !_isolated && _releaseVerified && _release != null && UpdateLauncher.CanInstall;
    public Visibility PrimaryVisibility => _release != null || _preview ? Visibility.Visible : Visibility.Collapsed;
    public string PrimaryLabel => _staged != null ? Localization.Choose("Install & restart", "安装并重启") : Localization.Choose("Download update", "下载更新");
    public Visibility ReleaseVisibility => _release != null || _preview ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NewGamesVisibility => NewGames.Count != 0 ? Visibility.Visible : Visibility.Collapsed;
    public IReadOnlyList<string> NewGames => _preview
        ? new[] { Localization.Choose("Sample game · New trainer controls", "示例游戏 · 全新修改器功能") }
        : _release?.NewGames ?? Array.Empty<string>();
    public string NewGamesLabel => Localization.Choose("NEW GAMES & TRAINERS", "新增游戏与修改器");
    public string ChangelogLabel => Localization.Choose("What's new", "更新内容");
    public string Changelog => _preview ? Localization.Choose(
        "Sample release notes\n\n• New trainer controls and game support.\n• Improvements to client reliability and game discovery.\n• Download releases and read changelogs directly in HappyCheats.",
        "示例更新说明\n\n• 新增修改器功能与游戏支持。\n• 改善客户端稳定性及游戏识别。\n• 在 HappyCheats 中直接下载新版并阅读更新日志。")
        : string.IsNullOrWhiteSpace(Localization.Choose(_release?.ChangelogEnglish ?? "", _release?.ChangelogChinese ?? ""))
            ? (_release?.ChangelogEnglish is { Length: > 0 } english ? english : _release?.ChangelogChinese is { Length: > 0 } chinese ? chinese : Localization.Choose("No release notes were provided.", "此版本未提供更新说明。"))
            : Localization.Choose(_release!.ChangelogEnglish, _release.ChangelogChinese);
    public string ReleaseMetadata => _preview ? Localization.Choose("PREVIEW RELEASE  ·  2026-09-13  ·  128.0 MB", "预览版本  ·  2026-09-13  ·  128.0 MB")
        : _release == null ? "" : "v" + _release.Version + "  ·  " + _release.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "  ·  " + FormatBytes(_release.PackageSize);
    public string StateTitle => _preview ? Localization.Choose("A new release is ready", "新版本已准备就绪") : _state switch
    {
        UpdateState.Checking => Localization.Choose("Checking for updates…", "正在检查更新…"),
        UpdateState.Available => Localization.Choose($"Version {_release?.Version} is available", $"发现新版本 {_release?.Version}"),
        UpdateState.Latest => Localization.Choose("You're up to date", "当前已是最新版本"),
        UpdateState.Downloading => Localization.Choose("Downloading your update…", "正在下载更新…"),
        UpdateState.Staged => Localization.Choose("Ready to install", "可以安装了"),
        UpdateState.Installing => Localization.Choose("Preparing to restart…", "正在准备重启…"),
        UpdateState.Cancelled => Localization.Choose("Update cancelled", "更新已取消"),
        UpdateState.Failed => Localization.Choose("Update couldn't finish", "暂时无法完成更新"),
        UpdateState.Unconfigured => Localization.Choose("Updates aren't connected yet", "尚未连接更新服务"),
        UpdateState.Disabled => Localization.Choose("Preview / development session", "预览 / 开发模式"),
        _ => Localization.Choose("Get the latest HappyCheats", "获取最新版 HappyCheats")
    };
    public string StateDescription => _preview ? Localization.Choose("Preview only · Sample release content", "仅供预览 · 以下为示例更新内容") : _state switch
    {
        UpdateState.Checking => _configuration?.FallbackFeedUrl != null
            ? Localization.Choose("Checking your preferred update server, then the backup if it is unavailable.", "正在优先检查更新服务器，不可用时将自动使用备用服务器。")
            : Localization.Choose("Getting the latest release information from your update server.", "正在从更新服务器获取最新版本信息。"),
        UpdateState.Available => Localization.Choose("Read the release notes, then download when you're ready.", "阅读更新说明后，可随时开始下载。"),
        UpdateState.Latest => Localization.Choose("Your client has the latest release for this channel.", "当前客户端已是此更新通道的最新版本。"),
        UpdateState.Downloading => Localization.Choose("The download will be verified before installation. You can cancel at any time.", "下载完成后将验证安装包。您可以随时取消下载。"),
        UpdateState.Staged => Localization.Choose("Download verified. Close your games, then choose Install & restart.", "下载已验证。请关闭游戏，然后选择“安装并重启”。"),
        UpdateState.Installing => Localization.Choose("Checking that your games are closed before restarting HappyCheats.", "正在确认游戏已关闭，随后将重启 HappyCheats。"),
        UpdateState.Cancelled => Localization.Choose("You can check again or restart the download when you're ready.", "您可以重新检查更新，或在准备好后再次下载。"),
        UpdateState.Failed => Localization.Choose("See the details below, then retry. Your installed client is still available.", "请查看下方详情后重试。当前客户端仍可继续使用。"),
        UpdateState.Unconfigured => Localization.Choose("This client needs a release with update-server settings. Contact the HappyCheats publisher for a configured client.", "此客户端尚未配置更新服务器。请联系 HappyCheats 发布者获取已配置的客户端。"),
        UpdateState.Disabled => Localization.Choose("Update checks and installation are disabled in preview sessions and when using a custom data root.", "预览模式及自定义数据目录模式下，不会检查或安装客户端更新。"),
        _ => Localization.Choose("Check for new trainers, improvements and release notes.", "检查新修改器、客户端改进与更新日志。")
    };
    public string InstallNote => _isolated
        ? Localization.Choose("This preview cannot download or install releases.", "此预览无法下载或安装版本。")
        : !UpdateLauncher.CanInstall
            ? Localization.Choose("Automatic installation requires the published Windows client with its updater. Use the complete release package to update this copy.", "自动安装需要带有更新程序的 Windows 发布版本。请使用完整发布包更新此副本。")
            : Localization.Choose("This updates HappyCheats and its bundled trainers. After restarting, choose Install / update for each game to apply its new trainer. Close supported games before installing the client update.",
                "本操作将更新 HappyCheats 及随附的修改器。重启后，请为各游戏选择“安装 / 更新”以应用新版修改器。安装客户端更新前，请关闭支持的游戏。");
    public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(_error) ? Visibility.Collapsed : Visibility.Visible;
    public string ErrorLabel => Localization.Choose("Details", "详细信息");
    public string ErrorDetails => _error ?? "";
    public Visibility ProgressVisibility => IsWorking ? Visibility.Visible : Visibility.Collapsed;
    public bool IndeterminateProgress => _state != UpdateState.Downloading || _totalBytes <= 0 || _bytesReceived >= _totalBytes;
    public double ProgressPercent => _totalBytes > 0 ? Math.Clamp(_bytesReceived * 100d / _totalBytes, 0, 100) : 0;
    public string ProgressLabel => _state == UpdateState.Checking ? Localization.Choose("Checking release", "正在检查版本")
        : _state == UpdateState.Installing ? Localization.Choose("Preparing installation", "正在准备安装")
        : _totalBytes > 0 && _bytesReceived >= _totalBytes ? Localization.Choose("Verifying package", "正在验证安装包") : Localization.Choose("Downloading", "正在下载");
    public string ProgressAmount => _state != UpdateState.Downloading ? "" : FormatBytes(_bytesReceived) + (_totalBytes > 0 ? " / " + FormatBytes(_totalBytes) : "");

    private async void CheckClicked(object sender, RoutedEventArgs e) => await CheckAsync();
    private async Task CheckAsync()
    {
        if (!CanCheck) return;
        _releaseVerified = false;
        BeginOperation(UpdateState.Checking);
        _operation!.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var release = await _client!.CheckAsync(UpdateLauncher.CurrentVersion, _operation.Token);
            DiscardStagedUpdate();
            _release = release;
            _releaseVerified = release != null;
            _releaseChanged(release);
            _state = release == null ? UpdateState.Latest : UpdateState.Available;
        }
        catch (OperationCanceledException) { CancelledOrTimedOut(); }
        catch (Exception error) { Fail(error); }
        finally { EndOperation(); }
    }

    private async void PrimaryClicked(object sender, RoutedEventArgs e)
    {
        if (!CanAct) return;
        if (_staged != null) { await InstallAsync(); return; }
        BeginOperation(UpdateState.Downloading);
        _operation!.CancelAfter(TimeSpan.FromMinutes(30));
        _totalBytes = _release!.PackageSize;
        _bytesReceived = 0;
        Refresh();
        try
        {
            var progress = new Progress<UpdateProgress>(value =>
            {
                if (_state != UpdateState.Downloading) return;
                _bytesReceived = value.BytesReceived;
                _totalBytes = value.TotalBytes;
                Refresh();
            });
            _staged = await _client!.DownloadAndStageAsync(_release, UpdateLauncher.StagingRoot, progress, _operation!.Token);
            _state = UpdateState.Staged;
        }
        catch (OperationCanceledException) { CancelledOrTimedOut(); }
        catch (Exception error) { Fail(error); }
        finally { EndOperation(); }
    }

    private async Task InstallAsync()
    {
        BeginOperation(UpdateState.Installing);
        var launchAttempted = false;
        try
        {
            await _prepareInstall();
            if (Owner is not MainWindow main) throw new InvalidOperationException(Localization.Choose("The main window is unavailable. Reopen HappyCheats and retry.", "主窗口不可用。请重新打开 HappyCheats 后重试。"));
            _handoff = true;
            launchAttempted = true;
            main.BeginUpdateHandoff(_staged!);
        }
        catch (Exception error)
        {
            _handoff = false;
            if (launchAttempted) DiscardStagedUpdate();
            Fail(error);
        }
        finally { EndOperation(); }
    }

    private void BeginOperation(UpdateState state)
    {
        _operation?.Dispose();
        _operation = new CancellationTokenSource();
        _userCancelled = false;
        _error = null;
        _state = state;
        Refresh();
    }

    private void EndOperation()
    {
        _operation?.Dispose();
        _operation = null;
        Refresh();
        if (_closeRequested && !_handoff) Close();
    }

    private void Fail(Exception error)
    {
        _state = UpdateState.Failed;
        _error = error.Message switch
        {
            "The fallback update source does not have this exact release. Check for updates again." => Localization.Choose(error.Message, "备用服务器上的版本已变化，请重新检查更新。"),
            "The update source timed out." => Localization.Choose(error.Message, "更新服务器响应超时，请检查网络后重试。"),
            var message when message.StartsWith("The update source could not be reached. ", StringComparison.Ordinal) => Localization.Choose(message, "无法连接更新服务器，请检查网络后重试。"),
            _ => error.Message
        };
    }

    private void CloseClicked(object sender, RoutedEventArgs e)
    {
        if (IsWorking) CancelPending(); else Close();
    }

    public void CancelPending()
    {
        _userCancelled = true;
        _operation?.Cancel();
    }

    private void CancelledOrTimedOut()
    {
        _state = _userCancelled ? UpdateState.Cancelled : UpdateState.Failed;
        if (!_userCancelled) _error = Localization.Choose("The update server took too long to respond. Check your connection and retry.", "更新服务器响应超时。请检查网络连接后重试。");
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!IsWorking || _handoff) return;
        e.Cancel = true;
        if (_state == UpdateState.Installing) return;
        _closeRequested = true;
        CancelPending();
    }

    private void DiscardStagedUpdate()
    {
        if (_staged == null || _handoff) return;
        try
        {
            var root = Path.GetFullPath(UpdateLauncher.StagingRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var work = Path.GetFullPath(_staged.WorkDirectory);
            if (work.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(work)) Directory.Delete(work, recursive: true);
        }
        catch { /* A locked staging folder can be cleaned up on a later update. */ }
        _staged = null;
    }

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? (bytes / (1024d * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
        : (bytes / (1024d * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
    public event PropertyChangedEventHandler? PropertyChanged;
}
