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
    private readonly Func<Task> _reload;
    private readonly CancellationToken _lifetime;
    private CheatUpdateCheck? _check;
    private bool _busy;
    private string _status = "", _error = "";
    public ObservableCollection<CheatUpdateRow> Rows { get; } = new();
    public string WindowTitle => Localization.Choose("Cheat updates", "修改器更新");
    public string Introduction => Localization.Choose($"HappyCheats {UpdateLauncher.CurrentVersion} · Each game has its own cheat version. Download only the game you choose. Client updates remain under Updates.",
        $"HappyCheats {UpdateLauncher.CurrentVersion} · 每款游戏拥有独立的修改器版本。仅下载所选游戏的更新。客户端更新仍位于“客户端更新”。");
    public string CheckLabel => Localization.Choose("Check for updates", "检查更新");
    public string ReloadLabel => Localization.Choose("Reload library", "重新加载修改器库");
    public string Status => _status;
    public string Error => _error;

    internal CheatUpdateWindow(CheatFeedConfiguration? configuration, CheatStore? store, IReadOnlyDictionary<string, string> bundled,
        IEnumerable<(string Id, string Name)> games, string? startupError, bool isolated, Func<Task> reload, CancellationToken lifetime,
        HttpClient? http = null)
    {
        _store = store; _bundled = bundled; _names = games.ToDictionary(x => x.Id, x => x.Name);
        _isolated = isolated; _reload = reload; _lifetime = lifetime;
        if (configuration != null) _client = new(http ?? Http, configuration);
        _error = startupError ?? "";
        InitializeComponent(); DataContext = this; ThemeManager.Attach(this); WindowLayout.Attach(this);
        Closing += (_, e) => { if (_busy && !_lifetime.IsCancellationRequested) { e.Cancel = true; Hide(); } };
        try { Populate(); } catch (Exception error) { Report(error); }
    }

    private void Populate()
    {
        var installed = _store?.InstalledVersions(_bundled) ?? new Dictionary<string, string>(_bundled);
        Rows.Clear();
        var ids = _names.Keys.Union(_check?.Catalog.Games.Select(x => x.Id) ?? []);
        foreach (var id in ids.OrderBy(id => _names.GetValueOrDefault(id, _check?.Catalog.Games.FirstOrDefault(x => x.Id == id)?.Name ?? id), StringComparer.CurrentCultureIgnoreCase))
        {
            var release = _check?.Catalog.Games.SingleOrDefault(x => x.Id == id);
            Rows.Add(new(id, _names.GetValueOrDefault(id, release?.Name ?? id), installed.GetValueOrDefault(id), release));
        }
    }

    public Task CheckAsync() => OperateAsync(async () =>
    {
        RequireConfigured();
        _status = Localization.Choose("Checking signed cheat catalogs on both LAN hosts and GitHub…", "正在检查两台局域网主机及 GitHub 上的签名修改器目录……"); Refresh();
        _check = await _client!.CheckAsync(_store!.Root, _lifetime);
        Populate();
        var count = Rows.Count(x => x.IsUpdate);
        _status = count == 0 ? Localization.Choose("All listed cheats are current.", "所有已列出的修改器均为最新版本。")
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
        if (_check == null || row.Release == null) throw new IOException(Localization.Choose("Check for updates first, then choose this game again.", "请先检查更新，再选择此游戏。"));
        CheatStore.RequireCompatible(row.Release, UpdateLauncher.CurrentVersion);
        _status = Localization.Choose($"Downloading {row.Name}…", $"正在下载《{row.Name}》的修改器……"); Refresh();
        await _client!.InstallAsync(_check, row.Release, _store!, UpdateLauncher.CurrentVersion,
            new Progress<UpdateProgress>(p => { _status = Localization.Choose($"{row.Name}: {p.BytesReceived / 1048576d:F1} / {p.TotalBytes / 1048576d:F1} MB", $"{row.Name}：{p.BytesReceived / 1048576d:F1} / {p.TotalBytes / 1048576d:F1} MB"); Refresh(); }), _lifetime);
        Populate();
        _status = Localization.Choose($"{row.Name} {row.Release.Version} verified. Close supported games, choose Reload library, then use the game's Install / update action if it installs trainer files. HappyCheats stays at {UpdateLauncher.CurrentVersion}.",
            $"《{row.Name}》修改器 {row.Release.Version} 已通过验证。请关闭支持的游戏，点击“重新加载修改器库”，随后按需在游戏页面点击“安装 / 更新”。HappyCheats 版本保持 {UpdateLauncher.CurrentVersion}。");
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
    private async void GameUpdateClicked(object sender, RoutedEventArgs e) { if (sender is Button { Tag: CheatUpdateRow row }) await InstallAsync(row); }
    private async void ReloadClicked(object sender, RoutedEventArgs e) => await OperateAsync(async () =>
    {
        RequireConfigured(); await _reload();
        _status = Localization.Choose("Cheat library reloaded. Apply the updated trainer from its game page. The client version is unchanged.", "修改器库已重新加载，请在对应游戏页面应用更新后的修改器。客户端版本保持不变。");
    });
    public void RefreshLanguage() { try { Populate(); } catch (Exception error) { Report(error); } Refresh(); }
    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record CheatUpdateRow(string Id, string Name, string? InstalledVersion, CheatRelease? Release)
{
    public bool IsUpdate => Release != null && (InstalledVersion == null || ReleaseVersion.Parse(Release.Version).CompareTo(ReleaseVersion.Parse(InstalledVersion)) > 0);
    public string Versions => Localization.Choose($"Installed: {InstalledVersion ?? "Not installed"}   ·   Available: {Release?.Version ?? "Check for updates"}",
        $"已安装：{InstalledVersion ?? "未安装"}   ·   可用版本：{Release?.Version ?? "请检查更新"}");
    public string Notes => Release == null ? "" : Localization.Choose(Release.NotesEnglish, string.IsNullOrWhiteSpace(Release.NotesChinese) ? Release.NotesEnglish : Release.NotesChinese);
    public string ActionLabel => Release != null && (Release.ContractVersion != 1 || ReleaseVersion.Parse(Release.MinimumClientVersion).CompareTo(ReleaseVersion.Parse(UpdateLauncher.CurrentVersion)) > 0)
        ? Localization.Choose("Client needed", "需更新客户端") : Localization.Choose(Release == null ? "Check first" : InstalledVersion == null ? "Add game" : IsUpdate ? "Update" : "Repair", Release == null ? "请先检查" : InstalledVersion == null ? "添加游戏" : IsUpdate ? "更新" : "修复");
    public string AccessibleAction => Name + " · " + ActionLabel;
}
