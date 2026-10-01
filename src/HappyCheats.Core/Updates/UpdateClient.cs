using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace HappyCheats.Core.Updates;

public sealed class UpdateClient
{
    private const int MaxFeedBytes = 256 * 1024;
    private readonly HttpClient http;
    private readonly UpdateConfiguration configuration;
    private readonly ConcurrentDictionary<string, byte> verifiedReleases = new(StringComparer.Ordinal);

    public UpdateClient(HttpClient http, UpdateConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        this.http = http;
        configuration = config;
    }

    public async Task<ReleaseManifest?> CheckAsync(string currentVersion, CancellationToken token = default)
    {
        var current = ReleaseVersion.Parse(currentVersion);
        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateValidation.Url(configuration.FeedUrl, "Update feed"));
        request.Headers.CacheControl = new() { NoCache = true };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        ValidateResponseUrl(response, request.RequestUri!);
        if (response.Content.Headers.ContentLength > MaxFeedBytes)
            throw new InvalidDataException("The update feed is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > MaxFeedBytes) throw new InvalidDataException("The update feed is too large.");
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        ReleaseSigning.RejectDuplicateProperties(bytes);
        var envelope = JsonSerializer.Deserialize<SignedReleaseEnvelope>(bytes, ReleaseSigning.JsonOptions)
            ?? throw new InvalidDataException("The update feed is empty.");
        var release = ReleaseSigning.Verify(envelope, configuration.PublicKeyPem);
        if (!string.Equals(release.Channel, configuration.Channel, StringComparison.Ordinal))
            throw new InvalidDataException("The signed release does not match this client's update channel.");
        if (ReleaseVersion.Parse(release.Version).CompareTo(current) <= 0) return null;
        verifiedReleases.TryAdd(Fingerprint(release), 0);
        return release;
    }

    public async Task<StagedUpdate> DownloadAndStageAsync(ReleaseManifest release, string stagingRoot,
        IProgress<UpdateProgress>? progress = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        UpdateValidation.Manifest(release);
        if (!verifiedReleases.ContainsKey(Fingerprint(release)))
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
            using var request = new HttpRequestMessage(HttpMethod.Get, UpdateValidation.Url(release.PackageUrl, "Update package URL"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            ValidateResponseUrl(response, request.RequestUri!);
            if (response.Content.Headers.ContentLength is long length && length != release.PackageSize)
                throw new InvalidDataException("The update download size does not match the signed release.");
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long received = 0;
                progress?.Report(new(0, release.PackageSize));
                int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    received += count;
                    if (received > release.PackageSize) throw new InvalidDataException("The update download exceeds its signed size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    progress?.Report(new(received, release.PackageSize));
                }
                if (received != release.PackageSize) throw new InvalidDataException("The update download is incomplete.");
                if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
                    throw new InvalidDataException("The update download failed SHA-256 verification.");
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

    private static string Fingerprint(ReleaseManifest release) => Convert.ToHexString(SHA256.HashData(ReleaseSigning.SerializeManifest(release)));

    private static void ValidateResponseUrl(HttpResponseMessage response, Uri requested)
        => UpdateValidation.Url((response.RequestMessage?.RequestUri ?? requested).AbsoluteUri, "Redirected update URL");
}
