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
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            string historicalCityName = pCity.data.name ?? string.Empty;
            pCity.data.get(AWNameDataKeys.ChineseName,
                out string storedChineseName, string.Empty);
            if (!string.IsNullOrWhiteSpace(storedChineseName))
                historicalCityName = storedChineseName.Trim();
            XiaHistoricalDeJureCatalog catalog =
                XiaHistoricalDeJureCatalogService.Current;
            XiaHistoricalCommanderyDefinition commandery = null;
            if (DeJureRegionStore.TryGetForCity(cityId,
                    out DeJureRegion region) &&
                !string.IsNullOrWhiteSpace(region.HistoricalCommanderyId))
                commandery = catalog.GetCommandery(
                    region.HistoricalCommanderyId);
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
                match.Ordinal = ordinal;
                match.ZoneIds = group.ToList();
                match.Active = true;
                if (match.ManualName && !string.IsNullOrWhiteSpace(match.Name))
                    usedNames.Add(match.Name);
                else
                {
                    string historicalName = SelectHistoricalCountyName(
                        catalog, commandery, historicalCityName, usedNames,
                        ordinal, unchecked((int)(cityId * 397L + ordinal)));
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

        private static string SelectHistoricalCountyName(
            XiaHistoricalDeJureCatalog pCatalog,
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
