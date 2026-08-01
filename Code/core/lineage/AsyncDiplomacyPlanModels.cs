using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    internal enum AsyncStrategyAction
    {
        None = 0,
        DeclareWar = 1,
        DiplomacyProposal = 2
    }

    internal enum AsyncDiplomacyProposalKind
    {
        None = 0,
        Alliance = 1,
        NonAggression = 2,
        RoyalMarriage = 3,
        Tributary = 4,
        Truce = 5,
        EndAlliance = 6,
        BreakNonAggression = 7,
        Coalition = 8,
        HouseholdOffering = 9,
        JoinWar = 10,
        Vassalize = 11,
        EndVassal = 12
    }

    internal enum WarStrategyCandidateKind
    {
        None = 0,
        Normal = 1,
        TakeMandate = 2,
        MandateConquest = 3,
        Zhulu = 4
    }

    internal readonly struct KingdomStrategyFacts
    {
        public KingdomStrategyFacts(long kingdomId, float power, float war,
            float peace, float aggression, long rootSuzerainId,
            float livelihood = .5f)
        {
            KingdomId = kingdomId;
            Power = FiniteNonNegative(power);
            War = FiniteNonNegative(war);
            Peace = FiniteNonNegative(peace);
            Aggression = FiniteNonNegative(aggression);
            Livelihood = FiniteNonNegative(livelihood);
            RootSuzerainId = rootSuzerainId;
        }

        public long KingdomId { get; }
        public float Power { get; }
        public float War { get; }
        public float Peace { get; }
        public float Aggression { get; }
        public float Livelihood { get; }
        public long RootSuzerainId { get; }
        public bool IsValid => KingdomId >= 0L && Power > 0f;

        private static float FiniteNonNegative(float pValue)
        {
            return float.IsNaN(pValue) || float.IsInfinity(pValue) || pValue < 0f
                ? 0f
                : pValue;
        }
    }

    internal readonly struct StrategyTargetFacts
    {
        public StrategyTargetFacts(long targetId, float power, int opinion,
            bool neighbor, bool atWar, bool warBlocked,
            WarStrategyCandidateKind preferredKind =
                WarStrategyCandidateKind.Normal,
            bool sameRoot = false, bool vassalBlocked = false,
            bool fabricationAvailable = true, bool sameAlliance = false,
            float sourceAlliancePower = 0f,
            float targetAlliancePower = 0f, int mandateValue = 0,
            float mandateCoreControl = 1f,
            bool zhuluEligible = false, float capitalDistance = 0f,
            bool zhuluAge = false)
        {
            TargetId = targetId;
            Power = float.IsNaN(power) || float.IsInfinity(power) || power < 0f
                ? 0f
                : power;
            Opinion = opinion;
            Neighbor = neighbor;
            AtWar = atWar;
            WarBlocked = warBlocked;
            PreferredKind = preferredKind;
            SameRoot = sameRoot;
            VassalBlocked = vassalBlocked;
            FabricationAvailable = fabricationAvailable;
            SameAlliance = sameAlliance;
            SourceAlliancePower = FiniteNonNegative(sourceAlliancePower);
            TargetAlliancePower = FiniteNonNegative(targetAlliancePower);
            MandateValue = mandateValue;
            MandateCoreControl = Clamp01(mandateCoreControl);
            ZhuluEligible = zhuluEligible;
            CapitalDistance = FiniteNonNegative(capitalDistance);
            ZhuluAge = zhuluAge;
        }

        public long TargetId { get; }
        public float Power { get; }
        public int Opinion { get; }
        public bool Neighbor { get; }
        public bool AtWar { get; }
        public bool WarBlocked { get; }
        public WarStrategyCandidateKind PreferredKind { get; }
        public bool SameRoot { get; }
        public bool VassalBlocked { get; }
        public bool FabricationAvailable { get; }
        public bool SameAlliance { get; }
        public float SourceAlliancePower { get; }
        public float TargetAlliancePower { get; }
        public int MandateValue { get; }
        public float MandateCoreControl { get; }
        public bool ZhuluEligible { get; }
        public float CapitalDistance { get; }
        public bool ZhuluAge { get; }

        private static float FiniteNonNegative(float pValue)
        {
            return float.IsNaN(pValue) || float.IsInfinity(pValue) ||
                   pValue < 0f ? 0f : pValue;
        }

        private static float Clamp01(float pValue)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue)) return 0f;
            return Math.Max(0f, Math.Min(1f, pValue));
        }
    }

    internal readonly struct AsyncDiplomacySelectionTargetFacts
    {
        public AsyncDiplomacySelectionTargetFacts(long targetKingdomId,
            float power, bool targetHasMandate, bool requesterAtWar,
            bool responderAtWar, bool targetAlive = true,
            bool targetCivilized = true, bool targetNeutral = false,
            bool subjectConflict = false,
            bool servingTargetInWar = false, bool duplicateTarget = false,
            bool membersAtWar = false, bool requesterSubject = false,
            bool responderSubject = false, int requesterActiveCount = 0,
            int responderActiveCount = 0, float strongerMemberPower = 0f,
            bool eligible = true, bool serviceReady = true,
            bool requesterAlive = true, bool responderAlive = true,
            bool distinctRealms = true)
        {
            TargetKingdomId = targetKingdomId;
            Power = float.IsNaN(power) || float.IsInfinity(power) || power < 0f
                ? 0f
                : power;
            TargetHasMandate = targetHasMandate;
            RequesterAtWar = requesterAtWar;
            ResponderAtWar = responderAtWar;
            TargetAlive = targetAlive;
            TargetCivilized = targetCivilized;
            TargetNeutral = targetNeutral;
            SubjectConflict = subjectConflict;
            ServingTargetInWar = servingTargetInWar;
            DuplicateTarget = duplicateTarget;
            MembersAtWar = membersAtWar;
            RequesterSubject = requesterSubject;
            ResponderSubject = responderSubject;
            RequesterActiveCount = Math.Max(0, requesterActiveCount);
            ResponderActiveCount = Math.Max(0, responderActiveCount);
            StrongerMemberPower = float.IsNaN(strongerMemberPower) ||
                                  float.IsInfinity(strongerMemberPower) ||
                                  strongerMemberPower < 0f
                ? 0f
                : strongerMemberPower;
            Eligible = eligible;
            ServiceReady = serviceReady;
            RequesterAlive = requesterAlive;
            ResponderAlive = responderAlive;
            DistinctRealms = distinctRealms;
        }

        public long TargetKingdomId { get; }
        public float Power { get; }
        public bool TargetHasMandate { get; }
        public bool RequesterAtWar { get; }
        public bool ResponderAtWar { get; }
        public bool TargetAlive { get; }
        public bool TargetCivilized { get; }
        public bool TargetNeutral { get; }
        public bool SubjectConflict { get; }
        public bool ServingTargetInWar { get; }
        public bool DuplicateTarget { get; }
        public bool MembersAtWar { get; }
        public bool RequesterSubject { get; }
        public bool ResponderSubject { get; }
        public int RequesterActiveCount { get; }
        public int ResponderActiveCount { get; }
        public float StrongerMemberPower { get; }
        public bool Eligible { get; }
        public bool ServiceReady { get; }
        public bool RequesterAlive { get; }
        public bool ResponderAlive { get; }
        public bool DistinctRealms { get; }
    }

    internal static class AsyncDiplomacyCoalitionRules
    {
        private const int MaximumActiveCoalitionsPerRealm = 2;

        public static IReadOnlyList<AsyncDiplomacySelectionTargetFacts>
            RankParticipants(
                IEnumerable<AsyncDiplomacySelectionTargetFacts> pTargets)
        {
            var result = pTargets == null
                ? new List<AsyncDiplomacySelectionTargetFacts>()
                : new List<AsyncDiplomacySelectionTargetFacts>(pTargets);
            result.Sort(Compare);
            return result;
        }

        public static bool IsEligible(
            AsyncDiplomacySelectionTargetFacts pTarget)
        {
            if (!pTarget.Eligible || !pTarget.ServiceReady ||
                !pTarget.RequesterAlive || !pTarget.ResponderAlive ||
                !pTarget.DistinctRealms || pTarget.TargetKingdomId < 0L ||
                !pTarget.TargetAlive || !pTarget.TargetCivilized ||
                pTarget.TargetNeutral || pTarget.SubjectConflict ||
                pTarget.ServingTargetInWar || pTarget.DuplicateTarget ||
                pTarget.MembersAtWar || pTarget.RequesterSubject ||
                pTarget.ResponderSubject ||
                pTarget.RequesterActiveCount >=
                MaximumActiveCoalitionsPerRealm ||
                pTarget.ResponderActiveCount >=
                MaximumActiveCoalitionsPerRealm)
                return false;
            return pTarget.TargetHasMandate ||
                   pTarget.Power >= Math.Max(1f,
                       pTarget.StrongerMemberPower) * 1.25f;
        }

        private static int Compare(
            AsyncDiplomacySelectionTargetFacts pLeft,
            AsyncDiplomacySelectionTargetFacts pRight)
        {
            int threat = ThreatScore(pRight).CompareTo(ThreatScore(pLeft));
            return threat != 0
                ? threat
                : pLeft.TargetKingdomId.CompareTo(pRight.TargetKingdomId);
        }

        private static long ThreatScore(
            AsyncDiplomacySelectionTargetFacts pTarget)
        {
            long score = Math.Max(1L, (long)pTarget.Power);
            if (pTarget.TargetHasMandate) score += 100000L;
            if (pTarget.RequesterAtWar) score += 20000L;
            if (pTarget.ResponderAtWar) score += 20000L;
            return score;
        }
    }

    internal readonly struct AsyncDiplomacySelectionIdentity
    {
        public AsyncDiplomacySelectionIdentity(long responderKingdomId,
            int proposalType, AsyncDiplomacyProposalKind proposalKind,
            long warId, long targetKingdomId, long requesterActorId,
            long responderActorId, long targetCityId, string detailId)
        {
            ResponderKingdomId = responderKingdomId;
            ProposalType = proposalType;
            ProposalKind = proposalKind;
            WarId = warId;
            TargetKingdomId = targetKingdomId;
            RequesterActorId = requesterActorId;
            ResponderActorId = responderActorId;
            TargetCityId = targetCityId;
            DetailId = detailId ?? string.Empty;
        }

        public long ResponderKingdomId { get; }
        public int ProposalType { get; }
        public AsyncDiplomacyProposalKind ProposalKind { get; }
        public long WarId { get; }
        public long TargetKingdomId { get; }
        public long RequesterActorId { get; }
        public long ResponderActorId { get; }
        public long TargetCityId { get; }
        public string DetailId { get; }

        public bool Matches(AsyncDiplomacySelectionIdentity pOther)
        {
            return ResponderKingdomId == pOther.ResponderKingdomId &&
                   ProposalType == pOther.ProposalType &&
                   ProposalKind == pOther.ProposalKind &&
                   WarId == pOther.WarId &&
                   TargetKingdomId == pOther.TargetKingdomId &&
                   RequesterActorId == pOther.RequesterActorId &&
                   ResponderActorId == pOther.ResponderActorId &&
                   TargetCityId == pOther.TargetCityId &&
                   string.Equals(DetailId, pOther.DetailId,
                       StringComparison.Ordinal);
        }
    }

    internal readonly struct WarStrategyCandidate
    {
        public WarStrategyCandidate(long pTargetKingdomId,
            WarStrategyCandidateKind pKind, double pScore)
        {
            TargetKingdomId = pTargetKingdomId;
            Kind = pKind;
            Score = pScore;
        }

        public long TargetKingdomId { get; }
        public WarStrategyCandidateKind Kind { get; }
        public double Score { get; }
    }

    internal static class WarStrategyCandidateRules
    {
        public static bool TryEvaluate(KingdomStrategyFacts pSource,
            StrategyTargetFacts pTarget, out WarStrategyCandidate pCandidate)
        {
            pCandidate = default;
            if (!pSource.IsValid || pTarget.TargetId < 0L ||
                pTarget.TargetId == pSource.KingdomId || pTarget.Power <= 0f ||
                pTarget.SameRoot || pTarget.VassalBlocked ||
                pTarget.WarBlocked) return false;

            if (pTarget.PreferredKind == WarStrategyCandidateKind.Zhulu)
            {
                if (!pTarget.ZhuluEligible || pTarget.AtWar) return false;
                double score = ZhuluWarRules.ScoreTarget(pSource.Power,
                    pTarget.Power, pTarget.Neighbor,
                    pTarget.CapitalDistance);
                if (score == double.MinValue && pTarget.ZhuluAge)
                    score = ZhuluWarRules.ScoreWeakFallbackTarget(
                        pSource.Power, pTarget.Power, pTarget.Neighbor,
                        pTarget.CapitalDistance);
                pCandidate = new WarStrategyCandidate(pTarget.TargetId,
                    WarStrategyCandidateKind.Zhulu,
                    score);
                return pCandidate.Score > double.MinValue;
            }

            if (pTarget.PreferredKind ==
                WarStrategyCandidateKind.MandateConquest)
            {
                bool eligible = !pTarget.AtWar &&
                    MandateConquestRules.CanUseMandateConquest(
                        pAttackerIsCurrentMandate: true,
                        pVassalBlocked: pTarget.VassalBlocked,
                        pSameAlliance: pTarget.SameAlliance,
                        pAttackerSystemPower: pTarget.SourceAlliancePower,
                        pDefenderAlliancePower: pTarget.TargetAlliancePower);
                if (eligible)
                {
                    pCandidate = new WarStrategyCandidate(pTarget.TargetId,
                        WarStrategyCandidateKind.MandateConquest,
                        MandateConquestRules.ScoreMandateConquest(
                            pTarget.SourceAlliancePower,
                            pTarget.TargetAlliancePower, pTarget.Neighbor));
                    return true;
                }
            }

            if (pTarget.PreferredKind == WarStrategyCandidateKind.TakeMandate)
            {
                if (!MandateWarAiRules.ShouldConsiderTakeMandate(
                        pTargetIsCurrentMandate: true,
                        pVassalBlocked: pTarget.VassalBlocked,
                        pAttackerPower: pSource.Power,
                        pDefenderPower: pTarget.Power,
                        pMandateValue: pTarget.MandateValue)) return false;
                double score = MandateWarAiRules.ScoreTakeMandate(
                    pSource.Power, pTarget.Power, pTarget.MandateValue);
                if (pTarget.AtWar) score += 80d;
                if (pTarget.MandateCoreControl < .5f) score += 70d;
                pCandidate = new WarStrategyCandidate(pTarget.TargetId,
                    WarStrategyCandidateKind.TakeMandate, score);
                return true;
            }

            if (pTarget.AtWar || !pTarget.FabricationAvailable ||
                !pTarget.Neighbor && pTarget.Opinion > -65 ||
                pSource.Power < pTarget.Power * 1.35f) return false;
            double normalScore = 120d + (pTarget.Neighbor ? 90d : 0d) +
                                 Math.Max(0, -pTarget.Opinion) +
                                 Math.Min(160d, pTarget.Power);
            normalScore *= WarCourtMultiplierRules.OffensiveWarMultiplier(
                pSource.Aggression, pSource.Peace, pSource.Livelihood,
                pSource.War, pProtectedWar: false);
            pCandidate = new WarStrategyCandidate(pTarget.TargetId,
                WarStrategyCandidateKind.Normal, normalScore);
            return true;
        }

        public static IReadOnlyList<WarStrategyCandidate> RankCandidates(
            KingdomStrategyFacts pSource,
            IEnumerable<StrategyTargetFacts> pTargets)
        {
            var result = new List<WarStrategyCandidate>();
            if (pTargets == null) return result;
            foreach (StrategyTargetFacts target in pTargets)
                if (TryEvaluate(pSource, target,
                        out WarStrategyCandidate candidate))
                    result.Add(candidate);
            result.Sort(Compare);
            return result;
        }

        public static bool MatchesKind(WarStrategyCandidateKind pPlanKind,
            WarStrategyCandidateKind pLiveKind)
        {
            return pPlanKind != WarStrategyCandidateKind.None &&
                   pPlanKind == pLiveKind;
        }

        private static int Compare(WarStrategyCandidate pFirst,
            WarStrategyCandidate pSecond)
        {
            int score = pSecond.Score.CompareTo(pFirst.Score);
            if (score != 0) return score;
            int target = pFirst.TargetKingdomId.CompareTo(
                pSecond.TargetKingdomId);
            return target != 0 ? target : pFirst.Kind.CompareTo(pSecond.Kind);
        }

    }

    internal readonly struct AsyncDiplomacyProposalFacts
    {
        public AsyncDiplomacyProposalFacts(long targetKingdomId,
            AsyncDiplomacyProposalKind proposalKind, double score,
            bool activeBlocker, bool cooldown)
        {
            TargetKingdomId = targetKingdomId;
            ProposalKind = proposalKind;
            Score = double.IsNaN(score) || double.IsInfinity(score)
                ? double.MinValue
                : score;
            ActiveBlocker = activeBlocker;
            Cooldown = cooldown;
        }

        public long TargetKingdomId { get; }
        public AsyncDiplomacyProposalKind ProposalKind { get; }
        public double Score { get; }
        public bool ActiveBlocker { get; }
        public bool Cooldown { get; }
    }

    internal readonly struct AsyncStrategyCandidate
    {
        public AsyncStrategyCandidate(long pTargetKingdomId,
            AsyncStrategyAction pAction,
            AsyncDiplomacyProposalKind pProposalKind, double pScore,
            double pRoll,
            WarStrategyCandidateKind pWarKind = WarStrategyCandidateKind.None)
        {
            TargetKingdomId = pTargetKingdomId;
            Action = pAction;
            ProposalKind = pProposalKind;
            Score = pScore;
            Roll = pRoll;
            WarKind = pWarKind;
        }

        public long TargetKingdomId { get; }
        public AsyncStrategyAction Action { get; }
        public AsyncDiplomacyProposalKind ProposalKind { get; }
        public double Score { get; }
        public double Roll { get; }
        public WarStrategyCandidateKind WarKind { get; }
    }

    internal static class AsyncStrategyShadowRules
    {
        public static string Summarize(
            IReadOnlyList<AsyncStrategyCandidate> pCandidates)
        {
            if (pCandidates == null || pCandidates.Count == 0) return "none";
            var result = new StringBuilder();
            for (int index = 0; index < pCandidates.Count; index++)
            {
                if (index > 0) result.Append(';');
                AsyncStrategyCandidate candidate = pCandidates[index];
                result.Append("target=").Append(candidate.TargetKingdomId)
                    .Append(",action=").Append(candidate.Action)
                    .Append(",war=").Append(candidate.WarKind)
                    .Append(",proposal=").Append(candidate.ProposalKind)
                    .Append(",score=").Append(candidate.Score.ToString("R",
                        CultureInfo.InvariantCulture))
                    .Append(",roll=").Append(candidate.Roll.ToString("R",
                        CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }

        public static string SummarizeDecisions(
            IReadOnlyList<AsyncStrategyCandidate> pCandidates)
        {
            if (pCandidates == null || pCandidates.Count == 0) return "none";
            var result = new StringBuilder();
            for (int index = 0; index < pCandidates.Count; index++)
            {
                if (index > 0) result.Append(';');
                AsyncStrategyCandidate candidate = pCandidates[index];
                result.Append("target=").Append(candidate.TargetKingdomId)
                    .Append(",action=").Append(candidate.Action)
                    .Append(",war=").Append(candidate.WarKind)
                    .Append(",proposal=").Append(candidate.ProposalKind)
                    .Append(",score=").Append(candidate.Score.ToString("R",
                        CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }
    }

    internal readonly struct AsyncStrategyAuthorityTrace
    {
        public AsyncStrategyAuthorityTrace(string pSummary,
            bool pOrdinaryPlanningReached)
        {
            Summary = pSummary ?? "none";
            OrdinaryPlanningReached = pOrdinaryPlanningReached;
        }

        public string Summary { get; }
        public bool OrdinaryPlanningReached { get; }

        public static AsyncStrategyAuthorityTrace Skipped(string pReason)
        {
            return new AsyncStrategyAuthorityTrace(
                string.IsNullOrEmpty(pReason) ? "none" : pReason, false);
        }

        public static AsyncStrategyAuthorityTrace Planned(string pSummary)
        {
            return new AsyncStrategyAuthorityTrace(pSummary, true);
        }
    }

    internal static class AsyncStrategyLifecycleRules
    {
        public static bool ShouldSchedule(bool captureAvailable,
            bool authoritativeAnnualReady)
        {
            return captureAvailable && authoritativeAnnualReady;
        }
    }

    internal static class AsyncStrategyCandidateWindow
    {
        public static bool TryCaptureUnique<T>(IEnumerable<T> pCandidates,
            Func<T, long> pIdSelector, int pMaximumCandidates,
            out T[] pCaptured)
        {
            pCaptured = null;
            if (pIdSelector == null || pMaximumCandidates < 0 ||
                pMaximumCandidates >
                AsyncStrategyRevisionSet.MaximumCandidateKingdoms)
                return false;
            var result = new List<T>(pMaximumCandidates);
            var seen = new HashSet<long>();
            if (pCandidates != null)
            {
                foreach (T candidate in pCandidates)
                {
                    long id = pIdSelector(candidate);
                    if (id < 0L || !seen.Add(id)) continue;
                    if (result.Count >= pMaximumCandidates) return false;
                    result.Add(candidate);
                }
            }
            pCaptured = result.ToArray();
            return true;
        }
    }

    internal static class AsyncWarCandidateProducer
    {
        public static bool TryCapture<T>(IEnumerable<T> pCandidates,
            Func<T, long> pIdSelector, int pMaximumCandidates,
            out T[] pCaptured)
        {
            return AsyncStrategyCandidateWindow.TryCaptureUnique(pCandidates,
                pIdSelector, pMaximumCandidates, out pCaptured);
        }
    }

    internal static class AsyncStrategyFactEncoding
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatIntUnion
        {
            [FieldOffset(0)] public float Float;
            [FieldOffset(0)] public int Int;
        }

        public static long FloatBits(float pValue)
        {
            return new FloatIntUnion { Float = pValue }.Int;
        }
    }

    internal sealed class AsyncStrategyFactFingerprint
    {
        private readonly long[] _values;

        private AsyncStrategyFactFingerprint(long[] pValues)
        {
            _values = pValues ?? Array.Empty<long>();
        }

        public static AsyncStrategyFactFingerprint CaptureWar(
            KingdomStrategyFacts pSource,
            IReadOnlyList<StrategyTargetFacts> pTargets)
        {
            var values = new List<long>();
            AppendSource(values, pSource);
            var targets = new List<StrategyTargetFacts>(
                pTargets?.Count ?? 0);
            if (pTargets != null)
                for (int index = 0; index < pTargets.Count; index++)
                    targets.Add(pTargets[index]);
            targets.Sort((left, right) => left.TargetId.CompareTo(
                right.TargetId));
            values.Add(targets.Count);
            for (int index = 0; index < targets.Count; index++)
                AppendWarTarget(values, targets[index]);
            return new AsyncStrategyFactFingerprint(values.ToArray());
        }

        public static AsyncStrategyFactFingerprint CaptureDiplomacy(
            KingdomStrategyFacts pSource,
            IReadOnlyList<AsyncDiplomacyProposalFacts> pProposals,
            IReadOnlyList<AsyncDiplomacySelectionTargetFacts>
                pSelectionTargets,
            IReadOnlyList<AsyncDiplomacySelectionIdentity> pSelections)
        {
            var values = new List<long>();
            AppendSource(values, pSource);
            var proposals = new List<AsyncDiplomacyProposalFacts>(
                pProposals?.Count ?? 0);
            if (pProposals != null)
                for (int index = 0; index < pProposals.Count; index++)
                    proposals.Add(pProposals[index]);
            proposals.Sort(CompareProposal);
            values.Add(proposals.Count);
            for (int index = 0; index < proposals.Count; index++)
                AppendProposal(values, proposals[index]);

            var targets = new List<AsyncDiplomacySelectionTargetFacts>(
                pSelectionTargets?.Count ?? 0);
            if (pSelectionTargets != null)
                for (int index = 0; index < pSelectionTargets.Count; index++)
                    targets.Add(pSelectionTargets[index]);
            targets.Sort((left, right) => left.TargetKingdomId.CompareTo(
                right.TargetKingdomId));
            values.Add(targets.Count);
            for (int index = 0; index < targets.Count; index++)
                AppendSelectionTarget(values, targets[index]);

            var selections = new List<AsyncDiplomacySelectionIdentity>(
                pSelections?.Count ?? 0);
            if (pSelections != null)
                for (int index = 0; index < pSelections.Count; index++)
                    selections.Add(pSelections[index]);
            selections.Sort(CompareSelection);
            values.Add(selections.Count);
            for (int index = 0; index < selections.Count; index++)
                AppendSelection(values, selections[index]);
            return new AsyncStrategyFactFingerprint(values.ToArray());
        }

        public bool MatchesWar(KingdomStrategyFacts pSource,
            IReadOnlyList<StrategyTargetFacts> pTargets)
        {
            return EqualsValues(CaptureWar(pSource, pTargets)._values);
        }

        public bool MatchesDiplomacy(KingdomStrategyFacts pSource,
            IReadOnlyList<AsyncDiplomacyProposalFacts> pProposals,
            IReadOnlyList<AsyncDiplomacySelectionTargetFacts>
                pSelectionTargets,
            IReadOnlyList<AsyncDiplomacySelectionIdentity> pSelections)
        {
            return EqualsValues(CaptureDiplomacy(pSource, pProposals,
                pSelectionTargets, pSelections)._values);
        }

        private bool EqualsValues(long[] pOther)
        {
            if (pOther == null || pOther.Length != _values.Length)
                return false;
            for (int index = 0; index < _values.Length; index++)
                if (_values[index] != pOther[index]) return false;
            return true;
        }

        private static void AppendSource(List<long> pValues,
            KingdomStrategyFacts pSource)
        {
            pValues.Add(pSource.KingdomId);
            pValues.Add(FloatBits(pSource.Power));
            pValues.Add(FloatBits(pSource.War));
            pValues.Add(FloatBits(pSource.Peace));
            pValues.Add(FloatBits(pSource.Aggression));
            pValues.Add(FloatBits(pSource.Livelihood));
            pValues.Add(pSource.RootSuzerainId);
        }

        private static void AppendWarTarget(List<long> pValues,
            StrategyTargetFacts pTarget)
        {
            pValues.Add(pTarget.TargetId);
            pValues.Add(FloatBits(pTarget.Power));
            pValues.Add(pTarget.Opinion);
            pValues.Add(Bool(pTarget.Neighbor));
            pValues.Add(Bool(pTarget.AtWar));
            pValues.Add(Bool(pTarget.WarBlocked));
            pValues.Add((long)pTarget.PreferredKind);
            pValues.Add(Bool(pTarget.SameRoot));
            pValues.Add(Bool(pTarget.VassalBlocked));
            pValues.Add(Bool(pTarget.FabricationAvailable));
            pValues.Add(Bool(pTarget.SameAlliance));
            pValues.Add(FloatBits(pTarget.SourceAlliancePower));
            pValues.Add(FloatBits(pTarget.TargetAlliancePower));
            pValues.Add(pTarget.MandateValue);
            pValues.Add(FloatBits(pTarget.MandateCoreControl));
            pValues.Add(Bool(pTarget.ZhuluEligible));
            pValues.Add(FloatBits(pTarget.CapitalDistance));
        }

        private static void AppendProposal(List<long> pValues,
            AsyncDiplomacyProposalFacts pProposal)
        {
            pValues.Add(pProposal.TargetKingdomId);
            pValues.Add((long)pProposal.ProposalKind);
            pValues.Add(BitConverter.DoubleToInt64Bits(pProposal.Score));
            pValues.Add(Bool(pProposal.ActiveBlocker));
            pValues.Add(Bool(pProposal.Cooldown));
        }

        private static void AppendSelectionTarget(List<long> pValues,
            AsyncDiplomacySelectionTargetFacts pTarget)
        {
            pValues.Add(pTarget.TargetKingdomId);
            pValues.Add(FloatBits(pTarget.Power));
            pValues.Add(Bool(pTarget.TargetHasMandate));
            pValues.Add(Bool(pTarget.RequesterAtWar));
            pValues.Add(Bool(pTarget.ResponderAtWar));
            pValues.Add(Bool(pTarget.TargetAlive));
            pValues.Add(Bool(pTarget.TargetCivilized));
            pValues.Add(Bool(pTarget.TargetNeutral));
            pValues.Add(Bool(pTarget.SubjectConflict));
            pValues.Add(Bool(pTarget.ServingTargetInWar));
            pValues.Add(Bool(pTarget.DuplicateTarget));
            pValues.Add(Bool(pTarget.MembersAtWar));
            pValues.Add(Bool(pTarget.RequesterSubject));
            pValues.Add(Bool(pTarget.ResponderSubject));
            pValues.Add(pTarget.RequesterActiveCount);
            pValues.Add(pTarget.ResponderActiveCount);
            pValues.Add(FloatBits(pTarget.StrongerMemberPower));
            pValues.Add(Bool(pTarget.Eligible));
            pValues.Add(Bool(pTarget.ServiceReady));
            pValues.Add(Bool(pTarget.RequesterAlive));
            pValues.Add(Bool(pTarget.ResponderAlive));
            pValues.Add(Bool(pTarget.DistinctRealms));
        }

        private static void AppendSelection(List<long> pValues,
            AsyncDiplomacySelectionIdentity pSelection)
        {
            pValues.Add(pSelection.ResponderKingdomId);
            pValues.Add(pSelection.ProposalType);
            pValues.Add((long)pSelection.ProposalKind);
            pValues.Add(pSelection.WarId);
            pValues.Add(pSelection.TargetKingdomId);
            pValues.Add(pSelection.RequesterActorId);
            pValues.Add(pSelection.ResponderActorId);
            pValues.Add(pSelection.TargetCityId);
            string detail = pSelection.DetailId ?? string.Empty;
            pValues.Add(detail.Length);
            for (int index = 0; index < detail.Length; index++)
                pValues.Add(detail[index]);
        }

        private static int CompareProposal(
            AsyncDiplomacyProposalFacts pLeft,
            AsyncDiplomacyProposalFacts pRight)
        {
            int target = pLeft.TargetKingdomId.CompareTo(
                pRight.TargetKingdomId);
            return target != 0
                ? target
                : pLeft.ProposalKind.CompareTo(pRight.ProposalKind);
        }

        private static int CompareSelection(
            AsyncDiplomacySelectionIdentity pLeft,
            AsyncDiplomacySelectionIdentity pRight)
        {
            int result = pLeft.ResponderKingdomId.CompareTo(
                pRight.ResponderKingdomId);
            if (result != 0) return result;
            result = pLeft.ProposalType.CompareTo(pRight.ProposalType);
            if (result != 0) return result;
            result = pLeft.ProposalKind.CompareTo(pRight.ProposalKind);
            if (result != 0) return result;
            result = pLeft.WarId.CompareTo(pRight.WarId);
            if (result != 0) return result;
            result = pLeft.TargetKingdomId.CompareTo(
                pRight.TargetKingdomId);
            if (result != 0) return result;
            result = pLeft.RequesterActorId.CompareTo(
                pRight.RequesterActorId);
            if (result != 0) return result;
            result = pLeft.ResponderActorId.CompareTo(
                pRight.ResponderActorId);
            if (result != 0) return result;
            result = pLeft.TargetCityId.CompareTo(pRight.TargetCityId);
            return result != 0
                ? result
                : string.CompareOrdinal(pLeft.DetailId, pRight.DetailId);
        }

        private static long FloatBits(float pValue)
        {
            return AsyncStrategyFactEncoding.FloatBits(pValue);
        }

        private static long Bool(bool pValue)
        {
            return pValue ? 1L : 0L;
        }
    }

    internal sealed class AsyncStrategyRevisionSet
    {
        public const int MaximumCandidateKingdoms = 24;
        private readonly long[] _kingdomIds;
        private readonly long[] _revisions;

        private AsyncStrategyRevisionSet(long pSourceKingdomId,
            long[] pKingdomIds, long[] pRevisions)
        {
            SourceKingdomId = pSourceKingdomId;
            _kingdomIds = pKingdomIds;
            _revisions = pRevisions;
        }

        public long SourceKingdomId { get; }
        public long SourceRevision => _revisions.Length == 0
            ? 0L
            : _revisions[0];
        public int Count => _kingdomIds.Length;

        public static bool TryCapture(long pSourceKingdomId,
            IEnumerable<long> pCandidateKingdomIds,
            int pMaximumCandidateKingdoms,
            Func<long, long> pRevisionProvider,
            out AsyncStrategyRevisionSet pRevisionSet)
        {
            pRevisionSet = null;
            if (pSourceKingdomId < 0L || pRevisionProvider == null ||
                pMaximumCandidateKingdoms < 0 ||
                pMaximumCandidateKingdoms > MaximumCandidateKingdoms)
                return false;

            var ids = new List<long>(pMaximumCandidateKingdoms + 1)
            {
                pSourceKingdomId
            };
            var seen = new HashSet<long> { pSourceKingdomId };
            if (pCandidateKingdomIds != null)
            {
                foreach (long candidateId in pCandidateKingdomIds)
                {
                    if (candidateId < 0L || !seen.Add(candidateId)) continue;
                    if (ids.Count - 1 >= pMaximumCandidateKingdoms)
                        return false;
                    ids.Add(candidateId);
                }
            }

            var revisions = new long[ids.Count];
            for (int index = 0; index < ids.Count; index++)
                revisions[index] = pRevisionProvider(ids[index]);
            pRevisionSet = new AsyncStrategyRevisionSet(pSourceKingdomId,
                ids.ToArray(), revisions);
            return true;
        }

        public bool IsCurrent(Func<long, long> pRevisionProvider)
        {
            if (pRevisionProvider == null ||
                _kingdomIds.Length != _revisions.Length) return false;
            for (int index = 0; index < _kingdomIds.Length; index++)
                if (pRevisionProvider(_kingdomIds[index]) !=
                    _revisions[index])
                    return false;
            return true;
        }
    }

    internal sealed class AsyncStrategyPlan
    {
        public AsyncStrategyPlan(long pSourceKingdomId, long pTargetKingdomId,
            AsyncStrategyAction pAction,
            AsyncDiplomacyProposalKind pProposalKind, double pScore,
            double pRoll, WarStrategyCandidateKind pWarKind,
            int pCaptureYear, AWAsyncStamp pStamp,
            AsyncStrategyRevisionSet pRevisionSet,
            AsyncStrategyFactFingerprint pFactFingerprint = null)
        {
            SourceKingdomId = pSourceKingdomId;
            TargetKingdomId = pTargetKingdomId;
            Action = pAction;
            ProposalKind = pProposalKind;
            Score = pScore;
            Roll = pRoll;
            WarKind = pWarKind;
            CaptureYear = pCaptureYear;
            Stamp = pStamp;
            RevisionSet = pRevisionSet;
            FactFingerprint = pFactFingerprint;
        }

        public long SourceKingdomId { get; }
        public long TargetKingdomId { get; }
        public AsyncStrategyAction Action { get; }
        public AsyncDiplomacyProposalKind ProposalKind { get; }
        public double Score { get; }
        public double Roll { get; }
        public WarStrategyCandidateKind WarKind { get; }
        public int CaptureYear { get; }
        public AWAsyncStamp Stamp { get; }
        public AsyncStrategyRevisionSet RevisionSet { get; }
        public AsyncStrategyFactFingerprint FactFingerprint { get; }
    }

    internal static class AsyncStrategyPlanRules
    {
        public static bool Accept(AsyncStrategyPlan pPlan,
            long currentWorldGeneration,
            Func<long, long> pCurrentRevisionProvider,
            int currentYear, long currentTick, long maxAgeTicks)
        {
            if (pPlan == null || pPlan.SourceKingdomId < 0L ||
                pPlan.TargetKingdomId < 0L ||
                pPlan.Action == AsyncStrategyAction.None || maxAgeTicks < 0L ||
                currentYear != pPlan.CaptureYear ||
                currentTick < pPlan.Stamp.CaptureTick ||
                pPlan.RevisionSet == null ||
                pPlan.RevisionSet.SourceKingdomId != pPlan.SourceKingdomId ||
                pPlan.RevisionSet.SourceRevision !=
                pPlan.Stamp.SourceRevision ||
                !pPlan.RevisionSet.IsCurrent(pCurrentRevisionProvider))
                return false;
            long currentSourceRevision = pCurrentRevisionProvider(
                pPlan.SourceKingdomId);
            return AWAsyncVersionRules.Accept(pPlan.Stamp.WorldGeneration,
                       currentWorldGeneration, pPlan.Stamp.SourceRevision,
                       currentSourceRevision) &&
                   currentTick - pPlan.Stamp.CaptureTick <= maxAgeTicks;
        }

        public static bool AcceptWar(AsyncStrategyPlan pPlan,
            long currentWorldGeneration,
            Func<long, long> pCurrentRevisionProvider,
            int currentYear, long currentTick, long maxAgeTicks,
            bool sourceAlive,
            bool targetAlive, bool alreadyAtWar, bool truceBlocked)
        {
            return sourceAlive && targetAlive && !alreadyAtWar &&
                   !truceBlocked && pPlan?.Action ==
                   AsyncStrategyAction.DeclareWar &&
                   pPlan.WarKind != WarStrategyCandidateKind.None &&
                   Accept(pPlan, currentWorldGeneration,
                       pCurrentRevisionProvider, currentYear, currentTick,
                       maxAgeTicks);
        }
    }

    internal static class AsyncStrategyDeterminism
    {
        public static double Roll(long pWorldSeed, int pYear,
            long pSourceKingdomId, long pSourceRevision, long pSalt)
        {
            ulong value = unchecked((ulong)pWorldSeed);
            value = Mix(value ^ unchecked((ulong)(long)pYear));
            value = Mix(value ^ unchecked((ulong)pSourceKingdomId));
            value = Mix(value ^ unchecked((ulong)pSourceRevision));
            value = Mix(value ^ unchecked((ulong)pSalt));
            return (value >> 11) * (1d / 9007199254740992d);
        }

        private static ulong Mix(ulong pValue)
        {
            unchecked
            {
                pValue += 0x9E3779B97F4A7C15UL;
                pValue = (pValue ^ (pValue >> 30)) * 0xBF58476D1CE4E5B9UL;
                pValue = (pValue ^ (pValue >> 27)) * 0x94D049BB133111EBUL;
                return pValue ^ (pValue >> 31);
            }
        }
    }
}
