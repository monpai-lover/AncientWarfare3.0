using AncientWarfare3.core.lineage;
using AncientWarfare3.api.multiplayer;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Clan-related chronicle hooks.
    /// </summary>
    [HarmonyPatch]
    public static class AW_ClanEventPatch
    {
        public struct ClanChangeState
        {
            public bool hadClan;
            public long oldClanId;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Clan), nameof(Clan.addChief))]
        public static void AddChief_Postfix(Actor pActor)
        {
            ChronicleEvents.OnBecomeClanChief(pActor);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setClan))]
        public static void SetClan_Prefix(Actor __instance, Clan pObject, out ClanChangeState __state)
        {
            __state = new ClanChangeState
            {
                hadClan = __instance?.clan?.data != null,
                oldClanId = __instance?.clan?.data?.id ?? -1L
            };
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setClan))]
        public static void SetClan_Postfix(Actor __instance, ClanChangeState __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__instance?.data == null ||
                !LineageService.UsesNativeSiniticGenealogy(__instance)) return;

            LineageService.ArchiveActor(__instance, pAlive: true);

            Clan newClan = __instance.clan;
            long newClanId = newClan?.data?.id ?? -1L;
            if (newClanId >= 0)
            {
                if (newClanId != __state.oldClanId)
                    ChronicleEvents.OnJoinedOriginalClan(__instance, newClan);
                return;
            }

            if (__state.hadClan)
                ChronicleEvents.OnExiledFromClan(__instance);
        }
    }
}
