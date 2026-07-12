using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public sealed class CitySchoolDirtyQueue
    {
        private readonly Queue<long> _queue = new Queue<long>();
        private readonly HashSet<long> _queued = new HashSet<long>();

        public int Count => _queued.Count;

        public bool Mark(long pCityId)
        {
            if (pCityId < 0 || !_queued.Add(pCityId)) return false;
            _queue.Enqueue(pCityId);
            return true;
        }

        public long[] TakeBatch(int pBudget)
        {
            int count = Math.Min(Math.Max(0, pBudget), _queue.Count);
            var result = new long[count];
            for (int i = 0; i < count; i++)
            {
                long cityId = _queue.Dequeue();
                _queued.Remove(cityId);
                result[i] = cityId;
            }
            return result;
        }

        public void Clear()
        {
            _queue.Clear();
            _queued.Clear();
        }
    }
}
