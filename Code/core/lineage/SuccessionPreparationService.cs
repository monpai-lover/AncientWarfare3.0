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
        private static readonly Dictionary<long, long> Revisions =
            new Dictionary<long, long>();
        private static readonly Dictionary<long, SuccessionPreparationSnapshot>
            Snapshots =
                new Dictionary<long, SuccessionPreparationSnapshot>();
        private static readonly SuccessionDirtyQueue DirtyKingdoms =
            new SuccessionDirtyQueue();
        private static readonly KingSuccessionPreparationState Deaths =
            new KingSuccessionPreparationState();

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
            var key = new KingSuccessionKey(AWAsyncRuntime.WorldGeneration,
                pKingdom.id, pKing.data.id);
            return Deaths.TryCapture(key, revision,
                snapshot?.CandidateId ?? -1L);
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
            Actor candidate = HeirService.FindHeirReadOnly(pKingdom);

            InheritanceLaw law = InheritanceLawService.GetEffectiveLaw(
                pKingdom);
            Actor registered = HeirService.PeekStoredHeirForMinimap(pKingdom);
            pKingdom.data.get(LineageKeys.KINGDOM_SUCCESSION_MODE,
                out string registeredMode, SuccessionMode.NONE);
            string mode = HeirService.ResolveSuccessionModeForCandidate(
                pKingdom, king, candidate, law, registered, registeredMode);
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
    }
}
