using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace HappyCheats.ProjectHospital
{
    internal static class PatienceHooks
    {
        // Inject at the real call sites rather than detouring a trivial getter that Mono
        // may inline. Only these inspected waiting methods receive the override.
        internal static IEnumerable<CodeInstruction> WaitingThresholdsTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var bindings = RuntimeHooks.Bindings;
            var expected = Equals(__originalMethod, bindings.WaitingSitting) ? 3 : Equals(__originalMethod, bindings.WaitingPharmacy) ? 2 : 0;
            var scale = typeof(RuntimeHooks).GetMethod("ScalePatienceThreshold", BindingFlags.NonPublic | BindingFlags.Static);
            var result = new List<CodeInstruction>();
            string id = null;
            int matches = 0;
            foreach (var instruction in instructions)
            {
                result.Add(instruction);
                if (instruction.opcode == OpCodes.Ldstr) id = instruction.operand as string;
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && Equals(instruction.operand, bindings.TweakableValue))
                {
                    if (id == "TWEAKABLE_PATIENT_MAX_WAIT_TIME_HOURS" || id == "TWEAKABLE_PATIENT_LEAVE_TIME_HOURS" ||
                        id == "TWEAKABLE_PHARMACY_WARNING_WAIT" || id == "TWEAKABLE_PHARMACY_MAX_WAIT")
                    {
                        result.Add(new CodeInstruction(OpCodes.Ldstr, id));
                        result.Add(new CodeInstruction(OpCodes.Call, scale));
                        matches++;
                    }
                    id = null;
                }
            }
            if (expected == 0 || matches != expected) throw new InvalidOperationException("Patient waiting threshold bindings changed; trainer was not installed.");
            return result;
        }

        // Scale only the elapsed hours read by the satisfaction calculation. No patient timers
        // or database values are overwritten; medical, needs and closing-time paths keep running.
        internal static IEnumerable<CodeInstruction> WaitingModifiersTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>();
            var conversion = RuntimeHooks.Bindings.WaitHoursConversion;
            var scale = typeof(RuntimeHooks).GetMethod("ScaleWaitElapsed", BindingFlags.NonPublic | BindingFlags.Static);
            int matches = 0;
            foreach (var instruction in instructions)
            {
                result.Add(instruction);
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && Equals(instruction.operand, conversion))
                {
                    result.Add(new CodeInstruction(OpCodes.Call, scale));
                    matches++;
                }
            }
            if (matches != 1) throw new InvalidOperationException("Patient waiting satisfaction binding changed; trainer was not installed.");
            return result;
        }
    }
}
