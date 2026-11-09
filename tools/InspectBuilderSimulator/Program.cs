using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

var path = args.FirstOrDefault() ?? @"F:\Games\steamapps\steamapps\common\Builder Simulator\Builder Simulator_Data\Managed\Assembly-CSharp.dll";
using var assembly = AssemblyDefinition.ReadAssembly(path);
var expected = "0786962D80AA8B6BFCE8561FCD1502C982B33F6856E59A5567864BB10DE7279D";
var contracts = new (string Type, string Name, string Return, string[] Parameters)[] {
    ("Core.Data.EquipmentData", "get_PlayerCash", "System.Int32", []),
    ("Core.Data.EquipmentData", "set_PlayerCash", "System.Void", ["System.Int32"]),
    ("Core.Data.EquipmentData", "RemoveFromConstructionItem", "System.Void", ["ConstructionObjectID", "System.Int32"]),
    ("Core.Data.EquipmentData", "RemoveFurnitureFromEquipment", "System.Void", ["System.String", "System.Int32", "System.Int32"]),
    ("Core.Data.EquipmentData", "RemoveFurnitureFromEquipment", "System.Void", ["System.String", "UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation"]),
    ("Builder.Mixer.WheelbarrowMix", "GetMix", "System.Single", ["System.Single"]),
    ("Builder.Mixer.WheelbarrowMixController", "GetMix", "System.Single", ["System.Single"]),
    ("ECM.Controllers.BaseFirstPersonController", "GetTargetSpeed", "System.Single", []),
    ("ECM.Controllers.BaseCharacterController", "get_jumpImpulse", "System.Single", []),
    ("Gameplay.SkillsSystem.SkillData", "LevelUp", "System.Void", []),
    ("Gameplay.SkillsSystem.UpgradesData", "get_skillsData", "System.Collections.Generic.Dictionary`2<Gameplay.SkillsSystem.Skill,Gameplay.SkillsSystem.SkillData>", []),
    ("Core.Managers.GameSceneManager", "get_GameSceneIsPrepared", "System.Boolean", []),
    ("Core.Managers.GameSceneManager", "get_IsLoadingSaveData", "System.Boolean", []),
    ("Core.ScenesLogic.SceneLoadingHandler", "get_PreventFromMultipleLoad", "System.Boolean", [])
};
var errors = new List<string>();
var bindings = new List<string>();
foreach (var contract in contracts) {
    var type = assembly.MainModule.GetType(contract.Type);
    var matching = type?.Methods.Where(m => m.Name == contract.Name && m.ReturnType.FullName == contract.Return
        && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(contract.Parameters)).ToArray() ?? [];
    if (matching.Length != 1 || !matching[0].HasBody) errors.Add(contract.Type + "." + contract.Name);
    else bindings.Add(matching[0].FullName);
}
var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
if (hash != expected) errors.Add("Unreviewed assembly hash");
Console.WriteLine(JsonSerializer.Serialize(new { steam_build = "26462579", assembly_sha256 = hash, mvid = assembly.MainModule.Mvid,
    static_contract_valid = errors.Count == 0, live_tested = false, errors, bindings }, new JsonSerializerOptions { WriteIndented = true }));
return errors.Count == 0 ? 0 : 1;
