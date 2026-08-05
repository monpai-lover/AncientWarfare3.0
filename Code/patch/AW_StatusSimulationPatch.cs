using System.Collections.Generic;
using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_StatusSimulationPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(StatusManager), nameof(StatusManager.update))]
        private static bool UpdateStatusLogic(StatusManager __instance,
            float pElapsed)
        {
            return !AWStatusSimulationScheduler.TryUpdate(__instance,
                pElapsed);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.checkSimManagerLists))]
        private static bool GateStatusListRebuild(MapBox __instance)
        {
            if (__instance?.list_all_sim_managers == null)
                return true;

            bool updateStatuses = AWStatusSimulationScheduler
                .ShouldRunListSync();
            List<BaseSystemManager> managers =
                __instance.list_all_sim_managers;
            for (int i = 0; i < managers.Count; i++)
            {
                BaseSystemManager manager = managers[i];
                if (!updateStatuses && manager is StatusManager) continue;
                manager.checkLists();
            }
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StatusManager), nameof(StatusManager.newStatus))]
        private static void RegisterScheduledStatus(Status __result)
        {
            AWStatusSimulationScheduler.NotifyAdded(__result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Status), nameof(Status.setDuration))]
        private static void RescheduleStatusDuration(Status __instance)
        {
            AWStatusSimulationScheduler.NotifyDurationChanged(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Status), nameof(Status.finish))]
        private static void ScheduleFinishedStatus(Status __instance)
        {
            AWStatusSimulationScheduler.NotifyFinished(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StatusManager), nameof(StatusManager.removeObject))]
        private static void UnregisterScheduledStatus(Status pObject)
        {
            AWStatusSimulationScheduler.NotifyRemoved(pObject);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ActorManager),
            nameof(ActorManager.checkSleepingUnits))]
        private static void SyncStatusesBeforeSleepingQuery()
        {
            AWStatusSimulationScheduler.EnsureListCurrent(
                World.world?.statuses);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearStatusScheduler()
        {
            AWStatusSimulationScheduler.ClearRuntime();
        }
    }
}
