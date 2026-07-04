using System;
using AncientWarfare3.core.lineage;

namespace WarFabricationRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectBlocked("same_kingdom_or_invalid",
                pForeignCivilTarget: false,
                pTargetCityOwnedByTarget: true,
                pNeighboringCity: true,
                pBlockedByVassalRelation: false);

            ExpectBlocked("target_city_invalid",
                pForeignCivilTarget: true,
                pTargetCityOwnedByTarget: false,
                pNeighboringCity: true,
                pBlockedByVassalRelation: false);

            ExpectBlocked("not_neighbor",
                pForeignCivilTarget: true,
                pTargetCityOwnedByTarget: true,
                pNeighboringCity: false,
                pBlockedByVassalRelation: false);

            ExpectBlocked("vassal_annex_by_decision",
                pForeignCivilTarget: true,
                pTargetCityOwnedByTarget: true,
                pNeighboringCity: true,
                pBlockedByVassalRelation: true);

            ExpectAllowed(
                pForeignCivilTarget: true,
                pTargetCityOwnedByTarget: true,
                pNeighboringCity: true,
                pBlockedByVassalRelation: false);

            Console.WriteLine("War fabrication rule tests passed.");
            return 0;
        }

        private static void ExpectBlocked(string pReason,
            bool pForeignCivilTarget,
            bool pTargetCityOwnedByTarget,
            bool pNeighboringCity,
            bool pBlockedByVassalRelation)
        {
            bool allowed = WarFabricationRules.CanFabricate(
                pForeignCivilTarget,
                pTargetCityOwnedByTarget,
                pNeighboringCity,
                pBlockedByVassalRelation,
                out string reason);
            if (allowed || reason != pReason)
                throw new Exception($"Expected block '{pReason}', got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectAllowed(
            bool pForeignCivilTarget,
            bool pTargetCityOwnedByTarget,
            bool pNeighboringCity,
            bool pBlockedByVassalRelation)
        {
            bool allowed = WarFabricationRules.CanFabricate(
                pForeignCivilTarget,
                pTargetCityOwnedByTarget,
                pNeighboringCity,
                pBlockedByVassalRelation,
                out string reason);
            if (!allowed || reason != "")
                throw new Exception($"Expected allowed fabrication, got allowed={allowed}, reason='{reason}'.");
        }
    }
}
