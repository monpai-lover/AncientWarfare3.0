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
        public SchoolRosterReadModel(string pSchoolId,
            HistoricalSchoolRosterRevisionStamp pRevisionStamp,
            IReadOnlyList<SchoolRosterReadNode> pNodes,
            IReadOnlyList<SchoolRosterLink> pLinks, int pExcludedCount, int pTeacherCount)
        {
            SchoolId = pSchoolId ?? "";
            RevisionStamp = pRevisionStamp;
            Nodes = pNodes ?? Array.Empty<SchoolRosterReadNode>();
            Links = pLinks ?? Array.Empty<SchoolRosterLink>();
            ExcludedCount = Math.Max(0, pExcludedCount);
            TeacherCount = Math.Max(0, pTeacherCount);
        }

        public string SchoolId { get; }
        public HistoricalSchoolRosterRevisionStamp RevisionStamp { get; }
        public IReadOnlyList<SchoolRosterReadNode> Nodes { get; }
        public IReadOnlyList<SchoolRosterLink> Links { get; }
        public int ExcludedCount { get; }
        public int TeacherCount { get; }
    }

    internal sealed class SchoolRosterCapture
    {
        public SchoolRosterCapture(string pSchoolId,
            HistoricalSchoolRosterRevisionStamp pRevisionStamp,
            SchoolRosterCandidate[] pCandidates,
            Dictionary<long, SchoolRosterActorMetadata> pMetadata)
        {
            SchoolId = pSchoolId ?? "";
            RevisionStamp = pRevisionStamp;
            Candidates = pCandidates ?? Array.Empty<SchoolRosterCandidate>();
            Metadata = pMetadata ??
                new Dictionary<long, SchoolRosterActorMetadata>();
        }

        public string SchoolId { get; }
        public HistoricalSchoolRosterRevisionStamp RevisionStamp { get; }
        public SchoolRosterCandidate[] Candidates { get; }
        internal Dictionary<long, SchoolRosterActorMetadata> Metadata { get; }
    }

    internal sealed class SchoolRosterActorMetadata
    {
        public SchoolRosterActorMetadata(Actor pActor,
            SchoolMembershipRecord pMembership, Kingdom pDisplayKingdom,
            City pResidenceCity, string pActorName)
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

    internal static class SchoolRosterReadModelService
    {
        public static SchoolRosterReadModel Build(string pSchoolId,
            float pHorizontalSpacing, float pVerticalSpacing, int pColumnsPerRow)
        {
            SchoolRosterCapture capture = Capture(pSchoolId);
            SchoolRosterLayout layout = SchoolRosterRules.Build(
                capture.SchoolId, capture.Candidates, pHorizontalSpacing,
                pVerticalSpacing, pColumnsPerRow);
            return Materialize(capture, layout);
        }

        public static SchoolRosterCapture Capture(string pSchoolId)
        {
            long[] memberIds = SchoolMembershipService.Members(pSchoolId);
            Dictionary<long, SchoolLectureSeniority> lectureSeniority =
                HistoricalSchoolStore.LoadEarliestLectureSeniority(pSchoolId);
            var candidates = new List<SchoolRosterCandidate>(memberIds.Length);
            var metadata = new Dictionary<long, SchoolRosterActorMetadata>();

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
                int followers = HistoricalSchoolRuntimeIndex.Instance.DirectDiscipleCount(actorId);
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
                    membershipValid, firstLectureYear, firstLectureTime, SafeAge(actor),
                    membership?.Standing ?? HistoricalSchoolStanding.Member));

                if (!live || !membershipValid) continue;
                City residence = HistoricalAffiliationService.ResidenceCity(actor) ?? actor.city;
                Kingdom displayKingdom = HistoricalAffiliationService.ServiceKingdom(actor) ??
                                         residence?.kingdom ?? actor.kingdom;
                metadata[actorId] = new SchoolRosterActorMetadata(actor,
                    membership,
                    displayKingdom, residence, SafeName(actor));
            }

            HistoricalSchoolRosterRevisionStamp revisionStamp =
                HistoricalSchoolRosterRevisionStamp.Capture(pSchoolId,
                    metadata.Values.Select(p =>
                        p.ResidenceCity?.data?.id ?? -1L),
                    HistoricalSchoolRevisionService.Source);
            return new SchoolRosterCapture(pSchoolId, revisionStamp,
                candidates.ToArray(), metadata);
        }

        public static SchoolRosterReadModel Materialize(
            SchoolRosterCapture pCapture, SchoolRosterLayout pLayout)
        {
            if (pCapture == null || pLayout == null)
                return new SchoolRosterReadModel("", null,
                    Array.Empty<SchoolRosterReadNode>(),
                    Array.Empty<SchoolRosterLink>(), 0, 0);
            Dictionary<long, SchoolRosterActorMetadata> metadata =
                pCapture.Metadata;
            var nodes = new List<SchoolRosterReadNode>(pLayout.Nodes.Count);
            foreach (SchoolRosterNode node in pLayout.Nodes)
            {
                if (!metadata.TryGetValue(node.ActorId,
                        out SchoolRosterActorMetadata value))
                    continue;
                string teacherName = node.TeacherActorId < 0
                    ? ""
                    : metadata.TryGetValue(node.TeacherActorId,
                        out SchoolRosterActorMetadata teacher)
                        ? teacher.ActorName
                        : SafeName(FindActor(node.TeacherActorId));
                nodes.Add(new SchoolRosterReadNode(node, value.Actor, value.Membership,
                    value.DisplayKingdom, value.ResidenceCity, value.ActorName, teacherName));
            }

            int teacherCount = pLayout.Nodes.Count(p =>
                p.Candidate.PersistedStanding == HistoricalSchoolStanding.CanonicalMaster ||
                p.Candidate.PersistedStanding == HistoricalSchoolStanding.Leader ||
                p.Candidate.PersistedStanding == HistoricalSchoolStanding.Teacher);
            return new SchoolRosterReadModel(pCapture.SchoolId,
                pCapture.RevisionStamp, nodes, pLayout.Links,
                pLayout.ExcludedCount, teacherCount);
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

    }
}
