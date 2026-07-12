using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_SchoolInfluencePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "die")]
        public static void Die_Prefix(Actor __instance)
        {
            SchoolMembershipService.OnDeath(__instance);
        }
    }
}
