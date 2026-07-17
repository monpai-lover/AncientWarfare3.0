using AncientWarfare3.core.policy;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.court;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_KingdomPolicyPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.updateAge))]
        public static void UpdateAge_Postfix(Kingdom __instance)
        {
            if (__instance?.data == null || __instance.isRekt() || __instance.isNeutral()) return;

            HeirService.OnKingdomYear(__instance);
            RoyalAsylumService.OnKingdomYear(__instance);
            TemporaryLevyService.OnKingdomYear(__instance);
            WarNoticeService.OnKingdomYear(__instance);

            long benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                XiaContactService.OnKingdomYear(__instance);
                XiaizationService.OnKingdomYear(__instance);
            }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomXiaizationIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { KingdomPolicyService.OnKingdomYear(__instance); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomPolicyIndex, benchmark); }

            RoyalMedicalCareService.OnKingdomYear(__instance);
            CourtDirectionService.RecalculateIfDirty(__instance);
            CitySchoolSnapshotService.OnKingdomYear(__instance);
            CourtPeaceService.OnKingdomYear(__instance);

            benchmark = UpdateAgeBenchmark.Begin();
            try { CityTechService.OnKingdomYear(__instance); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCityTechIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { CityEconomyService.OnKingdomYear(__instance); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCityEconomyIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { VassalService.SettleAnnualTribute(__instance); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomVassalTributeIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { WarTerritoryService.OnKingdomYear(__instance); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomWarTerritoryIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { MandateService.OnKingdomYear(__instance); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomMandateIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { MandateDecisionService.OnKingdomYear(__instance); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomMandateDecisionIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { MandateRebelService.OnKingdomYear(__instance); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomMandateRebelIndex, benchmark); }

            benchmark = UpdateAgeBenchmark.Begin();
            try { ForeignOccupationService.OnKingdomYear(__instance); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomForeignOccupationIndex, benchmark); }

            int year = Date.getCurrentYear();
            long id = __instance.id;
            benchmark = UpdateAgeBenchmark.Begin();
            bool runHeavy;
            try { runHeavy = KingdomYearSchedulerRules.ShouldRunHeavySystem(year, id, pModulo: 2, pSlot: 0); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomHeavyScheduleIndex, benchmark); }

            if (runHeavy)
            {
                benchmark = UpdateAgeBenchmark.Begin();
                try { WarPlotRedirectService.OnKingdomYear(__instance); }
                finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomWarPlotIndex, benchmark); }

                benchmark = UpdateAgeBenchmark.Begin();
                try { WarDecisionAI.OnKingdomYear(__instance); }
                finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomWarAiIndex, benchmark); }

                benchmark = UpdateAgeBenchmark.Begin();
                try
                {
                    VassalService.OnKingdomYear(__instance);
                    VassalAIService.OnKingdomYear(__instance);
                }
                finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomVassalAiIndex, benchmark); }
            }

            benchmark = UpdateAgeBenchmark.Begin();
            bool runGeneral;
            try { runGeneral = KingdomYearSchedulerRules.ShouldRunHeavySystem(year, id, pModulo: 4, pSlot: 2); }
            finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomGeneralScheduleIndex, benchmark); }

            if (runGeneral)
            {
                benchmark = UpdateAgeBenchmark.Begin();
                try { GeneralService.OnKingdomYear(__instance); }
                finally { UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomGeneralIndex, benchmark); }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "makeOwnKingdom")]
        public static void MakeOwnKingdom_Prefix(City __instance, Actor pActor)
        {
            KingdomPolicyInheritanceService.RememberSplitSource(pActor, __instance?.kingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "makeOwnKingdom")]
        public static void MakeOwnKingdom_Postfix(Kingdom __result, Actor pActor)
        {
            KingdomPolicyInheritanceService.InheritForNewKingdom(__result, pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.makeNewCivKingdom))]
        public static void MakeNewCivKingdom_PolicyPostfix(Kingdom __result, Actor pActor)
        {
            KingdomPolicyInheritanceService.InheritForNewKingdom(__result, pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.getMaxCities))]
        public static void GetMaxCities_Postfix(Kingdom __instance, ref int __result)
        {
            if (__instance?.data == null || __instance.isRekt()) return;
            __result += KingdomTitleService.GetCitiesBonus(KingdomTitleService.GetTitle(__instance));
        }
    }
}
