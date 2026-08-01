using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AncientWarfare3.core.policy
{
    internal sealed class HierarchicalVassalMapModeState
    {
        private readonly List<long> _breadcrumbs = new List<long>();
        private readonly List<int> _ranks = new List<int>();
        private readonly ReadOnlyCollection<long> _readOnlyBreadcrumbs;

        public HierarchicalVassalMapModeState()
        {
            _readOnlyBreadcrumbs = _breadcrumbs.AsReadOnly();
        }

        public bool IsRoot => _breadcrumbs.Count == 0;

        public long FocusKingdomId => IsRoot
            ? -1L
            : _breadcrumbs[_breadcrumbs.Count - 1];

        public long ParentFocusKingdomId => _breadcrumbs.Count < 2
            ? -1L
            : _breadcrumbs[_breadcrumbs.Count - 2];

        public int CurrentRank => IsRoot ? -1 : _ranks[_ranks.Count - 1];

        public IReadOnlyList<long> Breadcrumbs => _readOnlyBreadcrumbs;

        public void Reset()
        {
            _breadcrumbs.Clear();
            _ranks.Clear();
        }

        public void PushFocus(long pKingdomId, int pRank)
        {
            if (pKingdomId < 0L) return;
            _breadcrumbs.Add(pKingdomId);
            _ranks.Add(pRank);
        }

        public bool TryPushFocus(long pKingdomId, int pRank,
            bool pHasDirectVassals)
        {
            if (!HierarchicalVassalMapModeRules.CanDrill(
                    pHasDirectVassals) || pKingdomId < 0L)
                return false;
            PushFocus(pKingdomId, pRank);
            return true;
        }

        public bool PopFocus()
        {
            if (IsRoot) return false;
            int last = _breadcrumbs.Count - 1;
            _breadcrumbs.RemoveAt(last);
            _ranks.RemoveAt(last);
            return true;
        }
    }
}
