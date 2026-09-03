using System;
using System.Collections.Generic;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class SuccessionDisputePersistenceService
    {
        private sealed class InstalledSuccessionContext
        {
            internal long WorldGeneration;
            internal long KingdomId;
            internal long PredecessorId;
            internal long SuccessorId;
            internal long Revision;
            internal string Mode;
            internal InheritanceLaw AccessionLaw;
            internal Actor Predecessor;
            internal Actor Successor;
            internal int ResolutionAttempts;
        }

        private static readonly Dictionary<long, long> Revisions =
            new Dictionary<long, long>();
        private static readonly Dictionary<long, InstalledSuccessionContext>
            PendingBuilds =
                new Dictionary<long, InstalledSuccessionContext>();
        private static readonly SuccessionDirtyQueue BuildQueue =
            new SuccessionDirtyQueue();
        private static readonly Dictionary<long,
                SuccessionDisputePreparationFacts> PendingDisputes =
            new Dictionary<long, SuccessionDisputePreparationFacts>();
        private static readonly Dictionary<long, long>
            DisputePersistenceRevisions = new Dictionary<long, long>();
        private static readonly SuccessionDirtyQueue DisputeRetryQueue =
            new SuccessionDirtyQueue();

        internal static long CurrentRevision(long pKingdomId)
        {
            return pKingdomId >= 0L && Revisions.TryGetValue(pKingdomId,
                out long revision) ? revision : 0L;
        }

        internal static void EnqueueInstalledSuccession(Kingdom pKingdom,
            Actor pPredecessor, Actor pSuccessor, string pMode)
        {
            EnqueueInstalledSuccession(pKingdom, pPredecessor, pSuccessor,
                pMode, InheritanceLawService.GetEffectiveLaw(pKingdom));
        }

        internal static void EnqueueInstalledSuccession(Kingdom pKingdom,
            Actor pPredecessor, Actor pSuccessor, string pMode,
            InheritanceLaw pAccessionLaw)
        {
            if (pKingdom?.data == null || pPredecessor?.data == null ||
                pSuccessor?.data == null || pKingdom.king != pSuccessor ||
                pKingdom.id < 0L) return;
            long revision = NextRevision(pKingdom.id);
            PendingBuilds[pKingdom.id] = new InstalledSuccessionContext
            {
                WorldGeneration = AWAsyncRuntime.WorldGeneration,
                KingdomId = pKingdom.id,
                PredecessorId = pPredecessor.data.id,
                SuccessorId = pSuccessor.data.id,
                Revision = revision,
                Mode = pMode ?? SuccessionMode.NONE,
                AccessionLaw = pAccessionLaw,
                Predecessor = pPredecessor,
                Successor = pSuccessor,
                ResolutionAttempts = 0
            };
            BuildQueue.MarkDirty(pKingdom.id);
        }

        internal static void ProcessAuthorityCycle()
        {
            RetryPendingDispute();
            IReadOnlyList<long> ids = BuildQueue.Take(1);
            if (ids.Count == 0 || !PendingBuilds.TryGetValue(ids[0],
                    out InstalledSuccessionContext context)) return;
            if (context.WorldGeneration != AWAsyncRuntime.WorldGeneration ||
                context.Revision != CurrentRevision(context.KingdomId))
            {
                PendingBuilds.Remove(context.KingdomId);
                return;
            }

            Kingdom kingdom = World.world?.kingdoms?.get(context.KingdomId);
            Actor predecessor = context.Predecessor;
            Actor successor = context.Successor;
            if (kingdom?.data == null || successor?.data == null ||
                predecessor?.data == null)
            {
                context.ResolutionAttempts++;
                if (context.ResolutionAttempts <= 8)
                {
                    BuildQueue.MarkDirty(context.KingdomId);
                    return;
                }
                ModClass.LogWarning(
                    "Deferred succession dispute context expired for kingdom " +
                    context.KingdomId + " predecessor " +
                    context.PredecessorId + " successor " +
                    context.SuccessorId);
                PendingBuilds.Remove(context.KingdomId);
                return;
            }
            if (kingdom.king != successor)
            {
                PendingBuilds.Remove(context.KingdomId);
                return;
            }

            SuccessionDisputePreparationFacts facts =
                SuccessionDisputeService.BuildPreparationFacts(kingdom,
                    predecessor, successor, context.Mode,
                    context.AccessionLaw);
            PendingBuilds.Remove(context.KingdomId);
            QueueDisputePersistence(facts);
        }

        internal static void Reset()
        {
            Revisions.Clear();
            PendingBuilds.Clear();
            BuildQueue.Clear();
            PendingDisputes.Clear();
            DisputePersistenceRevisions.Clear();
            DisputeRetryQueue.Clear();
        }

        private static long NextRevision(long pKingdomId)
        {
            long current = CurrentRevision(pKingdomId);
            long next = current == long.MaxValue ? 1L : current + 1L;
            Revisions[pKingdomId] = next;
            return next;
        }

        /// <summary>
        ///     小朝廷分裂的入口：首都沦陷后由
        ///     <see cref="RumpCourtSplitService"/> 直接投递一份已经备好的
        ///     争议事实，走与「新君即位遭旁支反对」完全相同的落库与推进路径。
        /// </summary>
        internal static void QueueRumpCourtSplit(
            SuccessionDisputePreparationFacts pFacts)
        {
            QueueDisputePersistence(pFacts);
        }

        private static void QueueDisputePersistence(
            SuccessionDisputePreparationFacts pFacts)
        {
            if (pFacts == null || pFacts.KingdomId < 0L) return;
            long revision = DisputePersistenceRevisions.TryGetValue(
                pFacts.KingdomId, out long current) && current < long.MaxValue
                ? current + 1L
                : 1L;
            DisputePersistenceRevisions[pFacts.KingdomId] = revision;
            SuccessionDisputePreparationFacts captured = CopyDisputeFacts(
                pFacts, revision);
            PendingDisputes[pFacts.KingdomId] = captured;
            if (!TryEnqueueDispute(captured))
                DisputeRetryQueue.MarkDirty(pFacts.KingdomId);
        }

        private static void RetryPendingDispute()
        {
            IReadOnlyList<long> ids = DisputeRetryQueue.Take(1);
            if (ids.Count == 0 || !PendingDisputes.TryGetValue(ids[0],
                    out SuccessionDisputePreparationFacts facts)) return;
            if (!TryEnqueueDispute(facts))
                DisputeRetryQueue.MarkDirty(ids[0]);
        }

        private static bool TryEnqueueDispute(
            SuccessionDisputePreparationFacts pFacts)
        {
            SuccessionDisputeWriteFacts write = BuildWriteFacts(pFacts);
            string operationKey = "succession-dispute:v1:" +
                pFacts.WorldGeneration + ":" + pFacts.KingdomId + ":" +
                pFacts.PredecessorActorId + ":" + pFacts.SuccessorActorId +
                ":" + pFacts.Revision;
            return HistoricalWriteService.TryEnqueueCustom(operationKey,
                (sequence, stamp) => new SuccessionDisputeWriteEnvelope(
                    sequence, operationKey, stamp, write),
                (sequence, outcome) => AcceptDisputeCommit(pFacts, write,
                    outcome),
                (sequence, error) => MarkDisputePersistencePending(pFacts),
                out _, out _);
        }

        private static void AcceptDisputeCommit(
            SuccessionDisputePreparationFacts pFacts,
            SuccessionDisputeWriteFacts pWrite, object pOutcome)
        {
            if (!(pOutcome is SuccessionDisputeWriteResult result) ||
                pFacts == null ||
                pFacts.WorldGeneration != AWAsyncRuntime.WorldGeneration ||
                !PendingDisputes.TryGetValue(pFacts.KingdomId,
                    out SuccessionDisputePreparationFacts pending) ||
                pending.Revision != pFacts.Revision ||
                !DisputePersistenceRevisions.TryGetValue(pFacts.KingdomId,
                    out long revision) || revision != pFacts.Revision)
                return;
            Kingdom kingdom = World.world?.kingdoms?.get(pFacts.KingdomId);
            Actor successor = kingdom?.king;
            if (kingdom?.data == null || successor?.data == null ||
                successor.data.id != pFacts.SuccessorActorId) return;
            PendingDisputes.Remove(pFacts.KingdomId);
            SuccessionDisputeService.PublishCommitted(pFacts, pWrite,
                result, kingdom, successor);
        }

        private static void MarkDisputePersistencePending(
            SuccessionDisputePreparationFacts pFacts)
        {
            if (pFacts == null ||
                pFacts.WorldGeneration != AWAsyncRuntime.WorldGeneration ||
                !PendingDisputes.TryGetValue(pFacts.KingdomId,
                    out SuccessionDisputePreparationFacts pending) ||
                pending.Revision != pFacts.Revision) return;
            DisputeRetryQueue.MarkDirty(pFacts.KingdomId);
        }

        private static SuccessionDisputeWriteFacts BuildWriteFacts(
            SuccessionDisputePreparationFacts pFacts)
        {
            int year = Date.getCurrentYear();
            return new SuccessionDisputeWriteFacts
            {
                OriginalKingdomId = pFacts.KingdomId,
                PredecessorActorId = pFacts.PredecessorActorId,
                SuccessorActorId = pFacts.SuccessorActorId,
                ClaimantActorId = pFacts.ClaimantActorId,
                OriginalStateName = pFacts.OriginalStateName,
                OriginalQualifier = pFacts.OriginalQualifier,
                RivalQualifier = pFacts.RivalQualifier,
                AccessionLaw = (int)pFacts.AccessionLaw,
                SuccessorMode = pFacts.SuccessorMode,
                ClaimantMode = pFacts.ClaimantMode,
                SuccessorSupport = pFacts.SuccessorSupport,
                ClaimantSupport = pFacts.ClaimantSupport,
                PreparedTime = LineageService.CurTime(),
                PreparedYear = year,
                DeadlineYear = SuccessionDisputeRules.DeadlineYear(year),
                Status = (int)SuccessionDisputeStatus.Prepared,
                OriginalLineageId = pFacts.OriginalLineageId,
                OriginalShiId = pFacts.OriginalShiId,
                ClaimGenerationBoundary =
                    SuccessionDisputeRules.ReunificationClaimGenerations,
                SupportCityIds = pFacts.SupportCityIds == null
                    ? Array.Empty<long>()
                    : (long[])pFacts.SupportCityIds.Clone()
            };
        }

        private static SuccessionDisputePreparationFacts CopyDisputeFacts(
            SuccessionDisputePreparationFacts pFacts, long pRevision)
        {
            return new SuccessionDisputePreparationFacts
            {
                WorldGeneration = pFacts.WorldGeneration,
                Revision = pRevision,
                KingdomId = pFacts.KingdomId,
                PredecessorActorId = pFacts.PredecessorActorId,
                SuccessorActorId = pFacts.SuccessorActorId,
                ClaimantActorId = pFacts.ClaimantActorId,
                LegitimateClaimantId = pFacts.LegitimateClaimantId,
                MilitaryClaimantId = pFacts.MilitaryClaimantId,
                CivilClaimantId = pFacts.CivilClaimantId,
                SuccessorMode = pFacts.SuccessorMode,
                ClaimantMode = pFacts.ClaimantMode,
                ClaimantKind = pFacts.ClaimantKind,
                SuccessorSupport = pFacts.SuccessorSupport,
                ClaimantSupport = pFacts.ClaimantSupport,
                RunnerUpSupport = pFacts.RunnerUpSupport,
                AccessionLaw = pFacts.AccessionLaw,
                OriginalLineageId = pFacts.OriginalLineageId,
                OriginalShiId = pFacts.OriginalShiId,
                OriginalStateName = pFacts.OriginalStateName,
                OriginalQualifier = pFacts.OriginalQualifier,
                RivalQualifier = pFacts.RivalQualifier,
                SupportCityIds = pFacts.SupportCityIds == null
                    ? Array.Empty<long>()
                    : (long[])pFacts.SupportCityIds.Clone()
            };
        }
    }
}
