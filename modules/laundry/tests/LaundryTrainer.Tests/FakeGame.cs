using System.Runtime.CompilerServices;

// These fakes model generated IL2CPP wrappers: native fields become properties,
// and the stored singleton lives on a constructed generic base class.
namespace Il2Cpp
{
    internal class SceneInstance<T> where T : class
    {
        private static T? _instance { get; set; }
        public static T Instance => throw new InvalidOperationException("Must not create a scene singleton.");
        public static void SetForTest(T? instance) => _instance = instance;
    }

    internal class Singleton<T> where T : class
    {
        protected static T? _instance { get; set; }
        public static void SetForTest(T? instance) => _instance = instance;
    }

    internal class SingletonSceneSpecific<T> : Singleton<T> where T : class
    {
        public static T Instance => throw new InvalidOperationException("Must not create an XP singleton.");
    }

    // Lp deliberately is not zero: the binding must use its name.
    internal enum MoneyType { Crystal = 0, Lp = 17, Ticket = 3 }

    internal sealed class GameManager : SceneInstance<GameManager>
    {
        public bool isFinishLoading { get; set; }
        public float Cash { get; set; } = 125;
        public int MoneyCalls { get; private set; }
        public (MoneyType Type, float Amount, bool Popup) LastMoney { get; private set; }
        public void AddCurrency(MoneyType type, float val, bool usePopup = false)
        {
            MoneyCalls++;
            LastMoney = (type, val, usePopup);
            Cash = Math.Min(1500, Cash + val);
        }
        public float GetCurrency(MoneyType type) => type == MoneyType.Lp ? Cash : -999;
        public void AddCurrency(float val) => throw new InvalidOperationException("Wrong overload.");
    }

    internal sealed class LevelXPManager : SingletonSceneSpecific<LevelXPManager>
    {
        public bool hasLoaded { get; set; }
        public int XpCalls { get; private set; }
        public (float Amount, bool IapMultiplier, bool Popup) LastXp { get; private set; }
        public float AddXP(float xpToAdd = 1, bool useIAPMultiplier = true, bool showPopUp = true)
        {
            XpCalls++;
            LastXp = (xpToAdd, useIAPMultiplier, showPopUp);
            return Math.Min(xpToAdd, 42); // Simulates the game's own XP adjustment/cap.
        }
    }

    internal sealed class WashingMachinePickableScript
    {
        private float _remaining = 100;
        private float timer
        {
            get => ThrowOnRead ? throw new InvalidOperationException("Timer read failed") : _remaining;
            set
            {
                if (ThrowOnWrite) throw new InvalidOperationException("Timer write failed");
                TimerWrites++;
                _remaining = value;
            }
        }
        public float Remaining { get => _remaining; set => _remaining = value; }
        public float Elapsed { get; set; } = 1;
        public bool Paused { get; set; }
        public bool ResetCycle { get; set; }
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnWrite { get; set; }
        public int TimerWrites { get; private set; }
        public bool Completed { get; private set; }
        public int Updates { get; private set; }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private void WashingUpdate()
        {
            Updates++;
            if (Paused) return;
            if (ResetCycle) { _remaining = 100; return; }
            _remaining -= Elapsed;
            if (_remaining <= 0) Completed = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Tick() => WashingUpdate();
    }
}

namespace FieldFixtures
{
    internal class Scene<T> where T : class
    {
        private static T? _instance = null;
        public static void SetForTest(T instance) => _instance = instance;
        public static T? PeekForTest() => _instance;
    }
    internal sealed class Manager : Scene<Manager>
    {
        private bool loaded = true;
        private float timer = 25;
        public bool Loaded => loaded;
        public float Remaining => timer;
    }
    internal sealed class WrongMembers
    {
        public string loaded { get; set; } = "yes";
        public static float StaticMethod() => 1;
        public int IncorrectResult() => 1;
        public void WrongArguments(int value) { }
    }
}

internal sealed class LegacyOnly { }
internal sealed class Il2CppJoinedOnly { }
