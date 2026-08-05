using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class KingdomInstitutionalXiaizationService
    {
        private static readonly Dictionary<long, Kingdom> Pending =
            new Dictionary<long, Kingdom>();
        private static bool _restored;

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
        }

        internal static void Reset()
        {
            Pending.Clear();
            _restored = false;
        }

        internal static void ProcessAuthorityCycle()
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            RestorePendingKingdoms();
            if (Pending.Count == 0) return;
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return;

            foreach (long kingdomId in Pending.Keys.ToArray())
            {
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

                try
                {
                    if (state.Phase == "prepared")
                    {
                        KingdomPolicyProfileService.EnsureAssigned(kingdom);
                        KingdomPolicyService.EnsureInitialized(kingdom);
                        KingdomInstitutionalXiaizationStatePersistence
                            .AdvancePhase(db, kingdomId, "policy_migrated",
                                LineageService.CurTime());
                        continue;
                    }

                    if (state.Phase == "policy_migrated")
                    {
                        CourtInstitutionService.Refresh(kingdom,
                            pRecordHistory: false);
                        CourtService.OnKingdomYear(kingdom);
                        KingdomInstitutionalXiaizationStatePersistence
                            .MarkComplete(db, kingdomId,
                                LineageService.CurTime());
                        Pending.Remove(kingdomId);
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
            }
        }

        private static void RestorePendingKingdoms()
        {
            if (_restored) return;
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null || World.world?.kingdoms == null) return;
            try
            {
                foreach (Kingdom kingdom in World.world.kingdoms)
                    if (kingdom?.data != null &&
                        KingdomInstitutionalXiaizationRules
                            .ShouldUseXiaInstitutions(
                                XiaizationService.GetLevel(kingdom)))
                        Request(kingdom);
                _restored = true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Institutional Xiaization restore failed: " +
                    error.Message);
            }
        }
    }
}
