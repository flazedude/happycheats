using System.IO.Compression;

namespace HappyCheats.Core.Updates;

public static class UpdatePackage
{
    private const int MaxEntries = 30_000;
    private const long MaxFileBytes = 1024L * 1024 * 1024;
    private static readonly string[] RequiredFiles =
    {
        "HappyCheats/HappyCheats.exe", "HappyCheats/HappyCheats.dll", "HappyCheats/HappyCheats.runtimeconfig.json",
        "HappyCheats/updater/HappyCheats.Updater.exe", "HappyCheats/updater/HappyCheats.Updater.dll",
        "HappyCheats/updater/HappyCheats.Updater.runtimeconfig.json"
    };

    public static void ValidateArchive(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        _ = Validate(archive);
    }

    internal static async Task ExtractAsync(string archivePath, string workDirectory, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = Validate(archive);
        var buffer = new byte[81920];
        foreach (var item in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(workDirectory, item.Path.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(workDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update archive contains a path outside its package.");
            if (item.Directory) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = item.Entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            long written = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                written += count;
                if (written > item.Entry.Length) throw new InvalidDataException("An update file exceeds its declared size.");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
            if (written != item.Entry.Length) throw new InvalidDataException("An update file is truncated.");
        }
    }

    private static List<PackageEntry> Validate(ZipArchive archive)
    {
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaxEntries)
            throw new InvalidDataException("The update archive is empty or contains too many entries.");
        var entries = new List<PackageEntry>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.Length > 240 || name.Contains('\\') || name.StartsWith('/') || name.Contains(':'))
                throw new InvalidDataException("The update archive contains an unsafe path.");
            var directory = name.EndsWith('/');
            var path = directory ? name[..^1] : name;
            var parts = path.Split('/');
            if (parts.Length == 0 || !string.Equals(parts[0], "HappyCheats", StringComparison.Ordinal) ||
                (parts.Length == 1 && !directory) || parts.Any(UnsafeComponent))
                throw new InvalidDataException("Update files must be inside a single HappyCheats/ folder with safe filenames.");
            if (!paths.Add(path)) throw new InvalidDataException("The update archive contains duplicate paths.");
            // Reject Unix links/devices and DOS reparse points; never materialize archive-supplied links.
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if ((unixType != 0 && unixType != (directory ? 0x4000 : 0x8000)) ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The update archive contains a link or unsupported file type.");
            if (entry.Length < 0 || entry.Length > MaxFileBytes || (directory && entry.Length != 0))
                throw new InvalidDataException("An update archive entry has an invalid or excessive size.");
            total = checked(total + entry.Length);
            if (total > UpdateValidation.MaxExtractedBytes ||
                (entry.Length > 10 * 1024 * 1024 && entry.Length / Math.Max(1, entry.CompressedLength) > 1000))
                throw new InvalidDataException("The update archive expands beyond the supported safety limits.");
            if (!directory) files.Add(path);
            entries.Add(new(entry, path, directory));
        }
        foreach (var item in entries)
        {
            var path = item.Path;
            for (var index = path.IndexOf('/'); index >= 0; index = path.IndexOf('/', index + 1))
                if (files.Contains(path[..index])) throw new InvalidDataException("An update archive path conflicts with a file.");
        }
        if (RequiredFiles.Any(p => !files.Contains(p)) ||
            !files.Any(p => p.StartsWith("HappyCheats/payload/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The update archive is missing the client, updater, or cheat payload files.");
        if (entries.Any(e => !e.Directory && RequiredFiles.Contains(e.Path, StringComparer.OrdinalIgnoreCase) && e.Entry.Length == 0))
            throw new InvalidDataException("A required update file is empty.");
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

    private sealed record PackageEntry(ZipArchiveEntry Entry, string Path, bool Directory);
}
