using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.schools
{
    internal sealed class SchoolRosterReadNode
    {
        public SchoolRosterReadNode(SchoolRosterNode pLayout, Actor pActor,
            SchoolMembershipRecord pMembership, Kingdom pDisplayKingdom, City pResidenceCity,
            string pActorName, string pTeacherName)
        {
            Layout = pLayout;
            Actor = pActor;
            Membership = pMembership;
            DisplayKingdom = pDisplayKingdom;
            ResidenceCity = pResidenceCity;
            ActorName = pActorName ?? "";
            TeacherName = pTeacherName ?? "";
        }

        public SchoolRosterNode Layout { get; }
        public Actor Actor { get; }
        public SchoolMembershipRecord Membership { get; }
        public Kingdom DisplayKingdom { get; }
        public City ResidenceCity { get; }
        public string ActorName { get; }
        public string TeacherName { get; }
    }

    internal sealed class SchoolRosterReadModel
    {
        public SchoolRosterReadModel(string pSchoolId, long pMembershipVersion,
            long pResidenceRevision, long pLectureRevision,
            IReadOnlyList<SchoolRosterReadNode> pNodes,
            IReadOnlyList<SchoolRosterLink> pLinks, int pExcludedCount, int pTeacherCount)
        {
            SchoolId = pSchoolId ?? "";
            MembershipVersion = pMembershipVersion;
            ResidenceRevision = pResidenceRevision;
            LectureRevision = pLectureRevision;
            Nodes = pNodes ?? Array.Empty<SchoolRosterReadNode>();
            Links = pLinks ?? Array.Empty<SchoolRosterLink>();
            ExcludedCount = Math.Max(0, pExcludedCount);
            TeacherCount = Math.Max(0, pTeacherCount);
        }

        public string SchoolId { get; }
        public long MembershipVersion { get; }
        public long ResidenceRevision { get; }
        public long LectureRevision { get; }
        public IReadOnlyList<SchoolRosterReadNode> Nodes { get; }
        public IReadOnlyList<SchoolRosterLink> Links { get; }
        public int ExcludedCount { get; }
        public int TeacherCount { get; }
    }

    internal static class SchoolRosterReadModelService
    {
        public static SchoolRosterReadModel Build(string pSchoolId,
            float pHorizontalSpacing, float pVerticalSpacing, int pColumnsPerRow)
        {
            long membershipVersion = SchoolMembershipService.Version;
            long residenceRevision = HistoricalAffiliationService.ResidenceRevision;
            long lectureRevision = HistoricalSchoolStore.LectureRevision;
            long[] memberIds = SchoolMembershipService.Members(pSchoolId);
            Dictionary<long, SchoolLectureSeniority> lectureSeniority =
                HistoricalSchoolStore.LoadEarliestLectureSeniority(pSchoolId);
            Dictionary<long, int> followerCounts =
                SchoolLineageService.BuildDirectDiscipleCounts();
            var candidates = new List<SchoolRosterCandidate>(memberIds.Length);
            var metadata = new Dictionary<long, RosterActorMetadata>();

            foreach (long actorId in memberIds)
            {
                SchoolMembershipRecord membership = SchoolMembershipService.GetActive(actorId);
                Actor actor = FindActor(actorId);
                bool live = actor?.data != null && actor.isAlive() && !actor.isRekt();
                bool membershipValid = membership != null && membership.Active &&
                    membership.ActorId == actorId && string.Equals(membership.SchoolId,
                        pSchoolId, StringComparison.Ordinal);
                string schoolId = membership?.SchoolId ?? pSchoolId ?? "";
                SchoolMembershipSource source = membership?.Source ??
                    SchoolMembershipSource.AuthoredEvent;
                followerCounts.TryGetValue(actorId, out int followers);
                bool canonical = live &&
                    HistoricalSchoolDescentService.IsCanonicalMaster(actor);
                bool qualifiedTeacher = live &&
                    SchoolLineageService.IsQualifiedTeacher(actor);
                int firstLectureYear = int.MaxValue;
                double firstLectureTime = double.MaxValue;
                if (lectureSeniority.TryGetValue(actorId,
                        out SchoolLectureSeniority lecture))
                {
                    firstLectureYear = lecture.FirstLectureYear;
                    firstLectureTime = lecture.FirstLectureTime;
                }
                candidates.Add(new SchoolRosterCandidate(actorId, schoolId, source,
                    membership?.TeacherActorId ?? -1L, membership?.Generation ?? 0,
                    membership?.Reputation ?? 0f, followers, Learning(actor),
                    membership?.StartYear ?? -1, canonical, qualifiedTeacher, live,
                    membershipValid, firstLectureYear, firstLectureTime, SafeAge(actor)));

                if (!live || !membershipValid) continue;
                City residence = HistoricalAffiliationService.ResidenceCity(actor) ?? actor.city;
                Kingdom displayKingdom = HistoricalAffiliationService.ServiceKingdom(actor) ??
                                         residence?.kingdom ?? actor.kingdom;
                metadata[actorId] = new RosterActorMetadata(actor, membership,
                    displayKingdom, residence, SafeName(actor));
            }

            SchoolRosterLayout layout = SchoolRosterRules.Build(pSchoolId, candidates,
                pHorizontalSpacing, pVerticalSpacing, pColumnsPerRow);
            var nodes = new List<SchoolRosterReadNode>(layout.Nodes.Count);
            foreach (SchoolRosterNode node in layout.Nodes)
            {
                if (!metadata.TryGetValue(node.ActorId, out RosterActorMetadata value))
                    continue;
                string teacherName = node.TeacherActorId < 0
                    ? ""
                    : metadata.TryGetValue(node.TeacherActorId,
                        out RosterActorMetadata teacher)
                        ? teacher.ActorName
                        : SafeName(FindActor(node.TeacherActorId));
                nodes.Add(new SchoolRosterReadNode(node, value.Actor, value.Membership,
                    value.DisplayKingdom, value.ResidenceCity, value.ActorName, teacherName));
            }

            int teacherCount = layout.Nodes.Count(p =>
                p.Candidate.CanonicalMaster || p.Candidate.QualifiedTeacher);
            return new SchoolRosterReadModel(pSchoolId, membershipVersion,
                residenceRevision, lectureRevision, nodes, layout.Links,
                layout.ExcludedCount, teacherCount);
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static float Learning(Actor pActor)
        {
            float learning = SafeStat(pActor, "learning");
            return learning > 0f ? learning : SafeStat(pActor, "intelligence");
        }

        private static int SafeAge(Actor pActor)
        {
            try { return Math.Max(0, pActor?.getAge() ?? 0); }
            catch { return 0; }
        }

        private static float SafeStat(Actor pActor, string pStat)
        {
            try
            {
                float value = pActor?.stats?[pStat] ?? 0f;
                return float.IsNaN(value) || float.IsInfinity(value)
                    ? 0f
                    : Math.Max(0f, value);
            }
            catch { return 0f; }
        }

        private static string SafeName(Actor pActor)
        {
            try { return pActor?.getName() ?? ""; }
            catch { return pActor?.data?.name ?? ""; }
        }

        private sealed class RosterActorMetadata
        {
            public RosterActorMetadata(Actor pActor, SchoolMembershipRecord pMembership,
                Kingdom pDisplayKingdom, City pResidenceCity, string pActorName)
            {
                Actor = pActor;
                Membership = pMembership;
                DisplayKingdom = pDisplayKingdom;
                ResidenceCity = pResidenceCity;
                ActorName = pActorName ?? "";
            }

            public Actor Actor { get; }
            public SchoolMembershipRecord Membership { get; }
            public Kingdom DisplayKingdom { get; }
            public City ResidenceCity { get; }
            public string ActorName { get; }
        }
    }
}
