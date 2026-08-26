using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.county
{
    internal static class CountyAdministrationService
    {
        internal static void ReconcileCity(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt() || pCity.zones == null) return;
            long cityId = pCity.data.id;
            HashSet<long> valid = new HashSet<long>(pCity.zones
                .Where(p => p != null).Select(p => (long)p.id));
            IReadOnlyList<CountyRecord> existing =
                CountyAdministrationStore.ForCityIncludingInactive(cityId);
            if (valid.Count == 0)
            {
                foreach (CountyRecord county in existing.Where(p => p != null &&
                             p.Active))
                {
                    county.ZoneIds = new List<long>();
                    county.Active = false;
                    CountyAdministrationStore.Upsert(county);
                }
                return;
            }
            var adjacency = pCity.zones.Where(p => p != null).ToDictionary(
                p => (long)p.id,
                p => (IReadOnlyList<long>)(p.neighbours ?? Array.Empty<TileZone>())
                    .Where(n => n != null && valid.Contains(n.id))
                    .Select(n => (long)n.id).OrderBy(n => n).ToArray());
            IReadOnlyList<IReadOnlyList<long>> groups =
                CountyZonePartitionRules.Partition(valid, adjacency);
            var used = new HashSet<long>();
            string historicalCityName = pCity.data.name ?? string.Empty;
            pCity.data.get(AWNameDataKeys.ChineseName,
                out string storedChineseName, string.Empty);
            if (!string.IsNullOrWhiteSpace(storedChineseName))
                historicalCityName = storedChineseName.Trim();
            XiaHistoricalDeJureCatalog catalog =
                XiaHistoricalDeJureCatalogService.Current;
            DeJureRegion region = null;
            if (DeJureRegionStore.TryGetForCity(cityId,
                    out DeJureRegion resolvedRegion))
                region = resolvedRegion;
            var usedNames = CollectRegionCountyNames(region, cityId);
            var usedCommanderyIds = region?.SeatCityId == cityId
                ? new HashSet<string>(StringComparer.Ordinal)
                : CollectRegionCommanderyIds(region, cityId);
            if (region != null && region.SeatCityId != cityId &&
                !string.IsNullOrWhiteSpace(region.HistoricalCommanderyId))
                usedCommanderyIds.Add(region.HistoricalCommanderyId);
            string persistedCommanderyId = existing.Where(p => p != null &&
                    !string.IsNullOrWhiteSpace(p.HistoricalCommanderyId))
                .OrderBy(p => p.Ordinal)
                .Select(p => p.HistoricalCommanderyId).FirstOrDefault() ??
                string.Empty;
            string stateId = region?.HistoricalStateId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(stateId) &&
                !string.IsNullOrWhiteSpace(region?.HistoricalCommanderyId))
                stateId = catalog.States.FirstOrDefault(state => state != null &&
                    state.Commanderies.Any(item => string.Equals(item.Id,
                        region.HistoricalCommanderyId,
                        StringComparison.Ordinal)))?.Id ?? string.Empty;
            XiaHistoricalCommanderyDefinition commandery =
                XiaHistoricalDeJureRules.SelectCityCommandery(catalog,
                    stateId, persistedCommanderyId,
                    region?.SeatCityId == cityId
                        ? region.HistoricalCommanderyId
                        : string.Empty,
                    historicalCityName, usedCommanderyIds,
                    unchecked((int)pCity.data.id));
            if (commandery == null)
            {
                XiaHistoricalDeJureProfile profile =
                    XiaHistoricalDeJureRules.SelectProfile(catalog,
                        new[] { historicalCityName },
                        unchecked((int)pCity.data.id));
                commandery = catalog.GetCommandery(profile.CommanderyId);
            }
            for (int ordinal = 0; ordinal < groups.Count; ordinal++)
            {
                IReadOnlyList<long> group = groups[ordinal];
                HashSet<long> groupSet = new HashSet<long>(group);
                CountyRecord match = existing.Where(p => p != null && p.Active &&
                        !used.Contains(p.CountyId))
                    .Select(p => new
                    {
                        Record = p,
                        Overlap = (p.ZoneIds ?? new List<long>())
                            .Count(groupSet.Contains)
                    })
                    .OrderByDescending(p => p.Overlap)
                    .ThenBy(p => p.Record.Ordinal)
                    .Select(p => p.Record)
                    .FirstOrDefault();
                if (match == null || (match.ZoneIds ?? new List<long>())
                        .Count(groupSet.Contains) == 0)
                    match = new CountyRecord
                    {
                        CityId = cityId,
                        CreatedYear = Date.getCurrentYear()
                    };
                else
                    used.Add(match.CountyId);

                match.CityId = cityId;
                match.RegionId = region?.RegionId ?? -1L;
                match.HistoricalCommanderyId = commandery?.Id ??
                    string.Empty;
                match.Ordinal = ordinal;
                match.ZoneIds = group.ToList();
                match.Active = true;
                if (match.ManualName && !string.IsNullOrWhiteSpace(match.Name))
                    usedNames.Add(match.Name);
                else
                {
                    string historicalName = SelectHistoricalCountyName(
                        catalog, stateId, commandery, historicalCityName,
                        usedNames, ordinal,
                        unchecked((int)(cityId * 397L + ordinal)));
                    match.Name = string.IsNullOrWhiteSpace(historicalName)
                        ? CountyNameRules.Build(pCity.data.name, ordinal,
                            pHistoricalName: null)
                        : historicalName;
                }
                if (!string.IsNullOrWhiteSpace(match.Name))
                    usedNames.Add(match.Name);
                match.LastRepairedYear = Date.getCurrentYear();
                CountyAdministrationStore.Upsert(match);
                if (match.CountyId >= 0L) used.Add(match.CountyId);
            }

            // Old saves may contain more records than the current partition.
            // Retire those records instead of exposing duplicate county offices;
            // their IDs and history remain in the sidecar for compatibility.
            foreach (CountyRecord stale in existing.Where(p => p != null &&
                         p.Active && !used.Contains(p.CountyId)))
            {
                stale.ZoneIds = new List<long>();
                stale.Active = false;
                CountyAdministrationStore.Upsert(stale);
            }
        }

        private static HashSet<string> CollectRegionCountyNames(
            DeJureRegion pRegion, long pExcludedCityId)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (long memberCityId in pRegion?.MemberCityIds ??
                     new List<long>())
            {
                if (memberCityId == pExcludedCityId) continue;
                foreach (CountyRecord county in CountyAdministrationStore.
                             ForCity(memberCityId))
                    if (county != null && !string.IsNullOrWhiteSpace(
                            county.Name))
                        result.Add(county.Name);
            }
            return result;
        }

        private static HashSet<string> CollectRegionCommanderyIds(
            DeJureRegion pRegion, long pExcludedCityId)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (long memberCityId in pRegion?.MemberCityIds ??
                     new List<long>())
            {
                if (memberCityId == pExcludedCityId) continue;
                CountyRecord county = CountyAdministrationStore.
                    ForCity(memberCityId).FirstOrDefault(item => item != null &&
                        !string.IsNullOrWhiteSpace(
                            item.HistoricalCommanderyId));
                if (county != null)
                    result.Add(county.HistoricalCommanderyId);
            }
            return result;
        }

        private static string SelectHistoricalCountyName(
            XiaHistoricalDeJureCatalog pCatalog,
            string pStateId,
            XiaHistoricalCommanderyDefinition pCommandery,
            string pCityName, ISet<string> pUsedNames, int pOrdinal,
            int pStableSelector)
        {
            string city = TrimCountySuffix(pCityName);
            var used = new HashSet<string>(StringComparer.Ordinal) { city };
            if (pUsedNames != null)
                foreach (string name in pUsedNames)
                    used.Add(TrimCountySuffix(name));
            string selected = string.Empty;
            if (pCommandery?.CityNames != null)
            {
                string[] available = pCommandery.CityNames.Where(name =>
                        !used.Contains(TrimCountySuffix(name)))
                    .OrderBy(name => name, StringComparer.Ordinal).ToArray();
                if (available.Length > 0)
                    selected = available[Math.Min(Math.Max(0, pOrdinal),
                        available.Length - 1)];
            }
            if (string.IsNullOrWhiteSpace(selected))
                selected = XiaHistoricalDeJureRules.
                    SelectUnusedCountyFromState(pCatalog, pStateId, used,
                        pStableSelector);
            if (string.IsNullOrWhiteSpace(selected))
                selected = XiaHistoricalDeJureRules.
                    SelectUnusedCountyFromCatalog(pCatalog, used,
                        pStableSelector);
            if (string.IsNullOrWhiteSpace(selected)) return string.Empty;
            return selected.EndsWith("县", StringComparison.Ordinal)
                ? selected : selected + "县";
        }

        private static string TrimCountySuffix(string pName)
        {
            string name = (pName ?? string.Empty).Trim();
            return name.EndsWith("县", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - 1)
                : name;
        }

        internal static CountyRecord FindForZone(long pZoneId)
        {
            return CountyAdministrationStore.FindByZone(pZoneId);
        }

        internal static IReadOnlyList<CountyRecord> CountiesForCity(long pCityId)
        {
            return CountyAdministrationStore.ForCity(pCityId);
        }
    }
}
