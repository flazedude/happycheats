using System.Diagnostics;
using System.Text.Json;

namespace HappyCheats.Core.Updates;

/// <summary>Best-effort retention for completed updates of this installation only.</summary>
public static class UpdateCacheCleanup
{
    private static readonly string[] ClientFiles =
    [
        "HappyCheats.exe", "HappyCheats.dll", "HappyCheats.runtimeconfig.json", "update-settings.json",
        "updater/HappyCheats.Updater.exe", "updater/HappyCheats.Updater.dll", "updater/HappyCheats.Updater.runtimeconfig.json"
    ];
    private static readonly HashSet<string> TransactionEntries = new(StringComparer.Ordinal)
    {
        "previous", "runner", "apply.json", "archive.zip", "helper-ready.txt", "launch-ready.txt", "transaction.json"
    };

    // The updated app acknowledges startup before its updater writes "complete" and exits.
    // Delay and retry busy sessions, without holding up the UI or surfacing maintenance errors.
    public static Task RunAfterStartupAsync(string appDirectory, string currentVersion, CancellationToken token = default)
        => RunAfterStartupAsync(appDirectory, currentVersion, OtherSessionIsRunning,
            delay => Task.Delay(delay, token), token);

    internal static async Task RunAfterStartupAsync(string appDirectory, string currentVersion,
        Func<bool> isBusy, Func<TimeSpan, Task> delay, CancellationToken token = default)
    {
        try
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                await delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var result = Prune(appDirectory, currentVersion, isBusy, token);
                if (!result.RetryNeeded) return;
            }
        }
        catch (Exception) { /* Cache maintenance must never prevent startup or shutdown. */ }
    }

    internal readonly record struct Result(int RemovedTransactions, bool RetryNeeded);

    internal static Result Prune(string appDirectory, string currentVersion, Func<bool> isBusy,
        CancellationToken token = default)
    {
        var removed = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            if (isBusy()) return new(0, true);
            var version = ReleaseVersion.Parse(currentVersion);
            var app = Full(appDirectory);
            RequireClient(app);
            var parent = Directory.GetParent(app)?.FullName ?? throw new IOException("Missing application parent.");
            var history = Path.Combine(parent, ".happycheats-updates");
            if (!Directory.Exists(history)) return default;
            RejectLinkedPath(history);
            var lockPath = Path.Combine(history, ".cleanup.lock");
            RejectLinkedPath(lockPath);
            using var cleanupLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            RejectLinkedPath(lockPath);
            var complete = new List<Transaction>();
            foreach (var directory in Directory.EnumerateDirectories(history))
            {
                token.ThrowIfCancellationRequested();
                var transaction = Inspect(directory, app, version);
                if (transaction != null) complete.Add(transaction);
            }
            if (complete.Count < 2) return default;
            // Releases advance monotonically; a clock correction must not evict the later
            // release's rollback. Preserve all equal version/time ties rather than guessing.
            var newest = complete.OrderByDescending(transaction => transaction.LaunchedVersion)
                .ThenByDescending(transaction => transaction.CompletedAt).First();
            foreach (var transaction in complete.Where(transaction =>
                         transaction.LaunchedVersion.CompareTo(newest.LaunchedVersion) < 0 ||
                         transaction.LaunchedVersion == newest.LaunchedVersion && transaction.CompletedAt < newest.CompletedAt))
            {
                token.ThrowIfCancellationRequested();
                if (isBusy()) return new(removed, true);
                if (Remove(transaction, app, isBusy, token)) removed++;
                else if (isBusy()) return new(removed, true);
            }
            return new(removed, false);
        }
        catch (OperationCanceledException) { return new(removed, false); }
        catch (Exception) { return new(removed, false); }
    }

    private static Transaction? Inspect(string directory, string app, ReleaseVersion current)
    {
        try
        {
            var work = Full(directory);
            if (!Guid.TryParseExact(Path.GetFileName(work), "N", out _) ||
                !Same(Path.GetDirectoryName(work), Path.Combine(Directory.GetParent(app)!.FullName, ".happycheats-updates"))) return null;
            var files = SafeFiles(work);
            var entries = Directory.EnumerateFileSystemEntries(work).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
            if (!entries.SetEquals(TransactionEntries)) return null;
            var journalBytes = ReadSmall(Path.Combine(work, "transaction.json"));
            ReleaseSigning.RejectDuplicateProperties(journalBytes);
            using var journal = JsonDocument.Parse(journalBytes);
            var root = journal.RootElement;
            if (root.EnumerateObject().Count() != 5 || root.GetProperty("state").GetString() != "complete" ||
                !Same(root.GetProperty("TargetDirectory").GetString(), app) ||
                !Same(root.GetProperty("PackageDirectory").GetString(), Path.Combine(work, "HappyCheats")) ||
                !Same(root.GetProperty("backup").GetString(), Path.Combine(work, "previous")) ||
                !root.GetProperty("updatedAt").TryGetDateTimeOffset(out var completedAt) ||
                completedAt > DateTimeOffset.UtcNow) return null;
            var applyBytes = ReadSmall(Path.Combine(work, "apply.json"));
            ReleaseSigning.RejectDuplicateProperties(applyBytes);
            using var apply = JsonDocument.Parse(applyBytes);
            var request = apply.RootElement;
            if (request.EnumerateObject().Count() != 5 ||
                !Same(request.GetProperty("TargetDirectory").GetString(), app) ||
                !Same(request.GetProperty("PackageDirectory").GetString(), Path.Combine(work, "HappyCheats")) ||
                !Same(request.GetProperty("WorkDirectory").GetString(), work) ||
                request.GetProperty("ParentProcessId").GetInt32() <= 0 ||
                request.GetProperty("ParentStartTimeUtcTicks").GetInt64() <= 0) return null;
            var launchBytes = ReadSmall(Path.Combine(work, "launch-ready.txt"));
            var launched = ReleaseVersion.Parse(System.Text.Encoding.UTF8.GetString(launchBytes).Trim());
            if (launched.CompareTo(current) > 0 ||
                System.Text.Encoding.UTF8.GetString(ReadSmall(Path.Combine(work, "helper-ready.txt"))) != "ready") return null;
            var previous = Path.Combine(work, "previous");
            RequireClient(previous);
            // Custom/changed trust configuration may be valuable recovery data. Keep it.
            var configuration = ReadSmall(Path.Combine(previous, "update-settings.json"));
            if (!configuration.AsSpan().SequenceEqual(ReadSmall(Path.Combine(app, "update-settings.json")))) return null;
            foreach (var file in new[] { "HappyCheats.Updater.exe", "HappyCheats.Updater.dll", "HappyCheats.Updater.runtimeconfig.json" })
                RequireFile(Path.Combine(work, "runner", file));
            RequireFile(Path.Combine(work, "archive.zip"));
            return new(work, completedAt, launched, files, journalBytes, applyBytes, launchBytes, configuration);
        }
        catch (Exception) { return null; }
    }

    private static bool Remove(Transaction transaction, string app, Func<bool> isBusy,
        CancellationToken token)
    {
        var handles = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // FileShare.Delete lets this call delete after preflight, but denies new readers,
            // writers and launches. Any existing incompatible handle skips the entire tree.
            foreach (var file in transaction.Files)
            {
                token.ThrowIfCancellationRequested();
                RejectLinkedPath(file);
                handles.Add(file, new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Delete));
            }
            if (!SafeFiles(transaction.Directory).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(handles.Keys) ||
                !Matches("transaction.json", transaction.Journal) || !Matches("apply.json", transaction.Apply) ||
                !Matches("launch-ready.txt", transaction.Launch) || !Matches("previous/update-settings.json", transaction.Configuration) ||
                !transaction.Configuration.AsSpan().SequenceEqual(ReadSmall(Path.Combine(app, "update-settings.json")))) return false;
            token.ThrowIfCancellationRequested();
            if (isBusy()) return false;
            // This is an older completed backup; the newest complete rollback is never a target.
            // If the OS interrupts deletion, leave the remainder for manual inspection.
            Directory.Delete(transaction.Directory, recursive: true);
            return true;
        }
        catch (Exception) { return false; }
        finally { foreach (var handle in handles.Values) handle.Dispose(); }

        bool Matches(string relative, byte[] expected)
        {
            var stream = handles[Path.Combine(transaction.Directory, relative.Replace('/', Path.DirectorySeparatorChar))];
            if (stream.Length != expected.Length) return false;
            var actual = new byte[expected.Length];
            stream.ReadExactly(actual);
            return actual.AsSpan().SequenceEqual(expected);
        }
    }

    private static bool OtherSessionIsRunning()
    {
        try
        {
            foreach (var name in new[] { "HappyCheats", "HappyCheats.Bootstrap", "HappyCheats.Updater" })
                foreach (var process in Process.GetProcessesByName(name))
                    using (process)
                        if (process.Id != Environment.ProcessId && !process.HasExited) return true;
            return false;
        }
        catch (Exception) { return true; } // An unreadable process list is not permission to delete.
    }

    private static void RequireClient(string app)
    {
        RejectLinkedPath(app);
        foreach (var relative in ClientFiles) RequireFile(Path.Combine(app, relative.Replace('/', Path.DirectorySeparatorChar)));
        var payload = Path.Combine(app, "payload");
        if (SafeFiles(payload).Count == 0) throw new IOException("Missing client payload.");
    }

    private static void RequireFile(string path)
    {
        RejectLinkedPath(path);
        if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new IOException("Missing installed file.");
    }

    private static byte[] ReadSmall(string path)
    {
        RequireFile(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 16 * 1024) throw new IOException("Unexpected maintenance metadata size.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static List<string> SafeFiles(string root)
    {
        RejectLinkedPath(root);
        var prefix = Full(root) + Path.DirectorySeparatorChar;
        var pending = new Stack<string>();
        var files = new List<string>();
        pending.Push(root);
        while (pending.Count > 0)
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var full = Full(entry);
                if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected maintenance path.");
                var attributes = File.GetAttributes(full);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked cache entry.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(full);
                else files.Add(full);
            }
        return files;
    }

    private static void RejectLinkedPath(string path)
    {
        for (var cursor = Full(path); cursor != null; cursor = Path.GetDirectoryName(cursor))
        {
            try
            {
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked maintenance path.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static bool Same(string? left, string right)
        => left != null && Path.IsPathFullyQualified(left) && string.Equals(Full(left), Full(right), StringComparison.OrdinalIgnoreCase);
    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private sealed record Transaction(string Directory, DateTimeOffset CompletedAt, ReleaseVersion LaunchedVersion, List<string> Files,
        byte[] Journal, byte[] Apply, byte[] Launch, byte[] Configuration);
}
