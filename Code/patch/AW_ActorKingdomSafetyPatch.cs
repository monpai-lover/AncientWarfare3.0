using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;
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
            long benchmark = RecentFeatureBenchmark.BeginOutsideFrameStage();
            try
            {
                MapBoxFrameStageGuard.Run("actor_kingdom_repair",
                    () => ActorKingdomSafetyService.DrainPendingRepairs());
            }
            finally
            {
                RecentFeatureBenchmark.EndOutsideFrameStage(
                    RecentFeatureBenchmarkRules.ActorKingdomRepairIndex,
                    benchmark);
            }
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearActorKingdomRepairs_Prefix()
        {
            ActorKingdomSafetyService.ClearRuntime();
            AWParallelSimObjectZoneUnits.Invalidate();
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(SimObjectsZones), "addUnit")]
        private static bool SimObjectsZonesAddUnit_Prefix(Actor pActor,
            WorldTile pTile)
        {
            bool valid = ActorKingdomSafetyRules.
                CanEnterVanillaZoneProcessing(
                    pActor?.data != null, pActor?.asset != null,
                    pTile?.data != null &&
                        pActor?.current_tile?.data != null,
                    pActor?.profession_asset != null,
                    pActor?.kingdom?.asset != null);
            if (valid) return true;
            ActorKingdomSafetyService.QueueRepair(pActor);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(City), "updateConquest")]
        private static bool CityUpdateConquest_Prefix(Actor pActor)
        {
            bool valid = ActorKingdomSafetyRules.
                CanEnterVanillaZoneProcessing(
                    pActor?.data != null, pActor?.asset != null,
                    pActor?.current_tile?.data != null,
                    pActor?.profession_asset != null,
                    pActor?.kingdom?.asset != null);
            if (valid) return true;
            ActorKingdomSafetyService.QueueRepair(pActor);
            return false;
        }

    }
}
