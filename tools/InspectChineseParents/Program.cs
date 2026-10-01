using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

var path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
    ?? @"E:\Games\SteamLibrary\steamapps\common\ChineseParent\game_Data\Managed\Assembly-CSharp.dll";
if (Directory.Exists(path)) path = Path.Combine(path, "game_Data", "Managed", "Assembly-CSharp.dll");
if (!File.Exists(path)) { Console.Error.WriteLine("Pass an installed game folder or Assembly-CSharp.dll path."); return 2; }
using var assembly = AssemblyDefinition.ReadAssembly(path);
var module = assembly.MainModule;
var player = module.Types.FirstOrDefault(t => t.FullName == "NewPlayerDataMgr");
var record = module.Types.FirstOrDefault(t => t.FullName == "NewRecordMgr");
var expected = new[] { "Money", "Wisdom", "Potentiality", "Pressure", "Shadow", "Iq", "Eq", "Imagination", "Memory", "Stamination", "Charm", "Face" };
var errors = new List<string>();
if (player == null || record == null) errors.Add("Expected player/record managers were not found.");
if (player != null)
{
    if (player.BaseType?.FullName != "SingleTon`1<NewPlayerDataMgr>") errors.Add("Player singleton base has changed.");
    foreach (var name in expected)
    {
        var property = player.Properties.FirstOrDefault(p => p.Name == name);
        if (property?.PropertyType.FullName != "System.Single" || property.SetMethod?.IsPublic != true || property.GetMethod?.IsPublic != true)
            errors.Add("Missing public float getter/setter: " + name);
        if (property?.SetMethod?.Body?.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_OnMainInfoValueChange") != true)
            errors.Add("UI notification pattern changed: " + name);
    }
    foreach (var name in new[] { "CurTable", "Potential_max", "CurRound" })
        if (player.Properties.All(p => p.Name != name)) errors.Add("Missing readiness/limit property: " + name);
}
if (record != null)
    foreach (var name in new[] { "CurrentRecord", "CurrentRoleCoreInfo" })
        if (record.Properties.All(p => p.Name != name)) errors.Add("Missing record readiness property: " + name);
var singleton = module.Types.FirstOrDefault(t => t.FullName == "SingleTon`1");
if (singleton?.Fields.Any(f => f.Name == "m_instance" && f.IsStatic) != true) errors.Add("Non-instantiating singleton field is absent.");
var report = new
{
    inspected_utc = DateTimeOffset.UtcNow, assembly_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
    assembly = assembly.Name.FullName, mvid = module.Mvid, architecture = module.Architecture.ToString(),
    static_contract_valid = errors.Count == 0, live_tested = false, errors,
    player_properties = player?.Properties.Select(p => new { p.Name, type = p.PropertyType.FullName, public_setter = p.SetMethod?.IsPublic == true }),
    attribute_enum = module.Types.FirstOrDefault(t => t.Name == "EMainInfoType")?.Fields.Where(f => f.HasConstant).Select(f => new { f.Name, f.Constant })
};
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
if (args.Contains("--il") && player != null)
{
    foreach (var method in player.Methods.Where(m => expected.Select(p => "set_" + p).Append("get_Potential_max").Contains(m.Name)))
    {
        Console.WriteLine(method.FullName);
        foreach (var instruction in method.Body.Instructions) Console.WriteLine(instruction);
    }
}
return errors.Count == 0 ? 0 : 1;
