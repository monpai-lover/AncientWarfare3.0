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
        [HarmonyPrepare]
        private static bool Prepare()
        {
            // Diagnostic isolation: this global ActorManager factory hook is
            // disabled while validating the vanilla spawn path.
            return false;
        }

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
        private static void ActorManagerCreateNewUnitCapture_Finalizer(
            HistoricalSchoolActorSpawnCapture.FactoryFrame __state,
            Exception __exception,
            string pStatsID,
            WorldTile pTile,
            Subspecies pSubspecies)
        {
            if (__exception != null)
            {
                ModClass.LogError(
                    "ActorManager.createNewUnit failed: asset=" +
                    (pStatsID ?? "<null>") +
                    " tile=" + (pTile != null) +
                    " subspecies=" + (pSubspecies != null) +
                    " capture=" + (__state != null) +
                    " world=" + (World.world != null) +
                    " map_stats=" + (World.world?.map_stats != null) +
                    " units=" + (World.world?.units != null) +
                    " actor_asset=" + (AssetManager.actor_library?.get(pStatsID) != null));
            }
            HistoricalSchoolActorSpawnCapture.ExitFactory(__state);
        }

        [HarmonyTranspiler]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.createNewUnit))]
        private static IEnumerable<CodeInstruction> ActorManagerCreateNewUnitCapture_Transpiler(
            IEnumerable<CodeInstruction> pInstructions)
        {
            // The allocation hook must not rewrite vanilla ActorManager IL.
            // The factory prefix/finalizer still delimit school-owned spawns;
            // registration is observed through addObject.
            return pInstructions;
            /*
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
            */
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
        private static void ActorManagerAddObjectCapture_Finalizer(
            HistoricalSchoolActorSpawnCapture.RegistrationFrame __state)
        {
            HistoricalSchoolActorSpawnCapture.ExitRegistration(__state);
        }
    }
}
