using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.ui.components
{
    public sealed class CourtWorkflowCanvas : MonoBehaviour
    {
        private readonly List<CourtWorkflowVacancyCard> _cards =
            new List<CourtWorkflowVacancyCard>();

        public IReadOnlyList<CourtWorkflowVacancyCard> Cards => _cards;

        public void AddCard(CourtWorkflowVacancyCard card)
        {
            if (card != null && !_cards.Contains(card)) _cards.Add(card);
        }

        public void RemoveCard(CourtWorkflowVacancyCard card)
        {
            if (card != null) _cards.Remove(card);
        }

        public void Clear()
        {
            _cards.Clear();
        }
    }
}
