using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class KingdomMilitaryReadinessIndex
    {
        private sealed class CityEntry
        {
            public int Generation;
            public bool PositiveCore;
            public bool Ready;
        }

        private readonly Dictionary<long, CityEntry> _cities =
            new Dictionary<long, CityEntry>();

        public int Generation { get; private set; }
        public int ObservedCityCount { get; private set; } = -1;
        public int PositiveCoreCities { get; private set; }
        public int UnreadyCoreCities { get; private set; }
        public bool ScanComplete { get; private set; }

        public void StartGeneration(int pObservedCityCount)
        {
            Generation = Generation == int.MaxValue ? 1 : Generation + 1;
            ObservedCityCount = Math.Max(0, pObservedCityCount);
            PositiveCoreCities = 0;
            UnreadyCoreCities = 0;
            ScanComplete = false;
        }

        public void Observe(long pCityId, bool pPositiveCore, bool pReady)
        {
            if (pCityId < 0) return;
            if (!_cities.TryGetValue(pCityId, out CityEntry entry))
            {
                entry = new CityEntry();
                _cities[pCityId] = entry;
            }
            if (entry.Generation == Generation) RemoveContribution(entry);
            entry.Generation = Generation;
            entry.PositiveCore = pPositiveCore;
            entry.Ready = pReady;
            AddContribution(entry);
        }

        public bool Remove(long pCityId)
        {
            if (!_cities.TryGetValue(pCityId, out CityEntry entry)) return false;
            if (entry.Generation == Generation) RemoveContribution(entry);
            _cities.Remove(pCityId);
            return true;
        }

        public void MarkComplete()
        {
            ScanComplete = true;
        }

        public void MarkIncomplete()
        {
            ScanComplete = false;
        }

        public bool IsReady(int pCurrentCityCount, bool pTemporaryLeviesActive)
        {
            return KingdomMilitaryReadinessRules.IsReady(
                ScanComplete,
                ObservedCityCount,
                pCurrentCityCount,
                PositiveCoreCities,
                UnreadyCoreCities,
                pTemporaryLeviesActive);
        }

        private void AddContribution(CityEntry pEntry)
        {
            if (!pEntry.PositiveCore) return;
            PositiveCoreCities++;
            if (!pEntry.Ready) UnreadyCoreCities++;
        }

        private void RemoveContribution(CityEntry pEntry)
        {
            if (!pEntry.PositiveCore) return;
            PositiveCoreCities = Math.Max(0, PositiveCoreCities - 1);
            if (!pEntry.Ready) UnreadyCoreCities = Math.Max(0, UnreadyCoreCities - 1);
        }
    }
}
