using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.court
{
    internal static class DeJureNewCityAssignmentService
    {
        private const string RetryPrefix = "de_jure_new_city:";
        private const string WorldRepairRetryKey = "de_jure_world_repair";
        private const int MaxWorldRepairDeferrals = 600;
        private static readonly HashSet<long> RetryIds = new HashSet<long>();
        private static bool _worldRepairCompleted;
        private static int _worldRepairDeferrals;
        private static bool _worldRepairDeferralWarningLogged;

        internal static void OnCityFounded(City pCity)
        {
            if (pCity?.kingdom?.data != null)
                DeJureRegionMaintenanceService.MarkKingdomDirty(
                    pCity.kingdom.data.id, DeJureDirtyReason.CityRoster);
            if (TryAssign(pCity, allowRetry: true)) return;
        }

        internal static void ClearRuntime()
        {
            RetryIds.Clear();
            _worldRepairCompleted = false;
            _worldRepairDeferrals = 0;
            _worldRepairDeferralWarningLogged = false;
        }

        // Existing saves may contain cities created before the automatic
        // assignment hook was installed, or while the city had no kingdom
        // yet. Repair those gaps once after the world is fully available.
        internal static void RepairUnassignedCities()
        {
            if (_worldRepairCompleted || World.world?.cities == null ||
                World.world.cities.Count == 0 || !Config.game_loaded ||
                SmoothLoader.isLoading()) return;
            // Save loading can expose the city list before native kingdom
            // ownership has been restored. Do not consume the one-shot repair
            // in that intermediate state; retry from the deferred runtime lane.
            if (!IsWorldOwnershipReady())
            {
                QueueWorldRepairRetry();
                return;
            }
            _worldRepairCompleted = true;
            try
            {
                foreach (City city in World.world.cities.ToArray())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom?.data == null || city.kingdom.isRekt() ||
                        city.kingdom.isNeutral() ||
                        PeasantRebelBanditStrongholdService.IsStrongholdCity(city))
                        continue;
                    if (DeJureRegionStore.TryGetForCity(city.data.id, out _))
                        continue;
                    TryAssign(city, allowRetry: true);
                }
            }
            catch (Exception error)
            {
                _worldRepairCompleted = false;
                ModClass.LogWarning("De jure unassigned city repair failed: " +
                    error.Message);
            }
        }

        private static bool TryAssign(City pCity, bool allowRetry)
        {
            if (pCity?.data == null || pCity.data.id < 0L || pCity.isRekt())
                return false;
            if (PeasantRebelBanditStrongholdService.IsStrongholdCity(pCity))
                return false;
            Kingdom kingdom = pCity.kingdom;
            if (kingdom?.data == null)
            {
                QueueRetry(pCity, allowRetry);
                return false;
            }
            if (kingdom.isRekt() || kingdom.isNeutral())
                return false;
            // A retired/unassigned city is an intentional empty-map state,
            // not a missed automatic assignment. It can be assigned again
            // only through the explicit create/assign power.
            if (DeJureRegionStore.HasExplicitDeJureRemoval(pCity.data.id))
                return false;
            if (kingdom.capital == pCity)
            {
                DeJureRegionStore.EnsureKingdomCapitalSeat(kingdom);
                ApplyHistoricalCityName(pCity);
                return true;
            }
            if (DeJureRegionStore.TryGetForCity(pCity.data.id, out _)) return true;

            if (!PrepareNeighbours(pCity))
            {
                QueueRetry(pCity, allowRetry);
                return false;
            }

            long targetId = SelectRegion(pCity, kingdom);
            if (targetId < 0L)
            {
                if (!DeJureRegionStore.CreateState(pCity,
                        "city_created_isolated_region", out _, out _))
                {
                    QueueRetry(pCity, allowRetry);
                    return false;
                }
                RetryIds.Remove(pCity.data.id);
                ApplyHistoricalCityName(pCity);
                HierarchicalVassalMapModeService.MarkHierarchyDirty(kingdom);
                HierarchicalVassalMapModeService.RefreshAfterDeJureMutation();
                return true;
            }
            if (!DeJureRegionStore.AssignCityAutomatically(targetId, pCity,
                    "city_created_auto_assign", out string error))
            {
                if (error == "already_assigned") return false;
                if (error == "invalid_city")
                {
                    QueueRetry(pCity, allowRetry);
                    return false;
                }
                QueueRetry(pCity, allowRetry);
                return false;
            }

            RetryIds.Remove(pCity.data.id);
            ApplyHistoricalCityName(pCity);
            HierarchicalVassalMapModeService.MarkHierarchyDirty(kingdom);
            HierarchicalVassalMapModeService.RefreshAfterDeJureMutation();
            return true;
        }

        private static void ApplyHistoricalCityName(City pCity)
        {
            if (pCity?.data == null || pCity.data.custom_name ||
                !LineageService.IsXiaKingdom(pCity.kingdom) ||
                !XiaHistoricalDeJureRules.ShouldNameCity(
                    AWPerformanceSettings.EnableHistoricalDeJureCityNames,
                    !pCity.data.custom_name)) return;
            try
            {
                string currentName = ResolveChineseCityName(pCity);
                if (XiaHistoricalDeJureRules.IsHistoricalCityName(
                        XiaHistoricalDeJureCatalogService.Current,
                        currentName)) return;
                if (!DeJureRegionStore.TryGetForCity(pCity.data.id,
                        out DeJureRegion region)) return;
                var memberNames = (region.MemberCityIds ??
                    new List<long>()).Select(ResolveChineseCityName);
                XiaHistoricalDeJureProfile profile =
                    XiaHistoricalDeJureRules.SelectProfile(
                        XiaHistoricalDeJureCatalogService.Current, memberNames,
                        StableSelector(pCity.data.id));
                // 去重要看全图,不能只看本 region:历史名在整张地图上唯一,
                // 否则两个州各自挑走一个「雒阳」,玩家看到的就是两座同名城。
                string[] usedNames = CollectWorldCityNames();
                int selector = StableSelector(pCity.data.id);
                string stateId = string.IsNullOrWhiteSpace(
                        region.HistoricalStateId)
                    ? profile.StateId
                    : region.HistoricalStateId;
                string candidate = XiaHistoricalDeJureRules.
                    SelectHistoricalCityName(
                        XiaHistoricalDeJureCatalogService.Current, stateId,
                        usedNames, selector);
                // 库里的郡名自带「郡」字（「五原郡」），但这是要写进城市名的，
                // 城市名不带行政级别后缀 —— 否则地图上就出现一座叫「五原郡」
                // 的城。县名本身不带后缀，剥了也不受影响。
                candidate = StripAdministrativeSuffix(candidate);
                if (string.IsNullOrWhiteSpace(candidate) ||
                    string.Equals(candidate, ResolveChineseCityName(pCity),
                        StringComparison.Ordinal)) return;
                // Preserve the native/generated city identity before replacing
                // only the Chinese presentation slot with the historical
                // county label.
                AWLocalizedNameService.CaptureNative(pCity.data);
                pCity.data.set(AWNameDataKeys.ChineseName, candidate);
                AWLocalizedNameService.ProjectStored(pCity.data);
            }
            catch (Exception error)
            {
                ModClass.LogError("Historical city name assignment failed: " +
                    error.Message);
            }
        }

        /// <summary>
        ///     全世界所有活着的城市的中文名。历史名在整张地图上唯一,所以
        ///     去重必须按全图算 —— 只看本 region 会让不同州各自挑走同一个
        ///     「雒阳」。取不到城市列表时返回空集,调用方会因此拿到一个可能
        ///     重复的名字,但那远好过整条取名链直接断掉。
        /// </summary>
        /// <summary>
        ///     剥掉行政级别后缀。历史库里郡名是全称（「五原郡」「河东郡」），
        ///     但写进城市名的必须是裸名 —— 城市名不带级别后缀，界面另有地方
        ///     显示「郡」「州」这些层级。剥完为空则保留原值（比如某个县就叫
        ///     单字「郡」），宁可带后缀也不能给出空名。
        /// </summary>
        private static string StripAdministrativeSuffix(string pName)
        {
            string name = (pName ?? string.Empty).Trim();
            if (name.Length <= 1) return name;
            foreach (string suffix in new[] { "郡", "州", "府", "县" })
            {
                if (!name.EndsWith(suffix, StringComparison.Ordinal))
                    continue;
                string stripped = name
                    .Substring(0, name.Length - suffix.Length).Trim();
                return stripped.Length > 0 ? stripped : name;
            }
            return name;
        }

        private static string[] CollectWorldCityNames()
        {
            try
            {
                if (World.world?.cities == null)
                    return Array.Empty<string>();
                return World.world.cities.ToArray()
                    .Where(p => p?.data != null && !p.isRekt())
                    .Select(ResolveChineseCityName)
                    .Select(StripAdministrativeSuffix)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        private static string ResolveChineseCityName(long pCityId)
        {
            return ResolveChineseCityName(World.world?.cities?.get(pCityId));
        }

        private static string ResolveChineseCityName(City pCity)
        {
            if (pCity?.data == null) return string.Empty;
            pCity.data.get(AWNameDataKeys.ChineseName,
                out string chineseName, string.Empty);
            return string.IsNullOrWhiteSpace(chineseName)
                ? pCity.data.name ?? string.Empty
                : chineseName.Trim();
        }

        private static bool PrepareNeighbours(City pCity)
        {
            try
            {
                pCity.recalculateNeighbourZones();
                pCity.recalculateNeighbourCities();
                return pCity.getTile() != null;
            }
            catch { return false; }
        }

        private static long SelectRegion(City pCity, Kingdom pKingdom)
        {
            WorldTile cityTile = pCity.getTile();
            if (cityTile == null) return -1L;
            var adjacent = new HashSet<long>((pCity.neighbours_cities ??
                new HashSet<City>()).Where(p => p?.data != null &&
                !p.isRekt() && p.kingdom == pKingdom &&
                DeJureRegionStore.IsEligibleCityId(p.data.id)).Select(
                p => p.data.id));
            var facts = new List<DeJureNewCityRegionCandidate>();
            foreach (DeJureRegion region in DeJureRegionStore.ActiveRegions())
            {
                var allMembers = (region.MemberCityIds ?? new List<long>())
                    .Select(id => World.world?.cities?.get(id))
                    .Where(city => city?.data != null && !city.isRekt() &&
                        DeJureRegionStore.IsEligibleCityId(city.data.id))
                    .ToList();
                var members = allMembers
                    .Where(city =>
                        city.kingdom == pKingdom &&
                        DeJureRegionStore.IsEligibleCityId(city.data.id))
                    .ToList();
                if (members.Count == 0) continue;
                long nearest = members.Select(city => Distance(cityTile,
                    city.getTile())).DefaultIfEmpty(long.MaxValue).Min();
                City seat = members.FirstOrDefault(city =>
                    city.data.id == region.SeatCityId) ??
                    allMembers.FirstOrDefault(city =>
                        city.data.id == region.SeatCityId);
                bool adjacentSeat = seat?.data != null &&
                    adjacent.Contains(seat.data.id);
                int adjacentMemberCount = members.Count(city =>
                    adjacent.Contains(city.data.id));
                long seatDistance = Distance(cityTile, seat?.getTile());
                facts.Add(new DeJureNewCityRegionCandidate(region.RegionId,
                    adjacentSeat, adjacentMemberCount, nearest,
                    seatDistance,
                    allMembers.Count < RegionalGovernmentRules.
                        MaximumRegionCityCount));
            }
            return DeJureNewCityAssignmentRules.Select(facts,
                StableSelector(pCity.data.id));
        }

        private static int StableSelector(long pCityId)
        {
            unchecked
            {
                ulong value = (ulong)pCityId ^
                    ((ulong)(uint)MapBox.current_world_seed_id << 32);
                value ^= value >> 33;
                value *= 0xff51afd7ed558ccdUL;
                value ^= value >> 33;
                return (int)(value ^ (value >> 32));
            }
        }

        private static long Distance(WorldTile pFirst, WorldTile pSecond)
        {
            if (pFirst?.pos == null || pSecond?.pos == null)
                return long.MaxValue;
            try
            {
                double dx = pFirst.pos.x - pSecond.pos.x;
                double dy = pFirst.pos.y - pSecond.pos.y;
                double value = dx * dx + dy * dy;
                return value >= long.MaxValue ? long.MaxValue : (long)value;
            }
            catch { return long.MaxValue; }
        }

        private static void QueueRetry(City pCity, bool allowRetry)
        {
            if (!allowRetry || pCity?.data == null ||
                !RetryIds.Add(pCity.data.id)) return;
            long cityId = pCity.data.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                RetryPrefix + cityId, DeferredWorkClass.Runtime,
                () =>
                {
                    RetryIds.Remove(cityId);
                    // The first retry can still race city/zone initialization;
                    // keep the same bounded coalesced retry contract until
                    // the native city graph is ready.
                    TryAssign(World.world?.cities?.get(cityId), true);
                });
        }

        private static bool IsWorldOwnershipReady()
        {
            try
            {
                foreach (City city in World.world.cities)
                {
                    if (city?.data == null || city.isRekt()) continue;
                    if (city.kingdom?.data == null) return false;
                }
                return true;
            }
            catch { return false; }
        }

        private static void QueueWorldRepairRetry()
        {
            if (_worldRepairDeferrals++ >= MaxWorldRepairDeferrals)
            {
                if (!_worldRepairDeferralWarningLogged)
                {
                    _worldRepairDeferralWarningLogged = true;
                    ModClass.LogWarning(
                        "De jure world repair deferred too long; ownership " +
                        "never became ready.");
                }
                return;
            }
            DeferredRuntimeWorkService.EnqueueCoalesced(
                WorldRepairRetryKey, DeferredWorkClass.Runtime,
                RepairUnassignedCities);
        }
    }
}
