using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_WarRefugeePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.joinCity))]
        private static void ActorJoinCity_Postfix(Actor __instance, City pCity)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            WarRefugeeService.OnActorJoinedCity(__instance, pCity);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BabyHelper), nameof(BabyHelper.applyParentsMeta))]
        private static void Birth_Postfix(Actor pParent1, Actor pParent2,
            Actor pBaby)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            WarRefugeeService.OnActorBorn(pBaby, pParent1, pParent2);
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorld_Prefix()
        {
            if (!AWAsyncClearWorldGuard.CleanupAllowed) return;
            WarRefugeeService.Reset();
        }
    }
}
