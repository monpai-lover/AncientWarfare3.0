using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    internal static class AW_ActorMetaPartitionPatch
    {
        [HarmonyPrefix, HarmonyPatch(typeof(Actor), nameof(Actor.setAlive))]
        private static void SetAlivePrefix(
            Actor __instance,
            out bool __state)
        {
            __state = __instance.isAlive();
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Actor), nameof(Actor.setAlive))]
        private static void SetAlivePostfix(
            Actor __instance,
            bool pValue,
            bool __state)
        {
            AWActorMetaPartitionVersion.MarkAliveCall(
                __instance,
                __state,
                pValue);
            if (__state != pValue)
            {
                AWActorZoneMembershipDirtyIndex.Mark(
                    __instance,
                    AWActorZoneDirtyKind.Spatial |
                    AWActorZoneDirtyKind.CityEligibility);
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Actor), "setKingdom")]
        private static void SetKingdomPrefix(
            Actor __instance,
            Kingdom pKingdomToSet)
        {
            AWActorMetaPartitionVersion.MarkKingdomChange(
                __instance,
                pKingdomToSet);
            if (!ReferenceEquals(__instance.kingdom, pKingdomToSet))
            {
                AWActorZoneMembershipDirtyIndex.Mark(
                    __instance,
                    AWActorZoneDirtyKind.ChunkMetadata |
                    AWActorZoneDirtyKind.CityEligibility);
            }
        }
    }
}
