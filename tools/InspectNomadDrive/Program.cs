using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

var path = args.FirstOrDefault() ?? @"G:\Games\SteamLib\steamapps\common\Nomad Drive\Nomad Drive_Data\Managed\Assembly-CSharp.dll";
using var assembly = AssemblyDefinition.ReadAssembly(path);
var contracts = new Dictionary<string, string[]> {
    ["NomadDrive.Features.Player.PlayerStatsManager"] = ["get_IsInitialized", "get_IsDead", "get_Nutrition", "get_Hydration", "get_Energy", "get_Health", "get_Poison", "ApplyMedicine", "HealDamage"],
    ["NomadDrive.Features.Player.PlayerStat"] = ["get_CurrentValue", "get_MaxValue", "SetMax"],
    ["NomadDrive.Features.Vehicle.VehicleManager"] = ["get_IsInitialized", "get_FuelContainer"],
    ["NomadDrive.Features.LiquidTransferSystem.LiquidContainerComponent"] = ["get_Capacity", "get_CurrentLiquidType", "SetAmount"],
    ["NomadDrive.Features.Vehicle.Modules.VehicleEngineModule"] = ["get_InstalledEngine"],
    ["NomadDrive.Features.Attachables.ConditionComponent"] = ["get_MaxCondition", "ServerSetCondition"]
};
var errors = new List<string>();
var bindings = new Dictionary<string, string[]>();
foreach (var (name, methods) in contracts) {
    var type = assembly.MainModule.GetType(name);
    foreach (var method in methods)
        if (type?.Methods.Any(m => m.Name == method && m.IsPublic) != true) errors.Add(name + "." + method);
    bindings[name] = type?.Methods.Where(m => methods.Contains(m.Name)).Select(m => m.FullName).ToArray() ?? [];
}
var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
if (hash != "6957F9A1180D811CA2AE1BB44748157D794FED5BEFD845290FD15E41FB54FFAF") errors.Add("Unreviewed assembly hash");
Console.WriteLine(JsonSerializer.Serialize(new { verified_date = "2026-11-14", steam_build = "26391437", assembly_sha256 = hash, mvid = assembly.MainModule.Mvid,
    static_contract_valid = errors.Count == 0, live_tested = false, errors, bindings }, new JsonSerializerOptions { WriteIndented = true }));
return errors.Count == 0 ? 0 : 1;
