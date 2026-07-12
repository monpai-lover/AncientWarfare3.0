using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public sealed class CitySchoolDirtyQueue
    {
        private readonly Queue<long> _queue = new Queue<long>();
        private readonly HashSet<long> _queued = new HashSet<long>();

        public int Count => _queued.Count;

        public bool Contains(long pCityId)
        {
            return pCityId >= 0 && _queued.Contains(pCityId);
        }

        public bool Remove(long pCityId)
        {
            return pCityId >= 0 && _queued.Remove(pCityId);
        }

        public bool Mark(long pCityId)
        {
            if (pCityId < 0 || !_queued.Add(pCityId)) return false;
            _queue.Enqueue(pCityId);
            return true;
        }

        public long[] TakeBatch(int pBudget)
        {
            int budget = Math.Max(0, pBudget);
            var result = new List<long>(Math.Min(budget, _queue.Count));
            while (result.Count < budget && _queue.Count > 0)
            {
                long cityId = _queue.Dequeue();
                if (_queued.Remove(cityId)) result.Add(cityId);
            }
            return result.ToArray();
        }

        public void Clear()
        {
            _queue.Clear();
            _queued.Clear();
        }
    }
}
