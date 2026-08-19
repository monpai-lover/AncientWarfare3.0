using System.Collections.Generic;
using System.Reflection;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_SyntheticPopulationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodInfo people = AccessTools.Method(typeof(City),
                nameof(City.getPopulationPeople));
            if (people != null) yield return people;

            MethodInfo total = AccessTools.Method(typeof(City),
                "getPopulationTotal", new[] { typeof(bool) });
            if (total != null) yield return total;
        }

        [HarmonyPostfix]
        private static void ExcludeSynthetic_Postfix(City __instance,
            ref int __result)
        {
            if (__instance?.data == null) return;
            __result = SyntheticLevyRules.AuthenticPopulation(__result,
                SyntheticMobilizationLedgerService.LiveSyntheticForCity(
                    __instance.data.id));
        }
    }
}
