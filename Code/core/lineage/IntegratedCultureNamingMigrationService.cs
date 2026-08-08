using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    internal static class IntegratedCultureNamingMigrationService
    {
        internal const int DefaultBudget = 8;
        private static readonly Dictionary<long, Culture> Pending =
            new Dictionary<long, Culture>();
        private static readonly Dictionary<long, List<Actor>> Candidates =
            new Dictionary<long, List<Actor>>();
        private static readonly Dictionary<long, int> CandidateCursors =
            new Dictionary<long, int>();
        private static readonly Queue<long> PendingOrder =
            new Queue<long>();
        private static readonly HashSet<long> Enqueued =
            new HashSet<long>();
        private static bool _candidateIndexBuilt;

        internal static void Request(Culture pCulture)
        {
            if (pCulture?.data == null ||
                AW3MultiplayerReplicaScope.IsReplicaSession) return;
            long cultureId = pCulture.getID();
            if (cultureId < 0L) return;
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return;
            IntegratedCultureNamingMigrationStatePersistence.Request(db,
                cultureId, LineageService.CurTime());
            Pending[cultureId] = pCulture;
            _candidateIndexBuilt = false;
            CandidateCursors.Clear();
            Enqueue(cultureId);
        }

        internal static void Reset()
        {
            Pending.Clear();
            Candidates.Clear();
            CandidateCursors.Clear();
            PendingOrder.Clear();
            Enqueued.Clear();
            _candidateIndexBuilt = false;
        }

        internal static void ProcessAuthorityCycle()
        {
            ProcessAuthorityCycle(DefaultBudget);
        }

        internal static void ProcessAuthorityCycle(int pBudget)
        {
            if (pBudget <= 0 || Pending.Count == 0 ||
                AW3MultiplayerReplicaScope.IsReplicaSession) return;
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return;

            int remaining = pBudget;
            int culturesToInspect = PendingOrder.Count;
            while (remaining > 0 && culturesToInspect-- > 0 &&
                   PendingOrder.Count > 0)
            {
                long cultureId = PendingOrder.Dequeue();
                Enqueued.Remove(cultureId);
                if (!Pending.TryGetValue(cultureId, out Culture culture) ||
                    culture?.data == null)
                {
                    RemovePending(cultureId);
                    continue;
                }

                IntegratedCultureNamingMigrationState state =
                    IntegratedCultureNamingMigrationStatePersistence.Load(db,
                        cultureId);
                if (state == null || state.Phase == "complete")
                {
                    RemovePending(cultureId);
                    continue;
                }

                try
                {
                    if (!Candidates.TryGetValue(cultureId,
                            out List<Actor> actors))
                    {
                        actors = BuildCandidates(culture);
                        Candidates[cultureId] = actors;
                    }

                    if (!CandidateCursors.TryGetValue(cultureId,
                            out int cursor))
                        cursor = FindFirstActorAfter(actors,
                            state.CursorActorId);
                    long latestCursor = state.CursorActorId;
                    int startCursor = cursor;
                    while (cursor < actors.Count && remaining > 0)
                    {
                        Actor actor = actors[cursor];
                        if (actor?.data == null)
                        {
                            cursor++;
                            continue;
                        }
                        if (!ProcessActor(actor, culture))
                        {
                            IntegratedCultureNamingMigrationStatePersistence
                                .RecordFailure(db, cultureId,
                                    "actor_migration_failed",
                                    LineageService.CurTime());
                            break;
                        }
                        cursor++;
                        latestCursor = actor.data.id;
                        remaining--;
                    }
                    CandidateCursors[cultureId] = cursor;
                    if (cursor > startCursor && latestCursor >
                            state.CursorActorId)
                        IntegratedCultureNamingMigrationStatePersistence
                            .AdvanceCursor(db, cultureId, latestCursor,
                                LineageService.CurTime());

                    if (cursor >= actors.Count)
                    {
                        IntegratedCultureNamingMigrationStatePersistence
                            .MarkComplete(db, cultureId,
                                LineageService.CurTime());
                        RemovePending(cultureId);
                    }
                    else
                        Enqueue(cultureId);
                }
                catch (Exception error)
                {
                    IntegratedCultureNamingMigrationStatePersistence
                        .RecordFailure(db, cultureId, error.Message,
                            LineageService.CurTime());
                    CandidateCursors.Remove(cultureId);
                    Enqueue(cultureId);
                }
            }
        }

        private static List<Actor> BuildCandidates(Culture pCulture)
        {
            BuildCandidateIndex();
            long cultureId = pCulture?.getID() ?? -1L;
            if (cultureId >= 0L && Candidates.TryGetValue(cultureId,
                    out List<Actor> result)) return result;
            result = new List<Actor>();
            if (cultureId >= 0L) Candidates[cultureId] = result;
            return result;
        }

        private static void BuildCandidateIndex()
        {
            if (_candidateIndexBuilt) return;
            Candidates.Clear();
            if (World.world?.units == null)
            {
                _candidateIndexBuilt = true;
                return;
            }
            foreach (Actor actor in World.world.units)
            {
                long cultureId = actor?.culture?.getID() ?? -1L;
                if (actor?.data == null || cultureId < 0L) continue;
                if (!Candidates.TryGetValue(cultureId,
                        out List<Actor> actors))
                {
                    actors = new List<Actor>();
                    Candidates[cultureId] = actors;
                }
                actors.Add(actor);
            }
            foreach (List<Actor> actors in Candidates.Values)
                actors.Sort((left, right) =>
                    left.data.id.CompareTo(right.data.id));
            _candidateIndexBuilt = true;
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

        private static void Enqueue(long pCultureId)
        {
            if (pCultureId < 0L || !Enqueued.Add(pCultureId)) return;
            PendingOrder.Enqueue(pCultureId);
        }

        private static void RemovePending(long pCultureId)
        {
            Pending.Remove(pCultureId);
            CandidateCursors.Remove(pCultureId);
            Enqueued.Remove(pCultureId);
        }

        private static bool ProcessActor(Actor pActor, Culture pCulture)
        {
            if (pActor?.data == null || pActor.isRekt() ||
                pActor.culture != pCulture) return true;

            NamingProfileId profile = AWCultureNamingTraditionService
                .ResolveForActorReadOnly(pActor).Profile;
            pActor.data.get(LineageKeys.NAME_INTEGRATED,
                out bool alreadyXia, false);
            IntegratedCultureNamingMigrationAction action =
                IntegratedCultureNamingMigrationRules.Decide(
                    alive: true, sameCulture: true,
                    xiaProfile: profile == NamingProfileId.Xia,
                    alreadyXia, pActor.data.custom_name,
                    LineageService.HasProtectedAuthoredName(pActor));
            if (action == IntegratedCultureNamingMigrationAction.Skip)
                return true;

            XiaizedFamilyBranchTransitionPrepared familyTransition = null;
            if (action == IntegratedCultureNamingMigrationAction
                    .ApplyGeneratedName &&
                !XiaizedFamilyBranchTransitionService.TryPrepareForActor(
                    pActor, out familyTransition))
                return false;

            pActor.data.set(LineageKeys.NAMING_PROFILE, "xia");
            pActor.data.removeString(LineageKeys.WESTERN_NAMING_TRADITION);
            pActor.data.set(LineageKeys.NAME_INTEGRATED, true);
            if (action == IntegratedCultureNamingMigrationAction
                    .RecordProfileOnly)
            {
                LineageService.ArchiveActor(pActor, pAlive: true);
                return true;
            }

            XiaizedFamilyBranchTransitionService.Publish(familyTransition);
            AWLocalizedNameService.ProjectActor(pActor);
            LineageService.ApplyDisplayName(pActor);
            LineageService.ArchiveActor(pActor, pAlive: true);
            return true;
        }
    }
}
