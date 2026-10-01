using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Runs the unmodified, verified SMAPI installer; SMAPI remains independently owned.</summary>
public static class SmapiInstaller
{
    public const string BundleFile = "smapi/SMAPI-4.5.2-installer.zip";
    public const string BundleSha256 = "dd01ddca7b566bfe0d3b3d2d03833496abc56c53da976241f2ab443f5484acc4";
    public static readonly Version BundledVersion = new(4, 5, 2, 0);
    public const string MissingBundle = "The bundled SMAPI installer is missing. Reinstall the current HappyCheats release, then choose Install / update.";
    public const string InvalidInstallation = "The existing SMAPI installation is incomplete or unrecognized. Repair SMAPI using its official installer from smapi.io, then scan again. Existing files were preserved.";
    private const string ArchivePrefix = "SMAPI 4.5.2 installer/internal/windows/";
    private static readonly string[] RequiredFiles = { "StardewModdingAPI.exe", "StardewModdingAPI.dll", "StardewModdingAPI.runtimeconfig.json", "StardewModdingAPI.deps.json",
        "smapi-internal/SMAPI.Toolkit.dll", "smapi-internal/SMAPI.Toolkit.CoreInterfaces.dll", "smapi-internal/0Harmony.dll",
        "smapi-internal/Newtonsoft.Json.dll", "smapi-internal/Mono.Cecil.dll", "smapi-internal/Mono.Cecil.Mdb.dll", "smapi-internal/Mono.Cecil.Pdb.dll",
        "smapi-internal/MonoMod.Common.dll", "smapi-internal/Markdig.dll", "smapi-internal/Pathoschild.Http.Client.dll", "smapi-internal/Pintail.dll",
        "smapi-internal/System.Net.Http.Formatting.dll", "smapi-internal/TMXTile.dll", "smapi-internal/VdfConverter.dll", "smapi-internal/config.json" };

    public enum State { Missing, Outdated, Ready, Invalid }
    public sealed record Installation(State Status, Version? Version);

    public static Installation Inspect(string directory)
    {
        var paths = RequiredFiles.Select(file => SafePath(directory, file)).ToArray();
        if (!paths.Any(path => File.Exists(path) || Directory.Exists(path)) && !Directory.Exists(SafePath(directory, "smapi-internal")))
            return new(State.Missing, null);
        if (paths.Any(path => !File.Exists(path) || new FileInfo(path).Length == 0)) return new(State.Invalid, null);
        try {
            var assembly = AssemblyName.GetAssemblyName(paths[1]);
            if (assembly.Name != "StardewModdingAPI" || assembly.Version is not { Major: > 0 } version) return new(State.Invalid, null);
            return new(version >= BundledVersion ? State.Ready : State.Outdated, version);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException) {
            return new(State.Invalid, null);
        }
    }

    public static bool BundleAvailable(string package) => File.Exists(SafePath(package, BundleFile));

