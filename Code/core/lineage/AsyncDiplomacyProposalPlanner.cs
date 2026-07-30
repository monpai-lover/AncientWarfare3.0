using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class AsyncDiplomacyProposalPlanner
    {
        public static IReadOnlyList<AsyncStrategyCandidate> RankCandidates(
            KingdomStrategyFacts pSource,
            IEnumerable<AsyncDiplomacyProposalFacts> pCandidates,
            long worldSeed, int year, long sourceRevision)
        {
            var result = new List<AsyncStrategyCandidate>();
            if (!pSource.IsValid || pCandidates == null) return result;
            foreach (AsyncDiplomacyProposalFacts candidate in pCandidates)
            {
                if (candidate.TargetKingdomId < 0L ||
                    candidate.TargetKingdomId == pSource.KingdomId ||
                    candidate.ProposalKind == AsyncDiplomacyProposalKind.None ||
                    candidate.ActiveBlocker || candidate.Cooldown) continue;
                long salt = unchecked(
                    ((long)candidate.ProposalKind << 48) ^
                    candidate.TargetKingdomId);
                result.Add(new AsyncStrategyCandidate(
                    candidate.TargetKingdomId,
                    AsyncStrategyAction.DiplomacyProposal,
                    candidate.ProposalKind, candidate.Score,
                    AsyncStrategyDeterminism.Roll(worldSeed, year,
                        pSource.KingdomId, sourceRevision, salt)));
            }
            result.Sort(Compare);
            return result;
        }

        public static AsyncStrategyCandidate? SelectBest(
            KingdomStrategyFacts pSource,
            IEnumerable<AsyncDiplomacyProposalFacts> pCandidates,
            long worldSeed, int year, long sourceRevision)
        {
            IReadOnlyList<AsyncStrategyCandidate> ranked = RankCandidates(
                pSource, pCandidates, worldSeed, year, sourceRevision);
            return ranked.Count == 0 ? null : ranked[0];
        }

        private static int Compare(AsyncStrategyCandidate pFirst,
            AsyncStrategyCandidate pSecond)
        {
            return DiplomacyProposalOrderRules.Compare(pFirst.Score,
                (int)pFirst.ProposalKind, pFirst.TargetKingdomId,
                pSecond.Score, (int)pSecond.ProposalKind,
                pSecond.TargetKingdomId);
        }
    }
}
