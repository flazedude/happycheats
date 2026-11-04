// Synthetic engine/game surface. The real plugin source is compiled unchanged against these fixtures.
namespace UnityEngine {
    public class Object {
        public static readonly List<Object> Objects = new();
        public Object() { Objects.Add(this); }
        public static T[] FindObjectsByType<T>(FindObjectsSortMode mode) => Objects.OfType<T>().ToArray();
    }
    public enum FindObjectsSortMode { None }
    public sealed class GameObject { public int scene = 1; }
    public class MonoBehaviour : Object { public bool isActiveAndEnabled = true; public GameObject gameObject = new(); }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace BepInEx {
    public class BepInPlugin : Attribute { public BepInPlugin(string a, string b, string c) {} }
    public class BepInProcess : Attribute { public BepInProcess(string a) {} }
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour { public readonly Log Logger = new(); }
    public class Log { public void LogError(object x) {} public void LogWarning(object x) {} }
    public static class Paths { public static string BepInExRootPath = "", GameRootPath = ""; }
}
namespace HarmonyLib {
    public sealed class Harmony { public Harmony(string x) {} public void UnpatchSelf() {} public void Patch(object m, HarmonyMethod prefix = null, HarmonyMethod postfix = null) {} }
    public sealed class HarmonyMethod { public HarmonyMethod(Type x, string y) {} }
    public static class AccessTools {
        public static System.Reflection.MethodInfo Method(Type t, string n) => t.GetMethod(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
        public static object PropertyGetter(Type t, string n) => t.GetProperty(n)?.GetMethod;
    }
}
namespace makif.DatabaseSystem {
    public class InventoryDatabase { public float Money; }
    public class ProgressDatabase { public int Level = 1; public float XP; public PlayStyle PlayStyle; }
    public class SkillPointDatabase { public int SkillPoint, TotalEarned; }
    public class Database { public InventoryDatabase InventoryDatabase = new(); public ProgressDatabase ProgressDatabase = new(); public SkillPointDatabase SkillPointDatabase = new(); }
}
namespace makif { public static class GlobalData { public static DatabaseSystem.Database Database = new(); } }
namespace makif.MoneyKit { public static class Money {
    public static int Writes;
    public static float GetMoney() => makif.GlobalData.Database.InventoryDatabase.Money;
    public static void SetMoney(float x, string source, bool silent = false) { Writes++; makif.GlobalData.Database.InventoryDatabase.Money = x; }
} }
public enum PlayStyle { Classic, Cozy }
public static class PlayStyleRules { public static PlayStyle Current => makif.GlobalData.Database.ProgressDatabase.PlayStyle; public static bool CustomersHavePatience => Current != PlayStyle.Cozy; }
public class Player : UnityEngine.MonoBehaviour { public Shop Shop; public Shop GetCurrentShop() => Shop; }
public class Shop : UnityEngine.MonoBehaviour { public int TimerRefreshes; private void OnPlayStyleChanged(PlayStyle s) { TimerRefreshes++; } }
public class ShopSaveManager { public static bool RestoreComplete = true; public static void MarkRestorePending() { RestoreComplete = false; } }
public class LevelManager : UnityEngine.MonoBehaviour {
    public int Grants; public float Required = 100;
    public int GetCurrentLevel() => makif.GlobalData.Database.ProgressDatabase.Level;
    public float GetCurrentXP() => makif.GlobalData.Database.ProgressDatabase.XP;
    public float GetXPRequiredForLevel(int level) => Required;
    public void AddXP(float value) {
        Grants++; var p = makif.GlobalData.Database.ProgressDatabase; p.XP += value;
        while (p.XP >= Required) { p.XP -= Required; p.Level++; makif.GlobalData.Database.SkillPointDatabase.SkillPoint += 2; }
    }
}
public class SkillTreeManager : UnityEngine.MonoBehaviour {
    public int Grants;
    public int GetAvailablePoints() => makif.GlobalData.Database.SkillPointDatabase.SkillPoint;
    public void EarnSkillPoint(int amount) { Grants++; var p = makif.GlobalData.Database.SkillPointDatabase; p.SkillPoint += amount; p.TotalEarned += amount; }
}
public class SoftServeStock : UnityEngine.MonoBehaviour {
    public float Amount, MaxStock = 100; public int Restores;
    public float GetStockAmount() => Amount;
    public void RestoreStock(float value) { Restores++; Amount = value; }
    public void DecreaseStock(float value) { Amount -= value; }
}
