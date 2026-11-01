using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HappyCheats.Core.Updates;

namespace HappyCheats.Core.Cheats;

public sealed record CheatCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public string Channel { get; init; } = "stable";
    public long Revision { get; init; }
    public CheatRelease[] Games { get; init; } = [];
}

public sealed record CheatRelease
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string MinimumClientVersion { get; init; } = "0.3.65";
    public int ContractVersion { get; init; } = 1;
    public string ModuleType { get; init; } = "";
    public string Assembly { get; init; } = "lib/HappyCheats.Core.dll";
    public string SourceCommit { get; init; } = "";
    public string PackageUrl { get; init; } = "";
    public long PackageSize { get; init; }
    public string Sha256 { get; init; } = "";
    public string NotesEnglish { get; init; } = "";
    public string NotesChinese { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string FileName => $"HappyCheats-cheat-{Id}-{Version}.zip";
}

public sealed record CheatFeedConfiguration(string PublicKeyPem, string Channel, string[] FeedUrls)
{
    public const string GitHubFeed = "https://raw.githubusercontent.com/flazedude/happycheats/cheat-feed/cheats.json";
    public static CheatFeedConfiguration? FromClient(UpdateConfiguration? client)
    {
        if (client == null) return null;
        var official = new[] { "http://192.168.2.76:17845/happycheats/latest.json", "http://192.168.2.59:17845/happycheats/latest.json",
            "https://github.com/flazedude/happycheats/releases/latest/download/latest.json" };
        var configured = client.FeedUrls ?? new[] { client.FeedUrl, client.FallbackFeedUrl }.Where(x => x != null).Cast<string>().ToArray();
        if (configured.Any(x => !official.Contains(x, StringComparer.OrdinalIgnoreCase))) return null;
        return new(client.PublicKeyPem, client.Channel, [official[0].Replace("latest.json", "cheats.json"), official[1].Replace("latest.json", "cheats.json"), GitHubFeed]);
    }
}

public static class CheatSigning
{
    public const int MaximumFeedBytes = 512 * 1024;
    public static byte[] Sign(CheatCatalog catalog, string privateKeyPem)
    {
        Validate(catalog);
        var payload = JsonSerializer.SerializeToUtf8Bytes(catalog, ReleaseSigning.JsonOptions);
        if (payload.Length > 350_000) throw new InvalidDataException("Cheat catalog is too large.");
        using var key = ReleaseSigning.ReadKey(privateKeyPem);
        return JsonSerializer.SerializeToUtf8Bytes(new SignedReleaseEnvelope { Payload = Convert.ToBase64String(payload),
            Signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) }, ReleaseSigning.JsonOptions);
    }
    public static CheatCatalog Verify(byte[] bytes, string publicKeyPem, string channel)
    {
        if (bytes.Length > MaximumFeedBytes) throw new InvalidDataException("Cheat catalog is too large.");
        ReleaseSigning.RejectDuplicateProperties(bytes);
        var envelope = JsonSerializer.Deserialize<SignedReleaseEnvelope>(bytes, ReleaseSigning.JsonOptions) ?? throw new InvalidDataException("Empty cheat catalog.");
        byte[] payload, signature;
        try { payload = Convert.FromBase64String(envelope.Payload); signature = Convert.FromBase64String(envelope.Signature); }
        catch (FormatException e) { throw new InvalidDataException("Invalid cheat signature encoding.", e); }
        using var key = ReleaseSigning.ReadKey(publicKeyPem);
        if (payload.Length is 0 or > 350_000 || signature.Length > 1024 || !key.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException("Cheat catalog signature is invalid. Check the trusted source; no fallback was attempted.");
        ReleaseSigning.RejectDuplicateProperties(payload);
        var catalog = JsonSerializer.Deserialize<CheatCatalog>(payload, ReleaseSigning.JsonOptions) ?? throw new InvalidDataException("Empty cheat catalog.");
        Validate(catalog);
        if (catalog.Channel != channel) throw new InvalidDataException("Cheat update channel does not match this client.");
        return catalog;
    }
    public static void Validate(CheatCatalog catalog)
    {
        if (catalog.SchemaVersion != 1 || catalog.Revision <= 0 || catalog.Games == null || catalog.Games.Length > 200)
            throw new InvalidDataException("Unsupported cheat catalog.");
        UpdateValidation.Channel(catalog.Channel);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in catalog.Games)
        {
            if (game == null || !Regex.IsMatch(game.Id, "^[a-z0-9]+(-[a-z0-9]+)*$") || game.Id.Length > 80 || !ids.Add(game.Id)
                || string.IsNullOrWhiteSpace(game.Name) || game.Name.Length > 200 || game.ContractVersion < 1
                || game.Assembly != "lib/HappyCheats.Core.dll" || !Regex.IsMatch(game.ModuleType, @"^HappyCheats\.Core\.[A-Za-z0-9_]+Module$")
                || !Regex.IsMatch(game.SourceCommit, "^[a-f0-9]{40}$") || !Regex.IsMatch(game.Sha256, "^[a-f0-9]{64}$")
                || game.PackageSize is <= 0 or > 2147483648L || game.NotesEnglish == null || game.NotesChinese == null
                || game.NotesEnglish.Length > 20000 || game.NotesChinese.Length > 20000)
                throw new InvalidDataException("Invalid cheat package identity or metadata.");
            _ = ReleaseVersion.Parse(game.Version); _ = ReleaseVersion.Parse(game.MinimumClientVersion);
            var uri = UpdateAddress.Validate(game.PackageUrl, "Cheat package");
            if (uri.Scheme != "https" || uri.Host != "github.com" || uri.AbsolutePath != $"/flazedude/happycheats/releases/download/cheat-{game.Id}-{game.Version}/{game.FileName}")
                throw new InvalidDataException("Cheat package must use its immutable official GitHub asset.");
        }
    }
    public static bool SamePackage(CheatRelease a, CheatRelease b) => a.Id == b.Id && a.Version == b.Version && a.Sha256 == b.Sha256 && a.PackageSize == b.PackageSize
        && a.ModuleType == b.ModuleType && a.Assembly == b.Assembly && a.SourceCommit == b.SourceCommit && a.MinimumClientVersion == b.MinimumClientVersion && a.ContractVersion == b.ContractVersion;
}
