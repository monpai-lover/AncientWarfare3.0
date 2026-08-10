using System;
using System.Collections.Generic;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CityBureauAnnualWorkService
    {
        private const int CitiesPerSlice = 2;
        private const int MaximumWriteAttempts = 3;

        private sealed class PendingWork
        {
            internal int Year;
            internal float CourtEfficiency;
            internal IEnumerator<City> Cities;
            internal readonly HashSet<long> CompletedCityIds =
                new HashSet<long>();
            internal long RetryCityId = -1L;
            internal int RetryAttempts;
        }

        private static readonly Dictionary<long, PendingWork> Pending =
            new Dictionary<long, PendingWork>();

        internal static void Schedule(Kingdom pKingdom,
            float pCourtEfficiency)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                !CourtService.HasOfficialCourt(pKingdom)) return;

            var work = new PendingWork
            {
                Year = Date.getCurrentYear(),
                CourtEfficiency = pCourtEfficiency
            };
            try { work.Cities = pKingdom.getCities()?.GetEnumerator(); }
            catch { return; }
            if (work.Cities == null) return;
            if (Pending.TryGetValue(pKingdom.id, out PendingWork previous))
                Dispose(previous);
            Pending[pKingdom.id] = work;
            Enqueue(pKingdom.id);
        }

        internal static void ClearRuntime()
        {
            foreach (PendingWork work in Pending.Values) Dispose(work);
            Pending.Clear();
        }

        private static void Enqueue(long pKingdomId)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "annual-city-bureau:" + pKingdomId,
                DeferredWorkClass.Persistent,
                () => ProcessSlice(pKingdomId));
        }

        private static void ProcessSlice(long pKingdomId)
        {
            if (!Pending.TryGetValue(pKingdomId, out PendingWork work))
                return;
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data == null || kingdom.isRekt() ||
                !CourtService.HasOfficialCourt(kingdom))
            {
                Remove(pKingdomId);
                return;
            }

            if (work.RetryCityId >= 0L)
            {
                City retryCity = ResolveCity(work.RetryCityId);
                if (retryCity?.data == null || retryCity.isRekt() ||
                    retryCity.kingdom != kingdom ||
                    ProcessCity(kingdom, retryCity, work.CourtEfficiency,
                        work.Year))
                {
                    if (retryCity?.data != null)
                        work.CompletedCityIds.Add(retryCity.data.id);
                    ClearRetry(work);
                }
                else if (++work.RetryAttempts < MaximumWriteAttempts)
                {
                    Enqueue(pKingdomId);
                    return;
                }
                else
                {
                    work.CompletedCityIds.Add(work.RetryCityId);
                    ModClass.LogWarning("City bureau annual write exhausted " +
                                        "for city " + work.RetryCityId);
                    ClearRetry(work);
                }
            }

            int inspected = 0;
            while (inspected < CitiesPerSlice)
            {
                City city;
                try
                {
                    if (!work.Cities.MoveNext())
                    {
                        Remove(pKingdomId);
                        return;
                    }
                    city = work.Cities.Current;
                }
                catch
                {
                    if (!TryRestart(work, kingdom))
                    {
                        Remove(pKingdomId);
                        return;
                    }
                    Enqueue(pKingdomId);
                    return;
                }

                inspected++;
                if (city?.data == null || city.isRekt() ||
                    city.kingdom != kingdom ||
                    work.CompletedCityIds.Contains(city.data.id)) continue;
                if (!ProcessCity(kingdom, city, work.CourtEfficiency,
                        work.Year))
                {
                    work.RetryCityId = city.data.id;
                    work.RetryAttempts = 1;
                    Enqueue(pKingdomId);
                    return;
                }
                work.CompletedCityIds.Add(city.data.id);
            }
            Enqueue(pKingdomId);
        }

        private static bool ProcessCity(Kingdom pKingdom, City pCity,
            float pCourtEfficiency, int pYear)
        {
            int slots = CourtRules.CityOfficeSlots(
                SafeCityPopulation(pCity), SafeZoneCount(pCity),
                pKingdom.capital == pCity);
            int filled = CourtBureauRules.FilledSlots(slots,
                pCourtEfficiency);
            CitySchoolSnapshot schoolSnapshot =
                CitySchoolSnapshotService.GetSnapshot(pCity);
            string localSchool = schoolSnapshot?.DominantSchool ??
                                 CourtSchoolId.None;
            if (schoolSnapshot == null)
                CitySchoolSnapshotService.MarkDirty(pCity);
            float efficiency = CourtBureauRules.BureauEfficiency(slots,
                filled);

            pCity.data.get(LineageKeys.CITY_BUREAU_STATE_INITIALIZED,
                out bool initialized, false);
            pCity.data.get(LineageKeys.CITY_BUREAU_OFFICE_SLOTS,
                out int previousSlots, -1);
            pCity.data.get(LineageKeys.CITY_BUREAU_LOCAL_SCHOOL,
                out string previousSchool, string.Empty);

            string table = CityBureauStateTableItem.GetTableName();
            var updates = new[]
            {
                new HistoricalSqlColumn("KINGDOM_ID", pKingdom.id),
                new HistoricalSqlColumn("CITY_NAME", pCity.data.name ?? ""),
                new HistoricalSqlColumn("OFFICE_SLOTS", slots),
                new HistoricalSqlColumn("LOCAL_SCHOOL", localSchool ?? ""),
                new HistoricalSqlColumn("BUREAU_EFFICIENCY",
                    (double)efficiency),
                new HistoricalSqlColumn("OFFICER_ACTOR_IDS", ""),
                new HistoricalSqlColumn("LAST_REFRESH_YEAR", pYear),
                new HistoricalSqlColumn("UPDATED_TIME",
                    LineageService.CurTime())
            };
            var inserts = new HistoricalSqlColumn[updates.Length + 1];
            inserts[0] = new HistoricalSqlColumn("CITY_ID", pCity.data.id);
            Array.Copy(updates, 0, inserts, 1, updates.Length);
            if (!HistoricalWriteService.TryUpsertState(
                    "city-bureau:" + pCity.data.id, table,
                    new[]
                    {
                        new HistoricalSqlColumn("CITY_ID", pCity.data.id)
                    }, updates, inserts, pOnCommitted: null,
                    out _, out _)) return false;

            pCity.data.set(LineageKeys.CITY_BUREAU_STATE_INITIALIZED, true);
            pCity.data.set(LineageKeys.CITY_BUREAU_OFFICE_SLOTS, slots);
            pCity.data.set(LineageKeys.CITY_BUREAU_LOCAL_SCHOOL,
                localSchool ?? "");
            if ((!initialized || previousSlots != slots ||
                 !string.Equals(previousSchool ?? "", localSchool ?? "",
                     StringComparison.Ordinal)) &&
                CourtBureauRules.ShouldRecordCityBureauChange(previousSlots,
                    slots, previousSchool, localSchool))
                ChronicleEvents.OnCourtCityBureau(pKingdom,
                    pCity.data.name ?? "", localSchool ?? "");
            return true;
        }

        private static bool TryRestart(PendingWork pWork, Kingdom pKingdom)
        {
            DisposeEnumerator(pWork);
            try
            {
                pWork.Cities = pKingdom.getCities()?.GetEnumerator();
                return pWork.Cities != null;
            }
            catch { return false; }
        }

        private static void ClearRetry(PendingWork pWork)
        {
            pWork.RetryCityId = -1L;
            pWork.RetryAttempts = 0;
        }

        private static void Remove(long pKingdomId)
        {
            if (!Pending.TryGetValue(pKingdomId, out PendingWork work))
                return;
            Pending.Remove(pKingdomId);
            Dispose(work);
        }

        private static void Dispose(PendingWork pWork)
        {
            DisposeEnumerator(pWork);
        }

        private static void DisposeEnumerator(PendingWork pWork)
        {
            if (pWork?.Cities == null) return;
            try { pWork.Cities.Dispose(); }
            catch { }
            pWork.Cities = null;
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static City ResolveCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static int SafeCityPopulation(City pCity)
        {
            try { return Math.Max(0, pCity?.getPopulationPeople() ?? 0); }
            catch { return 0; }
        }

        private static int SafeZoneCount(City pCity)
        {
            try { return Math.Max(0, pCity?.countZones() ?? 0); }
            catch { return 0; }
        }
    }
}
