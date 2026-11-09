using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using HappyCheats.Core.Updates;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core.Cheats;

public sealed record CheatUpdateCheck(CheatCatalog Catalog, byte[] Proof, IReadOnlyList<(string Source, CheatCatalog Catalog)> Sources);

public sealed class CheatUpdateClient(HttpClient http, CheatFeedConfiguration configuration, TimeSpan? networkTimeout = null)
{
    public event Action<CheatUpdateActivity>? ActivityChanged;
    public async Task<CheatUpdateCheck> CheckAsync(string storeRoot, CancellationToken token = default)
    {
        var candidates = new List<(string Source, CheatCatalog Catalog, byte[] Proof)>();
        Exception? last = null;
        foreach (var source in configuration.FeedUrls)
        {
            ActivityChanged?.Invoke(new(CheatUpdateStage.CheckingSource, source));
            try
            {
                var proof = await ReadAsync(source, CheatSigning.MaximumFeedBytes, token).ConfigureAwait(false);
                CheatCatalog catalog;
                try { catalog = CheatSigning.Verify(proof, configuration.PublicKeyPem, configuration.Channel); }
                catch (Exception error) when (error is JsonException or FormatException or CryptographicException) { throw new InvalidDataException("Invalid signed cheat feed.", error); }
                candidates.Add((source, catalog, proof));
                ActivityChanged?.Invoke(new(CheatUpdateStage.SourceVerified, source));
            }
            catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !token.IsCancellationRequested)
            { last = error; ActivityChanged?.Invoke(new(CheatUpdateStage.SourceUnavailable, source)); }
        }
        token.ThrowIfCancellationRequested();
        if (candidates.Count == 0) throw new IOException("No cheat update source could be reached. Check your connection and try again.", last);
        ActivityChanged?.Invoke(new(CheatUpdateStage.SelectingCatalog));
        var cache = SafePath(storeRoot, "catalog.json");
        Directory.CreateDirectory(storeRoot);
        using var catalogLock = new FileStream(SafePath(storeRoot, "catalog.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var all = candidates.ToList();
        if (File.Exists(cache))
        {
            if (new FileInfo(cache).Length > CheatSigning.MaximumFeedBytes) throw new InvalidDataException("Cached cheat catalog is too large.");
            var proof = await File.ReadAllBytesAsync(cache, token).ConfigureAwait(false);
            all.Add(("cache", CheatSigning.Verify(proof, configuration.PublicKeyPem, configuration.Channel), proof));
        }
        var newest = all.MaxBy(x => x.Catalog.Revision);
        foreach (var other in all)
        {
            if (other.Catalog.Revision == newest.Catalog.Revision && !other.Proof.SequenceEqual(newest.Proof)) throw new InvalidDataException("Conflicting signed cheat catalogs have the same revision.");
            foreach (var old in other.Catalog.Games)
            {
                var current = newest.Catalog.Games.SingleOrDefault(x => x.Id == old.Id);
                if (current == null || ReleaseVersion.Parse(current.Version).CompareTo(ReleaseVersion.Parse(old.Version)) < 0
                    || current.Version == old.Version && !CheatSigning.SamePackage(current, old))
                    throw new InvalidDataException("Cheat catalog removed, downgraded, or changed an immutable game package.");
            }
        }
        Directory.CreateDirectory(storeRoot);
        var temp = SafePath(storeRoot, "catalog-" + Guid.NewGuid().ToString("N") + ".tmp");
        await File.WriteAllBytesAsync(temp, newest.Proof, token).ConfigureAwait(false);
        try { File.Move(temp, cache, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return new(newest.Catalog, newest.Proof, candidates.Select(x => (x.Source, x.Catalog)).ToArray());
    }

    public async Task InstallAsync(CheatUpdateCheck check, CheatRelease release, CheatStore store, string clientVersion,
        IProgress<UpdateProgress>? progress = null, CancellationToken token = default)
    {
        await store.InstallWithActivityAsync(release, check.Proof, async destination =>
        {
            var urls = new List<string>();
            foreach (var candidate in check.Sources)
            {
                var matching = candidate.Catalog.Games.SingleOrDefault(x => x.Id == release.Id);
                if (matching == null || !CheatSigning.SamePackage(matching, release)) continue;
                var feed = UpdateAddress.Validate(candidate.Source, "Cheat feed");
                if (IsLocalUrl(feed.AbsoluteUri)) urls.Add(new Uri(feed, release.FileName).AbsoluteUri);
            }
            urls.Add(release.PackageUrl);
            Exception? last = null;
            foreach (var url in urls.Distinct())
            {
                try { await DownloadAsync(url, destination, release, progress, token).ConfigureAwait(false); return; }
                catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !token.IsCancellationRequested)
                { last = error; ActivityChanged?.Invoke(new(CheatUpdateStage.SourceUnavailable, url)); if (File.Exists(destination)) File.Delete(destination); }
            }
            throw new IOException("This exact cheat package could not be downloaded. Check your connection and retry.", last);
        }, clientVersion, token, activity => ActivityChanged?.Invoke(activity)).ConfigureAwait(false);
    }

    private CancellationTokenSource Timeout(string url, CancellationToken token)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        source.CancelAfter(networkTimeout ?? (IsLocalUrl(url) ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(60)));
        return source;
    }

    private async Task<byte[]> ReadAsync(string url, int maximum, CancellationToken token)
    {
        using var timeout = Timeout(url, token);
        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateAddress.Validate(url, "Cheat feed"));
        request.Headers.CacheControl = new() { NoCache = true };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        ValidateRedirect(response, url);
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Cheat feed exceeds its size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[8192];
        int count;
        while ((count = await ReadChunkAsync(input, buffer, url, timeout.Token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("Cheat feed exceeds its size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private async Task DownloadAsync(string url, string destination, CheatRelease release, IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        ActivityChanged?.Invoke(new(CheatUpdateStage.Connecting, url));
        progress?.Report(new(0, release.PackageSize));
        using var headersTimeout = Timeout(url, token);
        using var response = await http.GetAsync(UpdateAddress.Validate(url, "Cheat package"), HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode(); ValidateRedirect(response, url);
        if (response.Content.Headers.ContentLength is { } size && size != release.PackageSize) throw new InvalidDataException("Cheat download size does not match the signed catalog.");
        ActivityChanged?.Invoke(new(CheatUpdateStage.Downloading, url));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; long received = 0; int count;
        var reportClock = System.Diagnostics.Stopwatch.StartNew();
        while ((count = await ReadChunkAsync(input, buffer, url, timeout.Token).ConfigureAwait(false)) != 0)
        {
            received += count;
            if (received > release.PackageSize) throw new InvalidDataException("Cheat download exceeds its signed size.");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
            if (received == count || received == release.PackageSize || reportClock.ElapsedMilliseconds >= 100)
            { progress?.Report(new(received, release.PackageSize)); reportClock.Restart(); }
        }
        if (received != release.PackageSize || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cheat download failed SHA-256 verification. No fallback was attempted.");
    }

    private static void ValidateRedirect(HttpResponseMessage response, string original)
    {
        var final = UpdateAddress.Validate(response.RequestMessage?.RequestUri?.AbsoluteUri ?? original, "Cheat redirect");
        if (original.StartsWith("https:", StringComparison.OrdinalIgnoreCase) && final.Scheme != "https") throw new InvalidDataException("Cheat download redirected to insecure HTTP.");
    }

    private async Task<int> ReadChunkAsync(Stream input, byte[] buffer, string url, CancellationToken token)
    {
        using var inactivity = Timeout(url, token);
        try { return await input.ReadAsync(buffer, inactivity.Token).ConfigureAwait(false); }
        catch (IOException error) { throw new HttpRequestException("The cheat transfer connection was interrupted.", error); }
    }

    private static bool IsLocalUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.IsLoopback || System.Net.IPAddress.TryParse(uri.Host, out var ip) && UpdateAddress.IsLocalAddress(ip));
}
