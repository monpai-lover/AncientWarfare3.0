using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class WarPeaceBilateralCandidateRules
    {
        public static IReadOnlyList<WarPeaceDefaultTermCandidate> Build<TParty>(
            TParty pRequester, TParty pResponder,
            Func<TParty, TParty,
                IReadOnlyList<WarPeaceDefaultTermCandidate>>
                pBuildDirectedCandidates,
            int pMaximumPerDirection)
        {
            if (pBuildDirectedCandidates == null)
                throw new ArgumentNullException(
                    nameof(pBuildDirectedCandidates));
            IReadOnlyList<WarPeaceDefaultTermCandidate> demands =
                pBuildDirectedCandidates(pResponder, pRequester);
            IReadOnlyList<WarPeaceDefaultTermCandidate> concessions =
                pBuildDirectedCandidates(pRequester, pResponder);
            return Combine(demands, concessions, pMaximumPerDirection);
        }

        public static IReadOnlyList<WarPeaceDefaultTermCandidate>
            WithWhitePeace(
                IReadOnlyList<WarPeaceDefaultTermCandidate> pGenerated,
                int pMaximumSubstantiveCandidates)
        {
            int maximum = Math.Max(0, pMaximumSubstantiveCandidates);
            var result = new List<WarPeaceDefaultTermCandidate>(maximum + 1);
            WarPeaceDefaultTermCandidate whitePeace = null;
            for (int i = 0; i < (pGenerated?.Count ?? 0); i++)
            {
                WarPeaceDefaultTermCandidate candidate = pGenerated[i];
                if (candidate?.Term == null) continue;
                if (candidate.Term.Kind == WarPeaceTermKind.WhitePeace)
                {
                    whitePeace ??= candidate;
                    continue;
                }
                if (result.Count < maximum) result.Add(candidate);
            }
            result.Add(whitePeace ?? WhitePeaceCandidate());
            return result.AsReadOnly();
        }

        public static IReadOnlyList<WarPeaceDefaultTermCandidate> Combine(
            IReadOnlyList<WarPeaceDefaultTermCandidate> pDemands,
            IReadOnlyList<WarPeaceDefaultTermCandidate> pConcessions,
            int pMaximumPerDirection)
        {
            int maximum = Math.Max(0, pMaximumPerDirection);
            var result = new List<WarPeaceDefaultTermCandidate>(
                maximum * 2 + 1);
            WarPeaceDefaultTermCandidate whitePeace = null;
            AppendDirection(result, pDemands, maximum, ref whitePeace);
            AppendDirection(result, pConcessions, maximum, ref whitePeace);
            result.Add(whitePeace ?? WhitePeaceCandidate());
            return result.AsReadOnly();
        }

        private static void AppendDirection(
            List<WarPeaceDefaultTermCandidate> pTarget,
            IReadOnlyList<WarPeaceDefaultTermCandidate> pCandidates,
            int pMaximum, ref WarPeaceDefaultTermCandidate pWhitePeace)
        {
            var substantive = new List<IndexedCandidate>();
            for (int i = 0; i < (pCandidates?.Count ?? 0); i++)
            {
                WarPeaceDefaultTermCandidate candidate = pCandidates[i];
                if (candidate?.Term == null) continue;
                if (candidate.Term.Kind == WarPeaceTermKind.WhitePeace)
                {
                    pWhitePeace ??= candidate;
                    continue;
                }
                if (ContainsIdentity(pTarget, candidate.Term) ||
                    ContainsIdentity(substantive, candidate.Term)) continue;
                substantive.Add(new IndexedCandidate(candidate, i));
            }
            substantive.Sort(Compare);
            for (int i = 0; i < substantive.Count && i < pMaximum; i++)
                pTarget.Add(substantive[i].Candidate);
        }

        private static int Compare(IndexedCandidate pLeft,
            IndexedCandidate pRight)
        {
            int priority = pRight.Candidate.Priority.CompareTo(
                pLeft.Candidate.Priority);
            return priority != 0 ? priority :
                pLeft.Index.CompareTo(pRight.Index);
        }

        private static bool ContainsIdentity(
            IReadOnlyList<WarPeaceDefaultTermCandidate> pCandidates,
            WarPeaceSettlementTermDraft pTerm)
        {
            for (int i = 0; i < (pCandidates?.Count ?? 0); i++)
                if (SameIdentity(pCandidates[i]?.Term, pTerm)) return true;
            return false;
        }

        private static bool ContainsIdentity(
            IReadOnlyList<IndexedCandidate> pCandidates,
            WarPeaceSettlementTermDraft pTerm)
        {
            for (int i = 0; i < (pCandidates?.Count ?? 0); i++)
                if (SameIdentity(pCandidates[i].Candidate?.Term, pTerm))
                    return true;
            return false;
        }

        private static bool SameIdentity(WarPeaceSettlementTermDraft pLeft,
            WarPeaceSettlementTermDraft pRight)
        {
            return pLeft != null && pRight != null &&
                   pLeft.Kind == pRight.Kind &&
                   pLeft.FromKingdomId == pRight.FromKingdomId &&
                   pLeft.ToKingdomId == pRight.ToKingdomId &&
                   pLeft.CityId == pRight.CityId &&
                   pLeft.CaptiveActorId == pRight.CaptiveActorId &&
                   pLeft.ClaimId == pRight.ClaimId &&
                   string.Equals(pLeft.ResourceId, pRight.ResourceId,
                       StringComparison.Ordinal);
        }

        private static WarPeaceDefaultTermCandidate WhitePeaceCandidate()
        {
            return new WarPeaceDefaultTermCandidate(
                new WarPeaceSettlementTermDraft
                {
                    Kind = WarPeaceTermKind.WhitePeace
                }, false, 0, true);
        }

        private readonly struct IndexedCandidate
        {
            internal IndexedCandidate(
                WarPeaceDefaultTermCandidate pCandidate, int pIndex)
            {
                Candidate = pCandidate;
                Index = pIndex;
            }

            internal WarPeaceDefaultTermCandidate Candidate { get; }
            internal int Index { get; }
        }
    }
}
