using System.Text.Json;
using HappyCheats.Core;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

if (args.Length < 2) { Console.Error.WriteLine("Usage: HappyCheats.Cli <workspace-or-package> probe|status|install|remove|launch [module-id]"); return 2; }
try {
    var modules = ModuleCatalog.Create(Path.GetFullPath(args[0]));
    var selection = args.Length > 2 ? modules.Where(m => m.Definition.Id == args[2]).ToList() : modules.ToList();
    if (selection.Count == 0) throw new ArgumentException("Unknown game module.");
    if (args[1] is not ("probe" or "status") && selection.Count != 1) throw new ArgumentException("Choose a single module for this operation.");
    foreach (var module in selection) {
        var installation = await module.DiscoverAsync();
        if (args[1] == "probe") Console.WriteLine(JsonSerializer.Serialize(new { game = module.Definition.Id, installation }, new JsonSerializerOptions { WriteIndented = true }));
        else if (args[1] == "status") Console.WriteLine(JsonSerializer.Serialize(await module.ReadStatusAsync(installation), new JsonSerializerOptions { WriteIndented = true }));
        else if (args[1] == "install") await module.InstallAsync(installation, new Progress<string>(Console.WriteLine));
        else if (args[1] == "remove") await module.RemoveAsync(installation, new Progress<string>(Console.WriteLine));
        else if (args[1] == "launch") await module.LaunchAsync(installation);
        else throw new ArgumentException("Unknown operation.");
    }
    return 0;
} catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
