using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Diagnostics;
using HappyCheats.Core.Cheats;
using HappyCheats.Core.Updates;

namespace HappyCheats.App;

public partial class CheatUpdateWindow : Window, INotifyPropertyChanged
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly CheatUpdateClient? _client;
    private readonly CheatStore? _store;
    private readonly IReadOnlyDictionary<string, string> _bundled;
    private readonly Dictionary<string, string> _names;
    private readonly Func<IEnumerable<GameItem>> _games;
    private readonly bool _isolated;
    private readonly Func<CheatRelease, Func<Task>, Action<string>, Task<string>> _apply;
    private readonly HashSet<string> _unfinished = new(StringComparer.Ordinal);
    private readonly CancellationToken _lifetime;
    private readonly Func<Task<CheatUpdateCheck>>? _checkForUpdates;
    private string? _gameId;
    private string _focusReason = "";
    private CheatUpdateCheck? _check;
    private bool _busy;
    private string _status = "", _error = "";
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Queue<string> _log = new();
    private string _stage = "";
    private long _received, _total;
    private int _operation;
    private bool _checking, _determinate;
    private int _downloadBucket = -1;
    public bool IsWorking => _busy;
    public bool IsIndeterminate => _busy && !_determinate;
    public double ProgressPercent => _total > 0 ? Math.Clamp(100d * _received / _total, 0, 100) : 0;
    public string ActivityLog => string.Join(Environment.NewLine, _log);
    public string LogLabel => Localization.Choose("Update log", "更新日志");
    public string CopyLogLabel => Localization.Choose("Copy log", "复制日志");
    public string ProgressDetails => (_determinate ? $"{ProgressPercent:F0}% · " + (_total < 1048576
        ? $"{_received / 1024d:F1} / {_total / 1024d:F1} KB · " : $"{_received / 1048576d:F1} / {_total / 1048576d:F1} MB · ") : "")
        + Localization.Choose($"Elapsed {_elapsed.Elapsed:mm\\:ss}", $"已用时 {_elapsed.Elapsed:mm\\:ss}");
    public ObservableCollection<CheatUpdateRow> Rows { get; } = new();
    public string WindowTitle => Localization.Choose("Cheat updates", "修改器更新")
        + (_gameId == null ? "" : " · " + _names.GetValueOrDefault(_gameId, _gameId));
    public string FocusReason => _focusReason;
    public string Introduction => Localization.Choose("Add new trainers from the signed catalog, or update trainers for installed games. Close the affected game first. HappyCheats downloads, verifies and installs the trainer, then adds it to Library.",
        "从签名目录添加新修改器，或更新已安装游戏的修改器。请先关闭相关游戏。HappyCheats 会下载、验证并安装修改器，再将其加入游戏库。");
    public string CheckLabel => _gameId == null ? Localization.Choose("Check for updates", "检查更新")
        : Localization.Choose("Check this game", "检查此游戏更新");
    public string Status => _status;
    public string Error => _error;

    internal CheatUpdateWindow(CheatFeedConfiguration? configuration, CheatStore? store, IReadOnlyDictionary<string, string> bundled,
        Func<IEnumerable<GameItem>> games, string? startupError, bool isolated, Func<CheatRelease, Func<Task>, Task<string>> apply, CancellationToken lifetime,
        HttpClient? http = null, Func<Task<CheatUpdateCheck>>? checkForUpdates = null)
        : this(configuration, store, bundled, games, startupError, isolated, (release, download, _) => apply(release, download), lifetime, http, checkForUpdates) { }

    internal CheatUpdateWindow(CheatFeedConfiguration? configuration, CheatStore? store, IReadOnlyDictionary<string, string> bundled,
        Func<IEnumerable<GameItem>> games, string? startupError, bool isolated, Func<CheatRelease, Func<Task>, Action<string>, Task<string>> apply, CancellationToken lifetime,
        HttpClient? http = null, Func<Task<CheatUpdateCheck>>? checkForUpdates = null)
    {
        _store = store; _bundled = bundled; _games = games; _names = games().ToDictionary(x => x.Id, x => x.Name);
        _isolated = isolated; _apply = apply; _lifetime = lifetime;
        _checkForUpdates = checkForUpdates;
        if (configuration != null) _client = new(http ?? Http, configuration);
        _error = startupError ?? "";
        InitializeComponent(); DataContext = this; ThemeManager.Attach(this); WindowLayout.Attach(this);
        _progressTimer.Tick += (_, _) => Refresh();
        Closed += (_, _) => _progressTimer.Stop();
        Closing += (_, e) => { if (_busy && !_lifetime.IsCancellationRequested) { e.Cancel = true; Hide(); } };
        try { Populate(); } catch (Exception error) { Report(error); }
    }

    internal void FocusGame(string? id, string? reason = null)
    {
        _gameId = id; _focusReason = reason ?? "";
        try { Populate(); } catch (Exception error) { Report(error); }
        Refresh();
    }

    private void Populate()
    {
        var installed = _store?.InstalledVersions(_bundled) ?? new Dictionary<string, string>(_bundled);
        Rows.Clear();
        var games = _games().ToDictionary(x => x.Id);
        foreach (var release in (_check?.Catalog.Games ?? []).OrderBy(x => _names.GetValueOrDefault(x.Id, x.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            if (_gameId != null && release.Id != _gameId) continue;
            games.TryGetValue(release.Id, out var game);
            var missingFromLibrary = game == null;
            var pendingAddition = !_bundled.ContainsKey(release.Id) && installed.ContainsKey(release.Id)
                && (game == null || !GameIsInstalled(game) || game.Installation?.TrainerInstalled != true);
            if (game != null && !GameIsInstalled(game) && !pendingAddition && !_unfinished.Contains(release.Id + "/" + release.Version)) continue;
            var name = game?.Name ?? release.Name;
            _names[release.Id] = name;
            var current = installed.GetValueOrDefault(release.Id);
            if (game != null && (current == null || TrainerVersionWarning.Newer(game.LoadedCheatVersion, current))) current = game.LoadedCheatVersion;
            // A downloaded receipt alone does not mean setup in the game finished.
            var pending = (_unfinished.Contains(release.Id + "/" + release.Version) || game?.NeedsCheatReload == true || pendingAddition || missingFromLibrary && current != null)
                && (current == null || ReleaseVersion.Parse(release.Version).CompareTo(ReleaseVersion.Parse(current)) >= 0);
            var row = new CheatUpdateRow(release.Id, name, current, release, pending, missingFromLibrary);
            if (row.IsUpdate || pending) Rows.Add(row);
        }
    }

    private static bool GameIsInstalled(GameItem game) => game.Installation is { IsInstalled: true } installation
        && Directory.Exists(installation.Directory)
        && (game.Module.GameExecutables.Count == 0 || game.Module.GameExecutables.Any(executable => File.Exists(Path.Combine(installation.Directory, executable))));

    public Task CheckAsync() => OperateAsync(async () =>
    {
        _checking = true;
        RequireConfigured();
        SetStage(Localization.Choose("Checking signed cheat catalogs on both LAN hosts and GitHub…", "正在检查两台局域网主机及 GitHub 上的签名修改器目录……"));
        _check = _checkForUpdates != null ? await _checkForUpdates() : await _client!.CheckAsync(_store!.Root, _lifetime);
        Populate();
        var count = Rows.Count;
        _status = count == 0 ? Localization.Choose("No new trainers or trainer updates available. For trainer setup or repair, use Install / update on the game's Library page.",
                "暂无新修改器或修改器更新。如需安装或修复，请在该游戏的游戏库页面点击“安装 / 更新”。")
            : Localization.Choose($"{count} trainers available to add, update or finish installing. Choose a game below.", $"有 {count} 款修改器可添加、更新或继续安装。请在下方选择游戏。");
        AppendLog(_status);
    });

    private void RequireConfigured()
    {
        if (_isolated) throw new IOException(Localization.Choose("Cheat downloads are unavailable in this isolated preview. Open the published client to update.", "隔离预览不提供修改器下载，请打开发布客户端进行更新。"));
        if (_client == null || _store == null) throw new IOException(Localization.Choose("Cheat updates require the official trusted update configuration. Use the complete official client or restore its update settings.", "修改器更新需要官方受信任的更新配置。请使用完整官方客户端或恢复其更新设置。"));
    }

    internal Task InstallAsync(CheatUpdateRow row) => OperateAsync(async () =>
    {
        RequireConfigured();
        SetStage(Localization.Choose($"Updating {row.Name}…", $"正在更新《{row.Name}》的修改器……"));
        if (_check == null || row.Release == null)
        {
            _check = _checkForUpdates != null ? await _checkForUpdates() : await _client!.CheckAsync(_store!.Root, _lifetime);
            Populate();
            row = Rows.SingleOrDefault(x => x.Id == row.Id) ?? throw new IOException(Localization.Choose("No trainer update is available for this installed game. Check again or use its Library setup action.", "此已安装游戏暂无修改器更新，请重新检查或使用游戏库中的安装操作。"));
        }
        var existing = _games().SingleOrDefault(game => game.Id == row.Id);
        if (existing != null && !GameIsInstalled(existing) && !row.PendingSetup)
            throw new IOException(Localization.Choose("Game installation not found. Use Find in Steam or Browse folder in Library, then check for updates again.", "未找到游戏安装目录。请在游戏库中点击“在 Steam 中查找”或“浏览目录”，然后重新检查更新。"));
        if (row.Release == null) throw new IOException(Localization.Choose("No trainer package is published for this game yet. Use Install / update in its Library page.", "此游戏尚未发布独立修改器包，请在游戏页面点击“安装 / 更新”。"));
        if (row.InstalledVersion != null && ReleaseVersion.Parse(row.InstalledVersion).CompareTo(ReleaseVersion.Parse(row.Release.Version)) > 0)
            throw new IOException(Localization.Choose("Your bundled trainer is newer than this package. Use Install / update in its Library page.", "内置修改器比此包更新，请在游戏页面点击“安装 / 更新”。"));
        CheatStore.RequireCompatible(row.Release, UpdateLauncher.CurrentVersion);
        _unfinished.Add(row.Id + "/" + row.Release.Version);
        var operation = _operation;
        try
        {
            _status = await _apply(row.Release, async () =>
            {
                var bytes = new Progress<UpdateProgress>(p =>
                {
                    if (!_busy || operation != _operation || !_determinate) return;
                    _received = p.BytesReceived; _total = p.TotalBytes;
                    var bucket = (int)ProgressPercent / 10;
                    if (bucket != _downloadBucket) { _downloadBucket = bucket; AppendLog(ProgressDetails); }
                    Refresh();
                });
                // Cache verification/extraction must also leave the progress window responsive.
                await Task.Run(() => _client!.InstallAsync(_check!, row.Release, _store!, UpdateLauncher.CurrentVersion, bytes, _lifetime), _lifetime);
            }, text => { if (_busy && operation == _operation) SetStage(text); });
            _unfinished.Remove(row.Id + "/" + row.Release.Version);
            _error = "";
            _focusReason = "";
            AppendLog(_status);
        }
        finally { Populate(); PackageInstalled?.Invoke(); }
    });

    private async Task OperateAsync(Func<Task> action)
    {
        if (_busy)
        {
            Report(new InvalidOperationException(Localization.Choose("A cheat update operation is already running. Wait for it to finish and retry.", "修改器更新操作正在进行，请等待完成后重试。")));
            return;
        }
        _busy = true; _checking = false; _error = ""; _determinate = false; _received = _total = 0; _downloadBucket = -1;
        var operation = ++_operation;
        void Activity(CheatUpdateActivity activity) => DispatchActivity(activity, operation);
        if (_client != null) _client.ActivityChanged += Activity;
        _elapsed.Restart(); _progressTimer.Start(); Refresh();
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        { _status = Localization.Choose("Update canceled because HappyCheats is closing. Retry after reopening.", "HappyCheats 正在关闭，更新已取消。重新打开后可重试。"); AppendLog(_status); }
        catch (Exception error)
        {
            _status = Localization.Choose("Update failed. Resolve the error below, then retry the same action.", "更新失败。请解决下方错误，然后重试同一操作。");
            Report(error);
        }
        finally
        {
            if (_client != null) _client.ActivityChanged -= Activity;
            _elapsed.Stop(); _progressTimer.Stop(); _busy = false; _checking = false; _determinate = false; Refresh();
        }
    }

    private void SetStage(string text, bool downloading = false)
    {
        _determinate = downloading;
        _status = text;
        if (_stage != text) { _stage = text; AppendLog(text); }
        Refresh();
    }

    private void AppendLog(string text)
    {
        _log.Enqueue($"[{DateTime.Now:HH:mm:ss}] {text.ReplaceLineEndings(" ")}");
        while (_log.Count > 300) _log.Dequeue();
        Refresh();
    }

    internal void ReportCatalogActivity(CheatUpdateActivity activity)
    {
        if (_checking) DispatchActivity(activity, _operation);
    }

    private void DispatchActivity(CheatUpdateActivity activity, int operation)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => DispatchActivity(activity, operation)); return; }
        if (!_busy || operation != _operation) return;
        var source = activity.Source == null ? "" : new Uri(activity.Source).Authority;
        var text = activity.Stage switch
        {
            CheatUpdateStage.CheckingSource => Localization.Choose($"Checking {source}…", $"正在检查 {source}……"),
            CheatUpdateStage.SourceVerified => Localization.Choose($"Verified signed catalog from {source}.", $"已验证 {source} 的签名目录。"),
            CheatUpdateStage.SourceUnavailable => Localization.Choose($"{source} unavailable; trying the next matching source…", $"{source} 不可用，正在尝试下一个匹配来源……"),
            CheatUpdateStage.SelectingCatalog => Localization.Choose("Comparing signed catalogs and checking cached catalog…", "正在比较签名目录并检查缓存目录……"),
            CheatUpdateStage.CheckingCache => Localization.Choose("Verifying package identity and cached files…", "正在验证包身份和缓存文件……"),
            CheatUpdateStage.ReusingCache => Localization.Choose("Verified cached package; continuing setup…", "缓存包已验证，正在继续安装……"),
            CheatUpdateStage.RepairingCache => Localization.Choose("Preparing a verified package download / cache repair…", "正在准备下载已验证的包 / 修复缓存……"),
            CheatUpdateStage.Connecting => Localization.Choose($"Connecting to {source}…", $"正在连接 {source}……"),
            CheatUpdateStage.Downloading => Localization.Choose($"Downloading from {source}…", $"正在从 {source} 下载……"),
            CheatUpdateStage.VerifyingPackage => Localization.Choose("Verifying package size and SHA-256…", "正在验证包大小和 SHA-256……"),
            CheatUpdateStage.Extracting => Localization.Choose("Extracting trainer files…", "正在解压修改器文件……"),
            CheatUpdateStage.VerifyingModule => Localization.Choose("Verifying extracted module and payload…", "正在验证解压后的模块和载荷……"),
            _ => Localization.Choose("Saving verified trainer package…", "正在保存已验证的修改器包……")
        };
        if (activity.Stage == CheatUpdateStage.Connecting) { _received = _total = 0; _downloadBucket = -1; }
        SetStage(text, activity.Stage == CheatUpdateStage.Downloading);
    }

    private void Report(Exception error) { _error = error.Message; AppendLog(Localization.Choose("Error: ", "错误：") + error.Message); ClientErrorLog.Write("cheat-updates", error.Message); Refresh(); }
    private void CopyLogClicked(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ActivityLog); }
        catch (Exception error) { Report(error); }
    }
    private void LogTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { SelectionLength: 0 } log) log.ScrollToEnd();
    }
    private async void CheckClicked(object sender, RoutedEventArgs e) => await CheckAsync();
    private async void GameUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CheatUpdateRow row })
        {
            await InstallAsync(row);
        }
    }
    public void RefreshLanguage() { try { Populate(); } catch (Exception error) { Report(error); } Refresh(); }
    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action? PackageInstalled;
}

