using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using HappyCheats.Core.Updates;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core.Cheats;

/// <summary>Versioned per-user modules survive client updates. Active receipts are signed catalogs.</summary>
public sealed class CheatStore(string root, CheatFeedConfiguration trust)
{
    public string Root { get; } = Path.GetFullPath(root);
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HappyCheats", "Cheats");
    private string Active(string id) => SafePath(Root, $"active/{id}.json");
    private string Package(CheatRelease release) => SafePath(Root, $"packages/{release.Id}/{release.Version}-{release.Sha256}");

    public static Dictionary<string, string> BundledVersions(string directory)
    {
        var path = Path.Combine(directory, "cheat-versions.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(path)) ?? new() : new();
    }

    public Dictionary<string, string> InstalledVersions(IReadOnlyDictionary<string, string> bundled)
    {
        var versions = new Dictionary<string, string>(bundled);
        foreach (var (id, release, _) in Receipts())
            if (!versions.TryGetValue(id, out var existing) || ReleaseVersion.Parse(release.Version).CompareTo(ReleaseVersion.Parse(existing)) > 0) versions[id] = release.Version;
        return versions;
    }

    private IEnumerable<(string Id, CheatRelease Release, byte[] Proof)> Receipts()
    {
        var active = SafePath(Root, "active");
        if (!Directory.Exists(active)) yield break;
        foreach (var path in Directory.EnumerateFiles(active, "*.json").Order(StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (new FileInfo(SafePath(Root, "active/" + Path.GetFileName(path))).Length > CheatSigning.MaximumFeedBytes) throw new InvalidDataException("Installed cheat receipt is too large.");
            var proof = File.ReadAllBytes(path);
            var catalog = CheatSigning.Verify(proof, trust.PublicKeyPem, trust.Channel);
            var release = catalog.Games.SingleOrDefault(x => x.Id == id) ?? throw new InvalidDataException("Installed cheat receipt does not contain its game.");
            yield return (id, release, proof);
        }
    }

    public IReadOnlyList<IGameModule> Load(IReadOnlyList<IGameModule> bundled, IReadOnlyDictionary<string, string> versions, string clientVersion, Action<string> error)
    {
        var result = bundled.ToDictionary(x => x.Definition.Id);
        string active;
        try { active = SafePath(Root, "active"); }
        catch (Exception ex)
        {
            var message = "Cannot verify the cheat cache. Restore its ordinary local folder and retry: " + ex.Message;
            error(message);
            return bundled.Select(module => (IGameModule)new UnavailableModule(module, module.Definition.Id, message)).ToArray();
        }
        if (!Directory.Exists(active)) return result.Values.ToArray();
        foreach (var file in Directory.EnumerateFiles(active, "*.json").Order(StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            try
            {
                var path = SafePath(Root, "active/" + Path.GetFileName(file));
                if (new FileInfo(path).Length > CheatSigning.MaximumFeedBytes) throw new InvalidDataException("Installed cheat receipt is too large.");
                var catalog = CheatSigning.Verify(File.ReadAllBytes(path), trust.PublicKeyPem, trust.Channel);
                var release = catalog.Games.SingleOrDefault(x => x.Id == id) ?? throw new InvalidDataException("Installed cheat receipt does not contain its game.");
                if (versions.TryGetValue(id, out var version) && ReleaseVersion.Parse(release.Version).CompareTo(ReleaseVersion.Parse(version)) <= 0) continue;
                RequireCompatible(release, clientVersion);
                result[id] = LoadPackage(Package(release), release);
            }
            catch (Exception ex)
            {
                var message = $"Cannot load {id} cheat: {ex.Message} Repair it from Cheat updates before using it.";
                error(message);
                result[id] = new UnavailableModule(result.GetValueOrDefault(id), id, message);
            }
        }
        return result.Values.ToArray();
    }

    public static void RequireCompatible(CheatRelease release, string clientVersion)
    {
        if (release.ContractVersion != 1 || ReleaseVersion.Parse(release.MinimumClientVersion).CompareTo(ReleaseVersion.Parse(clientVersion)) > 0)
            throw new InvalidOperationException($"Update HappyCheats to {release.MinimumClientVersion} or later before installing this cheat (module API {release.ContractVersion}).");
    }

    /// <summary>Load just the verified game being applied; leave other games' sessions untouched.</summary>
    public IGameModule LoadInstalled(CheatRelease release, string clientVersion)
    {
        var catalog = CheatSigning.Verify(File.ReadAllBytes(Active(release.Id)), trust.PublicKeyPem, trust.Channel);
        var active = catalog.Games.SingleOrDefault(x => x.Id == release.Id);
        if (active == null || !CheatSigning.SamePackage(active, release))
            throw new InvalidDataException("The downloaded trainer changed. Check for updates and try again.");
        RequireCompatible(active, clientVersion);
        return LoadPackage(Package(active), active);
    }

    public Task InstallAsync(CheatRelease release, byte[] proof, Func<string, Task> download, string clientVersion, CancellationToken token)
        => InstallWithActivityAsync(release, proof, download, clientVersion, token, null);

    internal async Task InstallWithActivityAsync(CheatRelease release, byte[] proof, Func<string, Task> download, string clientVersion, CancellationToken token,
        Action<CheatUpdateActivity>? activity)
    {
        activity?.Invoke(new(CheatUpdateStage.CheckingCache));
        var verified = CheatSigning.Verify(proof, trust.PublicKeyPem, trust.Channel).Games.SingleOrDefault(x => x.Id == release.Id);
        if (verified == null || !CheatSigning.SamePackage(verified, release)) throw new InvalidDataException("Check and verify this cheat before downloading it.");
        RequireCompatible(release, clientVersion);
        Directory.CreateDirectory(Root);
        using var mutation = new FileStream(SafePath(Root, "install.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        // The lock spans download and activation; a second client gets a click-time busy error.
        var active = Active(release.Id);
        if (File.Exists(active))
        {
            var old = CheatSigning.Verify(File.ReadAllBytes(active), trust.PublicKeyPem, trust.Channel).Games.Single(x => x.Id == release.Id);
            var order = ReleaseVersion.Parse(release.Version).CompareTo(ReleaseVersion.Parse(old.Version));
            if (order < 0 || order == 0 && !CheatSigning.SamePackage(release, old)) throw new InvalidDataException("Refusing a cheat downgrade or conflicting same-version package.");
        }
        var destination = Package(release);
        var ready = false;
        if (Directory.Exists(destination))
        {
            try { var probe = LoadPackage(destination, release); ReleaseModule(probe); ready = true; }
            catch (Exception error) when (error is IOException or InvalidDataException or BadImageFormatException) { /* Explicit repair retains the damaged package for recovery. */ }
        }
        activity?.Invoke(new(ready ? CheatUpdateStage.ReusingCache : CheatUpdateStage.RepairingCache));
        if (!ready)
        {
            var stage = SafePath(Root, "staging/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                var archive = SafePath(stage, "package.zip");
                await download(archive).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                activity?.Invoke(new(CheatUpdateStage.VerifyingPackage));
                VerifyArchive(archive, release);
                activity?.Invoke(new(CheatUpdateStage.Extracting));
                Extract(archive, stage, release.Id);
                activity?.Invoke(new(CheatUpdateStage.VerifyingModule));
                var probe = LoadPackage(stage, release);
                ReleaseModule(probe);
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (Directory.Exists(destination))
                {
                    var recovery = SafePath(Root, "recovery/" + release.Id + "-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
                    Directory.Move(destination, recovery);
                }
                Directory.Move(stage, destination);
            }
            catch
            {
                // This unique staging child was created by this operation; active versions stay intact.
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                throw;
            }
        }
        token.ThrowIfCancellationRequested();
        activity?.Invoke(new(CheatUpdateStage.SavingPackage));
        Directory.CreateDirectory(Path.GetDirectoryName(active)!);
        var temporary = active + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporary, proof, token).ConfigureAwait(false);
        try { File.Move(temporary, active, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void VerifyArchive(string path, CheatRelease release)
    {
        if (new FileInfo(path).Length != release.PackageSize || !Hash(path).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cheat package size or SHA-256 failed. No fallback was attempted.");
    }

    public static void ReleaseModule(IGameModule module)
    {
        module.Deactivate();
        var context = AssemblyLoadContext.GetLoadContext(module.GetType().Assembly);
        if (context?.IsCollectible == true) context.Unload();
    }

    private static IEnumerable<ZipArchiveEntry> Entries(ZipArchive zip, string id)
    {
        if (zip.Entries.Count is 0 or > 30000) throw new InvalidDataException("Invalid cheat archive entry count.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (name.EndsWith('/')) continue;
            if (name.Contains('\\') || name.Split('/').Any(p => p is "" or "." or ".." || p.Contains(':') || p.EndsWith('.') || p.EndsWith(' ') || p.Any(char.IsControl))
                || name.StartsWith('/') || !paths.Add(name) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000
                || !(name.StartsWith($"payload/{id}/", StringComparison.Ordinal) || name.StartsWith("lib/", StringComparison.Ordinal) || name is "artwork/icon.png" or "artwork/logo.png")
                || entry.Length < 0 || entry.Length > 1024L * 1024 * 1024 || (total += entry.Length) > 4L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Unsafe or unexpected file in cheat archive: " + name);
            yield return entry;
        }
    }

    private static void Extract(string archive, string stage, string id)
    {
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in Entries(zip, id))
        {
            var path = SafePath(stage, "content/" + entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open(); using var output = new FileStream(path, FileMode.CreateNew);
            input.CopyTo(output);
        }
    }

    public static IGameModule LoadPackage(string directory, CheatRelease release)
    {
        var archive = SafePath(directory, "package.zip");
        VerifyArchive(archive, release);
        using var zip = ZipFile.OpenRead(archive);
        var content = SafePath(directory, "content");
        var libraries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries(zip, release.Id))
        {
            var path = SafePath(content, entry.FullName);
            if (!File.Exists(path) || new FileInfo(path).Length != entry.Length) throw new InvalidDataException("Installed cheat file is missing or changed: " + entry.FullName);
            using var expected = entry.Open(); using var actual = File.OpenRead(path);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(expected), SHA256.HashData(actual))) throw new InvalidDataException("Installed cheat file failed verification: " + entry.FullName);
            if (entry.FullName.StartsWith("lib/", StringComparison.Ordinal))
            {
                if (entry.FullName.Count(c => c == '/') != 1 || !entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unexpected module library.");
                using var input = entry.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes);
                libraries.Add(Path.GetFileNameWithoutExtension(entry.FullName), bytes.ToArray());
            }
        }
        if (!libraries.TryGetValue("HappyCheats.Core", out var main)) throw new InvalidDataException("Cheat module library is missing.");
        var context = new ModuleContext(libraries);
        try
        {
            using var input = new MemoryStream(main);
            var assembly = context.LoadFromStream(input);
            var type = assembly.GetType(release.ModuleType, true)!;
            var module = Activator.CreateInstance(type, content) as IGameModule ?? throw new InvalidDataException("Cheat does not implement the shared module interface.");
            ValidateModule(module, release.Id);
            return module;
        }
        catch { context.Unload(); throw; }
    }

    public static void ValidateModule(IGameModule module, string id)
    {
        var game = module.Definition;
        if (game.Id != id || string.IsNullOrWhiteSpace(game.Name) || module.GameExecutables.Count == 0 || game.Features.Count == 0
            || game.Features.Select(x => x.Id).Distinct().Count() != game.Features.Count)
            throw new InvalidDataException("Cheat module is missing shared-menu metadata or game executables.");
        foreach (var executable in module.GameExecutables)
        {
            if (Path.IsPathRooted(executable) || executable.Contains(':') || executable.Replace('\\', '/').Split('/').Any(p => p is "" or "." or ".."))
                throw new InvalidDataException("Game executables must be exact installation-relative paths.");
            _ = SafePath(Path.GetTempPath(), executable);
        }
        foreach (var feature in game.Features)
            if (string.IsNullOrWhiteSpace(feature.Id) || string.IsNullOrWhiteSpace(feature.Name) || string.IsNullOrWhiteSpace(feature.Description)
                || feature.Number is { } number && (module is not IFeatureValueModule || number.Minimum > number.Maximum || number.DefaultValue < number.Minimum || number.DefaultValue > number.Maximum || number.Step <= 0)
                || feature.Choices is { } choices && (module is not IFeatureChoiceModule || choices.Count == 0 || choices.Select(x => x.Value).Distinct().Count() != choices.Count))
                throw new InvalidDataException("Cheat module has incomplete feature metadata.");
    }

    private sealed class ModuleContext(Dictionary<string, byte[]> libraries) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == typeof(IGameModule).Assembly.GetName().Name) return typeof(IGameModule).Assembly;
            if (name.Name != null && libraries.TryGetValue(name.Name, out var bytes)) { using var input = new MemoryStream(bytes); return LoadFromStream(input); }
            if (name.Name?.StartsWith("HappyCheats", StringComparison.Ordinal) == true || name.Name?.StartsWith("LaundryTrainer", StringComparison.Ordinal) == true)
                throw new FileNotFoundException("Required cheat dependency is missing: " + name.Name);
            return null;
        }
    }

    private sealed class UnavailableModule(IGameModule? previous, string id, string error) : IGameModule
    {
        public GameDefinition Definition => previous?.Definition ?? new(id, id, "", "", "!", error, []);
        public IReadOnlyList<string> GameExecutables => previous?.GameExecutables ?? [];
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => Task.FromResult(new GameInstallation(directory ?? "", "", false, false, false, false, error));
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.FromResult(TrainerStatus.Offline(error));
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.FromException(new IOException(error));
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => Task.FromException(new IOException(error));
        public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) => Task.FromException(new IOException(error));
        public Task LaunchAsync(GameInstallation installation) => Task.FromException(new IOException(error));
    }
}
