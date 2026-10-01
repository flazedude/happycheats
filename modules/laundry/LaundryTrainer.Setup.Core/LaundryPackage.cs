using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Win32;

namespace LaundryTrainer.Setup.Core;

public static class LaundryPackage
{
    public const string LoaderSha256 = "5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416";
    public const string RuntimeSha256 = "0D20DEBB26FC8B2BC84F25FBD9D4596A6364AF8517EBF012E8B871127B798941";

    public static async Task InstallAsync(string packageDirectory, string gameDirectory, IProgress<string>? progress = null)
    {
        var inspection = GameInspector.Probe(gameDirectory);
        if (!inspection.CanInstall) throw new IOException(string.Join(Environment.NewLine, inspection.Problems));
        packageDirectory = SafePaths.Absolute(packageDirectory);
        SafePaths.AssertPlain(packageDirectory);
        var trainer = SafePaths.Child(packageDirectory, "LaundryTrainer.dll");
        var loader = SafePaths.Child(packageDirectory, "loader.zip");
        SafePaths.AssertPlain(trainer);
        SafePaths.AssertPlain(loader);
        var manifestPath = SafePaths.Child(packageDirectory, "manifest.json");
        SafePaths.AssertPlain(manifestPath);
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var expectedTrainer = manifest.RootElement.GetProperty("trainerSha256").GetString();
        if (string.IsNullOrWhiteSpace(expectedTrainer) || !SafePaths.Hash(trainer).Equals(expectedTrainer, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Laundry trainer package checksum does not match. Rebuild the launcher package.");
        if (!SafePaths.Hash(loader).Equals(LoaderSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MelonLoader package checksum does not match the verified 0.7.3 release.");
        if (!HasRuntime()) await InstallRuntimeAsync(packageDirectory, progress);

        var temporaryRoot = SafePaths.Absolute(Path.Combine(Path.GetTempPath(), "HappyCheats", "LaundryPackages"));
        SafePaths.AssertPlain(temporaryRoot);
        Directory.CreateDirectory(temporaryRoot);
        var temporary = SafePaths.Child(temporaryRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            using (var archive = ZipFile.OpenRead(loader)) ExtractVerified(archive, temporary);
            await Task.Run(() => TrainerInstaller.Install(temporary, trainer, gameDirectory, progress));
        }
        finally
        {
            // Only remove our verified UUID extraction directory, never a caller-supplied directory.
            if (Path.GetDirectoryName(temporary) == temporaryRoot && Guid.TryParseExact(Path.GetFileName(temporary), "N", out _))
            {
                SafePaths.AssertPlain(temporary);
                foreach (var file in SafePaths.EnumeratePlainFiles(temporary)) File.Delete(file);
                foreach (var directory in Directory.EnumerateDirectories(temporary, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                {
                    SafePaths.AssertPlain(directory);
                    Directory.Delete(directory, false);
                }
                Directory.Delete(temporary, false);
            }
        }
    }

    private static void ExtractVerified(ZipArchive archive, string destination)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) continue;
            if (name != "version.dll" && !name.StartsWith("MelonLoader/", StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected loader archive entry.");
            var path = SafePaths.Child(destination, name);
            if (!names.Add(path)) throw new InvalidDataException("Duplicate loader archive entry.");
            SafePaths.AssertPlain(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: false);
        }
    }

    public static string Remove(string gameDirectory)
    {
        gameDirectory = SafePaths.Absolute(gameDirectory);
        SafePaths.AssertPlain(gameDirectory);
        if (GameInspector.IsGameRunning()) throw new InvalidOperationException("Close Laundry Store Simulator before removing its trainer.");
        var trainer = SafePaths.Child(gameDirectory, "Mods/LaundryTrainer.dll");
        SafePaths.AssertPlain(trainer);
        if (!File.Exists(trainer)) return "Laundry trainer is already removed.";
        var backup = SafePaths.Child(gameDirectory, $"UserData/HappyCheats/removed/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}/LaundryTrainer.dll");
        SafePaths.AssertPlain(backup);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Move(trainer, backup);
        return "Trainer removed. Backup: " + backup;
    }

    public static bool HasRuntime()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet") };
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var setup = registry.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
        if (setup?.GetValue("InstallLocation") is string root) roots.Add(root);
        return roots.Any(path => HasSix(Path.Combine(path, "shared", "Microsoft.WindowsDesktop.App")) &&
            HasSix(Path.Combine(path, "shared", "Microsoft.NETCore.App")));
    }

    private static bool HasSix(string directory) => Directory.Exists(directory) && Directory.EnumerateDirectories(directory)
        .Any(path => Version.TryParse(Path.GetFileName(path), out var version) && version.Major == 6 && version.Minor == 0 &&
            File.Exists(Path.Combine(path, Path.GetFileName(directory) + ".deps.json")));

    private static async Task InstallRuntimeAsync(string packageDirectory, IProgress<string>? progress)
    {
        var installer = SafePaths.Child(packageDirectory, "windowsdesktop-runtime-6.0.36-win-x64.exe");
        SafePaths.AssertPlain(installer);
        if (!SafePaths.Hash(installer).Equals(RuntimeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Microsoft runtime package checksum does not match.");
        progress?.Report("Installing the bundled Microsoft runtime. Windows may request administrator permission.");
        var start = new ProcessStartInfo(installer) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("/install");
        start.ArgumentList.Add("/passive");
        start.ArgumentList.Add("/norestart");
        using var process = Process.Start(start) ?? throw new IOException("Windows could not start the runtime installer.");
        await process.WaitForExitAsync();
        if (process.ExitCode == 3010) throw new InvalidOperationException("The runtime was installed. Restart Windows before launching the trainer.");
        if (process.ExitCode != 0 || !HasRuntime()) throw new InvalidOperationException("Microsoft runtime installation did not finish successfully.");
    }
}
