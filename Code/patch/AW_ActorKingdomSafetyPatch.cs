using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;
using System;
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
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(SimObjectsZones), "checkUnits")]
        private static void SimObjectsZonesCheckUnits_Prefix(
            out ActorKingdomSafetyService.ActorListIsolationState __state)
        {
            __state = ActorKingdomSafetyService.FilterRuntimeActors(
                pForZoneProcessing: true, pRequireKingdomForAlliance: false);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SimObjectsZones), "checkUnits")]
        private static void SimObjectsZonesCheckUnits_Postfix(
            ActorKingdomSafetyService.ActorListIsolationState __state)
        {
            ActorKingdomSafetyService.RestoreRuntimeActors(__state);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(SimObjectsZones), "checkUnits")]
        private static Exception SimObjectsZonesCheckUnits_Finalizer(
            ActorKingdomSafetyService.ActorListIsolationState __state,
            Exception __exception)
        {
            ActorKingdomSafetyService.RestoreRuntimeActors(__state);
            return __exception is NullReferenceException ? null : __exception;
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

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(City), "updateConquest")]
        private static Exception CityUpdateConquest_Finalizer(
            Exception __exception)
        {
            return __exception is NullReferenceException ? null : __exception;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(SimObjectsZones), "addUnit")]
        private static Exception SimObjectsZonesAddUnit_Finalizer(
            Exception __exception)
        {
            return __exception is NullReferenceException ? null : __exception;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(UnitLayer), "UpdateDirty")]
        private static void UnitLayerUpdateDirty_Prefix(
            out ActorKingdomSafetyService.ActorListIsolationState __state)
        {
            __state = ActorKingdomSafetyService.FilterRuntimeActors(
                pForZoneProcessing: false, pRequireKingdomForAlliance: true);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitLayer), "UpdateDirty")]
        private static void UnitLayerUpdateDirty_Postfix(
            ActorKingdomSafetyService.ActorListIsolationState __state)
        {
            ActorKingdomSafetyService.RestoreRuntimeActors(__state);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(UnitLayer), "UpdateDirty")]
        private static Exception UnitLayerUpdateDirty_Finalizer(
            ActorKingdomSafetyService.ActorListIsolationState __state,
            Exception __exception)
        {
            ActorKingdomSafetyService.RestoreRuntimeActors(__state);
            return __exception is NullReferenceException ? null : __exception;
        }
    }
}
