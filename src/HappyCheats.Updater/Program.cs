using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using HappyCheats.Updater;

internal static class Program
{
    private static int Main(string[] args)
    {
        ApplyRequest? request = null;
        try
        {
            if (args.Length != 2 || args[0] != "--request") throw new IOException("Start updates from HappyCheats.");
            request = JsonSerializer.Deserialize<ApplyRequest>(File.ReadAllText(args[1])) ?? throw new IOException("Invalid update request.");
            if (!UpdateInstaller.Same(Path.GetDirectoryName(args[1]), request.WorkDirectory)) throw new IOException("Invalid update request location.");
            UpdateInstaller.Validate(request);
            var expectedExe = Path.Combine(request.TargetDirectory, "HappyCheats.exe");
            using var parent = Process.GetProcessById(request.ParentProcessId);
            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartTimeUtcTicks ||
                !UpdateInstaller.Same(parent.MainModule?.FileName, expectedExe))
                throw new IOException("The running application no longer matches this update request.");
            CheckOtherClients(expectedExe, parent.Id);
            File.WriteAllText(Path.Combine(request.WorkDirectory, "helper-ready.txt"), "ready");
            if (!parent.WaitForExit(60_000)) throw new IOException("HappyCheats did not close. Close it and retry the update.");
            CheckOtherClients(expectedExe, -1);
            UpdateInstaller.Apply(request, target => LaunchAndConfirm(target, request.WorkDirectory));
            return 0;
        }
        catch (Exception error)
        {
            var logRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HappyCheats");
            try { Directory.CreateDirectory(logRoot); File.WriteAllText(Path.Combine(logRoot, "update-error.log"), error.ToString()); } catch { }
            if (OperatingSystem.IsWindows()) MessageBox(IntPtr.Zero,
                "HappyCheats 更新未完成 / Update could not finish.\n\n" + error.Message + "\n\n" + Path.Combine(logRoot, "update-error.log"),
                "HappyCheats", 0x10);
            return 1;
        }
    }

    private static void LaunchAndConfirm(string target, string work)
    {
        var receipt = Path.Combine(work, "launch-ready.txt");
        if (File.Exists(receipt)) throw new IOException("The restart receipt already exists.");
        var info = new ProcessStartInfo(Path.Combine(target, "HappyCheats.exe")) { WorkingDirectory = target, UseShellExecute = false };
        info.ArgumentList.Add("--update-ready");
        info.ArgumentList.Add(receipt);
        using var child = Process.Start(info) ?? throw new IOException("The updated application could not start.");
        var watch = Stopwatch.StartNew();
        try
        {
            while (watch.Elapsed < TimeSpan.FromSeconds(60))
            {
                if (child.HasExited) throw new IOException("The updated application exited during startup.");
                if (File.Exists(receipt)) return;
                Thread.Sleep(100);
            }
            throw new IOException("The updated application did not confirm startup.");
        }
        catch
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(10_000); }
            throw;
        }
    }

    private static void CheckOtherClients(string expectedExe, int parentId)
    {
        foreach (var process in Process.GetProcessesByName("HappyCheats"))
        {
            using (process)
            {
                if (process.Id == parentId) continue;
                try
                {
                    if (!process.HasExited && UpdateInstaller.Same(process.MainModule?.FileName, expectedExe))
                        throw new IOException("Another HappyCheats window is open. Close it before updating.");
                }
                catch (InvalidOperationException) { /* The process exited while being inspected. */ }
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
