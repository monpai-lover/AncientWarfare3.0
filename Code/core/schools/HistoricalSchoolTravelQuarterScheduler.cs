using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolTravelQuarterScheduler
    {
        public static int Process<TState, TPrepared, TCity>(
            Action pCompleteDueVoyages,
            Func<IReadOnlyList<TState>> pLoadBucketStates,
            int pCurrentOffset,
            int pMaxStates,
            Func<TState, TPrepared> pPrepareDestination,
            Func<IReadOnlyList<TCity>> pBuildIndexedCities,
            Action<TPrepared, IReadOnlyList<TCity>> pChooseDestination)
            where TPrepared : class
        {
            pCompleteDueVoyages();
            IReadOnlyList<TState> bucketStates = pLoadBucketStates() ?? Array.Empty<TState>();
            if (bucketStates.Count == 0) return pCurrentOffset;

            int start = ((pCurrentOffset % bucketStates.Count) + bucketStates.Count) %
                        bucketStates.Count;
            int count = Math.Min(Math.Max(0, pMaxStates), bucketStates.Count);
            IReadOnlyList<TCity> indexedCities = null;
            for (int index = 0; index < count; index++)
            {
                TState state = bucketStates[(start + index) % bucketStates.Count];
                TPrepared prepared = pPrepareDestination(state);
                if (prepared == null) continue;
                if (indexedCities == null)
                    indexedCities = pBuildIndexedCities() ?? Array.Empty<TCity>();
                pChooseDestination(prepared, indexedCities);
            }
            return (start + count) % bucketStates.Count;
        }
    }
}
