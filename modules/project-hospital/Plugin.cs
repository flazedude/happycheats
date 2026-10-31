using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace HappyCheats.ProjectHospital
{
    [BepInPlugin("happycheats.projecthospital", "Happy Cheats — Project Hospital", Version)]
    [BepInProcess("ProjectHospital.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Version = "0.1.1";
        public const string SupportedAssemblySha256 = "1120d899fb74d16f31115c5c27325edbd1154b68e9419e6b7ea03cc3d43b6f24";
        private string session = Guid.NewGuid().ToString("N");
        private readonly HashSet<string> handled = new HashSet<string>(StringComparer.Ordinal);
        private string ipcPath, commandsPath, responsesPath, assemblyHash = "", message = "Waiting for the game.", lastCommand, initializationError;
        private GameBindings bindings;
        private Harmony harmony;
        private HeartbeatPublisher heartbeat;
        private object previousState;
        private float nextPoll, nextStatus;
        private int processId;
        private bool shuttingDown, bindingsValid;

        private void Awake()
        {
            processId = Process.GetCurrentProcess().Id;
            ipcPath = Path.Combine(Path.Combine(Paths.BepInExRootPath, "HappyCheats"), "project-hospital");
            commandsPath = Path.Combine(ipcPath, "commands");
            responsesPath = Path.Combine(ipcPath, "responses");
            try
            {
                Directory.CreateDirectory(commandsPath);
                Directory.CreateDirectory(responsesPath);
                var path = Path.Combine(Path.Combine(Path.Combine(Paths.GameRootPath, "ProjectHospital_Data"), "Managed"), "Assembly-CSharp.dll");
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path)) assemblyHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (assemblyHash != SupportedAssemblySha256) throw new NotSupportedException("This game build has not been inspected. Trainer actions are disabled.");
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp") ?? Assembly.Load("Assembly-CSharp");
                bindings = new GameBindings(assembly);
                RuntimeHooks.Bindings = bindings;
                RuntimeHooks.ReportError = error => { message = error; Logger.LogWarning(error); };
                harmony = new Harmony("happycheats.projecthospital");
                Patch(bindings.DayUpdate, "DayTimePrefix", false);
                Patch(bindings.StaffLimitCheck, "StaffPostfix", true);
                Patch(bindings.CampaignCheck, "CampaignPrefix", false);
                Patch(bindings.StatisticsUpdate, "StatisticsPostfix", true);
                bindingsValid = true;
                message = "Trainer ready. Load a hospital for hospital actions; campaigns can be unlocked at the menu.";
                Logger.LogInfo(message);
            }
            catch (Exception exception)
            {
                RuntimeHooks.Bindings = null;
                if (harmony != null) harmony.UnpatchSelf();
                bindings = null;
                initializationError = message = Explain(exception);
                Logger.LogError(message);
            }
            try
            {
                Protocol.WriteAtomic(Path.Combine(ipcPath, "startup.json"), new
                {
                    schema_version = 1, plugin_version = Version, version = Version, session_id = session, process_id = processId,
                    assembly_sha256 = assemblyHash, bindings_valid = bindingsValid, initialization_error = initializationError, updated_utc = Protocol.Now()
                });
            }
            catch (Exception exception) { Logger.LogWarning(Explain(exception)); }
            heartbeat = new HeartbeatPublisher(Path.Combine(ipcPath, "status.json"));
            Publish();
        }

        private void Patch(MethodInfo target, string method, bool postfix)
        {
            var patch = new HarmonyMethod(typeof(RuntimeHooks).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static));
            harmony.Patch(target, prefix: postfix ? null : patch, postfix: postfix ? patch : null);
        }

        private void Update()
        {
            if (shuttingDown || ipcPath == null) return;
            try
            {
                var currentState = bindings == null ? null : bindings.GetLoadedState();
                if (!ReferenceEquals(currentState, previousState))
                {
                    // Rotate the session before accepting commands for a different hospital.
                    if (bindings != null) bindings.DisableAll();
                    session = Guid.NewGuid().ToString("N");
                    lastCommand = null;
                    previousState = currentState;
                    if (bindings != null) bindings.ActiveState = currentState;
                    message = currentState == null ? "Hospital closed. Campaign unlock is available at the menu." : "Hospital loaded. Trainer ready.";
                    Publish();
                }
                PollHotkeys();
                if (Time.realtimeSinceStartup >= nextPoll)
                {
                    nextPoll = Time.realtimeSinceStartup + 0.15f;
                    PollCommands();
                }
                if (Time.realtimeSinceStartup >= nextStatus)
                {
                    nextStatus = Time.realtimeSinceStartup + 0.5f;
                    Publish();
                }
            }
            catch (Exception exception)
            {
                if (bindings != null) bindings.DisableAll();
                message = "Trainer paused: " + Explain(exception);
                if (Time.realtimeSinceStartup >= nextStatus) { nextStatus = Time.realtimeSinceStartup + 2f; Logger.LogWarning(message); Publish(); }
            }
        }

        private void PollCommands()
        {
            CommandQueue.Poll(commandsPath, responsesPath, session, handled, command => ExecuteAction(command.action, command.enabled), (id, success, result) =>
            {
                lastCommand = id;
                message = result;
                Publish();
            });
        }

        private string ExecuteAction(string action, bool? enabled)
        {
            if (action == "disable_all") { if (bindings != null) bindings.DisableAll(); return "All continuous cheats are off."; }
            if (!bindingsValid || bindings == null) throw new InvalidOperationException("Trainer bindings are unavailable.");
            if (action == "max_money") return bindings.MaxMoney();
            if (!Protocol.Toggles.Contains(action) || !enabled.HasValue) throw new IOException("Unknown action or missing toggle state.");
            if (action == "unlock_campaigns")
            {
                if (enabled.Value && !bindings.ProfileReady) throw new InvalidOperationException("Wait for the game profile to load.");
                bindings.UnlockCampaigns = enabled.Value;
                return enabled.Value ? "Campaigns unlocked for this session. Reopen the campaign selection panel." : "Normal campaign locks restored.";
            }
            if (enabled.Value) bindings.RequireHospital();
            if (action == "freeze_time") bindings.FreezeTime = enabled.Value;
            if (action == "max_staff") bindings.MaxStaff = enabled.Value;
            if (action == "max_prestige") bindings.SetPrestige(enabled.Value);
            return action == "max_staff" ? (enabled.Value ? "Staff hiring limit increased to 1,000." : "Normal staff hiring limit restored; existing employees are kept.")
                : action == "freeze_time" ? (enabled.Value ? "Time of day frozen; hospital simulation continues." : "Time of day resumed.")
                : enabled.Value ? "Hospital and department prestige held at maximum." : "Natural prestige restored.";
        }

        private void PollHotkeys()
        {
            if (!Application.isFocused || bindings == null) return;
            string action = null;
            if (Input.GetKeyDown(KeyCode.F1)) action = "max_money";
            else if (Input.GetKeyDown(KeyCode.F2)) action = "freeze_time";
            else if (Input.GetKeyDown(KeyCode.F3)) action = "max_staff";
            else if (Input.GetKeyDown(KeyCode.F4)) action = "unlock_campaigns";
            else if (Input.GetKeyDown(KeyCode.F5)) action = "max_prestige";
            else if (Input.GetKeyDown(KeyCode.F10)) action = "disable_all";
            if (action == null) return;
            var toggles = bindings.ToggleValues();
            if (action != "disable_all" && action != "unlock_campaigns" && !bindings.IsCurrentHospitalActive) return;
            if (action == "unlock_campaigns" && !bindings.UnlockCampaigns && !bindings.ProfileReady) return;
            message = ExecuteAction(action, toggles.ContainsKey(action) ? (bool?)!toggles[action] : null);
            Publish();
        }

        private void Publish()
        {
            try
            {
                var status = new TrainerStatus
                {
                    session_id = session, process_id = processId, ready = !shuttingDown && bindingsValid && bindings != null && (bindings.ProfileReady || bindings.IsCurrentHospitalActive),
                    message = message, values = bindings == null ? new Dictionary<string, string>() : bindings.ReadValues(),
                    enabled = bindings == null ? new Dictionary<string, bool>() : bindings.ToggleValues(), last_command = lastCommand,
                    updated_utc = Protocol.Now(), assembly_sha256 = assemblyHash, bindings_valid = bindingsValid, initialization_error = initializationError
                };
                if (heartbeat != null) heartbeat.Publish(status);
                else Protocol.WriteAtomic(Path.Combine(ipcPath, "status.json"), status);
            }
            catch (Exception exception) { Logger.LogWarning("Cannot publish trainer status: " + Explain(exception)); }
        }
        private void OnDestroy() { Shutdown(); }
        private void OnApplicationQuit() { Shutdown(); }
        private void Shutdown()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            if (heartbeat != null) { heartbeat.Dispose(); heartbeat = null; }
            if (bindings != null) bindings.DisableAll();
            RuntimeHooks.Bindings = null;
            RuntimeHooks.ReportError = null;
            if (harmony != null) harmony.UnpatchSelf();
            message = "Trainer stopped.";
            Publish();
        }
        private static string Explain(Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
            return exception.GetType().Name + ": " + exception.Message;
        }
    }
}
