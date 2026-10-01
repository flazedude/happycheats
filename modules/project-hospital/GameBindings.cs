using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace HappyCheats.ProjectHospital
{
    // All names and signatures are verified against the supported Steam assembly by the read-only inspector.
    internal sealed class GameBindings
    {
        internal const int MoneyTarget = 999999999;
        internal const int StaffLimit = 1000;
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private readonly FieldInfo hospitalInstance, hospitalState, hospitalDepartments, budget, balance, dailyStatistics;
        private readonly FieldInfo departmentData, departmentStatistics, reputation;
        private readonly FieldInfo editorInstance, editorInitialized, editorLoaded, editorUnloading;
        private readonly FieldInfo profileInstance, dayInstance, dayData, dayHours, dayNumber;
        private readonly MethodInfo countEmployees, campaignCategory;
        private readonly Dictionary<object, float> naturalPrestige = new Dictionary<object, float>();
        internal object ActiveState;
        internal bool FreezeTime, MaxStaff, UnlockCampaigns, MaxPrestige;
        internal readonly MethodInfo DayUpdate, StaffLimitCheck, CampaignCheck, StatisticsUpdate;

        internal GameBindings(Assembly assembly)
        {
            var hospital = assembly.GetType("Lopital.Hospital", true);
            var state = assembly.GetType("Lopital.HospitalPersistentData", true);
            var editor = assembly.GetType("MapEditorController", true);
            var day = assembly.GetType("Lopital.DayTime", true);
            var profile = assembly.GetType("PlayerProfile", true);
            hospitalInstance = Field(hospital, "sm_instance", hospital, true);
            hospitalState = Field(hospital, "m_state", state);
            hospitalDepartments = Field(hospital, "m_departments");
            budget = Field(state, "m_budget", assembly.GetType("Lopital.Budget", true));
            balance = Field(budget.FieldType, "m_currentBalance", typeof(int));
            dailyStatistics = Field(state, "m_dailyStatistics", assembly.GetType("Lopital.DailyStatistics", true));
            reputation = Field(dailyStatistics.FieldType, "m_reputation", typeof(float));
            departmentData = Field(assembly.GetType("Lopital.Department", true), "m_departmentPersistentData");
            departmentStatistics = Field(departmentData.FieldType, "m_todaysStatistics", dailyStatistics.FieldType);
            editorInstance = Field(editor, "sm_instance", editor, true);
            editorInitialized = Field(editor, "m_initialized", typeof(bool));
            editorLoaded = Field(editor, "m_levelLoaded", typeof(bool));
            editorUnloading = Field(editor, "m_unloading", typeof(bool));
            profileInstance = Field(profile, "sm_instance", profile, true);
            dayInstance = Field(day, "sm_instance", day, true);
            dayData = Field(day, "m_persistentData");
            dayHours = Field(dayData.FieldType, "m_currentTimeHours", typeof(double));
            dayNumber = Field(dayData.FieldType, "m_day", typeof(int));
            countEmployees = Method(hospital, "CountEmployees", typeof(int));
            DayUpdate = Method(day, "MultiUpdate", typeof(void), typeof(int), typeof(float));
            StaffLimitCheck = Method(hospital, "ReachedCharacterLimit", typeof(bool));
            CampaignCheck = Method(profile, "IsCampaignLevelUnlocked", typeof(bool), assembly.GetType("GameDBCampaignLevel", true));
            campaignCategory = Method(assembly.GetType("GameDBCampaignLevel", true), "get_LevelCategory", assembly.GetType("LevelCategory", true));
            StatisticsUpdate = Method(hospital, "UpdateCurrentStatistics", typeof(void));
        }

        internal bool ProfileReady { get { return profileInstance.GetValue(null) != null; } }

        internal object GetLoadedState()
        {
            var editor = editorInstance.GetValue(null);
            if (editor == null || !(bool)editorInitialized.GetValue(editor) || !(bool)editorLoaded.GetValue(editor) || (bool)editorUnloading.GetValue(editor)) return null;
            var hospital = hospitalInstance.GetValue(null);
            var state = hospital == null ? null : hospitalState.GetValue(hospital);
            var time = dayInstance.GetValue(null);
            return state != null && budget.GetValue(state) != null && dailyStatistics.GetValue(state) != null && time != null && dayData.GetValue(time) != null ? state : null;
        }

        internal bool IsCurrentHospitalActive { get { return ActiveState != null && ReferenceEquals(ActiveState, GetLoadedState()); } }

        internal string MaxMoney()
        {
            RequireHospital();
            var value = budget.GetValue(ActiveState);
            if ((int)balance.GetValue(value) < MoneyTarget) balance.SetValue(value, MoneyTarget);
            return "Hospital balance is at least 999,999,999.";
        }

        internal bool StaffLimitReached(object hospital)
        {
            return (int)countEmployees.Invoke(hospital, null) >= StaffLimit;
        }

        internal bool IsCampaign(object level)
        {
            if (level == null) return false;
            var category = Convert.ToInt32(campaignCategory.Invoke(level, null), CultureInfo.InvariantCulture);
            return category == 1 || category == 5;
        }

        internal bool IsActiveHospital(object hospital)
        {
            return IsCurrentHospitalActive && ReferenceEquals(hospital, hospitalInstance.GetValue(null));
        }

        internal void SetPrestige(bool enabled)
        {
            if (enabled) { RequireHospital(); MaxPrestige = true; ApplyPrestige(false); }
            else { RestorePrestige(); MaxPrestige = false; }
        }

        internal void ApplyPrestige(bool freshlyCalculated)
        {
            if (!MaxPrestige || !IsCurrentHospitalActive) return;
            var current = CurrentStatistics();
            if (freshlyCalculated) naturalPrestige.Clear();
            foreach (var statistics in current)
            {
                var original = (float)reputation.GetValue(statistics);
                if (!naturalPrestige.ContainsKey(statistics)) naturalPrestige[statistics] = original;
                if (original < 1f) reputation.SetValue(statistics, 1f);
            }
            // Do not retain historical statistics after the game replaces today's objects.
            var stale = new List<object>();
            foreach (var entry in naturalPrestige) if (!current.Contains(entry.Key)) stale.Add(entry.Key);
            foreach (var entry in stale) naturalPrestige.Remove(entry);
        }

        private List<object> CurrentStatistics()
        {
            var result = new List<object>();
            if (!IsCurrentHospitalActive) return result;
            result.Add(dailyStatistics.GetValue(ActiveState));
            var hospital = hospitalInstance.GetValue(null);
            foreach (var department in (IEnumerable)hospitalDepartments.GetValue(hospital))
            {
                var persistent = departmentData.GetValue(department);
                var statistics = persistent == null ? null : departmentStatistics.GetValue(persistent);
                if (statistics != null) result.Add(statistics);
            }
            return result;
        }

        private void RestorePrestige()
        {
            foreach (var statistics in CurrentStatistics())
            {
                float original;
                if (naturalPrestige.TryGetValue(statistics, out original) && (float)reputation.GetValue(statistics) == 1f) reputation.SetValue(statistics, original);
            }
            naturalPrestige.Clear();
        }

        internal void DisableAll()
        {
            RestorePrestige();
            FreezeTime = MaxStaff = UnlockCampaigns = MaxPrestige = false;
        }

        internal Dictionary<string, bool> ToggleValues()
        {
            return new Dictionary<string, bool> { { "freeze_time", FreezeTime }, { "max_staff", MaxStaff }, { "unlock_campaigns", UnlockCampaigns }, { "max_prestige", MaxPrestige } };
        }

        internal Dictionary<string, string> ReadValues()
        {
            var loaded = IsCurrentHospitalActive;
            var result = new Dictionary<string, string> { { "hospital_loaded", loaded ? "true" : "false" } };
            foreach (var action in new[] { "max_money", "freeze_time", "max_staff", "max_prestige" })
            {
                result["available_" + action] = loaded ? "True" : "False";
                if (!loaded) result["reason_" + action] = "Load a hospital before using this action.";
            }
            result["available_unlock_campaigns"] = ProfileReady ? "True" : "False";
            if (!ProfileReady) result["reason_unlock_campaigns"] = "Wait for the game profile to load.";
            if (!loaded) return result;
            var time = dayData.GetValue(dayInstance.GetValue(null));
            result["hospital_money"] = ((int)balance.GetValue(budget.GetValue(ActiveState))).ToString(CultureInfo.InvariantCulture);
            result["hospital_staff"] = ((int)countEmployees.Invoke(hospitalInstance.GetValue(null), null)).ToString(CultureInfo.InvariantCulture);
            result["hospital_staff_limit"] = MaxStaff ? StaffLimit.ToString(CultureInfo.InvariantCulture) : "Game setting";
            result["hospital_prestige"] = ((float)reputation.GetValue(dailyStatistics.GetValue(ActiveState)) * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
            result["hospital_day"] = ((int)dayNumber.GetValue(time)).ToString(CultureInfo.InvariantCulture);
            var minutes = (int)Math.Floor((double)dayHours.GetValue(time) * 60) % (24 * 60);
            result["hospital_time"] = (minutes / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
            return result;
        }

        internal void RequireHospital() { if (!IsCurrentHospitalActive) throw new InvalidOperationException("Load a hospital before using this action."); }
        private static FieldInfo Field(Type type, string name, Type expected = null, bool isStatic = false)
        {
            var field = type.GetField(name, Flags);
            if (field == null || field.IsStatic != isStatic || (expected != null && field.FieldType != expected)) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        private static MethodInfo Method(Type type, string name, Type returns, params Type[] arguments)
        {
            var method = type.GetMethod(name, Flags, null, arguments, null);
            if (method == null || method.IsStatic || method.ReturnType != returns) throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }

    internal static class RuntimeHooks
    {
        internal static GameBindings Bindings;
        internal static Action<string> ReportError;
        internal static bool DayTimePrefix()
        {
            try { return Bindings == null || !Bindings.FreezeTime || !Bindings.IsCurrentHospitalActive; }
            catch (Exception exception) { if (Bindings != null) Bindings.FreezeTime = false; Report(exception); return true; }
        }
        internal static void StaffPostfix(object __instance, ref bool __result)
        {
            try { if (__result && Bindings != null && Bindings.MaxStaff && Bindings.IsActiveHospital(__instance)) __result = Bindings.StaffLimitReached(__instance); }
            catch (Exception exception) { if (Bindings != null) Bindings.MaxStaff = false; Report(exception); }
        }
        internal static bool CampaignPrefix(object __0, ref bool __result)
        {
            try
            {
                if (Bindings == null || !Bindings.UnlockCampaigns || !Bindings.ProfileReady || !Bindings.IsCampaign(__0)) return true;
                __result = true;
                return false;
            }
            catch (Exception exception) { if (Bindings != null) Bindings.UnlockCampaigns = false; Report(exception); return true; }
        }
        internal static void StatisticsPostfix(object __instance)
        {
            try { if (Bindings != null && Bindings.IsActiveHospital(__instance)) Bindings.ApplyPrestige(true); }
            catch (Exception exception) { if (Bindings != null) Bindings.MaxPrestige = false; Report(exception); }
        }
        private static void Report(Exception exception) { if (ReportError != null) ReportError("Trainer hook disabled: " + exception.GetType().Name + ": " + exception.Message); }
    }
}
