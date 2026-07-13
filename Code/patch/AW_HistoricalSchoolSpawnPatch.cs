using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using AncientWarfare3.core.schools;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_HistoricalSchoolSpawnPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.createNewUnit))]
        private static void ActorManagerCreateNewUnitCapture_Prefix(ActorManager __instance,
            out HistoricalSchoolActorSpawnCapture.FactoryFrame __state)
        {
            __state = HistoricalSchoolActorSpawnCapture.EnterFactory(__instance);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.createNewUnit))]
        private static Exception ActorManagerCreateNewUnitCapture_Finalizer(
            HistoricalSchoolActorSpawnCapture.FactoryFrame __state, Exception __exception)
        {
            HistoricalSchoolActorSpawnCapture.ExitFactory(__state);
            return __exception;
        }

        [HarmonyTranspiler]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.createNewUnit))]
        private static IEnumerable<CodeInstruction> ActorManagerCreateNewUnitCapture_Transpiler(
            IEnumerable<CodeInstruction> pInstructions)
        {
            MethodInfo allocationMethod = AccessTools.Method(
                typeof(SystemManager<Actor, ActorData>), "newObject", Type.EmptyTypes);
            MethodInfo armMethod = AccessTools.Method(
                typeof(HistoricalSchoolActorSpawnCapture),
                nameof(HistoricalSchoolActorSpawnCapture.ArmTargetFactoryAllocation));
            if (allocationMethod == null || armMethod == null)
                throw new MissingMethodException("ActorManager allocation capture target missing");

            int matches = 0;
            foreach (CodeInstruction instruction in pInstructions)
            {
                if (instruction.Calls(allocationMethod))
                {
                    matches++;
                    if (instruction.blocks.Count != 0)
                        throw new InvalidOperationException(
                            "ActorManager.createNewUnit allocation has an exception boundary");
                    var armInstruction = new CodeInstruction(OpCodes.Call, armMethod);
                    armInstruction.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    yield return armInstruction;
                }
                yield return instruction;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "ActorManager.createNewUnit allocation pattern changed: " + matches);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(ActorManager), "addObject")]
        private static void ActorManagerAddObjectCapture_Prefix(ActorManager __instance,
            Actor pObject, out HistoricalSchoolActorSpawnCapture.RegistrationFrame __state)
        {
            __state = HistoricalSchoolActorSpawnCapture.EnterRegistration(__instance, pObject);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(ActorManager), "addObject")]
        private static Exception ActorManagerAddObjectCapture_Finalizer(
            HistoricalSchoolActorSpawnCapture.RegistrationFrame __state,
            Exception __exception)
        {
            HistoricalSchoolActorSpawnCapture.ExitRegistration(__state);
            return __exception;
        }
    }
}
