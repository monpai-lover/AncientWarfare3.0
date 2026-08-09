using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class ReigningRoyalLineageIndexState
    {
        private readonly Dictionary<long, HashSet<long>>
            _kingdomsByLineage = new Dictionary<long, HashSet<long>>();
        private readonly Dictionary<long, long> _lineageByKingdom =
            new Dictionary<long, long>();

        public bool IsReady { get; private set; }

        public void BeginRebuild()
        {
            Clear();
        }

        public void CompleteRebuild()
        {
            IsReady = true;
        }

        public void Register(long pLineageId, long pKingdomId)
        {
            if (pLineageId < 0L || pKingdomId < 0L) return;
            RemoveKingdom(pKingdomId);
            if (!_kingdomsByLineage.TryGetValue(pLineageId,
                    out HashSet<long> kingdoms))
            {
                kingdoms = new HashSet<long>();
                _kingdomsByLineage.Add(pLineageId, kingdoms);
            }
            kingdoms.Add(pKingdomId);
            _lineageByKingdom[pKingdomId] = pLineageId;
        }

        public void RemoveKingdom(long pKingdomId)
        {
            if (!_lineageByKingdom.TryGetValue(pKingdomId,
                    out long lineageId)) return;
            _lineageByKingdom.Remove(pKingdomId);
            if (!_kingdomsByLineage.TryGetValue(lineageId,
                    out HashSet<long> kingdoms)) return;
            kingdoms.Remove(pKingdomId);
            if (kingdoms.Count == 0)
                _kingdomsByLineage.Remove(lineageId);
        }

        public bool HasReigningKing(long pLineageId)
        {
            return pLineageId >= 0L &&
                   _kingdomsByLineage.TryGetValue(pLineageId,
                       out HashSet<long> kingdoms) && kingdoms.Count > 0;
        }

        public bool Contains(long pLineageId, long pKingdomId)
        {
            return _lineageByKingdom.TryGetValue(pKingdomId,
                       out long registered) && registered == pLineageId;
        }

        public void Clear()
        {
            _kingdomsByLineage.Clear();
            _lineageByKingdom.Clear();
            IsReady = false;
        }
    }
}
