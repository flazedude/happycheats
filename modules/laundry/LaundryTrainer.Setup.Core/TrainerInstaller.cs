namespace LaundryTrainer.Setup.Core;

public static class TrainerInstaller
{
    private static readonly string[] ConflictingPaths =
    [
        "dobby.dll", "winhttp.dll", "doorstop_config.ini", "BepInEx", "dinput8.dll", "dxgi.dll",
        "d3d11.dll", "dsound.dll", "winmm.dll", "opengl32.dll", "ReShade.ini", "reshade-shaders",
        "Plugins/LaundryTrainer.dll"
    ];

    /// <summary>
    /// Installs from an already extracted, checksum-verified official MelonLoader release.
    /// The caller is responsible for validating its embedded archive before extraction.
    /// </summary>
    public static InstallResult Install(string payloadRoot, string trainerDll, string gameDirectory, IProgress<string>? progress = null) =>
        Install(payloadRoot, trainerDll, gameDirectory, CompatibilityProfile.Current, progress);

    internal static InstallResult Install(string payloadRoot, string trainerDll, string gameDirectory,
        CompatibilityProfile profile, IProgress<string>? progress = null, Func<bool>? runningCheck = null)
    {
        payloadRoot = SafePaths.Absolute(payloadRoot);
        trainerDll = SafePaths.Absolute(trainerDll);
        gameDirectory = SafePaths.Absolute(gameDirectory);
        SafePaths.AssertPlain(payloadRoot);
        SafePaths.AssertPlain(trainerDll);
        SafePaths.AssertPlain(gameDirectory);
        if (!File.Exists(trainerDll) || new FileInfo(trainerDll).Length == 0)
            throw new IOException("安装包中的修改器 DLL 缺失或为空。");
        var trainerHash = SafePaths.Hash(trainerDll);
        var files = BuildPayloadPlan(payloadRoot, gameDirectory);
        progress?.Report("正在检查游戏和现有模组加载器……");
        AssertInstallable(GameInspector.Probe(gameDirectory, profile, runningCheck));
        foreach (var relative in ConflictingPaths)
        {
            var path = SafePaths.Child(gameDirectory, relative);
            SafePaths.AssertPlain(path);
            if (Path.Exists(path))
                throw new IOException($"检测到已有的模组加载器或注入文件，需要手动检查。该文件未作更改：{path}");
        }

        var bootstrap = SafePaths.Child(gameDirectory, "version.dll");
        var loaderDirectory = SafePaths.Child(gameDirectory, "MelonLoader");
        var existingLoader = Path.Exists(bootstrap) || Path.Exists(loaderDirectory);
        foreach (var file in files)
        {
            SafePaths.AssertPlain(file.Destination);
            if (Path.Exists(file.Destination))
            {
                if (!File.Exists(file.Destination) || !SafePaths.SameFile(file.Source, file.Destination))
                    throw new IOException($"现有加载器与内置的 MelonLoader 0.7.3 不一致。该文件未作更改：{file.Relative}");
            }
            else if (existingLoader)
                throw new IOException($"现有 MelonLoader 安装不完整或版本不同（缺少 {file.Relative}）。请手动修复后再安装此修改器。");
        }
        var trainerPath = SafePaths.Child(gameDirectory, "Mods/LaundryTrainer.dll");
        SafePaths.AssertPlain(trainerPath);
        AssertDirectoryOrMissing(Path.GetDirectoryName(trainerPath)!);
        if (Directory.Exists(trainerPath))
            throw new IOException($"同名文件夹与修改器 DLL 冲突：{trainerPath}");
        var trainerExists = File.Exists(trainerPath);
        var oldTrainerHash = trainerExists ? SafePaths.Hash(trainerPath) : null;
        var updateTrainer = oldTrainerHash != trainerHash;
        string? backupPath = null;
        if (trainerExists && updateTrainer)
        {
            backupPath = SafePaths.Child(gameDirectory, $"UserData/LaundryTrainer/Backups/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}/LaundryTrainer.dll");
            SafePaths.AssertPlain(backupPath);
            AssertDirectoryOrMissing(Path.GetDirectoryName(backupPath)!);
        }

        // Recheck after hashing the package and loader; this can take time on large game files.
        AssertInstallable(GameInspector.Probe(gameDirectory, profile, runningCheck));
        var journal = new Journal(gameDirectory);
        var trainerReplaced = false;
        try
        {
            if (!existingLoader)
            {
                progress?.Report("正在安装 MelonLoader 0.7.3 依赖组件……");
                foreach (var file in files.Where(f => !f.IsBootstrap))
                    CopyNewVerified(file.Source, file.Destination, file.Hash, journal);
            }
            else progress?.Report("正在使用已验证的 MelonLoader 0.7.3 安装……");

            if (updateTrainer)
            {
                journal.EnsureDirectory(Path.GetDirectoryName(trainerPath)!);
                var stagedTrainer = SafePaths.Child(gameDirectory, $"Mods/.LaundryTrainer-{Guid.NewGuid():N}.tmp");
                CopyNewVerified(trainerDll, stagedTrainer, trainerHash, journal);
                if (backupPath is not null) journal.EnsureDirectory(Path.GetDirectoryName(backupPath)!);
                AssertNotRunning(runningCheck);
                progress?.Report("正在安装修改器……");
                SafePaths.AssertPlain(trainerPath);
                if (trainerExists)
                {
                    if (!File.Exists(trainerPath) || SafePaths.Hash(trainerPath) != oldTrainerHash)
                        throw new IOException("准备安装时，已安装的修改器发生了变化。请重新安装。");
                    File.Replace(stagedTrainer, trainerPath, backupPath);
                    trainerReplaced = true;
                    if (SafePaths.Hash(backupPath!) != oldTrainerHash)
                        throw new IOException("无法验证修改器备份。");
                }
                else
                {
                    File.Move(stagedTrainer, trainerPath); // Never replaces a file created by another installer.
                    journal.CreatedFile(trainerPath, trainerHash);
                }
            }

            if (!existingLoader)
            {
                // The bootstrap activates the loader; publish it only after dependencies and trainer succeed.
                AssertNotRunning(runningCheck);
                progress?.Report("正在启用模组加载器……");
                var version = files.Single(f => f.IsBootstrap);
                var stagedBootstrap = SafePaths.Child(gameDirectory, $".LaundryTrainer-bootstrap-{Guid.NewGuid():N}.tmp");
                CopyNewVerified(version.Source, stagedBootstrap, version.Hash, journal);
                AssertNotRunning(runningCheck);
                File.Move(stagedBootstrap, version.Destination);
                journal.CreatedFile(version.Destination, version.Hash);
            }
            return new InstallResult(gameDirectory, trainerPath, backupPath, !existingLoader, updateTrainer);
        }
        catch (Exception error)
        {
            var rollbackProblems = new List<string>();
            if (trainerReplaced && backupPath is not null)
            {
                try
                {
                    SafePaths.AssertPlain(trainerPath);
                    if (SafePaths.Hash(trainerPath) != trainerHash)
                        throw new IOException("修改器被其他程序更改；旧版本仍保存在备份中。");
                    var recovery = SafePaths.Child(gameDirectory, $"Mods/.LaundryTrainer-recovery-{Guid.NewGuid():N}.tmp");
                    CopyNewVerified(backupPath, recovery, oldTrainerHash!, journal);
                    File.Replace(recovery, trainerPath, null);
                }
                catch (Exception recoveryError) { rollbackProblems.Add($"无法恢复旧版修改器：{recoveryError.Message}。备份位置：{backupPath}"); }
            }
            rollbackProblems.AddRange(journal.Rollback());
            var suffix = rollbackProblems.Count == 0 ? " 本次安装创建的文件已回滚。" :
                " 部分清理操作需要处理：" + string.Join("；", rollbackProblems);
            // The desktop launcher can safely request elevation only after a clean rollback.
            if (error is UnauthorizedAccessException && rollbackProblems.Count == 0)
                throw new UnauthorizedAccessException($"此游戏目录需要管理员权限：{error.Message}{suffix}", error);
            throw new IOException($"安装未完成：{error.Message}{suffix}", error);
        }
    }

