using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HappyCheats.Core.Updates;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

return ReleaseCommand.Run(args);

internal static class ReleaseCommand
{
    private const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;

    public static int Run(string[] arguments)
    {
        if (arguments.Length == 0 || arguments[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine("""
                HappyCheats release publisher (no remote uploads)

                keygen --private-key PATH --public-key PATH
                configure --feed-url URL --public-key PATH --output PATH [--fallback-feed-url URL]
                publish --package ZIP --version X.Y.Z --base-url URL
                        --private-key PATH --output DIR --notes-en FILE
                        [--notes-zh FILE] [--new-game NAME ...] [--channel stable]

                Generate signing keys in a protected directory outside the repository.
                Configure embeds only the public key. Existing clients retain their
                trusted update configuration when installing a release archive.
                Publish writes a versioned ZIP and atomically replaces latest.json locally.
                URLs support HTTPS, or HTTP on a private LAN IPv4 address or localhost.
                Upload the ZIP first, then latest.json, to the chosen server.
                """);
            return 0;
        }

        try
        {
            var options = Parse(arguments.Skip(1).ToArray());
            switch (arguments[0])
            {
                case "keygen": GenerateKeys(options); break;
                case "configure": Configure(options); break;
                case "publish": Publish(options); break;
                default: throw new ArgumentException($"Unknown command: {arguments[0]}. Use --help.");
            }
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or
            UnauthorizedAccessException or CryptographicException or InvalidDataException or JsonException or FormatException or BadImageFormatException)
        {
            Console.Error.WriteLine($"Release failed: {exception.Message}");
            return 1;
        }
    }

    private static void GenerateKeys(Dictionary<string, List<string>> options)
    {
        Allow(options, "private-key", "public-key");
        var privatePath = Path.GetFullPath(Required(options, "private-key"));
        var publicPath = Path.GetFullPath(Required(options, "public-key"));
        if (privatePath.Equals(publicPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The private and public key paths must be different.");
        if (File.Exists(privatePath) || File.Exists(publicPath))
            throw new IOException("Key files already exist. Key generation never overwrites an existing key.");

        using var rsa = RSA.Create(3072);
        Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(publicPath)!);
        var privateCreated = false;
        try
        {
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
                fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(privatePath, fileOptions))
            {
                privateCreated = true;
                stream.Write(Encoding.UTF8.GetBytes(rsa.ExportPkcs8PrivateKeyPem()));
                stream.Flush(flushToDisk: true);
            }
            using var publicStream = new FileStream(publicPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            publicStream.Write(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem()));
            publicStream.Flush(flushToDisk: true);
        }
        catch
        {
            if (privateCreated) File.Delete(privatePath);
            throw;
        }
        Console.WriteLine($"Signing key created at {privatePath}");
        Console.WriteLine($"Public verification key created at {publicPath}");
    }

    private static void Configure(Dictionary<string, List<string>> options)
    {
        Allow(options, "feed-url", "fallback-feed-url", "public-key", "output");
        var feed = UpdateAddress.Validate(Required(options, "feed-url"), "Feed URL");
        var fallback = Optional(options, "fallback-feed-url") is { } fallbackUrl
            ? UpdateAddress.Validate(fallbackUrl, "Fallback feed URL") : null;
        if (fallback == feed) throw new ArgumentException("The fallback feed must differ from the primary feed.");
        var publicKeyPath = Path.GetFullPath(Required(options, "public-key"));
        var outputPath = Path.GetFullPath(Required(options, "output"));
        if (outputPath.Equals(publicKeyPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The configuration output cannot replace the public key file.");
        if (File.Exists(outputPath))
        {
            using var existing = File.OpenText(outputPath);
            var firstLine = existing.ReadLine()?.TrimStart('\uFEFF', ' ', '\t');
            if (firstLine?.StartsWith("-----BEGIN ", StringComparison.Ordinal) == true)
                throw new ArgumentException("The configuration output cannot replace a PEM key file.");
        }
        var publicKey = File.ReadAllText(publicKeyPath);
        if (publicKey.Contains("PRIVATE KEY", StringComparison.Ordinal) ||
            !publicKey.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
            throw new ArgumentException("Provide the public verification PEM, never a private key.");
        using (var rsa = RSA.Create())
        {
            rsa.ImportFromPem(publicKey);
            if (rsa.KeySize < 2048 || rsa.KeySize > 8192)
                throw new ArgumentException("The public RSA key must be between 2048 and 8192 bits.");
        }
        var configuration = new UpdateConfiguration
        {
            FeedUrl = feed.AbsoluteUri, FallbackFeedUrl = fallback?.AbsoluteUri,
            PublicKeyPem = publicKey, Channel = "stable"
        };
        WriteAtomically(outputPath,
            JsonSerializer.SerializeToUtf8Bytes(configuration, ReleaseSigning.JsonOptions));
        Console.WriteLine("Update configuration created with the public verification key.");
    }

    private static void Publish(Dictionary<string, List<string>> options)
    {
        Allow(options, "package", "version", "base-url", "private-key", "output", "notes-en", "notes-zh", "new-game", "channel");
        var version = Required(options, "version");
        _ = ReleaseVersion.Parse(version);
        var baseUrl = UpdateAddress.Validate(Required(options, "base-url"), "Base URL");
        if (!string.IsNullOrEmpty(baseUrl.Query)) throw new ArgumentException("Base URL cannot contain a query string.");
        var sourcePath = Path.GetFullPath(Required(options, "package"));
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists || sourceInfo.Length <= 0 || sourceInfo.Length > MaximumPackageBytes)
            throw new ArgumentException("The package must be an existing nonempty ZIP no larger than 2 GiB.");
        var english = ReadNotes(Required(options, "notes-en"));
        var chinese = Optional(options, "notes-zh") is { } chinesePath ? ReadNotes(chinesePath) : string.Empty;
        var privateKey = File.ReadAllText(Required(options, "private-key"));
        var outputDirectory = Path.GetFullPath(Required(options, "output"));
        Directory.CreateDirectory(outputDirectory);
        var packageName = $"HappyCheats-{version}-win-x64.zip";
        var packagePath = Path.Combine(outputDirectory, packageName);
        if (File.Exists(packagePath)) throw new IOException($"Version {version} has already been published here. Choose a new version; release ZIPs are immutable.");
        var temporaryPackage = Path.Combine(outputDirectory, $".{packageName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(sourcePath, temporaryPackage, overwrite: false);
            ValidateArchive(temporaryPackage, version);
            byte[] digest;
            using (var packageStream = File.OpenRead(temporaryPackage)) digest = SHA256.HashData(packageStream);
            var manifest = new ReleaseManifest
            {
                SchemaVersion = 1,
                Version = version,
                Channel = Optional(options, "channel") ?? "stable",
                PublishedAt = DateTimeOffset.UtcNow,
                PackageUrl = new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/" + packageName).AbsoluteUri,
                PackageSize = new FileInfo(temporaryPackage).Length,
                Sha256 = Convert.ToHexString(digest).ToLowerInvariant(),
                ChangelogEnglish = english,
                ChangelogChinese = chinese,
                NewGames = options.TryGetValue("new-game", out var games) ? games.ToArray() : []
            };
            var feed = ReleaseSigning.SerializeEnvelope(ReleaseSigning.Sign(manifest, privateKey));
            // The package is committed first: a visible feed must always reference a complete file.
            File.Move(temporaryPackage, packagePath, overwrite: false);
            WriteAtomically(Path.Combine(outputDirectory, "latest.json"), feed);
            Console.WriteLine($"Published local archive: {packagePath}");
            Console.WriteLine($"Published signed feed: {Path.Combine(outputDirectory, "latest.json")}");
            Console.WriteLine($"SHA-256: {manifest.Sha256}");
            Console.WriteLine("Upload the versioned ZIP first, then latest.json. No files have been uploaded.");
        }
        finally
        {
            if (File.Exists(temporaryPackage)) File.Delete(temporaryPackage);
        }
    }

    private static void ValidateArchive(string packagePath, string releaseVersion)
    {
        UpdatePackage.ValidateArchive(packagePath);
        using var archive = ZipFile.OpenRead(packagePath);
        var assemblyEntries = archive.Entries.Where(entry =>
            entry.FullName.Equals("HappyCheats/HappyCheats.dll", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (assemblyEntries.Length != 1)
            throw new InvalidDataException("The ZIP must contain one HappyCheats.dll in its package root.");
        var assembly = assemblyEntries[0];
        if (assembly.Length > 64 * 1024 * 1024)
            throw new InvalidDataException("HappyCheats.dll exceeds the assembly size limit.");
        using var assemblyStream = assembly.Open();
        using var assemblyBytes = new MemoryStream();
        assemblyStream.CopyTo(assemblyBytes);
        assemblyBytes.Position = 0;
        using var reader = new PEReader(assemblyBytes);
        if (!reader.HasMetadata) throw new InvalidDataException("HappyCheats.dll is not a managed assembly.");
        var metadata = reader.GetMetadataReader();
        var definition = metadata.GetAssemblyDefinition();
        var embeddedVersion = definition.Version;
        if (metadata.GetString(definition.Name) != "HappyCheats" || embeddedVersion.Revision != 0 ||
            $"{embeddedVersion.Major}.{embeddedVersion.Minor}.{embeddedVersion.Build}" != releaseVersion)
            throw new InvalidDataException($"The package assembly version {embeddedVersion} does not match release {releaseVersion}. Rebuild using -Version {releaseVersion}.");
    }

    private static string ReadNotes(string path)
    {
        if (new FileInfo(path).Length > 64 * 1024) throw new ArgumentException("Changelog files must be no larger than 64 KiB.");
        var notes = File.ReadAllText(path, Encoding.UTF8).Trim();
        if (string.IsNullOrWhiteSpace(notes)) throw new ArgumentException("Changelog files cannot be empty.");
        return notes;
    }

    private static void WriteAtomically(string path, byte[] contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static Dictionary<string, List<string>> Parse(string[] arguments)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Length; index += 2)
        {
            var flag = arguments[index];
            if (!flag.StartsWith("--", StringComparison.Ordinal) || flag.Length <= 2 || index + 1 >= arguments.Length ||
                arguments[index + 1].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(arguments[index + 1]))
                throw new ArgumentException($"Expected --option VALUE, near {flag}.");
            var name = flag[2..];
            if (!result.TryGetValue(name, out var values)) result.Add(name, values = []);
            else if (name != "new-game") throw new ArgumentException($"Option --{name} can only be specified once.");
            values.Add(arguments[index + 1]);
        }
        return result;
    }

    private static void Allow(Dictionary<string, List<string>> options, params string[] allowed)
    {
        foreach (var name in options.Keys)
            if (!allowed.Contains(name, StringComparer.Ordinal)) throw new ArgumentException($"Unknown option: --{name}.");
    }

    private static string Required(Dictionary<string, List<string>> options, string name) =>
        Optional(options, name) ?? throw new ArgumentException($"Missing required option: --{name}.");

    private static string? Optional(Dictionary<string, List<string>> options, string name) =>
        options.TryGetValue(name, out var values) ? values[0] : null;
}
