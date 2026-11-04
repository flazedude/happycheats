using System;
using System.Collections.Generic;
using System.Reflection;
using Builder.Mixer;
using Core.Data;
using ECM.Controllers;
using HarmonyLib;

namespace HappyCheats.BuilderSimulator
{
    [HarmonyPatch(typeof(EquipmentData), "get_PlayerCash")]
    internal static class MoneyReadPatch
    {
        internal static void Postfix(EquipmentData __instance, ref int __result) {
            var runtime = Runtime.Current;
            if (runtime != null && runtime.Owns(__instance) && runtime.Enabled("unlimited_money")) __result = Math.Max(__result, 100000000);
        }
    }
    [HarmonyPatch(typeof(EquipmentData), "set_PlayerCash")]
    internal static class MoneyWritePatch
    {
        internal static bool Prefix(EquipmentData __instance) => !(Runtime.Current != null && Runtime.Current.Owns(__instance) && Runtime.Current.Enabled("unlimited_money"));
    }
    // Suppress consumption, not item ownership/unlock checks or placement logic.
    [HarmonyPatch]
    internal static class MaterialsPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(EquipmentData), "RemoveFromConstructionItem");
            foreach (var method in typeof(EquipmentData).GetMethods()) if (method.Name == "RemoveFurnitureFromEquipment") yield return method;
        }
        internal static bool Prefix(EquipmentData __instance) => !(Runtime.Current != null && Runtime.Current.Owns(__instance) && Runtime.Current.Enabled("materials_never_decrease"));
    }
    // Both old and current wheelbarrows exist in the inspected assembly. Preserve an existing mix;
    // do not manufacture a type/quality, refill an empty tray, or block deliberate GetAllMix clearing.
    [HarmonyPatch(typeof(WheelbarrowMix), "GetMix")]
    internal static class LegacyMixPatch
    {
        internal static bool Prefix(WheelbarrowMix __instance, float _amount, ref float __result) {
            if (Runtime.Current == null || !Runtime.Current.Enabled("mixes_never_decrease") || !(__instance.mix.amountOfMix > 0) || !(_amount > 0)) return true;
            __result = Math.Min(_amount, __instance.mix.amountOfMix); return false;
        }
    }
    [HarmonyPatch(typeof(WheelbarrowMixController), "GetMix")]
    internal static class MixPatch
    {
        internal static bool Prefix(WheelbarrowMixController __instance, float _amount, ref float __result) {
            if (Runtime.Current == null || !Runtime.Current.Enabled("mixes_never_decrease") || !(__instance.mix.amountOfMix > 0) || !(_amount > 0)) return true;
            __result = Math.Min(_amount, __instance.mix.amountOfMix); return false;
        }
    }
    [HarmonyPatch(typeof(BaseFirstPersonController), "GetTargetSpeed")]
    internal static class WalkPatch
    {
        internal static void Postfix(BaseFirstPersonController __instance, ref float __result) {
            var runtime = Runtime.Current;
            if (runtime != null && runtime.Owns(__instance) && runtime.Enabled("super_walk_speed")) __result *= runtime.Multiplier("super_walk_speed");
        }
    }
    [HarmonyPatch(typeof(BaseCharacterController), "get_jumpImpulse")]
    internal static class JumpPatch
    {
        internal static void Postfix(BaseCharacterController __instance, ref float __result) {
            var runtime = Runtime.Current;
            if (runtime != null && runtime.Owns(__instance) && runtime.Enabled("super_jump")) __result *= (float)Math.Sqrt(runtime.Multiplier("super_jump"));
        }
    }
}
