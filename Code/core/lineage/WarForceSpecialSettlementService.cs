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
            RebellionCollapseSettlementService.ClearRuntime();
        }

        public static WarForceSpecialSettlementResult
            TrySettleZhuluZeroForce(War pWar)
        {
            if (!ZhuluWarService.IsZhuluWar(pWar))
                return WarForceSpecialSettlementResult.NotSpecial;
            ZhuluZeroForceFallback fallback;
            try
            {
                fallback = ZhuluWarRules.ResolveZeroForceFallback(
                    pWar.countAttackersWarriors(),
                    pWar.countDefendersWarriors());
            }
            catch
            {
                return WarForceSpecialSettlementResult.Failed;
            }
            if (fallback == ZhuluZeroForceFallback.None)
                return WarForceSpecialSettlementResult.NotSpecial;

            try
            {
                if (fallback == ZhuluZeroForceFallback.Peace)
                {
                    EndIfLive(pWar, WarWinner.Peace);
                    return WarForceSpecialSettlementResult.Handled;
                }

                bool attackersWin = fallback ==
                                    ZhuluZeroForceFallback.AttackersWin;
                Kingdom winner = attackersWin
                    ? pWar.getMainAttacker()
                    : ZhuluWarService.ResolveLiveDeclaredDefender(pWar);
                IEnumerable<Kingdom> losers = attackersWin
                    ? pWar.getDefenders()
                    : pWar.getAttackers();
                if (winner?.data == null || winner.isRekt() ||
                    !TransferAllCities(pWar, losers, winner))
                    return WarForceSpecialSettlementResult.Failed;
                EndIfLive(pWar, attackersWin
                    ? WarWinner.Attackers
                    : WarWinner.Defenders);
                return WarForceSpecialSettlementResult.Handled;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Zhulu zero-force fallback failed war=" +
                                    (pWar?.data?.id ?? -1L) + ": " +
                                    exception.Message);
                return WarForceSpecialSettlementResult.Failed;
            }
        }

        public static WarForceSpecialSettlementResult TrySettle(War pWar,
            WarForceEliminationDecision pDecision)
        {
            bool rebellion = false;
            try
            {
                rebellion = pWar?.data != null && !pWar.hasEnded() &&
                            pWar.getAsset()?.rebellion == true;
            }
            catch { }
            switch (WarForceEliminationRules.SpecialKind(rebellion))
            {
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

        internal static bool TransferAllCities(War pWar,
            IEnumerable<Kingdom> pLosers, Kingdom pWinner)
        {
            if (pLosers == null ||
                !CanContinueForcedTransfer(pWar, pWinner)) return false;
            var cities = new List<City>();
            foreach (Kingdom loser in pLosers)
            {
                if (loser?.data == null || loser == pWinner) continue;
                cities.AddRange(SnapshotCities(loser));
            }
            for (int i = 0; i < cities.Count; i++)
            {
                if (!CanContinueForcedTransfer(pWar, pWinner))
                    return !IsWarActive(pWar);
                City city = cities[i];
                if (city?.data == null || city.isRekt() ||
                    city.kingdom == pWinner) continue;
                city.joinAnotherKingdom(pWinner, pCaptured: false,
                    pRebellion: false);
                if (city.kingdom != pWinner) return false;
                if (!CanContinueForcedTransfer(pWar, pWinner))
                    return !IsWarActive(pWar);
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

        private static bool CanContinueForcedTransfer(War pWar,
            Kingdom pWinner)
        {
            bool recipientValid;
            try
            {
                recipientValid = pWinner?.data != null &&
                                 !pWinner.isRekt() && pWinner.isAlive();
            }
            catch { recipientValid = false; }
            return ZhuluWarRules.CanContinueForcedTransfer(
                IsWarActive(pWar), recipientValid);
        }

        private static bool IsWarActive(War pWar)
        {
            try { return pWar?.data != null && !pWar.hasEnded(); }
            catch { return false; }
        }

        private static void EndIfLive(War pWar, WarWinner pWinner)
        {
            bool live;
            try { live = pWar?.data != null && !pWar.hasEnded(); }
            catch { live = false; }
            if (live) World.world?.wars?.endWar(pWar, pWinner);
        }
    }
}
