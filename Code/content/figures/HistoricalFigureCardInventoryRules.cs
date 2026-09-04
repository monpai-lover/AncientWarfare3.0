using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.content.figures
{
    public enum HistoricalFigureCardInventorySort
    {
        Latest,
        Rarity,
        Name,
        Fame
    }

    public static class HistoricalFigureCardInventoryRules
    {
        public static IReadOnlyList<HistoricalFigureCardDefinition> Sort(
            IEnumerable<HistoricalFigureCardDefinition> pCards,
            HistoricalFigureCardInventorySort pSort,
            IReadOnlyDictionary<string, int> pLatestRanks = null)
        {
            IEnumerable<HistoricalFigureCardDefinition> cards =
                (pCards ?? Enumerable.Empty<HistoricalFigureCardDefinition>())
                .Where(p => p != null);
            switch (pSort)
            {
                case HistoricalFigureCardInventorySort.Latest:
                    return cards.OrderBy(p => LatestRank(p.CardId, pLatestRanks))
                        .ThenByDescending(p => p.FameScore)
                        .ThenBy(p => p.CardId, StringComparer.Ordinal).ToArray();
                case HistoricalFigureCardInventorySort.Rarity:
                    return cards.OrderBy(p => RarityRank(p.Rarity))
                        .ThenByDescending(p => p.FameScore)
                        .ThenBy(p => p.CardId, StringComparer.Ordinal).ToArray();
                case HistoricalFigureCardInventorySort.Name:
                    return cards.OrderBy(p => p.DisplayName,
                            StringComparer.Ordinal)
                        .ThenBy(p => p.CardId, StringComparer.Ordinal).ToArray();
                default:
                    return cards.OrderByDescending(p => p.FameScore)
                        .ThenBy(p => p.HistoricalYear < 0
                            ? int.MaxValue : p.HistoricalYear)
                        .ThenBy(p => p.CardId, StringComparer.Ordinal).ToArray();
            }
        }

        private static int LatestRank(string pCardId,
            IReadOnlyDictionary<string, int> pLatestRanks)
        {
            return pLatestRanks != null && pCardId != null &&
                   pLatestRanks.TryGetValue(pCardId, out int rank)
                ? rank
                : int.MaxValue;
        }

        private static int RarityRank(HistoricalFigureCardRarity pRarity)
        {
            if (pRarity == null) return int.MaxValue;
            for (int i = 0; i < HistoricalFigureCardRarity.All.Count; i++)
                if (HistoricalFigureCardRarity.All[i].Equals(pRarity)) return i;
            return int.MaxValue;
        }
    }
}
