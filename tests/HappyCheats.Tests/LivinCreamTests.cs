using System.Text.Json;
using System.Text.Json.Nodes;
using HappyCheats.Core;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Tests;
internal static class LivinCreamTests
{
    private static int checks;
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly DateTime Started = Now.AddMinutes(-2);
    private const string ProcessSession = "0123456789abcdef0123456789abcdef";
    private const string Session = ProcessSession + "_1";
    private const string Request = "0123456789abcdef0123456789abcdef";
    private static void Check(bool value) { checks++; if (!value) throw new Exception("Livin Cream assertion " + checks); }
    private static void Reject(Action action) { try { action(); } catch { checks++; return; } throw new Exception("Expected Livin Cream operation rejection."); }
    private static JsonElement Json(JsonObject value) => JsonDocument.Parse(value.ToJsonString()).RootElement.Clone();
    private static long Ms(DateTime time) => new DateTimeOffset(time).ToUnixTimeMilliseconds();
    private static JsonObject State() => new() {
        ["schema_version"] = 1, ["version"] = LivinCreamModule.TrainerVersion, ["process_id"] = 773,
        ["process_start_unix_ms"] = Ms(Started), ["heartbeat_unix_ms"] = Ms(Now), ["session_id"] = Session,
        ["process_session"] = ProcessSession, ["ready"] = true, ["message"] = "Fixture", ["enabled"] = new JsonObject(), ["values"] = new JsonObject(),
        ["available"] = new JsonObject(LivinCreamModule.FeatureIds.Select(id => KeyValuePair.Create<string, JsonNode?>(id, JsonValue.Create(true)))),
        ["last_command"] = Request, ["command_result"] = new JsonObject { ["request_id"] = Request, ["success"] = true }
    };
    private static LivinCreamModule.Snapshot Parse(JsonObject json) => LivinCreamModule.ParseSnapshot(Json(json), Now, Started, 773, Now);
    public static int RunAll()
    {
        try {
            checks = 0;
            var module = new LivinCreamModule(Path.GetTempPath());
            Check(module.GameExecutables.SequenceEqual(new[] { LivinCreamModule.Executable }));
            Check(module.Definition.AppId == "4621810" && module.Definition.Features.Count == 7);
            Check(module.Definition.Features.Count(x => x.IsToggle) == 2 && module.Definition.Features.Count(x => x.Number != null) == 3);
            var waiting = LivinCreamModule.AwaitingConnection(773, Started, true);
            Check(waiting.Values["available_lc_connect"] == bool.TrueString);
            Check(LivinCreamModule.FeatureIds.All(id => waiting.Values["available_" + id] == bool.FalseString));
            Check(Parse(State()).Status.Connected);
            foreach (var key in new[] { "schema_version", "version", "process_id", "process_start_unix_ms", "heartbeat_unix_ms", "session_id", "process_session" }) {
                var malformed = State(); malformed.Remove(key); Check(!Parse(malformed).Status.Connected);
            }
            foreach (var pair in new (string Key, JsonNode? Value)[] { ("schema_version", JsonValue.Create(2)), ("version", JsonValue.Create("0.0.0")),
                ("process_id", JsonValue.Create(774)), ("process_start_unix_ms", JsonValue.Create(Ms(Started)+1)),
                ("heartbeat_unix_ms", JsonValue.Create(Ms(Now)-4000)), ("session_id", JsonValue.Create("../bad")),
                ("session_id", JsonValue.Create(ProcessSession+"_garbage")), ("session_id", JsonValue.Create(ProcessSession+"_18446744073709551616")) }) {
                var malformed=State();malformed[pair.Key]=pair.Value;Check(!Parse(malformed).Status.Connected);
            }
            Check(!LivinCreamModule.ParseSnapshot(Json(State()), Now.AddSeconds(-4), Started, 773, Now).Status.Connected);
            Check(!LivinCreamModule.ParseSnapshot(Json(State()), Started.AddTicks(-1), Started, 773, Now).Status.Connected);
            var menu=State();menu["ready"]=false;Check(Parse(menu).Status.Values["diagnostic_lc_initialization_failed"]==bool.FalseString);
            ((JsonObject)menu["values"]!)["initialization_error"]="Unsupported build";Check(Parse(menu).Status.Values["diagnostic_lc_initialization_failed"]==bool.TrueString);
            var ready = Parse(State()).Status;
            foreach(var feature in module.Definition.Features.Where(x => x.Number != null)) {
                var number=feature.Number!;
                var command=LivinCreamModule.CreateCommand(ready,ready,feature.Id,null,number.DefaultValue,Request,Ms(Now));
                Check((decimal)command["value"]==number.DefaultValue);
                foreach(var invalid in new[] { number.Minimum-1,number.Maximum+1,number.Minimum+0.5m })
                    Reject(()=>LivinCreamModule.CreateCommand(ready,ready,feature.Id,null,invalid,Request,Ms(Now)));
                Reject(()=>LivinCreamModule.CreateCommand(ready,ready,feature.Id,true,number.DefaultValue,Request,Ms(Now)));
            }
            foreach(var toggle in LivinCreamModule.ToggleIds) {
                Check((bool)LivinCreamModule.CreateCommand(ready,ready,toggle,true,null,Request,Ms(Now))["enabled"]);
                Reject(()=>LivinCreamModule.CreateCommand(ready,ready,toggle,null,null,Request,Ms(Now)));
                var unready=ready with { Ready=false,Values=new Dictionary<string,string>() };
                _=LivinCreamModule.CreateCommand(unready,unready,toggle,false,null,Request,Ms(Now));checks++;
                Check((bool)LivinCreamModule.CreateCommand(unready,unready,toggle,true,null,Request,Ms(Now))["enabled"]);
            }
            _=LivinCreamModule.CreateCommand(ready with {Ready=false},ready,"disable_all",null,null,Request,Ms(Now));checks++;
            Check((bool)LivinCreamModule.CreateCommand(ready,ready with {SessionId="other-session"},"lc_no_patience",true,null,Request,Ms(Now))["enabled"]);
            var flagged=ready with {Ready=false,Connected=false,Values=new Dictionary<string,string>{{"available_lc_money",bool.FalseString}}};
            Check((decimal)LivinCreamModule.CreateCommand(flagged,ready,"lc_money",null,123,Request,Ms(Now))["value"]==123);
            var moved=State();moved["session_id"]=ProcessSession+"_2";
            Check(LivinCreamModule.AcceptAcknowledgement(Parse(moved),Session,Request));
            Reject(()=>LivinCreamModule.CreateCommand(ready,ready,"unknown",null,null,Request,Ms(Now)));
            Check(LivinCreamModule.AcceptAcknowledgement(Parse(State()),Session,Request));
            var denied=State();((JsonObject)denied["command_result"]!)["success"]=false;Reject(()=>LivinCreamModule.AcceptAcknowledgement(Parse(denied),Session,Request));
            Reject(()=>LivinCreamModule.AcceptAcknowledgement(Parse(State()),"changed-session",Request));
            Check(LivinCreamModule.SameProcessSession(ready, ProcessSession));
            Check(LivinCreamModule.SameProcessSession(ready with { SessionId = ProcessSession + "_2" }, ProcessSession));
            Check(!LivinCreamModule.SameProcessSession(ready, new string('f', 32)));
            _=LivinCreamModule.CreateCommand(ready,ready,"lc_refill_softserve",null,null,Request,Ms(Now));checks++;
            Reject(()=>LivinCreamModule.CreateCommand(ready,ready,"lc_refill_softserve",null,1,Request,Ms(Now)));
            LegacyChecks();
            ConnectionChecks();
            using var f = new Fixture();
            LivinCreamInstaller.Install(f.Package,f.Game,()=>{});Check(LivinCreamInstaller.MatchesPackage(f.Package,f.Game));
            File.AppendAllText(SafePath(f.Game,LivinCreamInstaller.DllPath),"foreign edit");
            Reject(()=>LivinCreamInstaller.Install(f.Package,f.Game,()=>{}));
            LivinCreamInstaller.Remove(f.Game,()=>{});Check(File.ReadAllText(SafePath(f.Game,LivinCreamInstaller.DllPath)).Contains("foreign edit"));
            using var corrupt = new Fixture();File.AppendAllText(SafePath(corrupt.Package,LivinCreamInstaller.DllPath),"corrupt");
            Reject(()=>LivinCreamInstaller.Install(corrupt.Package,corrupt.Game,()=>{}));Check(!Directory.EnumerateFiles(corrupt.Game).Any());
            using var rollback = new Fixture();int calls=0;
            Reject(()=>LivinCreamInstaller.Install(rollback.Package,rollback.Game,()=>{if(++calls==3)throw new IOException("game started");}));
            Check(!Directory.EnumerateFiles(rollback.Game,"*",SearchOption.AllDirectories).Any());
            using var ownership = new Fixture();var dll=SafePath(ownership.Game,LivinCreamInstaller.DllPath);Directory.CreateDirectory(Path.GetDirectoryName(dll)!);File.Copy(SafePath(ownership.Package,LivinCreamInstaller.DllPath),dll);
            LivinCreamInstaller.Install(ownership.Package,ownership.Game,()=>{});LivinCreamInstaller.Remove(ownership.Game,()=>{});Check(File.Exists(dll));
            using var identity = new Fixture();File.WriteAllText(SafePath(identity.Game,LivinCreamModule.Executable),"fixture");File.WriteAllText(SafePath(identity.Game,"GameAssembly.dll"),"fixture");
            var metadata=SafePath(identity.Game,LivinCreamModule.MetadataPath);Directory.CreateDirectory(Path.GetDirectoryName(metadata)!);File.WriteAllText(metadata,"fixture");
            var testModule=new LivinCreamModule(identity.Root) {HashProvider=p=>p.EndsWith("GameAssembly.dll")?LivinCreamModule.SupportedAssemblyHash:LivinCreamModule.SupportedMetadataHash};
            Check(testModule.Compatible(identity.Game,true));testModule.HashProvider=_=>new string('0',64);Check(!testModule.Compatible(identity.Game,true));
            testModule.ProcessProvider=_=>[(773,Started)];testModule.AttachTrainer=_=>throw new Exception("Should never inject on discovery");
            _=testModule.DiscoverAsync(identity.Game).GetAwaiter().GetResult();checks++;
            Reject(()=>testModule.InstallAsync(new(identity.Game,"fixture",true,false,false,false,"")).GetAwaiter().GetResult());
            Console.WriteLine($"PASS Livin Cream module: {checks} assertions; metadata, schema/session/time guards, numeric bounds, off while loading, acknowledgements, compatibility and installer ownership/rollback. No game launched.");
            return 0;
        } catch(Exception error) { Console.WriteLine("FAIL Livin Cream: "+error);return 1; }
    }
    private static void ConnectionChecks()
    {
        using var f=new Fixture();
        File.WriteAllText(SafePath(f.Game,LivinCreamModule.Executable),"fixture");
        File.WriteAllText(SafePath(f.Game,LivinCreamModule.AssemblyPath),"fixture");
        var metadata=SafePath(f.Game,LivinCreamModule.MetadataPath);Directory.CreateDirectory(Path.GetDirectoryName(metadata)!);File.WriteAllText(metadata,"fixture");
        LivinCreamInstaller.Install(f.Package,f.Game,()=>{});
        var module=new LivinCreamModule(f.Root) {
            HashProvider=p=>p.EndsWith("GameAssembly.dll")?LivinCreamModule.SupportedAssemblyHash:LivinCreamModule.SupportedMetadataHash,
            ProcessProvider=_=>[(773,Started)]
        };
        var installation=new GameInstallation(f.Game,"fixture",true,true,true,true,"");
        var status=SafePath(f.Game,LivinCreamModule.BridgeRoot+"status.json");
        var lease=SafePath(f.Game,LivinCreamModule.BridgeRoot+"lease.txt");
        void Heartbeat(string root,string epoch) {
            var json=State();json["process_session"]=root;json["session_id"]=root+"_"+epoch;json["heartbeat_unix_ms"]=Ms(DateTime.UtcNow);WriteJson(status,json);
        }
        int attaches=0;module.AttachTrainer=_=>{attaches++;Heartbeat(ProcessSession,"1");};
        Heartbeat(ProcessSession,"1");
        var waiting=module.ReadStatusAsync(installation).GetAwaiter().GetResult();
        Check(waiting.Values["available_lc_connect"]==bool.TrueString && !File.Exists(lease) && attaches==0);
        module.SendAsync(installation,waiting with {Connected=false,Ready=false,SessionId="old-ui-session",Values=new Dictionary<string,string>()},"lc_connect").GetAwaiter().GetResult();
        Check(attaches==1 && File.ReadAllText(lease)==ProcessSession);
        Heartbeat(ProcessSession,"2");
        var restored=module.ReadStatusAsync(installation).GetAwaiter().GetResult();
        Check(restored.SessionId==ProcessSession+"_2" && attaches==1 && File.ReadAllText(lease)==ProcessSession);
        Check(!File.Exists(SafePath(f.Game,LivinCreamModule.BridgeRoot+"command.json")));
        Heartbeat(new string('f',32),"1");
        Check(module.ReadStatusAsync(installation).GetAwaiter().GetResult().Values["available_lc_connect"]==bool.TrueString && attaches==1 && File.ReadAllText(lease)==ProcessSession);
        module.Deactivate();Check(!File.Exists(lease));
        Heartbeat(new string('f',32),"1");
        Check(module.ReadStatusAsync(installation).GetAwaiter().GetResult().Values["available_lc_connect"]==bool.TrueString && attaches==1 && !File.Exists(lease));
    }
    private static void LegacyChecks()
    {
        using var f = new Fixture();
        var plugin=SafePath(f.Game,LivinCreamLegacyMigration.Plugin); Directory.CreateDirectory(Path.GetDirectoryName(plugin)!); File.WriteAllText(plugin,"old plugin");
        var bootstrap=SafePath(f.Game,"winhttp.dll");File.WriteAllText(bootstrap,"old Mono loader");
        WriteJson(SafePath(f.Game,LivinCreamLegacyMigration.Receipt),new {Module="livin-cream",Files=new[]{new{Path=LivinCreamLegacyMigration.Plugin,Sha256=Hash(plugin)},new{Path="winhttp.dll",Sha256=Hash(bootstrap)}}});
        using (LivinCreamLegacyMigration.Begin(f.Game,()=>{})) {Check(!File.Exists(plugin) && !File.Exists(bootstrap));}
        Check(File.Exists(plugin) && File.Exists(bootstrap));
        var foreign=SafePath(f.Game,"BepInEx/plugins/Other.dll");File.WriteAllText(foreign,"foreign mod");
        Reject(()=>LivinCreamInstaller.Install(f.Package,f.Game,()=>{}));Check(File.Exists(plugin) && File.Exists(bootstrap));File.Delete(foreign);
        int closed=0;Reject(()=>LivinCreamInstaller.Install(f.Package,f.Game,()=>{if(++closed==6)throw new IOException("interrupt");}));Check(File.Exists(plugin) && File.Exists(bootstrap));
        LivinCreamInstaller.Install(f.Package,f.Game,()=>{});
        Check(!File.Exists(plugin) && !File.Exists(bootstrap));Check(Directory.EnumerateFiles(SafePath(f.Game,"happycheats-recovery"),"*",SearchOption.AllDirectories).Any());
        Check(LivinCreamInstaller.MatchesPackage(f.Package,f.Game));
        using var changed=new Fixture();var old=SafePath(changed.Game,LivinCreamLegacyMigration.Plugin);Directory.CreateDirectory(Path.GetDirectoryName(old)!);File.WriteAllText(old,"modified");
        WriteJson(SafePath(changed.Game,LivinCreamLegacyMigration.Receipt),new {Module="livin-cream",Files=new[]{new{Path=LivinCreamLegacyMigration.Plugin,Sha256=new string('a',64)}}});
        Reject(()=>LivinCreamInstaller.Install(changed.Package,changed.Game,()=>{}));Check(File.ReadAllText(old)=="modified");
        using var shared=new Fixture();var core=SafePath(shared.Game,"BepInEx/core/BepInEx.Preloader.dll");Directory.CreateDirectory(Path.GetDirectoryName(core)!);File.WriteAllText(core,"Mono loader");
        var unowned=SafePath(shared.Game,"winhttp.dll");File.WriteAllText(unowned,"unowned bootstrap");
        WriteJson(SafePath(shared.Game,LivinCreamLegacyMigration.Receipt),new {Module="livin-cream",Files=Array.Empty<object>()});
        Reject(()=>LivinCreamInstaller.Install(shared.Package,shared.Game,()=>{}));Check(File.ReadAllText(unowned)=="unowned bootstrap");
    }
    private sealed class Fixture : IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"HappyCheats-livin-cream-fixture-"+Guid.NewGuid().ToString("N"));
        public string Package => Path.Combine(Root,"payload","livin-cream");
        public string Game => Path.Combine(Root,"game");
        public Fixture() {
            Directory.CreateDirectory(Game);
            foreach(var file in LivinCreamInstaller.RequiredFiles) {var path=SafePath(Package,file);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"fixture "+file);}
            WriteJson(SafePath(Package,"package.json"),new{schemaVersion=1,game="livin-cream",version=LivinCreamModule.TrainerVersion,files=LivinCreamInstaller.RequiredFiles.Select(path=>new{path,sha256=Hash(SafePath(Package,path))})});
        }
        public void Dispose(){Directory.Delete(Root,true);}
    }
}
