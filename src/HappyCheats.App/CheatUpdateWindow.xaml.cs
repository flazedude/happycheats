using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
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
    private readonly bool _isolated;
    private readonly Func<CheatRelease, Func<Task>, Task<string>> _apply;
    private readonly HashSet<string> _completed = new(StringComparer.Ordinal);
    private readonly CancellationToken _lifetime;
    private readonly Func<Task<CheatUpdateCheck>>? _checkForUpdates;
    private string? _gameId;
    private string _focusReason = "";
    private CheatUpdateCheck? _check;
    private bool _busy;
    private string _status = "", _error = "";
    public ObservableCollection<CheatUpdateRow> Rows { get; } = new();
    public string WindowTitle => Localization.Choose("Cheat updates", "修改器更新")
        + (_gameId == null ? "" : " · " + _names.GetValueOrDefault(_gameId, _gameId));
    public string FocusReason => _focusReason;
    public string Introduction => Localization.Choose("Close the game, then click Update. HappyCheats downloads, repairs and installs its trainer for you. When it finishes, launch the game and play.",
        "关闭游戏后点击“更新”。HappyCheats 会自动下载、修复并安装修改器。完成后即可启动游戏游玩。");
    public string CheckLabel => _gameId == null ? Localization.Choose("Check for updates", "检查更新")
        : Localization.Choose("Check this game", "检查此游戏更新");
    public string Status => _status;
    public string Error => _error;

    internal CheatUpdateWindow(CheatFeedConfiguration? configuration, CheatStore? store, IReadOnlyDictionary<string, string> bundled,
        IEnumerable<(string Id, string Name)> games, string? startupError, bool isolated, Func<CheatRelease, Func<Task>, Task<string>> apply, CancellationToken lifetime,
        HttpClient? http = null, Func<Task<CheatUpdateCheck>>? checkForUpdates = null)
    {
        _store = store; _bundled = bundled; _names = games.ToDictionary(x => x.Id, x => x.Name);
        _isolated = isolated; _apply = apply; _lifetime = lifetime;
        _checkForUpdates = checkForUpdates;
        if (configuration != null) _client = new(http ?? Http, configuration);
        _error = startupError ?? "";
        InitializeComponent(); DataContext = this; ThemeManager.Attach(this); WindowLayout.Attach(this);
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
        var ids = _gameId == null ? _names.Keys.Union(_check?.Catalog.Games.Select(x => x.Id) ?? []) : new[] { _gameId };
        foreach (var id in ids.OrderBy(id => _names.GetValueOrDefault(id, _check?.Catalog.Games.FirstOrDefault(x => x.Id == id)?.Name ?? id), StringComparer.CurrentCultureIgnoreCase))
        {
            var release = _check?.Catalog.Games.SingleOrDefault(x => x.Id == id);
            Rows.Add(new(id, _names.GetValueOrDefault(id, release?.Name ?? id), installed.GetValueOrDefault(id), release,
                release != null && _completed.Contains(id + "/" + release.Version)));
        }
    }

    public Task CheckAsync() => OperateAsync(async () =>
    {
        RequireConfigured();
        _status = Localization.Choose("Checking signed cheat catalogs on both LAN hosts and GitHub…", "正在检查两台局域网主机及 GitHub 上的签名修改器目录……"); Refresh();
        _check = _checkForUpdates != null ? await _checkForUpdates() : await _client!.CheckAsync(_store!.Root, _lifetime);
        Populate();
        var count = Rows.Count(x => x.IsUpdate);
        _status = count == 0 ? Localization.Choose("No newer trainer packages. Click Install to install or repair the current trainer. Unsupported game builds still need a compatible trainer release.",
                "暂无更新的修改器包。点击“安装”即可安装或修复当前修改器。不受支持的游戏版本仍需等待兼容的修改器发布。")
            : Localization.Choose($"{count} game updates available. Choose a game below.", $"有 {count} 款游戏的修改器可更新。请在下方选择游戏。");
    });

    private void RequireConfigured()
    {
        if (_isolated) throw new IOException(Localization.Choose("Cheat downloads are unavailable in this isolated preview. Open the published client to update.", "隔离预览不提供修改器下载，请打开发布客户端进行更新。"));
        if (_client == null || _store == null) throw new IOException(Localization.Choose("Cheat updates require the official trusted update configuration. Use the complete official client or restore its update settings.", "修改器更新需要官方受信任的更新配置。请使用完整官方客户端或恢复其更新设置。"));
    }

    internal Task InstallAsync(CheatUpdateRow row) => OperateAsync(async () =>
    {
        RequireConfigured();
        if (_check == null || row.Release == null)
        {
            _check = _checkForUpdates != null ? await _checkForUpdates() : await _client!.CheckAsync(_store!.Root, _lifetime);
            Populate();
            row = Rows.Single(x => x.Id == row.Id);
        }
        if (row.Release == null) throw new IOException(Localization.Choose("No trainer package is published for this game yet. Use Install / update in its Library page.", "此游戏尚未发布独立修改器包，请在游戏页面点击“安装 / 更新”。"));
        if (row.InstalledVersion != null && ReleaseVersion.Parse(row.InstalledVersion).CompareTo(ReleaseVersion.Parse(row.Release.Version)) > 0)
            throw new IOException(Localization.Choose("Your bundled trainer is newer than this package. Use Install / update in its Library page.", "内置修改器比此包更新，请在游戏页面点击“安装 / 更新”。"));
        CheatStore.RequireCompatible(row.Release, UpdateLauncher.CurrentVersion);
        _status = Localization.Choose($"Downloading {row.Name}…", $"正在下载《{row.Name}》的修改器……"); Refresh();
        _completed.Remove(row.Id + "/" + row.Release.Version);
        try
        {
            _status = await _apply(row.Release, async () =>
            {
                await _client!.InstallAsync(_check!, row.Release, _store!, UpdateLauncher.CurrentVersion,
                    new Progress<UpdateProgress>(p => { _status = Localization.Choose($"{row.Name}: {p.BytesReceived / 1048576d:F1} / {p.TotalBytes / 1048576d:F1} MB", $"{row.Name}：{p.BytesReceived / 1048576d:F1} / {p.TotalBytes / 1048576d:F1} MB"); Refresh(); }), _lifetime);
                _status = Localization.Choose($"Installing {row.Name} trainer…", $"正在安装《{row.Name}》的修改器……"); Refresh();
            });
            _completed.Add(row.Id + "/" + row.Release.Version);
            _error = "";
            _focusReason = "";
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
        _busy = true; _error = ""; Refresh();
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { Report(error); }
        finally { _busy = false; Refresh(); }
    }

    private void Report(Exception error) { _error = error.Message; ClientErrorLog.Write("cheat-updates", error.Message); Refresh(); }
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

public sealed record CheatUpdateRow(string Id, string Name, string? InstalledVersion, CheatRelease? Release, bool Applied = false)
{
    public bool IsUpdate => Release != null && (InstalledVersion == null || ReleaseVersion.Parse(Release.Version).CompareTo(ReleaseVersion.Parse(InstalledVersion)) > 0);
    public string Versions => Localization.Choose($"Current: {InstalledVersion ?? "Not installed"}   ·   Available: {Release?.Version ?? "Check for updates"}",
        $"当前版本：{InstalledVersion ?? "未安装"}   ·   可用版本：{Release?.Version ?? "请检查更新"}")
        + (Applied ? Localization.Choose(" · Trainer installed", " · 修改器已安装") : "");
    public string Notes => Release == null ? "" : Localization.Choose(Release.NotesEnglish, string.IsNullOrWhiteSpace(Release.NotesChinese) ? Release.NotesEnglish : Release.NotesChinese);
    public string ActionLabel => Release != null && (Release.ContractVersion != 1 || ReleaseVersion.Parse(Release.MinimumClientVersion).CompareTo(ReleaseVersion.Parse(UpdateLauncher.CurrentVersion)) > 0)
        ? Localization.Choose("Client needed", "需更新客户端") : Applied ? Localization.Choose("Reinstall", "重新安装")
        : Localization.Choose(IsUpdate ? "Update" : "Install", IsUpdate ? "更新" : "安装");
    public string AccessibleAction => Name + " · " + ActionLabel;
}
