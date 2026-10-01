// Deliberately inert contracts for offline policy checks. No Unity or game assembly is loaded.
namespace Lopital
{
    public sealed class Budget { public int m_currentBalance; }
    public sealed class DailyStatistics { public float m_reputation; }
    public sealed class HospitalPersistentData
    {
        public Budget m_budget = new();
        public DailyStatistics m_dailyStatistics = new();
        public List<DailyStatistics> m_statisticsHistory = new();
    }
    public sealed class DepartmentPersistentData
    {
        public DailyStatistics m_todaysStatistics = new();
        public List<DailyStatistics> m_statistics = new();
    }
    public sealed class Department { public DepartmentPersistentData m_departmentPersistentData = new(); }
    public sealed class Hospital
    {
        public static Hospital sm_instance;
        public HospitalPersistentData m_state = new();
        public List<Department> m_departments = new();
        public int EmployeeCount;
        public int CountEmployees() => EmployeeCount;
        public bool ReachedCharacterLimit() => EmployeeCount >= 200;
        public void UpdateCurrentStatistics() { }
    }
    public sealed class DayTimePersistentData { public double m_currentTimeHours = 13.5; public int m_day = 2; }
    public sealed class DayTime
    {
        public static DayTime sm_instance;
        public DayTimePersistentData m_persistentData = new();
        public void MultiUpdate(int multiplier, float seconds) { }
    }
}

public sealed class MapEditorController
{
    public static MapEditorController sm_instance;
    public bool m_initialized;
    public bool m_levelLoaded;
    public bool m_unloading;
}
public enum LevelCategory { HIDDEN = 0, CAMPAIGN = 1, SANDBOX = 2, SCENARIO = 3, SCENARIO_WORKSHOP = 4, DLC_CAMPAIGN = 5 }
public sealed class GameDBCampaignLevel { public LevelCategory LevelCategory { get; set; } }
public sealed class PlayerProfile
{
    public static PlayerProfile sm_instance;
    public bool IsCampaignLevelUnlocked(GameDBCampaignLevel level) => false;
}
namespace HappyCheats.ProjectHospital { internal static class Plugin { internal const string Version = "offline-test"; } }
