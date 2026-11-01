using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using HappyCheats.Core.Cheats;

namespace HappyCheats.TestSupport;
internal sealed class CheatFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "HappyCheats-cheat-tests-" + Guid.NewGuid().ToString("N"));
    public RSA Key { get; } = RSA.Create(2048);
    public byte[] Archive { get; }
    public CheatRelease Release { get; }
    public CheatFeedConfiguration Configuration => new(Key.ExportSubjectPublicKeyInfoPem(), "stable", ["http://192.168.2.76:17845/happycheats/cheats.json", "http://192.168.2.59:17845/happycheats/cheats.json", CheatFeedConfiguration.GitHubFeed]);
    public CheatStore Store => new(Path.Combine(Root, "store"), Configuration);
    public CheatFixture()
    {
        Directory.CreateDirectory(Root);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var module = zip.CreateEntry("lib/HappyCheats.Core.dll");
            using (var stream = module.Open()) stream.Write(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "FutureGame.dll")));
            var file = zip.CreateEntry("payload/future-game/fixture.txt");
            using var text = new StreamWriter(file.Open()); text.Write("synthetic payload");
        }
        Archive = output.ToArray();
        Release = new() { Id = "future-game", Name = "Future Game", Version = "1.0.0", ModuleType = "HappyCheats.Core.FutureGameModule", SourceCommit = new('a', 40),
            PackageUrl = "https://github.com/flazedude/happycheats/releases/download/cheat-future-game-1.0.0/HappyCheats-cheat-future-game-1.0.0.zip",
            PackageSize = Archive.Length, Sha256 = Convert.ToHexString(SHA256.HashData(Archive)).ToLowerInvariant() };
    }
    public byte[] Proof(CheatRelease? release = null, long revision = 1) => CheatSigning.Sign(new() { Revision = revision, Games = [release ?? Release] }, Key.ExportPkcs8PrivateKeyPem());
    public Task Install(CheatRelease? release = null, byte[]? proof = null, CancellationToken token = default) => Store.InstallAsync(release ?? Release, proof ?? Proof(release), path => File.WriteAllBytesAsync(path, Archive, token), "0.3.65", token);
    public void Dispose()
    {
        Key.Dispose();
        var resolved = Path.GetFullPath(Root);
        if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("HappyCheats-cheat-tests-")) throw new IOException("Unsafe test cleanup path.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
