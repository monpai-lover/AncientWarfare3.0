using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class HereditaryTitleSuccessionService
    {
        private const int MaximumCollateralMembers = 256;
        private const int MaximumAncestryDepth = 16;

        internal static Actor FindSuccessor(Actor pHolder,
            Kingdom pKingdom = null)
        {
            // Succession is resolved from the deceased holder's persisted
            // parent links, so the holder may already be marked dead/rekt.
            if (pHolder?.data == null || !pHolder.isSexMale()) return null;
            Kingdom kingdom = pKingdom ?? pHolder.kingdom;
            if (kingdom?.data == null) return null;

            var candidates = new List<HereditaryTitleSuccessionCandidate>();
            var seen = new HashSet<long>();
            var directIds = new HashSet<long>();
            try
            {
                foreach (Actor child in pHolder.getChildren(false))
                    AddDirectCandidate(candidates, seen, directIds, child,
                        pHolder, kingdom);
            }
            catch { }
            try
            {
                foreach (long childId in LineageQuery.GetChildIds(
                             pHolder.data.id))
                {
                    Actor child = ResolveActor(childId);
                    AddDirectCandidate(candidates, seen, directIds, child,
                        pHolder, kingdom);
                }
            }
            catch { }

            long lineageId = LineageQuery.GetActorLineageId(pHolder.data.id);
            if (lineageId >= 0L)
            {
                try
                {
                    foreach (long actorId in
                             LineageQuery.GetLivingLineageMemberIds(
                                 lineageId, MaximumCollateralMembers))
                    {
                        if (seen.Contains(actorId) || actorId ==
                            pHolder.data.id) continue;
                        Actor candidate = ResolveActor(actorId);
                        AddCollateralCandidate(candidates, seen, directIds,
                            candidate, pHolder, kingdom);
                    }
                }
                catch { }
            }
            return ResolveActor(HereditaryTitleSuccessionRules
                .SelectSuccessor(candidates));
        }

        private static void AddDirectCandidate(
            List<HereditaryTitleSuccessionCandidate> pCandidates,
            HashSet<long> pSeen, HashSet<long> pDirectIds, Actor pCandidate,
            Actor pHolder, Kingdom pKingdom)
        {
            if (pCandidate?.data == null || pCandidate == pHolder ||
                !pSeen.Add(pCandidate.data.id)) return;
            pDirectIds.Add(pCandidate.data.id);
            bool legitimate = true;
            pCandidate.data.get(LineageKeys.BIRTH_LEGITIMACY,
                out legitimate, true);
            bool eligible = IsBaseEligible(pCandidate, pKingdom) &&
                            pCandidate.isSexMale();
            pCandidates.Add(new HereditaryTitleSuccessionCandidate(
                pCandidate.data.id, eligible, directSon: true,
                legitimateBirth: legitimate, adult: SafeAdult(pCandidate),
                agnatic: true, kinDistance: 0,
                birthTime: SafeBirthTime(pCandidate)));
        }

        private static void AddCollateralCandidate(
            List<HereditaryTitleSuccessionCandidate> pCandidates,
            HashSet<long> pSeen, HashSet<long> pDirectIds, Actor pCandidate,
            Actor pHolder, Kingdom pKingdom)
        {
            if (pCandidate?.data == null || pCandidate == pHolder ||
                pDirectIds.Contains(pCandidate.data.id) ||
                !pSeen.Add(pCandidate.data.id) ||
                !IsBaseEligible(pCandidate, pKingdom) ||
                !pCandidate.isSexMale() || !SafeAdult(pCandidate)) return;
            if (LineageQuery.IsAgnaticDescendantOf(pCandidate.data.id,
                    pHolder.data.id)) return;
            LineageQuery.NearestCommonAgnaticAncestor(pHolder.data.id,
                pCandidate.data.id, out int holderDepth,
                out int candidateDepth);
            if (holderDepth < 0 || candidateDepth < 0 ||
                holderDepth > MaximumAncestryDepth ||
                candidateDepth > MaximumAncestryDepth) return;
            bool legitimate = true;
            pCandidate.data.get(LineageKeys.BIRTH_LEGITIMACY,
                out legitimate, true);
            pCandidates.Add(new HereditaryTitleSuccessionCandidate(
                pCandidate.data.id, eligible: true, directSon: false,
                legitimateBirth: legitimate, adult: true, agnatic: true,
                kinDistance: holderDepth + candidateDepth,
                birthTime: SafeBirthTime(pCandidate)));
        }

        private static bool IsBaseEligible(Actor pCandidate,
            Kingdom pKingdom)
        {
            try
            {
                return pCandidate.kingdom == pKingdom &&
                       pCandidate.isAlive() && !pCandidate.isRekt() &&
                       !pCandidate.hasTrait("madness") &&
                       !SlaveService.IsSlave(pCandidate);
            }
            catch { return false; }
        }

        private static bool SafeAdult(Actor pActor)
        {
            try { return pActor.isAdult(); }
            catch { return false; }
        }

        private static double SafeBirthTime(Actor pActor)
        {
            try { return pActor.data.created_time; }
            catch { return double.MaxValue; }
        }

        private static Actor ResolveActor(long pActorId)
        {
            if (pActorId < 0L) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }
    }
}
