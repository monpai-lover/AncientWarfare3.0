using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ActorKingdomSafetyPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.loadObject))]
        private static void ActorLoad_Postfix(Actor __result)
        {
            bool repaired = ActorKingdomSafetyService.
                RepairLoadedActor(__result);
            if (ActorKingdomSafetyRules.ShouldQueueDeferredRepair(repaired))
                ActorKingdomSafetyService.QueueRepair(__result);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), nameof(Actor.isAllowedToLookForEnemies))]
        private static bool ActorEnemyCheck_Prefix(Actor __instance,
            ref bool __result)
        {
            if (ActorKingdomSafetyRules.CanRunEnemyCheck(
                    actorExists: __instance?.data != null,
                    actorAssetExists: __instance?.asset != null,
                    kingdomAssetExists:
                        __instance?.kingdom?.asset != null)) return true;
            __result = false;
            ActorKingdomSafetyService.QueueRepair(__instance);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static void DrainActorKingdomRepairs_Prefix()
        {
            MapBoxFrameStageGuard.Run("actor_kingdom_repair",
                () => ActorKingdomSafetyService.DrainPendingRepairs());
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearActorKingdomRepairs_Prefix()
        {
            ActorKingdomSafetyService.ClearRuntime();
        }
    }
}
