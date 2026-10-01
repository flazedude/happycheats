using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using HappyCheats.Core.Updates;

namespace HappyCheats.App;

public static class UpdateLauncher
{
    public static string CurrentVersion
    {
        get
        {
            var version = typeof(UpdateLauncher).Assembly.GetName().Version!;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
    public static string StagingRoot => Path.Combine(Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.FullName,
        ".happycheats-updates");
    public static bool CanInstall => string.Equals(Path.GetFileName(Environment.ProcessPath), "HappyCheats.exe", StringComparison.OrdinalIgnoreCase)
        && File.Exists(Path.Combine(AppContext.BaseDirectory, "updater", "HappyCheats.Updater.exe"));

    public static void Start(StagedUpdate staged)
    {
        if (!CanInstall) throw new IOException("Install updates from a published HappyCheats client.");
        var work = Path.GetFullPath(staged.WorkDirectory);
        if (!string.Equals(Path.GetDirectoryName(work), Path.GetFullPath(StagingRoot), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The update staging directory is invalid.");
        var runner = Path.Combine(work, "runner");
        if (Directory.Exists(runner)) throw new IOException("This update was already started. Download it again.");
        CopyHelper(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "updater")), new DirectoryInfo(runner));
        using var current = Process.GetCurrentProcess();
        var requestPath = Path.Combine(work, "apply.json");
        var readyPath = Path.Combine(work, "helper-ready.txt");
        File.WriteAllText(requestPath, JsonSerializer.Serialize(new
        {
            TargetDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
            staged.PackageDirectory, staged.WorkDirectory,
            ParentProcessId = current.Id, ParentStartTimeUtcTicks = current.StartTime.ToUniversalTime().Ticks
        }));
        var start = new ProcessStartInfo(Path.Combine(runner, "HappyCheats.Updater.exe")) { WorkingDirectory = runner, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--request"); start.ArgumentList.Add(requestPath);
        using var helper = Process.Start(start) ?? throw new IOException("The update helper could not start.");
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (helper.HasExited) throw new IOException("The update helper could not prepare the update. Check the update error log.");
            if (File.Exists(readyPath)) return;
            Thread.Sleep(50);
        }
        // Parent has not closed, so the helper cannot have started the replacement yet.
        if (!helper.HasExited) { helper.Kill(); helper.WaitForExit(5000); }
        throw new IOException("The update helper did not respond. Try downloading the update again.");
    }

    private static void CopyHelper(DirectoryInfo source, DirectoryInfo target)
    {
        if ((source.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A linked update helper cannot be used.");
        target.Create();
        foreach (var item in source.EnumerateFileSystemInfos())
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A linked update helper cannot be used.");
            if (item is DirectoryInfo folder) CopyHelper(folder, new DirectoryInfo(Path.Combine(target.FullName, item.Name)));
            else File.Copy(item.FullName, Path.Combine(target.FullName, item.Name));
        }
    }

    public static void ConfirmStartup(string[] args)
    {
        var index = Array.IndexOf(args, "--update-ready");
        if (index < 0 || index + 1 >= args.Length) return;
        var receipt = Path.GetFullPath(args[index + 1]);
        var work = Path.GetDirectoryName(receipt)!;
        if (Path.GetFileName(receipt) != "launch-ready.txt" ||
            !Guid.TryParseExact(Path.GetFileName(work), "N", out _) ||
            !string.Equals(Path.GetDirectoryName(work), Path.GetFullPath(StagingRoot), StringComparison.OrdinalIgnoreCase)) return;
        File.WriteAllText(receipt, CurrentVersion);
    }
}
