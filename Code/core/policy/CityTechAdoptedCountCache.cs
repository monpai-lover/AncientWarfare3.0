using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    internal sealed class CityTechAdoptedCountCache
    {
        private readonly Dictionary<long, int> _counts =
            new Dictionary<long, int>();
        private readonly int _maximumCount;

        internal CityTechAdoptedCountCache(int pMaximumCount = int.MaxValue)
        {
            _maximumCount = Math.Max(0, pMaximumCount);
        }

        internal bool Ready { get; private set; }

        internal void Clear()
        {
            _counts.Clear();
            Ready = false;
        }

        internal void BeginRebuild()
        {
            Clear();
        }

        internal void AddRebuiltCount(long pCityId, int pCount)
        {
            if (pCityId < 0) return;
            _counts[pCityId] = Clamp(pCount);
        }

        internal void CompleteRebuild()
        {
            Ready = true;
        }

        internal int Read(long pCityId)
        {
            return _counts.TryGetValue(pCityId, out int count) ? count : 0;
        }

        internal void RecordAdoption(long pCityId, int pMaximumCount)
        {
            if (pCityId < 0) return;
            int maximum = Math.Min(_maximumCount, Math.Max(0, pMaximumCount));
            _counts[pCityId] = Math.Min(maximum, Read(pCityId) + 1);
        }

        private int Clamp(int pCount)
        {
            return Math.Min(_maximumCount, Math.Max(0, pCount));
        }
    }
}
