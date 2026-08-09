using System;
using System.Collections.Generic;
using AncientWarfare3.core.asyncwork;

namespace AncientWarfare3.core.lineage
{
    internal sealed class SuccessionPreparationSnapshot
    {
        internal long WorldGeneration;
        internal long KingdomId;
        internal long KingId;
        internal long Revision;
        internal long CandidateId = -1L;
        internal string Mode = SuccessionMode.NONE;
        internal InheritanceLaw AccessionLaw;
        internal long LegitimateClaimantId = -1L;
        internal long MilitaryClaimantId = -1L;
        internal long CivilClaimantId = -1L;
        internal long[] SupportCityIds = Array.Empty<long>();
        internal SuccessionDisputePreparationFacts DisputeFacts;
    }

    internal static class SuccessionPreparationService
    {
        private sealed class PublishedContext
        {
            internal KingSuccessionKey Key;
            internal SuccessionPreparationSnapshot Snapshot;
        }

        private static readonly Dictionary<long, long> Revisions =
            new Dictionary<long, long>();
        private static readonly Dictionary<long, SuccessionPreparationSnapshot>
            Snapshots =
                new Dictionary<long, SuccessionPreparationSnapshot>();
        private static readonly SuccessionDirtyQueue DirtyKingdoms =
            new SuccessionDirtyQueue();
        private static readonly KingSuccessionPreparationState Deaths =
            new KingSuccessionPreparationState();
        private static readonly Dictionary<long, PublishedContext>
            PublishedByKingdom = new Dictionary<long, PublishedContext>();

        internal static long CurrentRevision(long pKingdomId)
        {
            return pKingdomId >= 0L && Revisions.TryGetValue(pKingdomId,
                out long revision) ? revision : 0L;
        }

        internal static void MarkDirty(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.id < 0L) return;
            long revision = CurrentRevision(pKingdom.id);
            Revisions[pKingdom.id] = revision == long.MaxValue
                ? 1L
                : revision + 1L;
            Snapshots.Remove(pKingdom.id);
            DirtyKingdoms.MarkDirty(pKingdom.id);
        }

        internal static void ProcessAuthorityCycle(int pKingdomBudget = 1)
        {
            if (pKingdomBudget <= 0 || !SuccessionRelationshipIndex.IsReady)
                return;
            IReadOnlyList<long> ids = DirtyKingdoms.Take(pKingdomBudget);
            for (int i = 0; i < ids.Count; i++)
            {
                long kingdomId = ids[i];
                Kingdom kingdom = null;
                try { kingdom = World.world?.kingdoms?.get(kingdomId); }
                catch { }
                if (!CanPrepare(kingdom))
                {
                    Snapshots.Remove(kingdomId);
                    continue;
                }
                long revision = CurrentRevision(kingdomId);
                SuccessionPreparationSnapshot snapshot = BuildSnapshot(
                    kingdom, revision);
                if (snapshot == null ||
                    revision != CurrentRevision(kingdomId))
                {
                    DirtyKingdoms.MarkDirty(kingdomId);
                    continue;
                }
                Snapshots[kingdomId] = snapshot;
            }
        }

