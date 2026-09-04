using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.content.figures
{
    public sealed class HistoricalFigureCardRecycleInput
    {
        public HistoricalFigureCardRecycleInput(string pCardId,
            HistoricalFigureCardRarity pRarity, string pCrateId)
        {
            CardId = pCardId ?? "";
            Rarity = pRarity;
            CrateId = pCrateId ?? "";
        }

        public string CardId { get; }
        public HistoricalFigureCardRarity Rarity { get; }
        public string CrateId { get; }
    }

    public sealed class HistoricalFigureCardRecyclePlan
    {
        internal HistoricalFigureCardRecyclePlan(
            HistoricalFigureCardRarity pInputRarity,
            HistoricalFigureCardRarity pOutputRarity,
            IReadOnlyDictionary<string, int> pSourceCounts)
        {
            InputRarity = pInputRarity;
            OutputRarity = pOutputRarity;
            SourceCounts = (pSourceCounts ??
                new Dictionary<string, int>())
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        }

        public HistoricalFigureCardRarity InputRarity { get; }
        public HistoricalFigureCardRarity OutputRarity { get; }
        public IReadOnlyDictionary<string, int> SourceCounts { get; }
    }

    public static class HistoricalFigureCardRecycleRules
    {
        public static bool TryCreatePlan(
            IReadOnlyList<HistoricalFigureCardRecycleInput> pInputs,
            out HistoricalFigureCardRecyclePlan pPlan, out string pError)
        {
            pPlan = null;
            pError = "";
            if (pInputs == null || pInputs.Count == 0)
            {
                pError = "inputs_missing";
                return false;
            }

            HistoricalFigureCardRarity inputRarity = pInputs[0]?.Rarity;
            if (inputRarity == null || pInputs.Any(p => p == null ||
                    !inputRarity.Equals(p.Rarity)))
            {
                pError = "rarity_must_match";
                return false;
            }

            int required = inputRarity.Equals(HistoricalFigureCardRarity.Red)
                ? 5 : 10;
            if (pInputs.Count != required)
            {
                pError = "invalid_input_count";
                return false;
            }
            HistoricalFigureCardRarity output = NextRarity(inputRarity);
            if (output == null)
            {
                pError = "rarity_cannot_recycle";
                return false;
            }
            var sourceCounts = pInputs.GroupBy(p => p.CrateId ?? "",
                    StringComparer.Ordinal)
                .ToDictionary(p => p.Key, p => p.Count(), StringComparer.Ordinal);
            pPlan = new HistoricalFigureCardRecyclePlan(inputRarity, output,
                sourceCounts);
            return true;
        }

        public static HistoricalFigureCardRarity NextRarity(
            HistoricalFigureCardRarity pRarity)
        {
            if (pRarity == null) return null;
            if (pRarity.Equals(HistoricalFigureCardRarity.Blue))
                return HistoricalFigureCardRarity.Purple;
            if (pRarity.Equals(HistoricalFigureCardRarity.Purple))
                return HistoricalFigureCardRarity.Pink;
            if (pRarity.Equals(HistoricalFigureCardRarity.Pink))
                return HistoricalFigureCardRarity.Red;
            if (pRarity.Equals(HistoricalFigureCardRarity.Red))
                return HistoricalFigureCardRarity.Gold;
            return null;
        }

        public static string SelectWeightedCrate(
            IReadOnlyDictionary<string, int> pSourceCounts, int pRoll)
        {
            if (pSourceCounts == null || pSourceCounts.Count == 0) return "";
            var sources = pSourceCounts.Where(p => p.Value > 0)
                .OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
            int total = sources.Sum(p => p.Value);
            if (total <= 0) return "";
            int selected = pRoll < 0 ? -pRoll : pRoll;
            selected %= total;
            foreach (KeyValuePair<string, int> source in sources)
            {
                if (selected < source.Value) return source.Key;
                selected -= source.Value;
            }
            return sources[sources.Length - 1].Key;
        }
    }
}
