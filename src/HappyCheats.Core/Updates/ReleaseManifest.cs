using System.Text.Json;

namespace HappyCheats.Core.Updates;

public sealed record UpdateConfiguration
{
    public string FeedUrl { get; init; } = "";
    public string PublicKeyPem { get; init; } = "";
    public string Channel { get; init; } = "stable";

    public static UpdateConfiguration? Load(string appDirectory)
    {
        var path = Path.Combine(appDirectory, "update-settings.json");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 32 * 1024) throw new InvalidDataException("Update settings are too large.");
        var settings = JsonSerializer.Deserialize<UpdateConfiguration>(File.ReadAllBytes(path), ReleaseSigning.JsonOptions)
            ?? throw new InvalidDataException("Update settings are empty.");
        settings.Validate();
        return settings;
    }

    internal void Validate()
    {
        UpdateValidation.Url(FeedUrl, "Update feed");
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
