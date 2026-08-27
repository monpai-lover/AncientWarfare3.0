using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal static class DeJureRegionMergeService
    {
        internal static IReadOnlyList<DeJureRegionMergeCandidate>
            GetMergeCandidates(Kingdom pKingdom)
        {
            var candidates = new List<DeJureRegionMergeCandidate>();
            if (pKingdom?.data == null || pKingdom.isRekt()) return candidates;

            IReadOnlyList<DeJureRegion> regions =
                DeJureRegionStore.ActiveRegions();
            if (regions == null || regions.Count < 2) return candidates;

            for (int leftIndex = 0; leftIndex < regions.Count; leftIndex++)
            {
                DeJureRegion left = regions[leftIndex];
                City leftCity = SingleCity(left);
                if (!IsEligible(leftCity, pKingdom)) continue;

                for (int rightIndex = leftIndex + 1;
                     rightIndex < regions.Count; rightIndex++)
                {
                    DeJureRegion right = regions[rightIndex];
                    City rightCity = SingleCity(right);
                    if (!IsEligible(rightCity, pKingdom) ||
                        leftCity.kingdom != rightCity.kingdom ||
                        !AreAdjacent(leftCity, rightCity)) continue;

                    DeJureRegion primary = left;
                    DeJureRegion secondary = right;
                    City primaryCity = leftCity;
                    City secondaryCity = rightCity;
                    if (ComparePrimary(leftCity, rightCity) > 0)
                    {
                        primary = right;
                        secondary = left;
                        primaryCity = rightCity;
                        secondaryCity = leftCity;
                    }

                    candidates.Add(new DeJureRegionMergeCandidate
                    {
                        PrimaryRegionId = primary.RegionId,
                        SecondaryRegionId = secondary.RegionId,
                        PrimaryCityId = primaryCity.data.id,
                        SecondaryCityId = secondaryCity.data.id,
                        PrimaryName = primary.RegionName ?? string.Empty,
                        SecondaryName = secondary.RegionName ?? string.Empty
                    });
                }
            }

            return candidates
                .OrderBy(p => p.PrimaryRegionId)
                .ThenBy(p => p.SecondaryRegionId)
                .ToArray();
        }

        internal static bool TryMerge(Kingdom pKingdom,
            long pPrimaryRegionId, long pSecondaryRegionId,
            out string pError)
        {
            pError = string.Empty;
            if (pKingdom?.data == null || pKingdom.isRekt())
            {
                pError = "invalid_kingdom";
                return false;
            }

            DeJureRegionMergeCandidate candidate = GetMergeCandidates(pKingdom)
                .FirstOrDefault(p => p.PrimaryRegionId == pPrimaryRegionId &&
                                     p.SecondaryRegionId == pSecondaryRegionId);
            if (candidate == null)
            {
                pError = "invalid_target";
                return false;
            }

            return DeJureRegionStore.TryMergeSingleCityRegions(
                pKingdom, pPrimaryRegionId, pSecondaryRegionId, out pError);
        }

        private static City SingleCity(DeJureRegion pRegion)
        {
            if (pRegion?.MemberCityIds == null ||
                pRegion.MemberCityIds.Count != 1) return null;
            try { return World.world?.cities?.get(pRegion.MemberCityIds[0]); }
            catch { return null; }
        }

        private static bool IsEligible(City pCity, Kingdom pKingdom)
        {
            if (pCity?.data == null || pCity.isRekt() || !pCity.isAlive() ||
                pCity.kingdom != pKingdom) return false;
            try
            {
                return DeJureRegionEligibilityRules.CanParticipate(
                    liveCity: true,
                    banditStronghold:
                        PeasantRebelBanditStrongholdService.IsStrongholdCity(
                            pCity));
            }
            catch { return false; }
        }

        private static bool AreAdjacent(City pLeft, City pRight)
        {
            if (pLeft?.data == null || pRight?.data == null) return false;
            try
            {
                bool leftToRight = pLeft.neighbours_cities != null &&
                    pLeft.neighbours_cities.Contains(pRight);
                bool rightToLeft = pRight.neighbours_cities != null &&
                    pRight.neighbours_cities.Contains(pLeft);
                return leftToRight && rightToLeft;
            }
            catch { return false; }
        }

        private static int ComparePrimary(City pLeft, City pRight)
        {
            int leftPopulation = SafePopulation(pLeft);
            int rightPopulation = SafePopulation(pRight);
            int leftEconomy = SafeEconomy(pLeft);
            int rightEconomy = SafeEconomy(pRight);
            return DeJureRegionMergeRules.ComparePrimary(leftPopulation,
                rightPopulation, leftEconomy, rightEconomy,
                pLeft.data.id, pRight.data.id);
        }

        private static int SafePopulation(City pCity)
        {
            try { return Math.Max(0, pCity?.getPopulationPeople() ?? 0); }
            catch { return 0; }
        }

        private static int SafeEconomy(City pCity)
        {
            try
            {
                return Math.Max(0, (int)Math.Round(
                    DevelopmentMapModeService.GetCityScore(pCity) * 100f));
            }
            catch { return 0; }
        }
    }
}
