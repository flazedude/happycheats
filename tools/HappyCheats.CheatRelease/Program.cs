using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HappyCheats.Core;
using HappyCheats.Core.Cheats;
using HappyCheats.Core.Updates;

try
{
    if (args.Length == 0) throw new ArgumentException("Use pack, catalog, verify, or mirror with --option value pairs.");
    var options = new Dictionary<string, string>();
    for (var i = 1; i < args.Length; i += 2)
        if (i + 1 >= args.Length || !args[i].StartsWith("--") || !options.TryAdd(args[i][2..], args[i + 1])) throw new ArgumentException("Invalid or duplicate command option.");
    string Required(string name) => options.GetValueOrDefault(name) ?? throw new ArgumentException("Missing --" + name);
    byte[] ReadProof(string name) => File.ReadAllBytes(Required(name));
    CheatCatalog Verify(string name) => CheatSigning.Verify(ReadProof(name), File.ReadAllText(Required("public-key")), "stable");
    switch (args[0])
    {
        case "pack":
        {
            var root = Path.GetFullPath(Required("root"));
            var id = Required("game"); var version = Required("version");
            var module = ModuleCatalog.Create(root).Single(x => x.Definition.Id == id);
            CheatStore.ValidateModule(module, id);
            var tracked = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(Path.Combine(root, "modules", "cheat-versions.json")))!;
            if (tracked.GetValueOrDefault(id) != version) throw new IOException("Commit this game's package version in modules/cheat-versions.json before publishing.");
            var output = Path.GetFullPath(Required("output")); Directory.CreateDirectory(output);
            var release = new CheatRelease
            {
                Id = id, Name = module.Definition.Name, Version = version, MinimumClientVersion = options.GetValueOrDefault("minimum-client", "0.3.65"),
                ModuleType = module.GetType().FullName!, SourceCommit = Required("source"),
                PackageUrl = $"https://github.com/flazedude/happycheats/releases/download/cheat-{id}-{version}/HappyCheats-cheat-{id}-{version}.zip",
                NotesEnglish = File.ReadAllText(Required("notes-en")), NotesChinese = File.ReadAllText(Required("notes-zh"))
            };
            var archive = Path.Combine(output, release.FileName);
            if (File.Exists(archive) || File.Exists(Path.Combine(output, "release.json"))) throw new IOException("Package stage already exists. Reuse its verified immutable bytes; never rebuild this version.");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                // Keep the small module assembly and its private dependencies separate from the frozen contract.
                foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll").Where(x => Path.GetFileName(x) is "HappyCheats.Core.dll" or "LaundryTrainer.Setup.Core.dll"))
                    zip.CreateEntryFromFile(dll, "lib/" + Path.GetFileName(dll), CompressionLevel.Optimal);
                var payload = ModuleIO.SafePath(root, "payload/" + id);
                if (!Directory.Exists(payload) || !Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories).Any()) throw new IOException("Game payload is missing.");
                foreach (var file in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    _ = ModuleIO.SafePath(payload, Path.GetRelativePath(payload, file));
                    zip.CreateEntryFromFile(file, "payload/" + id + "/" + Path.GetRelativePath(payload, file).Replace('\\', '/'), CompressionLevel.Optimal);
                }
                foreach (var kind in new[] { "icon", "logo" })
                {
                    var artwork = Path.Combine(root, "assets", "games", id + "-" + kind + ".png");
                    if (File.Exists(artwork)) zip.CreateEntryFromFile(artwork, "artwork/" + kind + ".png");
                }
            }
            release = release with { PackageSize = new FileInfo(archive).Length, Sha256 = ModuleIO.Hash(archive).ToLowerInvariant() };
            CheatSigning.Validate(new() { Revision = 1, Games = [release] });
            File.WriteAllBytes(Path.Combine(output, "release.json"), JsonSerializer.SerializeToUtf8Bytes(release, ReleaseSigning.JsonOptions));
            Console.WriteLine($"Packed {id} {version}: {release.PackageSize} bytes, SHA-256 {release.Sha256}");
            break;
        }
        case "catalog":
        {
            var release = JsonSerializer.Deserialize<CheatRelease>(ReadProof("release"), ReleaseSigning.JsonOptions)!;
            var previous = options.ContainsKey("previous") ? Verify("previous") : new CheatCatalog { Revision = 1 };
            var existing = previous.Games.SingleOrDefault(x => x.Id == release.Id);
            if (existing != null && (ReleaseVersion.Parse(release.Version).CompareTo(ReleaseVersion.Parse(existing.Version)) < 0
                || release.Version == existing.Version && !CheatSigning.SamePackage(existing, release))) throw new IOException("Cannot downgrade or replace a published cheat package.");
            var next = previous with { Revision = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), previous.Revision + 1),
                Games = previous.Games.Where(x => x.Id != release.Id).Append(release).OrderBy(x => x.Id).ToArray() };
            var proof = CheatSigning.Sign(next, File.ReadAllText(Required("private-key")));
            File.WriteAllBytes(Required("output"), proof);
            Console.WriteLine($"Signed cheat catalog revision {next.Revision} ({next.Games.Length} games).");
            break;
        }
        case "verify":
        {
            var catalog = Verify("catalog");
            if (options.TryGetValue("package", out var package)) CheatStore.VerifyArchive(package, catalog.Games.Single(x => x.Id == Required("game")));
            Console.WriteLine($"Verified cheat catalog {catalog.Revision} ({catalog.Games.Length} games).");
            break;
        }
        case "mirror":
        {
            var catalog = Verify("catalog"); var directory = Path.GetFullPath(Required("directory")); Directory.CreateDirectory(directory);
            foreach (var release in catalog.Games)
            {
                var package = ModuleIO.SafePath(directory, release.FileName);
                CheatStore.VerifyArchive(package, release);
            }
            var current = ModuleIO.SafePath(directory, "cheats.json");
            if (File.Exists(current))
            {
                var old = CheatSigning.Verify(File.ReadAllBytes(current), File.ReadAllText(Required("public-key")), "stable");
                if (old.Revision > catalog.Revision) throw new IOException("Refusing to downgrade the cheat mirror.");
            }
            var temp = current + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, ReadProof("catalog")); File.Move(temp, current, true);
            Console.WriteLine($"Activated cheat mirror revision {catalog.Revision}."); break;
        }
        default: throw new ArgumentException("Unknown cheat release command.");
    }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine("Cheat release failed: " + error.Message); return 1; }
