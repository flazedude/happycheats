using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

namespace HappyCheats.Core.Updates;

public sealed record UpdateConfiguration
{
    private const string LegacyLanFeed = "http://192.168.2.76:17845/happycheats/latest.json";
    private const string PeerLanFeed = "http://192.168.2.59:17845/happycheats/latest.json";
    private const string GitHubFeed = "https://github.com/flazedude/happycheats/releases/latest/download/latest.json";

    public string FeedUrl { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FallbackFeedUrl { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? FeedUrls { get; init; }
    public string PublicKeyPem { get; init; } = "";
    public string Channel { get; init; } = "stable";

    public static UpdateConfiguration? Load(string appDirectory)
    {
        var path = Path.Combine(appDirectory, "update-settings.json");
        if (!File.Exists(path)) return null;
        var settings = Read(path);
        // The updater preserves the installation's trusted settings. A signed
        // bundle may supply the official ordered feed chain while retaining its
        // signing identity, or supplement a missing mirror for legacy feeds.
        var defaultsPath = Path.Combine(appDirectory, "update-defaults.json");
        if (File.Exists(defaultsPath))
        {
            try
            {
                var defaults = Read(defaultsPath);
                using var trusted = ReleaseSigning.ReadKey(settings.PublicKeyPem);
                using var bundled = ReleaseSigning.ReadKey(defaults.PublicKeyPem);
                if (settings.Channel == defaults.Channel
                    && CryptographicOperations.FixedTimeEquals(trusted.ExportSubjectPublicKeyInfo(), bundled.ExportSubjectPublicKeyInfo()))
                {
                    // Only the known official configuration is migrated. A custom
                    // primary or explicit custom fallback remains the user's choice.
                    if (defaults.HasOfficialFeedChain() && settings.IsKnownOfficialConfiguration())
                        settings = settings with { FeedUrl = defaults.FeedUrl, FallbackFeedUrl = defaults.FallbackFeedUrl, FeedUrls = defaults.FeedUrls!.ToArray() };
                    else if (settings.FeedUrls == null && defaults.FeedUrls == null
                        && SameFeed(settings.FeedUrl, LegacyLanFeed) && SameFeed(defaults.FeedUrl, GitHubFeed)
                        && defaults.FallbackFeedUrl == null
                        && (settings.FallbackFeedUrl == null || SameFeed(settings.FallbackFeedUrl, GitHubFeed)))
                        settings = settings with { FeedUrl = defaults.FeedUrl, FallbackFeedUrl = null };
                    else if (settings.FeedUrls == null && defaults.FeedUrls == null
                        && settings.FallbackFeedUrl == null && defaults.FallbackFeedUrl != null
                        && SameFeed(settings.FeedUrl, defaults.FeedUrl))
                        settings = settings with { FallbackFeedUrl = defaults.FallbackFeedUrl };
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or CryptographicException)
            {
                // Optional bundled defaults must not disable a working trusted feed.
            }
        }
        return settings;
    }

    private static bool SameFeed(string left, string right)
        => UpdateValidation.Url(left, "Update feed").Equals(UpdateValidation.Url(right, "Update feed"));

    private bool HasOfficialFeedChain() => FeedUrls is { Length: 3 }
        && SameFeed(FeedUrls[0], LegacyLanFeed) && SameFeed(FeedUrls[1], PeerLanFeed) && SameFeed(FeedUrls[2], GitHubFeed)
        && SameFeed(FeedUrl, LegacyLanFeed) && FallbackFeedUrl != null && SameFeed(FallbackFeedUrl, GitHubFeed);

    private bool IsKnownOfficialConfiguration() => HasOfficialFeedChain() || (FeedUrls == null &&
        ((SameFeed(FeedUrl, LegacyLanFeed) && (FallbackFeedUrl == null || SameFeed(FallbackFeedUrl, GitHubFeed)))
         || (SameFeed(FeedUrl, GitHubFeed) && FallbackFeedUrl == null)));

    private static UpdateConfiguration Read(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024) throw new InvalidDataException("Update settings are too large.");
        var bytes = File.ReadAllBytes(path);
        ReleaseSigning.RejectDuplicateProperties(bytes);
        var settings = JsonSerializer.Deserialize<UpdateConfiguration>(bytes, ReleaseSigning.JsonOptions)
            ?? throw new InvalidDataException("Update settings are empty.");
        settings.Validate();
        return settings;
    }

    internal void Validate()
    {
        UpdateValidation.Url(FeedUrl, "Update feed");
        if (FallbackFeedUrl != null) UpdateValidation.Url(FallbackFeedUrl, "Fallback update feed");
        if (FeedUrls != null)
        {
            if (FeedUrls.Length is < 1 or > 8) throw new InvalidDataException("Configure between one and eight ordered update feeds.");
            var addresses = FeedUrls.Select(value => UpdateValidation.Url(value, "Ordered update feed")).ToArray();
            if (addresses.Distinct().Count() != addresses.Length || !SameFeed(FeedUrls[0], FeedUrl)
                || (FallbackFeedUrl != null && !SameFeed(FeedUrls[^1], FallbackFeedUrl)))
                throw new InvalidDataException("Ordered update feeds must be unique and match the primary and final fallback compatibility fields.");
        }
        UpdateValidation.Channel(Channel);
        using var key = ReleaseSigning.ReadKey(PublicKeyPem);
    }
}

public sealed record ReleaseManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string Version { get; init; } = "";
    public string Channel { get; init; } = "stable";
    public DateTimeOffset PublishedAt { get; init; }
    public string PackageUrl { get; init; } = "";
    public long PackageSize { get; init; }
    public string Sha256 { get; init; } = "";
    public string ChangelogEnglish { get; init; } = "";
    public string ChangelogChinese { get; init; } = "";
    public string[] NewGames { get; init; } = Array.Empty<string>();
}

public sealed record SignedReleaseEnvelope
{
    public string Payload { get; init; } = "";
    public string Signature { get; init; } = "";
}

public sealed record UpdateProgress(long BytesReceived, long TotalBytes);
public sealed record StagedUpdate(string PackageDirectory, string WorkDirectory, ReleaseManifest Release);

internal static class UpdateValidation
{
    internal const long MaxPackageBytes = 2L * 1024 * 1024 * 1024;
    internal const long MaxExtractedBytes = 4L * 1024 * 1024 * 1024;

