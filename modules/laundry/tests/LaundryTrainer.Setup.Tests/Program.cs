using System.Security.Cryptography;
using System.Text;
using LaundryTrainer.Setup.Core;

var tests = new (string Name, Action Run)[]
{
    ("VDF reads nested modern libraries with escaped backslashes, spaces, Unicode and comments", () =>
    {
        const string content = """
            // A brace in a comment must not terminate the object: }
            "libraryfolders"
            {
              "0" { "path" "C:\\Program Files (x86)\\Steam" "apps" { "3150440" "123" } }
              "1" { "path" "E:\\Games\\洗衣店 library" "label" "Escaped \"quote\"" }
            }
            """;
        var paths = SteamVdf.ReadLibraryPaths(content);
        Check.Equal(2, paths.Count);
        Check.Equal(@"C:\Program Files (x86)\Steam", paths[0]);
        Check.Equal(@"E:\Games\洗衣店 library", paths[1]);
    }),
    ("VDF reads legacy libraries and ignores non-library metadata", () =>
    {
        var paths = SteamVdf.ReadLibraryPaths("\"LibraryFolders\" { \"TimeNextStatsReport\" \"0\" \"1\" \"F:/Steam Library\" }");
        Check.Equal(1, paths.Count);
        Check.Equal("F:/Steam Library", paths[0]);
    }),
    ("VDF rejects truncated and ambiguous manifests", () =>
    {
        Check.Throws<IOException>(() => SteamVdf.ReadLibraryPaths("\"libraryfolders\" { \"0\" \"unterminated"));
        Check.Throws<IOException>(() => SteamVdf.ReadAppManifest("\"AppState\" { \"installdir\" \"one\" \"installdir\" \"two\" }"));
        Check.Throws<IOException>(() => SteamVdf.ReadAppManifest("\"AppState\" {} }"));
    }),
    ("Discovery follows a separate library using manifest installdir, and deduplicates libraries", () =>
    {
        using var fixture = new Fixture();
        var steam = fixture.Subdirectory("Steam root");
        var library = fixture.Subdirectory("Another drive library 洗衣");
        var game = fixture.CreateGame(library, "Custom Laundry name");
        Fixture.Write(Path.Combine(steam, "steam.exe"), "steam");
        Fixture.Write(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"0\" {{ \"path\" {Quote(steam)} }} \"1\" {{ \"path\" {Quote(library)} }} }}");
        var report = SteamDiscovery.DiscoverFromRoots([steam, steam], fixture.Profile);
        Check.Equal(1, report.Installations.Count);
        Check.Equal(game, report.Installations[0].GameDirectory);
        Check.True(report.Installations[0].Game.CanInstall);
        Check.Equal(Path.Combine(steam, "steam.exe"), report.Installations[0].SteamExecutable);
    }),
    ("A broken library does not hide a later valid installation", () =>
    {
        using var fixture = new Fixture();
        var steam = fixture.Subdirectory("steam");
        var bad = fixture.Subdirectory("bad");
        var good = fixture.Subdirectory("good");
        fixture.CreateGame(good);
        Fixture.Write(Path.Combine(bad, "steamapps", "appmanifest_3150440.acf"), "\"AppState\" { garbage");
        Fixture.Write(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"1\" {Quote(bad)} \"2\" {Quote(good)} }}");
        var report = SteamDiscovery.DiscoverFromRoots([steam], fixture.Profile);
        Check.Equal(1, report.Installations.Count);
        Check.True(report.Problems.Any(p => p.Contains("bad", StringComparison.Ordinal)));
    }),
    ("An invalid main library list still permits direct and legacy discovery", () =>
    {
        using var fixture = new Fixture();
        var steam = fixture.Subdirectory("steam");
        var legacy = fixture.Subdirectory("legacy");
        fixture.CreateGame(legacy);
        Fixture.Write(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "malformed {");
        Fixture.Write(Path.Combine(steam, "config", "libraryfolders.vdf"), $"\"libraryfolders\" {{ \"1\" {Quote(legacy)} }}");
        var report = SteamDiscovery.DiscoverFromRoots([steam], fixture.Profile);
        Check.Equal(1, report.Installations.Count);
        Check.True(report.Problems.Any(p => p.Contains("游戏库列表", StringComparison.Ordinal)));
    }),
    ("Incomplete Steam state blocks discovered and manually selected installations", () =>
    {
        using var fixture = new Fixture();
        var library = fixture.Subdirectory("steam");
        var game = fixture.CreateGame(library, state: "1028");
        Check.False(SteamDiscovery.DiscoverFromRoots([library], fixture.Profile).Installations[0].Game.CanInstall);
        Check.False(GameInspector.Probe(game, fixture.Profile, () => false).CanInstall);
    }),
    ("Pending Steam downloads, staging, and zero installed size block an otherwise finished state", () =>
    {
        using var fixture = new Fixture();
        var library = fixture.Subdirectory("steam");
        var game = fixture.CreateGame(library);
        foreach (var fields in new[]
        {
            "\"SizeOnDisk\" \"0\"",
            "\"SizeOnDisk\" \"1000\" \"BytesToDownload\" \"100\" \"BytesDownloaded\" \"90\"",
            "\"SizeOnDisk\" \"1000\" \"BytesToStage\" \"100\" \"BytesStaged\" \"90\""
        })
        {
            Fixture.Write(Path.Combine(library, "steamapps", "appmanifest_3150440.acf"),
                $"\"AppState\" {{ \"appid\" \"3150440\" \"installdir\" \"Laundry Store Simulator\" \"StateFlags\" \"4\" {fields} }}");
            Check.False(GameInspector.Probe(game, fixture.Profile, () => false).CanInstall);
            Check.False(SteamDiscovery.DiscoverFromRoots([library], fixture.Profile).Installations[0].Game.CanInstall);
        }
    }),
    ("A manually chosen nested Steam game folder still honors its manifest state", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame(name: "nested/Laundry Store Simulator", state: "1028");
        Check.False(GameInspector.Probe(game, fixture.Profile, () => false).CanInstall);
    }),
    ("Incomplete files and unsupported hashes are clearly rejected", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        Fixture.Write(Path.Combine(game, "GameAssembly.dll"), "new game build");
        var report = GameInspector.Probe(game, fixture.Profile, () => false);
        Check.False(report.CanInstall);
        Check.False(report.IsCompatible);
        Check.True(report.Problems.Any(p => p.Contains("尚未验证", StringComparison.Ordinal)));
        File.Delete(Path.Combine(game, GameInspector.MetadataRelativePath));
        Check.True(GameInspector.Probe(game, fixture.Profile, () => false).Problems.Any(p => p.Contains("缺少", StringComparison.Ordinal)));
    }),
    ("Manifest traversal and alternate data streams are rejected", () =>
    {
        using var fixture = new Fixture();
        foreach (var install in new[] { "../outside", @"..\outside", "game:evil", "game.", "game /child" })
        {
            var library = fixture.Subdirectory(Guid.NewGuid().ToString("N"));
            fixture.WriteManifest(library, install, "4");
            var report = SteamDiscovery.DiscoverFromRoots([library], fixture.Profile);
            Check.Equal(0, report.Installations.Count);
            Check.True(report.Problems.Any(p => p.Contains("不安全", StringComparison.Ordinal)));
        }
    }),
    ("Installation preserves unrelated mods and configuration, with idempotent loader reuse", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        Fixture.Write(Path.Combine(game, "Mods", "OtherMod.dll"), "keep-other-mod");
        Fixture.Write(Path.Combine(game, "UserData", "MelonPreferences.cfg"), "keep-settings");
        var result = fixture.Install(payload, trainer, game);
        Check.True(result.LoaderInstalled);
        Check.True(result.TrainerUpdated);
        Check.Equal("new-trainer", File.ReadAllText(result.TrainerPath));
        Check.Equal("keep-other-mod", File.ReadAllText(Path.Combine(game, "Mods", "OtherMod.dll")));
        Check.Equal("keep-settings", File.ReadAllText(Path.Combine(game, "UserData", "MelonPreferences.cfg")));
        Fixture.Write(Path.Combine(game, "MelonLoader", "Il2CppAssemblies", "generated.dll"), "generated");
        Fixture.Write(Path.Combine(game, "MelonLoader", "Latest.log"), "runtime log");
        var repeat = fixture.Install(payload, trainer, game);
        Check.False(repeat.LoaderInstalled);
        Check.False(repeat.TrainerUpdated);
        Check.True(repeat.BackupPath is null);
        Check.Equal("generated", File.ReadAllText(Path.Combine(game, "MelonLoader", "Il2CppAssemblies", "generated.dll")));
    }),
    ("Trainer replacement atomically retains a verified previous DLL backup", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        Fixture.Write(Path.Combine(game, "Mods", "LaundryTrainer.dll"), "old-trainer");
        var result = fixture.Install(payload, trainer, game);
        Check.True(result.BackupPath is not null);
        Check.True(result.BackupPath!.StartsWith(Path.Combine(game, "UserData", "LaundryTrainer", "Backups"), StringComparison.OrdinalIgnoreCase));
        Check.Equal("old-trainer", File.ReadAllText(result.BackupPath));
        Check.Equal("new-trainer", File.ReadAllText(result.TrainerPath));
    }),
    ("Conflicting loader and incomplete pinned loader block all writes", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        Fixture.Write(Path.Combine(game, "version.dll"), "some-other-loader");
        var before = fixture.Snapshot(game);
        Check.Throws<IOException>(() => fixture.Install(payload, trainer, game));
        Check.Equal(before, fixture.Snapshot(game));
        Fixture.Write(Path.Combine(game, "version.dll"), "bootstrap");
        before = fixture.Snapshot(game);
        Check.Throws<IOException>(() => fixture.Install(payload, trainer, game));
        Check.Equal(before, fixture.Snapshot(game));
    }),
    ("Other injection entry points block installation without changing game files", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        Fixture.Write(Path.Combine(game, "winhttp.dll"), "other-loader");
        var before = fixture.Snapshot(game);
        Check.Throws<IOException>(() => fixture.Install(payload, trainer, game));
        Check.Equal(before, fixture.Snapshot(game));
    }),
    ("A failure before activation removes only this install's new loader and trainer files", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        Fixture.Write(Path.Combine(game, "Mods", "OtherMod.dll"), "keep");
        var before = fixture.Snapshot(game);
        Check.Throws<IOException>(() => fixture.Install(payload, trainer, game,
            new CallbackProgress(message => { if (message.StartsWith("正在启用", StringComparison.Ordinal)) throw new IOException("simulated failure"); })));
        Check.Equal(before, fixture.Snapshot(game));
    }),
    ("Rollback restores an old trainer after a failure following its replacement", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        Fixture.Write(Path.Combine(game, "Mods", "LaundryTrainer.dll"), "old-trainer");
        Check.Throws<IOException>(() => fixture.Install(payload, trainer, game,
            new CallbackProgress(message => { if (message.StartsWith("正在启用", StringComparison.Ordinal)) throw new IOException("simulated activation failure"); })));
        Check.Equal("old-trainer", File.ReadAllText(Path.Combine(game, "Mods", "LaundryTrainer.dll")));
        Check.False(File.Exists(Path.Combine(game, "version.dll")));
        Check.False(Directory.Exists(Path.Combine(game, "MelonLoader")));
        Check.False(Directory.EnumerateFiles(Path.Combine(game, "Mods")).Any(p => p.EndsWith(".tmp", StringComparison.Ordinal)));
    }),
    ("Access denied remains eligible for elevation only after a clean rollback", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        var before = fixture.Snapshot(game);
        Check.Throws<UnauthorizedAccessException>(() => fixture.Install(payload, trainer, game,
            new CallbackProgress(message => { if (message.StartsWith("正在启用", StringComparison.Ordinal)) throw new UnauthorizedAccessException("simulated access denied"); })));
        Check.Equal(before, fixture.Snapshot(game));
    }),
    ("A failed rollback preserves an externally changed file and does not trigger elevation", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        Check.Throws<IOException>(() => fixture.Install(payload, trainer, game,
            new CallbackProgress(message =>
            {
                if (!message.StartsWith("正在启用", StringComparison.Ordinal)) return;
                Fixture.Write(Path.Combine(game, "MelonLoader", "net6", "MelonLoader.dll"), "external change");
                throw new UnauthorizedAccessException("simulated access denied");
            })));
        Check.Equal("external change", File.ReadAllText(Path.Combine(game, "MelonLoader", "net6", "MelonLoader.dll")));
        Check.False(File.Exists(Path.Combine(game, "version.dll")));
    }),
    ("A game starting during installation causes rollback before bootstrap activation", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        var before = fixture.Snapshot(game);
        var running = false;
        var progress = new CallbackProgress(message => { if (message.StartsWith("正在安装 MelonLoader", StringComparison.Ordinal)) running = true; });
        Check.Throws<IOException>(() => TrainerInstaller.Install(payload, trainer, game, fixture.Profile, progress, () => running));
        Check.Equal(before, fixture.Snapshot(game));
    }),
    ("Running game and unsupported build cause no writes", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        var before = fixture.Snapshot(game);
        Check.Throws<IOException>(() => TrainerInstaller.Install(payload, trainer, game, fixture.Profile, runningCheck: () => true));
        Check.Equal(before, fixture.Snapshot(game));
        Fixture.Write(Path.Combine(game, "GameAssembly.dll"), "unsupported update");
        before = fixture.Snapshot(game);
        Check.Throws<IOException>(() => fixture.Install(payload, trainer, game));
        Check.Equal(before, fixture.Snapshot(game));
    }),
    ("An unexpected payload file is refused before game writes", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        Fixture.Write(Path.Combine(payload, "unrelated.dll"), "bad");
        var before = fixture.Snapshot(game);
        Check.Throws<IOException>(() => fixture.Install(payload, trainer, game));
        Check.Equal(before, fixture.Snapshot(game));
    }),
    ("A file blocking the backup directory is caught before loader writes", () =>
    {
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        Fixture.Write(Path.Combine(game, "Mods", "LaundryTrainer.dll"), "old-trainer");
        Fixture.Write(Path.Combine(game, "UserData"), "not-a-directory");
        var before = fixture.Snapshot(game);
        Check.Throws<IOException>(() => fixture.Install(payload, trainer, game));
        Check.Equal(before, fixture.Snapshot(game));
    }),
    ("A linked Mods directory is refused without writing through the junction", () =>
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        var game = fixture.CreateGame();
        var (payload, trainer) = fixture.CreatePayload();
        var outside = fixture.Subdirectory("Outside target");
        Fixture.Write(Path.Combine(outside, "keep.txt"), "preserve");
        var junction = Path.Combine(game, "Mods");
        var command = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = $"/d /c mklink /J \"{junction}\" \"{outside}\""
        };
        using var process = System.Diagnostics.Process.Start(command)!;
        process.WaitForExit();
        Check.Equal(0, process.ExitCode);
        try
        {
            Check.Throws<IOException>(() => fixture.Install(payload, trainer, game));
            Check.Equal("preserve", File.ReadAllText(Path.Combine(outside, "keep.txt")));
            Check.Equal(1, Directory.EnumerateFiles(outside).Count());
            Check.False(Directory.Exists(Path.Combine(game, "MelonLoader")));
            Check.False(File.Exists(Path.Combine(game, "version.dll")));
        }
        finally
        {
            // Delete just this test-created junction, never its target or a recursive shell path.
            Directory.Delete(junction, false);
        }
    })
};

