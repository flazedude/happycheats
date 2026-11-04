using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Data;
using Core.Events;
using Core.Managers;
using ECM.Controllers;
using Pattern;
using UnityEngine;

namespace HappyCheats.BuilderSimulator
{
    // Requests survive a load; confirmed effects belong to one inspected live context.
    internal sealed class Runtime
    {
        internal static Runtime Current;
        internal static readonly string[] ToggleIds = { "unlimited_money", "materials_never_decrease", "mixes_never_decrease", "super_walk_speed", "super_jump", "game_speed" };
        internal readonly Dictionary<string, bool> Requested = ToggleIds.ToDictionary(x => x, _ => false);
        internal readonly Dictionary<string, bool> Active = ToggleIds.ToDictionary(x => x, _ => false);
        internal readonly Dictionary<string, string> Choices = new Dictionary<string, string> {
            { "super_walk_speed", "3" }, { "super_jump", "3" }, { "game_speed", "2" }
        };
        private EquipmentData equipment;
        private FirstPersonController player;
        private GameSceneManager scene;
        private object profile;
        private bool ready;
        private bool ownsTime;
        private float originalTime, appliedTime;
        internal string RestorationError { get; private set; }
        internal bool Ready => ready && SameContext();

        private static bool TryContext(out EquipmentData eq, out FirstPersonController controller, out GameSceneManager manager, out object playerProfile)
        {
            eq = null; controller = FirstPersonController.Instance; manager = GameSceneManager.Instance; playerProfile = null;
            if (!controller || controller.noGameplay || !controller.gameObject.activeInHierarchy || !manager
                || !GameSceneManager.GameSceneIsPrepared || manager.IsLoadingSaveData || !Singleton<ScenesCommunicator>.Instance) return false;
            var data = ScenesCommunicator.GetGameData;
            if (data == null || data.equipmentData == null || data.PlayerProfile == null
                || ScenesCommunicator.Instance.sceneLoadingHandler.PreventFromMultipleLoad) return false;
            eq = data.equipmentData; playerProfile = data.PlayerProfile;
            return true;
        }

        private bool SameContext() => TryContext(out var eq, out var controller, out var manager, out var playerProfile)
            && ReferenceEquals(eq, equipment) && ReferenceEquals(controller, player) && ReferenceEquals(manager, scene) && ReferenceEquals(profile, playerProfile);

        internal bool Refresh()
        {
            var nextReady = TryContext(out var eq, out var controller, out var manager, out var nextProfile);
            if (nextReady == ready && (!nextReady || (ReferenceEquals(eq, equipment) && ReferenceEquals(controller, player)
                && ReferenceEquals(manager, scene) && ReferenceEquals(nextProfile, profile)))) return false;
            RestoreTime();
            foreach (var id in ToggleIds) Active[id] = false;
            equipment = nextReady ? eq : null; player = nextReady ? controller : null;
            scene = nextReady ? manager : null; profile = nextReady ? nextProfile : null; ready = nextReady;
            RestorationError = null;
            // The same action path handles explicit clicks and one restoration attempt per new context.
            if (nextReady) foreach (var id in ToggleIds.Where(id => Requested[id]).ToArray()) {
                try { Execute(id, true, null); }
                catch (Exception error) { RestorationError = "Could not restore " + id + ": " + error.Message; }
            }
            return true;
        }

        internal bool Enabled(string id) => Ready && Active.TryGetValue(id, out var on) && on;
        internal bool Owns(EquipmentData value) => Ready && ReferenceEquals(value, equipment);
        internal bool Owns(BaseCharacterController value) => Ready && ReferenceEquals(value, player);
        internal float Multiplier(string id) => float.Parse(Choices[id], CultureInfo.InvariantCulture);

