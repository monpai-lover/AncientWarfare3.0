using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.schools
{
    public enum SchoolRosterStanding
    {
        HistoricalMaster = 0,
        QualifiedTeacher = 10,
        DirectDisciple = 20,
        LaterDisciple = 30,
        Member = 40
    }

    public sealed class SchoolRosterCandidate
    {
        public SchoolRosterCandidate(long pActorId, string pSchoolId,
            SchoolMembershipSource pSource, long pTeacherActorId, int pGeneration,
            float pReputation, int pFollowerCount, float pLearning, int pStartYear,
            bool pCanonicalMaster, bool pQualifiedTeacher, bool pAlive = true,
            bool pMembershipValid = true)
        {
            ActorId = pActorId;
            SchoolId = pSchoolId ?? "";
            Source = pSource;
            TeacherActorId = pTeacherActorId;
            Generation = Math.Max(0, pGeneration);
            Reputation = FiniteNonNegative(pReputation);
            FollowerCount = Math.Max(0, pFollowerCount);
            Learning = FiniteNonNegative(pLearning);
            StartYear = pStartYear;
            CanonicalMaster = pCanonicalMaster;
            QualifiedTeacher = pQualifiedTeacher;
            Alive = pAlive;
            MembershipValid = pMembershipValid;
        }

        public long ActorId { get; }
        public string SchoolId { get; }
        public SchoolMembershipSource Source { get; }
        public long TeacherActorId { get; }
        public int Generation { get; }
        public float Reputation { get; }
        public int FollowerCount { get; }
        public float Learning { get; }
        public int StartYear { get; }
        public bool CanonicalMaster { get; }
        public bool QualifiedTeacher { get; }
        public bool Alive { get; }
        public bool MembershipValid { get; }

        private static float FiniteNonNegative(float pValue)
        {
            return float.IsNaN(pValue) || float.IsInfinity(pValue)
                ? 0f
                : Math.Max(0f, pValue);
        }
    }

    public sealed class SchoolRosterNode
    {
        internal SchoolRosterNode(SchoolRosterCandidate pCandidate,
            SchoolRosterStanding pStanding, int pStableOrder)
        {
            Candidate = pCandidate;
            Standing = pStanding;
            StableOrder = pStableOrder;
        }

        public SchoolRosterCandidate Candidate { get; }
        public long ActorId => Candidate.ActorId;
        public string SchoolId => Candidate.SchoolId;
        public long TeacherActorId => Candidate.TeacherActorId;
        public int Generation => Candidate.Generation;
        public float Reputation => Candidate.Reputation;
        public int FollowerCount => Candidate.FollowerCount;
        public float Learning => Candidate.Learning;
        public int StartYear => Candidate.StartYear;
        public SchoolRosterStanding Standing { get; }
        public int StableOrder { get; }
        public int Row { get; internal set; }
        public int Column { get; internal set; }
        public float X { get; internal set; }
        public float Y { get; internal set; }
    }

    public readonly struct SchoolRosterLink
    {
        public SchoolRosterLink(long pTeacherActorId, long pStudentActorId)
        {
            TeacherActorId = pTeacherActorId;
            StudentActorId = pStudentActorId;
        }

        public long TeacherActorId { get; }
        public long StudentActorId { get; }
    }

    public sealed class SchoolRosterLayout
    {
        internal SchoolRosterLayout(IReadOnlyList<SchoolRosterNode> pNodes,
            IReadOnlyList<SchoolRosterLink> pLinks, int pExcludedCount)
        {
            Nodes = pNodes ?? Array.Empty<SchoolRosterNode>();
            Links = pLinks ?? Array.Empty<SchoolRosterLink>();
            ExcludedCount = Math.Max(0, pExcludedCount);
        }

        public IReadOnlyList<SchoolRosterNode> Nodes { get; }
        public IReadOnlyList<SchoolRosterLink> Links { get; }
        public int ExcludedCount { get; }
    }

    public static class SchoolRosterRules
    {
        public const int DefaultColumnsPerRow = 6;

        public static SchoolRosterStanding StandingFor(SchoolRosterCandidate pCandidate)
        {
            if (pCandidate?.CanonicalMaster == true)
                return SchoolRosterStanding.HistoricalMaster;
            if (pCandidate?.QualifiedTeacher == true)
                return SchoolRosterStanding.QualifiedTeacher;
            if (pCandidate?.Source == SchoolMembershipSource.DirectDiscipleship)
                return SchoolRosterStanding.DirectDisciple;
            if (pCandidate?.Source == SchoolMembershipSource.LaterDiscipleship)
                return SchoolRosterStanding.LaterDisciple;
            return SchoolRosterStanding.Member;
        }

        public static SchoolRosterLayout Build(string pSchoolId,
            IEnumerable<SchoolRosterCandidate> pCandidates, float pHorizontalSpacing,
            float pVerticalSpacing, int pColumnsPerRow = DefaultColumnsPerRow)
        {
            SchoolRosterCandidate[] source = (pCandidates ??
                Array.Empty<SchoolRosterCandidate>()).ToArray();
            var seen = new HashSet<long>();
            int excluded = 0;
            var valid = new List<SchoolRosterCandidate>(source.Length);
            foreach (SchoolRosterCandidate candidate in source)
            {
                if (!IsVisibleMember(pSchoolId, candidate) || !seen.Add(candidate.ActorId))
                {
                    excluded++;
                    continue;
                }
                valid.Add(candidate);
            }

            List<SchoolRosterNode> nodes = valid
                .Select(p => new SchoolRosterNode(p, StandingFor(p), 0))
                .OrderBy(p => p.Standing)
                .ThenByDescending(p => p.Reputation)
                .ThenByDescending(p => p.FollowerCount)
                .ThenByDescending(p => p.Learning)
                .ThenBy(p => p.StartYear < 0 ? int.MaxValue : p.StartYear)
                .ThenBy(p => p.ActorId)
                .ToList();
            for (int i = 0; i < nodes.Count; i++)
            {
                SchoolRosterNode current = nodes[i];
                nodes[i] = new SchoolRosterNode(current.Candidate, current.Standing, i);
            }

            LayoutRows(nodes, pHorizontalSpacing, pVerticalSpacing, pColumnsPerRow);
            var byActor = nodes.ToDictionary(p => p.ActorId);
            var links = new List<SchoolRosterLink>();
            foreach (SchoolRosterNode student in nodes)
            {
                if (!HasTeacherSource(student.Candidate.Source) ||
                    student.TeacherActorId < 0 || student.TeacherActorId == student.ActorId ||
                    !byActor.TryGetValue(student.TeacherActorId, out SchoolRosterNode teacher) ||
                    !string.Equals(teacher.SchoolId, student.SchoolId,
                        StringComparison.Ordinal)) continue;
                links.Add(new SchoolRosterLink(teacher.ActorId, student.ActorId));
            }
            return new SchoolRosterLayout(nodes, links, excluded);
        }

        private static bool IsVisibleMember(string pSchoolId, SchoolRosterCandidate pCandidate)
        {
            return pCandidate != null && pCandidate.ActorId >= 0 && pCandidate.Alive &&
                   pCandidate.MembershipValid && !string.IsNullOrWhiteSpace(pSchoolId) &&
                   string.Equals(pCandidate.SchoolId, pSchoolId, StringComparison.Ordinal);
        }

        private static bool HasTeacherSource(SchoolMembershipSource pSource)
        {
            return pSource == SchoolMembershipSource.DirectDiscipleship ||
                   pSource == SchoolMembershipSource.LaterDiscipleship;
        }

        private static void LayoutRows(List<SchoolRosterNode> pNodes,
            float pHorizontalSpacing, float pVerticalSpacing, int pColumnsPerRow)
        {
            float horizontal = Math.Max(1f, pHorizontalSpacing);
            float vertical = Math.Max(1f, pVerticalSpacing);
            int columns = Math.Max(1, pColumnsPerRow);
            int rowIndex = 0;
            foreach (IGrouping<SchoolRosterStanding, SchoolRosterNode> tier in pNodes
                         .GroupBy(p => p.Standing).OrderBy(p => p.Key))
            {
                SchoolRosterNode[] items = tier.ToArray();
                for (int start = 0; start < items.Length; start += columns)
                {
                    SchoolRosterNode[] row = items.Skip(start).Take(columns).ToArray();
                    float startX = -(row.Length - 1) * horizontal * 0.5f;
                    for (int column = 0; column < row.Length; column++)
                    {
                        row[column].Row = rowIndex;
                        row[column].Column = column;
                        row[column].X = startX + column * horizontal;
                        row[column].Y = -rowIndex * vertical;
                    }
                    rowIndex++;
                }
            }
        }
    }
}