var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} installer tests passed.");
return failed == 0 ? 0 : 1;

static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

sealed class Fixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "LaundryTrainer.Setup.Tests", Guid.NewGuid().ToString("N"));
    internal CompatibilityProfile Profile { get; } = new(Hash("native-game"), Hash("metadata-game"));

    internal Fixture() => Directory.CreateDirectory(Root);
    internal string Subdirectory(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    internal string CreateGame(string? library = null, string name = "Laundry Store Simulator", string state = "4")
    {
        library ??= Subdirectory("Steam library 游戏");
        var path = Path.Combine(library, "steamapps", "common", name);
        Write(Path.Combine(path, GameInspector.GameExecutableName), "game-exe");
        Write(Path.Combine(path, "GameAssembly.dll"), "native-game");
        Write(Path.Combine(path, GameInspector.MetadataRelativePath), "metadata-game");
        WriteManifest(library, name, state);
        return path;
    }

    internal void WriteManifest(string library, string install, string state) =>
        Write(Path.Combine(library, "steamapps", "appmanifest_3150440.acf"),
            $"\"AppState\" {{ \"appid\" \"3150440\" \"installdir\" \"{install.Replace("\\", "\\\\")}\" \"StateFlags\" \"{state}\" \"SizeOnDisk\" \"12345\" }}");

    internal (string Payload, string Trainer) CreatePayload()
    {
        var payload = Subdirectory("payload");
        Write(Path.Combine(payload, "version.dll"), "bootstrap");
        Write(Path.Combine(payload, "MelonLoader", "net6", "MelonLoader.dll"), "pinned-loader");
        Write(Path.Combine(payload, "MelonLoader", "Dependencies", "support.dll"), "support");
        var trainer = Path.Combine(Root, "LaundryTrainer.dll");
        Write(trainer, "new-trainer");
        return (payload, trainer);
    }

    internal InstallResult Install(string payload, string trainer, string game, IProgress<string>? progress = null) =>
        TrainerInstaller.Install(payload, trainer, game, Profile, progress, () => false);

    internal string Snapshot(string directory) => string.Join("\n", Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));

    internal static void Write(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public void Dispose()
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "LaundryTrainer.Setup.Tests")) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Root);
        if (!path.StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path.");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}

sealed class CallbackProgress(Action<string> callback) : IProgress<string>
{
    public void Report(string value) => callback(value);
}

static class Check
{
    internal static void True(bool condition) { if (!condition) throw new Exception("Expected true."); }
    internal static void False(bool condition) => True(!condition);
    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}.");
    }
    internal static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
