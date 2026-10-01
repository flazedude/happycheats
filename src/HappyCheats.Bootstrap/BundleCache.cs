using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace HappyCheats.Bootstrap;

/// <summary>Installs an embedded release once, preserving any later in-app update.</summary>
public static class BundleCache
{
    private const string AppDirectoryName = "HappyCheats";
    private const string ReceiptName = ".bundle-complete";
    private const int MaxEntries = 30_000;
    private const long MaxPackageBytes = 2L * 1024 * 1024 * 1024;
    private const long MaxFileBytes = 1024L * 1024 * 1024;
    private const long MaxExtractedBytes = 4L * 1024 * 1024 * 1024;
    private static readonly string[] RequiredFiles =
    {
        "HappyCheats.exe", "HappyCheats.dll", "HappyCheats.runtimeconfig.json",
        "updater/HappyCheats.Updater.exe", "updater/HappyCheats.Updater.dll",
        "updater/HappyCheats.Updater.runtimeconfig.json"
    };

    /// <summary>
    /// Returns the launch directory. The hash must be the build-time SHA-256 of the embedded ZIP.
    /// The receipt is deliberately outside the app directory which the updater replaces.
    /// </summary>
    public static string Ensure(string cacheRoot, string bundleHash, Func<Stream> openBundle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        ArgumentNullException.ThrowIfNull(openBundle);
        if (bundleHash is null || bundleHash.Length != 64 || bundleHash.Any(c =>
                !(c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')))
            throw new ArgumentException("The embedded bundle SHA-256 is invalid.", nameof(bundleHash));

        bundleHash = bundleHash.ToLowerInvariant();
        cacheRoot = Path.GetFullPath(cacheRoot);
        EnsureSafePath(cacheRoot);
        Directory.CreateDirectory(cacheRoot);
        EnsureSafePath(cacheRoot);

        var finalDirectory = Path.Combine(cacheRoot, bundleHash);
        var receipt = Receipt(bundleHash);
        // Never unlink the lock file: doing so would let another launcher lock a different inode.
        using var cacheLock = AcquireLock(Path.Combine(cacheRoot, bundleHash + ".lock"));
        EnsureSafePath(finalDirectory);
        if (PathExists(finalDirectory))
            return OpenExisting(finalDirectory, receipt);

        // A killed process can leave a staging directory, but it cannot leave a half-installed
        // final directory. A fresh GUID lets the next launch recover without deleting old files.
        var stagingDirectory = Path.Combine(cacheRoot, "." + bundleHash + ".staging-" + Guid.NewGuid().ToString("N"));
        EnsureSafePath(stagingDirectory);
        if (PathExists(stagingDirectory))
            throw new IOException("The bundle staging directory already exists.");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            EnsureSafePath(stagingDirectory);
            using (var suppliedStream = openBundle() ?? throw new InvalidDataException("The embedded bundle is missing."))
            {
                if (!suppliedStream.CanRead)
                    throw new InvalidDataException("The embedded bundle cannot be read.");
                if (suppliedStream.CanSeek)
                {
                    suppliedStream.Position = 0;
                    VerifyBundle(suppliedStream, bundleHash);
                    suppliedStream.Position = 0;
                    Extract(suppliedStream, stagingDirectory);
                }
                else
                {
                    // Embedded resources are seekable; the fallback also permits streamed callers
                    // without allocating the entire release in memory.
                    var archivePath = Path.Combine(stagingDirectory, ".bundle.zip");
                    using (var archiveStream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                    {
                        VerifyBundle(suppliedStream, bundleHash, archiveStream);
                        archiveStream.Position = 0;
                        Extract(archiveStream, stagingDirectory);
                    }
                    EnsureSafePath(archivePath);
                    File.Delete(archivePath);
                }
            }

            var appDirectory = Path.Combine(stagingDirectory, AppDirectoryName);
            ValidateInstalledLayout(appDirectory);
            EnsureSafeTree(stagingDirectory);
            var receiptPath = Path.Combine(stagingDirectory, ReceiptName);
            using (var receiptStream = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                receiptStream.Write(Encoding.UTF8.GetBytes(receipt));
                receiptStream.Flush(flushToDisk: true);
            }

            EnsureSafeTree(stagingDirectory);
            EnsureSafePath(finalDirectory);
            if (PathExists(finalDirectory))
                throw new IOException("The bundle cache changed while extracting. Existing files were preserved; please launch again.");
            Directory.Move(stagingDirectory, finalDirectory);
            return OpenExisting(finalDirectory, receipt);
        }
        catch
        {
            // Only this call's uncommitted, randomly named staging tree is eligible for cleanup.
            // Never recursively remove an unexpected link, final cache, or interrupted older stage.
            TryRemoveOwnedStage(stagingDirectory);
            throw;
        }
    }

    private static string Receipt(string hash) => "HappyCheats.BundleCache.v1\n" + hash + "\nHappyCheats\n";

    private static string OpenExisting(string directory, string receipt)
    {
        EnsureSafeTree(directory);
        var receiptPath = Path.Combine(directory, ReceiptName);
        EnsureSafePath(receiptPath);
        if (!File.Exists(receiptPath) || new FileInfo(receiptPath).Length != Encoding.UTF8.GetByteCount(receipt) ||
            File.ReadAllText(receiptPath, Encoding.UTF8) != receipt)
            throw new InvalidDataException("An unrecognized or incomplete HappyCheats cache already exists. Its files were preserved. " +
                                           "Move that cache folder aside before trying again: " + directory);
        var appDirectory = Path.Combine(directory, AppDirectoryName);
        try
        {
            ValidateInstalledLayout(appDirectory);
        }
        catch (IOException error)
        {
            throw new IOException("The cached HappyCheats installation is incomplete. An update may still be completing; " +
                                  "wait and retry. The launcher has preserved the installation instead of replacing it.", error);
        }
        return appDirectory;
    }

    private static FileStream AcquireLock(string path)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            EnsureSafePath(path);
            try
            {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    EnsureSafePath(path);
                    return stream;
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            catch (IOException error) when (timer.Elapsed < TimeSpan.FromSeconds(60) && IsSharingViolation(error))
            {
                Thread.Sleep(100);
            }
        }
    }

    private static bool IsSharingViolation(IOException error)
    {
        var code = error.HResult & 0xffff;
        return code is 32 or 33 or 11; // Windows sharing/lock violations and Unix EAGAIN.
    }

    private static void VerifyBundle(Stream source, string expectedHash, Stream? copyTo = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
        {
            total += count;
            if (total > MaxPackageBytes)
                throw new InvalidDataException("The embedded bundle exceeds the supported size.");
            hash.AppendData(buffer, 0, count);
            copyTo?.Write(buffer, 0, count);
        }
        if (total == 0 || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(expectedHash)))
            throw new InvalidDataException("The embedded bundle failed its SHA-256 integrity check. Please download HappyCheats again.");
    }

    private static void Extract(Stream source, string directory)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var entries = ValidateArchive(archive);
        var buffer = new byte[81920];
        foreach (var item in entries)
        {
            var target = Path.GetFullPath(Path.Combine(directory, item.Path.Replace('/', Path.DirectorySeparatorChar)));
            EnsureWithin(directory, target);
            EnsureSafePath(target);
            if (item.Directory)
            {
                Directory.CreateDirectory(target);
                EnsureSafePath(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            EnsureSafePath(target);
            using var input = item.Entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            long written = 0;
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                written += count;
                if (written > item.Entry.Length)
                    throw new InvalidDataException("A bundle file exceeds its declared size.");
                output.Write(buffer, 0, count);
            }
            if (written != item.Entry.Length)
                throw new InvalidDataException("A bundle file is truncated.");
        }
    }

    private static List<PackageEntry> ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaxEntries)
            throw new InvalidDataException("The bundle is empty or contains too many entries.");
        var entries = new List<PackageEntry>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.Length > 240 || name.Contains('\\') || name.StartsWith('/') || name.Contains(':'))
                throw new InvalidDataException("The bundle contains an unsafe path.");
            var isDirectory = name.EndsWith('/');
            var path = isDirectory ? name[..^1] : name;
            var parts = path.Split('/');
            if (parts.Length == 0 || parts[0] != AppDirectoryName ||
                (parts.Length == 1 && !isDirectory) || parts.Any(UnsafeComponent))
                throw new InvalidDataException("Bundle files must be inside a single HappyCheats/ folder with safe filenames.");
            if (!paths.Add(path))
                throw new InvalidDataException("The bundle contains duplicate paths.");
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if ((unixType != 0 && unixType != (isDirectory ? 0x4000 : 0x8000)) ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The bundle contains a link or unsupported file type.");
            if (entry.Length < 0 || entry.Length > MaxFileBytes || (isDirectory && entry.Length != 0))
                throw new InvalidDataException("A bundle entry has an invalid or excessive size.");
            total = checked(total + entry.Length);
            if (total > MaxExtractedBytes ||
                (entry.Length > 10 * 1024 * 1024 && entry.Length / Math.Max(1, entry.CompressedLength) > 1000))
                throw new InvalidDataException("The bundle expands beyond the supported safety limits.");
            if (!isDirectory) files.Add(path, entry.Length);
            entries.Add(new PackageEntry(entry, path, isDirectory));
        }
        foreach (var item in entries)
        {
            for (var index = item.Path.IndexOf('/'); index >= 0; index = item.Path.IndexOf('/', index + 1))
                if (files.ContainsKey(item.Path[..index]))
                    throw new InvalidDataException("A bundle directory path conflicts with a file.");
        }
        if (RequiredFiles.Any(path => !files.TryGetValue(AppDirectoryName + "/" + path, out var length) || length == 0) ||
            !files.Any(pair => pair.Value > 0 && pair.Key.StartsWith(AppDirectoryName + "/payload/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The bundle is missing the client, updater, or cheat payload files.");
        return entries;
    }

    private static bool UnsafeComponent(string component)
    {
        if (component.Length == 0 || component is "." or ".." || component.EndsWith('.') || component.EndsWith(' ') ||
            component.Any(c => c < 32 || c is '<' or '>' or '"' or '|' or '?' or '*' or ':' or '\\')) return true;
        var basename = component.Split('.')[0];
        return basename.Equals("CON", StringComparison.OrdinalIgnoreCase) || basename.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            basename.Equals("AUX", StringComparison.OrdinalIgnoreCase) || basename.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            basename.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || basename.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            (basename.Length == 4 && (basename.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || basename.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                (basename[3] is >= '1' and <= '9' or '¹' or '²' or '³'));
    }

    private static void ValidateInstalledLayout(string appDirectory)
    {
        EnsureSafePath(appDirectory);
        if (!Directory.Exists(appDirectory)) throw new IOException("The cached client directory is missing.");
        foreach (var relativePath in RequiredFiles)
        {
            var path = Path.Combine(appDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            EnsureSafePath(path);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new IOException("A required cached client or updater file is missing or empty: " + relativePath);
        }
        var payloadDirectory = Path.Combine(appDirectory, "payload");
        EnsureSafePath(payloadDirectory);
        if (!Directory.Exists(payloadDirectory) || !HasPayloadFile(payloadDirectory))
            throw new IOException("The cached cheat payload files are missing or empty.");
    }

    private static bool HasPayloadFile(string directory)
    {
        // SafeTree runs before reuse and after extraction; this traversal still checks each entry
        // before following it, so an unexpected link is never treated as a valid payload.
        var pending = new Stack<string>();
        pending.Push(directory);
        var hasFile = false;
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            EnsureSafePath(current);
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(path);
                RejectReparsePoint(path, attributes);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                else if (new FileInfo(path).Length > 0) hasFile = true;
            }
        }
        return hasFile;
    }

    private static void EnsureSafeTree(string directory)
    {
        EnsureSafePath(directory);
        if (!Directory.Exists(directory)) throw new IOException("The bundle cache is not a directory: " + directory);
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            EnsureSafePath(current);
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(path);
                RejectReparsePoint(path, attributes);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
            }
        }
    }

    private static void EnsureSafePath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                RejectReparsePoint(current, File.GetAttributes(current));
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void RejectReparsePoint(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The bundle cache contains a symbolic link or reparse point: " + path);
    }

    private static bool PathExists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void EnsureWithin(string directory, string target)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("The bundle contains a path outside its extraction directory.");
    }

    private static void TryRemoveOwnedStage(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            EnsureSafeTree(directory);
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record PackageEntry(ZipArchiveEntry Entry, string Path, bool Directory);
}
