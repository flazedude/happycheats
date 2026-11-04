using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

var game = args.FirstOrDefault() ?? @"G:\Games\SteamLib\steamapps\common\Livin' the Cream Ice Cream Simulator";
var path = Path.Combine(game, "Livin' the Cream Ice Cream Simulator_Data", "Managed", "Assembly-CSharp.dll");
var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
const string expected = "3756614fb3952a6bacf1e2e8d1994efd7eb451d7f27218a66b033e386e3cb98e";
using var assembly = AssemblyDefinition.ReadAssembly(path);
var checks = new List<string>();
void Check(bool ok, string name) { if (!ok) throw new InvalidDataException("Game binding changed: " + name); checks.Add(name); }
TypeDefinition Type(string name) => assembly.MainModule.Types.Single(t => t.FullName == name);
MethodDefinition Method(string type, string name, string result, params string[] parameters)
{
    var method = Type(type).Methods.Single(m => m.Name == name && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters));
    Check(method.ReturnType.FullName == result, type + "." + name);
    return method;
}
void Calls(MethodDefinition method, string target) => Check(method.Body.Instructions.Any(i => i.Operand is MethodReference m && m.FullName.Contains(target, StringComparison.Ordinal)), method.Name + " calls " + target);
Check(hash == expected, "exact inspected assembly SHA-256");
var setMoney = Method("makif.MoneyKit.Money", "SetMoney", "System.Void", "System.Single", "System.String", "System.Boolean");
Check(setMoney.IsPublic && setMoney.IsStatic, "public static money setter");
Check(setMoney.Body.Instructions.Any(i => i.Operand is FieldReference f && f.FullName.Contains("InventoryDatabase::Money")), "money setter targets live inventory");
var xp = Method("LevelManager", "AddXP", "System.Void", "System.Single");
Calls(xp, "LevelConfig::GetXPRequired"); Calls(xp, "LevelManager::set_CurrentLevel");
Check(xp.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "OnLevelUp"), "native level-up notifications");
Method("LevelManager", "GetXPRequiredForLevel", "System.Single", "System.Int32");
Calls(Method("SkillTreeManager", "EarnSkillPoint", "System.Void", "System.Int32"), "SkillPointDatabase::EarnPoint");
Method("SkillTreeManager", "GetAvailablePoints", "System.Int32");
Method("ShopSaveManager", "MarkRestorePending", "System.Void");
Method("ShopSaveManager", "get_RestoreComplete", "System.Boolean");
Method("Player", "GetCurrentShop", "Shop");
Method("Shop", "OnPlayStyleChanged", "System.Void", "PlayStyle");
Method("Customer", "StopPatience", "System.Void");
Calls(Method("Shop", "OnPlayStyleChanged", "System.Void", "PlayStyle"), "Customer::StopPatience");
Calls(Method("Shop", "OnPlayStyleChanged", "System.Void", "PlayStyle"), "Customer::StartPatienceDuration");
Method("PlayStyleRules", "get_CustomersHavePatience", "System.Boolean");
Method("SoftServeStock", "DecreaseStock", "System.Void", "System.Single");
Calls(Method("SoftServeStock", "RestoreStock", "System.Void", "System.Single"), "SoftServeStock::SetCurrentLiquidAmount");
Method("SoftServeStock", "get_MaxStock", "System.Single");
Method("SoftServeStock", "GetStockAmount", "System.Single");
foreach (var (type, field) in new[] { ("makif.DatabaseSystem.Database", "InventoryDatabase"), ("makif.DatabaseSystem.Database", "ProgressDatabase"), ("makif.DatabaseSystem.Database", "SkillPointDatabase"), ("makif.DatabaseSystem.SkillPointDatabase", "SkillPoint"), ("makif.DatabaseSystem.SkillPointDatabase", "TotalEarned") })
    Check(Type(type).Fields.Any(f => f.Name == field && f.IsPublic), type + "." + field);
Console.WriteLine(JsonSerializer.Serialize(new { assembly_sha256 = hash, mvid = assembly.MainModule.Mvid, steam_build = "26415994", checks, live_tested = false }, new JsonSerializerOptions { WriteIndented = true }));
