using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    internal sealed class ShiBranchRuntimeMetadata
    {
        internal long ShiId { get; set; } = -1L;
        internal long CreatedOrder { get; set; } = long.MaxValue;
        internal string DisplayName { get; set; } = "";
        internal bool IsValid { get; set; }
    }

    internal static class ShiBranchRuntimeMetadataCache
    {
        private static readonly Dictionary<long, ShiBranchRuntimeMetadata> Cache =
            new Dictionary<long, ShiBranchRuntimeMetadata>();
        private static readonly CitySchoolDirtyQueue Pending =
            new CitySchoolDirtyQueue();
        private static readonly Dictionary<long, HashSet<long>> DependentCities =
            new Dictionary<long, HashSet<long>>();

        internal static bool TryGet(long pShiId, long pCityId,
            out ShiBranchRuntimeMetadata pMetadata)
        {
            pMetadata = null;
            if (pShiId < 0L) return false;
            RegisterDependency(pShiId, pCityId);
            if (Cache.TryGetValue(pShiId, out pMetadata)) return true;
            Pending.Mark(pShiId);
            return false;
        }

        internal static void Invalidate(long pShiId)
        {
            if (pShiId < 0L) return;
            Cache.Remove(pShiId);
            Pending.Mark(pShiId);
            MarkDependentCitiesDirty(pShiId);
        }

        internal static int ProcessPending(int pBudget)
        {
            int budget = Math.Max(0, pBudget);
            int attempts = 0;
            int loaded = 0;
            while (attempts < budget && Pending.TryDequeue(out long shiId))
            {
                attempts++;
                try
                {
                    ShiBranchInfo branch = LineageQuery.GetShiBranchInfo(shiId);
                    Cache[shiId] = Build(shiId, branch);
                    MarkDependentCitiesDirty(shiId);
                    loaded++;
                }
                catch
                {
                    Pending.Mark(shiId);
                }
            }
            return loaded;
        }

        internal static void Clear()
        {
            Cache.Clear();
            Pending.Clear();
            DependentCities.Clear();
        }

        private static ShiBranchRuntimeMetadata Build(long pShiId,
            ShiBranchInfo pBranch)
        {
            if (pBranch == null)
                return new ShiBranchRuntimeMetadata { ShiId = pShiId };
            string origin = pBranch.origin_city_name ??
                            pBranch.origin_city_chinese_name;
            string displayName = ShiBranchRules.BuildDisplayName(origin,
                pBranch.clan_name, pBranch.source_type, pBranch.state_name);
            return new ShiBranchRuntimeMetadata
            {
                ShiId = pShiId,
                CreatedOrder = CreatedOrder(pBranch.created_time),
                DisplayName = displayName,
                IsValid = !string.IsNullOrWhiteSpace(displayName)
            };
        }

        private static void RegisterDependency(long pShiId, long pCityId)
        {
            if (pCityId < 0L) return;
            if (!DependentCities.TryGetValue(pShiId,
                    out HashSet<long> cities))
            {
                cities = new HashSet<long>();
                DependentCities[pShiId] = cities;
            }
            cities.Add(pCityId);
        }

        private static void MarkDependentCitiesDirty(long pShiId)
        {
            if (!DependentCities.TryGetValue(pShiId,
                    out HashSet<long> cities)) return;
            foreach (long cityId in cities)
                CityShiInfluenceSnapshotService.MarkDirtyById(cityId);
        }

        private static long CreatedOrder(double pCreatedTime)
        {
            if (double.IsNaN(pCreatedTime) || double.IsInfinity(pCreatedTime))
                return long.MaxValue;
            double scaled = Math.Floor(Math.Max(0d, pCreatedTime) * 1000d);
            return scaled >= long.MaxValue ? long.MaxValue : (long)scaled;
        }
    }
}
