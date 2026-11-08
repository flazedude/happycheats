using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;

var game=args.FirstOrDefault() ?? @"G:\Games\SteamLib\steamapps\common\Livin' the Cream Ice Cream Simulator";
string? Option(string key){var i=Array.IndexOf(args,key);return i>=0 && i+1<args.Length?args[i+1]:null;}
const string assemblyHash="c6081c74041250a89bf8cb943b5e482af8a05a15853dc727193ffd6fcf51f168";
const string metadataHash="f19c0af79b93122307211e81d2f2c2deaeb09f908670643f687f83773062d137";
var binary=Path.Combine(game,"GameAssembly.dll");
var metadata=Path.Combine(game,"Livin' the Cream Ice Cream Simulator_Data/il2cpp_data/Metadata/global-metadata.dat");
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
void Check(bool ok,string message){if(!ok)throw new InvalidDataException("Livin Cream binding check: "+message);}
Check(Hash(binary)==assemblyHash,"unsupported native assembly");Check(Hash(metadata)==metadataHash,"unsupported metadata");
var metaBytes=File.ReadAllBytes(metadata);Check(BitConverter.ToUInt32(metaBytes,0)==0xfab11baf && BitConverter.ToInt32(metaBytes,4)==39,"metadata schema 39");
using var file=File.OpenRead(binary);using var pe=new PEReader(file);Check(pe.PEHeaders.CoffHeader.Machine==System.Reflection.PortableExecutable.Machine.Amd64,"native x64 image");
byte[] Bytes(int rva,int count)=>pe.GetSectionData(rva).GetContent(0,count).ToArray();
int I32(byte[] bytes,int offset)=>BitConverter.ToInt32(bytes,offset);
var export=Bytes(pe.PEHeaders.PEHeader!.ExportTableDirectory.RelativeVirtualAddress,40);int names=I32(export,24),table=I32(export,32);Check(names>0 && names<10000,"export table bounds");
var exports=new HashSet<string>();var pointers=Bytes(table,names*4);
for(int i=0;i<names;i++){var text=pe.GetSectionData(I32(pointers,i*4)).GetContent().ToArray();exports.Add(Encoding.ASCII.GetString(text,0,Array.IndexOf(text,(byte)0)));}
var requiredExports=new[]{"domain_get","thread_attach","thread_detach","domain_get_assemblies","assembly_get_image","image_get_name","class_from_name","class_get_type","type_get_object","class_get_methods","method_get_name","method_get_param_count","method_is_generic","method_get_param","type_get_name","free","runtime_invoke","object_unbox","object_get_class","class_get_field_from_name","field_get_value","string_new","array_length"};
foreach(var name in requiredExports)Check(exports.Contains("il2cpp_"+name),"missing API export: "+name);
var record=Option("--record");
if(record!=null){
 Check(!File.Exists(record),"do not overwrite existing reviewed evidence");
 var dummy=Option("--dummy") ?? throw new ArgumentException("--dummy requires attributed Cpp2IL inspection output");
 using var managed=AssemblyDefinition.ReadAssembly(Path.Combine(dummy,"Assembly-CSharp.dll"));
 using var unity=AssemblyDefinition.ReadAssembly(Path.Combine(dummy,"UnityEngine.CoreModule.dll"));
 var bindings=new List<object>();var hookRvas=new Dictionary<string,uint>();
 var targets=new (string Type,string Method,string[] Parameters,string? Hook)[]{
  ("GameInput","Tick",[],"Tick"),("Customer","StartPatienceDuration",["System.Single"],"Patience"),
  ("SoftServeStock","DecreaseStock",["System.Single"],"Consume"),("ShopSaveManager","MarkRestorePending",[],"Restore"),
  ("ShopSaveManager","get_RestoreComplete",[],null),("Player","GetCurrentShop",[],null),
  ("makif.GlobalData","get_Database",[],null),("makif.MoneyKit.Money","SetMoney",["System.Single","System.String","System.Boolean"],null),
  ("makif.MoneyKit.Money","GetMoney",[],null),("LevelManager","AddXP",["System.Single"],null),("LevelManager","GetCurrentXP",[],null),
  ("LevelManager","GetCurrentLevel",[],null),("LevelManager","GetXPRequiredForLevel",["System.Int32"],null),
  ("SkillTreeManager","EarnSkillPoint",["System.Int32"],null),("SkillTreeManager","GetAvailablePoints",[],null),
  ("PlayStyleRules","get_Current",[],null),("Shop","OnPlayStyleChanged",["PlayStyle"],null),
  ("SoftServeStock","GetStockAmount",[],null),("SoftServeStock","get_MaxStock",[],null),("SoftServeStock","RestoreStock",["System.Single"],null),
  ("Customer","StopPatience",[],null),("Customer","get_HasActiveOrder",[],null),("OrderUI","HidePatience",[],null),
  ("UnityEngine.Object","FindObjectsByType",["System.Type","UnityEngine.FindObjectsSortMode"],null),
  ("UnityEngine.Object","GetInstanceID",[],null),("UnityEngine.Behaviour","get_isActiveAndEnabled",[],null),
  ("UnityEngine.Component","get_gameObject",[],null),("UnityEngine.GameObject","get_scene",[],null)};
 uint Rva(MethodDefinition method){var attr=method.CustomAttributes.SingleOrDefault(a=>a.AttributeType.Name=="AddressAttribute");if(attr==null)return 0;var value=attr.Fields.Single(x=>x.Name=="RVA").Argument.Value.ToString()!;return Convert.ToUInt32(value.Replace("0x",""),16);}
 foreach(var target in targets){
  var owner=target.Type.StartsWith("UnityEngine.",StringComparison.Ordinal)?unity:managed;
  var type=owner.MainModule.Types.Single(t=>t.FullName==target.Type);
  var method=type.Methods.Single(m=>m.Name==target.Method && !m.HasGenericParameters && m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(target.Parameters));
  uint rva=Rva(method);Check(rva!=0,"missing native method: "+target.Type+"."+target.Method);
  bindings.Add(new { type=target.Type,method=target.Method,parameters=target.Parameters,result=method.ReturnType.FullName,rva,prefix=Convert.ToHexString(Bytes((int)rva,24)).ToLowerInvariant() });
  if(target.Hook!=null){
   var aliases=managed.MainModule.Types.SelectMany(t=>t.Methods).Where(m=>m!=method && Rva(m)==rva).ToArray();
   Check(aliases.Length==0,"folded hook method would affect unrelated controls: "+target.Method);
   hookRvas[target.Hook]=rva;
  }
 }
 var fields=new[]{("Shop","currentCustomers"),("Shop","orderUI"),("SkillTreeManager","DB"),("makif.DatabaseSystem.Database","InventoryDatabase"),("makif.DatabaseSystem.Database","ProgressDatabase"),("makif.DatabaseSystem.Database","SkillPointDatabase"),("makif.DatabaseSystem.SkillPointDatabase","SkillPoint"),("makif.DatabaseSystem.SkillPointDatabase","TotalEarned")};
 foreach(var (type,name) in fields)Check(managed.MainModule.Types.Single(t=>t.FullName==type).Fields.Any(f=>f.Name==name),"missing field: "+type+"."+name);
 var evidence=new {steam_app_id="4621810",steam_build="26511230",game_assembly_sha256=assemblyHash,metadata_sha256=metadataHash,metadata_version=39,inspection_tool="Cpp2IL 2022.1.0-pre-release.21",inspection_tool_sha256="663fb432433b4371fd1ee0ebc321a8fff2a9aac5ac4230c843f9e03ddee4e04c",bindings,fields=fields.Select(f=>new{type=f.Item1,field=f.Item2}),live_tested=false};
 Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(record))!);File.WriteAllText(record,JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
 var header=Option("--header") ?? throw new ArgumentException("--header required when recording bindings");Check(!File.Exists(header),"do not overwrite a reviewed native header");
 var content=new StringBuilder("#pragma once\n#include <cstdint>\nnamespace bindings {\n");
 content.AppendLine($"inline constexpr const char* AssemblyHash=\"{assemblyHash}\";\ninline constexpr const char* MetadataHash=\"{metadataHash}\";");
 foreach(var pair in hookRvas)content.AppendLine($"inline constexpr uintptr_t {pair.Key}=0x{pair.Value:X};");
 content.AppendLine("struct Sentinel{uintptr_t rva;unsigned char bytes[24];};\ninline constexpr Sentinel sentinels[]={");
 foreach(var pair in hookRvas)content.AppendLine("{"+pair.Key+",{"+string.Join(",",Bytes((int)pair.Value,24).Select(b=>"0x"+b.ToString("x2")))+"}},");
 content.AppendLine("};\n}");Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(header))!);File.WriteAllText(header,content.ToString());
 Console.WriteLine($"Recorded {bindings.Count} native methods, {fields.Length} fields and four non-folded hook sites. No game executed.");
}else{
 var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
 using var json=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"tools/InspectLivinCream/verified-build.json")));
 var data=json.RootElement;Check(data.GetProperty("game_assembly_sha256").GetString()==assemblyHash && data.GetProperty("metadata_sha256").GetString()==metadataHash,"evidence identity");
 foreach(var binding in data.GetProperty("bindings").EnumerateArray()){int rva=binding.GetProperty("rva").GetInt32();Check(Convert.ToHexString(Bytes(rva,24)).Equals(binding.GetProperty("prefix").GetString(),StringComparison.OrdinalIgnoreCase),"native prologue drift");}
 Console.WriteLine($"PASS Livin Cream build 26511230: IL2CPP v39, native/metadata hashes, {data.GetProperty("bindings").GetArrayLength()} method sites and {requiredExports.Length} runtime exports; no game launched.");
}
