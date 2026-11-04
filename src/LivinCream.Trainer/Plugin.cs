using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx;
using HarmonyLib;
using makif;
using makif.DatabaseSystem;
using makif.MoneyKit;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HappyCheats.LivinCream;

[BepInPlugin("happycheats.livincream", "HappyCheats — Livin' the Cream", Version)]
[BepInProcess("Livin' the Cream Ice Cream Simulator.exe")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Version = "1.0.0";
    public const string AssemblyHash = "3756614fb3952a6bacf1e2e8d1994efd7eb451d7f27218a66b033e386e3cb98e";
    private static Plugin instance;
    private readonly HashSet<string> handled = new HashSet<string>();
    private readonly Dictionary<string, bool> toggles = Protocol.Toggles.ToDictionary(id => id, _ => false);
    private string session = Guid.NewGuid().ToString("N"), ipc, commands, responses, error, lastCommand;
    private Database database;
    private InventoryDatabase inventory;
    private ProgressDatabase progress;
    private Shop shop;
    private LevelManager levels;
    private SkillTreeManager skills;
    private Player player;
    private bool valid;
    private float nextPoll;
    private Harmony harmony;
    private int processId;

    private void Awake()
    {
        processId = Process.GetCurrentProcess().Id;
        ipc = Path.Combine(Paths.BepInExRootPath, "HappyCheats", "livin-cream");
        commands = Path.Combine(ipc, "commands"); responses = Path.Combine(ipc, "responses");
        try {
            Directory.CreateDirectory(commands); Directory.CreateDirectory(responses);
            var file = Path.Combine(Paths.GameRootPath, "Livin' the Cream Ice Cream Simulator_Data", "Managed", "Assembly-CSharp.dll");
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file))
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != AssemblyHash)
                    throw new NotSupportedException("Unsupported game build. Check this game's HappyCheats update before installing.");
            instance = this;
            harmony = new Harmony("happycheats.livincream");
            harmony.Patch(AccessTools.PropertyGetter(typeof(PlayStyleRules), "CustomersHavePatience"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(PatiencePostfix)));
            harmony.Patch(AccessTools.Method(typeof(SoftServeStock), "DecreaseStock"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(ConsumePrefix)));
            harmony.Patch(AccessTools.Method(typeof(ShopSaveManager), "MarkRestorePending"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(RestorePendingPostfix)));
            valid = true;
        } catch (Exception ex) { error = Explain(ex); Logger.LogError(error); harmony?.UnpatchSelf(); }
        Publish();
    }

    // Never call MonoSingleton.Instance: it can create a game object in the main menu.
    private bool ReadyNow() => valid && database != null && ReferenceEquals(database, GlobalData.Database)
        && ReferenceEquals(inventory, database.InventoryDatabase) && ReferenceEquals(progress, database.ProgressDatabase)
        && ShopSaveManager.RestoreComplete && shop != null && levels != null && skills != null && player != null
        && player.GetCurrentShop() == shop && shop.isActiveAndEnabled && levels.isActiveAndEnabled
        && skills.isActiveAndEnabled && player.isActiveAndEnabled;

    private void RefreshContext()
    {
        var nextPlayer = valid && ShopSaveManager.RestoreComplete ? Single<Player>() : null;
        var nextShop = nextPlayer != null ? nextPlayer.GetCurrentShop() : null;
        var nextLevel = nextShop != null ? Single<LevelManager>() : null;
        var nextSkills = nextShop != null ? Single<SkillTreeManager>() : null;
        var nextDb = nextShop != null ? GlobalData.Database : null;
        if (nextDb == null || nextDb.InventoryDatabase == null || nextDb.ProgressDatabase == null || nextDb.SkillPointDatabase == null
            || nextLevel == null || nextSkills == null) { nextDb = null; nextShop = null; nextPlayer = null; nextLevel = null; nextSkills = null; }
        if (!ReferenceEquals(database, nextDb) || !ReferenceEquals(inventory, nextDb?.InventoryDatabase)
            || !ReferenceEquals(progress, nextDb?.ProgressDatabase) || shop != nextShop || levels != nextLevel || skills != nextSkills || player != nextPlayer) {
            // The shared client remembers requested toggles and restores them only through fresh session commands.
            foreach (var key in Protocol.Toggles) toggles[key] = false;
            session = Guid.NewGuid().ToString("N"); lastCommand = null;
        }
        database = nextDb; shop = nextShop; levels = nextLevel; skills = nextSkills; player = nextPlayer;
        inventory = nextDb?.InventoryDatabase; progress = nextDb?.ProgressDatabase;
    }
    private static T Single<T>() where T : MonoBehaviour
    {
        var matches = Object.FindObjectsByType<T>(FindObjectsSortMode.None).Where(x => x.isActiveAndEnabled).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    private static bool Active(string id) => instance != null && instance.ReadyNow() && instance.toggles[id];
    private static void PatiencePostfix(ref bool __result) { if (Active("lc_no_patience")) __result = false; }
    private static bool ConsumePrefix(SoftServeStock __instance) => !(Active("lc_unlimited_softserve")
        && __instance != null && __instance.gameObject.scene == instance.shop.gameObject.scene);
    private static void RestorePendingPostfix()
    {
        if (instance == null) return;
        foreach (var key in Protocol.Toggles) instance.toggles[key] = false;
        instance.session = Guid.NewGuid().ToString("N"); instance.lastCommand = null;
        instance.database = null; instance.inventory = null; instance.progress = null;
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup < nextPoll || ipc == null) return;
        nextPoll = Time.realtimeSinceStartup + 0.25f;
        try { RefreshContext(); PollCommands(); Publish(); }
        catch (Exception ex) {
            valid = false;
            foreach (var key in Protocol.Toggles) toggles[key] = false;
            error = "Trainer stopped: " + Explain(ex) + " Restart the game after checking the trainer installation.";
            Logger.LogError(error); Publish();
        }
    }
    private void RefreshPatience()
    {
        if (ReadyNow()) AccessTools.Method(typeof(Shop), "OnPlayStyleChanged").Invoke(shop, new object[] { PlayStyleRules.Current });
    }
    private string Execute(TrainerCommand command)
    {
        RefreshContext();
        if (command.session_id != session) throw new InvalidOperationException("Shop session changed. Reconnect before retrying.");
        if (command.action == "disable_all") {
            var hadPatience = toggles["lc_no_patience"];
            foreach (var key in Protocol.Toggles) toggles[key] = false;
            if (hadPatience) RefreshPatience();
            return "All continuous cheats are off.";
        }
        if (Protocol.Toggles.Contains(command.action) && command.enabled == false) {
            var wasOn = toggles[command.action]; toggles[command.action] = false;
            if (wasOn && command.action == "lc_no_patience") RefreshPatience();
            return "Control turned off.";
        }
        if (!ReadyNow()) throw new InvalidOperationException("Load your shop and wait for restoration to finish before using this control.");
        if (Protocol.Toggles.Contains(command.action)) {
            toggles[command.action] = true;
            try { if (command.action == "lc_no_patience") RefreshPatience(); }
            catch { toggles[command.action] = false; throw; }
            return "Control turned on.";
        }
        switch (command.action) {
            case "lc_money": Money.SetMoney((float)command.value.Value, "HappyCheats", true); return "Money set.";
            case "lc_add_xp":
                ValidateXP((float)command.value.Value);
                levels.AddXP((float)command.value.Value); return "Shop XP added through normal level-up rewards.";
            case "lc_add_skills":
                var amount = (int)command.value.Value;
                var db = database.SkillPointDatabase;
                if (db.SkillPoint < 0 || db.TotalEarned < 0 || db.SkillPoint > int.MaxValue - amount || db.TotalEarned > int.MaxValue - amount)
                    throw new InvalidOperationException("Skill-point counters are outside the supported range.");
                skills.EarnSkillPoint(amount); return "Skill points added.";
            case "lc_refill_softserve":
                var stocks = Object.FindObjectsByType<SoftServeStock>(FindObjectsSortMode.None).Where(x => x.gameObject.scene == shop.gameObject.scene && x.GetStockAmount() > 0).ToArray();
                if (stocks.Length == 0) throw new InvalidOperationException("Fill at least one soft-serve machine with a flavor first. Empty machines are preserved.");
                foreach (var stock in stocks) stock.RestoreStock(stock.MaxStock);
                return "Refilled " + stocks.Length + " soft-serve reservoirs. Existing flavors preserved.";
            default: throw new InvalidDataException("Unknown trainer control.");
        }
    }
    private void ValidateXP(float amount)
    {
        var xp = levels.GetCurrentXP() + amount; var level = levels.GetCurrentLevel();
        if (level < 1 || float.IsNaN(xp) || float.IsInfinity(xp) || xp < 0) throw new InvalidOperationException("Invalid current XP/level.");
        for (var steps = 0; steps < 512; steps++, level++) {
            var required = levels.GetXPRequiredForLevel(level);
            if (required <= 0 || float.IsNaN(required) || float.IsInfinity(required)) throw new InvalidOperationException("Invalid level XP requirement.");
            if (xp < required) return;
            xp -= required;
        }
        throw new InvalidOperationException("Too many level-ups in one request. Use a smaller XP amount.");
    }
    private void PollCommands()
    {
        if (!Directory.Exists(commands)) return;
        foreach (var file in Directory.EnumerateFiles(commands, "*.json").OrderBy(File.GetCreationTimeUtc).Take(8)) {
            var id = Path.GetFileNameWithoutExtension(file);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            var response = Path.Combine(responses, id + ".json");
            if (handled.Contains(id) || File.Exists(response)) { File.Delete(file); continue; }
            var success = false; string message;
            try {
                string text;
                try {
                    if (new FileInfo(file).Length > 8192) throw new ArgumentException("Oversized trainer command.");
                    text = File.ReadAllText(file);
                } catch (IOException) { continue; }
                var command = JsonConvert.DeserializeObject<TrainerCommand>(text);
                Protocol.Validate(command, file, session, Protocol.Now());
                message = Execute(command); success = true;
            } catch (Exception ex) { message = Explain(ex); Logger.LogWarning(message); }
            handled.Add(id); lastCommand = id; // Claim before responding: a failed response cannot replay a grant.
            Protocol.WriteAtomic(response, new { schema_version = 1, request_id = id, session_id = session, success, message });
            File.Delete(file);
        }
    }
    private void Publish()
    {
        try {
            var ready = ReadyNow();
            var values = new Dictionary<string, string>();
            if (ready) {
                values["lc_money"] = Money.GetMoney().ToString(CultureInfo.InvariantCulture);
                values["diagnostic_lc_level"] = levels.GetCurrentLevel().ToString();
                values["diagnostic_lc_xp"] = levels.GetCurrentXP().ToString(CultureInfo.InvariantCulture);
                values["diagnostic_lc_skills"] = skills.GetAvailablePoints().ToString();
            }
            Protocol.WriteAtomic(Path.Combine(ipc, "status.json"), new {
                schema_version = 1, trainer_version = Version, assembly_sha256 = AssemblyHash, process_id = processId,
                session_id = session, ready, message = error ?? (ready ? "Shop loaded. Trainer ready." : "Load your shop and wait for restoration to finish."),
                enabled = toggles.ToDictionary(x => x.Key, x => ready && x.Value), values, last_command = lastCommand
            });
        } catch (Exception ex) { Logger.LogWarning("Cannot publish trainer status: " + Explain(ex)); }
    }
    private void OnDestroy()
    {
        foreach (var key in Protocol.Toggles) toggles[key] = false;
        try { RefreshPatience(); } catch (Exception ex) { Logger.LogWarning(Explain(ex)); }
        valid = false; harmony?.UnpatchSelf(); if (instance == this) instance = null;
        Publish();
    }
    private static string Explain(Exception ex) => ex.GetBaseException().Message;
}
