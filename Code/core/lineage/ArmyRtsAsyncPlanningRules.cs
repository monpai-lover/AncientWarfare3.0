using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct ArmyRtsAsyncPlanStamp
    {
        internal ArmyRtsAsyncPlanStamp(long pWorldGeneration,
            long pKingdomId, int pDirectorGeneration, long pWarId,
            long pCityFactsRevision)
        {
            WorldGeneration = pWorldGeneration;
            KingdomId = pKingdomId;
            DirectorGeneration = pDirectorGeneration;
            WarId = pWarId;
            CityFactsRevision = pCityFactsRevision;
        }

        internal long WorldGeneration { get; }
        internal long KingdomId { get; }
        internal int DirectorGeneration { get; }
        internal long WarId { get; }
        internal long CityFactsRevision { get; }
    }

    internal readonly struct ArmyRtsAsyncFrontCandidate
    {
        internal ArmyRtsAsyncFrontCandidate(long pCityId, int pScore)
        {
            CityId = pCityId;
            Score = pScore;
        }

        internal long CityId { get; }
        internal int Score { get; }
    }

    internal static class ArmyRtsAsyncPlanningRules
    {
        internal static bool Accept(ArmyRtsAsyncPlanStamp pStamp,
            long pCurrentWorldGeneration, long pCurrentKingdomId,
            int pCurrentDirectorGeneration, long pCurrentWarId,
            long pCurrentCityFactsRevision)
        {
            return pStamp.WorldGeneration == pCurrentWorldGeneration &&
                   pStamp.KingdomId == pCurrentKingdomId &&
                   pStamp.DirectorGeneration == pCurrentDirectorGeneration &&
                   pStamp.WarId == pCurrentWarId &&
                   pStamp.CityFactsRevision == pCurrentCityFactsRevision;
        }

        internal static IReadOnlyList<ArmyRtsAsyncFrontCandidate> Rank(
            IReadOnlyList<ArmyRtsAsyncFrontCandidate> pCandidates)
        {
            var ranked = new List<ArmyRtsAsyncFrontCandidate>(
                pCandidates?.Count ?? 0);
            if (pCandidates != null)
                for (int index = 0; index < pCandidates.Count; index++)
                {
                    ArmyRtsAsyncFrontCandidate candidate =
                        pCandidates[index];
                    if (candidate.CityId >= 0L) ranked.Add(candidate);
                }
            ranked.Sort(CompareCandidates);
            return ranked.ToArray();
        }

        private static int CompareCandidates(
            ArmyRtsAsyncFrontCandidate pLeft,
            ArmyRtsAsyncFrontCandidate pRight)
        {
            int score = pRight.Score.CompareTo(pLeft.Score);
            return score != 0
                ? score
                : pLeft.CityId.CompareTo(pRight.CityId);
        }
    }
}
