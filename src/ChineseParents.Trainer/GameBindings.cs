using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace HappyCheats.ChineseParents
{
    // These members were inspected in the installed Steam build, never discovered by writes.
    internal sealed class GameBindings
    {
        private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private readonly FieldInfo playerInstance;
        private readonly FieldInfo recordInstance;
        private readonly PropertyInfo currentRecord;
        private readonly PropertyInfo currentRole;
        private readonly PropertyInfo currentTable;
        private readonly PropertyInfo potentialMax;
        private readonly PropertyInfo round;
        private readonly Dictionary<string, PropertyInfo> properties;
        private object activeRecord;
        private object activeRole;
        private object characterIdentity;
        private static readonly string[] AttributeNames = { "Iq", "Eq", "Imagination", "Memory", "Stamination", "Charm" };

        public GameBindings(Assembly assembly)
        {
            var playerType = assembly.GetType("NewPlayerDataMgr", true);
            var recordType = assembly.GetType("NewRecordMgr", true);
            // Reading the existing backing field avoids get_Instance creating a manager at the menu.
            playerInstance = RequiredField(playerType.BaseType, "m_instance");
            recordInstance = RequiredField(recordType.BaseType, "m_instance");
            currentRecord = RequiredProperty(recordType, "CurrentRecord");
            currentRole = RequiredProperty(recordType, "CurrentRoleCoreInfo");
            currentTable = RequiredProperty(playerType, "CurTable");
            potentialMax = RequiredProperty(playerType, "Potential_max");
            round = RequiredProperty(playerType, "CurRound");
            properties = new[] { "Money", "Wisdom", "Potentiality", "Pressure", "Shadow", "Face" }
                .Concat(AttributeNames).ToDictionary(name => name, name => RequiredProperty(playerType, name));
            foreach (var property in properties.Values)
                if (property.PropertyType != typeof(float) || property.GetSetMethod() == null)
                    throw new InvalidOperationException("Unsupported property contract: " + property.Name);
        }

        public bool TryGetPlayer(out object player, out object record)
        {
            player = playerInstance.GetValue(null);
            var manager = recordInstance.GetValue(null);
            var selectedRecord = manager == null ? null : currentRecord.GetValue(manager, null);
            var selectedRole = manager == null ? null : currentRole.GetValue(manager, null);
            var ready = player != null && selectedRecord != null && selectedRole != null
                && currentTable.GetValue(player, null) != null;
            if (!ready)
            {
                activeRecord = null;
                activeRole = null;
                characterIdentity = null;
                record = null;
                return false;
            }
            // A generation can replace role data inside the same record; both identify the character.
            if (!ReferenceEquals(activeRecord, selectedRecord) || !ReferenceEquals(activeRole, selectedRole))
            {
                activeRecord = selectedRecord;
                activeRole = selectedRole;
                characterIdentity = new object();
            }
            record = characterIdentity;
            return true;
        }

        public Dictionary<string, string> ReadValues(object player)
        {
            var result = new Dictionary<string, string>();
            foreach (var pair in properties)
                result[ValueKey(pair.Key)] = Get(player, pair.Key).ToString("0.##", CultureInfo.InvariantCulture);
            result["energy_max"] = GetEnergyMaximum(player).ToString(CultureInfo.InvariantCulture);
            result["round"] = Convert.ToInt32(round.GetValue(player, null)).ToString(CultureInfo.InvariantCulture);
            return result;
        }

        public string Apply(object player, string action)
        {
            switch (action)
            {
                case "add_money": Add(player, "Money", 1000); return "Added 1,000 money.";
                case "add_knowledge": Add(player, "Wisdom", 1000, 99999); return "Added learning points (up to the game's 99,999 cap).";
                case "refill_energy": RefillEnergy(player); return "Refilled action points to the current round's maximum.";
                case "clear_stress": ClearStress(player); return "Cleared stress.";
                case "clear_shadow": Set(player, "Shadow", 0); return "Cleared psychological shadow.";
                case "add_face": Add(player, "Face", 100); return "Added 100 face.";
                case "add_attributes":
                    foreach (var property in AttributeNames)
                    {
                        var current = Get(player, property);
                        if (float.IsNaN(current) || float.IsInfinity(current))
                            throw new InvalidOperationException("Invalid current value: " + property);
                    }
                    foreach (var property in AttributeNames) Add(player, property, 100);
                    return "Added 100 IQ, EQ, imagination, memory, constitution and charm.";
                default: throw new InvalidOperationException("Unknown action: " + action);
            }
        }

        public void RefillEnergy(object player) { Set(player, "Potentiality", GetEnergyMaximum(player)); }
        public void ClearStress(object player) { Set(player, "Pressure", 0); }

        private int GetEnergyMaximum(object player)
        {
            var maximum = Convert.ToInt32(potentialMax.GetValue(player, null));
            if (maximum < 1 || maximum > 100000) throw new InvalidOperationException("The game's action point cap is unavailable.");
            return maximum;
        }

        private float Get(object player, string name) { return (float)properties[name].GetValue(player, null); }

        private void Add(object player, string name, float amount, float maximum = 1000000)
        {
            var current = Get(player, name);
            if (float.IsNaN(current) || float.IsInfinity(current)) throw new InvalidOperationException("Invalid current value: " + name);
            // Do not reduce a value already above our conservative additive limit.
            if (current >= maximum) return;
            Set(player, name, Math.Min(maximum, current + amount));
        }

        private void Set(object player, string name, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Invalid new value: " + name);
            if (Get(player, name) == value) return;
            // Use the game's public setter so its clamps and UI change callbacks run.
            properties[name].SetValue(player, value, null);
        }

        private static PropertyInfo RequiredProperty(Type type, string name)
        {
            return type.GetProperty(name, InstanceFlags) ?? throw new MissingMemberException(type.FullName, name);
        }

        private static FieldInfo RequiredField(Type type, string name)
        {
            return type.GetField(name, StaticFlags) ?? throw new MissingFieldException(type.FullName, name);
        }

        private static string ValueKey(string property)
        {
            switch (property)
            {
                case "Wisdom": return "knowledge";
                case "Potentiality": return "energy";
                case "Pressure": return "stress";
                case "Stamination": return "constitution";
                default: return property.ToLowerInvariant();
            }
        }
    }
}