        internal static bool CaptureDeath(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null ||
                pKingdom.id < 0L) return false;
            var key = new KingSuccessionKey(AWAsyncRuntime.WorldGeneration,
                pKingdom.id, pKing.data.id);
            if (Deaths.Contains(key)) return false;
            CaptureLegitimateIdentity(pKingdom, pKing);
            long revision = CurrentRevision(pKingdom.id);
            if (!Snapshots.TryGetValue(pKingdom.id, out var snapshot) ||
                snapshot.WorldGeneration != AWAsyncRuntime.WorldGeneration ||
                snapshot.Revision != revision ||
                snapshot.KingId != pKing.data.id)
            {
                MarkDirty(pKingdom);
                revision = CurrentRevision(pKingdom.id);
                snapshot = null;
            }
            return Deaths.TryCapture(key, revision,
                snapshot?.CandidateId ?? -1L);
        }

        internal static bool TryPublishForNativeSuccession(Kingdom pKingdom,
            Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return false;
            var key = new KingSuccessionKey(AWAsyncRuntime.WorldGeneration,
                pKingdom.id, pKing.data.id);
            if (PublishedByKingdom.TryGetValue(pKingdom.id,
                    out PublishedContext existing) &&
                existing.Key.Equals(key) && Deaths.TryGetPublished(key,
                    out _)) return true;

            CaptureDeath(pKingdom, pKing);
            if (!TryGetCurrentSnapshot(pKingdom, pKing,
                    out SuccessionPreparationSnapshot snapshot))
                return false;
            Deaths.TryRefreshUnpublished(key, snapshot.Revision,
                snapshot.CandidateId);
            Deaths.Publish(key, snapshot.Revision, snapshot.CandidateId,
                snapshot.Mode);
            if (!Deaths.TryGetPublished(key, out _)) return false;
            PublishedByKingdom[pKingdom.id] = new PublishedContext
            {
                Key = key,
                Snapshot = snapshot
            };
            pKingdom.data.set(LineageKeys.KINGDOM_SUCCESSION_MODE,
                snapshot.Mode ?? SuccessionMode.NONE);
            return true;
        }

        internal static bool TryGetPublishedCandidate(Kingdom pKingdom,
            out Actor pCandidate)
        {
            pCandidate = null;
            if (pKingdom?.data == null ||
                !PublishedByKingdom.TryGetValue(pKingdom.id,
                    out PublishedContext context) ||
                context.Key.WorldGeneration != AWAsyncRuntime.WorldGeneration ||
                !Deaths.TryGetPublished(context.Key, out var prepared))
                return false;
            if (prepared.CandidateId < 0L) return true;
            Actor candidate = World.world?.units?.get(prepared.CandidateId);
            if (candidate?.data == null || !candidate.isAlive() ||
                candidate.isRekt() || candidate.kingdom != pKingdom)
            {
                MarkDirty(pKingdom);
                return false;
            }
            pCandidate = candidate;
            return true;
        }

        internal static bool TryOverridePublishedCandidate(Kingdom pKingdom,
            Actor pPredecessor, Actor pCandidate, string pMode)
        {
            if (pKingdom?.data == null || pPredecessor?.data == null ||
                pCandidate?.data == null || pCandidate.kingdom != pKingdom ||
                !PublishedByKingdom.TryGetValue(pKingdom.id,
                    out PublishedContext context) ||
                context.Key.PredecessorId != pPredecessor.data.id)
                return false;
            long revision = CurrentRevision(pKingdom.id);
            SuccessionDisputePreparationFacts dispute =
                SuccessionDisputeService.BuildPreparationFacts(pKingdom,
                    pPredecessor, pCandidate, pMode);
            context.Snapshot.Revision = revision;
            context.Snapshot.CandidateId = pCandidate.data.id;
            context.Snapshot.Mode = pMode ?? SuccessionMode.NONE;
            context.Snapshot.DisputeFacts = dispute;
            context.Snapshot.SupportCityIds = dispute?.SupportCityIds ??
                                               Array.Empty<long>();
            if (!Deaths.TryReplacePublished(context.Key, revision,
                    pCandidate.data.id, context.Snapshot.Mode)) return false;
            pKingdom.data.set(LineageKeys.KINGDOM_SUCCESSION_MODE,
                context.Snapshot.Mode);
            return true;
        }

        internal static SuccessionDisputePreparationFacts
            OnSuccessorInstalled(Kingdom pKingdom, Actor pSuccessor)
        {
            if (pKingdom?.data == null || pSuccessor?.data == null ||
                !PublishedByKingdom.TryGetValue(pKingdom.id,
                    out PublishedContext context)) return null;
            long candidateId = context.Snapshot.CandidateId;
            if (candidateId >= 0L && candidateId != pSuccessor.data.id)
                return null;
            if (!Deaths.TryConsumePublished(context.Key, out _)) return null;
            PublishedByKingdom.Remove(pKingdom.id);
            return context.Snapshot.DisputeFacts;
        }

        internal static bool TryGetCurrentSnapshot(Kingdom pKingdom,
            Actor pKing, out SuccessionPreparationSnapshot pSnapshot)
        {
            pSnapshot = null;
            if (pKingdom?.data == null || pKing?.data == null ||
                !Snapshots.TryGetValue(pKingdom.id, out var snapshot))
                return false;
            Actor candidate = snapshot.CandidateId < 0L
                ? null
                : World.world?.units?.get(snapshot.CandidateId);
            bool noCandidate = snapshot.CandidateId < 0L;
            bool candidateAlive = noCandidate || candidate?.data != null &&
                                  candidate.isAlive() && !candidate.isRekt();
            bool candidateInRealm = noCandidate || candidateAlive &&
                                    candidate.kingdom == pKingdom;
            if (snapshot.WorldGeneration != AWAsyncRuntime.WorldGeneration ||
                !SuccessionSnapshotRules.IsCurrent(snapshot.Revision,
                    CurrentRevision(pKingdom.id), snapshot.KingId,
                    pKing.data.id, candidateAlive, candidateInRealm))
            {
                MarkDirty(pKingdom);
                return false;
            }
            pSnapshot = snapshot;
            return true;
        }

        internal static void Reset()
        {
            Revisions.Clear();
            Snapshots.Clear();
            DirtyKingdoms.Clear();
            Deaths.Clear();
            PublishedByKingdom.Clear();
        }

        private static bool CanPrepare(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   pKingdom.isCiv() && pKingdom.hasCities() &&
                   pKingdom.king?.data != null;
        }

        private static SuccessionPreparationSnapshot BuildSnapshot(
            Kingdom pKingdom, long pRevision)
        {
            Actor king = pKingdom.king;
            if (king?.data == null) return null;
            Actor candidate;
            string mode;
            if (RepublicGovernmentService.IsRepublic(pKingdom))
            {
                RepublicGovernmentService.RefreshRepublicSuccessor(
                    pKingdom, king);
                candidate = RepublicGovernmentService.
                    GetRegisteredSuccessor(pKingdom);
                mode = candidate?.data == null
                    ? SuccessionMode.NONE
                    : SuccessionMode.REPUBLIC_ELECTIVE;
            }
            else
            {
                candidate = HeirService.PreviewSuccessionCandidate(
                    pKingdom, king, out mode);
            }

            InheritanceLaw law = InheritanceLawService.GetEffectiveLaw(
                pKingdom);
            SuccessionDisputePreparationFacts dispute = candidate?.data == null
                ? null
                : SuccessionDisputeService.BuildPreparationFacts(pKingdom,
                    king, candidate, mode);

            return new SuccessionPreparationSnapshot
            {
                WorldGeneration = AWAsyncRuntime.WorldGeneration,
                KingdomId = pKingdom.id,
                KingId = king.data.id,
                Revision = pRevision,
                CandidateId = candidate?.data?.id ?? -1L,
                Mode = mode,
                AccessionLaw = law,
                LegitimateClaimantId = dispute?.LegitimateClaimantId ?? -1L,
                MilitaryClaimantId = dispute?.MilitaryClaimantId ?? -1L,
                CivilClaimantId = dispute?.CivilClaimantId ?? -1L,
                SupportCityIds = dispute?.SupportCityIds ?? Array.Empty<long>(),
                DisputeFacts = dispute
            };
        }

        private static void CaptureLegitimateIdentity(Kingdom pKingdom,
            Actor pKing)
        {
            pKingdom.data.set(LineageKeys.KINGDOM_PRE_SUCCESSION_KING_ID,
                pKing.data.id);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                out long storedLineage, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                out long storedShi, -1L);
            if (storedLineage < 0L)
            {
                pKing.data.get(LineageKeys.LINEAGE_ID,
                    out long lineageId, -1L);
                if (lineageId >= 0L)
                    pKingdom.data.set(
                        LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                        lineageId);
            }
            if (storedShi < 0L)
            {
                pKing.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
                if (shiId >= 0L)
                    pKingdom.data.set(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                        shiId);
            }
        }
    }
}
