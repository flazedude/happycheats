using System.Reflection;

// Small synthetic fixtures, never copied game code and never distributed with the trainer.
namespace UnityEngine {
    public class Object { public static implicit operator bool(Object value) => value != null; }
    public class GameObject { public bool activeInHierarchy = true; }
    public class MonoBehaviour : Object { public GameObject gameObject = new(); }
    public static class Time { public static float timeScale = 1, realtimeSinceStartup; }
    public static class Application { public static bool isFocused = true; }
    public enum KeyCode { F1, F2, F3, F4, F5, F6, F7, F12 }
    public static class Input { public static KeyCode? Pressed; public static bool GetKeyDown(KeyCode key) { if (Pressed != key) return false; Pressed = null; return true; } }
}
namespace BepInEx {
    [AttributeUsage(AttributeTargets.Class)] public class BepInPlugin(string id, string name, string version) : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public class BepInProcess(string name) : Attribute { }
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour { public TestLogger Logger = new(); }
    public class TestLogger { public List<string> Errors = new(); public void LogInfo(string value) { } public void LogWarning(string value) => Errors.Add(value); public void LogError(string value) => Errors.Add(value); }
    public static class Paths { public static string GameRootPath, BepInExRootPath; }
}
namespace HarmonyLib {
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch() { } public HarmonyPatch(Type type, string method) { } }
    public class Harmony(string id) { public void PatchAll(Assembly assembly) { } public void UnpatchSelf() { } }
    public static class AccessTools { public static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
}
namespace Pattern { public class Singleton<T> : UnityEngine.MonoBehaviour { public static T Instance; } }
namespace ECM.Controllers {
    public class BaseCharacterController : UnityEngine.MonoBehaviour { public float jumpImpulse => 4; }
    public class BaseFirstPersonController : BaseCharacterController { protected float GetTargetSpeed() => 5; }
}
public class FirstPersonController : ECM.Controllers.BaseFirstPersonController { public static FirstPersonController Instance = new(); public bool noGameplay; }
namespace Core.Managers {
    public class GameSceneManager : UnityEngine.MonoBehaviour {
        public static GameSceneManager Instance = new(); public static bool GameSceneIsPrepared = true; public bool IsLoadingSaveData;
    }
    public class ScenesCommunicator : Pattern.Singleton<ScenesCommunicator> {
        public static Core.Data.GameData GetGameData = new(); public LoadingHandler sceneLoadingHandler = new();
    }
    public class LoadingHandler { public bool PreventFromMultipleLoad; }
}
namespace Core.Data {
    public class EquipmentData {
        public int PlayerCash { get; set; } = 50;
        public void RemoveFromConstructionItem(int id, int amount) { }
        public void RemoveFurnitureFromEquipment(string id, int color, int amount) { }
        public void RemoveFurnitureFromEquipment(string id, object location) { }
    }
    public class GameData { public EquipmentData equipmentData = new(); public PlayerProfile PlayerProfile = new(); }
    public class PlayerProfile { public Upgrades upgradesData = new(); }
    public class Upgrades { public Dictionary<string, SkillRecord> skillsData = new() { ["test"] = new() }; }
    public class SkillRecord {
        public int currentLevel = 1, Calls;
        public bool Refuse;
        public void LevelUp() { Calls++; if (!Refuse) currentLevel++; }
    }
}
namespace Core.Events {
    public static class GameEvents { public static class UIEvents { public static void PublishOnMoneyUpdate() { } } }
}
public struct Mix { public float amountOfMix; public int mixType, mixQuality; }
namespace Builder.Mixer {
    public class WheelbarrowMix { public Mix mix; public float GetMix(float amount) => amount; }
    public class WheelbarrowMixController { public Mix mix; public float GetMix(float amount) => amount; }
}