    public static void EnsureInstalled(string package, string directory, Action checkGameClosed, IProgress<string>? progress = null)
    {
        checkGameClosed();
        var installed = Inspect(directory);
        if (installed.Status == State.Ready) { progress?.Report("Compatible SMAPI detected. Keeping the existing installation."); return; }
        if (installed.Status == State.Invalid) throw new IOException(InvalidInstallation);
        var archivePath = SafePath(package, BundleFile);
        if (!File.Exists(archivePath)) throw new IOException(MissingBundle);
        ValidateGameTree(directory);
        progress?.Report("Verifying bundled SMAPI 4.5.2...");
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var extraction = SafePath(temporaryRoot, "HappyCheats-SMAPI-" + Guid.NewGuid().ToString("N"));
        try {
            // Keep the same read-only handle for hashing and extraction so the verified bytes cannot be replaced.
            using (var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                var hash = Convert.ToHexString(SHA256.HashData(stream));
                if (!hash.Equals(BundleSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Bundled SMAPI checksum failed. Reinstall the current HappyCheats release.");
                stream.Position = 0;
                ExtractWindowsInstaller(stream, extraction);
            }
            ExecuteAndValidate(extraction, directory, checkGameClosed, RunInstaller, progress);
        } finally {
            try { DeleteExtraction(temporaryRoot, extraction); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { progress?.Report("SMAPI setup temporary files could not be removed. They can be deleted after restarting HappyCheats."); }
        }
    }

    internal static void ExtractWindowsInstaller(Stream stream, string destination)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(ZipArchiveEntry Entry, string Path)>();
        long size = 0;
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(ArchivePrefix, StringComparison.Ordinal))) {
            var relative = entry.FullName[ArchivePrefix.Length..];
            if (relative.Length == 0 || relative.EndsWith('/')) continue;
            if (relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(part => part is "" or "." or "..")
                || !seen.Add(relative) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new IOException("Invalid path in the bundled SMAPI installer.");
            size = checked(size + entry.Length);
            if (entries.Count >= 512 || size > 256L * 1024 * 1024) throw new IOException("The bundled SMAPI installer exceeds its extraction limit.");
            entries.Add((entry, SafePath(destination, relative)));
        }
        foreach (var required in new[] { "SMAPI.Installer.exe", "SMAPI.Installer.dll", "SMAPI.Installer.runtimeconfig.json", "install.dat", "hostfxr.dll" })
            if (!seen.Contains(required)) throw new IOException("The bundled SMAPI installer is incomplete.");
        foreach (var (entry, path) in entries) {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    internal static ProcessStartInfo InstallerStartInfo(string installerDirectory, string gameDirectory)
    {
        // SMAPI calls Console.Clear and needs a real console even in --no-prompt mode.
        // ShellExecute creates that console and SW_HIDE keeps setup out of the user's way.
        var info = new ProcessStartInfo(SafePath(installerDirectory, "SMAPI.Installer.exe")) {
            WorkingDirectory = Path.GetFullPath(installerDirectory), UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add("--install");
        info.ArgumentList.Add("--no-prompt");
        info.ArgumentList.Add("--game-path");
        info.ArgumentList.Add(Path.GetFullPath(gameDirectory));
        return info;
    }

    internal static void ExecuteAndValidate(string installerDirectory, string gameDirectory, Action checkGameClosed,
        Func<ProcessStartInfo, int> runInstaller, IProgress<string>? progress = null)
    {
        checkGameClosed();
        var before = Inspect(gameDirectory);
        if (before.Status == State.Ready) return; // Another setup may have completed while the archive was extracted.
        if (before.Status == State.Invalid) throw new IOException(InvalidInstallation);
        ValidateGameTree(gameDirectory);
        progress?.Report("Installing bundled SMAPI 4.5.2. This may take a moment...");
        var exitCode = runInstaller(InstallerStartInfo(installerDirectory, gameDirectory));
        // The official installer can return zero after a handled error, so its exit code alone isn't proof of success.
        var installed = Inspect(gameDirectory);
        if (exitCode != 0 || installed.Status != State.Ready || installed.Version != BundledVersion)
            throw new IOException("SMAPI setup did not complete successfully. Check the game folder permissions and repair SMAPI with the official installer from smapi.io if needed, then choose Install / update again.");
        checkGameClosed();
        progress?.Report("SMAPI 4.5.2 installed. Installing the HappyCheats mod...");
    }

    private static int RunInstaller(ProcessStartInfo info)
    {
        using var process = Process.Start(info) ?? throw new IOException("The bundled SMAPI installer could not be started.");
        if (!process.WaitForExit(120_000)) {
            try { process.Kill(entireProcessTree: true); process.WaitForExit(5_000); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            throw new IOException("SMAPI setup timed out. Check the game folder permissions and repair SMAPI with the official installer from smapi.io if needed.");
        }
        return process.ExitCode;
    }

    private static void ValidateGameTree(string root)
    {
        _ = SafePath(root, "Stardew Valley.exe"); // Validate the root and all ancestor directories too.
        var pending = new Stack<string>(); pending.Push(Path.GetFullPath(root));
        while (pending.TryPop(out var current)) {
            foreach (var path in Directory.EnumerateFileSystemEntries(current)) {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked game paths are unsupported during SMAPI setup: " + path);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
            }
        }
    }

    private static void DeleteExtraction(string temporaryRoot, string extraction)
    {
        var path = Path.GetFullPath(extraction);
        var parent = Path.GetFullPath(temporaryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("HappyCheats-SMAPI-", StringComparison.Ordinal))
            throw new IOException("Invalid SMAPI setup cleanup path.");
        if (!Directory.Exists(path)) return;
        ValidateGameTree(path);
        Directory.Delete(path, recursive: true);
    }
}
