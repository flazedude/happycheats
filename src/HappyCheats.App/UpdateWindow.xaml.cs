using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using HappyCheats.Core.Updates;

namespace HappyCheats.App;

public partial class UpdateWindow : Window, INotifyPropertyChanged
{
    private readonly BackgroundUpdateSession? _session;
    private readonly UpdateConfiguration? _configuration;
    private readonly Func<Task> _install;
    private readonly bool _isolated;
    private readonly bool _preview;
    private readonly string? _configurationError;
    private string? _actionError;
    private ReleaseManifest? _release => _session?.Release;
    private StagedUpdate? _staged => _session?.Staged;
    private BackgroundUpdateState _state => _session?.State ?? BackgroundUpdateState.Ready;
    private long _bytesReceived => _session?.BytesReceived ?? 0;
    private long _totalBytes => _session?.TotalBytes ?? 0;
    private string? _error => _actionError ?? _session?.Error?.Message ?? _configurationError;

    public UpdateWindow(BackgroundUpdateSession? session, UpdateConfiguration? configuration,
        string? configurationError, bool isolated, Func<Task> install, bool preview = false)
    {
        _session = session;
        _configuration = configuration;
        _configurationError = configurationError;
        _isolated = isolated;
        _preview = preview;
        _install = install;
        InitializeComponent();
        ThemeManager.Attach(this);
        if (!preview) WindowLayout.Attach(this);
        DataContext = this;
        if (_session != null) _session.PropertyChanged += SessionChanged;
        Closing += (_, e) => {
            if (_state != BackgroundUpdateState.Installing) return;
            e.Cancel = true;
            SetActionError(Localization.Choose("The update is preparing to restart. Please wait for it to finish.", "更新正在准备重启，请等待完成。"));
        };
        Closed += (_, _) => { if (_session != null) _session.PropertyChanged -= SessionChanged; };
    }

