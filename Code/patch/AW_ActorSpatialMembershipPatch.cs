using HarmonyLib;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ActorSpatialMembershipPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "setCurrentTilePosition")]
        private static void SetCurrentTilePosition_Prefix(Actor __instance)
        {
            AWActorZoneMembershipDirtyIndex.Mark(__instance,
                AWActorZoneDirtyKind.Spatial);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "dispose")]
        private static void Dispose_Prefix(Actor __instance)
        {
            AWActorZoneMembershipDirtyIndex.Mark(__instance,
                AWActorZoneDirtyKind.Spatial | AWActorZoneDirtyKind.ChunkMetadata);
        }
    }
}
