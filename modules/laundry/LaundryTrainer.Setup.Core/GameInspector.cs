using System.Diagnostics;
using System.Globalization;

namespace LaundryTrainer.Setup.Core;

public static class GameInspector
{
    public const string GameExecutableName = "Laundry Store Simulator.exe";
    public const string MetadataRelativePath = "Laundry Store Simulator_Data/il2cpp_data/Metadata/global-metadata.dat";
    public const string SteamAppId = "3150440";

    public static GameInspection Probe(string directory) => Probe(directory, CompatibilityProfile.Current);

    internal static GameInspection Probe(string directory, CompatibilityProfile profile, Func<bool>? runningCheck = null)
    {
        var problems = new List<string>();
        var exists = false;
        var compatible = false;
        var running = false;
        string? nativeHash = null;
        string? metadataHash = null;
        try
        {
            directory = SafePaths.Absolute(directory);
            SafePaths.AssertPlain(directory);
            exists = System.IO.Directory.Exists(directory);
            if (!exists)
                problems.Add("游戏目录不存在。请等待 Steam 完成安装，或选择游戏已安装的文件夹。");
            else
            {
                var exe = SafePaths.Child(directory, GameExecutableName);
                var native = SafePaths.Child(directory, "GameAssembly.dll");
                var metadata = SafePaths.Child(directory, MetadataRelativePath);
                foreach (var path in new[] { exe, native, metadata })
                {
                    SafePaths.AssertPlain(path);
                    if (!File.Exists(path))
                        problems.Add($"缺少必要的游戏文件：{Path.GetRelativePath(directory, path)}。请等待 Steam 完成安装，或验证游戏文件完整性。");
                }
                if (problems.Count == 0)
                {
                    nativeHash = SafePaths.Hash(native);
                    metadataHash = SafePaths.Hash(metadata);
                    compatible = nativeHash.Equals(profile.NativeSha256, StringComparison.OrdinalIgnoreCase) &&
                        metadataHash.Equals(profile.MetadataSha256, StringComparison.OrdinalIgnoreCase);
                    if (!compatible)
                        problems.Add("此游戏版本尚未验证是否兼容本修改器。游戏文件未作更改，请更新修改器后再试。");
                }
                CheckAdjacentManifest(directory, problems);
            }
            running = runningCheck?.Invoke() ?? IsGameRunning();
            if (running)
                problems.Add("安装或更新修改器之前，请先关闭《洗衣店模拟器》。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            problems.Add(error.Message);
        }
        return new GameInspection(directory, exists, compatible, running, problems, nativeHash, metadataHash);
    }

    public static bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExecutableName));
        try { return processes.Any(p => !p.HasExited); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static bool IsFinishedState(string? value) =>
        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var flags) && flags == 4;

    private static void CheckAdjacentManifest(string directory, List<string> problems)
    {
        var common = System.IO.Directory.GetParent(directory);
        while (common is not null && !(common.Name.Equals("common", StringComparison.OrdinalIgnoreCase) &&
            common.Parent?.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) == true))
            common = common.Parent;
        if (common is null) return;
        var manifestPath = Path.Combine(common.Parent!.FullName, $"appmanifest_{SteamAppId}.acf");
        SafePaths.AssertPlain(manifestPath);
        if (!File.Exists(manifestPath)) return;
        var manifest = SteamVdf.ReadAppManifest(File.ReadAllText(manifestPath));
        if (manifest.AppId != SteamAppId)
            problems.Add("Steam 的《洗衣店模拟器》安装清单包含不正确的应用 ID。");
        if (!manifest.IsComplete)
            problems.Add($"Steam 尚未完成游戏安装或更新（StateFlags：{manifest.StateFlags ?? "缺失"}，SizeOnDisk：{manifest.SizeOnDisk?.ToString(CultureInfo.InvariantCulture) ?? "缺失"}，下载或文件准备尚未完成）。请等待 Steam 完成操作。");
        var declared = SafePaths.Child(common.FullName, manifest.InstallDirectory);
        if (!declared.Equals(directory, StringComparison.OrdinalIgnoreCase))
            problems.Add("Steam 的游戏安装清单指向其他目录。请等待 Steam 完成移动游戏后，重新扫描。");
    }
}
