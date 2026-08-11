using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class KingdomInstitutionalXiaizationService
    {
        private const int MaxKingdomStagesPerCycle = 1;
        private const int MaxPendingInspectionsPerCycle = 8;
        private const int MaxRestoreKingdomsPerCycle = 4;
        private static readonly Dictionary<long, Kingdom> Pending =
            new Dictionary<long, Kingdom>();
        private static readonly Queue<long> PendingOrder =
            new Queue<long>();
        private static readonly HashSet<long> Enqueued =
            new HashSet<long>();
        private static bool _restored;
        private static long[] _restoreKingdomIds;
        private static int _restoreCursor;

        internal static void Request(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                AW3MultiplayerReplicaScope.IsReplicaSession ||
                !KingdomInstitutionalXiaizationRules.ShouldUseXiaInstitutions(
                    XiaizationService.GetLevel(pKingdom))) return;
            long kingdomId = pKingdom.id;
            if (kingdomId < 0L) return;
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return;
            KingdomInstitutionalXiaizationStatePersistence.Request(db,
                kingdomId, LineageService.CurTime());
            Pending[kingdomId] = pKingdom;
            Enqueue(kingdomId);
        }

        internal static void Reset()
        {
            Pending.Clear();
            PendingOrder.Clear();
            Enqueued.Clear();
            _restoreKingdomIds = null;
            _restoreCursor = 0;
            _restored = false;
        }

        internal static void ProcessAuthorityCycle()
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            RestorePendingKingdoms();
            if (Pending.Count == 0) return;
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return;

            int stages = 0;
            int inspections = 0;
            while (stages < MaxKingdomStagesPerCycle &&
                   inspections < MaxPendingInspectionsPerCycle &&
                   PendingOrder.Count > 0)
            {
                inspections++;
                long kingdomId = PendingOrder.Dequeue();
                Enqueued.Remove(kingdomId);
                if (!Pending.TryGetValue(kingdomId,
                        out Kingdom kingdom) || kingdom?.data == null ||
                    kingdom.isRekt() ||
                    !KingdomInstitutionalXiaizationRules.ShouldUseXiaInstitutions(
                        XiaizationService.GetLevel(kingdom)))
                {
                    Pending.Remove(kingdomId);
                    continue;
                }

                KingdomInstitutionalXiaizationState state =
                    KingdomInstitutionalXiaizationStatePersistence.Load(db,
                        kingdomId);
                if (state == null || state.Phase == "complete")
                {
                    Pending.Remove(kingdomId);
                    continue;
                }

                bool completed = false;
                try
                {
                    if (state.Phase == "prepared")
                    {
                        KingdomPolicyProfileService.EnsureAssigned(kingdom);
                        KingdomPolicyService.EnsureInitialized(kingdom);
                        KingdomInstitutionalXiaizationStatePersistence
                            .AdvancePhase(db, kingdomId, "policy_migrated",
                                LineageService.CurTime());
                    }
                    else if (state.Phase == "policy_migrated")
                    {
                        CourtInstitutionService.Refresh(kingdom,
                            pRecordHistory: false);
                        CourtService.OnKingdomYear(kingdom);
                        KingdomInstitutionalXiaizationStatePersistence
                            .MarkComplete(db, kingdomId,
                                LineageService.CurTime());
                        Pending.Remove(kingdomId);
                        completed = true;
                    }
                    else
                    {
                        KingdomInstitutionalXiaizationStatePersistence
                            .AdvancePhase(db, kingdomId, "prepared",
                                LineageService.CurTime());
                    }
                }
                catch (Exception error)
                {
                    KingdomInstitutionalXiaizationStatePersistence
                        .RecordFailure(db, kingdomId, error.Message,
                            LineageService.CurTime());
                }
                stages++;
                if (!completed && Pending.ContainsKey(kingdomId))
                    Enqueue(kingdomId);
            }
        }

        private static void RestorePendingKingdoms()
        {
            if (_restored) return;
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null || World.world?.kingdoms == null) return;
            MapBox world = World.world;
            try
            {
                if (_restoreKingdomIds == null)
                {
                    var ids = new List<long>();
                    foreach (Kingdom kingdom in world.kingdoms)
                        if (kingdom?.data != null && !kingdom.isRekt())
                            ids.Add(kingdom.id);
                    _restoreKingdomIds = ids.ToArray();
                    _restoreCursor = 0;
                }
                int remaining = MaxRestoreKingdomsPerCycle;
                while (remaining-- > 0 && _restoreCursor < _restoreKingdomIds.Length)
                {
                    long kingdomId = _restoreKingdomIds[_restoreCursor++];
                    Kingdom kingdom = world.kingdoms.get(kingdomId);
                    if (kingdom?.data == null ||
                        !KingdomInstitutionalXiaizationRules
                            .ShouldUseXiaInstitutions(
                                XiaizationService.GetLevel(kingdom)))
                        continue;
                    KingdomInstitutionalXiaizationState state =
                        KingdomInstitutionalXiaizationStatePersistence.Load(
                            db, kingdom.id);
                    if (state == null || state.Version !=
                        KingdomInstitutionalXiaizationStatePersistence.
                                CurrentVersion)
                    {
                        Request(kingdom);
                        continue;
                    }
                    if (state.Phase == "complete") continue;
                    Pending[kingdom.id] = kingdom;
                    Enqueue(kingdom.id);
                }
                if (_restoreCursor >= _restoreKingdomIds.Length)
                    _restored = true;
            }
            catch (Exception error)
            {
                _restoreKingdomIds = null;
                _restoreCursor = 0;
                ModClass.LogWarning(
                    "Institutional Xiaization restore failed: " +
                    error.Message);
            }
        }

        private static void Enqueue(long pKingdomId)
        {
            if (pKingdomId < 0L || !Enqueued.Add(pKingdomId)) return;
            PendingOrder.Enqueue(pKingdomId);
        }
    }
}
