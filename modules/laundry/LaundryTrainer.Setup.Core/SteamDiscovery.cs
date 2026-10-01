using Microsoft.Win32;
using System.Runtime.Versioning;

namespace LaundryTrainer.Setup.Core;

public static class SteamDiscovery
{
    public static string? FindSteamExecutable()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var roots = new List<string>();
        ReadRegistryRoots(roots, new List<string>());
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var executable = SafePaths.Child(root, "steam.exe");
                SafePaths.AssertPlain(executable);
                if (File.Exists(executable)) return executable;
            }
            catch (Exception error) when (Expected(error)) { }
        }
        return null;
    }

    public static SteamDiscoveryResult Discover()
    {
        var problems = new List<string>();
        var roots = new List<string>();
        if (OperatingSystem.IsWindows()) ReadRegistryRoots(roots, problems);
        else problems.Add("仅 Windows 支持自动查找 Steam 游戏。");
        var result = DiscoverFromRoots(roots);
        return result with { Problems = problems.Concat(result.Problems).ToArray() };
    }

    public static SteamDiscoveryResult DiscoverFromRoots(IEnumerable<string> steamDirectories) =>
        DiscoverFromRoots(steamDirectories, CompatibilityProfile.Current);

    internal static SteamDiscoveryResult DiscoverFromRoots(IEnumerable<string> steamDirectories, CompatibilityProfile profile)
    {
        var problems = new List<string>();
        var installations = new List<SteamInstallation>();
        var librariesVisited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in steamDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string root;
            try
            {
                root = SafePaths.Absolute(candidate);
                SafePaths.AssertPlain(root);
                if (!Directory.Exists(root)) throw new IOException("Steam 目录不存在。");
            }
            catch (Exception error) when (Expected(error))
            {
                problems.Add($"Steam 位置 {candidate}：{error.Message}");
                continue;
            }
            var executable = Path.Combine(root, "steam.exe");
            var libraries = new List<string> { root };
            foreach (var relative in new[] { "steamapps/libraryfolders.vdf", "config/libraryfolders.vdf" })
            {
                var file = SafePaths.Child(root, relative);
                try
                {
                    SafePaths.AssertPlain(file);
                    if (File.Exists(file)) libraries.AddRange(SteamVdf.ReadLibraryPaths(File.ReadAllText(file)));
                }
                catch (Exception error) when (Expected(error))
                {
                    problems.Add($"无法读取 Steam 游戏库列表 {file}：{error.Message}");
                }
            }
            foreach (var libraryCandidate in libraries)
            {
                try
                {
                    var library = SafePaths.Absolute(libraryCandidate);
                    if (!librariesVisited.Add(library)) continue;
                    SafePaths.AssertPlain(library);
                    if (!Directory.Exists(library)) throw new IOException("Steam 游戏库目录不存在，或所在驱动器未连接。");
                    var manifestPath = SafePaths.Child(library, $"steamapps/appmanifest_{GameInspector.SteamAppId}.acf");
                    SafePaths.AssertPlain(manifestPath);
                    if (!File.Exists(manifestPath)) continue;
                    var manifest = SteamVdf.ReadAppManifest(File.ReadAllText(manifestPath));
                    if (manifest.AppId != GameInspector.SteamAppId)
                        throw new IOException($"《洗衣店模拟器》安装清单中的应用 ID 不正确：{manifest.AppId ?? "缺失"}。");
                    var gameDirectory = SafePaths.Child(SafePaths.Child(library, "steamapps/common"), manifest.InstallDirectory);
                    var game = GameInspector.Probe(gameDirectory, profile);
                    if (!manifest.IsComplete && !game.Problems.Any(p => p.Contains("StateFlags", StringComparison.Ordinal)))
                        game = game with { Problems = game.Problems.Append($"Steam 尚未完成安装（StateFlags：{manifest.StateFlags ?? "缺失"}，缺少已安装大小，或仍有文件等待下载或准备）。").ToArray() };
                    installations.Add(new SteamInstallation(root, File.Exists(executable) ? executable : null, library,
                        manifestPath, gameDirectory, manifest.StateFlags, game));
                }
                catch (Exception error) when (Expected(error))
                {
                    problems.Add($"无法检查 Steam 游戏库 {libraryCandidate}：{error.Message}");
                }
            }
        }
        if (installations.Count == 0)
            problems.Add("未在 Steam 已注册的游戏库中找到《洗衣店模拟器》。您可以手动选择已安装的游戏文件夹。");
        return new SteamDiscoveryResult(installations, problems);
    }

    [SupportedOSPlatform("windows")]
    private static void ReadRegistryRoots(List<string> roots, List<string> problems)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(@"Software\Valve\Steam");
                if (key is null) continue;
                foreach (var name in new[] { "SteamPath", "InstallPath" })
                    if (key.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value))
                        roots.Add(value.Replace('/', Path.DirectorySeparatorChar));
                if (key.GetValue("SteamExe") is string steamExe && !string.IsNullOrWhiteSpace(steamExe))
                {
                    var directory = Path.GetDirectoryName(steamExe.Replace('/', Path.DirectorySeparatorChar));
                    if (!string.IsNullOrWhiteSpace(directory)) roots.Add(directory);
                }
            }
            catch (Exception error) when (Expected(error) || error is System.Security.SecurityException)
            {
                problems.Add($"无法读取 Steam 注册表位置（{hive}，{view}）：{error.Message}");
            }
        }
    }

    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException;
}
