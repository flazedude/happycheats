using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HappyCheats.Bootstrap;

internal static class Program
{
    private static int Main(string[] args)
    {
        try {
            var assembly = typeof(Program).Assembly;
            using var hashStream = assembly.GetManifestResourceStream("HappyCheats.Bundle.sha256")
                ?? throw new IOException("The embedded HappyCheats package checksum is missing.");
            using var reader = new StreamReader(hashStream);
            string hash = reader.ReadToEnd().Trim();
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HappyCheats", "Bundles");
            string app = BundleCache.Ensure(cache, hash, () => assembly.GetManifestResourceStream("HappyCheats.Bundle.zip")
                ?? throw new IOException("The embedded HappyCheats package is missing."));
            // Preserve the caller's working directory so relative screenshot/data-root
            // arguments have the same meaning as launching the ordinary client.
            var start = new ProcessStartInfo(Path.Combine(app, "HappyCheats.exe")) {
                WorkingDirectory = Environment.CurrentDirectory, UseShellExecute = false
            };
            foreach (string argument in args) start.ArgumentList.Add(argument);
            using var child = Process.Start(start) ?? throw new IOException("HappyCheats could not start.");
            // Capture mode is also a real first-run smoke test for the complete bundle.
            if (args.Contains("--screenshot", StringComparer.Ordinal)) { child.WaitForExit(); return child.ExitCode; }
            return 0;
        } catch (Exception error) {
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HappyCheats", "bootstrap-error.log");
            try { Directory.CreateDirectory(Path.GetDirectoryName(log)!); File.WriteAllText(log, error.ToString()); } catch { }
            int capture = Array.IndexOf(args, "--screenshot");
            if (capture >= 0 && capture + 1 < args.Length) {
                try { File.WriteAllText(Path.GetFullPath(args[capture + 1]) + ".bundle-error.txt", error.ToString()); } catch { }
            } else if (OperatingSystem.IsWindows()) {
                MessageBox(IntPtr.Zero, "HappyCheats could not start / 无法启动 HappyCheats\n\n" + error.Message + "\n\n" + log, "HappyCheats", 0x10);
            }
            return 1;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
