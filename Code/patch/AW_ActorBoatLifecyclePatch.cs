using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Keeps the AW3 boat index in sync with the native actor transport
    /// lifecycle. Native mode remains untouched so the original scheduler
    /// owns all boat checks when the large scheduler is disabled.
    /// </summary>
    [HarmonyPatch]
    internal static class AW_ActorBoatLifecyclePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BatchActors), "u1_checkInside")]
        private static bool CheckInsideBatchPrefix(
            BatchActors __instance)
        {
            if (!AWPerformanceSettings.EnableFramePriorityScheduler)
            {
                return true;
            }

            if (!AWInsideBoatActorIndex.TryGetSnapshot(
                    __instance,
                    out Actor[] actors,
                    out int count))
            {
                // Actors that were already aboard when the scheduler was
                // enabled have not passed through embarkInto yet. Keep the
                // native scan for this unindexed batch so they are not
                // stranded; subsequent lifecycle notifications use the
                // incremental index.
                return true;
            }

            int processed = 0;
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                if (actor == null ||
                    actor.data == null ||
                    !ReferenceEquals(actor.batch, __instance) ||
                    !actor.is_inside_boat)
                {
                    AWInsideBoatActorIndex.Notify(actor, false);
                    continue;
                }

                actor.u1_checkInside(0f);
                processed++;
            }

            AWInsideBoatActorIndex.RecordProcessed(processed);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.u1_checkInside))]
        private static bool CheckInsidePrefix(Actor __instance)
        {
            if (!AWPerformanceSettings.EnableFramePriorityScheduler)
            {
                return true;
            }

            if (__instance == null ||
                __instance.data == null ||
                !__instance.isInsideSomething())
            {
                return false;
            }

            if (__instance.is_inside_boat)
            {
                Actor boat = __instance.inside_boat?.actor ??
                    World.world?.units?.get(__instance.data.transportID);
                if (boat == null)
                {
                    __instance.is_inside_boat = false;
                    AWInsideBoatActorIndex.Notify(__instance, false);
                    return false;
                }

                __instance.setCurrentTilePosition(boat.current_tile);
                __instance.skipUpdates();
            }

            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.embarkInto))]
        private static void EmbarkIntoPostfix(Actor __instance)
        {
            AWInsideBoatActorIndex.Notify(__instance, true);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.exitBoat))]
        private static void ExitBoatPostfix(Actor __instance)
        {
            AWInsideBoatActorIndex.Notify(__instance, false);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.clearManagers))]
        private static void ClearManagersPostfix(Actor __instance)
        {
            AWInsideBoatActorIndex.Notify(__instance, false);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.Dispose))]
        private static void DisposePrefix(Actor __instance)
        {
            AWInsideBoatActorIndex.Notify(__instance, false);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorldPrefix()
        {
            AWInsideBoatActorIndex.Reset();
        }
    }
}