    internal static Uri Url(string value, string description)
    {
        return UpdateAddress.Validate(value, description);
    }

    internal static void Channel(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 32 || !IsAlphaNumeric(value[0]) ||
            value.Any(c => !IsAlphaNumeric(c) && c != '-'))
            throw new InvalidDataException("Update channel must contain lowercase letters, digits, or hyphens.");
    }

    private static bool IsAlphaNumeric(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9';

    internal static void Manifest(ReleaseManifest release)
    {
        if (release.SchemaVersion != 1) throw new InvalidDataException("This update feed format is not supported.");
        _ = ReleaseVersion.Parse(release.Version);
        Channel(release.Channel);
        Url(release.PackageUrl, "Update package URL");
        if (release.PublishedAt == default) throw new InvalidDataException("The release publication date is missing.");
        if (release.PackageSize <= 0 || release.PackageSize > MaxPackageBytes)
            throw new InvalidDataException("The update package size is outside the supported range (1 byte to 2 GiB).");
        if (release.Sha256 is null || release.Sha256.Length != 64 || release.Sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("The update package SHA-256 is invalid.");
        if (release.ChangelogEnglish is null || release.ChangelogChinese is null ||
            release.ChangelogEnglish.Length > 20_000 || release.ChangelogChinese.Length > 20_000 ||
            release.NewGames is null || release.NewGames.Length > 100 ||
            release.NewGames.Any(g => string.IsNullOrWhiteSpace(g) || g.Length > 200))
            throw new InvalidDataException("The release changelog or new-game list is invalid or too large.");
    }
}
