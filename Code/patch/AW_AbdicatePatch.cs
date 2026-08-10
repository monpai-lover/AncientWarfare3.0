using AncientWarfare3.core.lineage;
using AncientWarfare3.core.court;
using AncientWarfare3.api.multiplayer;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Records voluntary abdication after the native king-left transition.
    /// Actor death records its own chronicle event and is excluded here.
    /// </summary>
    [HarmonyPatch]
    public static class AW_AbdicatePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.kingLeftEvent))]
        public static void KingLeft_Prefix(Kingdom __instance, out Actor __state)
        {
            __state = __instance?.king;
            if (__state?.data == null) return;
            HeirService.RememberPreSuccessionKing(__instance, __state);
            ReigningRoyalLineageIndex.OnKingRemoved(__instance, __state);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.kingLeftEvent))]
        public static void KingLeft_Postfix(Kingdom __instance, Actor __state)
        {
            if (__state?.data == null) return;
            if (__instance?.king == __state)
            {
                ReigningRoyalLineageIndex.OnKingInstalled(__instance,
                    __state);
                return;
            }
            AccessionIdentityService.OnKingRemoved(__instance, __state);
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__state.data.id == AW_ActorDeathPatch.DyingKingActorId) return;
            CourtService.ClearOfficeForReignTransition(__state,
                "abdicated");
            ChronicleEvents.OnAbdicate(__instance, __state);
        }
    }
}
