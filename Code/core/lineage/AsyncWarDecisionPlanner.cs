using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class AsyncWarDecisionPlanner
    {
        public static IReadOnlyList<AsyncStrategyCandidate> RankCandidates(
            KingdomStrategyFacts pSource,
            IEnumerable<StrategyTargetFacts> pTargets, long worldSeed,
            int year, long sourceRevision)
        {
            var result = new List<AsyncStrategyCandidate>();
            if (!pSource.IsValid || pTargets == null) return result;
            IReadOnlyList<WarStrategyCandidate> ranked =
                WarStrategyCandidateRules.RankCandidates(pSource, pTargets);
            foreach (WarStrategyCandidate candidate in ranked)
            {
                long salt = unchecked(
                    ((long)AsyncStrategyAction.DeclareWar << 56) ^
                    ((long)candidate.Kind << 48) ^
                    candidate.TargetKingdomId);
                result.Add(new AsyncStrategyCandidate(
                    candidate.TargetKingdomId,
                    AsyncStrategyAction.DeclareWar,
                    AsyncDiplomacyProposalKind.None, candidate.Score,
                    AsyncStrategyDeterminism.Roll(worldSeed, year,
                        pSource.KingdomId, sourceRevision, salt),
                    candidate.Kind));
            }
            return result;
        }
    }
}
