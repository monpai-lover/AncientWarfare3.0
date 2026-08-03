using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    /// <summary>
    /// Bounds retries after a civil appointment fails to commit.  Without
    /// this, the same candidate is selected on every CityBehCheckLeader tick.
    /// </summary>
    internal static class CityLeaderCandidateRetryService
    {
        private const int RetryDays = 30;
        private const int MaxTrackedCities = 512;
        private static readonly Dictionary<long, Dictionary<long, long>>
            Failures = new Dictionary<long, Dictionary<long, long>>();

        public static bool IsSuppressed(City pCity, Actor pActor)
        {
            if (pCity?.data == null || pActor?.data == null) return false;
            long cityId = pCity.data.id;
            long actorId = pActor.data.id;
            if (!Failures.TryGetValue(cityId,
                    out Dictionary<long, long> cityFailures) ||
                !cityFailures.TryGetValue(actorId, out long failedDay))
                return false;
            long currentDay = CurrentWorldDay();
            if (CityLeaderCandidateRules.ShouldRetry(currentDay, failedDay,
                    RetryDays))
            {
                cityFailures.Remove(actorId);
                if (cityFailures.Count == 0) Failures.Remove(cityId);
                return false;
            }
            return true;
        }

        public static void RecordFailure(City pCity, Actor pActor)
        {
            if (pCity?.data == null || pActor?.data == null) return;
            if (!Failures.TryGetValue(pCity.data.id,
                    out Dictionary<long, long> cityFailures))
            {
                if (Failures.Count >= MaxTrackedCities) Failures.Clear();
                cityFailures = new Dictionary<long, long>();
                Failures[pCity.data.id] = cityFailures;
            }
            cityFailures[pActor.data.id] = CurrentWorldDay();
        }

        public static void Clear(City pCity, Actor pActor)
        {
            if (pCity?.data == null || pActor?.data == null ||
                !Failures.TryGetValue(pCity.data.id,
                    out Dictionary<long, long> cityFailures)) return;
            cityFailures.Remove(pActor.data.id);
            if (cityFailures.Count == 0) Failures.Remove(pCity.data.id);
        }

        public static void Reset()
        {
            Failures.Clear();
        }

        private static long CurrentWorldDay()
        {
            try
            {
                double time = Math.Max(0d,
                    World.world?.getCurWorldTime() ?? 0d);
                return (long)Math.Floor(time * 6d);
            }
            catch { return 0L; }
        }
    }
}