    private static List<PayloadFile> BuildPayloadPlan(string sourceRoot, string targetRoot)
    {
        if (!Directory.Exists(sourceRoot)) throw new IOException("解压后的加载器文件缺失。");
        var files = new List<PayloadFile>();
        foreach (var source in SafePaths.EnumeratePlainFiles(sourceRoot))
        {
            var relative = Path.GetRelativePath(sourceRoot, source).Replace('\\', '/');
            var bootstrap = relative.Equals("version.dll", StringComparison.OrdinalIgnoreCase);
            if (!bootstrap && !relative.StartsWith("MelonLoader/", StringComparison.OrdinalIgnoreCase))
                throw new IOException($"加载器文件包的根目录包含意外文件：{relative}");
            var destination = SafePaths.Child(targetRoot, relative);
            files.Add(new PayloadFile(source, destination, relative, SafePaths.Hash(source), bootstrap));
        }
        if (files.Count(f => f.IsBootstrap) != 1 || !files.Any(f => f.Relative.Equals("MelonLoader/net6/MelonLoader.dll", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("加载器文件包必须包含 version.dll 和 MelonLoader/net6/MelonLoader.dll。");
        if (files.Select(f => f.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new IOException("加载器文件包包含重复的目标路径。");
        return files;
    }

    private static void AssertInstallable(GameInspection game)
    {
        if (!game.CanInstall) throw new IOException(string.Join(Environment.NewLine, game.Problems));
    }

    private static void AssertNotRunning(Func<bool>? check)
    {
        if (check?.Invoke() ?? GameInspector.IsGameRunning())
            throw new IOException("安装过程中游戏被启动。请关闭游戏后重试。");
    }

    private static void AssertDirectoryOrMissing(string directory)
    {
        var cursor = directory;
        while (!string.IsNullOrEmpty(cursor))
        {
            SafePaths.AssertPlain(cursor);
            if (File.Exists(cursor)) throw new IOException($"同名文件与所需目录冲突：{cursor}");
            cursor = Path.GetDirectoryName(cursor);
        }
    }

    private static void CopyNewVerified(string source, string destination, string hash, Journal journal)
    {
        journal.EnsureDirectory(Path.GetDirectoryName(destination)!);
        SafePaths.AssertPlain(source);
        SafePaths.AssertPlain(destination);
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            journal.CreatedFile(destination, null);
            using var input = File.OpenRead(source);
            input.CopyTo(output);
            output.Flush(true);
        }
        var copiedHash = SafePaths.Hash(destination);
        journal.CreatedFile(destination, copiedHash);
        if (copiedHash != hash) throw new IOException($"复制校验失败：{Path.GetFileName(destination)}");
    }

    private sealed record PayloadFile(string Source, string Destination, string Relative, string Hash, bool IsBootstrap);

    private sealed class Journal(string root)
    {
        private readonly Dictionary<string, string?> files = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> directories = [];

        internal void CreatedFile(string path, string? hash) => files[path] = hash;

        internal void EnsureDirectory(string path)
        {
            SafePaths.AssertPlain(path);
            if (Directory.Exists(path)) return;
            if (File.Exists(path)) throw new IOException($"同名文件与目标目录冲突：{path}");
            var parent = Path.GetDirectoryName(path)!;
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("安装目录超出所选游戏文件夹。");
            EnsureDirectory(parent);
            Directory.CreateDirectory(path);
            directories.Add(path);
        }

        internal IReadOnlyList<string> Rollback()
        {
            var problems = new List<string>();
            foreach (var (path, hash) in files.Reverse())
            {
                try
                {
                    SafePaths.AssertPlain(path);
                    if (!File.Exists(path)) continue;
                    if (hash is not null && SafePaths.Hash(path) != hash)
                        throw new IOException("文件在安装程序写入后发生了变化，已保留该文件。");
                    File.Delete(path);
                }
                catch (Exception error) { problems.Add($"{path}: {error.Message}"); }
            }
            foreach (var path in directories.OrderByDescending(p => p.Length))
            {
                try
                {
                    SafePaths.AssertPlain(path);
                    if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                        Directory.Delete(path, false);
                }
                catch (Exception error) { problems.Add($"{path}: {error.Message}"); }
            }
            return problems;
        }
    }
}