public sealed record CheatUpdateRow(string Id, string Name, string? InstalledVersion, CheatRelease? Release, bool PendingSetup = false, bool NewTrainer = false)
{
    public bool IsUpdate => Release != null && (InstalledVersion == null || ReleaseVersion.Parse(Release.Version).CompareTo(ReleaseVersion.Parse(InstalledVersion)) > 0);
    public string Versions => Localization.Choose($"Current: {InstalledVersion ?? "Not installed"}   ·   Available: {Release?.Version ?? "Check for updates"}",
        $"当前版本：{InstalledVersion ?? "未安装"}   ·   可用版本：{Release?.Version ?? "请检查更新"}")
        + (PendingSetup ? Localization.Choose(" · Setup unfinished", " · 安装尚未完成") : "");
    public string Notes => Release == null ? "" : Localization.Choose(Release.NotesEnglish, string.IsNullOrWhiteSpace(Release.NotesChinese) ? Release.NotesEnglish : Release.NotesChinese);
    public string ActionLabel => Release != null && (Release.ContractVersion != 1 || ReleaseVersion.Parse(Release.MinimumClientVersion).CompareTo(ReleaseVersion.Parse(UpdateLauncher.CurrentVersion)) > 0)
        ? Localization.Choose("Client needed", "需更新客户端") : PendingSetup && !IsUpdate ? Localization.Choose("Finish update", "完成更新")
        : NewTrainer || InstalledVersion == null ? Localization.Choose("Add trainer", "添加修改器")
        : Localization.Choose(IsUpdate ? "Update" : "Install", IsUpdate ? "更新" : "安装");
    public string AccessibleAction => Name + " · " + ActionLabel;
}
