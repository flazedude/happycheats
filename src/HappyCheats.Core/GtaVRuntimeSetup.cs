using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Acquires the pinned author's runtime on demand. No runtime is bundled in HappyCheats.</summary>
public sealed class GtaVRuntimeSetup
{
    public const string OfficialArchiveUrl = "http://www.dev-c.com/files/ScriptHookV_3889.0_1158.13.zip";
    public const string OfficialPageUrl = "http://www.dev-c.com/gtav/scripthookv/";
    public const string ArchiveSha256 = "B64C97C3353906F14621E7E9511E4AEC2A7D436ECC21ED124D3816585E2E6188";
    public const string HookSha256 = "126BE57CA9DCA00E471F9DE611766009C900AAD88F89AE6C2926EC62E5AAEB4F";
    public const string LoaderSha256 = "9FD9E02353B7D39FE07B9667F7EA2697229A7F2D0E7D389EB79EB212B1BB181D";
    public const string RecognizedOldHookSha256 = "BC2B728F948A46EB91D1E301D8E1ED8D78CF7913637D208F7F0AB1059D858EC0";
    public const int MaximumArchiveBytes = 16 * 1024 * 1024;
    private static readonly SemaphoreSlim SetupLock = new(1, 1);
    private static readonly string[] Names = { "ScriptHookV.dll", "dinput8.dll" };
    private readonly PackageIdentity identity;
    private readonly string cacheDirectory;
    private readonly Func<CancellationToken, Task<byte[]>> download;

    public sealed record Result(IReadOnlyList<string> ChangedFiles, IReadOnlyList<string> BackupFiles, string? CachedArchive)
    {
        public bool Changed => ChangedFiles.Count != 0;
    }
    internal sealed record PackageIdentity(string Archive, string Hook, string Loader, string OldHook);
    private sealed record PlannedFile(string Name, string ExpectedHash, byte[]? Before)
    {
        public string? BeforeHash => Before == null ? null : Digest(Before);
        public bool NeedsChange => BeforeHash != ExpectedHash;
    }
    private sealed record Change(PlannedFile File, string? Backup);

    public GtaVRuntimeSetup() : this(new(ArchiveSha256, HookSha256, LoaderSha256, RecognizedOldHookSha256),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HappyCheats", "Cache", "gta-v-runtime"),
        DownloadOfficialAsync) { }

    // Hash and transport seams are internal; production callers cannot relax the pinned identity.
    internal GtaVRuntimeSetup(PackageIdentity identity, string cacheDirectory, Func<CancellationToken, Task<byte[]>> download)
    {
        this.identity = identity;
        this.cacheDirectory = Path.GetFullPath(cacheDirectory);
        this.download = download;
    }

