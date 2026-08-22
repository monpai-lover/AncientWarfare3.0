using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class WarPeaceDefaultOfferRules
    {
        public const int MaximumCandidates = 32;

        public static bool IsCompleteSurrenderOffer(
            int recipientWarScore, int demandGross, int concessionGross,
            int maximumLegalConcessionGross)
        {
            return recipientWarScore >= WarPeaceTermsRules.MaximumWarScore &&
                   demandGross == 0 && concessionGross > 0 &&
                   maximumLegalConcessionGross > 0 &&
                   concessionGross >= maximumLegalConcessionGross;
        }

        public static IReadOnlyList<WarPeaceSettlementTermDraft> SelectTerms(
            int signedWarScore, WarPeaceDefaultOfferMode mode,
            IReadOnlyList<WarPeaceDefaultTermCandidate> candidates)
        {
            return SelectTerms(signedWarScore, mode, candidates, -1);
        }

        public static IReadOnlyList<WarPeaceSettlementTermDraft> SelectTerms(
            int signedWarScore, WarPeaceDefaultOfferMode mode,
            IReadOnlyList<WarPeaceDefaultTermCandidate> candidates,
            int sourceCityCount)
        {
            if (mode == WarPeaceDefaultOfferMode.WhitePeace)
                return new[]
                {
                    new WarPeaceSettlementTermDraft
                    {
                        Kind = WarPeaceTermKind.WhitePeace
                    }
                };

            var ordered = new List<IndexedCandidate>();
            if (candidates != null)
                for (int i = 0; i < candidates.Count; i++)
                {
                    WarPeaceDefaultTermCandidate candidate = candidates[i];
                    if (candidate?.Eligible == true &&
                        candidate.Term != null)
                        ordered.Add(new IndexedCandidate(candidate, i));
                }
            ordered.Sort(Compare);

            if (mode ==
                WarPeaceDefaultOfferMode.ExhaustionMaximumBenefit)
                return SelectMaximumBenefitTerms(signedWarScore, ordered,
                    sourceCityCount);

            var result = new List<WarPeaceSettlementTermDraft>();
            var ledger = new WarPeaceOfferLedger();
            int targetGross = Math.Min(WarPeaceOfferLedger.MaximumGross,
                Math.Abs(WarPeaceTermsRules.ClampSignedWarScore(
                    signedWarScore)));
            int selectedGross = 0;
            var cities = new HashSet<long>();
            var captives = new HashSet<long>();
            var claims = new HashSet<long>();
            bool subjectSelected = false;
            int selectedCededCities = 0;
            bool sourceSurvivalSelected = false;
            for (int i = 0; i < ordered.Count &&
                            result.Count <
                            WarPeaceSettlementValidationRules.MaximumTerms;
                 i++)
            {
                WarPeaceSettlementTermDraft term =
                    ordered[i].Candidate.Term;
                if (term.Kind == WarPeaceTermKind.CedeCity &&
                    (term.CityId < 0 || cities.Contains(term.CityId)))
                    continue;
                if (term.Kind == WarPeaceTermKind.ReleaseCaptives &&
                    (term.CaptiveActorId < 0 ||
                     captives.Contains(term.CaptiveActorId))) continue;
                if (term.Kind == WarPeaceTermKind.RenounceClaims &&
                    (term.ClaimId < 0 || claims.Contains(term.ClaimId)))
                    continue;
                bool subject = term.Kind ==
                                   WarPeaceTermKind.ForceVassal ||
                               term.Kind ==
                                   WarPeaceTermKind.ForceTributary;
                if (subject && subjectSelected) continue;
                bool requiresSurvival = WarPeaceTreatySurvivalRules
                    .RequiresSourceSurvival(term.Kind);
                int nextCededCities = selectedCededCities +
                    (term.Kind == WarPeaceTermKind.CedeCity ? 1 : 0);
                if (!WarPeaceTreatySurvivalRules.LeavesRequiredSourceAlive(
                        sourceCityCount, nextCededCities,
                        sourceSurvivalSelected || requiresSurvival))
                    continue;
                int cost = WarPeaceTermsRules.NormalizeTermCost(term.Kind,
                    term.RequestedCost);
                if (cost > targetGross - selectedGross) continue;
                if (!ledger.TryAddDemand(cost, out _))
                    continue;
                result.Add(term.Clone());
                selectedGross += cost;
                if (term.Kind == WarPeaceTermKind.CedeCity)
                {
                    cities.Add(term.CityId);
                    selectedCededCities++;
                }
                if (term.Kind == WarPeaceTermKind.ReleaseCaptives)
                    captives.Add(term.CaptiveActorId);
                if (term.Kind == WarPeaceTermKind.RenounceClaims)
                    claims.Add(term.ClaimId);
                if (subject) subjectSelected = true;
                if (requiresSurvival) sourceSurvivalSelected = true;
            }

            if (result.Count == 0)
                result.Add(new WarPeaceSettlementTermDraft
                {
                    Kind = WarPeaceTermKind.WhitePeace
                });
            return result;
        }

        private static IReadOnlyList<WarPeaceSettlementTermDraft>
            SelectMaximumBenefitTerms(int pSignedWarScore,
                IReadOnlyList<IndexedCandidate> pOrdered,
                int pSourceCityCount)
        {
            IReadOnlyList<IndexedCandidate> candidates =
                RemoveDuplicateIdentityCandidates(pOrdered);
            int targetGross = Math.Min(WarPeaceOfferLedger.MaximumGross,
                Math.Abs(WarPeaceTermsRules.ClampSignedWarScore(
                    pSignedWarScore)));
            var states = new Dictionary<BenefitStateKey, BenefitState>();
            var empty = new BenefitState();
            states[new BenefitStateKey(empty)] = empty;

            for (int candidateIndex = 0;
                 candidateIndex < candidates.Count; candidateIndex++)
            {
                IndexedCandidate candidate = candidates[candidateIndex];
                var snapshot = new List<BenefitState>(states.Values);
                for (int stateIndex = 0; stateIndex < snapshot.Count;
                     stateIndex++)
                {
                    BenefitState state = snapshot[stateIndex];
                    if (state.Terms.Count >=
                        WarPeaceSettlementValidationRules.MaximumTerms)
                        continue;
                    WarPeaceSettlementTermDraft term =
                        candidate.Candidate.Term;
                    int cost = WarPeaceTermsRules.NormalizeTermCost(
                        term.Kind, term.RequestedCost);
                    if (cost <= 0 || state.Gross > targetGross - cost)
                        continue;
                    bool subject = term.Kind ==
                                       WarPeaceTermKind.ForceVassal ||
                                   term.Kind ==
                                       WarPeaceTermKind.ForceTributary;
                    if (subject && state.SubjectSelected) continue;
                    bool requiresSurvival = WarPeaceTreatySurvivalRules.
                        RequiresSourceSurvival(term.Kind);
                    int cededCities = state.CededCities +
                        (term.Kind == WarPeaceTermKind.CedeCity ? 1 : 0);
                    bool survivalSelected =
                        state.SourceSurvivalSelected || requiresSurvival;
                    if (!WarPeaceTreatySurvivalRules.
                            LeavesRequiredSourceAlive(pSourceCityCount,
                                cededCities, survivalSelected)) continue;

                    BenefitState next = state.Add(candidate, cost,
                        subject, cededCities, survivalSelected);
                    var key = new BenefitStateKey(next);
                    if (!states.TryGetValue(key,
                            out BenefitState previous) ||
                        IsPreferredBenefitState(next, previous))
                        states[key] = next;
                }
            }

            BenefitState best = null;
            foreach (BenefitState state in states.Values)
                if (best == null || IsPreferredBenefitState(state, best))
                    best = state;
            if (best == null || best.Gross <= 0)
                return new[]
                {
                    new WarPeaceSettlementTermDraft
                    {
                        Kind = WarPeaceTermKind.WhitePeace
                    }
                };
            var result = new List<WarPeaceSettlementTermDraft>(
                best.Terms.Count);
            for (int i = 0; i < best.Terms.Count; i++)
                result.Add(best.Terms[i].Candidate.Term.Clone());
            return result;
        }

        private static IReadOnlyList<IndexedCandidate>
            RemoveDuplicateIdentityCandidates(
                IReadOnlyList<IndexedCandidate> pOrdered)
        {
            var result = new List<IndexedCandidate>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            if (pOrdered == null) return result;
            for (int i = 0; i < pOrdered.Count; i++)
            {
                IndexedCandidate candidate = pOrdered[i];
                WarPeaceSettlementTermDraft term = candidate.Candidate.Term;
                string identity = term.Kind switch
                {
                    WarPeaceTermKind.CedeCity => "city:" + term.CityId,
                    WarPeaceTermKind.ReleaseCaptives =>
                        "captive:" + term.CaptiveActorId,
                    WarPeaceTermKind.RenounceClaims =>
                        "claim:" + term.ClaimId,
                    _ => ""
                };
                if (identity.Length > 0 && !identities.Add(identity))
                    continue;
                result.Add(candidate);
            }
            return result;
        }

        private static bool IsPreferredBenefitState(BenefitState pLeft,
            BenefitState pRight)
        {
            if (pRight == null) return pLeft != null;
            if (pLeft == null) return false;
            if (pLeft.WarGoalCount != pRight.WarGoalCount)
                return pLeft.WarGoalCount > pRight.WarGoalCount;
            // A terminal settlement is primarily territorial. Once declared
            // war goals are preserved, prefer taking more cities, then more
            // territorial value, before liquid compensation or subjects.
            if (pLeft.CededCities != pRight.CededCities)
                return pLeft.CededCities > pRight.CededCities;
            if (pLeft.CessionGross != pRight.CessionGross)
                return pLeft.CessionGross > pRight.CessionGross;
            if (pLeft.Gross != pRight.Gross)
                return pLeft.Gross > pRight.Gross;
            if (pLeft.PriorityTotal != pRight.PriorityTotal)
                return pLeft.PriorityTotal > pRight.PriorityTotal;
            int count = Math.Min(pLeft.Terms.Count, pRight.Terms.Count);
            for (int i = 0; i < count; i++)
                if (pLeft.Terms[i].Index != pRight.Terms[i].Index)
                    return pLeft.Terms[i].Index < pRight.Terms[i].Index;
            return pLeft.Terms.Count < pRight.Terms.Count;
        }

        private sealed class BenefitState
        {
            internal readonly List<IndexedCandidate> Terms =
                new List<IndexedCandidate>();
            internal int Gross;
            internal int CessionGross;
            internal int WarGoalCount;
            internal long PriorityTotal;
            internal bool SubjectSelected;
            internal int CededCities;
            internal bool SourceSurvivalSelected;

            internal BenefitState Add(IndexedCandidate pCandidate,
                int pCost, bool pSubject, int pCededCities,
                bool pSourceSurvivalSelected)
            {
                var next = new BenefitState
                {
                    Gross = Gross + pCost,
                    CessionGross = CessionGross +
                        (pCandidate.Candidate.Term.Kind ==
                         WarPeaceTermKind.CedeCity ? pCost : 0),
                    WarGoalCount = WarGoalCount +
                                   (pCandidate.Candidate.IsWarGoal ? 1 : 0),
                    PriorityTotal = PriorityTotal +
                                    pCandidate.Candidate.Priority,
                    SubjectSelected = SubjectSelected || pSubject,
                    CededCities = pCededCities,
                    SourceSurvivalSelected = pSourceSurvivalSelected
                };
                next.Terms.AddRange(Terms);
                next.Terms.Add(pCandidate);
                return next;
            }
        }

        private readonly struct BenefitStateKey :
            IEquatable<BenefitStateKey>
        {
            internal BenefitStateKey(BenefitState pState)
            {
                Gross = pState.Gross;
                CessionGross = pState.CessionGross;
                Count = pState.Terms.Count;
                SubjectSelected = pState.SubjectSelected;
                CededCities = pState.CededCities;
                SourceSurvivalSelected = pState.SourceSurvivalSelected;
            }

            private int Gross { get; }
            private int CessionGross { get; }
            private int Count { get; }
            private bool SubjectSelected { get; }
            private int CededCities { get; }
            private bool SourceSurvivalSelected { get; }

            public bool Equals(BenefitStateKey pOther)
            {
                return Gross == pOther.Gross &&
                       CessionGross == pOther.CessionGross &&
                       Count == pOther.Count &&
                       SubjectSelected == pOther.SubjectSelected &&
                       CededCities == pOther.CededCities &&
                       SourceSurvivalSelected ==
                       pOther.SourceSurvivalSelected;
            }

            public override bool Equals(object pObject)
            {
                return pObject is BenefitStateKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Gross;
                    hash = hash * 397 ^ CessionGross;
                    hash = hash * 397 ^ Count;
                    hash = hash * 397 ^ SubjectSelected.GetHashCode();
                    hash = hash * 397 ^ CededCities;
                    hash = hash * 397 ^
                           SourceSurvivalSelected.GetHashCode();
                    return hash;
                }
            }
        }

        private static int Compare(IndexedCandidate left,
            IndexedCandidate right)
        {
            int goal = right.Candidate.IsWarGoal.CompareTo(
                left.Candidate.IsWarGoal);
            if (goal != 0) return goal;
            int priority = right.Candidate.Priority.CompareTo(
                left.Candidate.Priority);
            return priority != 0 ? priority :
                left.Index.CompareTo(right.Index);
        }

        private readonly struct IndexedCandidate
        {
            public IndexedCandidate(WarPeaceDefaultTermCandidate candidate,
                int index)
            {
                Candidate = candidate;
                Index = index;
            }

            public WarPeaceDefaultTermCandidate Candidate { get; }
            public int Index { get; }
        }
    }
}
