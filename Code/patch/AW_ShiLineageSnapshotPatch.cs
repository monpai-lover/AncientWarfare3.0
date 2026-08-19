using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ShiLineageSnapshotPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.joinCity))]
        private static void ActorJoinCity_Prefix(Actor __instance,
            out City __state)
        {
            __state = __instance?.city;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.joinCity))]
        private static void ActorJoinCity_Postfix(Actor __instance,
            City pCity, City __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkDirty(__state);
            CityShiInfluenceSnapshotService.MarkDirty(
                __instance?.city ?? pCity);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LineageService), nameof(LineageService.OnActorBorn))]
        private static void ActorBorn_Postfix(Actor pActor)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkActorDirty(pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LineageService), nameof(LineageService.OnActorBornWithParents))]
        private static void ActorBornWithParents_Postfix(Actor pBaby)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkActorDirty(pBaby);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LineageService), nameof(LineageService.OnActorPromoted))]
        private static void ActorPromoted_Postfix(Actor pActor)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkActorDirty(pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LineageService), nameof(LineageService.EnsureLineageForNoble))]
        private static void NobleLineage_Postfix(Actor pActor)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkActorDirty(pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LineageService), nameof(LineageService.EnsureOfficialShiAndClan))]
        private static void OfficialShi_Postfix(Actor pActor)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkActorDirty(pActor);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "die",
            new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        private static void ActorDie_Prefix(Actor __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkActorDirty(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.setLeader))]
        private static void CityLeaderChanged_Postfix(City __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkDirty(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.removeLeader))]
        private static void CityLeaderRemoved_Postfix(City __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkDirty(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        private static void KingChanged_Postfix(Kingdom __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkKingdomDirty(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HeirService), nameof(HeirService.RefreshHeir))]
        private static void HeirChanged_Postfix(Kingdom pKingdom)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkKingdomDirty(pKingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HeirService), nameof(HeirService.ClearHeir))]
        private static void HeirCleared_Postfix(Kingdom pKingdom)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkKingdomDirty(pKingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HeirService), nameof(HeirService.StoreSelectedHeir))]
        private static void HeirStored_Postfix(Kingdom pKingdom)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkKingdomDirty(pKingdom);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        private static void CityOwnershipChanged_Prefix(City __instance,
            out Kingdom __state)
        {
            __state = __instance?.kingdom;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        private static void CityOwnershipChanged_Postfix(City __instance,
            Kingdom pKingdom, Kingdom __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityShiInfluenceSnapshotService.MarkCityOwnershipChanged(
                __instance, __state, __instance?.kingdom ?? pKingdom);
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorld_Prefix()
        {
            if (!AWAsyncClearWorldGuard.CleanupAllowed) return;
            CityShiInfluenceSnapshotService.Clear();
        }
    }
}
