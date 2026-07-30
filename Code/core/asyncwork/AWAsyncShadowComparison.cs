using System;
using System.Collections.Generic;
using System.Text;

namespace AncientWarfare3.core.asyncwork
{
    internal readonly struct AWAsyncShadowComparison
    {
        public AWAsyncShadowComparison(bool pIsMatch, string pMessage)
        {
            IsMatch = pIsMatch;
            Message = pMessage ?? string.Empty;
        }

        public bool IsMatch { get; }
        public string Message { get; }
    }

    internal static class AWAsyncShadowComparisonRules
    {
        private const int SummaryLimit = 8;

        public static AWAsyncShadowComparison CompareIds(
            long worldGeneration, string channel, string key,
            IReadOnlyList<long> synchronousIds,
            IReadOnlyList<long> asynchronousIds)
        {
            IReadOnlyList<long> synchronous = synchronousIds ??
                Array.Empty<long>();
            IReadOnlyList<long> asynchronous = asynchronousIds ??
                Array.Empty<long>();
            bool match = synchronous.Count == asynchronous.Count;
            if (match)
                for (int index = 0; index < synchronous.Count; index++)
                    if (synchronous[index] != asynchronous[index])
                    {
                        match = false;
                        break;
                    }
            if (match) return new AWAsyncShadowComparison(true, string.Empty);
            string message = "[AW3 ASYNC SHADOW] world=" +
                worldGeneration + " channel=" + (channel ?? string.Empty) +
                " key=" + (key ?? string.Empty) + " sync=" +
                Summarize(synchronous) + " async=" +
                Summarize(asynchronous);
            return new AWAsyncShadowComparison(false, message);
        }

        public static AWAsyncShadowComparison CompareSummary(
            long worldGeneration, string channel, string key,
            string synchronousSummary, string asynchronousSummary)
        {
            string synchronous = synchronousSummary ?? string.Empty;
            string asynchronous = asynchronousSummary ?? string.Empty;
            if (string.Equals(synchronous, asynchronous,
                    StringComparison.Ordinal))
                return new AWAsyncShadowComparison(true, string.Empty);
            string message = "[AW3 ASYNC SHADOW] world=" +
                worldGeneration + " channel=" + (channel ?? string.Empty) +
                " key=" + (key ?? string.Empty) + " sync=" + synchronous +
                " async=" + asynchronous;
            return new AWAsyncShadowComparison(false, message);
        }

        private static string Summarize(IReadOnlyList<long> pIds)
        {
            var result = new StringBuilder();
            int count = Math.Min(SummaryLimit, pIds.Count);
            for (int index = 0; index < count; index++)
            {
                if (index > 0) result.Append(',');
                result.Append(pIds[index]);
            }
            if (pIds.Count > count)
                result.Append(",...(").Append(pIds.Count).Append(')');
            return result.ToString();
        }
    }
}
