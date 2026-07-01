using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Clan-related chronicle hooks.
    /// </summary>
    [HarmonyPatch]
    public static class AW_ClanEventPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Clan), nameof(Clan.addChief))]
        public static void AddChief_Postfix(Actor pActor)
        {
            ChronicleEvents.OnBecomeClanChief(pActor);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setClan))]
        public static void SetClan_Prefix(Actor __instance, Clan pObject, out bool __state)
        {
            __state = __instance != null && __instance.clan != null && pObject == null;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setClan))]
        public static void SetClan_Postfix(Actor __instance, bool __state)
        {
            if (!__state) return;
            if (__instance == null || __instance.clan != null) return;
            ChronicleEvents.OnExiledFromClan(__instance);
        }
    }
}