    /// <summary>Read-only preflight: permits absent/matching required DLLs and the recognized prior hook, rejects conflicts.</summary>
    public void ValidateExisting(string directory, bool requireScriptHook = true)
    {
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) throw new IOException("The GTA V Enhanced game directory does not exist.");
        _ = Inspect(root, requireScriptHook);
    }

    /// <summary>True only when every selected dependency already matches the pinned runtime identity.</summary>
    public bool IsReady(string directory, bool requireScriptHook = true)
    {
        try
        {
            var root = Path.GetFullPath(directory);
            return Directory.Exists(root) && Inspect(root, requireScriptHook).All(file => !file.NeedsChange);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    /// <summary>
    /// The callback must check both game closure and the caller's current compatibility gate.
    /// It is repeated before every game-directory mutation. Removal of the trainer never calls this class.
    /// </summary>
    public Task<Result> EnsureAsync(string directory, Action checkGameClosedAndCompatible,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default, bool requireScriptHook = true)
        => RunCoreAsync(directory, checkGameClosedAndCompatible, null, progress, cancellationToken, requireScriptHook);

    /// <summary>The trainer operation must perform its own file rollback; runtime changes are restored if it fails.</summary>
    public Task<Result> RunWithRuntimeAsync(string directory, Action checkGameClosedAndCompatible, Action installTrainer,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default, bool requireScriptHook = true)
    {
        ArgumentNullException.ThrowIfNull(installTrainer);
        return RunCoreAsync(directory, checkGameClosedAndCompatible, installTrainer, progress, cancellationToken, requireScriptHook);
    }

    private async Task<Result> RunCoreAsync(string directory, Action checkGameClosedAndCompatible, Action? installTrainer,
        IProgress<string>? progress, CancellationToken cancellationToken, bool requireScriptHook)
    {
        ArgumentNullException.ThrowIfNull(checkGameClosedAndCompatible);
        await SetupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = Path.GetFullPath(directory);
            if (!Directory.Exists(root)) throw new IOException("The GTA V Enhanced game directory does not exist.");
            checkGameClosedAndCompatible();
            var plan = Inspect(root, requireScriptHook);
            if (plan.All(file => !file.NeedsChange))
            {
                VerifyFresh(root, plan, Array.Empty<Change>(), checkGameClosedAndCompatible);
                var unchanged = new Result(Array.Empty<string>(), Array.Empty<string>(), null);
                installTrainer?.Invoke();
                return unchanged;
            }
            progress?.Report(requireScriptHook
                ? "Obtaining the verified Script Hook V runtime from its author or the local cache."
                : "Obtaining the verified ASI loader from its author or the local cache.");
            var cache = SafePath(cacheDirectory, identity.Archive + ".zip");
            var cached = File.Exists(cache);
            var archive = cached ? await ReadFileAsync(cache, cancellationToken).ConfigureAwait(false)
                : await download(cancellationToken).ConfigureAwait(false);
            CheckBytes(archive, identity.Archive, "Script Hook V archive");
            var payloads = ReadPayloads(archive, requireScriptHook);
            if (!cached) await CacheAsync(cache, archive, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            VerifyFresh(root, plan, Array.Empty<Change>(), checkGameClosedAndCompatible);
            progress?.Report("Installing the verified runtime; existing unrecognized loaders and hooks are preserved.");
            var changes = new List<Change>();
            var backups = new List<string>();
            Result result;
            var backupFolder = "happycheats-runtime-backups/" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N");
            // Validate every permanent-backup path before the first game write.
            foreach (var file in plan.Where(file => file.NeedsChange && file.Before != null))
                _ = SafePath(root, backupFolder + "/" + file.Name + "." + file.BeforeHash + ".bak");
            try
            {
                // The hook is installed before a missing loader can activate ASI loading.
                foreach (var file in plan.Where(file => file.NeedsChange))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string? backup = null;
                    if (file.Before != null)
                    {
                        backup = SafePath(root, backupFolder + "/" + file.Name + "." + file.BeforeHash + ".bak");
                        VerifyFresh(root, plan, changes, checkGameClosedAndCompatible);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        VerifyFresh(root, plan, changes, checkGameClosedAndCompatible);
                        backup = SafePath(root, Path.GetRelativePath(root, backup));
                        using (var output = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                        {
                            output.Write(file.Before);
                            output.Flush(flushToDisk: true);
                        }
                        if (!Matches(backup, file.BeforeHash)) throw new IOException("The old Script Hook V backup could not be verified.");
                        backups.Add(backup); // Permanent recovery copies survive success and rollback.
                    }
                    WriteAtomic(root, file.Name, payloads[file.Name], file.BeforeHash,
                        () => VerifyFresh(root, plan, changes, checkGameClosedAndCompatible));
                    changes.Add(new(file, backup));
                }
                VerifyFresh(root, plan, changes, checkGameClosedAndCompatible);
                result = new(changes.Select(change => SafePath(root, change.File.Name)).ToArray(), backups.ToArray(), cache);
                installTrainer?.Invoke();
                // A successful trainer callback is the final commit. Do not run
                // another fallible callback that could roll back its prerequisites
                // after the trainer has already committed its own transaction.
            }
            catch (Exception failure)
            {
                Rollback(root, changes, checkGameClosedAndCompatible, failure);
                throw;
            }
            return result;
        }
        finally { SetupLock.Release(); }
    }

    private List<PlannedFile> Inspect(string root, bool requireScriptHook)
    {
        var plan = new List<PlannedFile>();
        foreach (var name in requireScriptHook ? Names : new[] { Names[1] })
        {
            var path = SafePath(root, name);
            if (Directory.Exists(path)) throw new IOException("A runtime file path is a directory: " + name);
            var before = File.Exists(path) ? ReadFile(path) : null;
            var expected = name == Names[0] ? identity.Hook : identity.Loader;
            var actual = before == null ? null : Digest(before);
            if (actual != null && actual != expected && !(name == Names[0] && actual == identity.OldHook))
                throw new IOException("An unrecognized existing " + name + " was preserved. Resolve the runtime conflict before installing.");
            plan.Add(new(name, expected, before));
        }
        return plan;
    }

    private Dictionary<string, byte[]> ReadPayloads(byte[] bytes, bool requireScriptHook)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in requireScriptHook ? Names : new[] { Names[1] })
        {
            var matches = archive.Entries.Where(entry => string.Equals(entry.FullName, "bin/" + name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new IOException("The official runtime archive has missing or duplicate " + name + " entries.");
            if (matches[0].Length <= 0 || matches[0].Length >= MaximumArchiveBytes) throw new IOException("Unexpected runtime file size: " + name);
            using var input = matches[0].Open();
            var data = ReadBounded(input);
            CheckBytes(data, name == Names[0] ? identity.Hook : identity.Loader, name);
            payloads.Add(name, data);
        }
        // No archive entry is extracted by name: args, xinput, NativeTrainer, and all other entries are ignored.
        return payloads;
    }

    private static void VerifyFresh(string root, IReadOnlyList<PlannedFile> plan, IReadOnlyList<Change> committed, Action check)
    {
        check();
        foreach (var file in plan)
        {
            var expected = committed.Any(change => change.File == file) ? file.ExpectedHash : file.BeforeHash;
            if (!Matches(SafePath(root, file.Name), expected))
                throw new IOException("Runtime files changed during setup; the changed file was preserved: " + file.Name);
        }
    }

    private static void WriteAtomic(string root, string relative, byte[] data, string? beforeHash, Action verify)
    {
        var temporaryRelative = ".happycheats-runtime-" + Guid.NewGuid().ToString("N") + ".tmp";
        var temporary = SafePath(root, temporaryRelative);
        try
        {
            verify();
            temporary = SafePath(root, temporaryRelative);
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(data);
                output.Flush(flushToDisk: true);
            }
            verify();
            var target = SafePath(root, relative);
            if (!Matches(target, beforeHash)) throw new IOException("Runtime target changed before commit: " + relative);
            if (!Matches(SafePath(root, temporaryRelative), Digest(data))) throw new IOException("Runtime staging file changed before commit.");
            File.Move(temporary, target, overwrite: beforeHash != null);
        }
        finally
        {
            // A stopped game remains required even for deleting an uncommitted staging file.
            try { if (File.Exists(temporary)) { verify(); File.Delete(SafePath(root, temporaryRelative)); } }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
    }

    private static void Rollback(string root, List<Change> changes, Action check, Exception failure)
    {
        var errors = new List<Exception> { failure };
        foreach (var change in changes.AsEnumerable().Reverse())
        {
            try
            {
                void Verify()
                {
                    check();
                    if (!Matches(SafePath(root, change.File.Name), change.File.ExpectedHash))
                        throw new IOException("A concurrently changed runtime file was preserved: " + change.File.Name);
                }
                Verify();
                if (change.File.Before == null) File.Delete(SafePath(root, change.File.Name));
                else WriteAtomic(root, change.File.Name, change.File.Before, change.File.ExpectedHash, Verify);
            }
            catch (Exception error)
            {
                errors.Add(new IOException("Could not safely roll back " + change.File.Name + "."
                    + (change.Backup == null ? " The newly installed runtime file remains for inspection." : " Original backup: " + change.Backup), error));
            }
        }
        if (errors.Count > 1) throw new AggregateException("Runtime setup failed and recovery needs attention. Existing backups were retained.", errors);
    }

    private async Task CacheAsync(string cache, byte[] data, CancellationToken token)
    {
        _ = SafePath(cacheDirectory, Path.GetFileName(cache));
        Directory.CreateDirectory(cacheDirectory);
        var temporary = SafePath(cacheDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, data, token).ConfigureAwait(false);
            _ = SafePath(cacheDirectory, Path.GetFileName(cache));
            if (File.Exists(cache)) { CheckBytes(await ReadFileAsync(cache, token).ConfigureAwait(false), identity.Archive, "Cached runtime archive"); return; }
            File.Move(temporary, cache);
        }
        finally { if (File.Exists(temporary)) File.Delete(SafePath(cacheDirectory, Path.GetFileName(temporary))); }
    }

    private static bool Matches(string path, string? hash) => hash == null ? !File.Exists(path) && !Directory.Exists(path)
        : File.Exists(path) && !Directory.Exists(path) && new FileInfo(path).Length < MaximumArchiveBytes
            && string.Equals(Hash(path), hash, StringComparison.OrdinalIgnoreCase);
    private static string Digest(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static void CheckBytes(byte[] data, string hash, string description)
    {
        if (data.Length == 0 || data.Length >= MaximumArchiveBytes || !string.Equals(Digest(data), hash, StringComparison.OrdinalIgnoreCase))
            throw new IOException(description + " failed the pinned size/SHA-256 verification. No runtime was installed.");
    }
    private static byte[] ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadBounded(stream);
    }
    private static byte[] ReadBounded(Stream stream)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        for (int count; (count = stream.Read(buffer)) > 0;)
        {
            if (output.Length + count >= MaximumArchiveBytes) throw new IOException("The runtime download or file exceeds its size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    private static async Task<byte[]> ReadFileAsync(string path, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, useAsync: true);
        return await ReadBoundedAsync(input, token).ConfigureAwait(false);
    }
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        for (int count; (count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0;)
        {
            if (output.Length + count >= MaximumArchiveBytes) throw new IOException("The runtime download or file exceeds its size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    private static async Task<byte[]> DownloadOfficialAsync(CancellationToken token)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
        var uri = new Uri(OfficialArchiveUrl);
        for (var redirects = 0; redirects < 4; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Referrer = new Uri(OfficialPageUrl);
            request.Headers.UserAgent.ParseAdd("HappyCheats");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location ?? throw new IOException("The author download returned an invalid redirect.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (uri.Scheme is not ("http" or "https") || uri.Host is not ("www.dev-c.com" or "dev-c.com")
                    || uri.AbsolutePath != new Uri(OfficialArchiveUrl).AbsolutePath || uri.Query.Length != 0 || uri.UserInfo.Length != 0)
                    throw new IOException("The author runtime download redirected away from its pinned archive.");
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is >= MaximumArchiveBytes) throw new IOException("The author runtime archive exceeds its size limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return await ReadBoundedAsync(stream, token).ConfigureAwait(false);
        }
        throw new IOException("The author runtime download redirected too many times.");
    }
}
