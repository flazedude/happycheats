using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace HappyCheats.Core.Updates;

public sealed class UpdateClient
{
    private const int MaxFeedBytes = 256 * 1024;
    private readonly HttpClient http;
    private readonly UpdateConfiguration configuration;
    private readonly ConcurrentDictionary<string, string> verifiedReleases = new(StringComparer.Ordinal);
    private readonly TimeSpan primaryTimeout;
    private readonly TimeSpan internetTimeout;

    public UpdateClient(HttpClient http, UpdateConfiguration config)
        : this(http, config, TimeSpan.FromSeconds(4)) { }

    internal UpdateClient(HttpClient http, UpdateConfiguration config, TimeSpan primaryTimeout, TimeSpan? internetTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(config);
        var snapshot = config with { FeedUrls = config.FeedUrls?.ToArray() };
        snapshot.Validate();
        this.http = http;
        configuration = snapshot;
        if (primaryTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(primaryTimeout));
        this.primaryTimeout = primaryTimeout;
        this.internetTimeout = internetTimeout ?? TimeSpan.FromSeconds(60);
        if (this.internetTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(internetTimeout));
    }

    public async Task<ReleaseManifest?> CheckAsync(string currentVersion, CancellationToken token = default)
    {
        var current = ReleaseVersion.Parse(currentVersion);
        if (configuration.FeedUrls != null)
            return await CheckOrderedSourcesAsync(current, token).ConfigureAwait(false);
        var source = configuration.FeedUrl;
        ReleaseManifest release;
        try
        {
            release = await ReadFeedAsync(source, HasFallback ? primaryTimeout : internetTimeout, token).ConfigureAwait(false);
        }
        catch (UpdateTransportException) when (HasFallback && !token.IsCancellationRequested)
        {
            source = configuration.FallbackFeedUrl!;
            release = await ReadFeedAsync(source, internetTimeout, token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        if (ReleaseVersion.Parse(release.Version).CompareTo(current) <= 0) return null;
        verifiedReleases.TryAdd(Fingerprint(release), source);
        return release;
    }

    private bool HasFallback => configuration.FallbackFeedUrl != null
        && !SameUrl(configuration.FeedUrl, configuration.FallbackFeedUrl);

    // UI cancellation must allow the complete bounded feed chain to finish.
    public TimeSpan CheckTimeout => configuration.FeedUrls != null
        ? TimeSpan.FromTicks(configuration.FeedUrls.Sum(url => SourceTimeout(url).Ticks)) + TimeSpan.FromSeconds(5)
        : (HasFallback ? primaryTimeout + internetTimeout : internetTimeout) + TimeSpan.FromSeconds(5);

    private TimeSpan SourceTimeout(string url) => IsLocalUrl(url) ? primaryTimeout : internetTimeout;

    private static bool IsLocalUrl(string url)
    {
        var address = new Uri(url);
        return address.IsLoopback || (IPAddress.TryParse(address.Host, out var ip) && UpdateAddress.IsLocalAddress(ip));
    }

    private static bool SamePackage(ReleaseManifest left, ReleaseManifest right)
        => left.Version == right.Version && left.Channel == right.Channel && left.PackageSize == right.PackageSize
            && string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase);

    private async Task<ReleaseManifest?> CheckOrderedSourcesAsync(ReleaseVersion current, CancellationToken token)
    {
        ReleaseManifest? newest = null;
        string? selectedSource = null;
        UpdateTransportException? lastTransportError = null;
        var seenVersions = new Dictionary<string, ReleaseManifest>(StringComparer.Ordinal);
        foreach (var source in configuration.FeedUrls!)
        {
            ReleaseManifest candidate;
            try { candidate = await ReadFeedAsync(source, SourceTimeout(source), token).ConfigureAwait(false); }
            catch (UpdateTransportException error) when (!token.IsCancellationRequested)
            { lastTransportError = error; continue; }
            if (seenVersions.TryGetValue(candidate.Version, out var previous) && !SamePackage(previous, candidate))
                throw new InvalidDataException("Update sources disagree about the signed package for the same release version.");
            seenVersions.TryAdd(candidate.Version, candidate);
            var comparison = newest == null ? 1 : ReleaseVersion.Parse(candidate.Version).CompareTo(ReleaseVersion.Parse(newest.Version));
            // Equal packages keep their configured order, preferring an actual
            // LAN package over an earlier bridge that points directly to GitHub.
            if (comparison > 0 || (comparison == 0 && IsLocalUrl(candidate.PackageUrl) && !IsLocalUrl(newest!.PackageUrl)))
            { newest = candidate; selectedSource = source; }
        }
        token.ThrowIfCancellationRequested();
        if (newest == null) throw lastTransportError ?? new IOException("No configured update source could be reached.");
        if (ReleaseVersion.Parse(newest.Version).CompareTo(current) <= 0) return null;
        verifiedReleases.TryAdd(Fingerprint(newest), selectedSource!);
        return newest;
    }

    private async Task<ReleaseManifest> ReadFeedAsync(string url, TimeSpan timeout, CancellationToken token)
    {
        var bytes = await NetworkAsync(async networkToken =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UpdateValidation.Url(url, "Update feed"));
            request.Headers.CacheControl = new() { NoCache = true };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, networkToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            ValidateResponseUrl(response, request.RequestUri!);
            if (response.Content.Headers.ContentLength > MaxFeedBytes)
                throw new InvalidDataException("The update feed is too large.");
            await using var input = await response.Content.ReadAsStreamAsync(networkToken).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, networkToken).ConfigureAwait(false)) != 0)
            {
                if (output.Length + count > MaxFeedBytes) throw new InvalidDataException("The update feed is too large.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }, timeout, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        ReleaseSigning.RejectDuplicateProperties(bytes);
        var envelope = JsonSerializer.Deserialize<SignedReleaseEnvelope>(bytes, ReleaseSigning.JsonOptions)
            ?? throw new InvalidDataException("The update feed is empty.");
        var release = ReleaseSigning.Verify(envelope, configuration.PublicKeyPem);
        if (!string.Equals(release.Channel, configuration.Channel, StringComparison.Ordinal))
            throw new InvalidDataException("The signed release does not match this client's update channel.");
        return release;
    }

    public async Task<StagedUpdate> DownloadAndStageAsync(ReleaseManifest release, string stagingRoot,
        IProgress<UpdateProgress>? progress = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        UpdateValidation.Manifest(release);
        if (!verifiedReleases.TryGetValue(Fingerprint(release), out var source))
            throw new InvalidDataException("Check and verify this release before downloading it.");
        token.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(stagingRoot);
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The update staging folder must not be a link.");
        var work = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var archivePath = Path.Combine(work, "archive.zip");
            if (configuration.FeedUrls != null)
                await DownloadFromOrderedSourcesAsync(release, source, archivePath, progress, token).ConfigureAwait(false);
            else
            {
                var canFallback = HasFallback && !SameUrl(source, configuration.FallbackFeedUrl!);
                try
                {
                    await DownloadAsync(release, archivePath, canFallback ? primaryTimeout : internetTimeout, progress, token).ConfigureAwait(false);
                }
                catch (UpdateTransportException) when (canFallback && !token.IsCancellationRequested)
                {
                    // Never invent a mirror URL. Independently verify its signed feed
                    // and require the exact package already accepted from the primary.
                    File.Delete(archivePath);
                    var mirror = await ReadFeedAsync(configuration.FallbackFeedUrl!, internetTimeout, token).ConfigureAwait(false);
                    if (mirror.Version != release.Version || mirror.PackageSize != release.PackageSize
                        || !string.Equals(mirror.Sha256, release.Sha256, StringComparison.OrdinalIgnoreCase)
                        || SameUrl(mirror.PackageUrl, release.PackageUrl))
                        throw new InvalidDataException("The fallback update source does not have this exact release. Check for updates again.");
                    await DownloadAsync(mirror, archivePath, internetTimeout, progress, token).ConfigureAwait(false);
                }
            }
            await UpdatePackage.ExtractAsync(archivePath, work, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new(Path.Combine(work, "HappyCheats"), work, release);
        }
        catch
        {
            // This is the unique directory created above, never a supplied application or game directory.
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private async Task DownloadFromOrderedSourcesAsync(ReleaseManifest release, string source, string archivePath,
        IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        var sources = configuration.FeedUrls!;
        var sourceIndex = Array.FindIndex(sources, candidate => SameUrl(candidate, source));
        if (sourceIndex < 0) throw new InvalidDataException("The verified release source is no longer configured.");
        var attemptedPackages = new HashSet<Uri>();
        UpdateTransportException? lastTransportError = null;
        for (var index = sourceIndex; index < sources.Length; index++)
        {
            var candidate = release;
            if (index != sourceIndex)
            {
                // Reverify the live signed feed after a failed package transfer.
                // A stale or advanced host is not a mirror for the accepted release.
                try { candidate = await ReadFeedAsync(sources[index], SourceTimeout(sources[index]), token).ConfigureAwait(false); }
                catch (UpdateTransportException error) when (!token.IsCancellationRequested)
                { lastTransportError = error; continue; }
                if (candidate.Version != release.Version) continue;
                if (!SamePackage(candidate, release))
                    throw new InvalidDataException("A subsequent update source changed the signed package for this release version.");
            }
            if (!attemptedPackages.Add(new Uri(candidate.PackageUrl))) continue;
            try
            {
                await DownloadAsync(candidate, archivePath, SourceTimeout(candidate.PackageUrl), progress, token).ConfigureAwait(false);
                return;
            }
            catch (UpdateTransportException error) when (!token.IsCancellationRequested)
            { lastTransportError = error; File.Delete(archivePath); }
        }
        token.ThrowIfCancellationRequested();
        throw new IOException("No subsequent update source could download the exact verified release. Check for updates again.", lastTransportError);
    }

    private async Task DownloadAsync(ReleaseManifest release, string archivePath, TimeSpan inactivityTimeout,
        IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateValidation.Url(release.PackageUrl, "Update package URL"));
        using var response = await NetworkAsync(async networkToken =>
        {
            var result = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, networkToken).ConfigureAwait(false);
            try { result.EnsureSuccessStatusCode(); return result; }
            catch { result.Dispose(); throw; }
        }, inactivityTimeout, token).ConfigureAwait(false);
        ValidateResponseUrl(response, request.RequestUri!);
        if (response.Content.Headers.ContentLength is long length && length != release.PackageSize)
            throw new InvalidDataException("The update download size does not match the signed release.");
        await using var input = await NetworkAsync(networkToken => response.Content.ReadAsStreamAsync(networkToken), inactivityTimeout, token).ConfigureAwait(false);
        await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long received = 0;
        progress?.Report(new(0, release.PackageSize));
        int count;
        // Each read gets a fresh timer. A large, continuously progressing LAN
        // download is not constrained to a four-second total duration. Internet
        // sources also have an inactivity limit because ResponseHeadersRead does
        // not extend HttpClient.Timeout to subsequent body reads.
        while ((count = await NetworkAsync(networkToken => input.ReadAsync(buffer.AsMemory(), networkToken).AsTask(), inactivityTimeout, token).ConfigureAwait(false)) != 0)
        {
            received += count;
            if (received > release.PackageSize) throw new InvalidDataException("The update download exceeds its signed size.");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            progress?.Report(new(received, release.PackageSize));
        }
        token.ThrowIfCancellationRequested();
        if (received != release.PackageSize) throw new InvalidDataException("The update download is incomplete.");
        if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
            throw new InvalidDataException("The update download failed SHA-256 verification.");
    }

    private static async Task<T> NetworkAsync<T>(Func<CancellationToken, Task<T>> operation, TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try { return await operation(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new UpdateTransportException("The update source timed out.", error); }
        catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or IOException)
        { throw new UpdateTransportException("The update source could not be reached. " + error.Message, error); }
    }

    private sealed class UpdateTransportException(string message, Exception inner) : IOException(message, inner);
    private static bool SameUrl(string left, string right) => new Uri(left).Equals(new Uri(right));

    private static string Fingerprint(ReleaseManifest release) => Convert.ToHexString(SHA256.HashData(ReleaseSigning.SerializeManifest(release)));

    private static void ValidateResponseUrl(HttpResponseMessage response, Uri requested)
        => UpdateValidation.Url((response.RequestMessage?.RequestUri ?? requested).AbsoluteUri, "Redirected update URL");
}
