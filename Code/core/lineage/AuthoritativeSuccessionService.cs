using AncientWarfare3.core.asyncwork;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class AuthoritativeSuccessionService
    {
        private const int FallbackRetryDelayFrames = 256;
        private static readonly SuccessionFallbackAttemptState
            FallbackAttempts = new SuccessionFallbackAttemptState();

        private readonly struct LegitimateContext
        {
            internal LegitimateContext(long pLineageId, long pShiId,
                int pPredecessorGeneration)
            {
                LineageId = pLineageId;
                ShiId = pShiId;
                PredecessorGeneration = pPredecessorGeneration;
            }
            internal long LineageId { get; }
            internal long ShiId { get; }
            internal int PredecessorGeneration { get; }
        }

        internal static Actor EnsureRegisteredCandidate(Kingdom pKingdom,
            Actor pPredecessor)
        {
            if (pKingdom?.data == null) return null;
            Actor registered = RepublicGovernmentService.IsRepublic(pKingdom)
                ? RepublicGovernmentService.GetRegisteredSuccessor(pKingdom)
                : HeirService.PeekRegisteredHeir(pKingdom);
            if (registered?.data != null) return registered;

            long predecessorId = pPredecessor?.data?.id ?? -1L;
            if (predecessorId < 0L)
                pKingdom.data.get(
                    LineageKeys.KINGDOM_PRE_SUCCESSION_KING_ID,
                    out predecessorId, -1L);
            var key = new KingSuccessionKey(AWAsyncRuntime.WorldGeneration,
                pKingdom.id, predecessorId);
            if (!FallbackAttempts.TryBegin(key, Time.frameCount,
                    FallbackRetryDelayFrames)) return null;

            if (RepublicGovernmentService.IsRepublic(pKingdom))
                return RepublicGovernmentService.ResolveRulerForVacancy(
                    pKingdom);

            HeirService.RefreshHeir(pKingdom);
            registered = HeirService.PeekRegisteredHeir(pKingdom);
            if (registered?.data != null) return registered;

            SuccessionEvidenceStatus evidence =
                SuccessionEvidenceStatus.PendingEvidence;
            if (TryResolveLegitimateContext(pKingdom, pPredecessor,
                    predecessorId, out LegitimateContext context))
            {
                SuccessionArchiveFallbackResult archive =
                    SuccessionArchiveFallbackService.Resolve(key,
                        context.LineageId,
                        context.PredecessorGeneration);
                evidence = archive.Status;
                if (archive.ScanInProgress)
                    FallbackAttempts.ScheduleRetry(key, Time.frameCount, 1L);
                if (AuthoritativeSuccessionRules.CanStartCourtUsurpation(
                        evidence))
                {
                    if (!SuccessionRelationshipIndex.IsReady)
                        evidence = SuccessionEvidenceStatus.PendingEvidence;
                    else if (evidence ==
                             SuccessionEvidenceStatus.ExtinctConfirmed &&
                             SuccessionRelationshipIndex.HasLivingLineageMembers(
                                 context.LineageId))
                        evidence = SuccessionEvidenceStatus.
                            EligibleLineExhausted;
                }
                if (evidence == SuccessionEvidenceStatus.Found &&
                    archive.Candidate?.data != null)
                {
                    HeirService.StoreSelectedHeir(pKingdom,
                        archive.Candidate,
                        SuccessionMode.COLLATERAL_RESTORE);
                    registered = HeirService.PeekRegisteredHeir(pKingdom);
                    if (registered?.data?.id == archive.Candidate.data.id)
                        return registered;
                    SuccessionArchiveFallbackService.Restart(key);
                    HeirService.RefreshHeir(pKingdom);
                    return null;
                }
            }

            if (!AuthoritativeSuccessionRules.CanStartCourtUsurpation(
                    evidence)) return null;
            SuccessionCourtFallbackResult court =
                SuccessionCourtFallbackService.ResolveCandidate(
                    pKingdom, key);
            if (court.ScanInProgress)
                FallbackAttempts.ScheduleRetry(key, Time.frameCount, 1L);
            Actor replacement = court.Candidate;
            if (replacement?.data == null)
            {
                if (court.EvidenceAvailable && !court.ScanInProgress)
                    SuccessionCourtFallbackService.Restart(key);
                return null;
            }
            HeirService.StoreSelectedHeir(pKingdom, replacement,
                SuccessionMode.COURT_USURPATION);
            registered = HeirService.PeekRegisteredHeir(pKingdom);
            if (registered?.data?.id == replacement.data.id)
                return registered;
            SuccessionCourtFallbackService.Restart(key);
            HeirService.RefreshHeir(pKingdom);
            return null;
        }

        internal static void OnSuccessorInstalled(Kingdom pKingdom,
            Actor pPredecessor)
        {
            if (pKingdom?.data == null) return;
            BindCourtUsurpationLegitimacy(pKingdom, pKingdom.king);
            long predecessorId = pPredecessor?.data?.id ?? -1L;
            if (predecessorId < 0L)
                pKingdom.data.get(
                    LineageKeys.KINGDOM_PRE_SUCCESSION_KING_ID,
                    out predecessorId, -1L);
            var key = new KingSuccessionKey(AWAsyncRuntime.WorldGeneration,
                pKingdom.id, predecessorId);
            FallbackAttempts.Complete(key);
            SuccessionArchiveFallbackService.Complete(key);
            SuccessionCourtFallbackService.Complete(key);
        }

        internal static void Reset()
        {
            FallbackAttempts.Clear();
            SuccessionArchiveFallbackService.Reset();
            SuccessionCourtFallbackService.Reset();
        }

        private static bool TryResolveLegitimateContext(Kingdom pKingdom,
            Actor pPredecessor, long pPredecessorId,
            out LegitimateContext pContext)
        {
            pContext = default;
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                out long lineageId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                out long shiId, -1L);

            bool hasSnapshot = TryReadPreSuccessionSnapshot(pKingdom,
                pPredecessorId, out int generation,
                out long snapshotLineageId, out long snapshotShiId);
            if (lineageId < 0L && snapshotLineageId >= 0L)
                lineageId = snapshotLineageId;
            if (shiId < 0L && snapshotShiId >= 0L)
                shiId = snapshotShiId;

            Actor predecessor = pPredecessor?.data != null
                ? pPredecessor
                : pKingdom.king?.data?.id == pPredecessorId
                    ? pKingdom.king
                    : null;
            if (predecessor?.data != null)
            {
                predecessor.data.get(LineageKeys.LINEAGE_ID,
                    out long actorLineageId, -1L);
                predecessor.data.get(LineageKeys.SHI_ID,
                    out long actorShiId, -1L);
                if (lineageId < 0L) lineageId = actorLineageId;
                if (shiId < 0L) shiId = actorShiId;
            }

            SuccessionArchiveIdentityResult archiveIdentity = default;
            if (!hasSnapshot && !TryResolvePredecessorGeneration(predecessor,
                    pPredecessorId, out generation, out archiveIdentity))
                return false;
            if (lineageId < 0L && !archiveIdentity.Found)
                archiveIdentity = SuccessionArchiveFallbackService.
                    ResolveIdentity(pPredecessorId);
            if (lineageId < 0L && archiveIdentity.Found)
                lineageId = archiveIdentity.LineageId;
            if (shiId < 0L && archiveIdentity.Found)
                shiId = archiveIdentity.ShiId;
            if (lineageId < 0L && shiId >= 0L)
            {
                try
                {
                    lineageId = LineageQuery.GetShiBranchInfo(shiId)?
                        .lineage_id ?? -1L;
                }
                catch { return false; }
            }
            if (lineageId < 0L) return false;

            pKingdom.data.set(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                lineageId);
            if (shiId >= 0L)
                pKingdom.data.set(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                    shiId);
            pContext = new LegitimateContext(lineageId, shiId, generation);
            return true;
        }

        private static bool TryReadPreSuccessionSnapshot(Kingdom pKingdom,
            long pPredecessorId, out int pGeneration, out long pLineageId,
            out long pShiId)
        {
            pGeneration = 0;
            pLineageId = -1L;
            pShiId = -1L;
            if (pKingdom?.data == null || pPredecessorId < 0L) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_PRE_SUCCESSION_KING_ID,
                out long snapshotActorId, -1L);
            if (snapshotActorId != pPredecessorId) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_PRE_SUCCESSION_GENERATION,
                out pGeneration, int.MinValue);
            if (pGeneration == int.MinValue) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_PRE_SUCCESSION_LINEAGE_ID,
                out pLineageId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_PRE_SUCCESSION_SHI_ID,
                out pShiId, -1L);
            return true;
        }

        private static bool TryResolvePredecessorGeneration(
            Actor pPredecessor, long pPredecessorId, out int pGeneration,
            out SuccessionArchiveIdentityResult pArchiveIdentity)
        {
            pArchiveIdentity = default;
            if (pPredecessor?.data != null)
            {
                pGeneration = pPredecessor.data.generation;
                return true;
            }
            pArchiveIdentity = SuccessionArchiveFallbackService.
                ResolveIdentity(pPredecessorId);
            if (pArchiveIdentity.EvidenceAvailable &&
                pArchiveIdentity.Found)
            {
                pGeneration = pArchiveIdentity.Generation;
                return true;
            }
            pGeneration = 0;
            return false;
        }

        private static void BindCourtUsurpationLegitimacy(Kingdom pKingdom,
            Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            pKingdom.data.get(LineageKeys.KINGDOM_SUCCESSION_MODE,
                out string mode, SuccessionMode.NONE);
            if (mode != SuccessionMode.COURT_USURPATION) return;
            pKing.data.get(LineageKeys.LINEAGE_ID,
                out long lineageId, -1L);
            pKing.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            if (lineageId < 0L && shiId >= 0L)
            {
                try
                {
                    lineageId = LineageQuery.GetShiBranchInfo(shiId)?
                        .lineage_id ?? -1L;
                }
                catch { }
            }
            if (lineageId >= 0L)
                pKingdom.data.set(
                    LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID, lineageId);
            if (shiId >= 0L)
                pKingdom.data.set(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                    shiId);
        }
    }
}
