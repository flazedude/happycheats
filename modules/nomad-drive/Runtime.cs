using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mirror;
using NomadDrive.Features.Player;
using NomadDrive.Features.Vehicle;
using NomadDrive.Features.Vehicle.Modules;
using NomadDrive.Features.LiquidTransferSystem;
using NomadDrive.Features.Attachables;
using UnityEngine;

namespace HappyCheats.NomadDrive
{
    internal sealed class Runtime
    {
        public PlayerStatsManager Player { get; private set; }
        public bool TryGetPlayer(out object player, out object record)
        {
            var identity = NetworkClient.localPlayer;
            Player = identity == null ? null : identity.GetComponent<PlayerStatsManager>();
            // A solo host may have its local connection in the server dictionary. Any remote peer ends this trainer session.
            bool solo = NetworkServer.active && NetworkServer.connections.Values.All(c => c == null || c == NetworkServer.localConnection);
            bool ready = solo && Player != null && Player.IsInitialized && !Player.IsDead && Player.Nutrition != null
                && Player.Hydration != null && Player.Energy != null && Player.Health != null && Player.Poison != null;
            player = ready ? Player : null; record = player;
            return ready;
        }

        public string Apply(object player, string action)
        {
            // Recheck authority and the exact local player at dispatch, including continuous actions.
            if (!TryGetPlayer(out var live, out _) || !ReferenceEquals(live, player))
                throw new InvalidOperationException("Load a living character in a solo hosted game. Multiplayer sessions are unsupported.");
            switch (action) {
                case "refill_food": Player.Nutrition.SetMax(); break;
                case "refill_water": Player.Hydration.SetMax(); break;
                case "refill_energy": Player.Energy.SetMax(); break;
                case "heal": Player.HealDamage(Player.Health.MaxValue); break;
                case "cure": Player.ApplyMedicine(Player.Poison.MaxValue, Player.Poison.MaxValue); break;
                case "refuel": Refuel(NearestVehicle()); break;
                case "repair_engine": RepairEngine(NearestVehicle()); break;
                default: throw new ArgumentException("Unknown Nomad Drive action.");
            }
            return "Applied " + action.Replace('_', ' ') + ".";
        }

        private VehicleManager NearestVehicle()
        {
            var position = Player.transform.position;
            var vehicles = UnityEngine.Object.FindObjectsByType<VehicleManager>(FindObjectsSortMode.None).Where(v => v.IsInitialized
                && (v.transform.position - position).sqrMagnitude <= 225f).OrderBy(v => (v.transform.position - position).sqrMagnitude).ToArray();
            if (vehicles.Length == 0) throw new InvalidOperationException("Move within 15 metres of an initialized vehicle.");
            if (vehicles.Length > 1 && Math.Abs((vehicles[0].transform.position - position).magnitude - (vehicles[1].transform.position - position).magnitude) < 1f)
                throw new InvalidOperationException("Two vehicles are equally close. Move nearer the intended vehicle and retry.");
            return vehicles[0];
        }

        private static void Refuel(VehicleManager vehicle)
        {
            var fuel = vehicle.FuelContainer as LiquidContainerComponent;
            if (fuel == null || fuel.Capacity <= 0 || !fuel.isServer) throw new InvalidOperationException("Install a fuel tank on the nearest vehicle first.");
            // Preserve the tank's liquid type. Do not guess fuel or alter a contaminated/empty untyped tank.
            var type = fuel.CurrentLiquidType.ToString();
            if (type != "Gasoline") throw new InvalidOperationException("Put the correct gasoline in the tank first; its liquid type is preserved.");
            fuel.SetAmount(fuel.Capacity);
        }

        private static void RepairEngine(VehicleManager vehicle)
        {
            var module = vehicle.GetComponentInChildren<VehicleEngineModule>();
            var engine = module == null ? null : module.InstalledEngine;
            var condition = engine == null ? null : engine.GetComponent<ConditionComponent>();
            if (condition == null || !condition.isServer) throw new InvalidOperationException("Install an engine on the nearest vehicle first.");
            condition.ServerSetCondition(condition.MaxCondition);
        }

        public Dictionary<string, string> ReadValues(object player)
        {
            var values = new Dictionary<string, string>();
            if (Player == null) return values;
            foreach (var pair in new[] { ("refill_food", Player.Nutrition), ("refill_water", Player.Hydration), ("refill_energy", Player.Energy), ("heal", Player.Health), ("cure", Player.Poison) })
                values[pair.Item1] = pair.Item2.CurrentValue.ToString("0.#", CultureInfo.InvariantCulture) + " / " + pair.Item2.MaxValue.ToString("0.#", CultureInfo.InvariantCulture);
            return values;
        }
    }
}
