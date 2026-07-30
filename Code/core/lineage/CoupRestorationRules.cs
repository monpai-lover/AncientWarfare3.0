using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum CoupRestorationWarWinner
    {
        None = 0,
        Loyalists = 1,
        Usurper = 2
    }

    public enum CoupRestorationSettlement
    {
        None = 0,
        RestoreOldDynasty = 1,
        ConfirmUsurper = 2,
        LeaveRivalClaim = 3
    }

    public enum CoupRestorationClaimantSource
    {
        None = 0,
        OldRuler = 1,
        LoyalistKing = 2,
        RecordedHeir = 3,
        DynasticFallback = 4
    }

    public readonly struct CoupRestorationSupportCandidate
    {
        public CoupRestorationSupportCandidate(long actorId, long cityId,
            int supportScore, int population)
        {
            ActorId = actorId;
            CityId = cityId;
            SupportScore = supportScore;
            Population = population;
        }

        public long ActorId { get; }
        public long CityId { get; }
        public int SupportScore { get; }
        public int Population { get; }
    }

    public static class CoupRestorationRules
    {
        public const string WarTypeId = "coup_restoration_war";
        public const int MinimumSeatPopulation = 25;
        public const int MinimumSupportScore = 60;
        public const int MaximumOfficerCandidates = 48;
        public const int MaximumCoalitionCities = 3;

        public static bool CanPrepare(bool monarchy, bool realmAtWar,
            int realmCityCount, bool oldRulerAlive)
        {
            return monarchy && !realmAtWar && realmCityCount >= 2 &&
                   oldRulerAlive;
        }

        public static bool CanUseSeat(bool cityAlive, bool ownedByRealm,
            bool isCapital, int population)
        {
            return cityAlive && ownedByRealm && !isCapital &&
                   population >= MinimumSeatPopulation;
        }

        public static int SupportScore(bool sameOldLineage,
            bool sameOldShi, bool sameUsurperLineage,
            bool sameUsurperShi, bool ambitious, bool content,
            bool general, bool governor, int institutionalLoyalty,
            int traitLoyalty)
        {
            int score = 0;
            if (sameOldLineage) score += 55;
            else if (sameOldShi) score += 35;
            if (sameUsurperLineage) score -= 55;
            else if (sameUsurperShi) score -= 35;
            if (ambitious) score -= 25;
            if (content) score += 10;
            if (general) score += 8;
            if (governor) score += 12;
            score += (Clamp(institutionalLoyalty, 0, 100) - 50) / 2;
            score += Clamp(traitLoyalty, -50, 50);
            return Clamp(score, -100, 150);
        }

        public static bool WillRise(int pSupportScore)
        {
            return pSupportScore >= MinimumSupportScore;
        }

        public static List<CoupRestorationSupportCandidate> SelectCoalition(
            IReadOnlyList<CoupRestorationSupportCandidate> pCandidates)
        {
            var result = new List<CoupRestorationSupportCandidate>(
                MaximumCoalitionCities);
            if (pCandidates == null) return result;
            for (int i = 0; i < pCandidates.Count; i++)
            {
                CoupRestorationSupportCandidate candidate = pCandidates[i];
                if (candidate.ActorId < 0 || candidate.CityId < 0 ||
                    !WillRise(candidate.SupportScore)) continue;
                int sameCityIndex = FindCity(result, candidate.CityId);
                if (sameCityIndex >= 0)
                {
                    if (CompareSupport(candidate, result[sameCityIndex]) >= 0)
                        continue;
                    result[sameCityIndex] = candidate;
                    SortBounded(result);
                    continue;
                }
                if (result.Count < MaximumCoalitionCities)
                {
                    result.Add(candidate);
                    SortBounded(result);
                    continue;
                }
                int worst = result.Count - 1;
                if (CompareSupport(candidate, result[worst]) >= 0) continue;
                result[worst] = candidate;
                SortBounded(result);
            }
            return result;
        }

        public static string EncodeCoalitionIds(IEnumerable<long> pIds)
        {
            if (pIds == null) return "";
            var result = new List<long>(MaximumCoalitionCities);
            foreach (long id in pIds)
            {
                if (id < 0 || result.Contains(id)) continue;
                result.Add(id);
                if (result.Count >= MaximumCoalitionCities) break;
            }
            return string.Join(",", result);
        }

        public static List<long> DecodeCoalitionIds(string pRaw)
        {
            var result = new List<long>(MaximumCoalitionCities);
            if (string.IsNullOrWhiteSpace(pRaw)) return result;
            string[] parts = pRaw.Split(',');
            for (int i = 0; i < parts.Length &&
                            result.Count < MaximumCoalitionCities; i++)
            {
                if (!long.TryParse(parts[i], out long id) || id < 0 ||
                    result.Contains(id)) continue;
                result.Add(id);
            }
            return result;
        }

        public static CoupRestorationSettlement ResolveSettlement(
            CoupRestorationWarWinner pWinner)
        {
            return pWinner switch
            {
                CoupRestorationWarWinner.Loyalists =>
                    CoupRestorationSettlement.RestoreOldDynasty,
                CoupRestorationWarWinner.Usurper =>
                    CoupRestorationSettlement.ConfirmUsurper,
                _ => CoupRestorationSettlement.LeaveRivalClaim
            };
        }

        public static CoupRestorationClaimantSource SelectClaimantSource(
            bool oldRulerEligible, bool loyalistKingEligible,
            bool recordedHeirEligible, bool dynasticFallbackEligible)
        {
            if (oldRulerEligible)
                return CoupRestorationClaimantSource.OldRuler;
            if (loyalistKingEligible)
                return CoupRestorationClaimantSource.LoyalistKing;
            if (recordedHeirEligible)
                return CoupRestorationClaimantSource.RecordedHeir;
            return dynasticFallbackEligible
                ? CoupRestorationClaimantSource.DynasticFallback
                : CoupRestorationClaimantSource.None;
        }

        public static bool ShouldFinalizeCapitalCapture(bool activeWar,
            bool correctWarType, bool newOwnerIsLoyalist,
            bool capturedOriginalCapital, bool winnerUnset)
        {
            return activeWar && correctWarType && newOwnerIsLoyalist &&
                   capturedOriginalCapital && winnerUnset;
        }

        private static int FindCity(
            IReadOnlyList<CoupRestorationSupportCandidate> pCandidates,
            long pCityId)
        {
            for (int i = 0; i < pCandidates.Count; i++)
                if (pCandidates[i].CityId == pCityId) return i;
            return -1;
        }

        private static int CompareSupport(
            CoupRestorationSupportCandidate pLeft,
            CoupRestorationSupportCandidate pRight)
        {
            int order = pRight.SupportScore.CompareTo(pLeft.SupportScore);
            if (order != 0) return order;
            order = pRight.Population.CompareTo(pLeft.Population);
            if (order != 0) return order;
            order = pLeft.ActorId.CompareTo(pRight.ActorId);
            return order != 0
                ? order
                : pLeft.CityId.CompareTo(pRight.CityId);
        }

        private static void SortBounded(
            List<CoupRestorationSupportCandidate> pCandidates)
        {
            for (int i = 1; i < pCandidates.Count; i++)
            {
                CoupRestorationSupportCandidate current = pCandidates[i];
                int insert = i;
                while (insert > 0 &&
                       CompareSupport(current, pCandidates[insert - 1]) < 0)
                {
                    pCandidates[insert] = pCandidates[insert - 1];
                    insert--;
                }
                pCandidates[insert] = current;
            }
        }

        private static int Clamp(int pValue, int pMinimum, int pMaximum)
        {
            return Math.Max(pMinimum, Math.Min(pMaximum, pValue));
        }
    }
}
