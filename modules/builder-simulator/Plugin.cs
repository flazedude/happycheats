using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace HappyCheats.BuilderSimulator
{
    [BepInPlugin("happycheats.buildersimulator", "Happy Cheats — Builder Simulator", Version)]
    [BepInProcess("Builder Simulator.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Version = "1.0.0";
        public const string SupportedAssemblySha256 = "364320218449c22db4a6e851276f4236cdc51dc74fcbf8eba4afb7fdfc1556f0";
        private string session = Guid.NewGuid().ToString("N");
        private readonly HashSet<string> handled = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> emptyToggles = Runtime.ToggleIds.ToDictionary(x => x, _ => false);
        private Dictionary<string, bool> toggles => bindings == null ? emptyToggles : bindings.Active;
        private Harmony harmony;
        private string ipcPath;
        private string commandsPath;
        private string responsesPath;
        private string assemblyHash = "";
        private string message = "Waiting for a loaded construction site.";
        private string lastCommand;
        private Runtime bindings;
        private float nextPoll;
        private float nextStatus;
        private int processId;
        private bool shuttingDown;
        private bool bindingsValid;
        private string initializationError;

        private void Awake()
        {
            processId = Process.GetCurrentProcess().Id;
            ipcPath = Path.Combine(Paths.BepInExRootPath, "HappyCheats", "builder-simulator");
            commandsPath = Path.Combine(ipcPath, "commands");
            responsesPath = Path.Combine(ipcPath, "responses");
            try
            {
                AssertPlain(commandsPath);
                AssertPlain(responsesPath);
                Directory.CreateDirectory(commandsPath);
                Directory.CreateDirectory(responsesPath);
                var gameAssemblyPath = Path.Combine(Paths.GameRootPath, "Builder Simulator_Data", "Managed", "Assembly-CSharp.dll");
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(gameAssemblyPath))
                    assemblyHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(assemblyHash, SupportedAssemblySha256, StringComparison.Ordinal))
                    throw new NotSupportedException("This game build has not been inspected. Trainer actions are disabled.");
                bindings = new Runtime();
                Runtime.Current = bindings;
                harmony = new Harmony("happycheats.buildersimulator");
                harmony.PatchAll(typeof(Plugin).Assembly);
                bindingsValid = true;
                Logger.LogInfo("Trainer ready; waiting for a construction site. IPC: " + ipcPath);
            }
            catch (Exception exception)
            {
                harmony?.UnpatchSelf();
                Runtime.Current = null;
                bindings = null;
                message = Explain(exception);
                initializationError = message;
                Logger.LogError(message);
            }
            try
            {
                Protocol.WriteAtomic(Path.Combine(ipcPath, "startup.json"), new
                {
                    schema_version = 1, plugin_version = Version, session_id = session,
                    process_id = processId, assembly_sha256 = assemblyHash,
                    bindings_valid = bindingsValid, initialization_error = initializationError,
                    updated_utc = Protocol.Now()
                });
            }
            catch (Exception exception) { Logger.LogWarning("Cannot publish startup diagnostics: " + Explain(exception)); }
            Publish(null, false);
        }

        private void Update()
        {
            if (shuttingDown || ipcPath == null) return;
            try {
                if (bindings != null && bindings.Refresh()) {
                    session = Guid.NewGuid().ToString("N");
                    lastCommand = null;
                    message = bindings.Ready ? "Construction site loaded. Trainer ready."
                        : "Waiting for a loaded construction site. Requested toggles are pending restoration.";
                    Publish(null, bindings.Ready);
                }
                var ready = bindings != null && bindings.Ready;
                PollHotkeys(null, ready);
                bindings?.TickTime();
                if (Time.realtimeSinceStartup >= nextPoll) {
                    nextPoll = Time.realtimeSinceStartup + 0.15f;
                    PollCommands(null, ready);
                }
                if (Time.realtimeSinceStartup >= nextStatus) {
                    nextStatus = Time.realtimeSinceStartup + 0.5f;
                    Publish(null, ready);
                }
            } catch (Exception exception) {
                DisableAll();
                message = "Trainer stopped its continuous effects: " + Explain(exception);
                if (Time.realtimeSinceStartup >= nextStatus) {
                    nextStatus = Time.realtimeSinceStartup + 2f;
                    Logger.LogError(message); Publish(null, false);
                }
            }
        }

        private void PollCommands(object player, bool ready)
        {
            AssertPlain(commandsPath);
            AssertPlain(responsesPath);
            if (!Directory.Exists(commandsPath)) return;
            foreach (var path in Directory.EnumerateFiles(commandsPath, "*.json")
                .Where(p => Guid.TryParse(Path.GetFileNameWithoutExtension(p), out _))
                .OrderBy(File.GetCreationTimeUtc).Take(8))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                Guid parsed;
                if (!Guid.TryParse(id, out parsed)) continue;
                if (handled.Contains(id)) { TryDelete(path); continue; }
                // A response is the durable processed marker. Never replay an acknowledged request.
                AssertPlain(Path.Combine(responsesPath, id + ".json"));
                if (File.Exists(Path.Combine(responsesPath, id + ".json"))) { handled.Add(id); TryDelete(path); continue; }
                var success = false;
                string result;
                try
                {
                    AssertPlain(path);
                    if (new FileInfo(path).Length > 8192) throw new InvalidDataException("Command exceeds the size limit.");
                    var command = JsonConvert.DeserializeObject<TrainerCommand>(File.ReadAllText(path));
                    Protocol.Validate(command, path, session, Protocol.Now());
                    result = ExecuteAction(player, ready, command.action, command.enabled, command.choice);
                    success = true;
                    message = result;
                }
                catch (Exception exception) { result = Explain(exception); }
                handled.Add(id); // Mark before writing the response so a failed disk write cannot replay a mutation.
                lastCommand = id;
                Protocol.WriteAtomic(Path.Combine(responsesPath, id + ".json"), new
                {
                    schema_version = 1, request_id = id, session_id = session, success,
                    message = result, updated_utc = Protocol.Now()
                });
                TryDelete(path);
                Publish(player, ready);
            }
        }

        private string ExecuteAction(object player, bool ready, string action, bool? enabled, string choice = null)
        {
            if (bindings == null) throw new InvalidOperationException(initializationError ?? "Trainer bindings are unavailable; check this game for an update.");
            return bindings.Execute(action, enabled, choice);
        }

        private void PollHotkeys(object player, bool ready)
        {
            if (!Application.isFocused) return;
            string action = null;
            if (Input.GetKeyDown(KeyCode.F1)) action = "unlimited_money";
            else if (Input.GetKeyDown(KeyCode.F2)) action = "max_skill_level";
            else if (Input.GetKeyDown(KeyCode.F3)) action = "materials_never_decrease";
            else if (Input.GetKeyDown(KeyCode.F4)) action = "mixes_never_decrease";
            else if (Input.GetKeyDown(KeyCode.F5)) action = "super_walk_speed";
            else if (Input.GetKeyDown(KeyCode.F6)) action = "super_jump";
            else if (Input.GetKeyDown(KeyCode.F7)) action = "game_speed";
            if (action == null) return;
            try {
                message = ExecuteAction(player, ready, action,
                    bindings != null && bindings.Active.ContainsKey(action) ? (bool?)!bindings.Active[action] : null);
            } catch (Exception error) { message = Explain(error); Logger.LogWarning(message); }
            Publish(player, ready);
        }

        private void Publish(object player, bool ready)
        {
            try
            {
                AssertPlain(ipcPath);
                var status = new TrainerStatus
                {
                    session_id = session, process_id = processId, ready = ready,
                    message = message,
                    values = bindings != null ? bindings.Values() : new Dictionary<string, string>(),
                    enabled = new Dictionary<string, bool>(toggles), last_command = lastCommand,
                    updated_utc = Protocol.Now(), assembly_sha256 = assemblyHash,
                    bindings_valid = bindingsValid, initialization_error = initializationError
                };
                Protocol.WriteAtomic(Path.Combine(ipcPath, "status.json"), status);
            }
            catch (Exception exception) { Logger.LogWarning("Cannot publish trainer status: " + Explain(exception)); }
        }

        private void DisableAll()
        {
            bindings?.Execute("disable_all", null, null);
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
            DisableAll();
            harmony?.UnpatchSelf();
            Runtime.Current = null;
            bindings = null;
            message = "Game closed.";
            Publish(null, false);
        }

        private void OnDestroy() { OnApplicationQuit(); }

        private static void AssertPlain(string path)
        {
            for (var cursor = Path.GetFullPath(path); cursor != null; cursor = Path.GetDirectoryName(cursor))
                if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Trainer mailbox links are unsupported.");
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static string Explain(Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
            return exception.GetType().Name + ": " + exception.Message;
        }
    }
}
