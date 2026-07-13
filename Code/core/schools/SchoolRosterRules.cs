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
        public int LineageDepth { get; internal set; }
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

    public readonly struct SchoolRosterCanvasPlacement
    {
        internal SchoolRosterCanvasPlacement(float pCanvasWidth, float pCanvasHeight,
            float pNodeOffsetX, float pNodeOffsetY, float pInitialPanX,
            float pInitialPanY)
        {
            CanvasWidth = pCanvasWidth;
            CanvasHeight = pCanvasHeight;
            NodeOffsetX = pNodeOffsetX;
            NodeOffsetY = pNodeOffsetY;
            InitialPanX = pInitialPanX;
            InitialPanY = pInitialPanY;
        }

        public float CanvasWidth { get; }
        public float CanvasHeight { get; }
        public float NodeOffsetX { get; }
        public float NodeOffsetY { get; }
        public float InitialPanX { get; }
        public float InitialPanY { get; }
    }

    public static class SchoolRosterRules
    {
        public const int DefaultColumnsPerRow = 6;

        public static SchoolRosterCanvasPlacement PlaceCanvas(float pViewportWidth,
            float pViewportHeight, float pMinX, float pMaxX, float pMinY, float pMaxY,
            float pPadding)
        {
            float viewportWidth = PositiveOrOne(pViewportWidth);
            float viewportHeight = PositiveOrOne(pViewportHeight);
            float padding = FiniteNonNegative(pPadding);
            float minX = FiniteOrZero(Math.Min(pMinX, pMaxX));
            float maxX = FiniteOrZero(Math.Max(pMinX, pMaxX));
            float minY = FiniteOrZero(Math.Min(pMinY, pMaxY));
            float maxY = FiniteOrZero(Math.Max(pMinY, pMaxY));
            float halfWidth = Math.Max(Math.Abs(minX), Math.Abs(maxX));
            float canvasWidth = Math.Max(viewportWidth, halfWidth * 2f + padding * 2f);
            float canvasHeight = Math.Max(viewportHeight,
                Math.Max(0f, maxY - minY) + padding * 2f);
            float initialPanX = canvasWidth <= viewportWidth
                ? 0f
                : (viewportWidth - canvasWidth) * .5f;
            return new SchoolRosterCanvasPlacement(canvasWidth, canvasHeight,
                canvasWidth * .5f, -padding - maxY, initialPanX, 0f);
        }

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

        private static float PositiveOrOne(float pValue)
        {
            return float.IsNaN(pValue) || float.IsInfinity(pValue) || pValue <= 0f
                ? 1f
                : pValue;
        }

        private static float FiniteNonNegative(float pValue)
        {
            return float.IsNaN(pValue) || float.IsInfinity(pValue)
                ? 0f
                : Math.Max(0f, pValue);
        }

        private static float FiniteOrZero(float pValue)
        {
            return float.IsNaN(pValue) || float.IsInfinity(pValue) ? 0f : pValue;
        }

        private static void LayoutRows(List<SchoolRosterNode> pNodes,
            float pHorizontalSpacing, float pVerticalSpacing, int pColumnsPerRow)
        {
            float horizontal = Math.Max(1f, pHorizontalSpacing);
            float vertical = Math.Max(1f, pVerticalSpacing);
            int columns = Math.Max(1, pColumnsPerRow);
            var byActor = pNodes.ToDictionary(p => p.ActorId);
            var depthByActor = new Dictionary<long, int>();
            int rowIndex = 0;
            foreach (IGrouping<SchoolRosterStanding, SchoolRosterNode> tier in pNodes
                         .GroupBy(p => p.Standing).OrderBy(p => p.Key))
            {
                SchoolRosterNode[] tierItems = tier.ToArray();
                foreach (SchoolRosterNode node in tierItems)
                    node.LineageDepth = ResolveLineageDepth(node, byActor, depthByActor,
                        new HashSet<long>());
                foreach (IGrouping<int, SchoolRosterNode> depth in tierItems
                             .GroupBy(p => p.LineageDepth).OrderBy(p => p.Key))
                {
                    SchoolRosterNode[] items = depth.ToArray();
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

        private static int ResolveLineageDepth(SchoolRosterNode pNode,
            IReadOnlyDictionary<long, SchoolRosterNode> pByActor,
            IDictionary<long, int> pDepthByActor, ISet<long> pVisiting)
        {
            if (pNode == null) return 0;
            if (pDepthByActor.TryGetValue(pNode.ActorId, out int cached)) return cached;
            if (!pVisiting.Add(pNode.ActorId)) return 0;
            int depth = 0;
            if (HasTeacherSource(pNode.Candidate.Source) && pNode.TeacherActorId >= 0 &&
                pByActor.TryGetValue(pNode.TeacherActorId, out SchoolRosterNode teacher) &&
                teacher.ActorId != pNode.ActorId && teacher.Standing <= pNode.Standing)
                depth = Math.Min(100, ResolveLineageDepth(teacher, pByActor, pDepthByActor,
                    pVisiting) + 1);
            pVisiting.Remove(pNode.ActorId);
            pDepthByActor[pNode.ActorId] = depth;
            return depth;
        }
    }
}
