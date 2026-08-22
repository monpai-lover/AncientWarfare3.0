using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct BanditLoyaltyCityCandidate
    {
        internal BanditLoyaltyCityCandidate(long pCityId, int pLoyalty,
            bool pLive)
        {
            CityId = pCityId;
            Loyalty = pLoyalty;
            Live = pLive;
        }

        internal long CityId { get; }
        internal int Loyalty { get; }
        internal bool Live { get; }
    }

    internal static class PeasantRebelBanditSpawnRules
    {
        internal const int LoyaltyThreshold = -50;
        internal const int AnnualRecruitmentCap = 12;
        internal const int MinimumCityPopulation = 6;
        private const float BaseRecruitmentRate = 0.01f;
        private const float FamineRecruitmentBonus = 0.02f;
        private const float CorruptionRecruitmentBonus = 0.02f;

        internal static int CalculateAnnualRecruitment(int adultPopulation,
            bool famine, bool highCorruption, int currentPopulation)
        {
            int population = Math.Max(0, adultPopulation);
            int available = Math.Max(0, currentPopulation -
                MinimumCityPopulation);
            if (!famine && !highCorruption) return 0;

            float rate = BaseRecruitmentRate;
            if (famine) rate += FamineRecruitmentBonus;
            if (highCorruption) rate += CorruptionRecruitmentBonus;
            int result = (int)Math.Floor(population * rate);
            if (result < 1 && available > 0) result = 1;
            return Math.Min(AnnualRecruitmentCap, Math.Min(result, available));
        }

        internal static bool CanRecruitResident(bool adult,
            bool civilianProfession, bool king, bool cityLeader, bool heir)
        {
            return adult && civilianProfession && !king && !cityLeader &&
                   !heir;
        }

        internal static bool IsEligibleKingdom(bool pCivilization,
            bool pBandit, bool pNeutral, bool pRekt)
        {
            return pCivilization && !pBandit && !pNeutral && !pRekt;
        }

        internal static long SelectCandidateCityId(
            IEnumerable<BanditLoyaltyCityCandidate> pCandidates)
        {
            long selected = -1L;
            int selectedLoyalty = int.MaxValue;
            if (pCandidates == null) return selected;
            foreach (BanditLoyaltyCityCandidate candidate in pCandidates)
            {
                if (!candidate.Live || candidate.CityId <= 0 ||
                    candidate.Loyalty >= LoyaltyThreshold) continue;
                if (candidate.Loyalty > selectedLoyalty ||
                    candidate.Loyalty == selectedLoyalty && selected > 0 &&
                    candidate.CityId >= selected) continue;
                selected = candidate.CityId;
                selectedLoyalty = candidate.Loyalty;
            }
            return selected;
        }
    }
}
