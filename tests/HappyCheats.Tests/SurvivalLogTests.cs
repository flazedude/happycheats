using System.Text.Json;
using System.Text.Json.Nodes;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;
internal static class SurvivalLogTests
{
    private static int checks;
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly DateTime Started = Now.AddMinutes(-2);
    private const string Session = "survival-fixture-session";
    private const string Request = "0123456789abcdef0123456789abcdef";
    private static void Check(bool value) { checks++; if (!value) throw new Exception("Survival Log assertion " + checks); }
    private static void Reject(Action action) { try { action(); } catch { checks++; return; } throw new Exception("Expected Survival Log operation rejection."); }
    private static JsonElement Json(JsonObject value) => JsonDocument.Parse(value.ToJsonString()).RootElement.Clone();
    private static long Ms(DateTime time) => new DateTimeOffset(time).ToUnixTimeMilliseconds();
    private static JsonObject State() => new() {
        ["schema_version"] = 1, ["version"] = SurvivalLogModule.TrainerVersion, ["process_id"] = 773,
        ["process_start_unix_ms"] = Ms(Started), ["heartbeat_unix_ms"] = Ms(Now), ["session_id"] = Session,
        ["ready"] = true, ["message"] = "Fixture", ["enabled"] = new JsonObject(), ["values"] = new JsonObject(),
        ["available"] = new JsonObject(SurvivalLogModule.FeatureIds.Select(id => KeyValuePair.Create<string, JsonNode?>(id, JsonValue.Create(true)))),
        ["last_command"] = Request, ["command_result"] = new JsonObject { ["request_id"] = Request, ["success"] = true }
    };
    private static SurvivalLogModule.Snapshot Parse(JsonObject json) => SurvivalLogModule.ParseSnapshot(Json(json), Now, Started, 773, Now);
    public static int RunAll()
    {
        try {
            checks = 0;
            var module = new SurvivalLogModule(Path.GetTempPath());
            Check(module.GameExecutables.SequenceEqual(new[] { "SurvivalLog.exe" }));
            Check(module.Definition.AppId == "4164790" && module.Definition.Features.Count == 11);
            Check(module.Definition.Features.Count(x => x.IsToggle) == 7 && module.Definition.Features.Count(x => x.Number != null) == 3);
            var waiting = SurvivalLogModule.AwaitingConnection(773, Started, true);
            Check(waiting.Values["available_sl_connect"] == bool.TrueString);
            Check(SurvivalLogModule.FeatureIds.All(id => waiting.Values["available_" + id] == bool.FalseString));
            Check(Parse(State()).Status.Connected);
            foreach (var key in new[] { "schema_version", "version", "process_id", "process_start_unix_ms", "heartbeat_unix_ms", "session_id" }) {
                var malformed = State(); malformed.Remove(key); Check(!Parse(malformed).Status.Connected);
            }
            foreach (var pair in new (string Key, JsonNode? Value)[] { ("schema_version", JsonValue.Create(2)), ("version", JsonValue.Create("0.0.0")),
                ("process_id", JsonValue.Create(774)), ("process_start_unix_ms", JsonValue.Create(Ms(Started)+1)),
                ("heartbeat_unix_ms", JsonValue.Create(Ms(Now)-4000)), ("session_id", JsonValue.Create("../bad")) }) {
                var malformed=State();malformed[pair.Key]=pair.Value;Check(!Parse(malformed).Status.Connected);
            }
            Check(!SurvivalLogModule.ParseSnapshot(Json(State()), Now.AddSeconds(-4), Started, 773, Now).Status.Connected);
            Check(!SurvivalLogModule.ParseSnapshot(Json(State()), Started.AddTicks(-1), Started, 773, Now).Status.Connected);
            var menu=State();menu["ready"]=false;Check(Parse(menu).Status.Values["diagnostic_sl_initialization_failed"]==bool.FalseString);
            ((JsonObject)menu["values"]!)["initialization_error"]="Unsupported build";Check(Parse(menu).Status.Values["diagnostic_sl_initialization_failed"]==bool.TrueString);
            var ready = Parse(State()).Status;
            foreach(var feature in module.Definition.Features.Where(x => x.Number != null)) {
                var number=feature.Number!;
                var command=SurvivalLogModule.CreateCommand(ready,ready,feature.Id,null,number.DefaultValue,Request,Ms(Now));
                Check((decimal)command["value"]==number.DefaultValue);
                foreach(var invalid in new[] { number.Minimum-1,number.Maximum+1,number.Minimum+0.5m })
                    Reject(()=>SurvivalLogModule.CreateCommand(ready,ready,feature.Id,null,invalid,Request,Ms(Now)));
                Reject(()=>SurvivalLogModule.CreateCommand(ready,ready,feature.Id,true,number.DefaultValue,Request,Ms(Now)));
            }
            foreach(var toggle in SurvivalLogModule.ToggleIds) {
                Check((bool)SurvivalLogModule.CreateCommand(ready,ready,toggle,true,null,Request,Ms(Now))["enabled"]);
                Reject(()=>SurvivalLogModule.CreateCommand(ready,ready,toggle,null,null,Request,Ms(Now)));
                var unready=ready with { Ready=false,Values=new Dictionary<string,string>() };
                _=SurvivalLogModule.CreateCommand(unready,unready,toggle,false,null,Request,Ms(Now));checks++;
                Reject(()=>SurvivalLogModule.CreateCommand(unready,unready,toggle,true,null,Request,Ms(Now)));
            }
            _=SurvivalLogModule.CreateCommand(ready with {Ready=false},ready,"disable_all",null,null,Request,Ms(Now));checks++;
            Reject(()=>SurvivalLogModule.CreateCommand(ready,ready with {SessionId="other-session"},"sl_satiety",true,null,Request,Ms(Now)));
            Reject(()=>SurvivalLogModule.CreateCommand(ready,ready,"unknown",null,null,Request,Ms(Now)));
            Check(SurvivalLogModule.AcceptAcknowledgement(Parse(State()),Session,Request));
            var denied=State();((JsonObject)denied["command_result"]!)["success"]=false;Reject(()=>SurvivalLogModule.AcceptAcknowledgement(Parse(denied),Session,Request));
            Reject(()=>SurvivalLogModule.AcceptAcknowledgement(Parse(State()),"changed-session",Request));
            using var f = new Fixture();
            SurvivalLogInstaller.Install(f.Package,f.Game,()=>{});Check(SurvivalLogInstaller.MatchesPackage(f.Package,f.Game));
            File.AppendAllText(SafePath(f.Game,SurvivalLogInstaller.DllPath),"foreign edit");
            Reject(()=>SurvivalLogInstaller.Install(f.Package,f.Game,()=>{}));
            SurvivalLogInstaller.Remove(f.Game,()=>{});Check(File.ReadAllText(SafePath(f.Game,SurvivalLogInstaller.DllPath)).Contains("foreign edit"));
            using var corrupt = new Fixture();File.AppendAllText(SafePath(corrupt.Package,SurvivalLogInstaller.DllPath),"corrupt");
            Reject(()=>SurvivalLogInstaller.Install(corrupt.Package,corrupt.Game,()=>{}));Check(!Directory.EnumerateFiles(corrupt.Game).Any());
            using var rollback = new Fixture();int calls=0;
            Reject(()=>SurvivalLogInstaller.Install(rollback.Package,rollback.Game,()=>{if(++calls==3)throw new IOException("game started");}));
            Check(!Directory.EnumerateFiles(rollback.Game,"*",SearchOption.AllDirectories).Any());
            using var ownership = new Fixture();var dll=SafePath(ownership.Game,SurvivalLogInstaller.DllPath);Directory.CreateDirectory(Path.GetDirectoryName(dll)!);File.Copy(SafePath(ownership.Package,SurvivalLogInstaller.DllPath),dll);
            SurvivalLogInstaller.Install(ownership.Package,ownership.Game,()=>{});SurvivalLogInstaller.Remove(ownership.Game,()=>{});Check(File.Exists(dll));
            using var identity = new Fixture();File.WriteAllText(SafePath(identity.Game,"SurvivalLog.exe"),"fixture");File.WriteAllText(SafePath(identity.Game,"GameAssembly.dll"),"fixture");
            var metadata=SafePath(identity.Game,SurvivalLogModule.MetadataPath);Directory.CreateDirectory(Path.GetDirectoryName(metadata)!);File.WriteAllText(metadata,"fixture");
            var testModule=new SurvivalLogModule(identity.Root) {HashProvider=p=>p.EndsWith("GameAssembly.dll")?SurvivalLogModule.SupportedAssemblyHash:SurvivalLogModule.SupportedMetadataHash};
            Check(testModule.Compatible(identity.Game,true));testModule.HashProvider=_=>new string('0',64);Check(!testModule.Compatible(identity.Game,true));
            testModule.ProcessProvider=_=>[(773,Started)];testModule.AttachTrainer=_=>throw new Exception("Should never inject on discovery");
            _=testModule.DiscoverAsync(identity.Game).GetAwaiter().GetResult();checks++;
            Reject(()=>testModule.InstallAsync(new(identity.Game,"fixture",true,false,false,false,"")).GetAwaiter().GetResult());
            Console.WriteLine($"PASS Survival Log module: {checks} assertions; metadata, schema/session/time guards, numeric bounds, off while loading, acknowledgements, compatibility and installer ownership/rollback. No game launched.");
            return 0;
        } catch(Exception error) { Console.WriteLine("FAIL Survival Log: "+error);return 1; }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"HappyCheats-survival-fixture-"+Guid.NewGuid().ToString("N"));
        public string Package => Path.Combine(Root,"payload","survival-log");
        public string Game => Path.Combine(Root,"game");
        public Fixture() {
            Directory.CreateDirectory(Game);
            foreach(var file in SurvivalLogInstaller.RequiredFiles) {var path=SafePath(Package,file);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"fixture "+file);}
            WriteJson(SafePath(Package,"package.json"),new{schemaVersion=1,game="survival-log",version=SurvivalLogModule.TrainerVersion,files=SurvivalLogInstaller.RequiredFiles.Select(path=>new{path,sha256=Hash(SafePath(Package,path))})});
        }
        public void Dispose(){Directory.Delete(Root,true);}
    }
}
