using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.schools
{
    internal static class SchoolLineageService
    {
        public const int DirectDiscipleCap = 8;
        private static readonly Dictionary<string, HashSet<long>> ItinerantReservations =
            new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        private static readonly Dictionary<long, long> SuccessorByTeacher =
            new Dictionary<long, long>();
        private static readonly HashSet<long> HandledTeacherDeaths = new HashSet<long>();

        public static void LoadState()
        {
            ItinerantReservations.Clear();
            SuccessorByTeacher.Clear();
            HandledTeacherDeaths.Clear();
            foreach (KeyValuePair<long, long> item in HistoricalSchoolStore.LoadLineageSuccessors())
                if (item.Key >= 0 && item.Value >= 0) SuccessorByTeacher[item.Key] = item.Value;
            foreach (HistoricalSchoolAffiliationSnapshot state in
                     HistoricalAffiliationService.ActiveSnapshots(pTravelEligibleOnly: true,
                         pTravelOnly: true))
            {
                if (state.LifecycleState != HistoricalSchoolLifecycleState.Travelling &&
                    state.LifecycleState != HistoricalSchoolLifecycleState.Voyage) continue;
                Actor actor = FindActor(state.ActorId);
                if (actor?.data == null) continue;
                TryReserveItinerant(actor, SchoolMembershipService.GetSchool(state.ActorId));
            }
        }

        public static void ClearRuntime()
        {
            ItinerantReservations.Clear();
            SuccessorByTeacher.Clear();
            HandledTeacherDeaths.Clear();
        }

        public static bool TryReserveItinerant(Actor pActor, string pSchoolId)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                string.IsNullOrWhiteSpace(pSchoolId) || CourtSchoolRegistry.Find(pSchoolId) == null)
                return false;
            if (HistoricalSchoolDescentService.IsCanonicalMaster(pActor)) return true;
            if (!IsQualifiedTeacher(pActor)) return false;
            SchoolMembershipRecord membership = SchoolMembershipService.GetActive(pActor.data.id);
            if (membership == null || !string.Equals(membership.SchoolId, pSchoolId,
                    StringComparison.Ordinal)) return false;
            if (!ItinerantReservations.TryGetValue(pSchoolId, out HashSet<long> actors))
            {
                actors = new HashSet<long>();
                ItinerantReservations[pSchoolId] = actors;
            }
            if (actors.Contains(pActor.data.id)) return true;
            foreach (KeyValuePair<string, HashSet<long>> item in ItinerantReservations)
                if (!string.Equals(item.Key, pSchoolId, StringComparison.Ordinal) &&
                    item.Value.Contains(pActor.data.id)) return false;
            if (actors.Count >= HistoricalSchoolRules.MaxNonHistoricalItinerantsPerSchool)
                return false;
            actors.Add(pActor.data.id);
            return true;
        }

        public static void ReleaseItinerant(Actor pActor)
        {
            if (pActor?.data == null) return;
            foreach (HashSet<long> reserved in ItinerantReservations.Values)
                reserved.Remove(pActor.data.id);
        }

        public static int ItinerantReservationCount(string pSchoolId)
        {
            return pSchoolId != null && ItinerantReservations.TryGetValue(pSchoolId,
                out HashSet<long> actors) ? actors.Count : 0;
        }

        public static Actor SuccessorFor(Actor pTeacher)
        {
            if (pTeacher?.data == null ||
                !SuccessorByTeacher.TryGetValue(pTeacher.data.id, out long successorId))
                return null;
            Actor successor = FindActor(successorId);
            return successor?.data != null && successor.isAlive() && !successor.isRekt()
                ? successor
                : null;
        }

        public static bool IsQualifiedTeacher(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return false;
            if (HistoricalSchoolDescentService.IsCanonicalMaster(pActor)) return true;
            return WasQualifiedTeacherAtDeath(
                SchoolMembershipService.GetActive(pActor.data.id));
        }

        public static bool WasQualifiedTeacherAtDeath(SchoolMembershipRecord pMembership)
        {
            return pMembership != null && pMembership.Active && pMembership.IsValid &&
                   (pMembership.Standing == HistoricalSchoolStanding.Teacher ||
                    pMembership.Standing == HistoricalSchoolStanding.Leader ||
                    pMembership.Standing == HistoricalSchoolStanding.CanonicalMaster);
        }

        public static int DirectDiscipleCount(long pTeacherActorId)
        {
            return BuildDirectDiscipleCounts().TryGetValue(pTeacherActorId, out int count)
                ? count
                : 0;
        }

        public static Dictionary<long, int> BuildDirectDiscipleCounts()
        {
            var result = new Dictionary<long, int>();
            foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
                foreach (long actorId in SchoolMembershipService.Members(school.Id))
                {
                    SchoolMembershipRecord membership = SchoolMembershipService.GetActive(actorId);
                    if (membership == null ||
                        (membership.Source != SchoolMembershipSource.DirectDiscipleship &&
                         membership.Source != SchoolMembershipSource.LaterDiscipleship) ||
                        membership.TeacherActorId < 0) continue;
                    result.TryGetValue(membership.TeacherActorId, out int count);
                    result[membership.TeacherActorId] = count + 1;
                }
            return result;
        }

        public static Actor SelectSuccessor(Actor pTeacher)
        {
            if (pTeacher?.data == null) return null;
            var actors = new Dictionary<long, Actor>();
            var candidates = new List<SchoolLineageCandidate>();
            Dictionary<long, int> directCounts = BuildDirectDiscipleCounts();
            foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
                foreach (long actorId in SchoolMembershipService.Members(school.Id))
                {
                    SchoolMembershipRecord membership = SchoolMembershipService.GetActive(actorId);
                    if (membership?.TeacherActorId != pTeacher.data.id) continue;
                    Actor actor = World.world?.units?.get(actorId);
                    if (actor?.data == null) continue;
                    actors[actorId] = actor;
                    bool directDisciple =
                        membership.Source == SchoolMembershipSource.DirectDiscipleship ||
                        membership.Source == SchoolMembershipSource.LaterDiscipleship;
                    candidates.Add(new SchoolLineageCandidate(actorId, actor.isAlive() &&
                        !actor.isRekt(),
                        directDisciple,
                        membership.Reputation, SafeLearning(actor), 0,
                        directCounts.TryGetValue(actorId, out int followerCount)
                            ? followerCount
                            : 0));
                }
            SchoolLineageCandidate selected = HistoricalSchoolRules.SelectLineageSuccessor(
                candidates);
            return selected != null && actors.TryGetValue(selected.ActorId, out Actor result)
                ? result
                : null;
        }

        public static void OnTeacherDeath(Actor pTeacher)
        {
            if (pTeacher?.data == null || !HandledTeacherDeaths.Add(pTeacher.data.id)) return;
            ReleaseItinerant(pTeacher);
            Actor successor = SelectSuccessor(pTeacher);
            if (successor?.data == null) return;
            SchoolMembershipRecord membership = SchoolMembershipService.GetActive(successor.data.id);
            bool recorded = HistoricalSchoolStore.RecordSchoolEvent("lineage_successor",
                successor.data.id,
                pTeacher.data.id, membership?.SchoolId ?? "", successor.city?.data?.id ?? -1L,
                successor.kingdom?.data?.id ?? -1L, Date.getCurrentYear(),
                successor.data.name ?? "", 3, World.world?.getCurWorldTime() ?? 0d);
            if (recorded) SuccessorByTeacher[pTeacher.data.id] = successor.data.id;
        }

        private static float SafeLearning(Actor pActor)
        {
            try { return Math.Max(0f, pActor.stats?["intelligence"] ?? 0f); }
            catch { return 0f; }
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }
    }
}
