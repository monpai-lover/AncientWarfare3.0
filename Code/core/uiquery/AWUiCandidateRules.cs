using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace AncientWarfare3.core.uiquery
{
    internal static class AWUiCandidateRules
    {
        public const int DefaultPageSize = 48;

        public static AWUiPage<AWUiCandidateRow> RankAndPage(
            AWUiQueryKey pKey, IEnumerable<AWUiCandidateRow> pCandidates,
            int pageIndex, int pageSize = DefaultPageSize)
        {
            AWUiCandidateRow[] ranked = Rank(pCandidates);
            int safePageSize = Math.Max(1, Math.Min(256, pageSize));
            int safePageIndex = Math.Max(0, pageIndex);
            long startLong = (long)safePageIndex * safePageSize;
            int start = startLong >= ranked.Length
                ? ranked.Length
                : (int)startLong;
            int count = Math.Min(safePageSize, ranked.Length - start);
            var items = new AWUiCandidateRow[count];
            if (count > 0) Array.Copy(ranked, start, items, 0, count);
            return new AWUiPage<AWUiCandidateRow>(pKey, ranked.Length,
                safePageIndex, items);
        }

        public static AWUiCandidateRow[] Rank(
            IEnumerable<AWUiCandidateRow> pCandidates)
        {
            var ranked = new List<AWUiCandidateRow>();
            if (pCandidates != null) ranked.AddRange(pCandidates);
            ranked.Sort(Compare);
            return ranked.ToArray();
        }

        public static AWUiLayoutPoint[] GridLayout(int pCount, int columns,
            float horizontalSpacing, float verticalSpacing)
        {
            int count = Math.Max(0, pCount);
            int columnCount = Math.Max(1, columns);
            float xSpacing = FiniteNonNegative(horizontalSpacing);
            float ySpacing = FiniteNonNegative(verticalSpacing);
            var result = new AWUiLayoutPoint[count];
            for (int index = 0; index < count; index++)
                result[index] = new AWUiLayoutPoint(
                    index % columnCount * xSpacing,
                    -(index / columnCount) * ySpacing);
            return result;
        }

        public static int TakeRenderBatch(int pPendingCount,
            int pMaximumRows)
        {
            return Math.Min(Math.Max(0, pPendingCount),
                Math.Max(0, pMaximumRows));
        }

        private static int Compare(AWUiCandidateRow pFirst,
            AWUiCandidateRow pSecond)
        {
            int primary = pSecond.PrimaryScore.CompareTo(
                pFirst.PrimaryScore);
            if (primary != 0) return primary;
            int secondary = pSecond.SecondaryScore.CompareTo(
                pFirst.SecondaryScore);
            return secondary != 0
                ? secondary
                : pFirst.ActorId.CompareTo(pSecond.ActorId);
        }

        private static float FiniteNonNegative(float pValue)
        {
            return float.IsNaN(pValue) || float.IsInfinity(pValue) ||
                   pValue < 0f
                ? 0f
                : pValue;
        }
    }

    internal static class AWUiShadowRules
    {
        public static string SummarizeLayout(IReadOnlyList<long> pActorIds,
            IReadOnlyList<AWUiLayoutPoint> pPoints)
        {
            int actorCount = pActorIds?.Count ?? 0;
            int pointCount = pPoints?.Count ?? 0;
            var result = new StringBuilder().Append("count=")
                .Append(actorCount).Append(",layout_count=")
                .Append(pointCount);
            int count = Math.Max(actorCount, pointCount);
            for (int index = 0; index < count; index++)
            {
                result.Append(';').Append("actor=");
                if (index < actorCount) result.Append(pActorIds[index]);
                else result.Append("missing");
                result.Append(",x=");
                if (index < pointCount)
                    result.Append(pPoints[index].X.ToString("R",
                        CultureInfo.InvariantCulture));
                else result.Append("missing");
                result.Append(",y=");
                if (index < pointCount)
                    result.Append(pPoints[index].Y.ToString("R",
                        CultureInfo.InvariantCulture));
                else result.Append("missing");
            }
            return result.ToString();
        }
    }

    internal sealed class AWUiCandidateRankExecution
    {
        private readonly AWUiCandidateRow[] _candidates;

        public AWUiCandidateRankExecution(
            IEnumerable<AWUiCandidateRow> pCandidates)
        {
            _candidates = pCandidates == null
                ? Array.Empty<AWUiCandidateRow>()
                : new List<AWUiCandidateRow>(pCandidates).ToArray();
        }

        public object Execute(CancellationToken pToken)
        {
            pToken.ThrowIfCancellationRequested();
            return AWUiCandidateRules.Rank(_candidates);
        }
    }
}
