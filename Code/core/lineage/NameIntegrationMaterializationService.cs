using System;
using System.Collections;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class NameIntegrationMaterializationService
    {
        internal const int DefaultBudget = 8;
        private const int MaxRestoreKingdomsPerCycle = 4;
        private static readonly Dictionary<long, Kingdom> Pending =
            new Dictionary<long, Kingdom>();
        private static readonly Dictionary<long, List<Actor>> Candidates =
            new Dictionary<long, List<Actor>>();
        private static readonly Dictionary<long, int> CandidateCursors =
            new Dictionary<long, int>();
        private static readonly Queue<long> PendingOrder =
            new Queue<long>();
        private static readonly HashSet<long> Enqueued =
            new HashSet<long>();
        private static bool _restored;
        private static IEnumerator _restoreEnumerator;
        private static MapBox _restoreWorld;

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
            CandidateCursors.Remove(kingdomId);
            Enqueue(kingdomId);
        }

        internal static void Reset()
        {
            Pending.Clear();
            Candidates.Clear();
            CandidateCursors.Clear();
            PendingOrder.Clear();
            Enqueued.Clear();
            DisposeRestoreEnumerator();
            _restoreWorld = null;
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
            int kingdomsToInspect = PendingOrder.Count;
            while (remaining > 0 && kingdomsToInspect-- > 0 &&
                   PendingOrder.Count > 0)
            {
                long kingdomId = PendingOrder.Dequeue();
                Enqueued.Remove(kingdomId);
                if (!Pending.TryGetValue(kingdomId,
                        out Kingdom kingdom) || kingdom?.data == null ||
                    kingdom.isRekt() || !LineageService.IsKingdomIntegrated(
                        kingdom))
                {
                    RemovePending(kingdomId);
                    continue;
                }

                NameIntegrationMaterializationState state =
                    NameIntegrationMaterializationStatePersistence.Load(db,
                        kingdomId);
                if (state == null || state.Phase == "complete")
                {
                    RemovePending(kingdomId);
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

                    if (!CandidateCursors.TryGetValue(kingdomId,
                            out int cursor))
                        cursor = FindFirstActorAfter(actors,
                            state.CursorActorId);
                    long latestCursor = state.CursorActorId;
                    int startCursor = cursor;
                    while (cursor < actors.Count && remaining > 0)
                    {
                        Actor actor = actors[cursor++];
                        if (actor?.data == null) continue;
                        if (actor.kingdom == kingdom && !actor.isRekt())
                            LineageService.ApplyNameIntegrationToActor(actor,
                                kingdomIntegrated: true);
                        latestCursor = actor.data.id;
                        remaining--;
                    }
                    CandidateCursors[kingdomId] = cursor;
                    if (cursor > startCursor && latestCursor >
                            state.CursorActorId)
                        NameIntegrationMaterializationStatePersistence
                            .AdvanceCursor(db, kingdomId, latestCursor,
                                LineageService.CurTime());

                    if (cursor >= actors.Count)
                    {
                        NameIntegrationMaterializationStatePersistence
                            .MarkComplete(db, kingdomId,
                                LineageService.CurTime());
                        RemovePending(kingdomId);
                    }
                    else
                        Enqueue(kingdomId);
                }
                catch (Exception error)
                {
                    NameIntegrationMaterializationStatePersistence
                        .RecordFailure(db, kingdomId, error.Message,
                            LineageService.CurTime());
                    CandidateCursors.Remove(kingdomId);
                    Enqueue(kingdomId);
                }
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
                if (!ReferenceEquals(_restoreWorld, world) ||
                    _restoreEnumerator == null)
                {
                    DisposeRestoreEnumerator();
                    _restoreWorld = world;
                    _restoreEnumerator = world.kingdoms.GetEnumerator();
                }
                int remaining = MaxRestoreKingdomsPerCycle;
                while (remaining-- > 0)
                {
                    if (!_restoreEnumerator.MoveNext())
                    {
                        DisposeRestoreEnumerator();
                        _restored = true;
                        return;
                    }
                    Kingdom kingdom = _restoreEnumerator.Current as Kingdom;
                    if (kingdom?.data == null ||
                        !LineageService.IsKingdomIntegrated(kingdom))
                        continue;
                    NameIntegrationMaterializationState state =
                        NameIntegrationMaterializationStatePersistence.Load(
                            db, kingdom.id);
                    if (state == null || state.Version !=
                            NameIntegrationMaterializationStatePersistence.
                                CurrentVersion)
                    {
                        Request(kingdom);
                        continue;
                    }
                    if (state.Phase == "complete") continue;
                    Pending[kingdom.id] = kingdom;
                    Enqueue(kingdom.id);
                }
            }
            catch (Exception error)
            {
                DisposeRestoreEnumerator();
                ModClass.LogWarning(
                    "Name integration migration restore failed: " +
                    error.Message);
            }
        }

        private static void DisposeRestoreEnumerator()
        {
            if (_restoreEnumerator is IDisposable disposable)
                disposable.Dispose();
            _restoreEnumerator = null;
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

        private static int FindFirstActorAfter(List<Actor> pActors,
            long pCursor)
        {
            int low = 0;
            int high = pActors?.Count ?? 0;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                long actorId = pActors[middle]?.data?.id ?? long.MinValue;
                if (actorId <= pCursor) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        private static void Enqueue(long pKingdomId)
        {
            if (pKingdomId < 0L || !Enqueued.Add(pKingdomId)) return;
            PendingOrder.Enqueue(pKingdomId);
        }

        private static void RemovePending(long pKingdomId)
        {
            Pending.Remove(pKingdomId);
            Candidates.Remove(pKingdomId);
            CandidateCursors.Remove(pKingdomId);
            Enqueued.Remove(pKingdomId);
        }
    }
}
