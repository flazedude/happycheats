using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;

namespace HappyCheats.NomadDrive
{
    [BepInPlugin("happycheats.nomaddrive", "Happy Cheats — Nomad Drive", Version)]
    [BepInProcess("Nomad Drive.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Version = "1.0.0";
        public const string SupportedAssemblySha256 = "6957f9a1180d811ca2ae1bb44748157d794fed5befd845290fd15e41fb54ffaf";
        private string session = Guid.NewGuid().ToString("N");
        private readonly HashSet<string> handled = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> toggles = new Dictionary<string, bool>
        {
            { "unlimited_food", false }, { "unlimited_water", false }, { "unlimited_energy", false }, { "rapid_heal", false }
        };
        private string ipcPath;
        private string commandsPath;
        private string responsesPath;
        private string assemblyHash = "";
        private string message = "Waiting for a loaded character.";
        private string lastCommand;
        private Runtime bindings;
        private object previousRecord;
        private float nextPoll;
        private float nextStatus;
        private int processId;
        private bool shuttingDown;
        private bool bindingsValid;
        private string initializationError;

        private void Awake()
        {
            processId = Process.GetCurrentProcess().Id;
            ipcPath = Path.Combine(Paths.BepInExRootPath, "HappyCheats", "nomad-drive");
            commandsPath = Path.Combine(ipcPath, "commands");
            responsesPath = Path.Combine(ipcPath, "responses");
            try
            {
                AssertPlain(commandsPath);
                AssertPlain(responsesPath);
                Directory.CreateDirectory(commandsPath);
                Directory.CreateDirectory(responsesPath);
                var gameAssemblyPath = Path.Combine(Paths.GameRootPath, "Nomad Drive_Data", "Managed", "Assembly-CSharp.dll");
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(gameAssemblyPath))
                    assemblyHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(assemblyHash, SupportedAssemblySha256, StringComparison.Ordinal))
                    throw new NotSupportedException("This game build has not been inspected. Trainer actions are disabled.");
                bindings = new Runtime();
                bindingsValid = true;
                Logger.LogInfo("Trainer ready; waiting for a character. IPC: " + ipcPath);
            }
            catch (Exception exception)
            {
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
            object player = null;
            object record = null;
            var ready = false;
            try
            {
                ready = bindings != null && bindings.TryGetPlayer(out player, out record);
                var nextRecord = ready ? record : null;
                if (!ReferenceEquals(nextRecord, previousRecord))
                {
                    session = Guid.NewGuid().ToString("N");
                    lastCommand = null;
                    DisableAll();
                    message = ready ? "Solo character loaded. Trainer ready." : "Load a living character in a solo hosted game. Multiplayer sessions are unsupported.";
                    Publish(player, ready); // Rotate before polling: commands from the previous character must fail.
                }
                if (!ready && bindings != null) message = "Load a living character in a solo hosted game. Multiplayer sessions are unsupported.";
                if (!ready || !ReferenceEquals(record, previousRecord)) DisableAll();
                previousRecord = ready ? record : null;
                PollHotkeys(player, ready);
                if (ready)
                {
                    if (toggles["unlimited_food"]) bindings.Apply(player, "refill_food");
                    if (toggles["unlimited_water"]) bindings.Apply(player, "refill_water");
                    if (toggles["unlimited_energy"]) bindings.Apply(player, "refill_energy");
                    if (toggles["rapid_heal"]) bindings.Apply(player, "heal");
                }
                if (Time.realtimeSinceStartup >= nextPoll)
                {
                    nextPoll = Time.realtimeSinceStartup + 0.15f;
                    PollCommands(player, ready);
                }
                if (Time.realtimeSinceStartup >= nextStatus)
                {
                    nextStatus = Time.realtimeSinceStartup + 0.5f;
                    Publish(player, ready);
                }
            }
            catch (Exception exception)
            {
                DisableAll();
                message = "Trainer paused: " + Explain(exception);
                if (Time.realtimeSinceStartup >= nextStatus)
                {
                    nextStatus = Time.realtimeSinceStartup + 2f;
                    Logger.LogWarning(message);
                    Publish(null, false);
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
                    result = ExecuteAction(player, ready, command.action, command.enabled);
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

        private string ExecuteAction(object player, bool ready, string action, bool? enabled)
        {
            if (action == "disable_all") { DisableAll(); return "All continuous cheats are off."; }
            if (!ready) throw new InvalidOperationException("Load a living character in a solo hosted game before using trainer actions.");
            if (toggles.ContainsKey(action))
            {
                if (!enabled.HasValue) throw new InvalidDataException("Toggle state is missing.");
                if (enabled.Value) bindings.Apply(player, action == "rapid_heal" ? "heal" : action.Replace("unlimited_", "refill_"));
                toggles[action] = enabled.Value;
                return action.Replace('_', ' ') + (enabled.Value ? " enabled." : " disabled.");
            }
            return bindings.Apply(player, action);
        }

        private void PollHotkeys(object player, bool ready)
        {
            if (!Application.isFocused) return;
            string action = null;
            if (Input.GetKeyDown(KeyCode.F1)) action = "heal";
            else if (Input.GetKeyDown(KeyCode.F2)) action = "refill_food";
            else if (Input.GetKeyDown(KeyCode.F3)) action = "refill_water";
            else if (Input.GetKeyDown(KeyCode.F4)) action = "refill_energy";
            else if (Input.GetKeyDown(KeyCode.F5)) action = "cure";
            else if (Input.GetKeyDown(KeyCode.F6)) action = "refuel";
            else if (Input.GetKeyDown(KeyCode.F7)) action = "repair_engine";
            else if (Input.GetKeyDown(KeyCode.F8)) action = "unlimited_food";
            else if (Input.GetKeyDown(KeyCode.F9)) action = "unlimited_water";
            else if (Input.GetKeyDown(KeyCode.F10)) action = "unlimited_energy";
            else if (Input.GetKeyDown(KeyCode.F11)) action = "rapid_heal";
            else if (Input.GetKeyDown(KeyCode.F12)) action = "disable_all";
            if (action == null || (!ready && action != "disable_all")) return;
            message = ExecuteAction(player, ready, action, toggles.ContainsKey(action) ? (bool?)!toggles[action] : null);
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
                    values = ready ? bindings.ReadValues(player) : new Dictionary<string, string>(),
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
            foreach (var key in toggles.Keys.ToArray()) toggles[key] = false;
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
            DisableAll();
            bindings = null;
            message = "Game closed.";
            Publish(null, false);
        }

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
