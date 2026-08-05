using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class NameIntegrationMaterializationService
    {
        internal const int DefaultBudget = 24;
        private static readonly Dictionary<long, Kingdom> Pending =
            new Dictionary<long, Kingdom>();
        private static readonly Dictionary<long, List<Actor>> Candidates =
            new Dictionary<long, List<Actor>>();
        private static bool _restored;

        internal static void Request(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                AW3MultiplayerReplicaScope.IsReplicaSession) return;
            long kingdomId = pKingdom.id;
            if (kingdomId < 0L) return;
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return;
            NameIntegrationMaterializationStatePersistence.Request(db,
                kingdomId, LineageService.CurTime());
            Pending[kingdomId] = pKingdom;
            Candidates.Remove(kingdomId);
        }

        internal static void Reset()
        {
            Pending.Clear();
            Candidates.Clear();
            _restored = false;
        }

        internal static void ProcessAuthorityCycle()
        {
            ProcessAuthorityCycle(DefaultBudget);
        }

        internal static void ProcessAuthorityCycle(int pBudget)
        {
            if (pBudget <= 0 ||
                AW3MultiplayerReplicaScope.IsReplicaSession) return;
            RestorePendingKingdoms();
            if (Pending.Count == 0) return;
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return;

            int remaining = pBudget;
            foreach (long kingdomId in Pending.Keys.ToArray())
            {
                if (remaining <= 0) break;
                if (!Pending.TryGetValue(kingdomId,
                        out Kingdom kingdom) || kingdom?.data == null ||
                    kingdom.isRekt() || !LineageService.IsKingdomIntegrated(
                        kingdom))
                {
                    Pending.Remove(kingdomId);
                    Candidates.Remove(kingdomId);
                    continue;
                }

                NameIntegrationMaterializationState state =
                    NameIntegrationMaterializationStatePersistence.Load(db,
                        kingdomId);
                if (state == null || state.Phase == "complete")
                {
                    Pending.Remove(kingdomId);
                    Candidates.Remove(kingdomId);
                    continue;
                }

                try
                {
                    if (!Candidates.TryGetValue(kingdomId,
                            out List<Actor> actors))
                    {
                        actors = BuildCandidates(kingdom);
                        Candidates[kingdomId] = actors;
                    }

                    bool found = false;
                    long latestCursor = state.CursorActorId;
                    foreach (Actor actor in actors)
                    {
                        if (actor?.data == null || actor.data.id <=
                            latestCursor) continue;
                        found = true;
                        if (actor.kingdom == kingdom && !actor.isRekt())
                            LineageService.ApplyNameIntegrationToActor(actor,
                                kingdomIntegrated: true);
                        NameIntegrationMaterializationStatePersistence
                            .AdvanceCursor(db, kingdomId, actor.data.id,
                                LineageService.CurTime());
                        latestCursor = actor.data.id;
                        remaining--;
                        if (remaining <= 0) break;
                    }

                    if (!found || remaining > 0 &&
                        !HasActorAfter(actors, latestCursor))
                    {
                        NameIntegrationMaterializationStatePersistence
                            .MarkComplete(db, kingdomId,
                                LineageService.CurTime());
                        Pending.Remove(kingdomId);
                        Candidates.Remove(kingdomId);
                    }
                }
                catch (Exception error)
                {
                    NameIntegrationMaterializationStatePersistence
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
                        LineageService.IsKingdomIntegrated(kingdom))
                        Request(kingdom);
                _restored = true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Name integration migration restore failed: " +
                    error.Message);
            }
        }

        private static List<Actor> BuildCandidates(Kingdom pKingdom)
        {
            var result = new List<Actor>();
            if (pKingdom?.data == null) return result;
            foreach (Actor actor in pKingdom.getUnits())
                if (actor?.data != null) result.Add(actor);
            result.Sort((left, right) => left.data.id.CompareTo(right.data.id));
            return result;
        }

        private static bool HasActorAfter(List<Actor> pActors,
            long pCursor)
        {
            foreach (Actor actor in pActors)
                if (actor?.data != null && actor.data.id > pCursor)
                    return true;
            return false;
        }
    }
}
