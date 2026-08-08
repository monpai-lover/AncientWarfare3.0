using HarmonyLib;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ActorSpatialMembershipPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "setCurrentTile")]
        private static void SetCurrentTile_Prefix(
            Actor __instance,
            out WorldTile __state)
        {
            __state = __instance.current_tile;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "setCurrentTile")]
        private static void SetCurrentTile_Postfix(
            Actor __instance,
            WorldTile pTile,
            WorldTile __state)
        {
            if (!ReferenceEquals(__state, pTile))
            {
                AWActorZoneMembershipDirtyIndex.Mark(__instance,
                    AWActorZoneDirtyKind.Spatial);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "setProfession")]
        private static void SetProfession_Postfix(Actor __instance)
        {
            AWActorZoneMembershipDirtyIndex.Mark(__instance,
                AWActorZoneDirtyKind.CityEligibility);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.stayInBuilding))]
        private static void StayInBuilding_Postfix(Actor __instance)
        {
            AWActorZoneMembershipDirtyIndex.Mark(__instance,
                AWActorZoneDirtyKind.CityEligibility);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "exitBuilding")]
        private static void ExitBuilding_Postfix(Actor __instance)
        {
            AWActorZoneMembershipDirtyIndex.Mark(__instance,
                AWActorZoneDirtyKind.CityEligibility);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.clearManagers))]
        private static void ClearManagers_Postfix(Actor __instance)
        {
            AWActorZoneMembershipDirtyIndex.Mark(__instance,
                AWActorZoneDirtyKind.Spatial |
                AWActorZoneDirtyKind.ChunkMetadata |
                AWActorZoneDirtyKind.CityEligibility);
        }

    }
}
