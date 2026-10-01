using System.Security.Cryptography;

namespace LaundryTrainer.Setup.Core;

internal static class SafePaths
{
    internal static string Absolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new IOException("请选择完整的绝对目录路径。");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    internal static string Child(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new IOException($"不安全的相对路径：{relative}");
        var segments = relative.Replace('\\', '/').Split('/');
        if (segments.Any(p => p is ".." or "." || p.Length == 0 || p.EndsWith(' ') || p.EndsWith('.')))
            throw new IOException($"不安全的相对路径：{relative}");
        var normalizedRoot = Absolute(root);
        var result = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
        if (!result.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"路径超出所选目录：{relative}");
        return result;
    }

    internal static void AssertPlain(string path)
    {
        var cursor = Absolute(path);
        while (!string.IsNullOrEmpty(cursor))
        {
            try
            {
                var attributes = File.GetAttributes(cursor);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"不支持符号链接和目录联接：{cursor}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            cursor = Path.GetDirectoryName(cursor);
        }
    }

    internal static string Hash(string path)
    {
        AssertPlain(path);
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    internal static bool SameFile(string first, string second) =>
        new FileInfo(first).Length == new FileInfo(second).Length && Hash(first) == Hash(second);

    internal static IReadOnlyList<string> EnumeratePlainFiles(string directory)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            AssertPlain(current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                AssertPlain(entry);
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                    pending.Push(entry);
                else
                    files.Add(entry);
            }
        }
        return files;
    }
}
