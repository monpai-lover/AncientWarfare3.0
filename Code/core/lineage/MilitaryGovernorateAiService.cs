using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class MilitaryGovernorateAiService
    {
        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                !pKingdom.isCiv() || pKingdom.isNeutral()) return;
            MilitaryGovernorateSuccessionService.OnKingdomYear(pKingdom);
            int year = SafeYear();
            pKingdom.data.get(
                LineageKeys.MILITARY_GOVERNORATE_AI_LAST_EVALUATION_YEAR,
                out int lastEvaluationYear, -1);
            if (!MilitaryGovernorateRules.CanRunAnnualAi(year,
                    lastEvaluationYear)) return;
            pKingdom.data.set(
                LineageKeys.MILITARY_GOVERNORATE_AI_LAST_EVALUATION_YEAR,
                year);

            bool xiaSystem = XiaizationService.GetLevel(pKingdom) >=
                             XiaizationService.LevelXiaizedDynasty;
            bool overLimit = MilitaryGovernorateRules.CanCreate(xiaSystem,
                pKingdom.countCities(), pKingdom.getMaxCities());
            if (!overLimit)
            {
                pKingdom.data.set(
                    LineageKeys.MILITARY_GOVERNORATE_OVER_LIMIT_SINCE_YEAR,
                    -1);
                pKingdom.data.set(
                    LineageKeys.MILITARY_GOVERNORATE_CITY_CURSOR, 0);
                return;
            }

            pKingdom.data.get(
                LineageKeys.MILITARY_GOVERNORATE_OVER_LIMIT_SINCE_YEAR,
                out int overLimitSinceYear, -1);
            if (overLimitSinceYear < 0)
            {
                pKingdom.data.set(
                    LineageKeys.MILITARY_GOVERNORATE_OVER_LIMIT_SINCE_YEAR,
                    year);
                return;
            }
            if (!MilitaryGovernorateRules.HasPersistedOverLimit(year,
                    overLimitSinceYear)) return;

            pKingdom.data.get(LineageKeys.MILITARY_GOVERNORATE_CITY_CURSOR,
                out int cursor, 0);
            List<MilitaryGovernorateSeatCandidate> seats =
                MilitaryGovernorateCreationService.GetEligibleSeats(
                    pKingdom, cursor,
                    MilitaryGovernorateRules.CityScanBudget,
                    out int nextCursor);
            pKingdom.data.set(LineageKeys.MILITARY_GOVERNORATE_CITY_CURSOR,
                nextCursor);
            if (seats.Count == 0) return;

            List<MilitaryGovernorateGeneralCandidate> generals =
                MilitaryGovernorateCreationService.GetGeneralCandidates(
                    pKingdom, MilitaryGovernorateRules.GeneralScanBudget);
            if (generals.Count == 0) return;

            int created = 0;
            if (created < MilitaryGovernorateRules.AnnualCreationLimit &&
                MilitaryGovernorateCreationService.TryCreateFromCandidateBatch(
                    seats[0].City, generals[0].Actor, generals,
                    out _, out _))
                created++;
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return -1; }
        }
    }
}
