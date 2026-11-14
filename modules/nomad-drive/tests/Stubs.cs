namespace UnityEngine {
    public enum FindObjectsSortMode { None }
    public class Object {
        public static object[] Scene = [];
        public static T[] FindObjectsByType<T>(FindObjectsSortMode mode) => Scene.OfType<T>().ToArray();
    }
    public class Component : Object {
        public Transform transform = new(); public Dictionary<Type, object> Components = new();
        public T GetComponent<T>() where T : class => Components.GetValueOrDefault(typeof(T)) as T;
        public T GetComponentInChildren<T>() where T : class => GetComponent<T>();
    }
    public class Transform { public Vector3 position; }
    public struct Vector3 {
        public float x; public float sqrMagnitude => x*x; public float magnitude => Math.Abs(x);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new() { x = a.x - b.x };
    }
}
namespace Mirror {
    public class NetworkConnectionToClient { }
    public static class NetworkServer {
        public static bool active = true; public static NetworkConnectionToClient localConnection = new();
        public static Dictionary<int, NetworkConnectionToClient> connections = new() { [0] = localConnection };
    }
    public static class NetworkClient { public static UnityEngine.Component localPlayer; }
}
namespace NomadDrive.Features.Player {
    public class PlayerStat {
        public float CurrentValue = 5; public float MaxValue = 100;
        public void SetMax() { CurrentValue = MaxValue; }
    }
    public class PlayerStatsManager : UnityEngine.Component {
        public bool IsInitialized = true; public bool IsDead;
        public PlayerStat Nutrition = new(), Hydration = new(), Energy = new(), Health = new(), Poison = new();
        public void HealDamage(float amount) { Health.CurrentValue = Math.Min(Health.MaxValue, Health.CurrentValue + amount); }
        public void ApplyMedicine(float rate, float reduction) { Poison.CurrentValue = Math.Max(0, Poison.CurrentValue-reduction); }
    }
}
namespace NomadDrive.Features.Vehicle {
    public class VehicleManager : UnityEngine.Component { public bool IsInitialized = true; public object FuelContainer; }
}
namespace NomadDrive.Features.Vehicle.Modules {
    public class VehicleEngineModule : UnityEngine.Component { public UnityEngine.Component InstalledEngine; }
}
namespace NomadDrive.Features.LiquidTransferSystem {
    public enum LiquidType { Empty, Gasoline, Water }
    public class LiquidContainerComponent { public bool isServer = true; public float Capacity=60, Amount=1; public LiquidType CurrentLiquidType=LiquidType.Gasoline; public void SetAmount(float value) { Amount=value; } }
}
namespace NomadDrive.Features.Attachables {
    public class ConditionComponent { public bool isServer=true; public float MaxCondition=100, Condition=5; public void ServerSetCondition(float value) { Condition=value; } }
}
namespace HappyCheats.NomadDrive { public class Plugin { public const string Version = "1.0.1"; } }
