using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.ui.components
{
    public sealed class CourtWorkflowCanvas : MonoBehaviour
    {
        private readonly List<CourtWorkflowOfficeCard> _cards =
            new List<CourtWorkflowOfficeCard>();

        public IReadOnlyList<CourtWorkflowOfficeCard> Cards => _cards;

        public void AddCard(CourtWorkflowOfficeCard card)
        {
            if (card != null && !_cards.Contains(card)) _cards.Add(card);
        }

        public void RemoveCard(CourtWorkflowOfficeCard card)
        {
            if (card != null) _cards.Remove(card);
        }
    }
}
