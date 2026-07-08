using System;

namespace AncientWarfare3.core.policy
{
    public static class CapitalMoveRules
    {
        public static bool CanConsiderCandidate(bool pCandidateAlive, bool pIsCurrentCapital, bool pIsCoreCity,
            bool pHasOwnNeighbor)
        {
            return pCandidateAlive && !pIsCurrentCapital && pIsCoreCity && pHasOwnNeighbor;
        }

        public static float ScoreCity(float pCityAge, float pCurrentCapitalAge, int pPopulation,
            int pCurrentPopulation, int pZones, int pCurrentZones, int pOwnNeighborCount, float pCentralityScore)
        {
            float ageScore = (pCityAge - pCurrentCapitalAge) * 0.4f;
            float populationScore = (pPopulation - pCurrentPopulation) * 2f;
            float zoneScore = (pZones - pCurrentZones) * 0.55f;
            float neighborScore = Math.Max(0, pOwnNeighborCount) * 30f;
            return ageScore + populationScore + zoneScore + neighborScore + pCentralityScore;
        }

        public static bool ShouldMoveCapital(float pCurrentScore, float pBestScore)
        {
            float improvement = pBestScore - pCurrentScore;
            float threshold = Math.Max(20f, Math.Abs(pCurrentScore) * 0.30f);
            return improvement > threshold;
        }
    }
}
