using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    internal static class IntegratedCultureNamingMigrationService
    {
        internal const int DefaultBudget = 24;
        private static readonly Dictionary<long, Culture> Pending =
            new Dictionary<long, Culture>();
        private static readonly Dictionary<long, List<Actor>> Candidates =
            new Dictionary<long, List<Actor>>();

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
            Candidates.Remove(cultureId);
        }

        internal static void Reset()
        {
            Pending.Clear();
            Candidates.Clear();
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
            foreach (long cultureId in Pending.Keys.ToArray())
            {
                if (remaining <= 0) break;
                if (!Pending.TryGetValue(cultureId, out Culture culture) ||
                    culture?.data == null)
                {
                    Pending.Remove(cultureId);
                    Candidates.Remove(cultureId);
                    continue;
                }

                IntegratedCultureNamingMigrationState state =
                    IntegratedCultureNamingMigrationStatePersistence.Load(db,
                        cultureId);
                if (state == null || state.Phase == "complete")
                {
                    Pending.Remove(cultureId);
                    Candidates.Remove(cultureId);
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

                    bool found = false;
                    long latestCursor = state.CursorActorId;
                    foreach (Actor actor in actors)
                    {
                        if (actor?.data == null || actor.data.id <=
                            latestCursor) continue;
                        found = true;
                        if (!ProcessActor(actor, culture))
                        {
                            IntegratedCultureNamingMigrationStatePersistence
                                .RecordFailure(db, cultureId,
                                    "actor_migration_failed",
                                    LineageService.CurTime());
                            break;
                        }
                        IntegratedCultureNamingMigrationStatePersistence
                            .AdvanceCursor(db, cultureId, actor.data.id,
                                LineageService.CurTime());
                        latestCursor = actor.data.id;
                        remaining--;
                        if (remaining <= 0) break;
                    }

                    if (!found || remaining > 0 &&
                        !HasActorAfter(actors, latestCursor))
                    {
                        IntegratedCultureNamingMigrationStatePersistence
                            .MarkComplete(db, cultureId,
                                LineageService.CurTime());
                        Pending.Remove(cultureId);
                        Candidates.Remove(cultureId);
                    }
                }
                catch (Exception error)
                {
                    IntegratedCultureNamingMigrationStatePersistence
                        .RecordFailure(db, cultureId, error.Message,
                            LineageService.CurTime());
                }
            }
        }

        private static List<Actor> BuildCandidates(Culture pCulture)
        {
            var result = new List<Actor>();
            if (World.world?.units == null) return result;
            foreach (Actor actor in World.world.units)
            {
                if (actor?.data != null && actor.culture == pCulture)
                    result.Add(actor);
            }
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
