using System;
using System.Collections.Generic;
using System.Threading;
using AncientWarfare3.core.asyncwork;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class KingdomStrategyRevisionService
    {
        private static readonly Dictionary<long, long> Revisions =
            new Dictionary<long, long>();

        public static long Current(long pKingdomId)
        {
            return pKingdomId >= 0L && Revisions.TryGetValue(pKingdomId,
                out long revision) ? revision : 1L;
        }

        public static void MarkChanged(params long[] pKingdomIds)
        {
            if (pKingdomIds == null) return;
            foreach (long kingdomId in pKingdomIds)
            {
                if (kingdomId < 0L) continue;
                long current = Current(kingdomId);
                Revisions[kingdomId] = current == long.MaxValue
                    ? 1L
                    : current + 1L;
            }
        }

        public static void Clear()
        {
            Revisions.Clear();
        }
    }

    internal static class AsyncKingdomStrategyService
    {
        public static void ClearRuntime()
        {
            KingdomStrategyRevisionService.Clear();
            WarDecisionAI.ClearAsyncAdmissionRuntime();
        }

        public static void ScheduleWar(Kingdom pKingdom, int pYear)
        {
            bool shadow = AWAsyncRuntime.ShadowEnabled;
            if (!AWAsyncRuntime.AiEnabled && !shadow)
            {
                WarDecisionAI.OnKingdomYear(pKingdom);
                return;
            }
            long kingdomId = pKingdom?.data == null ? -1L : pKingdom.id;
            long captureTick = Time.frameCount;
            bool captured = WarDecisionAI.TryCaptureAsyncPlan(pKingdom, pYear,
                out KingdomStrategyFacts sourceFacts,
                out StrategyTargetFacts[] targetFacts);
            AsyncStrategyRevisionSet revisionSet = null;
            AsyncStrategyFactFingerprint factFingerprint = null;
            if (captured)
            {
                captured = TryCaptureWarRevisionSet(sourceFacts, targetFacts,
                    out revisionSet);
                if (captured)
                    factFingerprint = AsyncStrategyFactFingerprint.CaptureWar(
                        sourceFacts, targetFacts);
            }
            AsyncStrategyAuthorityTrace authorityTrace = shadow
                ? WarDecisionAI.RunAuthoritativeYear(pKingdom)
                : AsyncStrategyAuthorityTrace.Skipped("live_async");
            bool authorityReady = !shadow ||
                                  authorityTrace.OrdinaryPlanningReached;
            if (!AsyncStrategyLifecycleRules.ShouldSchedule(captured,
                    authorityReady)) return;

            long worldGeneration = AWAsyncRuntime.WorldGeneration;
            long worldSeed = unchecked(
                ((long)MapBox.current_world_seed_id << 32) ^ worldGeneration);
            var stamp = new AWAsyncStamp(worldGeneration, captureTick,
                revisionSet.SourceRevision);
            string key = "war:" + pYear + ":" + kingdomId;
            string expectedShadow = shadow ? authorityTrace.Summary : null;
            if (!shadow && !AWAsyncRuntime.CanSchedule(key, AWAsyncLane.Ai, stamp))
                return;
            AsyncStrategyAdmissionToken admissionToken = default;
            System.Func<bool> tryAdmit = shadow
                ? null
                : () => WarDecisionAI.TryBeginAsyncYear(pKingdom, pYear,
                    out admissionToken);
            Action<Exception> fault = shadow
                ? null
                : _ => WarDecisionAI.TryRollbackAsyncYear(pKingdom,
                    admissionToken);
            var request = new AWAsyncWorkRequest(key, AWAsyncLane.Ai, stamp,
                token => shadow
                    ? PlanWarShadow(token, sourceFacts, targetFacts,
                        worldSeed, pYear, stamp)
                    : PlanWar(token, sourceFacts, targetFacts, worldSeed,
                        pYear, stamp, revisionSet, factFingerprint),
                result => CompleteWar(result, expectedShadow, key, shadow,
                    pKingdom, pYear, admissionToken), pFault: fault,
                pTryAdmit: tryAdmit,
                pCommitMode: shadow
                    ? AWAsyncCommitMode.Background
                    : AWAsyncCommitMode.MainThread);
            try
            {
                if (!AWAsyncRuntime.TrySchedule(request))
                {
                    WarDecisionAI.TryRollbackAsyncYear(pKingdom,
                        admissionToken);
                    return;
                }
            }
            catch
            {
                WarDecisionAI.TryRollbackAsyncYear(pKingdom,
                    admissionToken);
                throw;
            }
        }

        public static void ScheduleDiplomacy(Kingdom pKingdom, int pYear)
        {
            bool shadow = AWAsyncRuntime.ShadowEnabled;
            if (!AWAsyncRuntime.AiEnabled && !shadow)
            {
                DiplomacyProposalService.OnKingdomYear(pKingdom);
                return;
            }
            if (!shadow &&
                !DiplomacyProposalService.TryPrepareAsyncProposalYear(
                    pKingdom, pYear)) return;
            long kingdomId = pKingdom?.data == null ? -1L : pKingdom.id;
            long captureTick = Time.frameCount;
            bool captured = DiplomacyProposalService.TryCaptureAsyncProposal(
                pKingdom, pYear, out KingdomStrategyFacts sourceFacts,
                out AsyncDiplomacyProposalFacts[] proposalFacts,
                out AsyncDiplomacyCommitCandidate[] commitCandidates,
                out AsyncDiplomacySelectionTargetFacts[] selectionTargets);
            AsyncStrategyRevisionSet revisionSet = null;
            AsyncStrategyFactFingerprint factFingerprint = null;
            AsyncDiplomacySelectionIdentity[] selectionIdentities =
                captured
                    ? DiplomacyProposalService.BuildSelectionIdentities(
                        commitCandidates)
                    : Array.Empty<AsyncDiplomacySelectionIdentity>();
            if (captured)
            {
                captured = TryCaptureDiplomacyRevisionSet(sourceFacts,
                    proposalFacts, commitCandidates, selectionTargets,
                    out revisionSet);
                if (captured)
                    factFingerprint =
                        AsyncStrategyFactFingerprint.CaptureDiplomacy(
                            sourceFacts, proposalFacts, selectionTargets,
                            selectionIdentities);
            }
            AsyncStrategyAuthorityTrace authorityTrace = shadow
                ? DiplomacyProposalService.RunAuthoritativeYear(pKingdom)
                : AsyncStrategyAuthorityTrace.Skipped("live_async");
            bool authorityReady = !shadow ||
                                  authorityTrace.OrdinaryPlanningReached;
            if (!AsyncStrategyLifecycleRules.ShouldSchedule(captured,
                    authorityReady)) return;

            long worldGeneration = AWAsyncRuntime.WorldGeneration;
            long worldSeed = unchecked(
                ((long)MapBox.current_world_seed_id << 32) ^ worldGeneration);
            var stamp = new AWAsyncStamp(worldGeneration, captureTick,
                revisionSet.SourceRevision);
            string key = "diplomacy:" + pYear + ":" + kingdomId;
            string expectedShadow = shadow ? authorityTrace.Summary : null;
            if (!shadow && !AWAsyncRuntime.CanSchedule(key, AWAsyncLane.Ai, stamp))
                return;
            long expectedResponderId = commitCandidates.Length > 0
                ? commitCandidates[0].ResponderKingdomId
                : -1L;
            AsyncStrategyAdmissionToken admissionToken = default;
            System.Func<bool> tryAdmit = shadow
                ? null
                : () => DiplomacyProposalService.TryBeginAsyncProposalYear(
                    pKingdom, pYear, expectedResponderId,
                    out admissionToken);
            Action<Exception> fault = shadow
                ? null
                : _ => DiplomacyProposalService
                    .TryRollbackAsyncProposalYear(pKingdom,
                        admissionToken);
            var request = new AWAsyncWorkRequest(key, AWAsyncLane.Ai, stamp,
                token => shadow
                    ? PlanDiplomacyShadow(token, sourceFacts, proposalFacts,
                        worldSeed, pYear, stamp)
                    : PlanDiplomacy(token, sourceFacts, proposalFacts,
                        worldSeed, pYear, stamp, revisionSet,
                        factFingerprint),
                result => CompleteDiplomacy(result, expectedShadow, key,
                    commitCandidates, shadow, pKingdom, pYear,
                    admissionToken), pFault: fault, pTryAdmit: tryAdmit,
                pCommitMode: shadow
                    ? AWAsyncCommitMode.Background
                    : AWAsyncCommitMode.MainThread);
            try
            {
                if (!AWAsyncRuntime.TrySchedule(request))
                {
                    DiplomacyProposalService.TryRollbackAsyncProposalYear(
                        pKingdom, admissionToken);
                    return;
                }
            }
            catch
            {
                DiplomacyProposalService.TryRollbackAsyncProposalYear(
                    pKingdom, admissionToken);
                throw;
            }
        }

        private static void CompleteWar(object pResult,
            string pExpectedShadow, string key, bool pShadow,
            Kingdom pKingdom, int pYear,
            AsyncStrategyAdmissionToken pAdmissionToken)
        {
            if (pShadow)
            {
                string expected = pExpectedShadow ?? "missing";
                string actual = (pResult as StrategyShadowResult)?.Summary ??
                                "missing";
                AWAsyncShadowRuntime.CompareSummary("ai_war", key,
                    expected, actual);
                return;
            }
            if (!WarDecisionAI.TryCompleteAsyncYear(pKingdom, pYear,
                    pAdmissionToken))
                return;
            if (pResult is AsyncStrategyPlan plan)
                WarDecisionAI.TryCommitAsyncPlan(plan, Time.frameCount,
                    pShadowOnly: false);
        }

        private static void CompleteDiplomacy(object pResult,
            string pExpectedShadow, string key,
            IReadOnlyList<AsyncDiplomacyCommitCandidate> pCommitCandidates,
            bool pShadow, Kingdom pKingdom, int pYear,
            AsyncStrategyAdmissionToken pAdmissionToken)
        {
            if (pShadow)
            {
                string expected = pExpectedShadow ?? "missing";
                string actual = (pResult as StrategyShadowResult)?.Summary ??
                                "missing";
                AWAsyncShadowRuntime.CompareSummary("ai_diplomacy", key,
                    expected, actual);
                return;
            }
            if (!DiplomacyProposalService.TryCompleteAsyncProposalYear(
                    pKingdom, pYear, pAdmissionToken))
                return;
            if (pResult is AsyncStrategyPlan plan)
                DiplomacyProposalService.TryCommitAsyncProposal(plan,
                    pCommitCandidates, Time.frameCount, pShadowOnly: false);
        }

        private static object PlanWar(CancellationToken pToken,
            KingdomStrategyFacts pSourceFacts,
            StrategyTargetFacts[] pTargetFacts, long pWorldSeed,
            int pYear, AWAsyncStamp pStamp,
            AsyncStrategyRevisionSet pRevisionSet,
            AsyncStrategyFactFingerprint pFactFingerprint)
        {
            pToken.ThrowIfCancellationRequested();
            IReadOnlyList<AsyncStrategyCandidate> ranked =
                AsyncWarDecisionPlanner.RankCandidates(pSourceFacts,
                    pTargetFacts, pWorldSeed, pYear,
                    pStamp.SourceRevision);
            if (ranked.Count == 0) return null;
            AsyncStrategyCandidate best = ranked[0];
            return new AsyncStrategyPlan(pSourceFacts.KingdomId,
                best.TargetKingdomId, best.Action, best.ProposalKind,
                best.Score, best.Roll, best.WarKind, pYear, pStamp,
                pRevisionSet, pFactFingerprint);
        }

        private static object PlanDiplomacy(CancellationToken pToken,
            KingdomStrategyFacts pSourceFacts,
            AsyncDiplomacyProposalFacts[] pProposalFacts,
            long pWorldSeed, int pYear, AWAsyncStamp pStamp,
            AsyncStrategyRevisionSet pRevisionSet,
            AsyncStrategyFactFingerprint pFactFingerprint)
        {
            pToken.ThrowIfCancellationRequested();
            AsyncStrategyCandidate? best =
                AsyncDiplomacyProposalPlanner.SelectBest(pSourceFacts,
                    pProposalFacts, pWorldSeed, pYear,
                    pStamp.SourceRevision);
            if (!best.HasValue) return null;
            AsyncStrategyCandidate candidate = best.Value;
            return new AsyncStrategyPlan(pSourceFacts.KingdomId,
                candidate.TargetKingdomId, candidate.Action,
                candidate.ProposalKind, candidate.Score, candidate.Roll,
                WarStrategyCandidateKind.None, pYear, pStamp,
                pRevisionSet, pFactFingerprint);
        }

        private static bool TryCaptureWarRevisionSet(
            KingdomStrategyFacts pSourceFacts,
            IReadOnlyList<StrategyTargetFacts> pTargetFacts,
            out AsyncStrategyRevisionSet pRevisionSet)
        {
            var targetIds = new List<long>(pTargetFacts?.Count ?? 0);
            if (pTargetFacts != null)
                for (int index = 0; index < pTargetFacts.Count; index++)
                    targetIds.Add(pTargetFacts[index].TargetId);
            return AsyncStrategyRevisionSet.TryCapture(
                pSourceFacts.KingdomId, targetIds,
                AsyncStrategyRevisionSet.MaximumCandidateKingdoms,
                KingdomStrategyRevisionService.Current, out pRevisionSet);
        }

        private static bool TryCaptureDiplomacyRevisionSet(
            KingdomStrategyFacts pSourceFacts,
            IReadOnlyList<AsyncDiplomacyProposalFacts> pProposalFacts,
            IReadOnlyList<AsyncDiplomacyCommitCandidate> pCommitCandidates,
            IReadOnlyList<AsyncDiplomacySelectionTargetFacts>
                pSelectionTargets,
            out AsyncStrategyRevisionSet pRevisionSet)
        {
            int capacity = (pProposalFacts?.Count ?? 0) +
                           (pCommitCandidates?.Count ?? 0) * 2;
            var targetIds = new List<long>(capacity);
            if (pProposalFacts != null)
                for (int index = 0; index < pProposalFacts.Count; index++)
                    targetIds.Add(pProposalFacts[index].TargetKingdomId);
            if (pCommitCandidates != null)
                for (int index = 0; index < pCommitCandidates.Count; index++)
                {
                    AsyncDiplomacyCommitCandidate candidate =
                        pCommitCandidates[index];
                    targetIds.Add(candidate.ResponderKingdomId);
                    targetIds.Add(candidate.Selection.TargetKingdomId);
                }
            if (pSelectionTargets != null)
                for (int index = 0; index < pSelectionTargets.Count; index++)
                    targetIds.Add(
                        pSelectionTargets[index].TargetKingdomId);
            return AsyncStrategyRevisionSet.TryCapture(
                pSourceFacts.KingdomId, targetIds,
                AsyncStrategyRevisionSet.MaximumCandidateKingdoms,
                KingdomStrategyRevisionService.Current, out pRevisionSet);
        }

        private static StrategyShadowResult PlanWarShadow(
            CancellationToken pToken, KingdomStrategyFacts pSourceFacts,
            StrategyTargetFacts[] pTargetFacts, long pWorldSeed,
            int pYear, AWAsyncStamp pStamp)
        {
            pToken.ThrowIfCancellationRequested();
            IReadOnlyList<AsyncStrategyCandidate> ranked =
                AsyncWarDecisionPlanner.RankCandidates(pSourceFacts,
                    pTargetFacts, pWorldSeed, pYear,
                    pStamp.SourceRevision);
            return new StrategyShadowResult(
                AsyncStrategyShadowRules.SummarizeDecisions(ranked));
        }

        private static StrategyShadowResult PlanDiplomacyShadow(
            CancellationToken pToken, KingdomStrategyFacts pSourceFacts,
            AsyncDiplomacyProposalFacts[] pProposalFacts, long pWorldSeed,
            int pYear, AWAsyncStamp pStamp)
        {
            pToken.ThrowIfCancellationRequested();
            IReadOnlyList<AsyncStrategyCandidate> ranked =
                AsyncDiplomacyProposalPlanner.RankCandidates(pSourceFacts,
                    pProposalFacts, pWorldSeed, pYear,
                    pStamp.SourceRevision);
            return new StrategyShadowResult(
                AsyncStrategyShadowRules.SummarizeDecisions(ranked));
        }

        private sealed class StrategyShadowResult
        {
            public StrategyShadowResult(string pSummary)
            {
                Summary = pSummary ?? string.Empty;
            }

            public string Summary { get; }
        }
    }
}
