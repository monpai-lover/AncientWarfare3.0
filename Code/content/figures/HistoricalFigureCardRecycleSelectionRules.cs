using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.content.figures
{
    /// <summary>
    /// Transient selection state for the standalone recycle window. It does not
    /// own persistence; the collection store remains the transaction boundary.
    /// </summary>
    public sealed class HistoricalFigureCardRecycleSelectionState
    {
        private readonly List<string> _slotCardIds = new List<string>();

        public HistoricalFigureCardRarity LockedRarity { get; internal set; }

        public IReadOnlyList<string> SlotCardIds => _slotCardIds;

        public bool HasInputs => _slotCardIds.Count > 0;

        internal IList<string> MutableSlotCardIds => _slotCardIds;
    }

    public static class HistoricalFigureCardRecycleSelectionRules
    {
        public static IReadOnlyList<HistoricalFigureCardDefinition> FilterVisible(
            IEnumerable<HistoricalFigureCardDefinition> pCards,
            IReadOnlyDictionary<string, int> pOwned,
            HistoricalFigureCardRarity pLockedRarity)
        {
            return (pCards ?? Enumerable.Empty<HistoricalFigureCardDefinition>())
                .Where(p => p != null && p.Rarity != null &&
                    !p.Rarity.Equals(HistoricalFigureCardRarity.Gold) &&
                    (pOwned == null || (pOwned.TryGetValue(p.CardId,
                        out int count) && count > 0)) &&
                    (pLockedRarity == null || p.Rarity.Equals(pLockedRarity)))
                .ToArray();
        }

        public static bool TryAdd(
            HistoricalFigureCardRecycleSelectionState pState,
            HistoricalFigureCardDefinition pCard,
            IReadOnlyDictionary<string, int> pOwned,
            out string pErrorKey)
        {
            pErrorKey = "";
            if (pState == null || pCard == null || pCard.Rarity == null)
            {
                pErrorKey = "recycle_card_missing";
                return false;
            }
            if (pCard.Rarity.Equals(HistoricalFigureCardRarity.Gold))
            {
                pErrorKey = "recycle_gold_forbidden";
                return false;
            }
            if (pState.LockedRarity != null &&
                !pState.LockedRarity.Equals(pCard.Rarity))
            {
                pErrorKey = "recycle_same_rarity";
                return false;
            }
            int owned = pOwned != null && pOwned.TryGetValue(pCard.CardId,
                out int count) ? Math.Max(0, count) : 0;
            int selected = pState.MutableSlotCardIds.Count(p =>
                string.Equals(p, pCard.CardId, StringComparison.Ordinal));
            if (selected >= owned)
            {
                pErrorKey = "recycle_insufficient_owned";
                return false;
            }
            int required = RequiredCount(pCard.Rarity);
            if (required <= 0 || pState.MutableSlotCardIds.Count >= required)
            {
                pErrorKey = "recycle_selection_full";
                return false;
            }
            if (pState.LockedRarity == null)
                pState.LockedRarity = pCard.Rarity;
            pState.MutableSlotCardIds.Add(pCard.CardId);
            return true;
        }

        public static void RemoveOne(
            HistoricalFigureCardRecycleSelectionState pState, string pCardId)
        {
            if (pState == null || string.IsNullOrEmpty(pCardId)) return;
            for (int i = pState.MutableSlotCardIds.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(pState.MutableSlotCardIds[i], pCardId,
                    StringComparison.Ordinal)) continue;
                pState.MutableSlotCardIds.RemoveAt(i);
                break;
            }
            if (!pState.HasInputs) pState.LockedRarity = null;
        }

        public static void RemoveAt(
            HistoricalFigureCardRecycleSelectionState pState, int pIndex)
        {
            if (pState == null || pIndex < 0 ||
                pIndex >= pState.MutableSlotCardIds.Count) return;
            pState.MutableSlotCardIds.RemoveAt(pIndex);
            if (!pState.HasInputs) pState.LockedRarity = null;
        }

        public static void Clear(HistoricalFigureCardRecycleSelectionState pState)
        {
            if (pState == null) return;
            pState.MutableSlotCardIds.Clear();
            pState.LockedRarity = null;
        }

        public static int RequiredCount(HistoricalFigureCardRarity pRarity)
        {
            if (pRarity == null || pRarity.Equals(HistoricalFigureCardRarity.Gold))
                return 0;
            return pRarity.Equals(HistoricalFigureCardRarity.Red) ? 5 : 10;
        }
    }
}
