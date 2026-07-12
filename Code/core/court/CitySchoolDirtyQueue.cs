using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public sealed class CitySchoolDirtyQueue
    {
        private readonly LinkedList<long> _queue = new LinkedList<long>();
        private readonly Dictionary<long, LinkedListNode<long>> _nodes =
            new Dictionary<long, LinkedListNode<long>>();

        public int Count => _nodes.Count;

        public bool Contains(long pCityId)
        {
            return pCityId >= 0 && _nodes.ContainsKey(pCityId);
        }

        public bool Remove(long pCityId)
        {
            if (pCityId < 0 || !_nodes.TryGetValue(pCityId,
                    out LinkedListNode<long> node)) return false;
            _nodes.Remove(pCityId);
            _queue.Remove(node);
            return true;
        }

        public bool Mark(long pCityId)
        {
            if (pCityId < 0 || _nodes.ContainsKey(pCityId)) return false;
            _nodes[pCityId] = _queue.AddLast(pCityId);
            return true;
        }

        public long[] TakeBatch(int pBudget)
        {
            int budget = Math.Max(0, pBudget);
            var result = new List<long>(Math.Min(budget, _queue.Count));
            while (result.Count < budget && _queue.First != null)
            {
                long cityId = _queue.First.Value;
                _queue.RemoveFirst();
                _nodes.Remove(cityId);
                result.Add(cityId);
            }
            return result.ToArray();
        }

        public void Clear()
        {
            _queue.Clear();
            _nodes.Clear();
        }
    }
}
