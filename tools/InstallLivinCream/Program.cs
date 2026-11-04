using System.Diagnostics;
using HappyCheats.Core;
using HappyCheats.Core.Cheats;
using HappyCheats.Core.Updates;

// First-add bridge for clients 0.3.68/0.3.69, whose update UI omits unknown games.
// Uses the frozen signed update store and per-game installer; never replaces the client.
try {
    string? Option(string key) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    var client = Path.GetFullPath(Option("--client") ?? throw new ArgumentException("Pass --client with your configured HappyCheats client directory."));
    var executable = ModuleIO.SafePath(client, "HappyCheats.exe");
    var versionText = FileVersionInfo.GetVersionInfo(executable).ProductVersion?.Split('+')[0] ?? "";
    var clientVersion = ReleaseVersion.Parse(versionText);
    if (clientVersion.CompareTo(ReleaseVersion.Parse(LivinCreamModule.MinimumClientVersion)) < 0) throw new IOException("Use the existing official HappyCheats 0.3.69+ standalone EXE once before adding this game; its shared client retains requested toggles through loading. This helper never replaces your EXE.");
    var configuration = UpdateConfiguration.Load(client) ?? throw new IOException("This client has no trusted update configuration. Use the official standalone EXE.");
    var trust = CheatFeedConfiguration.FromClient(configuration) ?? throw new IOException("Custom update settings are preserved. Select an official configured client for this setup.");
    if (trust.Channel != "stable") throw new IOException("Select a stable official client for this setup.");
    var game = ModuleIO.FindSteam("4621810", LivinCreamModule.Executable, Option("--game"));
    if (!string.Equals(LivinCreamModule.SupportedAssemblyHash, ModuleIO.Hash(ModuleIO.SafePath(game.Directory, LivinCreamModule.AssemblyPath)), StringComparison.OrdinalIgnoreCase))
        throw new IOException("Unsupported Livin' the Cream build. Check its trainer release before installing.");
    void RequireClosed() {
        if (ModuleIO.Processes(game.Directory, LivinCreamModule.Executable).Length != 0) throw new IOException("Close Livin' the Cream, then run setup again. No game was closed automatically.");
        foreach (var process in Process.GetProcessesByName("HappyCheats")) using (process)
            if (!process.HasExited) throw new IOException("Close HappyCheats, then run setup again so its Library can load the new module on startup.");
    }
    RequireClosed();
    Console.WriteLine($"Client {versionText}: {client}\nGame build {game.Build}: {game.Directory}");
    if (args.Contains("--inspect-only")) { Console.WriteLine("Read-only setup prerequisites verified. No download, game launch, cache or game file changes."); return 0; }
    using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    var store = new CheatStore(CheatStore.DefaultRoot, trust);
    var updater = new CheatUpdateClient(http, trust);
    var check = await updater.CheckAsync(store.Root, lifetime.Token);
    var release = check.Catalog.Games.SingleOrDefault(x => x.Id == "livin-cream") ?? throw new IOException("Livin' the Cream has not reached the signed catalog yet. Retry after publication completes.");
    CheatStore.RequireCompatible(release, versionText);
    RequireClosed();
    Console.WriteLine($"Downloading and verifying Livin' the Cream {release.Version} using the trusted catalog.");
    await updater.InstallAsync(check, release, store, versionText, null, lifetime.Token);
    RequireClosed();
    var module = store.LoadInstalled(release, versionText);
    try {
        var installation = await module.DiscoverAsync(game.Directory);
        if (!installation.IsInstalled || !installation.Compatible) throw new IOException(installation.Message);
        await module.InstallAsync(installation, new Progress<string>(Console.WriteLine));
        installation = await module.DiscoverAsync(game.Directory);
        if (!installation.TrainerInstalled || !installation.Compatible) throw new IOException("Trainer installation did not verify. Retry this setup to finish.");
        Console.WriteLine($"Installed and verified Livin' the Cream {release.Version}. Reopen your existing HappyCheats EXE and select the new Library game. No game was launched or saves changed.");
    } finally { CheatStore.ReleaseModule(module); }
    return 0;
} catch (Exception error) { Console.Error.WriteLine("Livin' the Cream setup: " + error.Message); return 1; }
