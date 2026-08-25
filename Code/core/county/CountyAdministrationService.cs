using System;
using System.Collections.Generic;
using System.Linq;

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
            IReadOnlyList<CountyRecord> existing = CountyAdministrationStore.ForCity(cityId);
            var assigned = new HashSet<long>();
            foreach (CountyRecord county in existing)
            {
                county.ZoneIds = county.ZoneIds.Where(valid.Contains).ToList();
                assigned.UnionWith(county.ZoneIds);
                CountyAdministrationStore.Upsert(county);
            }
            long[] missing = valid.Where(p => !assigned.Contains(p)).OrderBy(p => p).ToArray();
            if (missing.Length == 0 && existing.Count > 0) return;
            var groups = new List<IReadOnlyList<long>>();
            int missingOffset = 0;
            foreach (CountyRecord county in existing.OrderBy(p => p.Ordinal))
            {
                int capacity = CountyZonePartitionRules.MaximumZonesPerCounty -
                    county.ZoneIds.Count;
                if (capacity <= 0) continue;
                int take = Math.Min(capacity, missing.Length - missingOffset);
                if (take <= 0) break;
                county.ZoneIds.AddRange(missing.Skip(missingOffset).Take(take));
                missingOffset += take;
                CountyAdministrationStore.Upsert(county);
            }
            if (missingOffset < missing.Length)
                groups.AddRange(CountyZonePartitionRules.Partition(
                    missing.Skip(missingOffset), pAdjacency: null));
            int ordinal = existing.Count == 0 ? 0 : existing.Max(p => p.Ordinal) + 1;
            foreach (IReadOnlyList<long> group in groups)
            {
                CountyAdministrationStore.Upsert(new CountyRecord
                {
                    CityId = cityId, Ordinal = ordinal,
                    Name = CountyNameRules.Build(pCity.data.name, ordinal,
                        pHistoricalName: null),
                    ZoneIds = group.ToList(), Active = true,
                    CreatedYear = World.world?.year ?? -1,
                    LastRepairedYear = World.world?.year ?? -1
                });
                ordinal++;
            }
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