    public static UpdateWindow CreatePreview(HttpClient http) => new(null, null, null, true, () => Task.CompletedTask, preview: true);
    public bool IsWorking => _session?.IsWorking == true;
    public string WindowTitle => Localization.Choose("HappyCheats updates", "HappyCheats 客户端更新");
    public string CurrentVersionLabel => Localization.Choose("Installed version  ", "当前版本  ") + UpdateLauncher.CurrentVersion;
    public string ChannelLabel => (_preview ? "stable" : _configuration?.Channel ?? "stable").ToUpperInvariant();
    public string CheckLabel => Localization.Choose(_state == BackgroundUpdateState.Failed ? "Retry update" : "Check again", _state == BackgroundUpdateState.Failed ? "重试更新" : "检查更新");
    public string CloseLabel => _staged != null ? Localization.Choose("Later", "稍后") : Localization.Choose("Close", "关闭");
    public bool CanClose => _state != BackgroundUpdateState.Installing;
    public bool CanCheck => !IsWorking && _staged == null && !_isolated && _session != null;
    public bool CanAct => !IsWorking && !_isolated && _staged != null && UpdateLauncher.CanInstall;
    public Visibility PrimaryVisibility => _staged != null || _preview ? Visibility.Visible : Visibility.Collapsed;
    public string PrimaryLabel => Localization.Choose("Restart & install", "重启并安装");
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
    public string StateTitle => _preview ? Localization.Choose("A new release is ready", "新版本已准备就绪") : _isolated ? Localization.Choose("Preview / development session", "预览 / 开发模式") : _session == null ? Localization.Choose("Automatic updates are unavailable", "无法自动更新") : _state switch
    {
        BackgroundUpdateState.Checking => Localization.Choose("Checking for updates…", "正在检查更新…"),
        BackgroundUpdateState.Latest => Localization.Choose("You're up to date", "当前已是最新版本"),
        BackgroundUpdateState.Downloading => Localization.Choose("Downloading your update…", "正在下载更新…"),
        BackgroundUpdateState.Staged => Localization.Choose("Update ready — restart to install", "更新已就绪，请重启安装"),
        BackgroundUpdateState.Installing => Localization.Choose("Preparing to restart…", "正在准备重启…"),
        BackgroundUpdateState.Failed => Localization.Choose("Update couldn't finish", "暂时无法完成更新"),
        _ => Localization.Choose("Get the latest HappyCheats", "获取最新版 HappyCheats")
    };
    public string StateDescription => _preview ? Localization.Choose("Preview only · Sample release content", "仅供预览 · 以下为示例更新内容") : _isolated ? Localization.Choose("Update checks and installation are disabled in preview sessions and when using a custom data root.", "预览模式及自定义数据目录模式下，不会检查或安装客户端更新。") : _session == null ? Localization.Choose("Use a complete published client with update-server settings and its updater.", "请使用带有更新服务器配置和更新程序的完整发布客户端。") : _state switch
    {
        BackgroundUpdateState.Checking => Localization.Choose("Checking configured update sources for the newest trusted release.", "正在检查已配置的更新源，查找最新的可信版本。"),
        BackgroundUpdateState.Latest => Localization.Choose("Your client has the latest release for this channel.", "当前客户端已是此更新通道的最新版本。"),
        BackgroundUpdateState.Downloading => Localization.Choose("Keep using your cheats. You can close this window; the download will continue in the background.", "您可继续使用修改器。关闭此窗口后，更新仍会在后台下载。"),
        BackgroundUpdateState.Staged => Localization.Choose("Your update has downloaded and been verified. Close supported games, then restart to install, or choose Later to keep using HappyCheats.", "更新已下载并通过验证。请关闭支持的游戏后重启安装，或选择“稍后”继续使用 HappyCheats。"),
        BackgroundUpdateState.Installing => Localization.Choose("Checking that your games are closed before restarting HappyCheats.", "正在确认游戏已关闭，随后将重启 HappyCheats。"),
        BackgroundUpdateState.Failed => Localization.Choose("See the details below, then retry. Your installed client is still available.", "请查看下方详情后重试。当前客户端仍可继续使用。"),
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
    public string ErrorDetails => _error switch
    {
        "The fallback update source does not have this exact release. Check for updates again." => Localization.Choose(_error, "备用服务器上的版本已变化，请重新检查更新。"),
        "The update source timed out." => Localization.Choose(_error, "更新服务器响应超时，请检查网络后重试。"),
        "The update took too long to finish. Check your connection and retry." => Localization.Choose(_error, "更新耗时过长，请检查网络后重试。"),
        { } message when message.StartsWith("The update source could not be reached. ", StringComparison.Ordinal) => Localization.Choose(message, "无法连接更新服务器，请检查网络后重试。"),
        _ => _error ?? ""
    };
    public Visibility ProgressVisibility => IsWorking ? Visibility.Visible : Visibility.Collapsed;
    public bool IndeterminateProgress => _state != BackgroundUpdateState.Downloading || _totalBytes <= 0 || _bytesReceived >= _totalBytes;
    public double ProgressPercent => _totalBytes > 0 ? Math.Clamp(_bytesReceived * 100d / _totalBytes, 0, 100) : 0;
    public string ProgressLabel => _state == BackgroundUpdateState.Checking ? Localization.Choose("Checking release", "正在检查版本")
        : _state == BackgroundUpdateState.Installing ? Localization.Choose("Preparing installation", "正在准备安装")
        : _totalBytes > 0 && _bytesReceived >= _totalBytes ? Localization.Choose("Verifying package", "正在验证安装包") : Localization.Choose("Downloading", "正在下载");
    public string ProgressAmount => _state != BackgroundUpdateState.Downloading ? "" : FormatBytes(_bytesReceived) + (_totalBytes > 0 ? " / " + FormatBytes(_totalBytes) : "");

    private async void CheckClicked(object sender, RoutedEventArgs e)
    {
        if (!CanCheck)
        {
            SetActionError(Localization.Choose(
                IsWorking ? "An update operation is already running." : _staged != null ? "An update is already downloaded. Choose Restart & install or Later." : StateDescription,
                IsWorking ? "更新操作正在进行。" : _staged != null ? "更新已下载，请选择“重启并安装”或“稍后”。" : StateDescription));
            return;
        }
        _actionError = null;
        await _session!.CheckAndDownloadAsync();
    }

    private async void PrimaryClicked(object sender, RoutedEventArgs e)
    {
        if (!CanAct)
        {
            SetActionError(IsWorking ? Localization.Choose("An update operation is already running.", "更新操作正在进行。") : InstallNote);
            return;
        }
        _actionError = null;
        try
        {
            await _install();
        }
        catch (Exception error) { SetActionError(error.Message); }
        Refresh();
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
    private void SetActionError(string message)
    {
        _actionError = message;
        if (!_isolated) ClientErrorLog.Write("updates", message);
        Refresh();
    }
    private void SessionChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    internal void RefreshLanguage() => Refresh();
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? (bytes / (1024d * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
        : (bytes / (1024d * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
    public event PropertyChangedEventHandler? PropertyChanged;
}
