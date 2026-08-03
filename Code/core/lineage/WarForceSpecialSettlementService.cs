using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal enum WarForceSpecialSettlementResult
    {
        NotSpecial = 0,
        Handled = 1,
        Failed = 2
    }

    internal static class WarForceSpecialSettlementService
    {
        public static void ClearRuntime()
        {
            ZhuluWarSettlementService.ClearRuntime();
            RebellionCollapseSettlementService.ClearRuntime();
        }

        public static WarForceSpecialSettlementResult TrySettle(War pWar,
            WarForceEliminationDecision pDecision)
        {
            bool zhulu = ZhuluWarService.IsZhuluWar(pWar,
                requireActive: false);
            bool rebellion = false;
            try
            {
                rebellion = pWar?.data != null && !pWar.hasEnded() &&
                            pWar.getAsset()?.rebellion == true;
            }
            catch { }
            switch (WarForceEliminationRules.SpecialKind(zhulu, rebellion))
            {
                case WarForceSpecialSettlementKind.Zhulu:
                    return ZhuluWarSettlementService.
                        QueueForceElimination(pWar, pDecision)
                            ? WarForceSpecialSettlementResult.Handled
                            : WarForceSpecialSettlementResult.Failed;
                case WarForceSpecialSettlementKind.Rebellion:
                    return RebellionCollapseSettlementService.
                        QueueForceElimination(pWar, pDecision)
                            ? WarForceSpecialSettlementResult.Handled
                            : WarForceSpecialSettlementResult.Failed;
                default:
                    return WarForceSpecialSettlementResult.NotSpecial;
            }
        }

        internal static bool TransferAllCities(Kingdom pLoser,
            Kingdom pWinner)
        {
            if (pLoser?.data == null || pWinner?.data == null ||
                pLoser == pWinner) return false;
            foreach (City city in SnapshotCities(pLoser))
            {
                city.joinAnotherKingdom(pWinner, pCaptured: false,
                    pRebellion: false);
                if (city.kingdom != pWinner) return false;
            }
            return true;
        }

        internal static bool TransferScoreAffordableCities(War pWar,
            Kingdom pLoser, Kingdom pWinner, int pScore)
        {
            if (pWar?.data == null || pLoser?.data == null ||
                pWinner?.data == null) return false;
            int score = Math.Max(0, Math.Min(100, pScore));
            if (score == 0) return true;
            IReadOnlyList<WarPeaceDefaultTermCandidate> generated =
                WarPeaceSettlementService.Instance.
                    BuildDirectedTermCandidates(pWar, pLoser, pWinner);
            var territorial = new List<WarPeaceDefaultTermCandidate>();
            for (int i = 0; i < generated.Count; i++)
            {
                WarPeaceDefaultTermCandidate candidate = generated[i];
                if (candidate?.Eligible == true &&
                    candidate.Term?.Kind == WarPeaceTermKind.CedeCity)
                    territorial.Add(candidate);
            }
            IReadOnlyList<WarPeaceSettlementTermDraft> selected =
                WarPeaceDefaultOfferRules.SelectTerms(score,
                    WarPeaceDefaultOfferMode.ExhaustionMaximumBenefit,
                    territorial, SafeCityCount(pLoser));
            for (int i = 0; i < selected.Count; i++)
            {
                WarPeaceSettlementTermDraft term = selected[i];
                if (term?.Kind != WarPeaceTermKind.CedeCity) continue;
                City city = FindCity(term.CityId);
                if (city?.data == null || city.isRekt() ||
                    city.kingdom != pLoser) continue;
                city.joinAnotherKingdom(pWinner, pCaptured: false,
                    pRebellion: false);
                if (city.kingdom != pWinner) return false;
            }
            return true;
        }

        private static List<City> SnapshotCities(Kingdom pKingdom)
        {
            var result = new List<City>();
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (city?.data != null && !city.isRekt())
                        result.Add(city);
            }
            catch { }
            return result;
        }

        private static int SafeCityCount(Kingdom pKingdom)
        {
            try { return Math.Max(0, pKingdom?.countCities() ?? 0); }
            catch { return 0; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }
    }
}
