using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using HappyCheats.Core;
using HappyCheats.Core.Cheats;
using HappyCheats.Core.Updates;

try {
    if (args.Length != 2) throw new ArgumentException("Usage: AddNomadDrive <official-public-key.pem> <existing-client-version>");
    // Pin the established public signing identity; a user-supplied replacement key is never trusted.
    var key = File.ReadAllText(args[0]);
    using var identity = RSA.Create();
    identity.ImportFromPem(key);
    var digest = Convert.ToHexString(SHA256.HashData(identity.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
    if (digest != "39e6cea79d18fadabcc400754b24e77c01321be729a712a89f9304cd7e5af1eb")
        throw new IOException("This is not the established HappyCheats public key.");
    var clientVersion = args[1];
    if (ReleaseVersion.Parse(clientVersion).CompareTo(ReleaseVersion.Parse("0.3.65")) < 0)
        throw new IOException("Install HappyCheats 0.3.65 or newer once before adding independent games.");
    void RequireClosed() {
        using var client = Process.GetProcessesByName("HappyCheats").FirstOrDefault();
        if (client != null) throw new IOException("Close HappyCheats before adding Nomad Drive, then run this helper again.");
        foreach (var module in ModuleCatalog.Create()) {
            foreach (var executable in module.GameExecutables) {
                var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
                try { if (processes.Length != 0) throw new IOException("Close supported games before adding modules: " + module.Definition.Name); }
                finally { foreach (var process in processes) process.Dispose(); }
            }
        }
    }
    RequireClosed();
    var configuration = new CheatFeedConfiguration(key, "stable", [
        "http://192.168.2.76:17845/happycheats/cheats.json",
        "http://192.168.2.59:17845/happycheats/cheats.json", CheatFeedConfiguration.GitHubFeed]);
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    var updater = new CheatUpdateClient(http, configuration);
    var store = new CheatStore(CheatStore.DefaultRoot, configuration);
    var check = await updater.CheckAsync(store.Root);
    var release = check.Catalog.Games.SingleOrDefault(x => x.Id == "nomad-drive")
        ?? throw new IOException("No signed Nomad Drive package is published yet.");
    CheatStore.RequireCompatible(release, clientVersion);
    RequireClosed();
    await updater.InstallAsync(check, release, store, clientVersion);
    RequireClosed();
    var downloaded = store.LoadInstalled(release, clientVersion);
    try {
        var installation = await downloaded.DiscoverAsync();
        if (!installation.IsInstalled || !installation.Compatible || installation.IsRunning)
            throw new IOException("Signed module added; game setup needs attention: " + installation.Message);
        RequireClosed();
        await downloaded.InstallAsync(installation);
        Console.WriteLine($"Nomad Drive {release.Version} added and installed. Open your existing HappyCheats EXE; select Nomad Drive. No game was launched and no save was changed.");
    } finally { CheatStore.ReleaseModule(downloaded); }
    return 0;
} catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
