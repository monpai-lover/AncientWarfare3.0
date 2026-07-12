using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_SchoolInfluencePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Actor), "die",
            new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        public static void Die_Prefix(Actor __instance)
        {
            if (__instance?.data != null)
            {
                HistoricalSchoolAffiliationSnapshot affiliation =
                    HistoricalAffiliationService.Get(__instance.data.id);
                if (affiliation?.LifecycleState == HistoricalSchoolLifecycleState.Serving)
                {
                    Kingdom host = World.world?.kingdoms?.get(affiliation.ServiceKingdomId);
                    CourtService.EndGuestOfficer(__instance, host, "death",
                        Date.getCurrentYear());
                }
            }
            if (__instance?.data != null && __instance.isAlive() &&
                !HistoricalSchoolDescentService.IsCanonicalMaster(__instance) &&
                SchoolLineageService.IsQualifiedTeacher(__instance))
                SchoolLineageService.OnTeacherDeath(__instance);
            SchoolMembershipService.OnDeath(__instance);
        }
    }
}