        internal string Execute(string action, bool? enabled, string choice)
        {
            if (action == "disable_all") {
                RestoreTime();
                foreach (var id in ToggleIds) { Requested[id] = false; Active[id] = false; }
                RestorationError = null;
                if (Ready) GameEvents.UIEvents.PublishOnMoneyUpdate();
                return "All continuous cheats and pending restorations are off. Skill levels already earned remain.";
            }
            if (choice != null) {
                ValidateChoice(action, choice);
                if (!Ready) throw new InvalidOperationException("Load a construction site before selecting a multiplier.");
                if (action == "game_speed") RestoreTime();
                Choices[action] = choice;
                TickTime();
                return action.Replace('_', ' ') + " multiplier: " + choice;
            }
            if (Requested.ContainsKey(action)) {
                if (!enabled.HasValue) throw new ArgumentException("A toggle requires an explicit state.");
                if (!enabled.Value) {
                    Requested[action] = Active[action] = false;
                    if (action == "game_speed") RestoreTime();
                    if (action == "unlimited_money" && Ready) GameEvents.UIEvents.PublishOnMoneyUpdate();
                    return action.Replace('_', ' ') + " off; pending restoration cancelled.";
                }
                if (!Ready) throw new InvalidOperationException("Load a construction site before enabling cheats.");
                Requested[action] = Active[action] = true;
                if (action == "unlimited_money") GameEvents.UIEvents.PublishOnMoneyUpdate();
                TickTime();
                return action.Replace('_', ' ') + " on.";
            }
            if (action != "max_skill_level") throw new ArgumentException("Unknown trainer action.");
            if (!Ready) throw new InvalidOperationException("Load a construction site before upgrading skills.");
            var upgrades = ScenesCommunicator.GetGameData.PlayerProfile.upgradesData;
            if (upgrades == null || upgrades.skillsData == null || upgrades.skillsData.Count == 0)
                throw new InvalidOperationException("Skill records are not loaded yet. Retry after opening the Skills tab.");
            foreach (var skill in upgrades.skillsData.Values) {
                if (skill == null || skill.currentLevel < 1 || skill.currentLevel > 3) throw new InvalidOperationException("Unexpected skill record; no upgrades were applied.");
            }
            foreach (var skill in upgrades.skillsData.Values) {
                for (var attempt = 0; attempt < 2 && skill.currentLevel < 3; attempt++) skill.LevelUp();
                if (skill.currentLevel != 3) throw new InvalidOperationException("The game did not confirm maximum skill level. Check the Skills tab before retrying.");
            }
            return "All loaded skills are at the game's maximum level (3). This one-shot action is not replayed after loads.";
        }

        internal static void ValidateChoice(string action, string choice)
        {
            var allowed = action == "game_speed" ? new[] { "0.5", "1.5", "2", "3" }
                : action == "super_walk_speed" || action == "super_jump" ? new[] { "2", "3", "5" } : Array.Empty<string>();
            if (!allowed.Contains(choice, StringComparer.Ordinal)) throw new ArgumentException("Unsupported multiplier for this control.");
        }

        internal void TickTime()
        {
            if (!Enabled("game_speed")) { RestoreTime(); return; }
            var current = Time.timeScale;
            if (current <= 0f) return; // Preserve a pause owned by the game.
            if (!ownsTime || Math.Abs(current - appliedTime) > 0.0001f) originalTime = current;
            appliedTime = originalTime * Multiplier("game_speed");
            Time.timeScale = appliedTime; ownsTime = true;
        }

        private void RestoreTime()
        {
            if (ownsTime && Math.Abs(Time.timeScale - appliedTime) <= 0.0001f) Time.timeScale = originalTime;
            ownsTime = false;
        }

        internal Dictionary<string, string> Values()
        {
            var result = Choices.ToDictionary(pair => "selected_" + pair.Key, pair => pair.Value);
            foreach (var id in ToggleIds) result["pending_" + id] = (Requested[id] && !Enabled(id)).ToString().ToLowerInvariant();
            if (Ready) result["money"] = equipment.PlayerCash.ToString(CultureInfo.InvariantCulture);
            return result;
        }
    }
}
